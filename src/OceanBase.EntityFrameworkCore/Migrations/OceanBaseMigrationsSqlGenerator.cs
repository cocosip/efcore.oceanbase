using System;
using System.Collections;
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
using OceanBase.EntityFrameworkCore.Storage;
using static OceanBase.EntityFrameworkCore.Metadata.OceanBaseRelationalAnnotationProvider;

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
            operations = OrderExpressionIndexChanges(operations);
            if (!Oracle)
            {
                var tablesWithDroppedIndexes = new HashSet<(string? Schema, string Table)>();
                foreach (var operation in operations)
                {
                    if (operation is AlterColumnOperation column && IsIdentity(column) != IsIdentity(column.OldColumn)
                        && operations.Any(candidate => ConflictsWithIdentityToggle(candidate, column.Table)))
                        throw Unsupported("renaming a table or changing its primary key in the same migration as its AUTO_INCREMENT attribute");
                    if (operation is DropIndexOperation { Table: { } table } drop)
                        tablesWithDroppedIndexes.Add((drop.Schema ?? _options.SchemaManagementScope, table));
                    if (operation is CreateIndexOperation { IsUnique: true } create
                        && create[IndexPrefixLengthsAnnotation] is not null
                        && tablesWithDroppedIndexes.Contains((create.Schema ?? _options.SchemaManagementScope, create.Table)))
                        throw Unsupported("creating unique prefix indexes after an index drop on the same table. Create the replacement first with a distinct name or use reviewed SQL that preserves uniqueness on failure");
                }
            }
            if (model is not null)
            {
                ValidateTpcKeyGeneration(model, Oracle, Dependencies.SqlGenerationHelper);
                if (Oracle) ValidateTpcSequenceChanges(operations, model);
                foreach (var table in model.GetRelationalModel().Tables.Where(t => !t.IsExcludedFromMigrations))
                {
                    foreach (var column in table.Columns)
                        if (column.DefaultValue is null && column.DefaultValueSql is null && column.ComputedColumnSql is null
                            && column.PropertyMappings.Any(m => m.Property.ValueGenerated is not (ValueGenerated.Never or ValueGenerated.OnUpdateSometimes)
                                && !(column[OnUpdateSqlAnnotation] is not null && m.Property.ValueGenerated == ValueGenerated.OnUpdate)
                                && !OceanBaseRelationalAnnotationProvider.IsIdentity(column)
                                && !(m.Property.IsPrimaryKey() && (m.Property.ClrType == typeof(Guid)
                                    || m.Property.ValueGenerated == ValueGenerated.OnAdd
                                    && (m.TypeMapping.Converter?.ProviderClrType ?? m.Property.ClrType) is var type
                                    && (type == typeof(int) || type == typeof(long))))))
                            throw Unsupported("generated columns without an identity, default or computed expression");
                }
            }
            var commands = new List<MigrationCommand>();
            var combinedRestarts = new HashSet<int>();
            for (var index = 0; index < operations.Count; index++)
            {
                if (combinedRestarts.Contains(index)) continue;
                var operation = operations[index];
                if (operation is AlterSequenceOperation alter
                    && FindRestart(index, alter) is var restartIndex && restartIndex >= 0)
                {
                    var restart = (RestartSequenceOperation)operations[restartIndex];
                    ValidateAnnotations(alter);
                    ValidateAnnotations(restart);
                    ValidateScope(alter.Schema);
                    if (restart.StartValue is { } start)
                        OceanBaseSequenceSql.ValidateStart(start, alter.IncrementBy, alter.MinValue, alter.MaxValue);
                    var builder = new MigrationCommandListBuilder(Dependencies);
                    builder.Append("ALTER SEQUENCE ").Append(Q(alter.Name, alter.Schema))
                        .Append(OceanBaseSequenceSql.Options(alter.IncrementBy, alter.MinValue, alter.MaxValue, alter.IsCyclic));
                    AppendSequenceOptions(alter, builder);
                    AppendRestart(builder, restart.StartValue);
                    Finish(builder);
                    AddCommand(builder.GetCommandList()[0].CommandText, operation);
                    combinedRestarts.Add(restartIndex);
                    continue;
                }
                foreach (var command in base.Generate(new[] { operation }, model, options))
                    AddCommand(command.CommandText, operation);
            }
            return commands;

            int FindRestart(int index, AlterSequenceOperation alter)
            {
                // EF sorts sequence alterations before restarts. Keep other migration operations in their original order.
                for (var next = index + 1; next < operations.Count; next++)
                {
                    switch (operations[next])
                    {
                        case RestartSequenceOperation restart:
                            if (restart.Name == alter.Name && restart.Schema == alter.Schema) return next;
                            break;
                        case AlterSequenceOperation other:
                            if (other.Name == alter.Name && other.Schema == alter.Schema) return -1;
                            break;
                        default:
                            return -1;
                    }
                }
                return -1;
            }

            void AddCommand(string sql, MigrationOperation operation)
            {
                    // Native Oracle SQL commands are not SQL*Plus batches; PL/SQL/raw SQL retains its own terminator.
                    if (Oracle && operation is not SqlOperation) sql = sql.TrimEnd().TrimEnd(';');
                    if (!Oracle && operation is SqlOperation && options.HasFlag(MigrationsSqlGenerationOptions.Script))
                        sql = sql.TrimEnd() + "\n;\n";
                    commands.Add(new MigrationCommand(Dependencies.CommandBuilderFactory.Create().Append(sql).Build(),
                        Dependencies.CurrentContext.Context, Dependencies.Logger, transactionSuppressed: true));
            }
        }

        private void ValidateTpcSequenceChanges(IReadOnlyList<MigrationOperation> operations, IModel model)
        {
            foreach (var property in TpcKeyProperties(model))
            {
                if (FindSharedOracleSequenceDefault(property, model, Dependencies.SqlGenerationHelper) is not { } sequence)
                    continue;

                var columns = property.GetTableColumnMappings().Select(mapping => mapping.Column).ToArray();
                var sequenceChanged = operations.Any(operation => operation switch
                {
                    RestartSequenceOperation restart => Matches(restart.Name, restart.Schema),
                    AlterSequenceOperation alter => Matches(alter.Name, alter.Schema),
                    DropSequenceOperation drop => Matches(drop.Name, drop.Schema),
                    RenameSequenceOperation rename => Matches(rename.Name, rename.Schema)
                        || Matches(rename.NewName ?? rename.Name, rename.NewSchema ?? rename.Schema),
                    _ => false
                });
                var defaultChanged = operations.OfType<AlterColumnOperation>().Any(alter =>
                    alter.OldColumn.DefaultValueSql != alter.DefaultValueSql && columns.Any(column =>
                        column.Name == alter.Name && column.Table.Name == alter.Table && column.Table.Schema == alter.Schema));
                if (sequenceChanged || defaultChanged)
                    throw Unsupported("changing a shared TPC sequence or its default on existing columns; use an explicit data-preserving migration");

                bool Matches(string name, string? schema)
                    => sequence.Name == name && (sequence.Schema ?? _options.SchemaManagementScope)
                        == (schema ?? _options.SchemaManagementScope);
            }
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
                case CreateSequenceOperation sequence:
                    ValidateScope(sequence.Schema);
                    builder.Append(OceanBaseSequenceSql.Create(Q(sequence.Name, sequence.Schema), sequence.ClrType,
                        sequence.StartValue, sequence.IncrementBy, sequence.MinValue, sequence.MaxValue, sequence.IsCyclic, noCache: false));
                    AppendSequenceOptions(sequence, builder, creating: true);
                    Finish(builder);
                    return;
                case AlterSequenceOperation sequence:
                    ValidateScope(sequence.Schema);
                    builder.Append("ALTER SEQUENCE ").Append(Q(sequence.Name, sequence.Schema))
                        .Append(OceanBaseSequenceSql.Options(sequence.IncrementBy, sequence.MinValue, sequence.MaxValue, sequence.IsCyclic));
                    AppendSequenceOptions(sequence, builder);
                    Finish(builder);
                    return;
                case RestartSequenceOperation sequence:
                    ValidateScope(sequence.Schema);
                    builder.Append("ALTER SEQUENCE ").Append(Q(sequence.Name, sequence.Schema));
                    AppendRestart(builder, sequence.StartValue);
                    Finish(builder);
                    return;
                case RenameSequenceOperation sequence when Oracle && sequence.Schema is null && sequence.NewSchema is null:
                    builder.Append("RENAME ").Append(Q(sequence.Name)).Append(" TO ")
                        .Append(Q(sequence.NewName ?? throw Unsupported("renaming a sequence without a target name")));
                    Finish(builder);
                    return;
                case DropSequenceOperation sequence:
                    ValidateScope(sequence.Schema);
                    break;
                case RenameSequenceOperation or DropSchemaOperation:
                    throw Unsupported("this sequence rename or namespace creation/deletion");
                case AlterDatabaseOperation database when database.Collation is not null || database.OldDatabase.Collation is not null
                    || database[DatabaseCollationAnnotation] is not null || database.OldDatabase[DatabaseCollationAnnotation] is not null:
                    var ownedDatabase = !Oracle && _options.DatabaseManagementEnabled ? _options.SchemaManagementScope : null;
                    if (database.Collation is not null)
                        OceanBaseSchemaSqlGenerator.ValidateDatabaseCollation(database.OldDatabase.Collation);
                    builder.Append(OceanBaseSchemaSqlGenerator.DatabaseCollationStatement(Dependencies.SqlGenerationHelper,
                        ownedDatabase, database.Collation ?? (string?)database[DatabaseCollationAnnotation]));
                    Finish(builder);
                    return;
                case CreateTableOperation table:
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
                    if (identities.Length > 1 || !Oracle && identities.Length == 1
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

        private static void AppendRestart(MigrationCommandListBuilder builder, long? start)
        {
            if (start is { } value)
                builder.Append(" START WITH ").Append(OceanBaseSequenceSql.Number(value));
            builder.Append(" RESTART");
        }

        private void AppendSequenceOptions(MigrationOperation operation, MigrationCommandListBuilder builder, bool creating = false)
        {
            var previous = (operation as AlterSequenceOperation)?.OldSequence;
            var cache = operation[SequenceCacheAnnotation] ?? (previous?[SequenceCacheAnnotation] is null ? null : (object)0L);
            var ordered = operation[SequenceOrderedAnnotation] ?? (previous?[SequenceOrderedAnnotation] is null ? null : (object)false);
            builder.Append(OceanBaseSequenceSql.AllocationOptions(Oracle, cache, ordered, creating));
        }

        private void ValidateScope(string? schema)
        {
            if (_options.SchemaManagementScope is { } scope && schema is not null && schema != scope)
                throw Unsupported("operations outside the explicitly selected namespace");
        }

        private void ValidateColumn(ColumnOperation column)
        {
            ValidateAnnotations(column);
            if (column.IsRowVersion || Oracle && column.Collation is not null)
                throw Unsupported("rowversion or Oracle column collations");
            if (IsIdentity(column) && (column.ClrType != typeof(int) && column.ClrType != typeof(long)
                || column.IsNullable || column.DefaultValue is not null || column.DefaultValueSql is not null
                || column.ComputedColumnSql is not null))
                throw Unsupported("identity columns without required integer storage");
            if (column.DefaultValue is DBNull || column.DefaultValueSql is { } sql && string.IsNullOrWhiteSpace(sql))
                throw Unsupported("unspecified defaults");
            if (column[IdentityGenerationAnnotation] is { } generation
                && (!Oracle || !IsIdentity(column) || generation is not ("ALWAYS" or "BY DEFAULT" or "BY DEFAULT ON NULL")))
                throw Unsupported("this identity generation mode");
            if (column[IdentityOptionsAnnotation] is { } identityOptions
                && (!Oracle || !IsIdentity(column) || identityOptions is not string options || string.IsNullOrWhiteSpace(options)))
                throw Unsupported("identity options without an Oracle identity column");
        }

        private static void ValidateAnnotations(MigrationOperation operation)
        {
            if (operation.GetAnnotations().Any(annotation => annotation.Name switch
                {
                    IdentityAnnotation => operation is not ColumnOperation || annotation.Value is not bool,
                    IdentityGenerationAnnotation or IdentityOptionsAnnotation => operation is not ColumnOperation || annotation.Value is not string,
                    SequenceCacheAnnotation => operation is not SequenceOperation || annotation.Value is not long,
                    SequenceOrderedAnnotation => operation is not SequenceOperation || annotation.Value is not bool,
                    OnUpdateSqlAnnotation => operation is not ColumnOperation || annotation.Value is not string,
                    IndexPrefixLengthsAnnotation => operation is not CreateIndexOperation || annotation.Value is not int[],
                    DatabaseCollationAnnotation => operation is not AlterDatabaseOperation || annotation.Value is not string,
                    _ when annotation.Name.StartsWith(ExpressionIndexPrefix, StringComparison.Ordinal) =>
                        operation is not (CreateTableOperation or AlterTableOperation)
                        || annotation.Name.Length == ExpressionIndexPrefix.Length
                        || annotation.Value is not string[] members || members.Length < 2
                        || members[0] is not ("UNIQUE" or "NONUNIQUE") || members.Any(string.IsNullOrWhiteSpace),
                    _ => true
                }))
                throw Unsupported("unrecognized migration annotations");
        }

        private static bool IsIdentity(ColumnOperation operation)
            => operation[OceanBaseRelationalAnnotationProvider.IdentityAnnotation] is true;

        protected override void Generate(CreateTableOperation operation, IModel? model, MigrationCommandListBuilder builder,
            bool terminate = true)
        {
            base.Generate(operation, model, builder, terminate: false);
            if (!Oracle && operation.Comment is not null)
                builder.Append(" COMMENT = ").Append(CommentLiteral(operation.Comment));

            if (!terminate) return;
            Finish(builder);
            CreateExpressionIndexes(operation.Name, operation.Schema, operation.GetAnnotations(), builder);
            if (!Oracle) return;

            if (operation.Comment is not null)
                OracleComment(builder, operation.Name, operation.Schema, null, operation.Comment);
            foreach (var column in operation.Columns.Where(column => column.Comment is not null))
                OracleComment(builder, operation.Name, operation.Schema, column.Name, column.Comment);
        }

        protected override void Generate(AlterTableOperation operation, IModel? model, MigrationCommandListBuilder builder)
        {
            if (operation.Comment == operation.OldTable.Comment) return;
            if (Oracle)
            {
                OracleComment(builder, operation.Name, operation.Schema, null, operation.Comment);
                return;
            }

            builder.Append("ALTER TABLE ").Append(Q(operation.Name, operation.Schema))
                .Append(" COMMENT = ").Append(CommentLiteral(operation.Comment));
            Finish(builder);
        }

        private IReadOnlyList<MigrationOperation> OrderExpressionIndexChanges(IReadOnlyList<MigrationOperation> operations)
        {
            var before = new List<MigrationOperation>();
            var after = new List<MigrationOperation>();
            foreach (var operation in operations.OfType<AlterTableOperation>())
            {
                ValidateAnnotations(operation);
                var current = operation.GetAnnotations().Where(annotation => annotation.Name.StartsWith(ExpressionIndexPrefix, StringComparison.Ordinal))
                    .ToDictionary(annotation => annotation.Name, annotation => annotation);
                var previous = operation.OldTable.GetAnnotations().Where(annotation => annotation.Name.StartsWith(ExpressionIndexPrefix, StringComparison.Ordinal))
                    .ToDictionary(annotation => annotation.Name, annotation => annotation);
                var changed = current.Keys.Union(previous.Keys).Where(name => !StructuralComparisons.StructuralEqualityComparer.Equals(
                    current.GetValueOrDefault(name)?.Value, previous.GetValueOrDefault(name)?.Value)).ToArray();
                if (changed.Any(name => current.ContainsKey(name) && previous.GetValueOrDefault(name)?.Value is string[] { Length: > 0 } members && members[0] == "UNIQUE"))
                    throw Unsupported("rebuilding a unique expression index with the same name; create its replacement with a distinct name before dropping it");
                var preservingUniqueness = changed.Any(name => previous.GetValueOrDefault(name)?.Value is string[] { Length: > 0 } members && members[0] == "UNIQUE")
                    && changed.Any(current.ContainsKey);
                if (preservingUniqueness && operations.Any(candidate => candidate is ColumnOperation column && column.Table == operation.Name && column.Schema == operation.Schema
                    || candidate is DropColumnOperation drop && drop.Table == operation.Name && drop.Schema == operation.Schema
                    || candidate is RenameColumnOperation rename && rename.Table == operation.Name && rename.Schema == operation.Schema
                    || candidate is RenameTableOperation table && table.NewName == operation.Name && table.NewSchema == operation.Schema))
                    throw Unsupported("replacing unique expression indexes together with table/column changes; create the replacement in a separate migration before dropping the original");
                foreach (var name in changed.Where(current.ContainsKey))
                {
                    var target = preservingUniqueness ? before : after;
                    target.Add(new SqlOperation { Sql = ExpressionIndexSql(Dependencies.SqlGenerationHelper, Oracle, operation.Name, operation.Schema, current[name]), SuppressTransaction = true });
                }
                foreach (var name in changed.Where(previous.ContainsKey))
                {
                    if (!Oracle) throw Unsupported("MySQL expression indexes");
                    before.Add(new SqlOperation { Sql = "DROP INDEX " + Q(name[ExpressionIndexPrefix.Length..], operation.Schema), SuppressTransaction = true });
                }
            }
            return before.Concat(operations).Concat(after).ToArray();
        }

        private void CreateExpressionIndexes(string table, string? schema, IEnumerable<IAnnotation> annotations, MigrationCommandListBuilder builder)
        {
            foreach (var annotation in annotations.Where(annotation => annotation.Name.StartsWith(ExpressionIndexPrefix, StringComparison.Ordinal)))
            {
                builder.Append(ExpressionIndexSql(Dependencies.SqlGenerationHelper, Oracle, table, schema, annotation));
                Finish(builder);
            }
        }

        protected override void Generate(AddColumnOperation operation, IModel? model, MigrationCommandListBuilder builder,
            bool terminate = true)
        {
            ValidateOnUpdateBackfill(operation);
            base.Generate(operation, model, builder, terminate);
            if (Oracle && terminate && operation.Comment is not null)
                OracleComment(builder, operation.Table, operation.Schema, operation.Name, operation.Comment);
        }

        private void OracleComment(MigrationCommandListBuilder builder, string table, string? schema, string? column, string? comment)
        {
            builder.Append(OceanBaseCommentSql.OracleStatement(Q(table, schema), column is null ? null : Q(column), comment));
            Finish(builder);
        }

        private string CommentLiteral(string? comment)
            => OceanBaseCommentSql.Literal(comment, Oracle);

        protected override void ColumnDefinition(string? schema, string table, string name, ColumnOperation operation,
            IModel? model, MigrationCommandListBuilder builder)
        {
            ValidateColumn(operation);
            if (operation.ComputedColumnSql is not null)
            {
                ComputedColumnDefinition(schema, table, name, operation, model, builder);
                AppendMySqlColumnComment(operation, builder);
                return;
            }
            var type = operation.ColumnType ?? GetColumnType(schema, table, name, operation, model);
            var update = operation[OnUpdateSqlAnnotation];
            if (update is not null && (Oracle || IsIdentity(operation) || !IsSupportedOnUpdate(update as string, type, operation.DefaultValueSql)))
                throw Unsupported("this ON UPDATE clause");
            builder.Append(Q(name)).Append(" ").Append(type);
            if (operation.Collation is { } collation) builder.Append(" COLLATE ").Append(Q(collation));
            builder.Append(IdentityClause(Oracle, IsIdentity(operation), operation[IdentityGenerationAnnotation], operation[IdentityOptionsAnnotation]));
            DefaultValue(operation.DefaultValue, operation.DefaultValueSql, type, builder);
            if (Oracle && operation is AlterColumnOperation alter && operation.DefaultValue is null && operation.DefaultValueSql is null
                && (alter.OldColumn.DefaultValue is not null || alter.OldColumn.DefaultValueSql is not null))
                builder.Append(" DEFAULT NULL");
            if (!Oracle || operation is not AlterColumnOperation old || old.IsNullable != old.OldColumn.IsNullable)
                builder.Append(operation.IsNullable ? " NULL" : " NOT NULL");
            if (!Oracle && IsIdentity(operation)) builder.Append(" AUTO_INCREMENT");
            if (update is string updateSql) builder.Append(" ON UPDATE ").Append(updateSql);
            AppendMySqlColumnComment(operation, builder);
        }

        private void AppendMySqlColumnComment(ColumnOperation operation, MigrationCommandListBuilder builder)
        {
            if (!Oracle && (operation.Comment is not null || operation is AlterColumnOperation { OldColumn.Comment: not null }))
                builder.Append(" COMMENT ").Append(CommentLiteral(operation.Comment));
        }

        protected override void ComputedColumnDefinition(string? schema, string table, string name, ColumnOperation operation,
            IModel? model, MigrationCommandListBuilder builder)
        {
            if (string.IsNullOrWhiteSpace(operation.ComputedColumnSql) || Oracle && operation.IsStored == true
                || operation.DefaultValue is not null && !(operation is AlterColumnOperation alter && IsComputedBackfillDefault(alter, model))
                || operation.DefaultValueSql is not null || operation.Collation is not null
                || operation[OnUpdateSqlAnnotation] is not null)
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
            var old = operation.OldColumn;
            if (old.IsNullable) ValidateOnUpdateBackfill(operation);
            var commentChanged = operation.Comment != old.Comment;
            var identityChanged = IsIdentity(operation) != IsIdentity(old);
            var sameDefinition = SameColumnDefinition(operation, old);
            var commentOnly = commentChanged && !identityChanged && sameDefinition;
            if (Oracle && commentOnly)
            {
                OracleComment(builder, operation.Table, operation.Schema, operation.Name, operation.Comment);
                return;
            }

            var virtualNullabilityOnly = !Oracle && operation.ComputedColumnSql is not null && operation.IsStored != true
                && !identityChanged && !IsIdentity(operation) && operation.IsNullable != old.IsNullable
                && SameColumnDefinition(operation, old, ignoreNullability: true, ignoreDefault: IsComputedBackfillDefault(operation, model));
            if (!commentOnly && !virtualNullabilityOnly && (operation.ComputedColumnSql is not null || old.ComputedColumnSql is not null
                || identityChanged || IsIdentity(operation)))
            {
                if (Oracle || !identityChanged || !sameDefinition || commentChanged || !IsMySqlIdentityToggle(operation, model))
                    throw Unsupported("altering computed or identity columns; use an explicit data-preserving migration");
                ValidateColumn(old);
            }
            builder.Append("ALTER TABLE ").Append(Q(operation.Table, operation.Schema))
                .Append(Oracle ? " MODIFY (" : " MODIFY COLUMN ");
            ColumnDefinition(operation.Schema, operation.Table, operation.Name, operation, model, builder);
            if (Oracle) builder.Append(")");
            Finish(builder);
            if (Oracle && commentChanged)
                OracleComment(builder, operation.Table, operation.Schema, operation.Name, operation.Comment);
        }

        private static bool ConflictsWithIdentityToggle(MigrationOperation operation, string table)
        {
            bool Matches(string? name) => string.Equals(name, table, StringComparison.OrdinalIgnoreCase);
            return operation switch
            {
                AddPrimaryKeyOperation add => Matches(add.Table),
                DropPrimaryKeyOperation drop => Matches(drop.Table),
                RenameTableOperation rename => Matches(rename.Name) || Matches(rename.NewName),
                _ => false
            };
        }

        private static bool IsMySqlIdentityToggle(AlterColumnOperation column, IModel? model)
        {
            var expectedType = column.ClrType == typeof(int) ? "INT" : column.ClrType == typeof(long) ? "BIGINT" : null;
            if (expectedType is null || !string.Equals(column.ColumnType, expectedType, StringComparison.OrdinalIgnoreCase)
                || column.IsNullable || column.DefaultValue is not null || column.DefaultValueSql is not null
                || column.ComputedColumnSql is not null || column.Collation is not null || column[OnUpdateSqlAnnotation] is not null)
                return false;

            var table = model?.GetRelationalModel().FindTable(column.Table, column.Schema);
            return table?.PrimaryKey?.Columns is [var key] && key.Name == column.Name
                && string.Equals(key.StoreType, expectedType, StringComparison.OrdinalIgnoreCase) && !key.IsNullable
                && key.DefaultValue is null && key.DefaultValueSql is null && key.ComputedColumnSql is null
                && key.Collation is null && key[OnUpdateSqlAnnotation] is null && key.Comment == column.Comment
                && OceanBaseRelationalAnnotationProvider.IsIdentity(key) == IsIdentity(column)
                && table.Columns.All(member => member == key || !OceanBaseRelationalAnnotationProvider.IsIdentity(member));
        }

        // EF adds a CLR backfill default when requiring a column, including computed columns.
        private static bool IsComputedBackfillDefault(AlterColumnOperation column, IModel? model)
            => !column.IsNullable && column.OldColumn.IsNullable && column.OldColumn.DefaultValue is null
                && column.DefaultValue is not null && (column.DefaultValue switch
                {
                    string text => column.ClrType == typeof(string) && text.Length == 0,
                    byte[] bytes => column.ClrType == typeof(byte[]) && bytes.Length == 0,
                    _ => column.ClrType.IsValueType && Equals(column.DefaultValue, Activator.CreateInstance(column.ClrType))
                })
                && model?.GetRelationalModel().FindTable(column.Table, column.Schema)?.FindColumn(column.Name)
                    is { ComputedColumnSql: not null, DefaultValue: null, DefaultValueSql: null } target
                && target.ComputedColumnSql == column.ComputedColumnSql;

        private static bool SameColumnDefinition(ColumnOperation column, ColumnOperation old, bool ignoreNullability = false, bool ignoreDefault = false)
            => column.ClrType == old.ClrType && column.ColumnType == old.ColumnType
                && column.IsUnicode == old.IsUnicode && column.IsFixedLength == old.IsFixedLength
                && column.MaxLength == old.MaxLength && column.Precision == old.Precision && column.Scale == old.Scale
                && (ignoreNullability || column.IsNullable == old.IsNullable) && column.IsRowVersion == old.IsRowVersion
                && column.Collation == old.Collation && column.DefaultValueSql == old.DefaultValueSql
                && (ignoreDefault || StructuralComparisons.StructuralEqualityComparer.Equals(column.DefaultValue, old.DefaultValue))
                && column.ComputedColumnSql == old.ComputedColumnSql && column.IsStored == old.IsStored
                && Equals(column[IdentityGenerationAnnotation], old[IdentityGenerationAnnotation])
                && Equals(column[IdentityOptionsAnnotation], old[IdentityOptionsAnnotation])
                && Equals(column[OnUpdateSqlAnnotation], old[OnUpdateSqlAnnotation]);

        private static void ValidateOnUpdateBackfill(ColumnOperation operation)
        {
            if (!operation.IsNullable && operation[OnUpdateSqlAnnotation] is not null && operation.DefaultValueSql is null
                && (operation.DefaultValue is null || operation.DefaultValue is DateTime value && value == DateTime.MinValue))
                throw Unsupported("requiring an update-only column without an insert default; use an explicit backfill migration");
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
            var lengths = IndexPrefixLengths(operation[IndexPrefixLengthsAnnotation], operation.Columns.Length, Oracle);
            builder.Append(operation.IsUnique ? "CREATE UNIQUE INDEX " : "CREATE INDEX ")
                .Append(Q(operation.Name, Oracle ? operation.Schema : null)).Append(" ON ")
                .Append(Q(operation.Table, operation.Schema)).Append(" (");
            if (lengths is null) GenerateIndexColumnList(operation, model, builder);
            else
            {
                var columns = operation.Columns.Select((column, position) => Q(column) + IndexPrefixSql(lengths, position)
                    + (operation.IsDescending is { } descending && (descending.Length == 0 || descending[position]) ? " DESC" : ""));
                builder.Append(string.Join(", ", columns));
            }
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
