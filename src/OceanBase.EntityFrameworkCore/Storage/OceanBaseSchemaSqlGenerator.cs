using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using static OceanBase.EntityFrameworkCore.Metadata.OceanBaseRelationalAnnotationProvider;

namespace OceanBase.EntityFrameworkCore.Storage
{
    internal sealed class OceanBaseSchemaSqlGenerator(ISqlGenerationHelper sql, OceanBaseCompatibilityMode mode)
    {
        private bool Oracle => mode == OceanBaseCompatibilityMode.Oracle;

        public IReadOnlyList<string> Generate(IModel model)
        {
            if (model.GetSequences().Any() || model.GetEntityTypes().Any(e => e.GetSeedData().Any()) || model.GetCollation() is not null)
            {
                throw Unsupported("model sequences, HasData inserts or database collations");
            }
            if (model.GetEntityTypes().Any(e => e.GetMappingStrategy() == "TPC"))
            {
                throw Unsupported("TPC inheritance");
            }
            var tables = model.GetRelationalModel().Tables.Where(t => !t.IsExcludedFromMigrations)
                .OrderBy(t => t.Schema, StringComparer.Ordinal).ThenBy(t => t.Name, StringComparer.Ordinal).ToArray();
            var commands = new List<string>();
            foreach (var table in tables)
            {
                if (table.Comment is not null || table.Triggers.Any()) throw Unsupported("table comments or triggers");
                var identities = table.Columns.Where(IsIdentity).ToArray();
                if (identities.Length > 1 || identities.Length == 1
                    && (table.PrimaryKey is null || !table.PrimaryKey.Columns.Contains(identities[0])
                        || !Oracle && table.PrimaryKey.Columns[0] != identities[0]))
                {
                    throw Unsupported("multiple identities or an identity outside the leading primary-key column");
                }
                var definitions = table.Columns.OrderBy(c => c.Order ?? int.MaxValue).ThenBy(c => c.Name, StringComparer.Ordinal)
                    .Select(Column).ToList();
                if (table.PrimaryKey is { } primary)
                {
                    definitions.Add("CONSTRAINT " + Q(primary.Name) + " PRIMARY KEY (" + Columns(primary.Columns) + ")");
                }
                foreach (var key in table.UniqueConstraints.Where(k => k != table.PrimaryKey).OrderBy(k => k.Name, StringComparer.Ordinal))
                {
                    definitions.Add("CONSTRAINT " + Q(key.Name) + " UNIQUE (" + Columns(key.Columns) + ")");
                }
                foreach (var check in table.CheckConstraints.OrderBy(c => c.Name, StringComparer.Ordinal))
                {
                    definitions.Add("CONSTRAINT " + Q(check.Name!) + " CHECK (" + check.Sql + ")");
                }
                commands.Add("CREATE TABLE " + Table(table) + " (\n    " + string.Join(",\n    ", definitions) + "\n)");
            }
            foreach (var table in tables)
            {
                foreach (var index in table.Indexes.OrderBy(i => i.Name, StringComparer.Ordinal))
                {
                    if (index.Filter is not null) throw Unsupported("filtered indexes");
                    var columns = index.Columns.Select((column, i) => Q(column.Name)
                        + (index.IsDescending is { } descending && (descending.Count == 0 || descending[i]) ? " DESC" : ""));
                    commands.Add("CREATE " + (index.IsUnique ? "UNIQUE " : "") + "INDEX "
                        + (Oracle ? sql.DelimitIdentifier(index.Name, table.Schema) : Q(index.Name))
                        + " ON " + Table(table) + " (" + string.Join(", ", columns) + ")");
                }
                foreach (var foreignKey in table.ForeignKeyConstraints.OrderBy(f => f.Name, StringComparer.Ordinal))
                {
                    var delete = foreignKey.OnDeleteAction switch
                    {
                        ReferentialAction.NoAction => "",
                        ReferentialAction.Restrict => Oracle ? "" : " ON DELETE RESTRICT",
                        ReferentialAction.Cascade => " ON DELETE CASCADE",
                        ReferentialAction.SetNull => " ON DELETE SET NULL",
                        _ => throw Unsupported("ON DELETE SET DEFAULT")
                    };
                    commands.Add("ALTER TABLE " + Table(table) + " ADD CONSTRAINT " + Q(foreignKey.Name)
                        + " FOREIGN KEY (" + Columns(foreignKey.Columns) + ") REFERENCES " + Table(foreignKey.PrincipalTable)
                        + " (" + Columns(foreignKey.PrincipalColumns) + ")" + delete);
                }
            }
            return commands;
        }

        private string Column(IColumn column)
        {
            if (column.Comment is not null || column.Collation is not null && Oracle)
            {
                throw Unsupported("column comments or Oracle column collations");
            }
            var collationSql = column.Collation is { } collation ? " COLLATE " + Q(collation) : "";
            if (column.ComputedColumnSql is { } computed)
            {
                if (string.IsNullOrWhiteSpace(computed) || Oracle && column.IsStored == true)
                {
                    throw Unsupported("unspecified computed expressions or Oracle stored computed columns");
                }
                return Q(column.Name) + (Oracle ? "" : " " + column.StoreType + collationSql)
                    + " GENERATED ALWAYS AS (" + computed + ") " + (column.IsStored == true ? "STORED" : "VIRTUAL")
                    + (column.IsNullable ? "" : " NOT NULL");
            }
            var identity = IsIdentity(column);
            foreach (var mapping in column.PropertyMappings)
            {
                var property = mapping.Property;
                if (property.ValueGenerated is not (ValueGenerated.Never or ValueGenerated.OnUpdateSometimes)
                    && column.DefaultValue is null && column.DefaultValueSql is null
                    && !identity && !IsKeyPropagation(mapping) && !(property.IsPrimaryKey() && property.ClrType == typeof(Guid)))
                {
                    throw Unsupported("generated columns without an identity, default or computed expression");
                }
            }
            var definition = Q(column.Name) + " " + column.StoreType + collationSql;
            if (identity && Oracle) definition += " GENERATED BY DEFAULT AS IDENTITY";
            if (column.DefaultValueSql is { } defaultSql)
            {
                if (string.IsNullOrWhiteSpace(defaultSql)) throw Unsupported("unspecified SQL defaults");
                definition += " DEFAULT (" + defaultSql + ")";
            }
            else if (column.DefaultValue is { } value)
            {
                if (value is DBNull) throw Unsupported("unspecified defaults");
                var literal = column.StoreTypeMapping.GenerateProviderValueSqlLiteral(value);
                definition += " DEFAULT " + (Oracle ? literal : "(" + literal + ")");
            }
            definition += column.IsNullable ? " NULL" : " NOT NULL";
            if (identity && !Oracle) definition += " AUTO_INCREMENT";
            return definition;
        }

        private string Q(string name) => sql.DelimitIdentifier(name);
        private string Table(ITable table) => sql.DelimitIdentifier(table.Name, table.Schema);
        private string Columns(IReadOnlyList<IColumn> columns) => string.Join(", ", columns.Select(c => Q(c.Name)));
        private static NotSupportedException Unsupported(string feature)
            => new("OceanBase create scripts do not yet support " + feature + ". Apply that schema feature externally.");
    }
}
