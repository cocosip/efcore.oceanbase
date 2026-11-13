using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace OceanBase.EntityFrameworkCore.TestUtilities;

internal sealed class ScaffoldingCatalogConnection(OceanBaseCompatibilityMode mode) : DbConnection
{
    private ConnectionState _state;
    [AllowNull] public override string ConnectionString { get; set; } = "Server=offline;Database=app;ExpectedMode=" + mode;
    public override string Database => "app";
    public override string DataSource => "offline";
    public override string ServerVersion => "offline";
    public override ConnectionState State => _state;
    public int Opens { get; private set; }
    public int Closes { get; private set; }
    public int Disposals { get; private set; }
    public bool CatalogFailure { get; init; }
    public bool UnknownChildType { get; init; }
    public bool Driver030Profile { get; init; }
    public List<string> Collections { get; } = [];
    public Action<string, DataTable>? Rewrite { get; init; }
    public List<string?[]> Restrictions { get; } = [];
    public List<(string Sql, object?[] Values)> Commands { get; } = [];
    public override void Open() { Opens++; _state = ConnectionState.Open; }
    public override void Close() { Closes++; _state = ConnectionState.Closed; }
    public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
    protected override DbCommand CreateDbCommand() => new CatalogCommand(this);
    protected override void Dispose(bool disposing) { if (disposing) Disposals++; base.Dispose(disposing); }
    public override DataTable GetSchema(string collectionName, string?[]? restrictionValues)
    {
        if (CatalogFailure) throw new InvalidOperationException("Synthetic catalog failure.");
        Restrictions.Add(restrictionValues!);
        Collections.Add(collectionName);
        var prefix = mode == OceanBaseCompatibilityMode.MySql ? new object?[] { "app", null } : [null, "app"];
        DataTable Rows(string columns, params object?[][] rows)
            => Table("TABLE_CATALOG TABLE_SCHEMA TABLE_NAME " + columns, rows.Select(r => prefix.Concat(r).ToArray()).ToArray());
        var result = collectionName switch
        {
            "Columns" => Columns(),
            "Tables" => Rows("TABLE_TYPE", ["Parents", "TABLE"], ["Children", "TABLE"], ["Recent", "VIEW"]),
            "Keys" or "Constraints" => Rows("CONSTRAINT_SCHEMA CONSTRAINT_NAME CONSTRAINT_TYPE",
                ["Parents", null, "PK_Parents", "PRIMARY KEY"], ["Parents", null, "UQ_Name", "UNIQUE"],
                ["Children", null, "PK_Children", "PRIMARY KEY"]),
            "ConstraintColumns" => Rows("CONSTRAINT_SCHEMA CONSTRAINT_NAME COLUMN_NAME ORDINAL_POSITION",
                ["Parents", null, "PK_Parents", "Id", 1], ["Parents", null, "UQ_Name", "Name", 1],
                ["Children", null, "PK_Children", "Seq", 2], ["Children", null, "PK_Children", "ParentId", 1]),
            "Indexes" => Rows("INDEX_SCHEMA INDEX_NAME INDEX_TYPE IS_UNIQUE",
                ["Parents", null, "PK_Parents", "BTREE", "YES"], ["Parents", null, "UQ_Name", "BTREE", "YES"],
                ["Children", null, "IX_Note", mode == OceanBaseCompatibilityMode.MySql ? "BTREE" : "NORMAL", "NO"]),
            "IndexColumns" => Rows("INDEX_SCHEMA INDEX_NAME COLUMN_NAME ORDINAL_POSITION SORT_ORDER PREFIX_LENGTH",
                ["Parents", null, "PK_Parents", "Id", 1, "ASC", null], ["Parents", null, "UQ_Name", "Name", 1, "ASC", null],
                ["Children", null, "IX_Note", "Seq", 2, "ASC", null], ["Children", null, "IX_Note", "Note", 1, "DESC", null]),
            "ForeignKeys" => Rows("CONSTRAINT_SCHEMA CONSTRAINT_NAME REFERENCED_TABLE_CATALOG REFERENCED_TABLE_SCHEMA REFERENCED_TABLE_NAME REFERENCED_CONSTRAINT_SCHEMA REFERENCED_CONSTRAINT_NAME UPDATE_RULE DELETE_RULE",
                ["Children", null, "FK_Parent", prefix[0], prefix[1], "Parents", null, "PK_Parents", "NO ACTION", "CASCADE"]),
            "ForeignKeyColumns" => Rows("CONSTRAINT_SCHEMA CONSTRAINT_NAME COLUMN_NAME ORDINAL_POSITION REFERENCED_TABLE_CATALOG REFERENCED_TABLE_SCHEMA REFERENCED_TABLE_NAME REFERENCED_COLUMN_NAME",
                ["Children", null, "FK_Parent", "ParentId", 1, prefix[0], prefix[1], "Parents", "Id"]),
            _ => throw new NotSupportedException(collectionName)
        };
        Rewrite?.Invoke(collectionName, result);
        return result;
    }

    // Fully populated rows exercise the consumer contract; Driver030Profile reproduces documented gaps.
    public DataTable Columns()
    {
        var mysql = mode == OceanBaseCompatibilityMode.MySql;
        var schema = mysql ? new object?[] { "app", null } : [null, "app"];
        var rows = Table("TABLE_CATALOG TABLE_SCHEMA TABLE_NAME COLUMN_NAME ORDINAL_POSITION COLUMN_DEFAULT IS_NULLABLE DATA_TYPE CHARACTER_MAXIMUM_LENGTH NUMERIC_PRECISION NUMERIC_SCALE DATETIME_PRECISION TYPE_DECLARATION IS_IDENTITY IS_COMPUTED COMPUTED_EXPRESSION IS_STORED HAS_DEFAULT DEFAULT_VALUE_SQL HAS_ON_UPDATE ON_UPDATE_SQL COLLATION_NAME COLUMN_COMMENT");
        void Add(string table, string name, int ordinal, string store, bool nullable, bool identity = false,
            string? computed = null, string? defaultSql = null)
        {
            var rawDefault = computed is not null && !mysql ? computed : defaultSql;
            rows.Rows.Add(schema.Concat(new object?[] { table, name, ordinal, rawDefault, nullable ? "YES" : "NO",
                store.Split('(')[0], null, null, null, null, store, identity, computed is not null, computed,
                computed is null ? null : false, defaultSql is not null, defaultSql, false, null,
                mysql && store.StartsWith("varchar", StringComparison.Ordinal) ? "utf8mb4_bin" : null, "" })
                .Select(v => v ?? DBNull.Value).ToArray());
        }
        Add("Parents", "Id", 1, mysql ? "int" : "NUMBER(10,0)", false, identity: true);
        Add("Parents", "Name", 2, mysql ? "varchar(40)" : "VARCHAR2(40 CHAR)", false, defaultSql: "'default'");
        Add("Children", "ParentId", 1, mysql ? "int" : "NUMBER(10,0)", false);
        Add("Children", "Seq", 2, mysql ? "int" : "NUMBER(9,0)", false);
        Add("Children", "Note", 3, UnknownChildType ? (mysql ? "geometry" : "OBJECT") : mysql ? "varchar(40)" : "VARCHAR2(40 CHAR)", true);
        Add("Children", "Total", 4, mysql ? "int" : "NUMBER(9,0)", true, computed: "1 + 1");
        Add("Recent", "Name", 1, mysql ? "varchar(40)" : "VARCHAR2(40 CHAR)", true);
        if (Driver030Profile)
        {
            foreach (DataRow row in rows.Rows)
            {
                if (mysql)
                {
                    if (!(bool)row["IS_COMPUTED"]) row["HAS_DEFAULT"] = row.IsNull("COLUMN_DEFAULT") ? DBNull.Value : true;
                    if (!row.IsNull("COLUMN_DEFAULT")) row["DEFAULT_VALUE_SQL"] = DBNull.Value;
                }
                else
                {
                    row["IS_IDENTITY"] = DBNull.Value;
                    row["HAS_ON_UPDATE"] = DBNull.Value;
                    if (!(bool)row["IS_COMPUTED"]) { row["IS_COMPUTED"] = DBNull.Value; row["HAS_DEFAULT"] = DBNull.Value; row["DEFAULT_VALUE_SQL"] = DBNull.Value; }
                }
            }
        }
        return rows;
    }

    private sealed class CatalogCommand(ScaffoldingCatalogConnection connection) : DbCommand
    {
        private readonly OceanBaseConnector.OceanBaseCommand _parameters = new();
        [AllowNull] public override string CommandText { get; set; } = "";
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; } = connection;
        protected override DbTransaction? DbTransaction { get; set; }
        protected override DbParameterCollection DbParameterCollection => _parameters.Parameters;
        protected override DbParameter CreateDbParameter() => new OceanBaseConnector.OceanBaseParameter();
        public override void Cancel() => throw new NotSupportedException();
        public override void Prepare() => throw new NotSupportedException();
        public override int ExecuteNonQuery() => throw new NotSupportedException();
        public override object ExecuteScalar() => "app";
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
            => throw new InvalidOperationException("Column metadata must use GetSchema, not provider catalog SQL.");
        protected override void Dispose(bool disposing) { if (disposing) _parameters.Dispose(); base.Dispose(disposing); }
    }

    private static DataTable Table(string columns, params object?[][] rows)
    {
        var table = new DataTable();
        var columnMetadata = columns.Contains("TYPE_DECLARATION", StringComparison.Ordinal);
        foreach (var column in columns.Split(' '))
            table.Columns.Add(column, column is "IS_IDENTITY" or "IS_COMPUTED" or "IS_STORED" or "HAS_DEFAULT" or "HAS_ON_UPDATE" ? typeof(bool)
                : column is "ORDINAL_POSITION" or "NUMERIC_PRECISION" or "NUMERIC_SCALE" or "DATETIME_PRECISION" ? typeof(int)
                : column == "CHARACTER_MAXIMUM_LENGTH" ? typeof(long) : columnMetadata ? typeof(string) : typeof(object));
        foreach (var row in rows) table.Rows.Add(row.Select(v => v ?? DBNull.Value).ToArray());
        return table;
    }
}
