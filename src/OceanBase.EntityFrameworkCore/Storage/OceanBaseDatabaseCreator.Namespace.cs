using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBaseConnector;

namespace OceanBase.EntityFrameworkCore.Storage
{
    internal sealed partial class OceanBaseDatabaseCreator
    {
        private bool DatabaseManagementEnabled => options.FindExtension<Internal.OceanBaseOptionsExtension>()!.DatabaseManagementEnabled;

        private OceanBaseRelationalConnection ManagementConnection(out string scope, out OceanBaseConnection target)
        {
            ValidateNoRetries();
            var extension = options.FindExtension<Internal.OceanBaseOptionsExtension>()!;
            if (!extension.DatabaseManagementEnabled
                || (_mode == OceanBaseCompatibilityMode.Oracle) != (extension.OracleUserPasswordFactory is not null))
                throw SchemaNotSupported();
            if (connection.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null
                || connection is ITransactionEnlistmentManager { EnlistedTransaction: not null })
                throw new NotSupportedException("Database namespace management cannot run inside a caller transaction.");
            target = connection.DbConnection as OceanBaseConnection
                ?? throw new InvalidOperationException("Database namespace management requires an OceanBaseConnector connection.");
            if (target.State != ConnectionState.Closed)
                throw new InvalidOperationException("Close the target connection before database namespace management.");
            scope = extension.SchemaManagementScope!;
            if (_mode == OceanBaseCompatibilityMode.Oracle)
            {
                ValidateOracleTarget(target, scope);
                return CreateManagementConnection(extension.ManagementConnectionFactory ?? throw SchemaNotSupported(), target);
            }
            if (!string.Equals(target.Database, scope, StringComparison.Ordinal))
                throw new InvalidOperationException("The configured database does not match the declared database management scope. No DDL was executed.");
            if (extension.ManagementConnectionFactory is { } factory)
                return CreateManagementConnection(factory, target);
            if (extension.Connection is not null || string.IsNullOrWhiteSpace(connection.ConnectionString))
                throw new NotSupportedException("Database namespace management requires a configured connection string or an explicit management connection factory.");
            if (connection is not OceanBaseRelationalConnection provider || !provider.IsProviderCreatedConnection(target)
                || target.AddressResolver is not null || target.CredentialProvider is not null
                || target.ClientCertificate is not null || target.RemoteCertificateValidationCallback is not null)
                throw new NotSupportedException("Database namespace management requires an unchanged provider-created connection without native routing, credential or TLS hooks. Customized management connections are not yet supported.");
            var builder = new OceanBaseConnectionStringBuilder(provider.ValidateConnectionString(connection.ConnectionString!));
            var actual = new OceanBaseConnectionStringBuilder(provider.ValidateConnectionString(target.ConnectionString));
            if (!builder.EquivalentTo(actual))
                throw new NotSupportedException("The native connection configuration differs from EF's connection string. Database namespace management cannot safely clone this configuration.");
            if (!string.Equals(builder.Database, scope, StringComparison.Ordinal)
                || !string.Equals(target.Database, scope, StringComparison.Ordinal))
                throw new InvalidOperationException("The configured database does not match the declared database management scope. No DDL was executed.");
            builder.Database = "";
            var managementOptions = new DbContextOptionsBuilder((DbContextOptions)options).UseOceanBase(builder.ConnectionString, _mode).Options;
            return new OceanBaseRelationalConnection(connectionDependencies with { ContextOptions = managementOptions })
                { CommandTimeout = connection.CommandTimeout };
        }

        private OceanBaseRelationalConnection CreateManagementConnection(Func<OceanBaseConnection> factory, OceanBaseConnection target)
        {
            if (connection is OceanBaseRelationalConnection provider)
                provider.ValidateConnectionString(target.ConnectionString);
            var management = factory() ?? throw new InvalidOperationException("The management connection factory returned null.");
            // A mistaken factory must never transfer ownership of the application's target connection.
            if (ReferenceEquals(management, target))
                throw new InvalidOperationException("The management connection must be independent of the target connection.");
            try
            {
                if (management.State != ConnectionState.Closed)
                    throw new InvalidOperationException("The management connection factory must return a closed connection.");
                if (_mode == OceanBaseCompatibilityMode.MySql && management.Database.Length != 0)
                    throw new InvalidOperationException("The MySQL management connection must not select a database.");
                if (_mode == OceanBaseCompatibilityMode.Oracle && IsOracleLogin(management, options.FindExtension<Internal.OceanBaseOptionsExtension>()!.SchemaManagementScope!))
                    throw new InvalidOperationException("Oracle administration must use a different login from the owned user.");
                var extension = options.FindExtension<Internal.OceanBaseOptionsExtension>()!
                    .WithConnection(management, owned: true);
                var managementOptions = new DbContextOptionsBuilder((DbContextOptions)options);
                ((IDbContextOptionsBuilderInfrastructure)managementOptions).AddOrUpdateExtension(extension);
                return new OceanBaseRelationalConnection(connectionDependencies with { ContextOptions = managementOptions.Options })
                    { CommandTimeout = connection.CommandTimeout };
            }
            catch (Exception error)
            {
                try { management.Dispose(); }
                catch (Exception cleanup) { error.Data["OceanBase.DatabaseManagementCleanupError"] = cleanup; }
                throw;
            }
        }

        private RawSqlCommand NamespaceProbe(string scope)
            => commands.Build(_mode == OceanBaseCompatibilityMode.MySql
                ? "SELECT SCHEMA_NAME FROM information_schema.schemata WHERE SCHEMA_NAME = {0}"
                : "SELECT USERNAME FROM SYS.DBA_USERS WHERE USERNAME = {0}", [scope]);

        private RelationalCommandParameterObject ManagementParameters(IRelationalConnection management, RawSqlCommand? command = null)
            => new(management, command?.ParameterValues, null, currentContext.Context, logger, CommandSource.Unknown);

        private static bool ReadNamespace(object? value, string scope)
        {
            if (value is null or DBNull) return false;
            if (value is string name && string.Equals(name, scope, StringComparison.Ordinal)) return true;
            throw new InvalidOperationException("The database catalog did not return the exact declared namespace. No DDL was executed.");
        }

        private IRelationalCommand NamespaceStatement(string scope, bool delete)
            => _mode == OceanBaseCompatibilityMode.Oracle ? OracleUserStatement(scope, delete)
                : commands.Build((delete ? "DROP DATABASE " : "CREATE DATABASE ")
                    + currentContext.Context.GetService<ISqlGenerationHelper>().DelimitIdentifier(scope));

        private bool ManageDatabase(bool delete, bool checkExists)
        {
            var management = ManagementConnection(out var scope, out var target);
            Exception? failure = null;
            try
            {
                management.Open();
                if (checkExists)
                {
                    var probe = NamespaceProbe(scope);
                    var exists = ReadNamespace(probe.RelationalCommand.ExecuteScalar(ManagementParameters(management, probe)), scope);
                    if (!delete && exists) return false;
                    if (delete && !exists && _mode == OceanBaseCompatibilityMode.Oracle) return false;
                    // Catalog visibility is permission-dependent; only DROP's unknown-database error proves absence.
                }
                var statement = NamespaceStatement(scope, delete);
                OceanBaseConnection.ClearPool(target);
                try
                {
                    statement.ExecuteNonQuery(ManagementParameters(management));
                    if (!delete && _mode == OceanBaseCompatibilityMode.Oracle)
                        OracleUserGrant(scope).ExecuteNonQuery(ManagementParameters(management));
                }
                catch (OceanBaseException error) when (_mode == OceanBaseCompatibilityMode.MySql && delete && checkExists
                    && error.ServerErrorCode == 1008 && !error.IsExecutionOutcomeUnknown)
                {
                    return false;
                }
                OceanBaseConnection.ClearPool(target);
                return true;
            }
            catch (Exception error)
            {
                failure = error;
                throw;
            }
            finally
            {
                try { management.Dispose(); }
                catch (Exception cleanup) when (failure is not null) { failure.Data["OceanBase.DatabaseManagementCleanupError"] = cleanup; }
            }
        }

        private async Task<bool> ManageDatabaseAsync(bool delete, bool checkExists, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var management = ManagementConnection(out var scope, out var target);
            Exception? failure = null;
            try
            {
                await management.OpenAsync(cancellationToken).ConfigureAwait(false);
                if (checkExists)
                {
                    var probe = NamespaceProbe(scope);
                    var value = await probe.RelationalCommand.ExecuteScalarAsync(ManagementParameters(management, probe), cancellationToken).ConfigureAwait(false);
                    var exists = ReadNamespace(value, scope);
                    if (!delete && exists) return false;
                    if (delete && !exists && _mode == OceanBaseCompatibilityMode.Oracle) return false;
                }
                var statement = NamespaceStatement(scope, delete);
                OceanBaseConnection.ClearPool(target);
                try
                {
                    await statement
                        .ExecuteNonQueryAsync(ManagementParameters(management), cancellationToken).ConfigureAwait(false);
                    if (!delete && _mode == OceanBaseCompatibilityMode.Oracle)
                        await OracleUserGrant(scope).ExecuteNonQueryAsync(ManagementParameters(management), cancellationToken).ConfigureAwait(false);
                }
                catch (OceanBaseException error) when (_mode == OceanBaseCompatibilityMode.MySql && delete && checkExists
                    && error.ServerErrorCode == 1008 && !error.IsExecutionOutcomeUnknown)
                {
                    return false;
                }
                OceanBaseConnection.ClearPool(target);
                return true;
            }
            catch (Exception error)
            {
                failure = error;
                throw;
            }
            finally
            {
                try { await management.DisposeAsync().ConfigureAwait(false); }
                catch (Exception cleanup) when (failure is not null) { failure.Data["OceanBase.DatabaseManagementCleanupError"] = cleanup; }
            }
        }
    }
}
