using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace OceanBase.EntityFrameworkCore.Storage
{
    internal sealed partial class OceanBaseDatabaseCreator
    {
        private void ValidateSeeder(bool async)
        {
            var core = options.FindExtension<CoreOptionsExtension>();
            if (async ? core?.AsyncSeeder is null && core?.Seeder is not null
                : core?.Seeder is null && core?.AsyncSeeder is not null)
                throw new InvalidOperationException(CoreStrings.MissingSeeder);
        }

        private void Seed(bool operationsPerformed)
        {
            var seeder = options.FindExtension<CoreOptionsExtension>()?.Seeder;
            if (seeder is null) return;
            var context = currentContext.Context;
            var transaction = context.Database.BeginTransaction();
            Exception? failure = null;
            try
            {
                ValidateCurrentScope(ScopeProbe().ExecuteScalar(Parameters()));
                seeder(context, operationsPerformed);
                transaction.Commit();
            }
            catch (Exception error)
            {
                failure = error;
                throw;
            }
            finally
            {
                try { transaction.Dispose(); }
                catch (Exception cleanup) when (failure is not null) { failure.Data["OceanBase.SeedingCleanupError"] = cleanup; }
            }
        }

        private async Task SeedAsync(bool operationsPerformed, CancellationToken cancellationToken)
        {
            var seeder = options.FindExtension<CoreOptionsExtension>()?.AsyncSeeder;
            if (seeder is null) return;
            cancellationToken.ThrowIfCancellationRequested();
            var context = currentContext.Context;
            var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            Exception? failure = null;
            try
            {
                ValidateCurrentScope(await ScopeProbe().ExecuteScalarAsync(Parameters(), cancellationToken).ConfigureAwait(false));
                await seeder(context, operationsPerformed, cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                failure = error;
                throw;
            }
            finally
            {
                try { await transaction.DisposeAsync().ConfigureAwait(false); }
                catch (Exception cleanup) when (failure is not null) { failure.Data["OceanBase.SeedingCleanupError"] = cleanup; }
            }
        }
    }
}
