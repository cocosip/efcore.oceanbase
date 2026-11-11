using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.FunctionalTests
{
    public sealed class TransactionTests
    {
        public static IEnumerable<object[]> Modes()
        {
            foreach (var mode in Enum.GetValues<OceanBaseCompatibilityMode>())
                foreach (var async in new[] { false, true })
                    yield return [mode, async];
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Owned_transaction_commit_rollback_and_disposal_preserve_connection_ownership(
            OceanBaseCompatibilityMode mode, bool async)
        {
            await using var context = Create(mode);
            var native = Native(context);
            using var source = new CancellationTokenSource();
            foreach (var commit in new[] { true, false })
            {
                var transaction = async
                    ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, source.Token)
                    : context.Database.BeginTransaction(IsolationLevel.Serializable);
                var physical = Assert.IsType<NativeTransaction>(transaction.GetDbTransaction());
                Assert.Equal(IsolationLevel.Serializable, physical.IsolationLevel);
                Assert.Same(transaction, context.Database.CurrentTransaction);
                Assert.Equal(ConnectionState.Open, native.State);
                if (async)
                {
                    if (commit) await transaction.CommitAsync(source.Token);
                    else await transaction.RollbackAsync(source.Token);
                }
                else if (commit) transaction.Commit();
                else transaction.Rollback();
                Assert.Equal(commit ? 1 : 0, physical.Commits);
                Assert.Equal(commit ? 0 : 1, physical.Rollbacks);
                Assert.Null(context.Database.CurrentTransaction);
                Assert.Equal(ConnectionState.Closed, native.State);
                if (async) await transaction.DisposeAsync();
                else transaction.Dispose();
                Assert.Equal(1, physical.Disposals);
                Assert.Equal(async ? 1 : 0, physical.AsyncEnds);
                if (async) Assert.Equal(source.Token, physical.LastToken);
                if (async) Assert.Equal(source.Token, native.LastBeginToken);
            }
            Assert.Equal(2, native.Begins);
            Assert.Equal(async ? 2 : 0, native.AsyncBegins);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Borrowed_transaction_is_not_disposed_and_caller_open_connection_stays_open(
            OceanBaseCompatibilityMode mode, bool async)
        {
            await using var context = Create(mode);
            var native = Native(context);
            native.Open();
            using var physical = new NativeTransaction(native, IsolationLevel.ReadCommitted);
            var transaction = async ? await context.Database.UseTransactionAsync(physical) : context.Database.UseTransaction(physical);
            Assert.NotNull(transaction);
            Assert.Same(physical, transaction.GetDbTransaction());
            if (async) await transaction.DisposeAsync();
            else transaction.Dispose();
            Assert.Equal(0, physical.Disposals);
            Assert.Equal(0, physical.Commits);
            Assert.Equal(0, physical.Rollbacks);
            Assert.Null(context.Database.CurrentTransaction);
            Assert.Equal(ConnectionState.Open, native.State);
            Assert.Equal(0, native.Closes);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Transaction_from_another_connection_is_rejected(OceanBaseCompatibilityMode mode, bool async)
        {
            await using var context = Create(mode);
            using var other = new NativeConnection();
            other.Open();
            using var physical = new NativeTransaction(other, IsolationLevel.ReadCommitted);
            if (async) await Assert.ThrowsAsync<InvalidOperationException>(() => context.Database.UseTransactionAsync(physical));
            else Assert.Throws<InvalidOperationException>(() => context.Database.UseTransaction(physical));
            Assert.Null(context.Database.CurrentTransaction);
            Assert.Equal(0, physical.Disposals);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Savepoints_use_the_same_transaction_and_Oracle_release_is_logical_only(
            OceanBaseCompatibilityMode mode, bool async)
        {
            var events = new ReleaseEvents();
            await using var context = Create(mode, events);
            var native = Native(context);
            using var source = new CancellationTokenSource();
            var transaction = async ? await context.Database.BeginTransactionAsync(source.Token) : context.Database.BeginTransaction();
            Assert.True(transaction.SupportsSavepoints);
            var name = mode == OceanBaseCompatibilityMode.MySql ? "a`b" : "a\"b";
            if (async)
            {
                await transaction.CreateSavepointAsync(name, source.Token);
                await transaction.RollbackToSavepointAsync(name, source.Token);
                await transaction.ReleaseSavepointAsync(name, source.Token);
            }
            else
            {
                transaction.CreateSavepoint(name);
                transaction.RollbackToSavepoint(name);
                transaction.ReleaseSavepoint(name);
            }
            var quoted = mode == OceanBaseCompatibilityMode.MySql ? "`a``b`" : "\"a\"\"b\"";
            var expected = new List<string> { "SAVEPOINT " + quoted + ";", "ROLLBACK TO " + quoted + ";" };
            if (mode == OceanBaseCompatibilityMode.MySql) expected.Add("RELEASE SAVEPOINT " + quoted + ";");
            Assert.Equal(expected, native.Commands.Select(c => c.Sql));
            Assert.All(native.Commands, c => Assert.Same(transaction.GetDbTransaction(), c.Transaction));
            Assert.All(native.Commands, c => Assert.Equal(async, c.Async));
            if (async) Assert.All(native.Commands, c => Assert.Equal(source.Token, c.Token));
            Assert.Equal(1, events.Releasing);
            Assert.Equal(1, events.Released);
            Assert.Equal(async ? 2 : 0, events.AsyncCalls);
            Assert.Same(transaction.GetDbTransaction(), events.Transaction);
            Assert.Same(transaction, context.Database.CurrentTransaction);
            if (async) await transaction.DisposeAsync();
            else transaction.Dispose();
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public async Task Cancelled_savepoint_release_does_not_execute_or_report_success(OceanBaseCompatibilityMode mode)
        {
            var events = new ReleaseEvents();
            await using var context = Create(mode, events);
            await using var transaction = await context.Database.BeginTransactionAsync();
            using var source = new CancellationTokenSource();
            source.Cancel();
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => transaction.ReleaseSavepointAsync("checkpoint", source.Token));
            Assert.Equal(source.Token, error.CancellationToken);
            Assert.Empty(Native(context).Commands);
            Assert.Equal(0, events.Released);
            Assert.Same(transaction, context.Database.CurrentTransaction);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Multiple_writes_commit_together_and_failure_disposes_without_committing(
            OceanBaseCompatibilityMode mode, bool async)
        {
            foreach (var fail in new[] { false, true })
            {
                await using var context = Create(mode);
                var native = Native(context);
                var failure = new InvalidOperationException("synthetic second write");
                native.WriteFailure = fail ? failure : null;
                context.AddRange(new Row { Id = 1, Name = "one" }, new Row { Id = 2, Name = "two" });
                if (fail)
                {
                    var error = async
                        ? await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync())
                        : Assert.Throws<DbUpdateException>(() => context.SaveChanges());
                    Assert.Same(failure, error.InnerException);
                    Assert.All(context.ChangeTracker.Entries(), e => Assert.Equal(EntityState.Added, e.State));
                }
                else Assert.Equal(2, async ? await context.SaveChangesAsync() : context.SaveChanges());
                var physical = Assert.Single(native.Transactions);
                Assert.Equal(fail ? 0 : 1, physical.Commits);
                Assert.Equal(1, physical.Disposals);
                Assert.Equal(fail, physical.DisposedActive);
                Assert.Equal(2, native.Commands.Count);
                Assert.All(native.Commands, c => Assert.Same(physical, c.Transaction));
                Assert.Null(context.Database.CurrentTransaction);
                Assert.Equal(ConnectionState.Closed, native.State);
            }
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Failed_save_in_existing_transaction_rolls_back_to_savepoint_and_can_retry(
            OceanBaseCompatibilityMode mode, bool async)
        {
            await using var context = Create(mode);
            var native = Native(context);
            var transaction = async ? await context.Database.BeginTransactionAsync() : context.Database.BeginTransaction();
            var physical = Assert.IsType<NativeTransaction>(transaction.GetDbTransaction());
            native.WriteFailure = new InvalidOperationException("synthetic second write");
            context.AddRange(new Row { Id = 1, Name = "one" }, new Row { Id = 2, Name = "two" });
            if (async) await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            else Assert.Throws<DbUpdateException>(() => context.SaveChanges());
            var quoted = mode == OceanBaseCompatibilityMode.MySql ? "`__EFSavePoint`" : "\"__EFSavePoint\"";
            var expected = new List<string> { "SAVEPOINT " + quoted + ";", "INSERT", "INSERT", "ROLLBACK TO " + quoted + ";" };
            if (mode == OceanBaseCompatibilityMode.MySql) expected.Add("RELEASE SAVEPOINT " + quoted + ";");
            Assert.Equal(expected, native.Commands.Select(c => c.Sql.StartsWith("INSERT", StringComparison.Ordinal) ? "INSERT" : c.Sql));
            Assert.All(native.Commands, c => Assert.Same(physical, c.Transaction));
            Assert.Equal(0, physical.Commits);
            Assert.Equal(0, physical.Disposals);
            Assert.Same(transaction, context.Database.CurrentTransaction);
            Assert.Equal(ConnectionState.Open, native.State);
            native.WriteFailure = null;
            Assert.Equal(2, async ? await context.SaveChangesAsync() : context.SaveChanges());
            Assert.All(context.ChangeTracker.Entries(), e => Assert.Equal(EntityState.Unchanged, e.State));
            if (async) { await transaction.CommitAsync(); await transaction.DisposeAsync(); }
            else { transaction.Commit(); transaction.Dispose(); }
            Assert.Equal(1, physical.Commits);
            Assert.Equal(1, physical.Disposals);
            Assert.Equal(ConnectionState.Closed, native.State);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public async Task Cancelled_write_recovers_the_caller_savepoint_with_an_uncancelled_token(OceanBaseCompatibilityMode mode)
        {
            await using var context = Create(mode);
            var native = Native(context);
            await using var transaction = await context.Database.BeginTransactionAsync();
            var physical = Assert.IsType<NativeTransaction>(transaction.GetDbTransaction());
            using var source = new CancellationTokenSource();
            var failure = new OperationCanceledException(source.Token);
            native.WriteFailure = failure;
            native.BeforeWriteFailure = source.Cancel;
            context.AddRange(new Row { Id = 1, Name = "one" }, new Row { Id = 2, Name = "two" });
            var actual = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.SaveChangesAsync(source.Token));
            Assert.Same(failure, actual);
            var rollback = Assert.Single(native.Commands, c => c.Sql.StartsWith("ROLLBACK TO", StringComparison.Ordinal));
            Assert.Equal(CancellationToken.None, rollback.Token);
            Assert.All(native.Commands.Where(c => c.Sql.StartsWith("INSERT", StringComparison.Ordinal)), c => Assert.Equal(source.Token, c.Token));
            if (mode == OceanBaseCompatibilityMode.MySql)
                Assert.Equal(CancellationToken.None, Assert.Single(native.Commands, c => c.Sql.StartsWith("RELEASE", StringComparison.Ordinal)).Token);
            Assert.Equal(0, physical.Commits);
            Assert.Equal(0, physical.Disposals);
            Assert.Same(transaction, context.Database.CurrentTransaction);
            Assert.Equal(ConnectionState.Open, native.State);
            Assert.All(context.ChangeTracker.Entries(), e => Assert.Equal(EntityState.Added, e.State));
            native.WriteFailure = null;
            Assert.Equal(2, await context.SaveChangesAsync());
            await transaction.RollbackAsync();
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Recovery_failure_and_warning_escalation_do_not_replace_cancellation(OceanBaseCompatibilityMode mode, bool async)
        {
            foreach (var escalate in new[] { false, true })
            {
                var builder = new DbContextOptionsBuilder<TestContext>().UseOceanBase("Server=offline;Database=offline", mode)
                    .ReplaceService<IRelationalConnection, TestConnection>();
                if (escalate) builder.ConfigureWarnings(w => w.Throw(RelationalEventId.BatchExecutorFailedToRollbackToSavepoint));
                await using var context = new TestContext(builder.Options);
                var native = Native(context);
                await using var transaction = await context.Database.BeginTransactionAsync();
                using var source = new CancellationTokenSource();
                var failure = new OperationCanceledException(source.Token);
                var recovery = new InvalidOperationException("rollback failed");
                native.WriteFailure = failure;
                native.BeforeWriteFailure = source.Cancel;
                native.SavepointRollbackFailure = recovery;
                context.AddRange(new Row { Id = 1, Name = "one" }, new Row { Id = 2, Name = "two" });
                var actual = async ? await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.SaveChangesAsync(source.Token))
                    : Assert.ThrowsAny<OperationCanceledException>(() => context.SaveChanges());
                Assert.Same(failure, actual);
                Assert.Same(recovery, actual.Data["OceanBase.SavepointRollbackError"]);
                Assert.Equal(escalate, actual.Data.Contains("OceanBase.SavepointDiagnosticsError"));
                Assert.Single(native.Commands, c => c.Sql.StartsWith("ROLLBACK TO", StringComparison.Ordinal));
                Assert.Same(transaction, context.Database.CurrentTransaction);
                Assert.Equal(ConnectionState.Open, native.State);
            }
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Failed_open_during_save_does_not_consume_the_caller_transaction_lease(OceanBaseCompatibilityMode mode, bool async)
        {
            await using var context = Create(mode);
            await using var transaction = await context.Database.BeginTransactionAsync();
            var native = Native(context);
            var physical = Assert.IsType<NativeTransaction>(transaction.GetDbTransaction());
            var connection = Assert.IsType<TestConnection>(context.GetService<IRelationalConnection>());
            var failure = new OperationCanceledException("open failed before acquiring a lease");
            connection.OpenFailure = failure;
            context.Add(new Row { Id = 1, Name = "one" });
            var actual = async ? await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.SaveChangesAsync())
                : Assert.ThrowsAny<OperationCanceledException>(() => context.SaveChanges());
            Assert.Same(failure, actual);
            Assert.Empty(native.Commands);
            Assert.Same(transaction, context.Database.CurrentTransaction);
            Assert.Equal(0, physical.Disposals);
            Assert.Equal(ConnectionState.Open, native.State);
            connection.OpenFailure = null;
            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public async Task Disabled_savepoints_leave_cancelled_caller_transaction_for_explicit_rollback(OceanBaseCompatibilityMode mode)
        {
            await using var context = Create(mode);
            context.Database.AutoSavepointsEnabled = false;
            var native = Native(context);
            await using var transaction = await context.Database.BeginTransactionAsync();
            using var source = new CancellationTokenSource();
            native.WriteFailure = new OperationCanceledException(source.Token);
            native.BeforeWriteFailure = source.Cancel;
            context.AddRange(new Row { Id = 1, Name = "one" }, new Row { Id = 2, Name = "two" });
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.SaveChangesAsync(source.Token));
            Assert.Equal(2, native.Commands.Count);
            Assert.All(native.Commands, c => Assert.StartsWith("INSERT", c.Sql));
            Assert.Same(transaction, context.Database.CurrentTransaction);
            await transaction.RollbackAsync();
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Owned_transaction_cancellation_preserves_the_error_even_if_disposal_fails(OceanBaseCompatibilityMode mode, bool async)
        {
            foreach (var failCleanup in new[] { false, true })
            {
                await using var context = Create(mode);
                var native = Native(context);
                using var source = new CancellationTokenSource();
                var failure = new OperationCanceledException(source.Token);
                var cleanup = new InvalidOperationException("native disposal failed");
                native.WriteFailure = failure;
                native.BeforeWriteFailure = source.Cancel;
                native.TransactionDisposeFailure = failCleanup ? cleanup : null;
                context.AddRange(new Row { Id = 1, Name = "one" }, new Row { Id = 2, Name = "two" });
                var actual = async ? await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.SaveChangesAsync(source.Token))
                    : Assert.ThrowsAny<OperationCanceledException>(() => context.SaveChanges());
                Assert.Same(failure, actual);
                var physical = Assert.Single(native.Transactions);
                Assert.Equal(0, physical.Commits);
                Assert.Equal(1, physical.Disposals);
                Assert.True(physical.DisposedActive);
                Assert.All(context.ChangeTracker.Entries(), e => Assert.Equal(EntityState.Added, e.State));
                if (failCleanup) Assert.Same(cleanup, actual.Data["OceanBase.BatchCleanupError"]);
                else
                {
                    Assert.Null(context.Database.CurrentTransaction);
                    Assert.Equal(ConnectionState.Closed, native.State);
                }
            }
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Failed_transaction_start_restores_the_registered_provider_connection_lease(OceanBaseCompatibilityMode mode, bool async)
        {
            var failure = new OperationCanceledException("transaction start failed");
            var interceptor = new BeginFailure(failure);
            var builder = new DbContextOptionsBuilder<TestContext>().UseOceanBase("Server=offline;Database=offline", mode)
                .AddInterceptors(interceptor).ConfigureWarnings(w => w.Ignore(RelationalEventId.AmbientTransactionWarning));
            await using var context = new TestContext(builder.Options);
            context.Database.AutoTransactionBehavior = AutoTransactionBehavior.Always;
            context.Add(new Row { Id = 1, Name = "one" });
            var actual = async ? await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.SaveChangesAsync())
                : Assert.ThrowsAny<OperationCanceledException>(() => context.SaveChanges());
            Assert.Same(failure, actual);
            var connection = context.GetService<IRelationalConnection>();
            Assert.Equal(0, (int)typeof(RelationalConnection).GetField("_openedCount",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(connection)!);
            Assert.Null(connection.CurrentTransaction);
            Assert.Equal(1, interceptor.Starts);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Savepoint_release_failure_respects_warning_policy_and_preserves_primary_errors(OceanBaseCompatibilityMode mode, bool async)
        {
            foreach (var failWrite in new[] { false, true })
            foreach (var escalate in new[] { false, true })
            {
                var release = new InvalidOperationException("release callback failed");
                var write = new InvalidOperationException("write failed");
                var events = new ReleaseEvents { Failure = release, FailurePhase = "starting" };
                var builder = new DbContextOptionsBuilder<TestContext>().UseOceanBase("Server=offline;Database=offline", mode)
                    .ReplaceService<IRelationalConnection, TestConnection>().AddInterceptors(events);
                if (escalate) builder.ConfigureWarnings(w => w.Throw(RelationalEventId.BatchExecutorFailedToReleaseSavepoint));
                await using var context = new TestContext(builder.Options);
                var native = Native(context);
                await using var transaction = await context.Database.BeginTransactionAsync();
                native.WriteFailure = failWrite ? write : null;
                context.AddRange(new Row { Id = 1, Name = "one" }, new Row { Id = 2, Name = "two" });
                var error = await Record.ExceptionAsync(async () =>
                {
                    if (async) await context.SaveChangesAsync();
                    else context.SaveChanges();
                });
                if (failWrite)
                {
                    Assert.Same(write, Assert.IsType<DbUpdateException>(error).InnerException);
                    Assert.Same(release, error!.Data["OceanBase.SavepointReleaseError"]);
                    Assert.Equal(escalate, error.Data.Contains("OceanBase.SavepointDiagnosticsError"));
                }
                else if (escalate) Assert.IsType<InvalidOperationException>(error);
                else Assert.Null(error);
                Assert.Same(transaction, context.Database.CurrentTransaction);
                Assert.Equal(ConnectionState.Open, native.State);
                Assert.Equal(0, Assert.Single(native.Transactions).Disposals);
                var connection = context.GetService<IRelationalConnection>();
                Assert.Equal(1, (int)typeof(RelationalConnection).GetField("_openedCount",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(connection)!);
            }
        }

        private sealed class BeginFailure(Exception failure, bool failStarting = true) : DbConnectionInterceptor, IDbTransactionInterceptor
        {
            public int Starts { get; private set; }
            public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
                => InterceptionResult.Suppress();
            public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
                InterceptionResult result, CancellationToken cancellationToken = default) => ValueTask.FromResult(InterceptionResult.Suppress());
            public InterceptionResult<DbTransaction> TransactionStarting(DbConnection connection, TransactionStartingEventData eventData,
                InterceptionResult<DbTransaction> result) { Starts++; if (failStarting) throw failure; return result; }
            public ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection, TransactionStartingEventData eventData,
                InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(TransactionStarting(connection, eventData, result));
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Failed_started_callback_disposes_untracked_transaction_and_preserves_outer_lease(OceanBaseCompatibilityMode mode, bool async)
        {
            foreach (var outerLease in new[] { false, true })
            foreach (var failDispose in new[] { false, true })
            {
                var failure = new OperationCanceledException("started callback failed");
                var cleanup = new InvalidOperationException("untracked transaction disposal failed");
                var interceptor = new StartedFailure(failure, failDispose ? cleanup : null);
                await using var context = new DbContext(new DbContextOptionsBuilder().UseOceanBase("Server=offline;Database=offline", mode)
                    .AddInterceptors(new BeginFailure(failure, failStarting: false), interceptor).Options);
                // The later transaction interceptor supplies the transaction; opening is suppressed by BeginFailure.
                if (outerLease) await context.Database.OpenConnectionAsync();
                var actual = async ? await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.Database.BeginTransactionAsync())
                    : Assert.ThrowsAny<OperationCanceledException>(() => context.Database.BeginTransaction());
                Assert.Same(failure, actual);
                Assert.NotNull(interceptor.Transaction);
                Assert.Equal(1, interceptor.Transaction!.Disposals);
                if (failDispose) Assert.Same(cleanup, actual.Data["OceanBase.TransactionStartCleanupError"]);
                Assert.Null(context.Database.CurrentTransaction);
                var connection = context.GetService<IRelationalConnection>();
                Assert.Equal(outerLease ? 1 : 0, (int)typeof(RelationalConnection).GetField("_openedCount",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(connection)!);
                if (outerLease) await context.Database.CloseConnectionAsync();
            }
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Registered_transaction_start_owns_returned_handle_and_rejects_nested_start_without_new_lease(OceanBaseCompatibilityMode mode, bool async)
        {
            foreach (var outerLease in new[] { false, true })
            {
                var interceptor = new StartedFailure(null, null);
                await using var context = new DbContext(new DbContextOptionsBuilder().UseOceanBase("Server=offline;Database=offline", mode)
                    .AddInterceptors(new BeginFailure(new Exception("unused"), failStarting: false), interceptor).Options);
                if (outerLease) await context.Database.OpenConnectionAsync();
                var transaction = async ? await context.Database.BeginTransactionAsync() : context.Database.BeginTransaction();
                var physical = interceptor.Transaction;
                Assert.Same(transaction, context.Database.CurrentTransaction);
                Assert.Same(physical, transaction.GetDbTransaction());
                if (async) await Assert.ThrowsAsync<InvalidOperationException>(() => context.Database.BeginTransactionAsync());
                else Assert.Throws<InvalidOperationException>(() => context.Database.BeginTransaction());
                Assert.Same(physical, interceptor.Transaction);
                if (async) await transaction.DisposeAsync();
                else transaction.Dispose();
                Assert.Equal(1, physical!.Disposals);
                Assert.Null(context.Database.CurrentTransaction);
                var connection = context.GetService<IRelationalConnection>();
                Assert.Equal(outerLease ? 1 : 0, (int)typeof(RelationalConnection).GetField("_openedCount",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(connection)!);
                if (outerLease) await context.Database.CloseConnectionAsync();
            }
        }

        private sealed class StartedFailure(Exception? failure, Exception? disposeFailure) : DbTransactionInterceptor
        {
            public StartTransaction? Transaction { get; private set; }
            public override InterceptionResult<DbTransaction> TransactionStarting(DbConnection connection, TransactionStartingEventData eventData,
                InterceptionResult<DbTransaction> result)
                => InterceptionResult<DbTransaction>.SuppressWithResult(Transaction = new StartTransaction(connection, disposeFailure));
            public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection, TransactionStartingEventData eventData,
                InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(TransactionStarting(connection, eventData, result));
            public override DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
                => failure is null ? result : throw failure;
            public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection, TransactionEndEventData eventData,
                DbTransaction result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(TransactionStarted(connection, eventData, result));
        }

        private sealed class StartTransaction(DbConnection connection, Exception? failure) : DbTransaction
        {
            public int Disposals { get; private set; }
            public override IsolationLevel IsolationLevel => IsolationLevel.Unspecified;
            protected override DbConnection DbConnection => connection;
            public override void Commit() => throw new NotSupportedException();
            public override void Rollback() => throw new NotSupportedException();
            protected override void Dispose(bool disposing)
            {
                if (disposing) { Disposals++; if (failure is not null) throw failure; }
                base.Dispose(disposing);
            }
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Generated_key_readback_is_enlisted_and_MySql_starts_a_transaction_for_the_followup_read(
            OceanBaseCompatibilityMode mode, bool async)
        {
            await using var context = Create(mode);
            var native = Native(context);
            var row = new GeneratedRow { Name = "generated" };
            context.Add(row);
            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            Assert.Equal(101, row.Id);
            if (mode == OceanBaseCompatibilityMode.MySql)
            {
                var physical = Assert.Single(native.Transactions);
                Assert.Equal(1, physical.Commits);
                Assert.Equal(1, physical.Disposals);
                Assert.Equal(2, native.Commands.Count);
                Assert.StartsWith("SELECT", native.Commands[1].Sql);
                Assert.All(native.Commands, c => Assert.Same(physical, c.Transaction));
            }
            else
            {
                Assert.Empty(native.Transactions);
                Assert.Null(Assert.Single(native.Commands).Transaction);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Failed_MySql_readback_does_not_commit_the_preceding_insert(bool async)
        {
            await using var context = Create(OceanBaseCompatibilityMode.MySql);
            var native = Native(context);
            var failure = new InvalidOperationException("synthetic readback failure");
            native.ReadFailure = failure;
            var row = new GeneratedRow { Name = "generated" };
            context.Add(row);
            var error = async ? await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync())
                : Assert.Throws<DbUpdateException>(() => context.SaveChanges());
            Assert.Same(failure, error.InnerException);
            Assert.Equal(EntityState.Added, context.Entry(row).State);
            var physical = Assert.Single(native.Transactions);
            Assert.Equal(0, physical.Commits);
            Assert.Equal(1, physical.Disposals);
            Assert.True(physical.DisposedActive);
            Assert.Equal(2, native.Commands.Count);
            Assert.All(native.Commands, c => Assert.Same(physical, c.Transaction));
            Assert.Null(context.Database.CurrentTransaction);
            Assert.Equal(ConnectionState.Closed, native.State);
        }

        [Theory]
        [InlineData(false, "suppress")]
        [InlineData(true, "suppress")]
        [InlineData(false, "starting")]
        [InlineData(true, "starting")]
        [InlineData(false, "completed")]
        [InlineData(true, "completed")]
        public async Task Oracle_logical_release_preserves_interception_and_original_errors(bool async, string phase)
        {
            var failure = new InvalidOperationException("synthetic release callback failure");
            var events = new ReleaseEvents { Suppress = phase == "suppress", Failure = failure, FailurePhase = phase };
            await using var context = Create(OceanBaseCompatibilityMode.Oracle, events);
            await using var transaction = await context.Database.BeginTransactionAsync();
            if (phase == "suppress")
            {
                if (async) await transaction.ReleaseSavepointAsync("checkpoint");
                else transaction.ReleaseSavepoint("checkpoint");
                Assert.Equal(1, events.Released);
                Assert.Null(events.Error);
            }
            else
            {
                var actual = async ? await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.ReleaseSavepointAsync("checkpoint"))
                    : Assert.Throws<InvalidOperationException>(() => transaction.ReleaseSavepoint("checkpoint"));
                Assert.Same(failure, actual);
                Assert.Same(failure, events.Error);
                Assert.Equal("ReleaseSavepoint", events.ErrorAction);
                Assert.Equal(async ? 1 : 0, events.AsyncErrors);
            }
            Assert.Empty(Native(context).Commands);
            Assert.Same(transaction, context.Database.CurrentTransaction);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Bulk_commands_attach_to_the_caller_transaction_without_savepoints_or_committing_it(OceanBaseCompatibilityMode mode, bool async)
        {
            await using var context = Create(mode);
            var native = Native(context);
            using var source = new CancellationTokenSource();
            await using var transaction = await context.Database.BeginTransactionAsync(source.Token);
            var physical = Assert.IsType<NativeTransaction>(transaction.GetDbTransaction());
            Assert.Equal(1, async ? await context.Set<Row>().Where(row => row.Id > 2)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Name, "bulk"), source.Token)
                : context.Set<Row>().Where(row => row.Id > 2).ExecuteUpdate(setters => setters.SetProperty(row => row.Name, "bulk")));
            Assert.Equal(1, async ? await context.Set<Row>().Where(row => row.Id == 5).ExecuteDeleteAsync(source.Token)
                : context.Set<Row>().Where(row => row.Id == 5).ExecuteDelete());
            Assert.Equal(2, native.Commands.Count);
            Assert.StartsWith("UPDATE", native.Commands[0].Sql);
            Assert.StartsWith("DELETE", native.Commands[1].Sql);
            Assert.All(native.Commands, command =>
            {
                Assert.Same(physical, command.Transaction);
                Assert.Equal(async, command.Async);
                Assert.Equal(async ? source.Token : default, command.Token);
            });
            Assert.Equal(1, native.Begins);
            Assert.Equal(0, physical.Commits);
            Assert.Equal(0, physical.Rollbacks);
            Assert.Same(transaction, context.Database.CurrentTransaction);
            Assert.Equal(ConnectionState.Open, native.State);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Oracle_correlated_update_keeps_the_caller_transaction_and_adds_no_savepoint(bool async)
        {
            await using var context = Create(OceanBaseCompatibilityMode.Oracle);
            var native = Native(context);
            using var source = new CancellationTokenSource();
            await using var transaction = await context.Database.BeginTransactionAsync(source.Token);
            var physical = Assert.IsType<NativeTransaction>(transaction.GetDbTransaction());
            var query = context.Set<Row>().Join(context.Set<Row>(), row => row.Id, other => other.Id,
                (row, other) => new { Target = row, Source = other });
            Assert.Equal(1, async ? await query.ExecuteUpdateAsync(setters => setters.SetProperty(pair => pair.Target.Name, pair => pair.Source.Name), source.Token)
                : query.ExecuteUpdate(setters => setters.SetProperty(pair => pair.Target.Name, pair => pair.Source.Name)));
            var command = Assert.Single(native.Commands);
            Assert.Contains("WHERE EXISTS", command.Sql);
            Assert.Same(physical, command.Transaction);
            Assert.Equal(async, command.Async);
            Assert.Equal(async ? source.Token : default, command.Token);
            Assert.Equal(1, native.Begins);
            Assert.Equal(0, physical.Commits);
            Assert.Equal(0, physical.Rollbacks);
            Assert.Equal(0, physical.Disposals);
            Assert.Same(transaction, context.Database.CurrentTransaction);
            Assert.Equal(ConnectionState.Open, native.State);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task MySql_snapshot_update_keeps_the_caller_transaction_without_savepoints(bool async)
        {
            await using var context = Create(OceanBaseCompatibilityMode.MySql);
            var native = Native(context);
            using var source = new CancellationTokenSource();
            await using var transaction = await context.Database.BeginTransactionAsync(source.Token);
            var physical = Assert.IsType<NativeTransaction>(transaction.GetDbTransaction());
            var query = context.Set<Row>().Join(context.Set<Row>(), row => row.Id, other => other.Id,
                (row, other) => new { Target = row, Source = other });
            Assert.Equal(1, async ? await query.ExecuteUpdateAsync(setters => setters.SetProperty(pair => pair.Target.Name, pair => pair.Source.Name)
                .SetProperty(pair => pair.Target.Id, pair => pair.Target.Name.Length), source.Token)
                : query.ExecuteUpdate(setters => setters.SetProperty(pair => pair.Target.Name, pair => pair.Source.Name)
                    .SetProperty(pair => pair.Target.Id, pair => pair.Target.Name.Length)));
            var command = Assert.Single(native.Commands);
            Assert.Contains("SELECT /*+ NO_MERGE */", command.Sql);
            Assert.Same(physical, command.Transaction);
            Assert.Equal(async, command.Async);
            Assert.Equal(async ? source.Token : default, command.Token);
            Assert.Equal(1, native.Begins);
            Assert.Equal(0, physical.Commits);
            Assert.Equal(0, physical.Rollbacks);
            Assert.Equal(0, physical.Disposals);
            Assert.Same(transaction, context.Database.CurrentTransaction);
            Assert.Equal(ConnectionState.Open, native.State);
        }

        private static TestContext Create(OceanBaseCompatibilityMode mode, params IInterceptor[] interceptors)
            => new(new DbContextOptionsBuilder<TestContext>()
                .UseOceanBase("Server=localhost;Database=offline", mode)
                .ReplaceService<IRelationalConnection, TestConnection>()
                .AddInterceptors(interceptors).Options);

        private static NativeConnection Native(DbContext context)
            => Assert.IsType<NativeConnection>(context.Database.GetDbConnection());

        public sealed class TestConnection(RelationalConnectionDependencies dependencies) : RelationalConnection(dependencies)
        {
            public Exception? OpenFailure { get; set; }
            protected override DbConnection CreateDbConnection() => new NativeConnection();
            public override bool Open(bool errorsExpected = false)
            {
                if (OpenFailure is { } failure) throw failure;
                return base.Open(errorsExpected);
            }
            public override Task<bool> OpenAsync(CancellationToken cancellationToken, bool errorsExpected = false)
                => OpenFailure is { } failure ? Task.FromException<bool>(failure) : base.OpenAsync(cancellationToken, errorsExpected);
        }

        private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
        {
            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity<Row>().ToTable("Rows");
                modelBuilder.Entity<Row>().Property(r => r.Id).ValueGeneratedNever();
                modelBuilder.Entity<Row>().Property(r => r.Name).HasMaxLength(40);
                modelBuilder.Entity<GeneratedRow>().ToTable("GeneratedRows");
                modelBuilder.Entity<GeneratedRow>().Property(r => r.Name).HasMaxLength(40);
            }
        }

        private sealed class Row { public int Id { get; set; } public string Name { get; set; } = ""; }
        private sealed class GeneratedRow { public int Id { get; set; } public string Name { get; set; } = ""; }
        private sealed record Command(string Sql, DbTransaction? Transaction, bool Async, CancellationToken Token);

        private sealed class ReleaseEvents : DbTransactionInterceptor
        {
            public int Releasing { get; private set; }
            public int Released { get; private set; }
            public int AsyncCalls { get; private set; }
            public DbTransaction? Transaction { get; private set; }
            public bool Suppress { get; init; }
            public Exception? Failure { get; init; }
            public string? FailurePhase { get; init; }
            public Exception? Error { get; private set; }
            public string? ErrorAction { get; private set; }
            public int AsyncErrors { get; private set; }
            public override InterceptionResult ReleasingSavepoint(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result)
            {
                Releasing++; Transaction = transaction;
                if (FailurePhase == "starting") throw Failure!;
                return Suppress ? InterceptionResult.Suppress() : result;
            }
            public override void ReleasedSavepoint(DbTransaction transaction, TransactionEventData eventData)
            {
                Released++;
                if (FailurePhase == "completed") throw Failure!;
            }
            public override ValueTask<InterceptionResult> ReleasingSavepointAsync(DbTransaction transaction, TransactionEventData eventData,
                InterceptionResult result, CancellationToken cancellationToken = default)
            { AsyncCalls++; return ValueTask.FromResult(ReleasingSavepoint(transaction, eventData, result)); }
            public override Task ReleasedSavepointAsync(DbTransaction transaction, TransactionEventData eventData, CancellationToken cancellationToken = default)
            { AsyncCalls++; ReleasedSavepoint(transaction, eventData); return Task.CompletedTask; }
            public override void TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData)
            { Error = eventData.Exception; ErrorAction = eventData.Action; }
            public override Task TransactionFailedAsync(DbTransaction transaction, TransactionErrorEventData eventData, CancellationToken cancellationToken = default)
            { AsyncErrors++; TransactionFailed(transaction, eventData); return Task.CompletedTask; }
        }

        private sealed class NativeConnection : DbConnection
        {
            private ConnectionState _state;
            public List<Command> Commands { get; } = [];
            public List<NativeTransaction> Transactions { get; } = [];
            public Exception? WriteFailure { get; set; }
            public Action? BeforeWriteFailure { get; set; }
            public Exception? SavepointRollbackFailure { get; set; }
            public Exception? TransactionDisposeFailure { get; set; }
            public Exception? ReadFailure { get; set; }
            public CancellationToken LastBeginToken { get; private set; }
            private int _writes;
            public int Begins { get; private set; }
            public int AsyncBegins { get; private set; }
            public int Closes { get; private set; }
            [AllowNull] public override string ConnectionString { get; set; } = "";
            public override string Database => "offline";
            public override string DataSource => "offline";
            public override string ServerVersion => "offline";
            public override ConnectionState State => _state;
            public override void Open() => _state = ConnectionState.Open;
            public override Task OpenAsync(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); Open(); return Task.CompletedTask; }
            public override void Close() { Closes++; _state = ConnectionState.Closed; }
            public override Task CloseAsync() { Close(); return Task.CompletedTask; }
            public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
            protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
            {
                Begins++;
                var transaction = new NativeTransaction(this, isolationLevel);
                Transactions.Add(transaction);
                return transaction;
            }
            protected override ValueTask<DbTransaction> BeginDbTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken)
            { cancellationToken.ThrowIfCancellationRequested(); LastBeginToken = cancellationToken; AsyncBegins++; return ValueTask.FromResult(BeginDbTransaction(isolationLevel)); }
            protected override DbCommand CreateDbCommand() => new NativeCommand(this);
            public void Execute(NativeCommand command, bool async, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                Commands.Add(new Command(command.CommandText, command.Transaction, async, token));
                if (command.CommandText.StartsWith("ROLLBACK TO", StringComparison.Ordinal) && SavepointRollbackFailure is not null)
                    throw SavepointRollbackFailure;
                if (command.CommandText.StartsWith("SELECT", StringComparison.Ordinal) && ReadFailure is not null) throw ReadFailure;
                if (command.CommandText.StartsWith("INSERT", StringComparison.Ordinal))
                {
                    if (++_writes == 2 && WriteFailure is not null)
                    {
                        BeforeWriteFailure?.Invoke();
                        throw WriteFailure;
                    }
                    foreach (DbParameter parameter in command.Parameters)
                        if (parameter.Direction == ParameterDirection.Output) parameter.Value = 101;
                }
            }
        }

        private sealed class NativeTransaction(NativeConnection connection, IsolationLevel isolation) : DbTransaction
        {
            private bool _ended;
            public int Commits { get; private set; }
            public int Rollbacks { get; private set; }
            public int Disposals { get; private set; }
            public int AsyncEnds { get; private set; }
            public bool DisposedActive { get; private set; }
            public CancellationToken LastToken { get; private set; }
            public override IsolationLevel IsolationLevel => isolation;
            protected override DbConnection DbConnection => connection;
            public override void Commit() { Commits++; _ended = true; }
            public override void Rollback() { Rollbacks++; _ended = true; }
            public override Task CommitAsync(CancellationToken cancellationToken = default)
            { cancellationToken.ThrowIfCancellationRequested(); AsyncEnds++; LastToken = cancellationToken; Commit(); return Task.CompletedTask; }
            public override Task RollbackAsync(CancellationToken cancellationToken = default)
            { cancellationToken.ThrowIfCancellationRequested(); AsyncEnds++; LastToken = cancellationToken; Rollback(); return Task.CompletedTask; }
            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    Disposals++;
                    DisposedActive = !_ended;
                    _ended = true;
                    if (connection.TransactionDisposeFailure is { } failure)
                    {
                        connection.TransactionDisposeFailure = null;
                        throw failure;
                    }
                }
                base.Dispose(disposing);
            }
            public override ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }

        private sealed class NativeCommand(NativeConnection connection) : DbCommand
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
            public override int ExecuteNonQuery() { connection.Execute(this, false, default); return 1; }
            public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
            { connection.Execute(this, true, cancellationToken); return Task.FromResult(1); }
            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
            { connection.Execute(this, false, default); return Reader(); }
            protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
            { connection.Execute(this, true, cancellationToken); return Task.FromResult(Reader()); }
            private static DbDataReader Reader()
            {
                var table = new DataTable();
                table.Columns.Add("Id", typeof(int));
                table.Rows.Add(101);
                return table.CreateDataReader();
            }
            protected override void Dispose(bool disposing) { if (disposing) _parameters.Dispose(); base.Dispose(disposing); }
        }
    }
}
