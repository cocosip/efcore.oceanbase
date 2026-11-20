using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBase.EntityFrameworkCore.Internal;
using OceanBase.EntityFrameworkCore.Storage;

namespace OceanBase.EntityFrameworkCore.Migrations
{
    internal sealed class OceanBaseHistoryRepository(HistoryRepositoryDependencies dependencies) : HistoryRepository(dependencies)
    {
        private OceanBaseOptionsExtension Options => Dependencies.Options.FindExtension<OceanBaseOptionsExtension>()!;
        private bool Oracle => Options.CompatibilityMode == OceanBaseCompatibilityMode.Oracle;
        private string Q(string name) => SqlGenerationHelper.DelimitIdentifier(name);
        private string Table => SqlGenerationHelper.DelimitIdentifier(TableName, TableSchema);
        private string Literal(string value) => Dependencies.TypeMappingSource.GetMapping(typeof(string)).GenerateSqlLiteral(value);

        private void ValidateHistoryScope()
        {
            if (Options.SchemaManagementScope is { } scope && TableSchema is not null && TableSchema != scope)
                throw new NotSupportedException("The migrations history table must be in the selected schema management scope.");
        }

        protected override string ExistsSql
        {
            get
            {
                ValidateHistoryScope();
                return Oracle
                    ? "SELECT COUNT(*) " + OracleTableCatalog(TableName)
                    : "SELECT COUNT(*) FROM information_schema.tables WHERE BINARY TABLE_SCHEMA = BINARY "
                        + (TableSchema is null ? "DATABASE()" : Literal(TableSchema))
                        + " AND ((@@lower_case_table_names = 0 AND BINARY TABLE_NAME = BINARY " + Literal(TableName)
                        + ") OR (@@lower_case_table_names <> 0 AND LOWER(TABLE_NAME) = LOWER(" + Literal(TableName)
                        + "))) AND TABLE_TYPE = 'BASE TABLE'";
            }
        }

        private string OracleTableCatalog(string name)
            => "FROM ALL_TABLES WHERE OWNER = "
                + (TableSchema is null ? "SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA')" : Literal(TableSchema))
                + " AND TABLE_NAME = " + Literal(name);

        protected override bool InterpretExistsResult(object? value)
            => value is null or DBNull ? throw new InvalidOperationException("The migrations history catalog returned no count.")
                : (long)OceanBaseNumericConversion.ConvertValue(value, typeof(long)) > 0;

        protected override string GetAppliedMigrationsSql => NativeHistorySql(base.GetAppliedMigrationsSql);
        public override string GetInsertScript(HistoryRow row) => NativeHistorySql(base.GetInsertScript(row));
        public override string GetDeleteScript(string migrationId) => NativeHistorySql(base.GetDeleteScript(migrationId));
        private string NativeHistorySql(string sql)
        {
            ValidateHistoryScope();
            return Oracle ? sql.TrimEnd().TrimEnd(';') : sql;
        }

        public override string GetCreateIfNotExistsScript()
        {
            ValidateHistoryScope();
            var sql = GetCreateScript();
            return Oracle
                ? "DECLARE ob_count NUMBER;\nBEGIN\n    SELECT COUNT(*) INTO ob_count " + OracleTableCatalog(TableName)
                    + ";\n    IF ob_count = 0 THEN\n        EXECUTE IMMEDIATE '"
                    + sql.Trim().TrimEnd(';').Replace("'", "''", StringComparison.Ordinal)
                    + "';\n    END IF;\nEND;\n"
                : "CREATE TABLE IF NOT EXISTS " + sql["CREATE TABLE ".Length..];
        }

        public override string GetBeginIfNotExistsScript(string migrationId) => Condition(migrationId, exists: false);
        public override string GetBeginIfExistsScript(string migrationId) => Condition(migrationId, exists: true);
        private string Condition(string migrationId, bool exists)
        {
            var query = "SELECT COUNT(*)" + (Oracle ? " INTO ob_count" : "") + " FROM " + Table
                + " WHERE " + Q(MigrationIdColumnName) + " = " + Literal(migrationId);
            return Oracle
                ? "DECLARE ob_count NUMBER;\nBEGIN\n    " + query + ";\n    IF ob_count " + (exists ? "> 0" : "= 0") + " THEN"
                : "SET @ob_migration_apply = ((" + query + ") " + (exists ? "> 0" : "= 0") + ");";
        }
        public override string GetEndIfScript() => Oracle ? "    END IF;\nEND;\n/\n" : "";
    }
}
