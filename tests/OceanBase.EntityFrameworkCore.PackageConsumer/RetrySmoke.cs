using System;
using System.Data;
using System.Data.Common;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBase.EntityFrameworkCore;
using OceanBase.EntityFrameworkCore.Storage;
using OceanBaseConnector;
using Mode = OceanBase.EntityFrameworkCore.OceanBaseCompatibilityMode;

namespace PackageConsumer;

internal static class RetrySmoke
{
    public static void Run(Mode mode)
    {
        using var context = new DbContext(new DbContextOptionsBuilder().UseOceanBase("Server=offline;Database=app", mode,
            options => options.EnableRetryOnFailure(2, TimeSpan.Zero)).EnableOceanBaseSchemaManagement("app").Options);
        var strategy = context.Database.CreateExecutionStrategy();
        if (strategy is not OceanBaseRetryingExecutionStrategy || !strategy.RetriesOnFailure)
            throw new Exception("Packed retry configuration did not resolve the public execution strategy.");
        var connection = context.GetService<IRelationalConnection>();
        var attempts = 0;
        var rollbacks = 0;
        var commits = 0;
        var disposals = 0;
        foreach (var async in new[] { false, true })
        {
            attempts = 0;
            int Execute()
            {
                using var transaction = context.GetService<IRelationalTransactionFactory>().Create(connection,
                    new Transaction(connection.DbConnection, () => rollbacks++, () => commits++, () => disposals++), Guid.NewGuid(),
                    context.GetService<IDiagnosticsLogger<DbLoggerCategory.Database.Transaction>>(), transactionOwned: true);
                if (++attempts == 1) throw Error(mode, unknown: false);
                transaction.Commit();
                return 7;
            }
            async System.Threading.Tasks.Task<int> ExecuteAsync()
            {
                await using var transaction = context.GetService<IRelationalTransactionFactory>().Create(connection,
                    new Transaction(connection.DbConnection, () => rollbacks++, () => commits++, () => disposals++), Guid.NewGuid(),
                    context.GetService<IDiagnosticsLogger<DbLoggerCategory.Database.Transaction>>(), transactionOwned: true);
                if (++attempts == 1) throw Error(mode, unknown: false);
                await transaction.CommitAsync();
                return 7;
            }
            var result = async ? strategy.ExecuteAsync(ExecuteAsync).GetAwaiter().GetResult() : strategy.Execute(Execute);
            if (result != 7 || attempts != 2) throw new Exception("Packed retry did not replay a confirmed failure.");
        }
        if (rollbacks != 2 || commits != 2 || disposals != 4)
            throw new Exception("Packed retry did not confirm rollback before native disposal.");
        attempts = 0;
        var unknown = Error(mode, unknown: true);
        try { strategy.Execute((Action)(() => { attempts++; throw unknown; })); }
        catch (OceanBaseException actual) when (ReferenceEquals(actual, unknown)) { }
        if (attempts != 1) throw new Exception("Packed strategy replayed an unknown outcome.");
        var rejected = false;
        try { strategy.Execute(() => context.Database.EnsureCreated()); }
        catch (NotSupportedException) { rejected = true; }
        if (!rejected) throw new Exception("Packed strategy allowed implicit-commit DDL replay.");
        Console.WriteLine($"{mode}: packed opt-in retries, confirmed rollback, unknown outcome and DDL guards passed (offline).");
    }

    private static OceanBaseException Error(Mode mode, bool unknown)
        => (OceanBaseException)Activator.CreateInstance(typeof(OceanBaseException), BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null, args: ["Synthetic consumer transient failure.", mode == Mode.MySql ? 1213 : 60, null, unknown, null, null], culture: null)!;

    private sealed class Transaction(DbConnection connection, Action rollback, Action commit, Action dispose) : DbTransaction
    {
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        protected override DbConnection DbConnection => connection;
        public override void Commit() => commit();
        public override void Rollback() => rollback();
        protected override void Dispose(bool disposing) { if (disposing) dispose(); base.Dispose(disposing); }
    }
}
