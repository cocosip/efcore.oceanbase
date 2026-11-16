using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace OceanBase.EntityFrameworkCore.FunctionalTests
{
    public sealed class SaveChangesInterceptionTests
    {
        public static IEnumerable<object[]> Cases()
        {
            foreach (var mode in Enum.GetValues<OceanBaseCompatibilityMode>())
                foreach (var async in new[] { false, true })
                    foreach (var suppress in new[] { false, true }) yield return [mode, async, suppress];
        }

        public static IEnumerable<object[]> Modes()
        {
            foreach (var mode in Enum.GetValues<OceanBaseCompatibilityMode>())
                foreach (var async in new[] { false, true }) yield return [mode, async];
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Conflict_diagnostic_is_dispatched_without_a_SaveChanges_interceptor(
            OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new Commands { AffectedRows = 0 };
            var diagnostics = new List<EventData>();
            await using var context = Create(mode, commands, diagnostics);
            var row = new Row { Id = 7, Name = "changed", Version = 11 };
            context.Entry(row).State = EntityState.Modified;
            var error = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => Save(context, async));
            var payload = Assert.IsType<ConcurrencyExceptionEventData>(Assert.Single(diagnostics));
            Assert.Same(error, payload.Exception);
            Assert.Same(context, payload.Context);
            Assert.Same(row, Assert.Single(payload.Entries).Entity);
            Assert.Equal(0, commands.Reads);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Suppressed_identity_conflict_does_not_invent_a_permanent_key(
            OceanBaseCompatibilityMode mode, bool async)
        {
            foreach (var accept in new[] { false, true })
            {
                var commands = new Commands { AffectedRows = 0 };
                var probe = new SaveProbe { Suppress = true };
                var diagnostics = new List<EventData>();
                await using var context = Create(mode, commands, diagnostics, probe);
                var row = new IdentityRow();
                context.Add(row);
                var key = context.Entry(row).Property(entity => entity.Id);
                Assert.True(key.IsTemporary);
                var temporary = key.CurrentValue;
                Assert.Equal(1, await Save(context, async, accept));
                AssertConflict(probe, diagnostics, context, row, async, default);
                Assert.Equal(!accept, key.IsTemporary);
                Assert.Equal(accept ? 0 : temporary, key.CurrentValue);
                Assert.Equal(0, row.Id);
                Assert.Equal(accept ? EntityState.Unchanged : EntityState.Added, context.Entry(row).State);
                Assert.Equal(0, commands.Reads);
            }
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Command_cancellation_is_not_dispatched_as_a_concurrency_conflict(
            OceanBaseCompatibilityMode mode, bool async)
        {
            using var source = new CancellationTokenSource();
            var failure = new OperationCanceledException(source.Token);
            var commands = new Commands { Failure = failure };
            var probe = new SaveProbe { Suppress = true };
            var diagnostics = new List<EventData>();
            await using var context = Create(mode, commands, diagnostics, probe);
            context.Entry(new Row { Id = 7 }).State = EntityState.Modified;
            var error = await Assert.ThrowsAsync<OperationCanceledException>(() => Save(context, async, true, source.Token));
            Assert.Same(failure, error);
            Assert.Equal(1, probe.Canceled);
            Assert.Null(probe.Conflict);
            Assert.Empty(diagnostics);
            Assert.Equal(0, commands.Reads);
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public async Task Affected_row_conflicts_dispatch_once_and_honor_suppression_and_acceptance(
            OceanBaseCompatibilityMode mode, bool async, bool suppress)
        {
            foreach (var state in new[] { EntityState.Added, EntityState.Modified, EntityState.Deleted })
                foreach (var affected in new[] { -1, 0, 2 })
                    foreach (var accept in new[] { false, true })
                    {
                        var commands = new Commands { AffectedRows = affected };
                        var probe = new SaveProbe { Suppress = suppress };
                        var diagnostics = new List<EventData>();
                        await using var context = Create(mode, commands, diagnostics, probe);
                        var row = new Row { Id = 7, Name = "changed", Version = 11 };
                        context.Entry(row).State = state;
                        using var source = new CancellationTokenSource();
                        if (suppress)
                        {
                            Assert.Equal(1, await Save(context, async, accept, source.Token));
                            Assert.Equal(1, probe.Completed);
                        }
                        else
                        {
                            var error = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
                                () => Save(context, async, accept, source.Token));
                            Assert.Same(error, Assert.IsType<ConcurrencyExceptionEventData>(probe.Conflict).Exception);
                            Assert.Contains(affected.ToString(System.Globalization.CultureInfo.InvariantCulture), error.Message);
                            Assert.Equal(0, probe.Completed);
                        }
                        AssertConflict(probe, diagnostics, context, row, async, source.Token);
                        Assert.Equal(suppress && accept
                            ? state == EntityState.Deleted ? EntityState.Detached : EntityState.Unchanged
                            : state, context.Entry(row).State);
                        Assert.Equal(11, row.Version);
                        Assert.Equal(1, commands.Writes);
                        Assert.Equal(0, commands.Reads);
                        Assert.Equal(0, probe.Failed);
                    }
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task Missing_MySql_generated_row_uses_the_same_concurrency_contract(bool async, bool suppress)
        {
            var commands = new Commands { MissingRow = true };
            var probe = new SaveProbe { Suppress = suppress };
            var diagnostics = new List<EventData>();
            await using var context = Create(OceanBaseCompatibilityMode.MySql, commands, diagnostics, probe);
            var row = new Row { Id = 7, Name = "changed", Version = 11 };
            context.Entry(row).State = EntityState.Modified;
            using var source = new CancellationTokenSource();
            if (suppress) Assert.Equal(1, await Save(context, async, true, source.Token));
            else
            {
                var error = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => Save(context, async, true, source.Token));
                Assert.Same(error, Assert.IsType<ConcurrencyExceptionEventData>(probe.Conflict).Exception);
                Assert.Equal("The generated row could not be read.", error.Message);
            }
            AssertConflict(probe, diagnostics, context, row, async, source.Token);
            Assert.Equal(suppress ? EntityState.Unchanged : EntityState.Modified, context.Entry(row).State);
            Assert.Equal(11, row.Version);
            Assert.Equal(1, commands.Writes);
            Assert.Equal(1, commands.Reads);
            Assert.True(commands.Reader!.IsClosed);
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public async Task Normal_success_and_command_errors_do_not_dispatch_concurrency(
            OceanBaseCompatibilityMode mode, bool async, bool fail)
        {
            var failure = new InvalidOperationException("synthetic command error");
            var commands = new Commands { Failure = fail ? failure : null };
            var probe = new SaveProbe { Suppress = true };
            var diagnostics = new List<EventData>();
            await using var context = Create(mode, commands, diagnostics, probe);
            var row = new Row { Id = 7, Name = "changed", Version = 11 };
            context.Entry(row).State = EntityState.Modified;
            if (fail)
            {
                var error = await Assert.ThrowsAsync<DbUpdateException>(() => Save(context, async));
                Assert.Same(failure, error.InnerException);
                Assert.Same(row, Assert.Single(error.Entries).Entity);
                Assert.Equal(1, probe.Failed);
            }
            else
            {
                Assert.Equal(1, await Save(context, async));
                Assert.Equal(42, row.Version);
                Assert.Equal(1, probe.Completed);
            }
            Assert.Null(probe.Conflict);
            Assert.Empty(diagnostics);
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public async Task Callback_errors_keep_existing_wrapping_and_cancellation_rules(
            OceanBaseCompatibilityMode mode, bool async, bool cancel)
        {
            using var source = new CancellationTokenSource();
            Exception failure = cancel ? new OperationCanceledException(source.Token)
                : new InvalidOperationException("synthetic concurrency interceptor error");
            var commands = new Commands { AffectedRows = 0 };
            var probe = new SaveProbe { Callback = _ => throw failure };
            var diagnostics = new List<EventData>();
            await using var context = Create(mode, commands, diagnostics, probe);
            var row = new Row { Id = 7, Name = "changed", Version = 11 };
            context.Entry(row).State = EntityState.Modified;
            if (cancel)
            {
                var error = await Assert.ThrowsAsync<OperationCanceledException>(() => Save(context, async, true, source.Token));
                Assert.Same(failure, error);
                Assert.Equal(1, probe.Canceled);
            }
            else
            {
                var error = await Assert.ThrowsAsync<DbUpdateException>(() => Save(context, async, true, source.Token));
                Assert.Same(failure, error.InnerException);
                Assert.Same(row, Assert.Single(error.Entries).Entity);
                Assert.Equal(1, probe.Failed);
            }
            AssertConflict(probe, diagnostics, context, row, async, source.Token);
            Assert.Equal(EntityState.Modified, context.Entry(row).State);
            Assert.Equal(11, row.Version);
            Assert.Equal(0, commands.Reads);
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public async Task Suppressed_conflicts_continue_later_batches_and_compose_interceptors(
            OceanBaseCompatibilityMode mode, bool async, bool accept)
        {
            var commands = new Commands { FirstWriteConflicts = true };
            var suppressor = new SaveProbe { Suppress = true };
            var observer = new SaveProbe();
            var diagnostics = new List<EventData>();
            await using var context = Create(mode, commands, diagnostics, suppressor, observer);
            var first = new Row { Id = 7, Name = "first", Version = 11 };
            var second = new Row { Id = 8, Name = "second", Version = 12 };
            context.Entry(first).State = EntityState.Modified;
            context.Entry(second).State = EntityState.Modified;
            Assert.Equal(2, await Save(context, async, accept));
            AssertConflict(suppressor, diagnostics, context, first, async, default);
            Assert.Same(suppressor.Conflict, observer.Conflict);
            Assert.True(observer.ReceivedSuppression);
            Assert.Equal(11, first.Version);
            Assert.Equal(accept ? 42 : 12, second.Version);
            Assert.Equal(42, context.Entry(second).Property(row => row.Version).CurrentValue);
            Assert.Equal(accept ? EntityState.Unchanged : EntityState.Modified, context.Entry(first).State);
            Assert.Equal(accept ? EntityState.Unchanged : EntityState.Modified, context.Entry(second).State);
            Assert.Equal(2, commands.Writes);
            Assert.Equal(mode == OceanBaseCompatibilityMode.MySql ? 1 : 0, commands.Reads);
            Assert.Equal(1, suppressor.Completed);
            Assert.Equal(1, observer.Completed);
        }

        private static void AssertConflict(SaveProbe probe, List<EventData> diagnostics, DbContext context,
            object row, bool async, CancellationToken token)
        {
            var payload = Assert.IsType<ConcurrencyExceptionEventData>(Assert.Single(diagnostics));
            Assert.Same(payload, probe.Conflict);
            Assert.Same(context, payload.Context);
            Assert.Same(row, Assert.Single(payload.Entries).Entity);
            Assert.Same(row, Assert.Single(payload.Exception.Entries).Entity);
            Assert.Equal(async ? 0 : 1, probe.SyncCalls);
            Assert.Equal(async ? 1 : 0, probe.AsyncCalls);
            if (async) Assert.Equal(token, probe.Token);
        }

        private static async Task<int> Save(DbContext context, bool async, bool accept = true, CancellationToken token = default)
            => async ? await context.SaveChangesAsync(accept, token) : context.SaveChanges(accept);

        private static SaveContext Create(OceanBaseCompatibilityMode mode, Commands commands,
            List<EventData> diagnostics, params IInterceptor[] interceptors)
        {
            var context = new SaveContext(new DbContextOptionsBuilder<SaveContext>()
                .UseOceanBase("Server=offline;Database=offline", mode)
                .AddInterceptors(commands).AddInterceptors(interceptors)
                .LogTo((id, _) => id == CoreEventId.OptimisticConcurrencyException, diagnostics.Add).Options);
            context.Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
            return context;
        }

        private sealed class SaveContext(DbContextOptions<SaveContext> options) : DbContext(options)
        {
            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity<IdentityRow>();
                modelBuilder.Entity<Row>().Property(row => row.Id).ValueGeneratedNever();
                modelBuilder.Entity<Row>().Property(row => row.Name).HasMaxLength(80);
                modelBuilder.Entity<Row>().Property(row => row.Version).HasComputedColumnSql("42").IsConcurrencyToken();
            }
        }

        private sealed class Row
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public int Version { get; set; }
        }

        private sealed class IdentityRow
        {
            public int Id { get; set; }
        }

        internal sealed class SaveProbe : SaveChangesInterceptor
        {
            public bool Suppress { get; init; }
            public Action<ConcurrencyExceptionEventData>? Callback { get; init; }
            public ConcurrencyExceptionEventData? Conflict { get; private set; }
            public bool ReceivedSuppression { get; private set; }
            public int SyncCalls { get; private set; }
            public int AsyncCalls { get; private set; }
            public int Completed { get; private set; }
            public int Failed { get; private set; }
            public int Canceled { get; private set; }
            public CancellationToken Token { get; private set; }

            private InterceptionResult ConflictResult(ConcurrencyExceptionEventData data, InterceptionResult result)
            {
                Conflict = data;
                ReceivedSuppression = result.IsSuppressed;
                Callback?.Invoke(data);
                return Suppress ? InterceptionResult.Suppress() : result;
            }

            public override InterceptionResult ThrowingConcurrencyException(ConcurrencyExceptionEventData eventData, InterceptionResult result)
            { SyncCalls++; return ConflictResult(eventData, result); }
            public override ValueTask<InterceptionResult> ThrowingConcurrencyExceptionAsync(ConcurrencyExceptionEventData eventData,
                InterceptionResult result, CancellationToken cancellationToken = default)
            { AsyncCalls++; Token = cancellationToken; return ValueTask.FromResult(ConflictResult(eventData, result)); }
            public override int SavedChanges(SaveChangesCompletedEventData eventData, int result) { Completed++; return result; }
            public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
                CancellationToken cancellationToken = default) { Completed++; return ValueTask.FromResult(result); }
            public override void SaveChangesFailed(DbContextErrorEventData eventData) => Failed++;
            public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
            { Failed++; return Task.CompletedTask; }
            public override void SaveChangesCanceled(DbContextEventData eventData) => Canceled++;
            public override Task SaveChangesCanceledAsync(DbContextEventData eventData, CancellationToken cancellationToken = default)
            { Canceled++; return Task.CompletedTask; }
        }

        private sealed class Commands : DbConnectionInterceptor, IDbCommandInterceptor
        {
            public int AffectedRows { get; init; } = 1;
            public bool FirstWriteConflicts { get; init; }
            public bool MissingRow { get; init; }
            public Exception? Failure { get; init; }
            public int Writes { get; private set; }
            public int Reads { get; private set; }
            public DbDataReader? Reader { get; private set; }

            public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData,
                InterceptionResult result) => InterceptionResult.Suppress();
            public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
                InterceptionResult result, CancellationToken cancellationToken = default) => ValueTask.FromResult(InterceptionResult.Suppress());

            public InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
            {
                Writes++;
                if (Failure is not null) throw Failure;
                foreach (DbParameter parameter in command.Parameters)
                    if (parameter.Direction == ParameterDirection.Output) parameter.Value = 42;
                return InterceptionResult<int>.SuppressWithResult(FirstWriteConflicts && Writes == 1 ? 0 : AffectedRows);
            }
            public ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
                InterceptionResult<int> result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(NonQueryExecuting(command, eventData, result));
            public InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData,
                InterceptionResult<DbDataReader> result)
            {
                Reads++;
                var table = new DataTable();
                table.Columns.Add("Version", typeof(int));
                if (!MissingRow) table.Rows.Add(42);
                Reader = table.CreateDataReader();
                return InterceptionResult<DbDataReader>.SuppressWithResult(Reader);
            }
            public ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
                InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(ReaderExecuting(command, eventData, result));
        }
    }
}
