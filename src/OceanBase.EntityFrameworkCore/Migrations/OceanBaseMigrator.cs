using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBase.EntityFrameworkCore.Internal;

namespace OceanBase.EntityFrameworkCore.Migrations
{
#pragma warning disable EF1001 // EF10's Migrator exposes the script-command seam; review on patch upgrades.
    internal sealed class OceanBaseMigrator(
        IMigrationsAssembly migrationsAssembly, IHistoryRepository historyRepository, IDatabaseCreator databaseCreator,
        IMigrationsSqlGenerator migrationsSqlGenerator, IRawSqlCommandBuilder rawSqlCommandBuilder,
        IMigrationCommandExecutor migrationCommandExecutor, IRelationalConnection connection,
        ISqlGenerationHelper sqlGenerationHelper, ICurrentDbContext currentContext,
        IModelRuntimeInitializer modelRuntimeInitializer, IDiagnosticsLogger<DbLoggerCategory.Migrations> logger,
        IRelationalCommandDiagnosticsLogger commandLogger, IDatabaseProvider databaseProvider,
        IMigrationsModelDiffer migrationsModelDiffer, IDesignTimeModel designTimeModel,
        IDbContextOptions contextOptions, IExecutionStrategy executionStrategy, IRelationalTypeMappingSource mappingSource)
        : Migrator(migrationsAssembly, historyRepository, databaseCreator, migrationsSqlGenerator, rawSqlCommandBuilder,
            migrationCommandExecutor, connection, sqlGenerationHelper, currentContext, modelRuntimeInitializer,
            logger, commandLogger, databaseProvider, migrationsModelDiffer, designTimeModel, contextOptions, executionStrategy)
    {
        private readonly IHistoryRepository _historyRepository = historyRepository;
        private readonly IRawSqlCommandBuilder _rawSqlCommandBuilder = rawSqlCommandBuilder;
        private readonly IRelationalConnection _connection = connection;
        private readonly ICurrentDbContext _currentContext = currentContext;
        private readonly IDbContextOptions _contextOptions = contextOptions;
        private readonly IExecutionStrategy _executionStrategy = executionStrategy;
        private readonly IRelationalCommandDiagnosticsLogger _commandLogger = commandLogger;
        private readonly OceanBaseOptionsExtension _options = contextOptions.FindExtension<OceanBaseOptionsExtension>()!;
        private bool Oracle => _options.CompatibilityMode == OceanBaseCompatibilityMode.Oracle;
        private string Probe => Oracle ? "SELECT SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA') FROM DUAL" : "SELECT DATABASE()";
        private RelationalCommandParameterObject Parameters()
            => new(_connection, null, null, _currentContext.Context, _commandLogger, CommandSource.Migrations);

        private void Validate(bool async)
        {
            if (_options.SchemaManagementScope is null)
                throw new NotSupportedException("Migrate requires EnableOceanBaseSchemaManagement for an already provisioned target.");
            if (_connection.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null
                || _connection is ITransactionEnlistmentManager { EnlistedTransaction: not null })
                throw new NotSupportedException("OceanBase migrations cannot run inside a caller, ambient or enlisted transaction.");
            if (_executionStrategy.RetriesOnFailure || ExecutionStrategy.Current is not null)
                throw new NotSupportedException("Automatic migration retries are unsupported because OceanBase DDL commits implicitly. Inspect the applied schema before retrying.");
            var core = _contextOptions.FindExtension<CoreOptionsExtension>();
            if (async ? core?.AsyncSeeder is null && core?.Seeder is not null
                : core?.Seeder is null && core?.AsyncSeeder is not null)
                throw new InvalidOperationException(CoreStrings.MissingSeeder);
        }
        private void ValidateScope(object? scope)
        {
            if (scope is not string name || name != _options.SchemaManagementScope)
                throw new InvalidOperationException("The current namespace differs from the selected migration scope. No migration DDL was executed.");
        }

        public override void Migrate(string? targetMigration = null)
        {
            Validate(async: false);
            var history = _historyRepository as OceanBaseHistoryRepository;
            history?.BeginMigration();
            var opened = false;
            Exception? failure = null;
            try
            {
                _connection.Open(); opened = true;
                ValidateScope(_rawSqlCommandBuilder.Build(Probe).ExecuteScalar(Parameters()));
                base.Migrate(targetMigration);
            }
            catch (Exception error) { failure = error; throw; }
            finally
            {
                Exception? cleanupError = null;
                try { history?.EndMigration(); }
                catch (Exception cleanup) { cleanupError = cleanup; }
                try { if (opened) _connection.Close(); }
                catch (Exception cleanup) { cleanupError ??= cleanup; }
                HandleCleanup(failure, cleanupError);
            }
        }

        public override async Task MigrateAsync(string? targetMigration = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Validate(async: true);
            var history = _historyRepository as OceanBaseHistoryRepository;
            history?.BeginMigration();
            var opened = false;
            Exception? failure = null;
            try
            {
                await _connection.OpenAsync(cancellationToken).ConfigureAwait(false); opened = true;
                ValidateScope(await _rawSqlCommandBuilder.Build(Probe).ExecuteScalarAsync(Parameters(), cancellationToken).ConfigureAwait(false));
                await base.MigrateAsync(targetMigration, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error) { failure = error; throw; }
            finally
            {
                Exception? cleanupError = null;
                try { if (history is not null) await history.EndMigrationAsync().ConfigureAwait(false); }
                catch (Exception cleanup) { cleanupError = cleanup; }
                try { if (opened) await _connection.CloseAsync().ConfigureAwait(false); }
                catch (Exception cleanup) { cleanupError ??= cleanup; }
                HandleCleanup(failure, cleanupError);
            }
        }

        private static void HandleCleanup(Exception? failure, Exception? cleanup)
        {
            if (cleanup is null) return;
            if (failure is not null) failure.Data["OceanBase.MigrationCleanupError"] = cleanup;
            else ExceptionDispatchInfo.Capture(cleanup).Throw();
        }

        public override string GenerateScript(string? fromMigration = null, string? toMigration = null,
            MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default)
        {
            var script = base.GenerateScript(fromMigration, toMigration, options | MigrationsSqlGenerationOptions.NoTransactions);
            if (Oracle && (fromMigration is null or "" || fromMigration == Migration.InitialDatabase))
            {
                var create = _historyRepository.GetCreateIfNotExistsScript();
                if (script.StartsWith(create, StringComparison.Ordinal))
                    script = create + "/\n" + script[create.Length..];
            }
            return Oracle ? "SET DEFINE OFF\nWHENEVER SQLERROR EXIT SQL.SQLCODE ROLLBACK\n" + script + "COMMIT;\n" : script;
        }

        protected override IReadOnlyList<MigrationCommand> GenerateUpSql(Migration migration,
            MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default)
            => ScriptCommands(base.GenerateUpSql(migration, options), options);
        protected override IReadOnlyList<MigrationCommand> GenerateDownSql(Migration migration, Migration? previousMigration,
            MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default)
            => ScriptCommands(base.GenerateDownSql(migration, previousMigration, options), options);

        private IReadOnlyList<MigrationCommand> ScriptCommands(IReadOnlyList<MigrationCommand> commands, MigrationsSqlGenerationOptions options)
        {
            if (!options.HasFlag(MigrationsSqlGenerationOptions.Script) || !Oracle && !options.HasFlag(MigrationsSqlGenerationOptions.Idempotent))
                return commands;
            return commands.Select(command =>
            {
                var sql = command.CommandText.Trim();
                if (!Oracle) sql = sql.TrimEnd(';');
                string body;
                if (Oracle)
                {
                    if (Encoding.UTF8.GetByteCount(sql) > 32767)
                        throw new NotSupportedException("Oracle scripted migration commands cannot exceed 32767 UTF-8 bytes.");
                    // EF indents script lines; encode line breaks so indentation cannot change string data.
                    var literal = sql.Replace("'", "''", StringComparison.Ordinal)
                        .Replace("\r", "' || CHR(13) || '", StringComparison.Ordinal)
                        .Replace("\n", "' || CHR(10) || '", StringComparison.Ordinal);
                    body = "EXECUTE IMMEDIATE '" + literal + "';\n";
                    if (!options.HasFlag(MigrationsSqlGenerationOptions.Idempotent)) body = "BEGIN\n" + body + "END;\n/\n";
                }
                else
                {
                    var literal = mappingSource.GetMapping(typeof(string)).GenerateSqlLiteral(sql);
                    body = "SET @ob_migration_sql = IF(@ob_migration_apply, " + literal + ", 'SELECT 1');\n"
                        + "PREPARE ob_migration_stmt FROM @ob_migration_sql;\nEXECUTE ob_migration_stmt;\nDEALLOCATE PREPARE ob_migration_stmt;\n";
                }
                return new MigrationCommand(_rawSqlCommandBuilder.Build(body), _currentContext.Context, _commandLogger, transactionSuppressed: true);
            }).ToArray();
        }
    }
#pragma warning restore EF1001
}
