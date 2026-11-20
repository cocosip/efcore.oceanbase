using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
        private DbConnection? _currentConnection;
        private System.Transactions.Transaction? _ambientConnectionLease;
        private System.Transactions.Transaction? _resettingAmbientTransaction;
        private bool _ownsConnection;
        private bool _borrowedAmbientConnectionWasReset;

        internal bool HasAmbientConnectionLease => _ambientConnectionLease is not null;

        internal bool IsProviderCreatedConnection(OceanBaseConnection value)
            => ReferenceEquals(value, _providerCreatedConnection);

        public OceanBaseRelationalConnection(RelationalConnectionDependencies dependencies)
            : base(dependencies)
        {
            var options = dependencies.ContextOptions.FindExtension<Internal.OceanBaseOptionsExtension>();
            _compatibilityMode = options?.CompatibilityMode ?? OceanBaseCompatibilityMode.MySql;
            _ownsConnection = options?.Connection is null || options.IsConnectionOwned;
            _currentConnection = options?.Connection;
            if (options?.Connection is { } connection)
            {
                ValidateConnection(connection);
            }
        }

        private OceanBaseConnector.OceanBaseCompatibilityMode ExpectedMode
            => _compatibilityMode == OceanBaseCompatibilityMode.Oracle
                ? OceanBaseConnector.OceanBaseCompatibilityMode.Oracle
                : OceanBaseConnector.OceanBaseCompatibilityMode.MySql;

        protected override bool SupportsAmbientTransactions => true;

        protected override DbConnection CreateDbConnection()
            => _providerCreatedConnection = new OceanBaseConnection(ValidateConnectionString(GetValidatedConnectionString()));

        [AllowNull]
        public override DbConnection DbConnection
        {
            get => _currentConnection = base.DbConnection;
            set => SetDbConnection(value, contextOwnsConnection: false);
        }

        public override void SetDbConnection(DbConnection? value, bool contextOwnsConnection)
        {
            if (value is not null)
            {
                ValidateConnection(value);
            }

            var unchanged = ReferenceEquals(value, _currentConnection);
            if (!unchanged && _ambientConnectionLease is not null)
            {
                throw new InvalidOperationException(RelationalStrings.CannotChangeWhenOpen);
            }
            base.SetDbConnection(value, contextOwnsConnection);
            if (!unchanged)
            {
                _currentConnection = value;
                _ownsConnection = contextOwnsConnection;
            }
        }

        public override void EnlistTransaction(System.Transactions.Transaction? transaction)
        {
            ValidateConnection(DbConnection);
            ValidateAmbientConnection();
            base.EnlistTransaction(transaction);
            RetainAmbientConnection();
        }

        public override bool Open(bool errorsExpected = false)
        {
            ValidateConnection(DbConnection);
            ValidateAmbientConnection();
            var entry = new OceanBaseConnectionOpenState(this);
            try
            {
                var opened = base.Open(errorsExpected);
                ValidateConnection(DbConnection);
                RetainAmbientConnection();
                return opened;
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
            ValidateAmbientConnection();
            var entry = new OceanBaseConnectionOpenState(this);
            try
            {
                var opened = await base.OpenAsync(cancellationToken, errorsExpected).ConfigureAwait(false);
                ValidateConnection(DbConnection);
                RetainAmbientConnection();
                return opened;
            }
            catch (Exception error)
            {
                await entry.RestoreAsync(error).ConfigureAwait(false);
                throw;
            }
        }

        private void ValidateAmbientConnection()
        {
            if (_borrowedAmbientConnectionWasReset && _ambientConnectionLease is not null)
            {
                throw new InvalidOperationException(RelationalStrings.PendingAmbientTransaction);
            }
            if (CurrentAmbientTransaction is { } current
                && (_ambientConnectionLease ?? EnlistedTransaction) is { } retained && retained != current)
            {
                throw new InvalidOperationException(RelationalStrings.ConflictingAmbientTransaction);
            }
        }

        private void RetainAmbientConnection()
        {
            var transaction = CurrentAmbientTransaction ?? EnlistedTransaction;
            if (transaction is null || _ambientConnectionLease is not null)
            {
                return;
            }

            // The Connector must stay open until its single-resource enlistment completes.
            base.Open();
            _ambientConnectionLease = transaction;
            transaction.TransactionCompleted += ReleaseAmbientConnection;
        }

        private void ReleaseAmbientConnection(object? sender, System.Transactions.TransactionEventArgs args)
        {
            DetachAmbientConnection();
            Close();
        }

        private void DetachAmbientConnection()
        {
            if (_ambientConnectionLease is { } transaction)
            {
                transaction.TransactionCompleted -= ReleaseAmbientConnection;
                _ambientConnectionLease = null;
                _borrowedAmbientConnectionWasReset = false;
            }
        }

        protected override void ResetState(bool disposeDbConnection)
        {
            _resettingAmbientTransaction = _ownsConnection ? _ambientConnectionLease : null;
            if (_ownsConnection) DetachAmbientConnection();
            else _borrowedAmbientConnectionWasReset = _ambientConnectionLease is not null;
            try
            {
                base.ResetState(disposeDbConnection || _resettingAmbientTransaction is not null);
                if (_ownsConnection && (disposeDbConnection || _resettingAmbientTransaction is not null))
                {
                    _currentConnection = null;
                }
            }
            finally
            {
                _resettingAmbientTransaction = null;
            }
        }

        protected override async ValueTask ResetStateAsync(bool disposeDbConnection)
        {
            _resettingAmbientTransaction = _ownsConnection ? _ambientConnectionLease : null;
            if (_ownsConnection) DetachAmbientConnection();
            else _borrowedAmbientConnectionWasReset = _ambientConnectionLease is not null;
            try
            {
                await base.ResetStateAsync(disposeDbConnection || _resettingAmbientTransaction is not null).ConfigureAwait(false);
                if (_ownsConnection && (disposeDbConnection || _resettingAmbientTransaction is not null))
                {
                    _currentConnection = null;
                }
            }
            finally
            {
                _resettingAmbientTransaction = null;
            }
        }

        protected override void CloseDbConnection()
        {
            if (_resettingAmbientTransaction is null) base.CloseDbConnection();
        }

        protected override Task CloseDbConnectionAsync()
            => _resettingAmbientTransaction is null ? base.CloseDbConnectionAsync() : Task.CompletedTask;

        protected override void DisposeDbConnection()
        {
            if (_resettingAmbientTransaction is { } transaction) DisposeAfterTransaction(DbConnection, transaction);
            else base.DisposeDbConnection();
        }

        protected override ValueTask DisposeDbConnectionAsync()
        {
            if (_resettingAmbientTransaction is not { } transaction) return base.DisposeDbConnectionAsync();
            DisposeAfterTransaction(DbConnection, transaction);
            return ValueTask.CompletedTask;
        }

        private static void DisposeAfterTransaction(DbConnection connection, System.Transactions.Transaction transaction)
        {
            // A pooled/disposed context must not retain or abort an active native enlistment.
            DbConnection? remaining = connection;
            void Completed(object? sender, System.Transactions.TransactionEventArgs args)
            {
                transaction.TransactionCompleted -= Completed;
                try { Interlocked.Exchange(ref remaining, null)?.Dispose(); }
                catch (Exception error) { OceanBaseRetryingExecutionStrategy.PreventRetry(error); throw; }
            }
            transaction.TransactionCompleted += Completed;
            try
            {
                if (transaction.TransactionInformation.Status != System.Transactions.TransactionStatus.Active)
                    Completed(null, new System.Transactions.TransactionEventArgs());
            }
            catch (ObjectDisposedException)
            {
                Completed(null, new System.Transactions.TransactionEventArgs());
            }
        }

        public override bool Close()
        {
            try { return base.Close(); }
            catch (Exception error) { OceanBaseRetryingExecutionStrategy.PreventRetry(error); throw; }
        }

        public override async Task<bool> CloseAsync()
        {
            try { return await base.CloseAsync().ConfigureAwait(false); }
            catch (Exception error) { OceanBaseRetryingExecutionStrategy.PreventRetry(error); throw; }
        }

        public override IDbContextTransaction BeginTransaction(IsolationLevel isolationLevel)
        {
            ValidateConnection(DbConnection);
            ValidateTransactionStart();
            ValidateAmbientConnection();
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
                // No EF wrapper exists yet; native disposal cannot certify rollback for replay.
                if (transaction is not null) OceanBaseRetryingExecutionStrategy.PreventRetry(error);
                try { transaction?.Dispose(); }
                catch (Exception cleanup) { OceanBaseRetryingExecutionStrategy.PreventRetry(error); error.Data["OceanBase.TransactionStartCleanupError"] = cleanup; }
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
            ValidateAmbientConnection();
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
                if (transaction is not null) OceanBaseRetryingExecutionStrategy.PreventRetry(error);
                try { if (transaction is not null) await transaction.DisposeAsync().ConfigureAwait(false); }
                catch (Exception cleanup) { OceanBaseRetryingExecutionStrategy.PreventRetry(error); error.Data["OceanBase.TransactionStartCleanupError"] = cleanup; }
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

            _ = ValidateConnectionString(connection.ConnectionString);
            if (oceanBaseConnection.CompatibilityMode is { } actualMode && actualMode != ExpectedMode)
            {
                throw new InvalidOperationException($"The connection mode does not match '{ExpectedMode}'.");
            }

            // A supplied connection may hide credentials after opening or have fixed data-source options.
            // Validate its configured and detected modes without rewriting its configuration.
        }
    }
}
