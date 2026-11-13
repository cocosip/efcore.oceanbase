using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using OceanBase.EntityFrameworkCore.Internal;
using OceanBase.EntityFrameworkCore.Metadata;

namespace OceanBase.EntityFrameworkCore.Migrations
{
    internal sealed class OceanBaseMigrationsSqlGenerator(MigrationsSqlGeneratorDependencies dependencies, IDbContextOptions options)
        : MigrationsSqlGenerator(dependencies)
    {
        private readonly OceanBaseOptionsExtension _options = options.FindExtension<OceanBaseOptionsExtension>()!;
        private bool Oracle => _options.CompatibilityMode == OceanBaseCompatibilityMode.Oracle;
        private string Q(string name, string? schema = null) => Dependencies.SqlGenerationHelper.DelimitIdentifier(name, schema);

        public override IReadOnlyList<MigrationCommand> Generate(IReadOnlyList<MigrationOperation> operations, IModel? model = null,
            MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default)
        {
            if (model is not null)
            {
                if (model.GetEntityTypes().Any(e => e.GetMappingStrategy() is "TPT" or "TPC"
                    || e.GetMappingFragments(StoreObjectType.Table).Any()))
                    throw Unsupported("TPT/TPC inheritance or entity splitting");
                foreach (var table in model.GetRelationalModel().Tables.Where(t => !t.IsExcludedFromMigrations))
                {
                    if (table.Triggers.Any()) throw Unsupported("model triggers; author their SQL explicitly");
                    foreach (var column in table.Columns)
                        if (column.DefaultValue is null && column.DefaultValueSql is null && column.ComputedColumnSql is null
                            && column.PropertyMappings.Any(m => m.Property.ValueGenerated != ValueGenerated.Never
                                && !(m.Property.IsPrimaryKey() && (m.Property.ClrType == typeof(Guid)
                                    || m.Property.ValueGenerated == ValueGenerated.OnAdd
                                    && (m.TypeMapping.Converter?.ProviderClrType ?? m.Property.ClrType) is var type
                                    && (type == typeof(int) || type == typeof(long))))))
                            throw Unsupported("generated columns without an identity, default or computed expression");
                }
            }
            var commands = new List<MigrationCommand>();
            foreach (var operation in operations)
                foreach (var command in base.Generate(new[] { operation }, model, options))
                {
                    var sql = command.CommandText;
                    // Native Oracle SQL commands are not SQL*Plus batches; PL/SQL/raw SQL retains its own terminator.
                    if (Oracle && operation is not SqlOperation) sql = sql.TrimEnd().TrimEnd(';');
                    if (!Oracle && operation is SqlOperation && options.HasFlag(MigrationsSqlGenerationOptions.Script))
                        sql = sql.TrimEnd() + "\n;\n";
                    commands.Add(new MigrationCommand(Dependencies.CommandBuilderFactory.Create().Append(sql).Build(),
                        Dependencies.CurrentContext.Context, Dependencies.Logger, transactionSuppressed: true));
                }
            return commands;
        }

        protected override void Generate(MigrationOperation operation, IModel? model, MigrationCommandListBuilder builder)
        {
            ValidateAnnotations(operation);
            switch (operation)
            {
                case EnsureSchemaOperation ensure:
                    if (ensure.Name != _options.SchemaManagementScope)
                        throw Unsupported("creating schemas; provision the namespace and explicitly select it first");
                    return;
                case CreateSequenceOperation or AlterSequenceOperation or RestartSequenceOperation or RenameSequenceOperation
                    or DropSequenceOperation or DropSchemaOperation:
                    throw Unsupported("sequence strategies or namespace creation/deletion");
                case AlterDatabaseOperation database when database.Collation is not null || database.OldDatabase.Collation is not null:
                    throw Unsupported("database collations");
                case AlterTableOperation table when table.Comment is not null || table.OldTable.Comment is not null:
                    throw Unsupported("table comments");
                case CreateTableOperation table:
                    if (table.Comment is not null) throw Unsupported("table comments");
                    foreach (var column in table.Columns) ValidateColumn(column);
                    if (table.PrimaryKey is { } primary) ValidateAnnotations(primary);
                    foreach (var unique in table.UniqueConstraints) ValidateAnnotations(unique);
                    foreach (var check in table.CheckConstraints) ValidateAnnotations(check);
                    foreach (var foreignKey in table.ForeignKeys)
                    {
                        ValidateAnnotations(foreignKey);
                        ValidateScope(foreignKey.PrincipalSchema);
                    }
                    var identities = table.Columns.Where(IsIdentity).ToArray();
                    if (identities.Length > 1 || identities.Length == 1
                        && (table.PrimaryKey is null || !table.PrimaryKey.Columns.Contains(identities[0].Name)
                            || !Oracle && table.PrimaryKey.Columns[0] != identities[0].Name))
                        throw Unsupported("multiple identities or an identity outside the leading primary key");
                    break;
                case ColumnOperation column:
                    ValidateColumn(column);
                    break;
                case CreateIndexOperation index when index.Filter is not null:
                    throw Unsupported("filtered indexes");
            }
            var schema = operation is InsertDataOperation data
                ? data.Schema ?? model?.GetDefaultSchema()
                : (operation as ITableMigrationOperation)?.Schema;
            ValidateScope(schema);
            if (operation is AddForeignKeyOperation foreign) ValidateScope(foreign.PrincipalSchema);
            base.Generate(operation, model, builder);
        }

        private void ValidateScope(string? schema)
        {
            if (_options.SchemaManagementScope is { } scope && schema is not null && schema != scope)
                throw Unsupported("operations outside the explicitly selected namespace");
        }

        private void ValidateColumn(ColumnOperation column)
        {
            ValidateAnnotations(column);
            if (column.Comment is not null || column.IsRowVersion || Oracle && column.Collation is not null)
                throw Unsupported("column comments, rowversion or Oracle column collations");
            if (IsIdentity(column) && (column.ClrType != typeof(int) && column.ClrType != typeof(long)
                || column.IsNullable || column.DefaultValue is not null || column.DefaultValueSql is not null
                || column.ComputedColumnSql is not null))
                throw Unsupported("identity columns without required integer storage");
            if (column.DefaultValue is DBNull || column.DefaultValueSql is { } sql && string.IsNullOrWhiteSpace(sql))
                throw Unsupported("unspecified defaults");
        }

        private static void ValidateAnnotations(MigrationOperation operation)
        {
            if (operation.GetAnnotations().Any(a => operation is not ColumnOperation
                || a.Name != OceanBaseRelationalAnnotationProvider.IdentityAnnotation || a.Value is not bool))
                throw Unsupported("unrecognized migration annotations");
        }

        private static bool IsIdentity(ColumnOperation operation)
            => operation[OceanBaseRelationalAnnotationProvider.IdentityAnnotation] is true;

        protected override void ColumnDefinition(string? schema, string table, string name, ColumnOperation operation,
            IModel? model, MigrationCommandListBuilder builder)
        {
            ValidateColumn(operation);
            if (operation.ComputedColumnSql is not null)
            {
                ComputedColumnDefinition(schema, table, name, operation, model, builder);
                return;
            }
            var type = operation.ColumnType ?? GetColumnType(schema, table, name, operation, model);
            builder.Append(Q(name)).Append(" ").Append(type);
            if (operation.Collation is { } collation) builder.Append(" COLLATE ").Append(Q(collation));
            if (Oracle && IsIdentity(operation)) builder.Append(" GENERATED BY DEFAULT AS IDENTITY");
            DefaultValue(operation.DefaultValue, operation.DefaultValueSql, type, builder);
            if (Oracle && operation is AlterColumnOperation alter && operation.DefaultValue is null && operation.DefaultValueSql is null
                && (alter.OldColumn.DefaultValue is not null || alter.OldColumn.DefaultValueSql is not null))
                builder.Append(" DEFAULT NULL");
            if (!Oracle || operation is not AlterColumnOperation old || old.IsNullable != old.OldColumn.IsNullable)
                builder.Append(operation.IsNullable ? " NULL" : " NOT NULL");
            if (!Oracle && IsIdentity(operation)) builder.Append(" AUTO_INCREMENT");
        }

        protected override void ComputedColumnDefinition(string? schema, string table, string name, ColumnOperation operation,
            IModel? model, MigrationCommandListBuilder builder)
        {
            if (string.IsNullOrWhiteSpace(operation.ComputedColumnSql) || Oracle && operation.IsStored == true
                || operation.DefaultValue is not null || operation.DefaultValueSql is not null || operation.Collation is not null)
                throw Unsupported("this computed-column expression or storage combination");
            builder.Append(Q(name));
            if (!Oracle) builder.Append(" ").Append(operation.ColumnType ?? GetColumnType(schema, table, name, operation, model));
            builder.Append(" GENERATED ALWAYS AS (").Append(operation.ComputedColumnSql!).Append(") ")
                .Append(operation.IsStored == true ? "STORED" : "VIRTUAL");
            if (!operation.IsNullable) builder.Append(" NOT NULL");
        }

        protected override void DefaultValue(object? defaultValue, string? defaultValueSql, string? columnType,
            MigrationCommandListBuilder builder)
        {
            if (Oracle || defaultValue is null || defaultValueSql is not null)
            {
                base.DefaultValue(defaultValue, defaultValueSql, columnType, builder);
                return;
            }
            var mapping = (columnType is null ? null : Dependencies.TypeMappingSource.FindMapping(defaultValue.GetType(), columnType))
                ?? Dependencies.TypeMappingSource.GetMappingForValue(defaultValue);
            builder.Append(" DEFAULT (").Append(mapping.GenerateSqlLiteral(defaultValue)).Append(")");
        }

        protected override void Generate(AlterColumnOperation operation, IModel? model, MigrationCommandListBuilder builder)
        {
            if (operation.ComputedColumnSql is not null || operation.OldColumn.ComputedColumnSql is not null
                || IsIdentity(operation) != IsIdentity(operation.OldColumn) || IsIdentity(operation))
                throw Unsupported("altering computed or identity columns; use an explicit data-preserving migration");
            builder.Append("ALTER TABLE ").Append(Q(operation.Table, operation.Schema))
                .Append(Oracle ? " MODIFY (" : " MODIFY COLUMN ");
            ColumnDefinition(operation.Schema, operation.Table, operation.Name, operation, model, builder);
            if (Oracle) builder.Append(")");
            Finish(builder);
        }

        protected override void Generate(RenameColumnOperation operation, IModel? model, MigrationCommandListBuilder builder)
        {
            builder.Append("ALTER TABLE ").Append(Q(operation.Table, operation.Schema)).Append(" RENAME COLUMN ")
                .Append(Q(operation.Name)).Append(" TO ").Append(Q(operation.NewName));
            Finish(builder);
        }

        protected override void Generate(RenameTableOperation operation, IModel? model, MigrationCommandListBuilder builder)
        {
            if (operation.NewSchema is not null && operation.NewSchema != operation.Schema)
                throw Unsupported("transferring tables between namespaces");
            if (operation.NewName is null || operation.NewName == operation.Name) return;
            builder.Append("ALTER TABLE ").Append(Q(operation.Name, operation.Schema)).Append(" RENAME TO ")
                .Append(Q(operation.NewName));
            Finish(builder);
        }

        protected override void Generate(RenameIndexOperation operation, IModel? model, MigrationCommandListBuilder builder)
        {
            if (Oracle) builder.Append("ALTER INDEX ").Append(Q(operation.Name, operation.Schema)).Append(" RENAME TO ").Append(Q(operation.NewName));
            else builder.Append("ALTER TABLE ").Append(Q(operation.Table!, operation.Schema)).Append(" RENAME INDEX ")
                .Append(Q(operation.Name)).Append(" TO ").Append(Q(operation.NewName));
            Finish(builder);
        }

        protected override void Generate(CreateIndexOperation operation, IModel? model, MigrationCommandListBuilder builder, bool terminate = true)
        {
            builder.Append(operation.IsUnique ? "CREATE UNIQUE INDEX " : "CREATE INDEX ")
                .Append(Q(operation.Name, Oracle ? operation.Schema : null)).Append(" ON ")
                .Append(Q(operation.Table, operation.Schema)).Append(" (");
            GenerateIndexColumnList(operation, model, builder);
            builder.Append(")");
            if (terminate) Finish(builder);
        }

        protected override void Generate(DropIndexOperation operation, IModel? model, MigrationCommandListBuilder builder, bool terminate = true)
        {
            builder.Append("DROP INDEX ").Append(Q(operation.Name, Oracle ? operation.Schema : null));
            if (!Oracle) builder.Append(" ON ").Append(Q(operation.Table!, operation.Schema));
            if (terminate) Finish(builder);
        }

        protected override void Generate(DropPrimaryKeyOperation operation, IModel? model, MigrationCommandListBuilder builder, bool terminate = true)
            => DropConstraint(operation.Table, operation.Schema, operation.Name, "PRIMARY KEY", builder, terminate);
        protected override void Generate(DropForeignKeyOperation operation, IModel? model, MigrationCommandListBuilder builder, bool terminate = true)
            => DropConstraint(operation.Table, operation.Schema, operation.Name, "FOREIGN KEY " + Q(operation.Name), builder, terminate);
        protected override void Generate(DropUniqueConstraintOperation operation, IModel? model, MigrationCommandListBuilder builder)
            => DropConstraint(operation.Table, operation.Schema, operation.Name, "INDEX " + Q(operation.Name), builder, true);
        protected override void Generate(DropCheckConstraintOperation operation, IModel? model, MigrationCommandListBuilder builder)
            => DropConstraint(operation.Table, operation.Schema, operation.Name, "CHECK " + Q(operation.Name), builder, true);

        private void DropConstraint(string table, string? schema, string name, string mysqlClause, MigrationCommandListBuilder builder, bool terminate)
        {
            builder.Append("ALTER TABLE ").Append(Q(table, schema)).Append(" DROP ")
                .Append(Oracle ? "CONSTRAINT " + Q(name) : mysqlClause);
            if (terminate) Finish(builder);
        }

        protected override void ForeignKeyConstraint(AddForeignKeyOperation operation, IModel? model, MigrationCommandListBuilder builder)
        {
            if (!Oracle) { base.ForeignKeyConstraint(operation, model, builder); return; }
            if (operation.OnUpdate != ReferentialAction.NoAction || operation.OnDelete == ReferentialAction.SetDefault)
                throw Unsupported("Oracle ON UPDATE or ON DELETE SET DEFAULT");
            if (operation.Name is { } name) builder.Append("CONSTRAINT ").Append(Q(name)).Append(" ");
            builder.Append("FOREIGN KEY (").Append(ColumnList(operation.Columns)).Append(") REFERENCES ")
                .Append(Q(operation.PrincipalTable, operation.PrincipalSchema));
            if (operation.PrincipalColumns is { } columns) builder.Append(" (").Append(ColumnList(columns)).Append(")");
            if (operation.OnDelete is ReferentialAction.Cascade or ReferentialAction.SetNull)
            {
                builder.Append(" ON DELETE ");
                ForeignKeyAction(operation.OnDelete, builder);
            }
        }

        protected override void ForeignKeyAction(ReferentialAction action, MigrationCommandListBuilder builder)
        {
            if (action == ReferentialAction.SetDefault) throw Unsupported("ON DELETE/UPDATE SET DEFAULT");
            base.ForeignKeyAction(action, builder);
        }

        protected override void Generate(InsertDataOperation operation, IModel? model, MigrationCommandListBuilder builder, bool terminate = true)
            => Seed(GenerateModificationCommands(operation, model), builder, EntityState.Added);
        protected override void Generate(UpdateDataOperation operation, IModel? model, MigrationCommandListBuilder builder)
            => Seed(GenerateModificationCommands(operation, model), builder, EntityState.Modified);
        protected override void Generate(DeleteDataOperation operation, IModel? model, MigrationCommandListBuilder builder)
            => Seed(GenerateModificationCommands(operation, model), builder, EntityState.Deleted);

        private void Seed(IEnumerable<IReadOnlyModificationCommand> commands, MigrationCommandListBuilder builder, EntityState state)
        {
            foreach (var command in commands)
            {
                var sql = new StringBuilder();
                if (state == EntityState.Added) SqlGenerator.AppendInsertOperation(sql, command, 0, out _);
                else if (state == EntityState.Modified) SqlGenerator.AppendUpdateOperation(sql, command, 0, out _);
                else SqlGenerator.AppendDeleteOperation(sql, command, 0, out _);
                builder.Append(sql.ToString());
                EndStatement(builder);
            }
        }

        protected override void EndStatement(MigrationCommandListBuilder builder, bool suppressTransaction = false)
            => base.EndStatement(builder, suppressTransaction: true);

        private void Finish(MigrationCommandListBuilder builder)
        {
            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            EndStatement(builder);
        }

        private static NotSupportedException Unsupported(string feature)
            => new("OceanBase migrations do not support " + feature + ".");
    }
}
