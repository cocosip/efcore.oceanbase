using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBase.EntityFrameworkCore;

namespace PackageConsumer
{
    internal static class SeedingSmoke
    {
        public static void Run(OceanBaseCompatibilityMode mode)
        {
            foreach (var async in new[] { false, true })
            {
                using var source = new CancellationTokenSource();
                var calls = 0;
                void Seed(DbContext db, bool performed, bool isAsync)
                {
                    if (async != isAsync || performed != (calls == 0) || db.Database.CurrentTransaction is null)
                        throw new Exception("Packed seeding lost its callback, created flag or transaction.");
                    calls++;
                }
                using var context = new SeedContext(new DbContextOptionsBuilder<SeedContext>()
                    .UseOceanBase("Server=offline;Database=owned", mode).EnableOceanBaseSchemaManagement("owned")
                    .ReplaceService<IRelationalConnection, Connection>().EnableServiceProviderCaching(false)
                    .UseSeeding((db, performed) => Seed(db, performed, false))
                    .UseAsyncSeeding((db, performed, token) =>
                    {
                        if (token != source.Token) throw new Exception("Packed seeding lost its cancellation token.");
                        Seed(db, performed, true);
                        return Task.CompletedTask;
                    }).Options);
                var native = (NativeConnection)context.Database.GetDbConnection();
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    var created = async ? context.Database.EnsureCreatedAsync(source.Token).GetAwaiter().GetResult()
                        : context.Database.EnsureCreated();
                    if (created != (attempt == 0)) throw new Exception("Packed EnsureCreated changed its return value.");
                }
                if (calls != 2 || native.Commits != 2 || native.Disposals != 2 || native.SeedScopeProbes != 2
                    || context.Database.CurrentTransaction is not null || native.State != ConnectionState.Closed)
                    throw new Exception("Packed seeding lost scope validation, commit or cleanup.");
            }
            Console.WriteLine($"{mode}: packed sync/async EnsureCreated seeding passed (native doubles).");
        }

        private sealed class SeedContext(DbContextOptions<SeedContext> options) : DbContext(options)
        {
            protected override void OnModelCreating(ModelBuilder model) => model.Entity<Row>().Property(row => row.Id).ValueGeneratedNever();
        }
        private sealed class Row { public int Id { get; set; } }

        public sealed class Connection(RelationalConnectionDependencies dependencies) : RelationalConnection(dependencies)
        {
            protected override DbConnection CreateDbConnection() => new NativeConnection();
        }

        private sealed class NativeConnection : DbConnection
        {
            private ConnectionState _state;
            public bool HasTables { get; set; }
            public int Commits { get; set; }
            public int Disposals { get; set; }
            public int SeedScopeProbes { get; set; }
            [AllowNull] public override string ConnectionString { get; set; } = "";
            public override string Database => "owned";
            public override string DataSource => "offline";
            public override string ServerVersion => "offline";
            public override ConnectionState State => _state;
            public override void Open() => _state = ConnectionState.Open;
            public override void Close() => _state = ConnectionState.Closed;
            public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
            protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => new Transaction(this);
            protected override DbCommand CreateDbCommand() => new Command(this);
        }

        private sealed class Transaction(NativeConnection connection) : DbTransaction
        {
            public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
            protected override DbConnection DbConnection => connection;
            public override void Commit() => connection.Commits++;
            public override void Rollback() => throw new NotSupportedException();
            protected override void Dispose(bool disposing) { if (disposing) connection.Disposals++; base.Dispose(disposing); }
        }

        private sealed class Command(NativeConnection connection) : DbCommand
        {
            private readonly OceanBaseConnector.OceanBaseCommand _parameters = new();
            [AllowNull] public override string CommandText { get; set; } = "";
            public override int CommandTimeout { get; set; }
            public override CommandType CommandType { get; set; }
            public override bool DesignTimeVisible { get; set; }
            public override UpdateRowSource UpdatedRowSource { get; set; }
            protected override DbConnection? DbConnection { get; set; } = connection;
            protected override DbTransaction? DbTransaction { get; set; }
            protected override DbParameterCollection DbParameterCollection => _parameters.Parameters;
            protected override DbParameter CreateDbParameter() => _parameters.CreateParameter();
            public override object ExecuteScalar()
            {
                if (CommandText.StartsWith("SELECT COUNT", StringComparison.Ordinal)) return connection.HasTables ? 1 : 0;
                if (CommandText is not "SELECT DATABASE()" and not "SELECT SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA') FROM DUAL")
                    throw new Exception("Unexpected packed seeding command.");
                if (DbTransaction is not null) connection.SeedScopeProbes++;
                return "owned";
            }
            public override int ExecuteNonQuery()
            {
                if (!CommandText.StartsWith("CREATE TABLE", StringComparison.Ordinal) || DbTransaction is not null)
                    throw new Exception("Unexpected packed seeding DDL or DDL transaction.");
                connection.HasTables = true;
                return 0;
            }
            public override void Cancel() => throw new NotSupportedException();
            public override void Prepare() => throw new NotSupportedException();
            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
            protected override void Dispose(bool disposing) { if (disposing) _parameters.Dispose(); base.Dispose(disposing); }
        }
    }
}
