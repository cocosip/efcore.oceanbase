using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace OceanBase.EntityFrameworkCore.FunctionalTests
{
    public sealed class SeedingTests
    {
        public static IEnumerable<object[]> Modes()
        {
            foreach (var mode in Enum.GetValues<OceanBaseCompatibilityMode>())
                foreach (var async in new[] { false, true }) yield return [mode, async];
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Matching_callback_runs_once_after_schema_work_and_saves_inside_its_owned_transaction(OceanBaseCompatibilityMode mode, bool async)
        {
            foreach (var populated in new[] { false, true })
            {
                var capture = new Capture { HasTables = populated };
                using var source = new CancellationTokenSource();
                var calls = 0;
                void Check(DbContext context, bool performed)
                {
                    calls++;
                    Assert.Equal(!populated, performed);
                    Assert.Same(capture.Transaction, context.Database.CurrentTransaction!.GetDbTransaction());
                    capture.Events.Add("callback");
                    context.Add(new Row { Id = 13, Name = "seed" });
                }
                await using var context = Create(mode, capture,
                    (db, performed) => { Assert.False(async); Check(db, performed); db.SaveChanges(); },
                    async (db, performed, token) =>
                    {
                        Assert.True(async);
                        Assert.Equal(source.Token, token);
                        Check(db, performed);
                        await db.SaveChangesAsync(token);
                    });
                Assert.Equal(!populated, await Ensure(context, async, source.Token));
                Assert.Equal(1, calls);
                Assert.Equal(1, capture.Commits);
                Assert.Equal(1, capture.Disposals);
                Assert.Equal(async, capture.BeganAsync);
                Assert.Equal(async, capture.DisposedAsync);
                Assert.Null(context.Database.CurrentTransaction);
                var insert = Assert.Single(capture.Commands, command => command.Sql.StartsWith("INSERT", StringComparison.Ordinal));
                Assert.Same(capture.Transaction, insert.Transaction);
                Assert.All(capture.Commands.Where(command => command.Sql.StartsWith("CREATE", StringComparison.Ordinal)), command => Assert.Null(command.Transaction));
                Assert.True(capture.Events.IndexOf("begin") < capture.Events.IndexOf("callback"));
                Assert.Equal(new[] { "commit", "dispose" }, capture.Events.TakeLast(2));
                if (async)
                {
                    Assert.Equal(source.Token, capture.BeginToken);
                    Assert.Equal(source.Token, capture.CommitToken);
                    Assert.All(capture.Commands, command => Assert.Equal(source.Token, command.Token));
                }
            }
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Empty_models_still_seed_with_false_and_verify_scope_inside_the_transaction(OceanBaseCompatibilityMode mode, bool async)
        {
            var capture = new Capture();
            var calls = 0;
            void Seed(DbContext db, bool performed) { calls++; Assert.False(performed); Assert.NotNull(db.Database.CurrentTransaction); }
            await using var context = Create(mode, capture, Seed, (db, performed, _) => { Seed(db, performed); return Task.CompletedTask; }, empty: true);
            Assert.False(await Ensure(context, async));
            Assert.Equal(1, calls);
            Assert.Same(capture.Transaction, Assert.Single(capture.Commands).Transaction);
            Assert.Equal(1, capture.Commits);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Only_opposite_callback_rejects_before_any_schema_open_or_DDL(OceanBaseCompatibilityMode mode, bool async)
        {
            var capture = new Capture();
            await using var context = Create(mode, capture,
                async ? (_, _) => throw new Exception("Wrong callback.") : null,
                async ? null : (_, _, _) => throw new Exception("Wrong callback."));
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Ensure(context, async));
            Assert.Equal(CoreStrings.MissingSeeder, error.Message);
            Assert.Empty(capture.Events);
            Assert.Empty(capture.Commands);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Callback_and_commit_errors_remain_original_and_dispose_uncommitted_transactions(OceanBaseCompatibilityMode mode, bool async)
        {
            foreach (var failCommit in new[] { false, true })
            {
                var failure = new InvalidOperationException("synthetic seeding failure");
                var capture = new Capture { HasTables = true, CommitFailure = failCommit ? failure : null };
                void Seed(DbContext _, bool __) { if (!failCommit) throw failure; }
                await using var context = Create(mode, capture, Seed, (db, performed, _) => { Seed(db, performed); return Task.CompletedTask; });
                var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => Ensure(context, async));
                Assert.Same(failure, actual);
                Assert.Equal(1, capture.Disposals);
                Assert.Null(context.Database.CurrentTransaction);
                Assert.Equal(failCommit ? 1 : 0, capture.Commits);
            }
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Cleanup_errors_attach_to_primary_and_propagate_when_there_is_no_primary(OceanBaseCompatibilityMode mode, bool async)
        {
            foreach (var callbackFails in new[] { false, true })
            {
                var failure = new InvalidOperationException("synthetic callback failure");
                var cleanup = new InvalidOperationException("synthetic transaction disposal failure");
                var capture = new Capture { HasTables = true, CleanupFailure = cleanup };
                void Seed(DbContext _, bool __) { if (callbackFails) throw failure; }
                await using var context = Create(mode, capture, Seed, (db, performed, _) => { Seed(db, performed); return Task.CompletedTask; });
                var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => Ensure(context, async));
                Assert.Same(callbackFails ? failure : cleanup, actual);
                if (callbackFails) Assert.Same(cleanup, actual.Data["OceanBase.SeedingCleanupError"]);
                Assert.Equal(1, capture.Disposals);
            }
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task DDL_failure_never_begins_seeding_and_never_drops_partial_schema(OceanBaseCompatibilityMode mode, bool async)
        {
            var failure = new InvalidOperationException("synthetic create table failure");
            var capture = new Capture { DdlFailure = failure };
            await using var context = Create(mode, capture, (_, _) => throw new Exception("Must not seed."), (_, _, _) => throw new Exception("Must not seed."));
            var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => Ensure(context, async));
            Assert.Same(failure, actual);
            Assert.DoesNotContain("begin", capture.Events);
            Assert.DoesNotContain(capture.Commands, command => command.Sql.StartsWith("DROP", StringComparison.Ordinal));
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Empty_model_cannot_seed_a_different_actual_schema(OceanBaseCompatibilityMode mode, bool async)
        {
            var capture = new Capture { Scope = "other" };
            await using var context = Create(mode, capture, (_, _) => throw new Exception("Must not seed."), (_, _, _) => throw new Exception("Must not seed."), empty: true);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Ensure(context, async));
            Assert.Equal(0, capture.Commits);
            Assert.Equal(1, capture.Disposals);
            Assert.Null(context.Database.CurrentTransaction);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Direct_CreateTables_ignores_callbacks_and_does_not_begin_a_seed_transaction(OceanBaseCompatibilityMode mode, bool async)
        {
            var capture = new Capture();
            await using var context = Create(mode, capture, (_, _) => throw new Exception("Must not seed."), (_, _, _) => throw new Exception("Must not seed."));
            var creator = context.GetService<IRelationalDatabaseCreator>();
            if (async) await creator.CreateTablesAsync();
            else creator.CreateTables();
            Assert.Equal(2, capture.Commands.Count);
            Assert.DoesNotContain("begin", capture.Events);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Seed_scope_probe_failure_after_DDL_preserves_the_error_and_never_invokes_callback(OceanBaseCompatibilityMode mode, bool async)
        {
            var failure = new InvalidOperationException("synthetic transactional scope probe failure");
            var capture = new Capture { SeedProbeFailure = failure };
            await using var context = Create(mode, capture, (_, _) => throw new Exception("Must not seed."), (_, _, _) => throw new Exception("Must not seed."));
            var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => Ensure(context, async));
            Assert.Same(failure, actual);
            Assert.Contains(capture.Commands, command => command.Sql.StartsWith("CREATE TABLE", StringComparison.Ordinal));
            Assert.Same(capture.Transaction, capture.Commands.Last().Transaction);
            Assert.Equal(0, capture.Commits);
            Assert.Equal(1, capture.Disposals);
            Assert.Null(context.Database.CurrentTransaction);
            Assert.DoesNotContain(capture.Commands, command => command.Sql.StartsWith("DROP", StringComparison.Ordinal));
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Scope_change_after_DDL_rejects_seeding_without_claiming_that_no_DDL_ran(OceanBaseCompatibilityMode mode, bool async)
        {
            var capture = new Capture { SeedScope = "other" };
            await using var context = Create(mode, capture, (_, _) => throw new Exception("Must not seed."), (_, _, _) => throw new Exception("Must not seed."));
            var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => Ensure(context, async));
            Assert.Contains("does not match the declared schema management scope", actual.Message);
            Assert.DoesNotContain("No DDL was executed", actual.Message);
            Assert.Contains(capture.Commands, command => command.Sql.StartsWith("CREATE TABLE", StringComparison.Ordinal));
            Assert.Equal(0, capture.Commits);
            Assert.Equal(1, capture.Disposals);
            Assert.Null(context.Database.CurrentTransaction);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public async Task Cancellation_after_callback_returns_prevents_commit_and_disposes_the_seed_transaction(OceanBaseCompatibilityMode mode)
        {
            var capture = new Capture { HasTables = true };
            using var source = new CancellationTokenSource();
            var calls = 0;
            await using var context = Create(mode, capture, null, (_, _, token) =>
            {
                Assert.Equal(source.Token, token);
                calls++;
                source.Cancel();
                return Task.CompletedTask;
            });
            var actual = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Ensure(context, true, source.Token));
            Assert.Equal(source.Token, actual.CancellationToken);
            Assert.Equal(source.Token, capture.CommitToken);
            Assert.Equal(1, calls);
            Assert.Equal(0, capture.Commits);
            Assert.Equal(1, capture.Disposals);
            Assert.True(capture.DisposedAsync);
            Assert.Null(context.Database.CurrentTransaction);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public async Task Precancelled_and_callback_cancellation_keep_the_token_and_do_not_commit(OceanBaseCompatibilityMode mode)
        {
            var capture = new Capture { HasTables = true };
            using var source = new CancellationTokenSource();
            var failure = new OperationCanceledException(source.Token);
            await using var context = Create(mode, capture, null, (_, _, token) =>
            {
                Assert.Equal(source.Token, token);
                source.Cancel();
                throw failure;
            });
            using var pre = new CancellationTokenSource();
            pre.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Ensure(context, true, pre.Token));
            Assert.Empty(capture.Events);
            var actual = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Ensure(context, true, source.Token));
            Assert.Same(failure, actual);
            Assert.Equal(0, capture.Commits);
            Assert.Equal(1, capture.Disposals);
            Assert.True(capture.DisposedAsync);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Owned_MySql_namespace_creates_tables_before_starting_the_target_seed_transaction(bool async)
        {
            var failure = new InvalidOperationException("synthetic target transaction start failure");
            var capture = new Capture { BeginFailure = failure };
            await using var context = Create(OceanBaseCompatibilityMode.MySql, capture,
                (_, _) => throw new Exception("Must not seed after failed begin."),
                (_, _, _) => throw new Exception("Must not seed after failed begin."), database: true);
            var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => Ensure(context, async));
            Assert.Same(failure, actual);
            var creation = Assert.Single(capture.Commands, command => command.Sql.StartsWith("CREATE DATABASE", StringComparison.Ordinal));
            Assert.Equal("", creation.Database);
            Assert.Null(creation.Transaction);
            Assert.Contains(capture.Commands, command => command.Sql.StartsWith("CREATE TABLE", StringComparison.Ordinal));
            Assert.Equal("owned", capture.BeginDatabase);
            Assert.Equal(0, capture.Commits);
        }

        private static Task<bool> Ensure(DbContext context, bool async, CancellationToken token = default)
            => async ? context.Database.EnsureCreatedAsync(token) : Task.FromResult(context.Database.EnsureCreated());

        private static SeedContext Create(OceanBaseCompatibilityMode mode, Capture capture, Action<DbContext, bool>? seed,
            Func<DbContext, bool, CancellationToken, Task>? asyncSeed, bool empty = false, bool database = false)
        {
            var builder = new DbContextOptionsBuilder<SeedContext>().UseOceanBase("Server=offline;Database=owned", mode)
                .AddInterceptors(capture).ReplaceService<IModelCacheKeyFactory, ShapeKey>()
                // Keep this fixture's replacement services out of EF's process-wide provider cache.
                .EnableServiceProviderCaching(false);
            if (database) builder.EnableOceanBaseDatabaseManagement("owned");
            else builder.EnableOceanBaseSchemaManagement("owned").ReplaceService<IRelationalConnection, SeedConnection>();
            if (seed is not null) builder.UseSeeding(seed);
            if (asyncSeed is not null) builder.UseAsyncSeeding(asyncSeed);
            return new SeedContext(builder.Options, empty);
        }

        public sealed class ShapeKey(ModelCacheKeyFactoryDependencies dependencies) : ModelCacheKeyFactory(dependencies)
        {
            public override object Create(DbContext context, bool designTime) => (base.Create(context, designTime), ((SeedContext)context).Empty);
        }

        // A physical-state double keeps EF from discarding an active transaction on a closed intercepted socket.
        public sealed class SeedConnection(RelationalConnectionDependencies dependencies) : RelationalConnection(dependencies)
        {
            protected override DbConnection CreateDbConnection() => new SeedDbConnection(GetValidatedConnectionString());
        }

        private sealed class SeedDbConnection(string configuration) : DbConnection
        {
            private ConnectionState _state;
            [System.Diagnostics.CodeAnalysis.AllowNull] public override string ConnectionString { get; set; } = configuration;
            public override string Database => "owned";
            public override string DataSource => "offline";
            public override string ServerVersion => "offline";
            public override ConnectionState State => _state;
            public override void Open() => _state = ConnectionState.Open;
            public override void Close() => _state = ConnectionState.Closed;
            public override Task OpenAsync(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); Open(); return Task.CompletedTask; }
            public override Task CloseAsync() { Close(); return Task.CompletedTask; }
            public override void ChangeDatabase(string databaseName) => throw new InvalidOperationException("Must not change schema.");
            protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new InvalidOperationException("Must be intercepted.");
            protected override DbCommand CreateDbCommand() => new SeedCommand(this);
        }

        private sealed class SeedContext(DbContextOptions<SeedContext> options, bool empty) : DbContext(options)
        {
            public bool Empty => empty;
            protected override void OnModelCreating(ModelBuilder model)
            {
                if (!Empty) model.Entity<Row>().Property(row => row.Id).ValueGeneratedNever();
            }
        }

        private sealed class Row { public int Id { get; set; } public string Name { get; set; } = ""; }
        private sealed record Command(string Sql, string Database, DbTransaction? Transaction, CancellationToken Token);

        private sealed class Capture : DbConnectionInterceptor, IDbCommandInterceptor, IDbTransactionInterceptor
        {
            public List<string> Events { get; } = [];
            public List<Command> Commands { get; } = [];
            public bool HasTables { get; init; }
            public bool NamespaceExists { get; set; }
            public string Scope { get; init; } = "owned";
            public string? SeedScope { get; init; }
            public NativeTransaction? Transaction { get; private set; }
            public int Commits { get; set; }
            public int Disposals { get; set; }
            public bool BeganAsync { get; set; }
            public bool DisposedAsync { get; set; }
            public CancellationToken BeginToken { get; set; }
            public CancellationToken CommitToken { get; set; }
            public string? BeginDatabase { get; set; }
            public Exception? BeginFailure { get; init; }
            public Exception? DdlFailure { get; init; }
            public Exception? SeedProbeFailure { get; init; }
            public Exception? CommitFailure { get; init; }
            public Exception? CleanupFailure { get; set; }
            public InterceptionResult<DbCommand> CommandCreating(CommandCorrelatedEventData eventData, InterceptionResult<DbCommand> result)
                => InterceptionResult<DbCommand>.SuppressWithResult(new SeedCommand(eventData.Connection));
            public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
            { Events.Add("open"); return connection is SeedDbConnection ? result : InterceptionResult.Suppress(); }
            public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
                InterceptionResult result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(ConnectionOpening(connection, eventData, result));
            public InterceptionResult<DbTransaction> TransactionStarting(DbConnection connection, TransactionStartingEventData eventData,
                InterceptionResult<DbTransaction> result)
            {
                Events.Add("begin");
                BeginDatabase = connection.Database;
                if (BeginFailure is { } failure) throw failure;
                return InterceptionResult<DbTransaction>.SuppressWithResult(Transaction = new NativeTransaction(connection, this));
            }
            public ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection, TransactionStartingEventData eventData,
                InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
            { BeganAsync = true; BeginToken = cancellationToken; return ValueTask.FromResult(TransactionStarting(connection, eventData, result)); }
            public InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
                => InterceptionResult<object>.SuppressWithResult(Scalar(command, default));
            public ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData,
                InterceptionResult<object> result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(InterceptionResult<object>.SuppressWithResult(Scalar(command, cancellationToken)));
            private object Scalar(DbCommand command, CancellationToken token)
            {
                Record(command, token);
                if (command.Transaction is not null && SeedProbeFailure is { } failure) throw failure;
                if (command.Transaction is not null && SeedScope is { } scope) return scope;
                if (command.CommandText.StartsWith("SELECT SCHEMA_NAME", StringComparison.Ordinal)) return NamespaceExists ? "owned" : DBNull.Value;
                return command.CommandText.StartsWith("SELECT COUNT", StringComparison.Ordinal) ? HasTables ? 1 : 0 : Scope;
            }
            public InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
                => Write(command, default);
            public ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
                InterceptionResult<int> result, CancellationToken cancellationToken = default) => ValueTask.FromResult(Write(command, cancellationToken));
            private InterceptionResult<int> Write(DbCommand command, CancellationToken token)
            {
                Record(command, token);
                if (command.CommandText.StartsWith("CREATE TABLE", StringComparison.Ordinal) && DdlFailure is { } error) throw error;
                if (command.CommandText.StartsWith("CREATE DATABASE", StringComparison.Ordinal)) NamespaceExists = true;
                return InterceptionResult<int>.SuppressWithResult(1);
            }
            private void Record(DbCommand command, CancellationToken token)
                => Commands.Add(new Command(command.CommandText, command.Connection!.Database, command.Transaction, token));
        }

        private sealed class NativeTransaction(DbConnection connection, Capture capture) : DbTransaction
        {
            private bool _disposed;
            protected override DbConnection DbConnection => connection;
            public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
            public override void Commit()
            {
                capture.Commits++;
                capture.Events.Add("commit");
                if (capture.CommitFailure is { } failure) throw failure;
            }
            public override Task CommitAsync(CancellationToken cancellationToken = default)
            { capture.CommitToken = cancellationToken; cancellationToken.ThrowIfCancellationRequested(); Commit(); return Task.CompletedTask; }
            public override void Rollback() => throw new InvalidOperationException("No explicit rollback is expected from the EF seeding profile.");
            protected override void Dispose(bool disposing)
            {
                if (disposing && !_disposed)
                {
                    _disposed = true;
                    capture.Disposals++;
                    capture.Events.Add("dispose");
                    if (capture.CleanupFailure is { } failure) { capture.CleanupFailure = null; throw failure; }
                }
                base.Dispose(disposing);
            }
            public override ValueTask DisposeAsync() { capture.DisposedAsync = true; Dispose(); return ValueTask.CompletedTask; }
        }

        private sealed class SeedCommand(DbConnection connection) : DbCommand
        {
            private readonly OceanBaseConnector.OceanBaseCommand _parameters = new();
            [System.Diagnostics.CodeAnalysis.AllowNull] public override string CommandText { get; set; } = "";
            public override int CommandTimeout { get; set; }
            public override CommandType CommandType { get; set; }
            public override bool DesignTimeVisible { get; set; }
            public override UpdateRowSource UpdatedRowSource { get; set; }
            protected override DbConnection? DbConnection { get; set; } = connection;
            protected override DbTransaction? DbTransaction { get; set; }
            protected override DbParameterCollection DbParameterCollection => _parameters.Parameters;
            protected override DbParameter CreateDbParameter() => _parameters.CreateParameter();
            public override void Cancel() => throw new InvalidOperationException("Must be intercepted.");
            public override void Prepare() => throw new InvalidOperationException("Must be intercepted.");
            public override int ExecuteNonQuery()
                => CommandText.StartsWith("SAVEPOINT", StringComparison.Ordinal)
                    || CommandText.StartsWith("RELEASE SAVEPOINT", StringComparison.Ordinal)
                    || CommandText.StartsWith("ROLLBACK TO", StringComparison.Ordinal) ? 0
                    : throw new InvalidOperationException("Must be intercepted.");
            public override object ExecuteScalar() => throw new InvalidOperationException("Must be intercepted.");
            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new InvalidOperationException("Must be intercepted.");
            protected override void Dispose(bool disposing) { if (disposing) _parameters.Dispose(); base.Dispose(disposing); }
        }
    }
}
