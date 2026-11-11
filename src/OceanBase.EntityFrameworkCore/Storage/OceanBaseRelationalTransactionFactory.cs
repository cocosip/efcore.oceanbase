using System;
using System.Data.Common;
using System.Diagnostics;
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
            => mode == OceanBaseCompatibilityMode.Oracle
                ? new OracleTransaction(connection, transaction, transactionId, logger, transactionOwned, Dependencies.SqlGenerationHelper)
                : base.Create(connection, transaction, transactionId, logger, transactionOwned);

        private sealed class OracleTransaction : RelationalTransaction
        {
            private readonly DbTransaction _transaction;

            public OracleTransaction(
                IRelationalConnection connection, DbTransaction transaction, Guid transactionId,
                IDiagnosticsLogger<DbLoggerCategory.Database.Transaction> logger, bool transactionOwned,
                ISqlGenerationHelper sqlGenerationHelper)
                : base(connection, transaction, transactionId, logger, transactionOwned, sqlGenerationHelper)
            {
                _transaction = transaction;
            }

            // Oracle retains savepoints until replacement or transaction end; no release SQL exists.
            public override void ReleaseSavepoint(string name)
            {
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
