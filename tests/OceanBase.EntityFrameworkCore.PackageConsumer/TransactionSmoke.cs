using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBase.EntityFrameworkCore;

namespace PackageConsumer
{
    internal static class TransactionSmoke
    {
        public static void Run(OceanBaseCompatibilityMode mode)
        {
            using var context = new DbContext(new DbContextOptionsBuilder()
                .UseOceanBase("Server=localhost;Database=package_smoke", mode).Options);
            var connection = context.GetService<IRelationalConnection>();
            using var native = new BorrowedTransaction(connection.DbConnection);
            using (var transaction = context.GetService<IRelationalTransactionFactory>().Create(connection, native, Guid.NewGuid(),
                context.GetService<IDiagnosticsLogger<DbLoggerCategory.Database.Transaction>>(), transactionOwned: false))
            {
                if (mode == OceanBaseCompatibilityMode.Oracle)
                {
                    // The physical connection is closed: a release command would fail rather than contact a server.
                    transaction.ReleaseSavepoint("checkpoint");
                    transaction.ReleaseSavepointAsync("checkpoint").GetAwaiter().GetResult();
                    using var cancelled = new CancellationTokenSource();
                    cancelled.Cancel();
                    var rejected = false;
                    try { transaction.ReleaseSavepointAsync("checkpoint", cancelled.Token).GetAwaiter().GetResult(); }
                    catch (OperationCanceledException error) when (error.CancellationToken == cancelled.Token) { rejected = true; }
                    if (!rejected) throw new Exception("Packed Oracle logical release ignored cancellation.");
                }
                else if (transaction.GetType() != typeof(RelationalTransaction))
                {
                    throw new Exception("Packed MySQL transaction no longer uses EF's relational implementation.");
                }
            }
            if (native.Disposed) throw new Exception("Packed transaction disposed its borrowed native transaction.");
            Console.WriteLine($"{mode}: packed transaction factory/ownership and Oracle logical release passed (offline).");
        }

        private sealed class BorrowedTransaction(DbConnection connection) : DbTransaction
        {
            public bool Disposed { get; private set; }
            public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
            protected override DbConnection DbConnection => connection;
            public override void Commit() => throw new NotSupportedException();
            public override void Rollback() => throw new NotSupportedException();
            protected override void Dispose(bool disposing) { if (disposing) Disposed = true; base.Dispose(disposing); }
        }
    }
}
