using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using OceanBaseConnector;

namespace OceanBase.EntityFrameworkCore.Design
{
    internal sealed class OceanBaseDatabaseModelFactory(ILoggerFactory loggerFactory,
        OceanBaseScaffoldingTypeMappingSource mappings) : DatabaseModelFactory
    {
        private readonly ILogger _logger = loggerFactory.CreateLogger<OceanBaseDatabaseModelFactory>();
        public override DatabaseModel Create(string connectionString, DatabaseModelFactoryOptions options)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
            OceanBaseScaffoldingTypes.Mode(connectionString);
            using var connection = new OceanBaseConnection(connectionString);
            return Create(connection, options);
        }

        public override DatabaseModel Create(DbConnection connection, DatabaseModelFactoryOptions options)
        {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(options);
            var mode = OceanBaseScaffoldingTypes.Mode(connection.ConnectionString);
            var opened = connection.State == ConnectionState.Open;
            if (!opened && connection.State != ConnectionState.Closed)
                throw new InvalidOperationException("Reverse engineering requires an open or closed connection.");
            Exception? failure = null;
            try
            {
                if (!opened) connection.Open();
                if (connection is OceanBaseConnection native && native.CompatibilityMode is { } actual
                    && (actual == OceanBaseConnector.OceanBaseCompatibilityMode.Oracle) != (mode == OceanBaseCompatibilityMode.Oracle))
                    throw new InvalidOperationException("The detected compatibility mode differs from ExpectedMode.");
                var current = connection.Database;
                if (mode == OceanBaseCompatibilityMode.Oracle)
                {
                    using var command = connection.CreateCommand();
                    command.CommandText = "SELECT SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA') FROM DUAL";
                    current = Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture)!;
                }
                if (string.IsNullOrWhiteSpace(current)) throw new InvalidOperationException("Select a current database/schema before reverse engineering.");
                return Read(connection, options, mode, current);
            }
            catch (Exception error) { failure = error; throw; }
            finally
            {
                if (!opened && connection.State != ConnectionState.Closed)
                {
                    try { connection.Close(); }
                    catch (Exception cleanup) when (failure is not null) { failure.Data["OceanBase.ScaffoldingCleanupError"] = cleanup; }
                }
            }
        }

        private DatabaseModel Read(DbConnection connection, DatabaseModelFactoryOptions options, OceanBaseCompatibilityMode mode, string current)
        {
            var schemas = options.Schemas.ToHashSet(StringComparer.Ordinal);
            var requested = options.Tables.Select(ParseTable).Select(t => (Schema: t.Schema ?? current, t.Name)).ToHashSet();
            var namespaces = schemas.Concat(requested.Select(t => t.Schema)).ToHashSet(StringComparer.Ordinal);
            var all = schemas.Count == 0 && requested.Count == 0;
            if (all) namespaces.Add(current);
            var database = new DatabaseModel { DatabaseName = connection.Database, DefaultSchema = current };
            var tables = new Dictionary<(string Schema, string Name), DatabaseTable>();
            foreach (var schema in namespaces.OrderBy(s => s, StringComparer.Ordinal))
            {
                using var catalog = Schema(connection, "Tables", schema, mode);
                foreach (DataRow row in catalog.Rows)
                {
                    var name = Text(row, "TABLE_NAME")!;
                    if (!all && !schemas.Contains(schema) && !requested.Contains((schema, name))) continue;
                    var kind = Text(row, "TABLE_TYPE");
                    DatabaseTable table = kind switch
                    {
                        "TABLE" or "BASE TABLE" => new DatabaseTable(),
                        "VIEW" => new DatabaseView(),
                        _ => throw Unsupported("table kind " + kind)
                    };
                    table.Database = database;
                    table.Schema = schema;
                    table.Name = name;
                    tables.Add((schema, name), table);
                    database.Tables.Add(table);
                }
            }
            foreach (var table in requested)
                if (!tables.ContainsKey(table)) throw new InvalidOperationException("Requested table was not found in the visible catalog: " + table.Schema + "." + table.Name);
            foreach (var schema in schemas)
                if (!tables.Keys.Any(t => t.Schema == schema)) throw new InvalidOperationException("Requested schema has no visible tables/views: " + schema);
            foreach (var schema in namespaces.OrderBy(s => s, StringComparer.Ordinal))
            {
                ReadColumns(connection, mode, schema, tables);
                ReadKeysAndIndexes(connection, mode, schema, tables);
            }
            foreach (var schema in namespaces.OrderBy(s => s, StringComparer.Ordinal)) ReadForeignKeys(connection, mode, schema, tables);
            return database;
        }

        private void ReadColumns(DbConnection connection, OceanBaseCompatibilityMode mode, string schema,
            Dictionary<(string Schema, string Name), DatabaseTable> tables)
        {
            var typeMappings = mappings.ForMode(mode);
            using var rows = Schema(connection, "Columns", schema, mode);
            foreach (var field in new[] { "TYPE_DECLARATION", "IS_IDENTITY", "IS_COMPUTED", "COMPUTED_EXPRESSION", "IS_STORED",
                "HAS_DEFAULT", "DEFAULT_VALUE_SQL", "HAS_ON_UPDATE", "ON_UPDATE_SQL", "COLLATION_NAME", "COLUMN_COMMENT" })
                if (!rows.Columns.Contains(field))
                    throw Unsupported("missing " + field + " in Columns; use the OceanBaseConnector 0.3.0 metadata contract");
            foreach (var group in rows.Rows.Cast<DataRow>().GroupBy(r => Text(r, "TABLE_NAME")!))
            {
                if (!tables.TryGetValue((schema, group.Key), out var table)) continue;
                foreach (var row in group.OrderBy(r => Number(r, "ORDINAL_POSITION")))
                {
                    var name = Text(row, "COLUMN_NAME")!;
                    var store = Text(row, "TYPE_DECLARATION");
                    if (string.IsNullOrWhiteSpace(store)) throw Unsupported("missing TYPE_DECLARATION for " + table.Name + "." + name);
                    var clr = OceanBaseScaffoldingTypes.ClrType(store, mode);
                    _ = typeMappings.FindMapping(clr, store) ?? throw Unsupported("column type " + store);
                    var identity = Flag(row, "IS_IDENTITY", table.Name, name);
                    var computed = Flag(row, "IS_COMPUTED", table.Name, name);
                    var hasDefault = Flag(row, "HAS_DEFAULT", table.Name, name);
                    var hasUpdate = Flag(row, "HAS_ON_UPDATE", table.Name, name);
                    var expression = BehaviorSql(row, "IS_COMPUTED", "COMPUTED_EXPRESSION", computed, table.Name, name);
                    var defaultSql = BehaviorSql(row, "HAS_DEFAULT", "DEFAULT_VALUE_SQL", hasDefault, table.Name, name);
                    _ = BehaviorSql(row, "HAS_ON_UPDATE", "ON_UPDATE_SQL", hasUpdate, table.Name, name);
                    if (hasUpdate == true) throw Unsupported("HAS_ON_UPDATE on " + table.Name + "." + name);
                    if (computed == true && (identity == true || hasDefault == true))
                        throw Unsupported("inconsistent IS_IDENTITY/IS_COMPUTED/HAS_DEFAULT for " + table.Name + "." + name);
                    if (identity == true && clr != typeof(int) && clr != typeof(long))
                        throw Unsupported("identity outside the supported int/long range");
                    if (identity == true && mode == OceanBaseCompatibilityMode.Oracle)
                        WarnIncomplete(table.Name, name, "Oracle identity mode/options");
                    var stored = row.Field<bool?>("IS_STORED");
                    if (computed != true && stored is not null)
                        throw Unsupported("IS_STORED without established IS_COMPUTED on " + table.Name + "." + name);
                    if (computed == true && stored is null) WarnIncomplete(table.Name, name, "IS_STORED");
                    var column = new DatabaseColumn
                    {
                        Table = table, Name = name, StoreType = store,
                        IsNullable = Text(row, "IS_NULLABLE") switch
                        {
                            "YES" => true, "NO" => false, _ => throw Unsupported("unknown IS_NULLABLE for " + table.Name + "." + name)
                        },
                        ComputedColumnSql = expression, IsStored = stored, DefaultValueSql = defaultSql,
                        DefaultValue = hasDefault == true && defaultSql is null ? DBNull.Value : null,
                        ValueGenerated = computed == true ? ValueGenerated.OnAddOrUpdate : identity == true ? ValueGenerated.OnAdd : null,
                        Comment = Text(row, "COLUMN_COMMENT") is { Length: > 0 } comment ? comment : null,
                        Collation = Text(row, "COLLATION_NAME")
                    };
                    column["ClrType"] = clr;
                    table.Columns.Add(column);
                }
            }
            foreach (var table in tables.Where(t => t.Key.Schema == schema).Select(t => t.Value))
                if (table.Columns.Count == 0) throw new InvalidOperationException("No visible columns for selected table " + schema + "." + table.Name);
        }

        private bool? Flag(DataRow row, string field, string table, string column)
        {
            var value = row.Field<bool?>(field);
            if (value is null) WarnIncomplete(table, column, field);
            return value;
        }

        private string? BehaviorSql(DataRow row, string flag, string field, bool? present, string table, string column)
        {
            var sql = Text(row, field);
            if (present != true && !string.IsNullOrWhiteSpace(sql))
                throw Unsupported("inconsistent " + flag + "/" + field + " on " + table + "." + column);
            if (present != true) return null;
            if (!string.IsNullOrWhiteSpace(sql)) return sql;
            WarnIncomplete(table, column, field);
            // EF scaffolding accepts an empty computed expression; unknown defaults use DBNull.Value instead.
            return field == "COMPUTED_EXPRESSION" ? "" : null;
        }

        private void WarnIncomplete(string table, string column, string field)
            => _logger.LogWarning("Column {Table}.{Column} has unavailable {Field} metadata. Review generation/default configuration before writes or schema recreation.", table, column, field);

        private void ReadKeysAndIndexes(DbConnection connection, OceanBaseCompatibilityMode mode, string schema,
            Dictionary<(string Schema, string Name), DatabaseTable> tables)
        {
            using var keys = Schema(connection, "Constraints", schema, mode);
            using var columns = Schema(connection, "ConstraintColumns", schema, mode);
            foreach (DataRow row in keys.Rows)
            {
                if (!tables.TryGetValue((schema, Text(row, "TABLE_NAME")!), out var table) || table is DatabaseView) continue;
                var name = Text(row, "CONSTRAINT_NAME")!;
                var kind = Text(row, "CONSTRAINT_TYPE");
                if (kind == "CHECK") { _logger.LogWarning("CHECK constraint {Constraint} on {Table} is not represented by EF reverse engineering.", name, table.Name); continue; }
                if (kind is not ("PRIMARY KEY" or "UNIQUE")) continue;
                var keyColumns = Ordered(columns, "CONSTRAINT_NAME", name, table.Name).Select(r => Column(table, Text(r, "COLUMN_NAME")!)).ToArray();
                if (keyColumns.Length == 0) throw Unsupported("key without visible columns");
                if (kind == "PRIMARY KEY")
                {
                    if (table.PrimaryKey is not null) throw Unsupported("multiple primary keys");
                    table.PrimaryKey = new DatabasePrimaryKey { Table = table, Name = name };
                    foreach (var column in keyColumns) table.PrimaryKey.Columns.Add(column);
                }
                else
                {
                    var key = new DatabaseUniqueConstraint { Table = table, Name = name };
                    foreach (var column in keyColumns) key.Columns.Add(column);
                    table.UniqueConstraints.Add(key);
                }
            }
            using var indexes = Schema(connection, "Indexes", schema, mode);
            using var indexColumns = Schema(connection, "IndexColumns", schema, mode);
            foreach (DataRow row in indexes.Rows)
            {
                if (!tables.TryGetValue((schema, Text(row, "TABLE_NAME")!), out var table) || table is DatabaseView) continue;
                var name = Text(row, "INDEX_NAME")!;
                var kind = Text(row, "INDEX_TYPE");
                if (kind is not ("BTREE" or "NORMAL")) throw Unsupported("index type " + kind);
                var members = Ordered(indexColumns, "INDEX_NAME", name, table.Name).ToArray();
                if (members.Length == 0 || members.Any(r => Text(r, "COLUMN_NAME") is null || Text(r, "PREFIX_LENGTH") is not null))
                    throw Unsupported("empty, expression or prefix index " + name);
                var mapped = members.Select(r => Column(table, Text(r, "COLUMN_NAME")!)).ToArray();
                var unique = Text(row, "IS_UNIQUE") switch { "YES" => true, "NO" => false, _ => throw Unsupported("unknown index uniqueness") };
                if (unique && (table.PrimaryKey is { } primary && primary.Columns.SequenceEqual(mapped)
                        && (primary.Name == name || mode == OceanBaseCompatibilityMode.MySql && name == "PRIMARY")
                    || table.UniqueConstraints.Any(k => k.Name == name && k.Columns.SequenceEqual(mapped)))) continue;
                var index = new DatabaseIndex { Table = table, Name = name, IsUnique = unique };
                foreach (var column in mapped) index.Columns.Add(column);
                foreach (var member in members)
                {
                    var direction = Text(member, "SORT_ORDER");
                    if (direction is not ("ASC" or "DESC")) throw Unsupported("unknown index direction");
                    index.IsDescending.Add(direction == "DESC");
                }
                table.Indexes.Add(index);
            }
        }

        private void ReadForeignKeys(DbConnection connection, OceanBaseCompatibilityMode mode, string schema,
            Dictionary<(string Schema, string Name), DatabaseTable> tables)
        {
            using var foreigns = Schema(connection, "ForeignKeys", schema, mode);
            using var columns = Schema(connection, "ForeignKeyColumns", schema, mode);
            foreach (DataRow row in foreigns.Rows)
            {
                if (!tables.TryGetValue((schema, Text(row, "TABLE_NAME")!), out var table) || table is DatabaseView) continue;
                var name = Text(row, "CONSTRAINT_NAME")!;
                var principalSchema = Text(row, mode == OceanBaseCompatibilityMode.MySql ? "REFERENCED_TABLE_CATALOG" : "REFERENCED_TABLE_SCHEMA")!;
                var principalName = Text(row, "REFERENCED_TABLE_NAME")!;
                if (!tables.TryGetValue((principalSchema, principalName), out var principal))
                {
                    _logger.LogWarning("Foreign key {Constraint} references unselected table {Schema}.{Table}; no relationship is generated.", name, principalSchema, principalName);
                    continue;
                }
                if (Text(row, "UPDATE_RULE") is not ("NO ACTION" or "RESTRICT")) throw Unsupported("foreign-key update actions");
                var key = new DatabaseForeignKey { Table = table, PrincipalTable = principal, Name = name,
                    OnDelete = Text(row, "DELETE_RULE") switch
                    {
                        "NO ACTION" => ReferentialAction.NoAction, "RESTRICT" => ReferentialAction.Restrict,
                        "CASCADE" => ReferentialAction.Cascade, "SET NULL" => ReferentialAction.SetNull,
                        "SET DEFAULT" => ReferentialAction.SetDefault, _ => throw Unsupported("unknown foreign-key delete action")
                    } };
                foreach (var member in Ordered(columns, "CONSTRAINT_NAME", name, table.Name))
                {
                    if (Text(member, "REFERENCED_TABLE_NAME") != principalName
                        || Text(member, mode == OceanBaseCompatibilityMode.MySql ? "REFERENCED_TABLE_CATALOG" : "REFERENCED_TABLE_SCHEMA") != principalSchema)
                        throw Unsupported("inconsistent foreign-key targets");
                    key.Columns.Add(Column(table, Text(member, "COLUMN_NAME")!));
                    key.PrincipalColumns.Add(Column(principal, Text(member, "REFERENCED_COLUMN_NAME")!));
                }
                if (key.Columns.Count == 0) throw Unsupported("foreign key without visible columns");
                table.ForeignKeys.Add(key);
            }
        }

        private static DataTable Schema(DbConnection connection, string collection, string schema, OceanBaseCompatibilityMode mode)
        {
            var rows = connection.GetSchema(collection, mode == OceanBaseCompatibilityMode.MySql ? [schema, null] : [null, schema]);
            if (rows.Rows.Cast<DataRow>().Any(r => Text(r, mode == OceanBaseCompatibilityMode.MySql ? "TABLE_CATALOG" : "TABLE_SCHEMA") != schema))
            {
                rows.Dispose();
                throw Unsupported("catalog rows outside the requested namespace");
            }
            return rows;
        }
        private static IEnumerable<DataRow> Ordered(DataTable rows, string key, string name, string table)
        {
            var members = rows.Rows.Cast<DataRow>().Where(r => Text(r, key) == name && Text(r, "TABLE_NAME") == table)
                .OrderBy(r => Number(r, "ORDINAL_POSITION")).ToArray();
            if (members.Where((r, i) => Number(r, "ORDINAL_POSITION") != i + 1).Any())
                throw Unsupported("missing or duplicate key/index ordinals for " + table + "." + name);
            return members;
        }
        private static DatabaseColumn Column(DatabaseTable table, string name)
            => table.Columns.SingleOrDefault(c => c.Name == name) ?? throw Unsupported("missing catalog column " + table.Name + "." + name);
        private static string? Text(DataRow row, string name) => row[name] is DBNull ? null : Convert.ToString(row[name], CultureInfo.InvariantCulture);
        private static int? Number(DataRow row, string name) => row[name] is DBNull ? null : Convert.ToInt32(row[name], CultureInfo.InvariantCulture);
        private static NotSupportedException Unsupported(string feature) => new("OceanBase reverse engineering does not support " + feature + ".");

        private static (string? Schema, string Name) ParseTable(string value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            var parts = new List<string>();
            var part = new StringBuilder();
            var quote = '\0';
            var quoted = false;
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (quote != '\0')
                {
                    if (c != quote) part.Append(c);
                    else if (i + 1 < value.Length && value[i + 1] == quote) { part.Append(c); i++; }
                    else { quote = '\0'; quoted = true; }
                }
                else if (c == '.') { parts.Add(quoted ? part.ToString() : part.ToString().Trim()); part.Clear(); quoted = false; }
                else if (c is '"' or '`')
                {
                    if (part.ToString().Trim().Length != 0 || quoted) throw new ArgumentException("Invalid table selector.");
                    part.Clear(); quote = c;
                }
                else if (quoted && !char.IsWhiteSpace(c)) throw new ArgumentException("Invalid table selector.");
                else if (!quoted) part.Append(c);
            }
            parts.Add(quoted ? part.ToString() : part.ToString().Trim());
            if (quote != '\0' || parts.Count is < 1 or > 2 || parts.Any(p => p.Length == 0 || p.Contains('\0')))
                throw new ArgumentException("Use a table name or a schema-qualified table name.");
            return parts.Count == 1 ? (null, parts[0]) : (parts[0], parts[1]);
        }
    }
}
