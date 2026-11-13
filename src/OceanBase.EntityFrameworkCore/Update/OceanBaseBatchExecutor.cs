using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using Microsoft.EntityFrameworkCore.Update.Internal;
using OceanBase.EntityFrameworkCore.Storage;

namespace OceanBase.EntityFrameworkCore.Update
{
#pragma warning disable EF1001 // EF10 has no protected recovery/lease-cleanup seam; recheck these loops on EF upgrades.
    internal sealed class OceanBaseBatchExecutor(
        ICurrentDbContext currentContext, IDiagnosticsLogger<DbLoggerCategory.Update> updateLogger)
        : BatchExecutor(currentContext, updateLogger)
    {
        private const string SavepointName = "__EFSavePoint";

        public override int Execute(IEnumerable<ModificationCommandBatch> commandBatches, IRelationalConnection connection)
        {
            using var batches = commandBatches.GetEnumerator();
            if (!batches.MoveNext()) return 0;
            var batch = batches.Current;
            var transaction = connection.CurrentTransaction;
            var beganTransaction = false;
            var opened = false;
            var createdSavepoint = false;
            Exception? failure = null;
            try
            {
                var enlistment = connection as ITransactionEnlistmentManager;
                var behavior = CurrentContext.Context.Database.AutoTransactionBehavior;
                if (transaction is null && enlistment?.EnlistedTransaction is null && enlistment?.CurrentAmbientTransaction is null
                    && (behavior == AutoTransactionBehavior.WhenNeeded && (batch.AreMoreBatchesExpected || batch.RequiresTransaction)
                        || behavior == AutoTransactionBehavior.Always))
                {
                    transaction = connection.BeginTransaction();
                    beganTransaction = true;
                }
                else
                {
                    connection.Open();
                    opened = true;
                    if (transaction?.SupportsSavepoints == true && CurrentContext.Context.Database.AutoSavepointsEnabled)
                    {
                        transaction.CreateSavepoint(SavepointName);
                        createdSavepoint = true;
                    }
                }
                var rowsAffected = 0;
                do
                {
                    batch = batches.Current;
                    batch.Execute(connection);
                    rowsAffected += batch.ModificationCommands.Count;
                }
                while (batches.MoveNext());
                if (beganTransaction) transaction!.Commit();
                return rowsAffected;
            }
            catch (Exception error)
            {
                failure = error;
                if (createdSavepoint && connection.DbConnection.State == ConnectionState.Open)
                {
                    try { transaction!.RollbackToSavepoint(SavepointName); }
                    catch (Exception cleanup) { ReportSavepointFailure(cleanup, failure, rollback: true); }
                }
                throw;
            }
            finally
            {
                if (beganTransaction)
                {
                    try { transaction!.Dispose(); }
                    catch (Exception cleanup) when (failure is not null) { OceanBaseRetryingExecutionStrategy.PreventRetry(failure); failure.Data["OceanBase.BatchCleanupError"] = cleanup; }
                }
                else
                {
                    try
                    {
                        if (createdSavepoint && connection.DbConnection.State == ConnectionState.Open)
                        {
                            try { transaction!.ReleaseSavepoint(SavepointName); }
                            catch (Exception cleanup) { ReportSavepointFailure(cleanup, failure, rollback: false); }
                        }
                    }
                    catch (Exception error)
                    {
                        failure ??= error;
                        throw;
                    }
                    finally
                    {
                        try { if (opened) connection.Close(); }
                        catch (Exception cleanup) when (failure is not null) { OceanBaseRetryingExecutionStrategy.PreventRetry(failure); failure.Data["OceanBase.BatchCleanupError"] = cleanup; }
                    }
                }
            }
        }

        public override async Task<int> ExecuteAsync(
            IEnumerable<ModificationCommandBatch> commandBatches, IRelationalConnection connection,
            CancellationToken cancellationToken = default)
        {
            using var batches = commandBatches.GetEnumerator();
            if (!batches.MoveNext()) return 0;
            cancellationToken.ThrowIfCancellationRequested();
            var batch = batches.Current;
            var transaction = connection.CurrentTransaction;
            var beganTransaction = false;
            var opened = false;
            var createdSavepoint = false;
            Exception? failure = null;
            try
            {
                var enlistment = connection as ITransactionEnlistmentManager;
                var behavior = CurrentContext.Context.Database.AutoTransactionBehavior;
                if (transaction is null && enlistment?.EnlistedTransaction is null && enlistment?.CurrentAmbientTransaction is null
                    && (behavior == AutoTransactionBehavior.WhenNeeded && (batch.AreMoreBatchesExpected || batch.RequiresTransaction)
                        || behavior == AutoTransactionBehavior.Always))
                {
                    transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                    beganTransaction = true;
                }
                else
                {
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                    opened = true;
                    if (transaction?.SupportsSavepoints == true && CurrentContext.Context.Database.AutoSavepointsEnabled)
                    {
                        await transaction.CreateSavepointAsync(SavepointName, cancellationToken).ConfigureAwait(false);
                        createdSavepoint = true;
                    }
                }
                var rowsAffected = 0;
                do
                {
                    batch = batches.Current;
                    await batch.ExecuteAsync(connection, cancellationToken).ConfigureAwait(false);
                    rowsAffected += batch.ModificationCommands.Count;
                }
                while (batches.MoveNext());
                if (beganTransaction) await transaction!.CommitAsync(cancellationToken).ConfigureAwait(false);
                return rowsAffected;
            }
            catch (Exception error)
            {
                failure = error;
                if (createdSavepoint && connection.DbConnection.State == ConnectionState.Open)
                {
                    try
                    {
                        await transaction!.RollbackToSavepointAsync(SavepointName, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception cleanup) { ReportSavepointFailure(cleanup, failure, rollback: true); }
                }
                throw;
            }
            finally
            {
                if (beganTransaction)
                {
                    try { await transaction!.DisposeAsync().ConfigureAwait(false); }
                    catch (Exception cleanup) when (failure is not null) { OceanBaseRetryingExecutionStrategy.PreventRetry(failure); failure.Data["OceanBase.BatchCleanupError"] = cleanup; }
                }
                else
                {
                    try
                    {
                        if (createdSavepoint && connection.DbConnection.State == ConnectionState.Open)
                        {
                            try
                            {
                                await transaction!.ReleaseSavepointAsync(SavepointName, CancellationToken.None).ConfigureAwait(false);
                            }
                            catch (Exception cleanup) { ReportSavepointFailure(cleanup, failure, rollback: false); }
                        }
                    }
                    catch (Exception error)
                    {
                        failure ??= error;
                        throw;
                    }
                    finally
                    {
                        try { if (opened) await connection.CloseAsync().ConfigureAwait(false); }
                        catch (Exception cleanup) when (failure is not null) { OceanBaseRetryingExecutionStrategy.PreventRetry(failure); failure.Data["OceanBase.BatchCleanupError"] = cleanup; }
                    }
                }
            }
        }

        private void ReportSavepointFailure(Exception cleanup, Exception? failure, bool rollback)
        {
            if (failure is not null)
            {
                OceanBaseRetryingExecutionStrategy.PreventRetry(failure);
                failure.Data[rollback ? "OceanBase.SavepointRollbackError" : "OceanBase.SavepointReleaseError"] = cleanup;
            }
            try
            {
                if (rollback) UpdateLogger.BatchExecutorFailedToRollbackToSavepoint(CurrentContext.Context.GetType(), cleanup);
                else UpdateLogger.BatchExecutorFailedToReleaseSavepoint(CurrentContext.Context.GetType(), cleanup);
            }
            catch (Exception diagnostics) when (failure is not null)
            {
                failure.Data["OceanBase.SavepointDiagnosticsError"] = diagnostics;
            }
        }
    }
#pragma warning restore EF1001
}
