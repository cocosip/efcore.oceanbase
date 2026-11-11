using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBaseConnector;

namespace OceanBase.EntityFrameworkCore.Storage
{
    internal sealed class OceanBaseRelationalConnection : RelationalConnection
    {
        private readonly OceanBaseCompatibilityMode _compatibilityMode;
        private OceanBaseConnection? _providerCreatedConnection;

        internal bool IsProviderCreatedConnection(OceanBaseConnection value)
            => ReferenceEquals(value, _providerCreatedConnection);

        public OceanBaseRelationalConnection(RelationalConnectionDependencies dependencies)
            : base(dependencies)
        {
            var options = dependencies.ContextOptions.FindExtension<Internal.OceanBaseOptionsExtension>();
            _compatibilityMode = options?.CompatibilityMode ?? OceanBaseCompatibilityMode.MySql;
            if (options?.Connection is { } connection)
            {
                ValidateConnection(connection);
            }
        }

        private OceanBaseConnector.OceanBaseCompatibilityMode ExpectedMode
            => _compatibilityMode == OceanBaseCompatibilityMode.Oracle
                ? OceanBaseConnector.OceanBaseCompatibilityMode.Oracle
                : OceanBaseConnector.OceanBaseCompatibilityMode.MySql;

        protected override DbConnection CreateDbConnection()
            => _providerCreatedConnection = new OceanBaseConnection(ValidateConnectionString(GetValidatedConnectionString()));

        public override void SetDbConnection(DbConnection? value, bool contextOwnsConnection)
        {
            if (value is not null)
            {
                ValidateConnection(value);
            }

            base.SetDbConnection(value, contextOwnsConnection);
        }

        public override bool Open(bool errorsExpected = false)
        {
            ValidateConnection(DbConnection);
            var entry = new OceanBaseConnectionOpenState(this);
            try
            {
                return base.Open(errorsExpected);
            }
            catch (Exception error)
            {
                entry.Restore(error);
                throw;
            }
        }

        public override async Task<bool> OpenAsync(CancellationToken cancellationToken, bool errorsExpected = false)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateConnection(DbConnection);
            var entry = new OceanBaseConnectionOpenState(this);
            try
            {
                return await base.OpenAsync(cancellationToken, errorsExpected).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                await entry.RestoreAsync(error).ConfigureAwait(false);
                throw;
            }
        }

        public override IDbContextTransaction BeginTransaction(IsolationLevel isolationLevel)
        {
            ValidateConnection(DbConnection);
            ValidateTransactionStart();
            var entry = new OceanBaseConnectionOpenState(this);
            DbTransaction? transaction = null;
            try
            {
                Open();
                var id = Guid.NewGuid();
                var start = DateTimeOffset.UtcNow;
                var watch = Stopwatch.StartNew();
                var intercepted = Dependencies.TransactionLogger.TransactionStarting(this, isolationLevel, id, start);
                transaction = intercepted.HasResult ? intercepted.Result : ConnectionBeginTransaction(isolationLevel);
                transaction = Dependencies.TransactionLogger.TransactionStarted(this, transaction, id, start, watch.Elapsed);
                return CurrentTransaction = Dependencies.RelationalTransactionFactory.Create(
                    this, transaction, id, Dependencies.TransactionLogger, transactionOwned: true);
            }
            catch (Exception error)
            {
                try { transaction?.Dispose(); }
                catch (Exception cleanup) { error.Data["OceanBase.TransactionStartCleanupError"] = cleanup; }
                entry.Restore(error);
                throw;
            }
        }

        public override async Task<IDbContextTransaction> BeginTransactionAsync(
            IsolationLevel isolationLevel, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateConnection(DbConnection);
            ValidateTransactionStart();
            var entry = new OceanBaseConnectionOpenState(this);
            DbTransaction? transaction = null;
            try
            {
                await OpenAsync(cancellationToken).ConfigureAwait(false);
                var id = Guid.NewGuid();
                var start = DateTimeOffset.UtcNow;
                var watch = Stopwatch.StartNew();
                var intercepted = await Dependencies.TransactionLogger.TransactionStartingAsync(
                    this, isolationLevel, id, start, cancellationToken).ConfigureAwait(false);
                transaction = intercepted.HasResult ? intercepted.Result
                    : await ConnectionBeginTransactionAsync(isolationLevel, cancellationToken).ConfigureAwait(false);
                transaction = await Dependencies.TransactionLogger.TransactionStartedAsync(
                    this, transaction, id, start, watch.Elapsed, cancellationToken).ConfigureAwait(false);
                return CurrentTransaction = Dependencies.RelationalTransactionFactory.Create(
                    this, transaction, id, Dependencies.TransactionLogger, transactionOwned: true);
            }
            catch (Exception error)
            {
                try { if (transaction is not null) await transaction.DisposeAsync().ConfigureAwait(false); }
                catch (Exception cleanup) { error.Data["OceanBase.TransactionStartCleanupError"] = cleanup; }
                await entry.RestoreAsync(error).ConfigureAwait(false);
                throw;
            }
        }

        private void ValidateTransactionStart()
        {
            if (CurrentTransaction is not null) throw new InvalidOperationException(RelationalStrings.TransactionAlreadyStarted);
            if (CurrentAmbientTransaction is not null) throw new InvalidOperationException(RelationalStrings.ConflictingAmbientTransaction);
            if (EnlistedTransaction is not null) throw new InvalidOperationException(RelationalStrings.ConflictingEnlistedTransaction);
        }

        internal string ValidateConnectionString(string connectionString)
        {
            var builder = new OceanBaseConnectionStringBuilder(connectionString);
            if (builder.ExpectedMode is { } configuredMode && configuredMode != ExpectedMode)
            {
                throw new InvalidOperationException($"The connection mode does not match '{ExpectedMode}'.");
            }

            if (_compatibilityMode == OceanBaseCompatibilityMode.MySql && builder.UseAffectedRows)
            {
                throw new InvalidOperationException("EF Core requires UseAffectedRows=false to detect matched rows for concurrency checks.");
            }

            builder.ExpectedMode = ExpectedMode;
            return builder.ConnectionString;
        }

        private void ValidateConnection(DbConnection connection)
        {
            if (connection is not OceanBaseConnection oceanBaseConnection)
            {
                throw new InvalidOperationException("The OceanBase provider requires an OceanBaseConnector connection.");
            }

            var connectionString = ValidateConnectionString(connection.ConnectionString);
            if (oceanBaseConnection.CompatibilityMode is { } actualMode && actualMode != ExpectedMode)
            {
                throw new InvalidOperationException($"The connection mode does not match '{ExpectedMode}'.");
            }

            // EF also reads DbConnection during cleanup, so validation belongs at use boundaries.
            if (connection.State == ConnectionState.Closed && connection.ConnectionString != connectionString)
            {
                connection.ConnectionString = connectionString;
            }
        }
    }
}
