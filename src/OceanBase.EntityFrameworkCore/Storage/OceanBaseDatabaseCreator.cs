using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBaseConnector;

namespace OceanBase.EntityFrameworkCore.Storage
{
    internal sealed class OceanBaseDatabaseCreator(
        IRelationalConnection connection, IRawSqlCommandBuilder commands, ICurrentDbContext currentContext,
        IRelationalCommandDiagnosticsLogger logger, IDbContextOptions options) : IRelationalDatabaseCreator
    {
        private readonly OceanBaseCompatibilityMode _mode = options.FindExtension<Internal.OceanBaseOptionsExtension>()!.CompatibilityMode;

        public bool Exists()
        {
            var opened = false;
            try
            {
                connection.Open(errorsExpected: true);
                opened = true;
                return true;
            }
            catch (OceanBaseException error) when (_mode == OceanBaseCompatibilityMode.MySql && error.ServerErrorCode == 1049)
            {
                return false;
            }
            finally
            {
                if (opened) connection.Close();
            }
        }

        public async Task<bool> ExistsAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var opened = false;
            try
            {
                await connection.OpenAsync(cancellationToken, errorsExpected: true).ConfigureAwait(false);
                opened = true;
                return true;
            }
            catch (OceanBaseException error) when (_mode == OceanBaseCompatibilityMode.MySql && error.ServerErrorCode == 1049)
            {
                return false;
            }
            finally
            {
                if (opened) await connection.CloseAsync().ConfigureAwait(false);
            }
        }

        public bool CanConnect()
        {
            try { return Exists(); }
            catch (Exception error) when (error is not OperationCanceledException) { return false; }
        }

        public async Task<bool> CanConnectAsync(CancellationToken cancellationToken = default)
        {
            try { return await ExistsAsync(cancellationToken).ConfigureAwait(false); }
            catch (Exception error) when (error is not OperationCanceledException) { return false; }
        }

        private IRelationalCommand TableProbe() => commands.Build(_mode == OceanBaseCompatibilityMode.MySql
            ? "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_type = 'BASE TABLE'"
            : "SELECT COUNT(*) FROM ALL_TABLES WHERE OWNER = SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA')");

        private RelationalCommandParameterObject Parameters()
            => new(connection, null, null, currentContext.Context, logger, CommandSource.Unknown);

        public bool HasTables() => ReadTableCount(TableProbe().ExecuteScalar(Parameters()));

        public async Task<bool> HasTablesAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ReadTableCount(await TableProbe().ExecuteScalarAsync(Parameters(), cancellationToken).ConfigureAwait(false));
        }

        private static bool ReadTableCount(object? value)
            => value is null or DBNull
                ? throw new InvalidOperationException("The OceanBase table probe returned no count.")
                : (long)OceanBaseNumericConversion.ConvertValue(value, typeof(long)) > 0;

        private static NotSupportedException SchemaNotSupported()
            => new("OceanBase database/user creation and deletion are not implemented. Provision the database/schema externally; opt in to existing-schema table creation with EnableOceanBaseSchemaManagement.");

        public void Create() => throw SchemaNotSupported();
        public Task CreateAsync(CancellationToken cancellationToken = default) => throw SchemaNotSupported();
        public void Delete() => throw SchemaNotSupported();
        public Task DeleteAsync(CancellationToken cancellationToken = default) => throw SchemaNotSupported();
        public void CreateTables() => CreateTables(onlyIfEmpty: false);
        public async Task CreateTablesAsync(CancellationToken cancellationToken = default)
            => await CreateTablesAsync(onlyIfEmpty: false, cancellationToken).ConfigureAwait(false);

        public string GenerateCreateScript()
        {
            var statements = CreateStatements(currentContext.Context.GetService<IDesignTimeModel>().Model);
            return string.Join(";\n\n", statements) + (statements.Count > 0 ? ";\n" : "");
        }

        public bool EnsureCreated() => CreateTables(onlyIfEmpty: true);
        public Task<bool> EnsureCreatedAsync(CancellationToken cancellationToken = default)
            => CreateTablesAsync(onlyIfEmpty: true, cancellationToken);

        private IReadOnlyList<string> CreateStatements(IModel model)
            => new OceanBaseSchemaSqlGenerator(currentContext.Context.GetService<ISqlGenerationHelper>(), _mode).Generate(model);

        private IReadOnlyList<string> ValidateTableCreation()
        {
            var scope = options.FindExtension<Internal.OceanBaseOptionsExtension>()!.SchemaManagementScope;
            if (scope is null)
            {
                throw new NotSupportedException("Explicitly opt in with EnableOceanBaseSchemaManagement before creating tables.");
            }
            if (connection.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null
                || connection is ITransactionEnlistmentManager { EnlistedTransaction: not null })
            {
                throw new NotSupportedException("OceanBase schema DDL cannot run inside a caller transaction in this profile.");
            }
            var core = options.FindExtension<CoreOptionsExtension>();
            if (core?.Seeder is not null || core?.AsyncSeeder is not null)
            {
                throw new NotSupportedException("OceanBase schema creation does not yet support seeding callbacks.");
            }
            var model = currentContext.Context.GetService<IDesignTimeModel>().Model;
            var tables = model.GetRelationalModel().Tables.Where(t => !t.IsExcludedFromMigrations);
            if (tables.Any(t => t.Schema is not null && !string.Equals(t.Schema, scope, StringComparison.Ordinal)
                || t.ForeignKeyConstraints.Any(f => f.PrincipalTable.Schema is not null
                    && !string.Equals(f.PrincipalTable.Schema, scope, StringComparison.Ordinal))))
            {
                throw new NotSupportedException("The model contains a table outside the declared schema management scope.");
            }
            return CreateStatements(model);
        }

        private IRelationalCommand ScopeProbe() => commands.Build(_mode == OceanBaseCompatibilityMode.MySql
            ? "SELECT DATABASE()" : "SELECT SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA') FROM DUAL");

        private void ValidateCurrentScope(object? value)
        {
            var scope = options.FindExtension<Internal.OceanBaseOptionsExtension>()!.SchemaManagementScope;
            if (value is not string actual || !string.Equals(actual, scope, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The current database/schema does not match the declared schema management scope. No DDL was executed.");
            }
        }

        private bool CreateTables(bool onlyIfEmpty)
        {
            var statements = ValidateTableCreation();
            if (statements.Count == 0) return false;
            connection.Open();
            Exception? failure = null;
            try
            {
                ValidateCurrentScope(ScopeProbe().ExecuteScalar(Parameters()));
                if (onlyIfEmpty && HasTables()) return false;
                foreach (var statement in statements) commands.Build(statement).ExecuteNonQuery(Parameters());
                return true;
            }
            catch (Exception error)
            {
                failure = error;
                throw;
            }
            finally
            {
                try { connection.Close(); }
                catch (Exception cleanup) when (failure is not null)
                {
                    failure.Data["OceanBase.SchemaCleanupError"] = cleanup;
                }
            }
        }

        private async Task<bool> CreateTablesAsync(bool onlyIfEmpty, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var statements = ValidateTableCreation();
            if (statements.Count == 0) return false;
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            Exception? failure = null;
            try
            {
                ValidateCurrentScope(await ScopeProbe().ExecuteScalarAsync(Parameters(), cancellationToken).ConfigureAwait(false));
                if (onlyIfEmpty && await HasTablesAsync(cancellationToken).ConfigureAwait(false)) return false;
                foreach (var statement in statements)
                {
                    await commands.Build(statement).ExecuteNonQueryAsync(Parameters(), cancellationToken).ConfigureAwait(false);
                }
                return true;
            }
            catch (Exception error)
            {
                failure = error;
                throw;
            }
            finally
            {
                try { await connection.CloseAsync().ConfigureAwait(false); }
                catch (Exception cleanup) when (failure is not null)
                {
                    failure.Data["OceanBase.SchemaCleanupError"] = cleanup;
                }
            }
        }

        public bool EnsureDeleted() => throw SchemaNotSupported();
        public Task<bool> EnsureDeletedAsync(CancellationToken cancellationToken = default) => throw SchemaNotSupported();
    }
}
