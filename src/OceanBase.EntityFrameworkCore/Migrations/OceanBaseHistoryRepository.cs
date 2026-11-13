using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBase.EntityFrameworkCore.Internal;
using OceanBase.EntityFrameworkCore.Storage;
using OceanBaseConnector;

namespace OceanBase.EntityFrameworkCore.Migrations
{
    internal sealed class OceanBaseHistoryRepository(HistoryRepositoryDependencies dependencies) : HistoryRepository(dependencies)
    {
        private bool _migrationOwnsLock;
        private DatabaseLock? _lock;
        private OceanBaseOptionsExtension Options => Dependencies.Options.FindExtension<OceanBaseOptionsExtension>()!;
        private bool Oracle => Options.CompatibilityMode == OceanBaseCompatibilityMode.Oracle;
        public override LockReleaseBehavior LockReleaseBehavior => LockReleaseBehavior.Explicit;
        private string Q(string name) => SqlGenerationHelper.DelimitIdentifier(name);
        private string Table => SqlGenerationHelper.DelimitIdentifier(TableName, TableSchema);
        private string LockTable => SqlGenerationHelper.DelimitIdentifier(TableName + "Lock", TableSchema);
        private string Literal(string value) => Dependencies.TypeMappingSource.GetMapping(typeof(string)).GenerateSqlLiteral(value);

        private void ValidateHistoryScope()
        {
            if (Options.SchemaManagementScope is { } scope && TableSchema is not null && TableSchema != scope)
                throw new NotSupportedException("The migrations history table must be in the selected schema management scope.");
            if (Encoding.UTF8.GetByteCount(TableName + "Lock") > (Oracle ? 128 : 64))
                throw new NotSupportedException("The migrations history table name is too long for its lock table.");
        }

        protected override string ExistsSql
        {
            get
            {
                ValidateHistoryScope();
                return Oracle
                    ? "SELECT COUNT(*) FROM ALL_TABLES WHERE OWNER = " + (TableSchema is null ? "SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA')" : Literal(TableSchema))
                        + " AND TABLE_NAME = " + Literal(TableName)
                    : "SELECT COUNT(*) FROM information_schema.tables WHERE BINARY TABLE_SCHEMA = BINARY "
                        + (TableSchema is null ? "DATABASE()" : Literal(TableSchema))
                        + " AND ((@@lower_case_table_names = 0 AND BINARY TABLE_NAME = BINARY " + Literal(TableName)
                        + ") OR (@@lower_case_table_names <> 0 AND LOWER(TABLE_NAME) = LOWER(" + Literal(TableName)
                        + "))) AND TABLE_TYPE = 'BASE TABLE'";
            }
        }

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
            return CreateIfMissing(GetCreateScript());
        }

        private string CreateIfMissing(string sql)
            => Oracle
                ? "BEGIN\n    EXECUTE IMMEDIATE '" + sql.Trim().TrimEnd(';').Replace("'", "''", StringComparison.Ordinal)
                    + "';\nEXCEPTION WHEN OTHERS THEN\n    IF SQLCODE != -955 THEN RAISE; END IF;\nEND;\n"
                : "CREATE TABLE IF NOT EXISTS " + sql["CREATE TABLE ".Length..];

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

        private RelationalCommandParameterObject Parameters()
            => new(Dependencies.Connection, null, null, Dependencies.CurrentContext.Context, Dependencies.CommandLogger, CommandSource.Migrations);

        private string LockCreateScript()
        {
            ValidateHistoryScope();
            return CreateIfMissing("CREATE TABLE " + LockTable + " (" + Q("Id")
                + (Oracle ? " NUMBER(10)" : " INT") + " NOT NULL PRIMARY KEY, " + Q("Owner")
                + (Oracle ? " VARCHAR2(36)" : " VARCHAR(36)") + " NOT NULL);\n");
        }
        private string ClaimScript(string owner)
            => "INSERT INTO " + LockTable + " (" + Q("Id") + ", " + Q("Owner") + ") SELECT 1, " + Literal(owner)
                + " FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM " + LockTable + " WHERE " + Q("Id") + " = 1)";
        private static InvalidOperationException Busy(Exception? inner = null)
            => new("Another migrator owns the migrations lock. After a crash, verify that no migrator is running before clearing the lock table.", inner);

        internal void BeginMigration()
        {
            if (_migrationOwnsLock) throw new InvalidOperationException("A migration is already running on this context.");
            _migrationOwnsLock = true;
        }

        internal void EndMigration()
        {
            _migrationOwnsLock = false;
            try { _lock?.Dispose(); }
            finally { _lock = null; }
        }

        internal async ValueTask EndMigrationAsync()
        {
            _migrationOwnsLock = false;
            try { if (_lock is { } held) await held.DisposeAsync().ConfigureAwait(false); }
            finally { _lock = null; }
        }

        public override IMigrationsDatabaseLock AcquireDatabaseLock()
        {
            if (_migrationOwnsLock && _lock is not null) return _lock;
            Dependencies.RawSqlCommandBuilder.Build(LockCreateScript()).ExecuteNonQuery(Parameters());
            var owner = Guid.NewGuid().ToString();
            _lock = new DatabaseLock(this, owner);
            try
            {
                if (Dependencies.RawSqlCommandBuilder.Build(ClaimScript(owner)).ExecuteNonQuery(Parameters()) != 1) throw Busy();
            }
            catch (OceanBaseException error) when (error.ServerErrorCode == (Oracle ? 1 : 1062)) { throw Busy(error); }
            return _lock;
        }

        public override async Task<IMigrationsDatabaseLock> AcquireDatabaseLockAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_migrationOwnsLock && _lock is not null) return _lock;
            await Dependencies.RawSqlCommandBuilder.Build(LockCreateScript()).ExecuteNonQueryAsync(Parameters(), cancellationToken).ConfigureAwait(false);
            var owner = Guid.NewGuid().ToString();
            _lock = new DatabaseLock(this, owner);
            try
            {
                if (await Dependencies.RawSqlCommandBuilder.Build(ClaimScript(owner)).ExecuteNonQueryAsync(Parameters(), cancellationToken).ConfigureAwait(false) != 1)
                    throw Busy();
            }
            catch (OceanBaseException error) when (error.ServerErrorCode == (Oracle ? 1 : 1062)) { throw Busy(error); }
            return _lock;
        }

        private sealed class DatabaseLock(OceanBaseHistoryRepository repository, string owner) : IMigrationsDatabaseLock
        {
            private bool _disposed;
            IHistoryRepository IMigrationsDatabaseLock.HistoryRepository => repository;
            private string ReleaseSql => "DELETE FROM " + repository.LockTable + " WHERE " + repository.Q("Id")
                + " = 1 AND " + repository.Q("Owner") + " = " + repository.Literal(owner);
            public void Dispose()
            {
                // The outer migrator owns cleanup, including failures before EF's execution finally is entered.
                if (_disposed || repository._migrationOwnsLock) return;
                repository.Dependencies.RawSqlCommandBuilder.Build(ReleaseSql).ExecuteNonQuery(repository.Parameters());
                _disposed = true;
            }
            public async ValueTask DisposeAsync()
            {
                if (_disposed || repository._migrationOwnsLock) return;
                await repository.Dependencies.RawSqlCommandBuilder.Build(ReleaseSql)
                    .ExecuteNonQueryAsync(repository.Parameters(), CancellationToken.None).ConfigureAwait(false);
                _disposed = true;
            }
        }
    }
}
