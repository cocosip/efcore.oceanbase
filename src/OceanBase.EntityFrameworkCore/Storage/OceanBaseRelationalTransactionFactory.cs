using System;
using System.Data.Common;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace OceanBase.EntityFrameworkCore.Storage
{
    internal sealed class OceanBaseRelationalTransactionFactory(
        RelationalTransactionFactoryDependencies dependencies, OceanBaseCompatibilityMode mode)
        : RelationalTransactionFactory(dependencies)
    {
        public override RelationalTransaction Create(
            IRelationalConnection connection, DbTransaction transaction, Guid transactionId,
            IDiagnosticsLogger<DbLoggerCategory.Database.Transaction> logger, bool transactionOwned)
        {
            var confirmRollback = transactionOwned && (ExecutionStrategy.Current is not null
                || connection.Context.Database.CreateExecutionStrategy().RetriesOnFailure);
            return mode == OceanBaseCompatibilityMode.Oracle || confirmRollback
                ? new OceanBaseTransaction(connection, transaction, transactionId, logger, transactionOwned,
                    Dependencies.SqlGenerationHelper, mode, confirmRollback)
                : base.Create(connection, transaction, transactionId, logger, transactionOwned);
        }

        private sealed class OceanBaseTransaction : RelationalTransaction
        {
            private readonly DbTransaction _transaction;
            private readonly OceanBaseCompatibilityMode _mode;
            private readonly bool _confirmRollback;
            private bool _completed;
            private bool _disposed;
            private Exception? _rollbackFailure;

            public OceanBaseTransaction(
                IRelationalConnection connection, DbTransaction transaction, Guid transactionId,
                IDiagnosticsLogger<DbLoggerCategory.Database.Transaction> logger, bool transactionOwned,
                ISqlGenerationHelper sqlGenerationHelper, OceanBaseCompatibilityMode mode, bool confirmRollback)
                : base(connection, transaction, transactionId, logger, transactionOwned, sqlGenerationHelper)
            {
                _transaction = transaction;
                _mode = mode;
                _confirmRollback = confirmRollback;
            }

            public override void Commit()
            {
                try { base.Commit(); _completed = true; }
                catch (Exception error) { OceanBaseRetryingExecutionStrategy.PreventRetry(error); throw; }
            }

            public override async Task CommitAsync(CancellationToken cancellationToken = default)
            {
                try { await base.CommitAsync(cancellationToken).ConfigureAwait(false); _completed = true; }
                catch (Exception error) { OceanBaseRetryingExecutionStrategy.PreventRetry(error); throw; }
            }

            public override void Rollback()
            {
                try { base.Rollback(); _completed = true; }
                catch (Exception error) { _rollbackFailure = error; OceanBaseRetryingExecutionStrategy.PreventRetry(error); throw; }
            }

            public override async Task RollbackAsync(CancellationToken cancellationToken = default)
            {
                try { await base.RollbackAsync(cancellationToken).ConfigureAwait(false); _completed = true; }
                catch (Exception error) { _rollbackFailure = error; OceanBaseRetryingExecutionStrategy.PreventRetry(error); throw; }
            }

            public override void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                Exception? failure = _confirmRollback ? _rollbackFailure : null;
                if (_confirmRollback && !_completed && failure is null)
                {
                    try { Rollback(); }
                    catch (Exception error) { failure = error; }
                }
                try { base.Dispose(); }
                catch (Exception error)
                {
                    if (failure is null) failure = error;
                    else failure.Data["OceanBase.TransactionDisposeError"] = error;
                }
                if (failure is not null)
                {
                    OceanBaseRetryingExecutionStrategy.PreventRetry(failure);
                    ExceptionDispatchInfo.Capture(failure).Throw();
                }
            }

            public override async ValueTask DisposeAsync()
            {
                if (_disposed) return;
                _disposed = true;
                Exception? failure = _confirmRollback ? _rollbackFailure : null;
                if (_confirmRollback && !_completed && failure is null)
                {
                    try { await RollbackAsync(CancellationToken.None).ConfigureAwait(false); }
                    catch (Exception error) { failure = error; }
                }
                try { await base.DisposeAsync().ConfigureAwait(false); }
                catch (Exception error)
                {
                    if (failure is null) failure = error;
                    else failure.Data["OceanBase.TransactionDisposeError"] = error;
                }
                if (failure is not null)
                {
                    OceanBaseRetryingExecutionStrategy.PreventRetry(failure);
                    ExceptionDispatchInfo.Capture(failure).Throw();
                }
            }

            // Oracle retains savepoints until replacement or transaction end; no release SQL exists.
            public override void ReleaseSavepoint(string name)
            {
                if (_mode != OceanBaseCompatibilityMode.Oracle) { base.ReleaseSavepoint(name); return; }
                ArgumentException.ThrowIfNullOrEmpty(name);
                var startTime = DateTimeOffset.UtcNow;
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    Logger.ReleasingTransactionSavepoint(Connection, _transaction, TransactionId, startTime);
                    Logger.ReleasedTransactionSavepoint(Connection, _transaction, TransactionId, startTime);
                }
                catch (Exception error)
                {
                    Logger.TransactionError(Connection, _transaction, TransactionId, "ReleaseSavepoint",
                        error, startTime, stopwatch.Elapsed);
                    throw;
                }
            }

            public override async Task ReleaseSavepointAsync(string name, CancellationToken cancellationToken = default)
            {
                if (_mode != OceanBaseCompatibilityMode.Oracle)
                {
                    await base.ReleaseSavepointAsync(name, cancellationToken).ConfigureAwait(false);
                    return;
                }
                cancellationToken.ThrowIfCancellationRequested();
                ArgumentException.ThrowIfNullOrEmpty(name);
                var startTime = DateTimeOffset.UtcNow;
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    await Logger.ReleasingTransactionSavepointAsync(Connection, _transaction, TransactionId,
                        startTime, cancellationToken).ConfigureAwait(false);
                    await Logger.ReleasedTransactionSavepointAsync(Connection, _transaction, TransactionId,
                        startTime, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    await Logger.TransactionErrorAsync(Connection, _transaction, TransactionId, "ReleaseSavepoint",
                        error, startTime, stopwatch.Elapsed, cancellationToken).ConfigureAwait(false);
                    throw;
                }
            }
        }
    }
}
