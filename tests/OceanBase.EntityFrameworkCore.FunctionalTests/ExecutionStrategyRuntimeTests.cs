using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.FunctionalTests;

public sealed class ExecutionStrategyRuntimeTests
{
    public static IEnumerable<object[]> Modes()
    {
        foreach (var mode in Enum.GetValues<OceanBaseCompatibilityMode>())
            foreach (var async in new[] { false, true }) yield return [mode, async];
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Buffered_reader_retries_before_delivering_any_partial_result(OceanBaseCompatibilityMode mode, bool async)
    {
        var capture = new Commands { ReadFailure = Transient(mode) };
        await using var context = Create(mode, capture);
        var query = context.Set<Principal>().OrderBy(p => p.Id).Select(p => p.Id);
        var values = new List<int>();
        if (async)
        {
            await using var reader = query.AsAsyncEnumerable().GetAsyncEnumerator();
            Assert.True(await reader.MoveNextAsync());
            values.Add(reader.Current);
            Assert.Equal(2, capture.QueryAttempts);
            Assert.Equal(3, capture.Readers[1].Reads);
            while (await reader.MoveNextAsync()) values.Add(reader.Current);
        }
        else
        {
            using var reader = query.AsEnumerable().GetEnumerator();
            Assert.True(reader.MoveNext());
            values.Add(reader.Current);
            Assert.Equal(2, capture.QueryAttempts);
            Assert.Equal(3, capture.Readers[1].Reads);
            while (reader.MoveNext()) values.Add(reader.Current);
        }
        Assert.Equal(new[] { 101, 102 }, values);
        Assert.All(capture.Readers, r => Assert.True(r.Disposed));
        Assert.Equal(ConnectionState.Closed, Native(context).State);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Unknown_reader_outcome_is_not_replayed_and_later_operation_can_run(OceanBaseCompatibilityMode mode, bool async)
    {
        var error = ServerError(9999, unknown: true);
        var capture = new Commands { ReadFailure = error };
        await using var context = Create(mode, capture);
        var query = context.Set<Principal>().Select(p => p.Id);
        Assert.Same(error, async ? await Assert.ThrowsAsync<OceanBaseException>(() => query.ToArrayAsync())
            : Assert.Throws<OceanBaseException>(() => query.ToArray()));
        Assert.Equal(1, capture.QueryAttempts);
        Assert.True(capture.Readers[0].Disposed);
        Assert.Equal(ConnectionState.Closed, Native(context).State);
        capture.ReadFailure = null;
        Assert.Equal(new[] { 101, 102 }, async ? await query.ToArrayAsync() : query.ToArray());
        Assert.Equal(2, capture.QueryAttempts);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Reader_cleanup_cannot_mask_unknown_outcome_or_trigger_replay(OceanBaseCompatibilityMode mode, bool async)
    {
        foreach (var failRead in new[] { false, true })
        {
            var primary = ServerError(9999, unknown: true);
            var cleanup = Transient(mode);
            var capture = new Commands { ReadFailure = failRead ? primary : null, ReaderDisposeFailure = cleanup };
            await using var context = Create(mode, capture);
            var query = context.Set<Principal>().Select(p => p.Id);
            var actual = async ? await Assert.ThrowsAsync<OceanBaseException>(() => query.ToArrayAsync())
                : Assert.Throws<OceanBaseException>(() => query.ToArray());
            Assert.Same(failRead ? primary : cleanup, actual);
            if (failRead) Assert.Same(cleanup, actual.Data["OceanBase.ReaderCleanupError"]);
            Assert.Equal(1, capture.QueryAttempts);
            Assert.Equal(ConnectionState.Closed, Native(context).State);
        }
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Buffered_reader_exhaustion_retains_last_error_and_disposes_every_attempt(OceanBaseCompatibilityMode mode, bool async)
    {
        var error = Transient(mode);
        var capture = new Commands { ReadFailure = error, PersistentReadFailure = true };
        await using var context = Create(mode, capture);
        var query = context.Set<Principal>().Select(p => p.Id);
        var actual = async ? await Assert.ThrowsAsync<RetryLimitExceededException>(() => query.ToArrayAsync())
            : Assert.Throws<RetryLimitExceededException>(() => query.ToArray());
        Assert.Same(error, actual.InnerException);
        Assert.Equal(3, capture.QueryAttempts);
        Assert.All(capture.Readers, reader => Assert.True(reader.Disposed));
        Assert.Equal(ConnectionState.Closed, Native(context).State);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Cancelled_save_rolls_back_without_cancelled_token_and_never_replays(OceanBaseCompatibilityMode mode, bool async)
    {
        using var source = new CancellationTokenSource();
        var error = new OperationCanceledException(source.Token);
        var rollback = new RollbackCapture();
        await using var context = Create(mode, new Commands(), rollback);
        var native = Native(context);
        native.WriteFailure = error;
        native.BeforeWriteFailure = source.Cancel;
        var principal = new Principal { Name = "cancelled", Details = [new Detail { Id = 7 }] };
        context.Add(principal);
        Assert.Same(error, async ? await Assert.ThrowsAsync<OperationCanceledException>(() => context.SaveChangesAsync(source.Token))
            : Assert.Throws<OperationCanceledException>(() => context.SaveChanges()));
        Assert.Equal(1, Assert.Single(native.Transactions).Rollbacks);
        Assert.Equal(1, rollback.Attempts);
        Assert.Equal(CancellationToken.None, rollback.LastToken);
        Assert.All(context.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Added, entry.State));
        Assert.True(context.Entry(principal).Property(p => p.Id).IsTemporary);
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Equal(ConnectionState.Closed, native.State);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Generated_graph_save_confirms_rollback_then_restores_temporary_keys_for_replay(OceanBaseCompatibilityMode mode, bool async)
    {
        var capture = new Commands();
        await using var context = Create(mode, capture);
        var native = Native(context);
        native.WriteFailure = Transient(mode);
        var principal = new Principal { Name = "new", Details = [new Detail { Id = 7, Value = "child" }] };
        context.Add(principal);
        Assert.True(context.Entry(principal).Property(p => p.Id).IsTemporary);
        Assert.Equal(2, async ? await context.SaveChangesAsync() : context.SaveChanges());
        Assert.Equal(201, principal.Id);
        Assert.Equal(201, principal.Details.Single().PrincipalId);
        Assert.All(context.ChangeTracker.Entries(), e => Assert.Equal(EntityState.Unchanged, e.State));
        Assert.Equal(2, native.Transactions.Count);
        Assert.Equal(1, native.Transactions[0].Rollbacks);
        Assert.Equal(0, native.Transactions[0].Commits);
        Assert.False(native.Transactions[0].DisposedActive);
        Assert.Equal(1, native.Transactions[1].Commits);
        Assert.All(native.Transactions, t => Assert.Equal(1, t.Disposals));
        var dependents = capture.Items.Where(c => c.Sql.StartsWith("INSERT", StringComparison.Ordinal) && c.Sql.Contains("Details", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, dependents.Length);
        Assert.Contains(101, dependents[0].Inputs);
        Assert.Contains(201, dependents[1].Inputs);
        Assert.DoesNotContain(101, dependents[1].Inputs);
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Equal(ConnectionState.Closed, native.State);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Assigned_key_save_retries_with_the_same_keys_and_inputs(OceanBaseCompatibilityMode mode, bool async)
    {
        var capture = new Commands();
        await using var context = Create(mode, capture);
        Native(context).WriteFailure = Transient(mode);
        context.AddRange(new Detail { Id = 7, PrincipalId = 41, Value = "first" },
            new Detail { Id = 8, PrincipalId = 41, Value = "second" });
        Assert.Equal(2, async ? await context.SaveChangesAsync() : context.SaveChanges());
        var inserts = capture.Items.Where(command => command.Sql.StartsWith("INSERT", StringComparison.Ordinal)).ToArray();
        Assert.Equal(4, inserts.Length);
        for (var index = 0; index < 2; index++)
        {
            Assert.Equal(inserts[index].Sql, inserts[index + 2].Sql);
            Assert.Equal(inserts[index].Inputs, inserts[index + 2].Inputs);
        }
        Assert.Equal(new[] { 7, 8 }, context.ChangeTracker.Entries<Detail>().Select(entry => entry.Entity.Id).Order());
        Assert.All(context.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
        Assert.Equal(1, Native(context).Transactions[0].Rollbacks);
        Assert.Equal(1, Native(context).Transactions[1].Commits);
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Equal(ConnectionState.Closed, Native(context).State);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Failed_rollback_or_disposal_preserves_primary_entries_and_prohibits_replay(OceanBaseCompatibilityMode mode, bool async)
    {
        foreach (var phase in new[] { "rollback", "dispose" })
        {
            var capture = new Commands();
            var cleanup = ServerError(9999, unknown: true);
            var rollback = new RollbackCapture { Failure = phase == "rollback" ? cleanup : null };
            await using var context = Create(mode, capture, rollback);
            var native = Native(context);
            var primary = Transient(mode);
            native.WriteFailure = primary;
            if (phase == "dispose") native.TransactionDisposeFailure = cleanup;
            var principal = new Principal { Name = "new", Details = [new Detail { Id = 7 }] };
            context.Add(principal);
            var error = async ? await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync())
                : Assert.Throws<DbUpdateException>(() => context.SaveChanges());
            Assert.Same(primary, error.InnerException);
            Assert.Same(principal.Details.Single(), Assert.Single(error.Entries).Entity);
            Assert.Same(cleanup, error.Data["OceanBase.BatchCleanupError"]);
            Assert.Single(native.Transactions);
            Assert.Equal(1, capture.RootWrites);
            Assert.All(context.ChangeTracker.Entries(), e => Assert.Equal(EntityState.Added, e.State));
            Assert.True(context.Entry(principal).Property(p => p.Id).IsTemporary);
            Assert.True(rollback.Attempts > 0);
            if (async) Assert.Equal(CancellationToken.None, rollback.LastToken);
        }
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task User_transaction_inside_strategy_confirms_rollback_before_the_next_attempt(OceanBaseCompatibilityMode mode, bool async)
    {
        var capture = new Commands();
        await using var context = Create(mode, capture);
        var strategy = context.Database.CreateExecutionStrategy();
        var attempts = 0;
        if (async)
            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync();
                await context.Database.ExecuteSqlRawAsync("UPDATE Principals SET Name = 'replayable'");
                if (++attempts == 1) throw Transient(mode);
                await transaction.CommitAsync();
            });
        else
            strategy.Execute(() =>
            {
                using var transaction = context.Database.BeginTransaction();
                context.Database.ExecuteSqlRaw("UPDATE Principals SET Name = 'replayable'");
                if (++attempts == 1) throw Transient(mode);
                transaction.Commit();
            });
        Assert.Equal(2, attempts);
        Assert.Equal(2, Native(context).Transactions.Count);
        Assert.Equal(1, Native(context).Transactions[0].Rollbacks);
        Assert.Equal(1, Native(context).Transactions[1].Commits);
        Assert.Null(context.Database.CurrentTransaction);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task User_transaction_cleanup_failure_cannot_be_retried_even_with_a_transient_code(OceanBaseCompatibilityMode mode, bool async)
    {
        foreach (var phase in new[] { "rollback", "dispose" })
        {
            var cleanup = Transient(mode);
            var rollback = new RollbackCapture { Failure = phase == "rollback" ? cleanup : null };
            await using var context = Create(mode, new Commands(), rollback);
            if (phase == "dispose") Native(context).TransactionDisposeFailure = cleanup;
            var attempts = 0;
            void Execute()
            {
                using var transaction = context.Database.BeginTransaction();
                attempts++;
                throw Transient(mode);
            }
            async Task ExecuteAsync()
            {
                await using var transaction = await context.Database.BeginTransactionAsync();
                attempts++;
                throw Transient(mode);
            }
            var strategy = context.Database.CreateExecutionStrategy();
            Assert.Same(cleanup, async ? await Assert.ThrowsAsync<OceanBaseException>(() => strategy.ExecuteAsync(ExecuteAsync))
                : Assert.Throws<OceanBaseException>(() => strategy.Execute(Execute)));
            Assert.Equal(1, attempts);
            Assert.Single(Native(context).Transactions);
        }
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Committed_callback_failure_cannot_replay_a_completed_write(OceanBaseCompatibilityMode mode, bool async)
    {
        var error = Transient(mode);
        await using var context = Create(mode, new Commands(), new CommittedFailure(error));
        var attempts = 0;
        void Execute()
        {
            using var transaction = context.Database.BeginTransaction();
            attempts++;
            transaction.Commit();
        }
        async Task ExecuteAsync()
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            attempts++;
            await transaction.CommitAsync();
        }
        var strategy = context.Database.CreateExecutionStrategy();
        Assert.Same(error, async ? await Assert.ThrowsAsync<OceanBaseException>(() => strategy.ExecuteAsync(ExecuteAsync))
            : Assert.Throws<OceanBaseException>(() => strategy.Execute(Execute)));
        Assert.Equal(1, attempts);
        Assert.Equal(1, Assert.Single(Native(context).Transactions).Commits);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Ordinary_nonretry_transaction_keeps_existing_disposal_after_explicit_rollback_failure(OceanBaseCompatibilityMode mode, bool async)
    {
        var rollback = new RollbackCapture { Failure = Transient(mode) };
        await using var context = new RetryContext(new DbContextOptionsBuilder<RetryContext>()
            .UseOceanBase("Server=offline;Database=app", mode)
            .ReplaceService<IRelationalConnection, TransactionTests.TestConnection>()
            .EnableServiceProviderCaching(false).AddInterceptors(rollback).Options);
        var transaction = async ? await context.Database.BeginTransactionAsync() : context.Database.BeginTransaction();
        if (async) await Assert.ThrowsAsync<OceanBaseException>(() => transaction.RollbackAsync());
        else Assert.Throws<OceanBaseException>(transaction.Rollback);
        if (async) await transaction.DisposeAsync(); else transaction.Dispose();
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Equal(1, Assert.Single(Native(context).Transactions).Disposals);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Already_active_transaction_rejects_before_query_or_save_work(OceanBaseCompatibilityMode mode, bool async)
    {
        var capture = new Commands();
        await using var context = Create(mode, capture);
        await using var transaction = async ? await context.Database.BeginTransactionAsync() : context.Database.BeginTransaction();
        var query = context.Set<Principal>().Select(p => p.Id);
        if (async) await Assert.ThrowsAsync<InvalidOperationException>(() => query.ToArrayAsync());
        else Assert.Throws<InvalidOperationException>(() => query.ToArray());
        context.Add(new Principal { Name = "new" });
        if (async) await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        else Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
        Assert.Empty(capture.Items);
        Assert.Same(transaction, context.Database.CurrentTransaction);
        if (async) await transaction.RollbackAsync(); else transaction.Rollback();
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Bulk_update_and_delete_retry_confirmed_failures_without_changing_binds(OceanBaseCompatibilityMode mode, bool async)
    {
        foreach (var delete in new[] { false, true })
        {
            var capture = new Commands { NonQueryFailure = Transient(mode) };
            await using var context = Create(mode, capture);
            var query = context.Set<Principal>().Where(p => p.Id > 7);
            var name = "changed";
            var count = delete ? async ? await query.ExecuteDeleteAsync() : query.ExecuteDelete()
                : async ? await query.ExecuteUpdateAsync(setters => setters.SetProperty(p => p.Name, name))
                    : query.ExecuteUpdate(setters => setters.SetProperty(p => p.Name, name));
            Assert.Equal(1, count);
            Assert.Equal(2, capture.Items.Count);
            Assert.Equal(capture.Items[0].Sql, capture.Items[1].Sql);
            Assert.Equal(capture.Items[0].Inputs, capture.Items[1].Inputs);
            if (!delete) Assert.Contains(name, capture.Items[1].Inputs);
            Assert.Empty(context.ChangeTracker.Entries());
            Assert.Equal(ConnectionState.Closed, Native(context).State);
        }
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Bulk_increment_cannot_replay_after_connection_close_callback_failure(OceanBaseCompatibilityMode mode, bool async)
    {
        var error = Transient(mode);
        var close = new CloseFailure(error);
        var write = new SuccessfulWrite();
        await using var context = new RetryContext(new DbContextOptionsBuilder<RetryContext>()
            .UseOceanBase("Server=offline;Database=app", mode, options => options.EnableRetryOnFailure(2, TimeSpan.Zero))
            .EnableServiceProviderCaching(false).AddInterceptors(close, write).Options);
        var query = context.Set<Principal>().Where(p => p.Id == 7);
        Assert.Same(error, async ? await Assert.ThrowsAsync<OceanBaseException>(() => query.ExecuteUpdateAsync(
                setters => setters.SetProperty(p => p.Id, p => p.Id + 1)))
            : Assert.Throws<OceanBaseException>(() => query.ExecuteUpdate(setters => setters.SetProperty(p => p.Id, p => p.Id + 1))));
        Assert.Equal(1, write.Executions);
        Assert.Equal(1, close.Failures);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Ddl_rejects_configured_and_outer_retries_before_network_factories_or_seeders(OceanBaseCompatibilityMode mode, bool async)
    {
        foreach (var inside in new[] { false, true })
            foreach (var operation in new[] { "tables", "ensure", "migrate", "create", "delete", "ensure-delete" })
            {
                var network = new NoNetwork();
                var factories = 0;
                var seeders = 0;
                var builder = new DbContextOptionsBuilder<RetryContext>().UseOceanBase("Server=offline.invalid;Database=app;User ID=app", mode,
                    options => options.EnableRetryOnFailure(2, TimeSpan.Zero, [9999]))
                    .EnableOceanBaseSchemaManagement("app").EnableServiceProviderCaching(false)
                    .UseSeeding((_, _) => seeders++).UseAsyncSeeding((_, _, _) => { seeders++; return Task.CompletedTask; })
                    .AddInterceptors(network);
                if (operation is "create" or "delete" or "ensure-delete")
                {
                    if (mode == OceanBaseCompatibilityMode.MySql) builder.EnableOceanBaseDatabaseManagement("app");
                    else builder.EnableOceanBaseUserManagement("app", () => { factories++; return "unused-test-password"; });
                    builder.UseOceanBaseManagementConnection(() => { factories++; throw new InvalidOperationException("Management factory must not run."); });
                }
                await using var context = new RetryContext(builder.Options);
                var creator = context.GetService<IRelationalDatabaseCreator>();
                void Execute()
                {
                    switch (operation)
                    {
                        case "tables": creator.CreateTables(); break;
                        case "ensure": context.Database.EnsureCreated(); break;
                        case "migrate": context.Database.Migrate(); break;
                        case "create": creator.Create(); break;
                        case "delete": creator.Delete(); break;
                        default: context.Database.EnsureDeleted(); break;
                    }
                }
                async Task ExecuteAsync()
                {
                    switch (operation)
                    {
                        case "tables": await creator.CreateTablesAsync(); break;
                        case "ensure": await context.Database.EnsureCreatedAsync(); break;
                        case "migrate": await context.Database.MigrateAsync(); break;
                        case "create": await creator.CreateAsync(); break;
                        case "delete": await creator.DeleteAsync(); break;
                        default: await context.Database.EnsureDeletedAsync(); break;
                    }
                }
                var strategy = context.Database.CreateExecutionStrategy();
                if (async) await Assert.ThrowsAsync<NotSupportedException>(() => inside ? strategy.ExecuteAsync(ExecuteAsync) : ExecuteAsync());
                else Assert.Throws<NotSupportedException>(() => { if (inside) strategy.Execute(Execute); else Execute(); });
                Assert.Equal(0, network.Attempts);
                Assert.Equal(0, factories);
                Assert.Equal(0, seeders);
                Assert.Contains("CREATE TABLE", context.Database.GenerateCreateScript());
            }
    }

    private static RetryContext Create(OceanBaseCompatibilityMode mode, Commands commands, params IInterceptor[] extra)
        => new(new DbContextOptionsBuilder<RetryContext>().UseOceanBase("Server=offline;Database=app", mode,
            options => options.EnableRetryOnFailure(2, TimeSpan.Zero, [9999]))
            .ReplaceService<IRelationalConnection, TransactionTests.TestConnection>().EnableServiceProviderCaching(false)
            .AddInterceptors(new IInterceptor[] { commands }.Concat(extra)).Options);
    private static TransactionTests.NativeConnection Native(DbContext context)
        => Assert.IsType<TransactionTests.NativeConnection>(context.Database.GetDbConnection());
    private static OceanBaseException Transient(OceanBaseCompatibilityMode mode) => ServerError(mode == OceanBaseCompatibilityMode.MySql ? 1213 : 60);
    private static OceanBaseException ServerError(int code, bool unknown = false)
        => (OceanBaseException)Activator.CreateInstance(typeof(OceanBaseException), BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null, args: ["Synthetic retry failure.", code, null, unknown, null, null], culture: null)!;
    private sealed class RetryContext(DbContextOptions<RetryContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<Principal>().ToTable("Principals").Property(p => p.Name).HasMaxLength(40);
            builder.Entity<Detail>().ToTable("Details").Property(d => d.Id).ValueGeneratedNever();
            builder.Entity<Detail>().Property(d => d.Value).HasMaxLength(40);
            builder.Entity<Detail>().HasOne(d => d.Principal).WithMany(p => p.Details).HasForeignKey(d => d.PrincipalId);
        }
    }
    private sealed class Principal { public int Id { get; set; } public string? Name { get; set; } public List<Detail> Details { get; set; } = []; }
    private sealed class Detail { public int Id { get; set; } public int PrincipalId { get; set; } public string? Value { get; set; } public Principal Principal { get; set; } = null!; }
    private sealed record Command(string Sql, object?[] Inputs);

    private sealed class Commands : DbCommandInterceptor
    {
        public Exception? ReadFailure { get; set; }
        public Exception? NonQueryFailure { get; set; }
        public Exception? ReaderDisposeFailure { get; set; }
        public bool PersistentReadFailure { get; set; }
        public int QueryAttempts { get; private set; }
        public int RootWrites { get; private set; }
        private int _generatedId;
        public List<Command> Items { get; } = [];
        public List<Reader> Readers { get; } = [];
        private void Capture(DbCommand command)
        {
            var parameters = command.Parameters.Cast<DbParameter>().ToArray();
            var position = 0;
            var values = new List<object?>();
            foreach (Match bind in Regex.Matches(command.CommandText, @"[:@]\w+|\?"))
            {
                var parameter = bind.Value == "?" ? parameters[position++]
                    : parameters.Single(p => p.ParameterName.TrimStart(':', '@') == bind.Value.TrimStart(':', '@'));
                if (parameter.Direction == ParameterDirection.Input) values.Add(parameter.Value);
            }
            Items.Add(new Command(command.CommandText, values.ToArray()));
        }
        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData data, InterceptionResult<int> result)
        {
            Capture(command);
            if (command.CommandText.StartsWith("INSERT", StringComparison.Ordinal) && command.CommandText.Contains("Principals", StringComparison.Ordinal))
                _generatedId = 100 * ++RootWrites + 1;
            if (NonQueryFailure is { } failure) { NonQueryFailure = null; throw failure; }
            return result;
        }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<int> result, CancellationToken token = default) => ValueTask.FromResult(NonQueryExecuting(command, data, result));
        public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData data, int result)
        {
            foreach (DbParameter parameter in command.Parameters)
                if (parameter.Direction == ParameterDirection.Output) parameter.Value = _generatedId;
            return result;
        }
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData data, int result,
            CancellationToken token = default) => ValueTask.FromResult(NonQueryExecuted(command, data, result));
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result)
        {
            Capture(command);
            var table = new DataTable();
            table.Columns.Add("Id", typeof(int));
            if (command.CommandText.Contains("LAST_INSERT_ID", StringComparison.Ordinal))
            { table.Rows.Add(_generatedId); return InterceptionResult<DbDataReader>.SuppressWithResult(table.CreateDataReader()); }
            var attempt = ++QueryAttempts;
            table.Rows.Add(attempt == 1 ? 1 : 101);
            table.Rows.Add(attempt == 1 ? 2 : 102);
            var reader = new Reader(table.CreateDataReader(), attempt == 1 || PersistentReadFailure ? ReadFailure : null, ReaderDisposeFailure);
            Readers.Add(reader);
            return InterceptionResult<DbDataReader>.SuppressWithResult(reader);
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken token = default) => ValueTask.FromResult(ReaderExecuting(command, data, result));
    }

    private sealed class RollbackCapture : DbTransactionInterceptor
    {
        public Exception? Failure { get; init; }
        public int Attempts { get; private set; }
        public CancellationToken LastToken { get; private set; }
        public override InterceptionResult TransactionRollingBack(DbTransaction transaction, TransactionEventData data, InterceptionResult result)
        { Attempts++; if (Failure is not null) throw Failure; return result; }
        public override ValueTask<InterceptionResult> TransactionRollingBackAsync(DbTransaction transaction, TransactionEventData data,
            InterceptionResult result, CancellationToken token = default)
        { LastToken = token; return ValueTask.FromResult(TransactionRollingBack(transaction, data, result)); }
    }
    private sealed class CommittedFailure(Exception error) : DbTransactionInterceptor
    {
        public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData data) => throw error;
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData data,
            CancellationToken token = default) => Task.FromException(error);
    }
    private sealed class NoNetwork : DbConnectionInterceptor
    {
        public int Attempts { get; private set; }
        public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData data, InterceptionResult result)
        { Attempts++; throw new InvalidOperationException("Retry DDL must reject before network access."); }
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData data,
            InterceptionResult result, CancellationToken token = default) => ValueTask.FromResult(ConnectionOpening(connection, data, result));
    }

    private sealed class SuccessfulWrite : DbCommandInterceptor
    {
        public int Executions { get; private set; }
        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData data, InterceptionResult<int> result)
        { Executions++; return InterceptionResult<int>.SuppressWithResult(1); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<int> result, CancellationToken token = default) => ValueTask.FromResult(NonQueryExecuting(command, data, result));
    }

    private sealed class CloseFailure(Exception error) : DbConnectionInterceptor
    {
        public int Failures { get; private set; }
        // Simulate a non-closed native state without creating a session or touching the wire.
        private static void Opening(DbConnection connection, bool value)
            => typeof(OceanBaseConnection).GetField("_opening", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(connection, value);
        public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData data, InterceptionResult result)
        { Opening(connection, true); return InterceptionResult.Suppress(); }
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData data,
            InterceptionResult result, CancellationToken token = default) => ValueTask.FromResult(ConnectionOpening(connection, data, result));
        public override InterceptionResult ConnectionClosing(DbConnection connection, ConnectionEventData data, InterceptionResult result)
            => InterceptionResult.Suppress();
        public override ValueTask<InterceptionResult> ConnectionClosingAsync(DbConnection connection, ConnectionEventData data,
            InterceptionResult result) => ValueTask.FromResult(InterceptionResult.Suppress());
        public override void ConnectionClosed(DbConnection connection, ConnectionEndEventData data)
        { Opening(connection, false); if (Failures++ == 0) throw error; }
        public override Task ConnectionClosedAsync(DbConnection connection, ConnectionEndEventData data)
        { ConnectionClosed(connection, data); return Task.CompletedTask; }
    }

    private sealed class Reader(DbDataReader inner, Exception? failure, Exception? disposeFailure) : DbDataReader
    {
        public int Reads { get; private set; }
        public bool Disposed { get; private set; }
        public override bool Read() { if (++Reads == 2 && failure is not null) throw failure; return inner.Read(); }
        public override Task<bool> ReadAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult(Read()); }
        public override void Close() => inner.Close();
        protected override void Dispose(bool disposing)
        {
            if (disposing && !Disposed) { Disposed = true; inner.Dispose(); if (disposeFailure is not null) throw disposeFailure; }
            base.Dispose(disposing);
        }
        public override ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        public override int Depth => inner.Depth;
        public override int FieldCount => inner.FieldCount;
        public override bool HasRows => inner.HasRows;
        public override bool IsClosed => inner.IsClosed;
        public override int RecordsAffected => inner.RecordsAffected;
        public override object this[int ordinal] => inner[ordinal];
        public override object this[string name] => inner[name];
        public override bool NextResult() => inner.NextResult();
        public override string GetName(int ordinal) => inner.GetName(ordinal);
        public override string GetDataTypeName(int ordinal) => inner.GetDataTypeName(ordinal);
        public override Type GetFieldType(int ordinal) => inner.GetFieldType(ordinal);
        public override object GetValue(int ordinal) => inner.GetValue(ordinal);
        public override int GetValues(object[] values) => inner.GetValues(values);
        public override int GetOrdinal(string name) => inner.GetOrdinal(name);
        public override bool GetBoolean(int ordinal) => inner.GetBoolean(ordinal);
        public override byte GetByte(int ordinal) => inner.GetByte(ordinal);
        public override long GetBytes(int ordinal, long offset, byte[]? buffer, int bufferOffset, int length) => inner.GetBytes(ordinal, offset, buffer, bufferOffset, length);
        public override char GetChar(int ordinal) => inner.GetChar(ordinal);
        public override long GetChars(int ordinal, long offset, char[]? buffer, int bufferOffset, int length) => inner.GetChars(ordinal, offset, buffer, bufferOffset, length);
        public override Guid GetGuid(int ordinal) => inner.GetGuid(ordinal);
        public override short GetInt16(int ordinal) => inner.GetInt16(ordinal);
        public override int GetInt32(int ordinal) => inner.GetInt32(ordinal);
        public override long GetInt64(int ordinal) => inner.GetInt64(ordinal);
        public override float GetFloat(int ordinal) => inner.GetFloat(ordinal);
        public override double GetDouble(int ordinal) => inner.GetDouble(ordinal);
        public override string GetString(int ordinal) => inner.GetString(ordinal);
        public override decimal GetDecimal(int ordinal) => inner.GetDecimal(ordinal);
        public override DateTime GetDateTime(int ordinal) => inner.GetDateTime(ordinal);
        public override bool IsDBNull(int ordinal) => inner.IsDBNull(ordinal);
        public override IEnumerator GetEnumerator() => ((IEnumerable)inner).GetEnumerator();
    }
}
