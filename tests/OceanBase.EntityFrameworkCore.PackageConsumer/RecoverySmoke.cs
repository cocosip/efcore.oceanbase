using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using OceanBase.EntityFrameworkCore;
using OceanBaseConnector;

namespace PackageConsumer
{
    internal static class RecoverySmoke
    {
        public static void Run(OceanBase.EntityFrameworkCore.OceanBaseCompatibilityMode mode)
        {
            using var context = new DbContext(new DbContextOptionsBuilder().UseOceanBase("Server=offline;Database=offline", mode)
                .ReplaceService<IRelationalConnection, Connection>().Options);
            var connection = context.GetService<IRelationalConnection>();
            var native = (NativeConnection)connection.DbConnection;
            using var transaction = context.Database.BeginTransaction();
            using var source = new CancellationTokenSource();
            var failure = new OperationCanceledException(source.Token);
            var rejected = false;
            try
            {
                context.GetService<IBatchExecutor>().ExecuteAsync([new CancelBatch(source, failure)], connection, source.Token)
                    .GetAwaiter().GetResult();
            }
            catch (OperationCanceledException error) when (ReferenceEquals(error, failure)) { rejected = true; }
            var expectedCount = mode == OceanBase.EntityFrameworkCore.OceanBaseCompatibilityMode.MySql ? 3 : 2;
            if (!rejected || native.Commands.Count != expectedCount || !native.Commands[0].Sql.StartsWith("SAVEPOINT", StringComparison.Ordinal)
                || native.Commands[0].Token != source.Token || !native.Commands[1].Sql.StartsWith("ROLLBACK TO", StringComparison.Ordinal)
                || native.Commands.Skip(1).Any(c => c.Token != CancellationToken.None)
                || !ReferenceEquals(transaction, context.Database.CurrentTransaction) || native.State != ConnectionState.Open)
                throw new Exception("Packed automatic cancellation recovery did not preserve the caller transaction or use uncancelled cleanup.");
            if (expectedCount == 3 && !native.Commands[2].Sql.StartsWith("RELEASE SAVEPOINT", StringComparison.Ordinal))
                throw new Exception("Packed MySQL cancellation recovery lost savepoint release.");
            Console.WriteLine($"{mode}: packed automatic savepoint cancellation recovery passed (native doubles).");
        }

        public sealed class Connection(RelationalConnectionDependencies dependencies) : RelationalConnection(dependencies)
        {
            protected override DbConnection CreateDbConnection() => new NativeConnection();
        }

        private sealed class CancelBatch(CancellationTokenSource source, Exception failure) : ModificationCommandBatch
        {
            public override IReadOnlyList<IReadOnlyModificationCommand> ModificationCommands => [];
            public override bool RequiresTransaction => false;
            public override bool AreMoreBatchesExpected => false;
            public override bool TryAddCommand(IReadOnlyModificationCommand command) => throw new NotSupportedException();
            public override void Complete(bool moreBatchesExpected) => throw new NotSupportedException();
            public override void Execute(IRelationalConnection connection) => throw new NotSupportedException();
            public override Task ExecuteAsync(IRelationalConnection connection, CancellationToken cancellationToken = default)
            {
                if (cancellationToken != source.Token) throw new Exception("Batch lost the caller token.");
                source.Cancel();
                return Task.FromException(failure);
            }
        }

        private sealed class NativeConnection : DbConnection
        {
            private ConnectionState _state;
            public List<(string Sql, CancellationToken Token)> Commands { get; } = [];
            [AllowNull] public override string ConnectionString { get; set; } = "";
            public override string Database => "offline";
            public override string DataSource => "offline";
            public override string ServerVersion => "offline";
            public override ConnectionState State => _state;
            public override void Open() => _state = ConnectionState.Open;
            public override void Close() => _state = ConnectionState.Closed;
            public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
            protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => new Transaction(this);
            protected override DbCommand CreateDbCommand() => new Command(this);
        }

        private sealed class Transaction(DbConnection connection) : DbTransaction
        {
            public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
            protected override DbConnection DbConnection => connection;
            public override void Commit() => throw new NotSupportedException();
            public override void Rollback() => throw new NotSupportedException();
        }

        private sealed class Command(NativeConnection connection) : DbCommand
        {
            private readonly OceanBaseCommand _parameters = new();
            [AllowNull] public override string CommandText { get; set; } = "";
            public override int CommandTimeout { get; set; }
            public override CommandType CommandType { get; set; }
            public override bool DesignTimeVisible { get; set; }
            public override UpdateRowSource UpdatedRowSource { get; set; }
            protected override DbConnection? DbConnection { get; set; } = connection;
            protected override DbTransaction? DbTransaction { get; set; }
            protected override DbParameterCollection DbParameterCollection => _parameters.Parameters;
            protected override DbParameter CreateDbParameter() => new OceanBaseParameter();
            public override void Cancel() => throw new NotSupportedException();
            public override void Prepare() => throw new NotSupportedException();
            public override object ExecuteScalar() => throw new NotSupportedException();
            public override int ExecuteNonQuery() { connection.Commands.Add((CommandText, default)); return 0; }
            public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                connection.Commands.Add((CommandText, cancellationToken));
                return Task.FromResult(0);
            }
            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
            protected override void Dispose(bool disposing) { if (disposing) _parameters.Dispose(); base.Dispose(disposing); }
        }
    }
}
