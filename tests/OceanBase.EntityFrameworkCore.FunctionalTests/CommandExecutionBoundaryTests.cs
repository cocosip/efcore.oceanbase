using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.FunctionalTests
{
    public sealed class CommandExecutionBoundaryTests
    {
        public static IEnumerable<object[]> EnumerationCases()
        {
            foreach (var pair in TransactionTests.Modes())
                foreach (var retries in new[] { false, true })
                    yield return [pair[0], pair[1], retries];
        }

        [Theory]
        [MemberData(nameof(EnumerationCases))]
        public async Task Early_enumerator_disposal_releases_reader_and_lease_with_or_without_buffering(
            OceanBaseCompatibilityMode mode, bool async, bool retries)
        {
            var commands = new CommandProbe(null) { ReturnedRows = [1, 2, 3] };
            await using var context = CreateEnumerationContext(mode, commands, retries);
            var query = context.Set<Row>().OrderBy(r => r.Id).Select(r => r.Id);
            if (async)
            {
                await using var iterator = query.AsAsyncEnumerable().GetAsyncEnumerator();
                Assert.Equal(0, commands.Executions);
                Assert.True(await iterator.MoveNextAsync());
                Assert.Equal(1, iterator.Current);
                Assert.Equal(retries, Assert.Single(commands.Readers).Disposed);
            }
            else
            {
                using var iterator = query.AsEnumerable().GetEnumerator();
                Assert.Equal(0, commands.Executions);
                Assert.True(iterator.MoveNext());
                Assert.Equal(1, iterator.Current);
                Assert.Equal(retries, Assert.Single(commands.Readers).Disposed);
            }
            Assert.Equal(retries ? 4 : 1, commands.Readers[0].Reads);
            Assert.True(commands.Readers[0].Disposed);
            Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
            commands.ReturnedRows = [4];
            Assert.Equal(new[] { 4 }, async ? await query.ToArrayAsync() : query.ToArray());
            Assert.Equal(2, commands.Executions);
            Assert.All(commands.Readers, reader => Assert.True(reader.Disposed));
            Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        }

        [Theory]
        [MemberData(nameof(EnumerationCases))]
        public async Task Read_failure_and_cancellation_preserve_outcomes_release_resources_and_allow_reuse(
            OceanBaseCompatibilityMode mode, bool async, bool retries)
        {
            foreach (var cancel in new[] { false, true })
            {
                using var cancellation = new CancellationTokenSource();
                var failure = cancel ? (Exception)new OperationCanceledException(cancellation.Token)
                    : new InvalidOperationException("second read failed");
                var commands = new CommandProbe(null) { ReturnedRows = [1, 2, 3] };
                await using var context = CreateEnumerationContext(mode, commands, retries);
                commands.BeforeRead = (read, token) =>
                {
                    Assert.Equal(ConnectionState.Open, context.Database.GetDbConnection().State);
                    Assert.Equal(async ? cancellation.Token : default, token);
                    if (read != 2) return;
                    if (cancel) cancellation.Cancel();
                    throw failure;
                };
                var observed = new List<int>();
                var query = context.Set<Row>().OrderBy(r => r.Id).Select(r => r.Id);
                var actual = await Record.ExceptionAsync(async () =>
                {
                    if (async)
                    {
                        await foreach (var value in query.AsAsyncEnumerable().WithCancellation(cancellation.Token)) observed.Add(value);
                    }
                    else
                    {
                        foreach (var value in query.AsEnumerable()) observed.Add(value);
                    }
                });
                Assert.Same(failure, actual);
                Assert.Equal(retries ? Array.Empty<int>() : [1], observed);
                Assert.Equal(1, commands.Executions);
                var reader = Assert.Single(commands.Readers);
                Assert.Equal(2, reader.Reads);
                Assert.True(reader.Disposed);
                Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
                commands.BeforeRead = null;
                commands.ReturnedRows = [7];
                Assert.Equal(new[] { 7 }, async ? await query.ToArrayAsync() : query.ToArray());
                Assert.Equal(2, commands.Executions);
                Assert.All(commands.Readers, item => Assert.True(item.Disposed));
                Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public async Task Consumer_cancellation_after_a_yield_reaches_the_next_read_and_allows_reuse(OceanBaseCompatibilityMode mode)
        {
            var commands = new CommandProbe(null) { ReturnedRows = [1, 2] };
            await using var context = CreateEnumerationContext(mode, commands);
            using var cancellation = new CancellationTokenSource();
            var query = context.Set<Row>().OrderBy(r => r.Id).Select(r => r.Id);
            await using (var iterator = query.AsAsyncEnumerable().GetAsyncEnumerator(cancellation.Token))
            {
                Assert.True(await iterator.MoveNextAsync());
                Assert.Equal(1, iterator.Current);
                cancellation.Cancel();
                var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => iterator.MoveNextAsync().AsTask());
                Assert.Same(commands.Readers[0].ReadFailure, error);
                Assert.True(error.CancellationToken.IsCancellationRequested);
            }
            var reader = Assert.Single(commands.Readers);
            Assert.Equal(2, reader.Reads);
            Assert.Equal(new[] { cancellation.Token, cancellation.Token }, reader.ReadTokens);
            Assert.Equal(cancellation.Token, Assert.Single(commands.Tokens));
            Assert.True(reader.Disposed);
            Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
            commands.ReturnedRows = [7];
            Assert.Equal(new[] { 7 }, await query.ToArrayAsync());
            Assert.Equal(2, commands.Executions);
            Assert.All(commands.Readers, item => Assert.True(item.Disposed));
            Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        }

        [Theory]
        [MemberData(nameof(TransactionTests.Modes), MemberType = typeof(TransactionTests))]
        public async Task Enumeration_releases_only_its_own_lease_on_completion_early_stop_failure_and_cancellation(
            OceanBaseCompatibilityMode mode, bool async)
        {
            foreach (var outcome in new[] { "complete", "early", "failure", "cancel" })
            {
                using var cancellation = new CancellationTokenSource();
                var commands = new CommandProbe(null) { ReturnedRows = [1, 2] };
                await using var context = CreateEnumerationContext(mode, commands);
                if (async) await context.Database.OpenConnectionAsync(); else context.Database.OpenConnection();
                var failure = outcome == "cancel" ? (Exception)new OperationCanceledException(cancellation.Token)
                    : new InvalidOperationException("read failed with caller lease");
                commands.BeforeRead = (read, token) =>
                {
                    if (read != 2 || outcome is "complete" or "early") return;
                    if (outcome == "cancel") cancellation.Cancel();
                    throw failure;
                };
                var query = context.Set<Row>().OrderBy(r => r.Id).Select(r => r.Id);
                var error = await Record.ExceptionAsync(async () =>
                {
                    if (async)
                    {
                        await foreach (var value in query.AsAsyncEnumerable().WithCancellation(cancellation.Token))
                        {
                            Assert.InRange(value, 1, 2);
                            if (outcome == "early") break;
                        }
                    }
                    else
                    {
                        foreach (var value in query.AsEnumerable())
                        {
                            Assert.InRange(value, 1, 2);
                            if (outcome == "early") break;
                        }
                    }
                });
                if (outcome is "failure" or "cancel") Assert.Same(failure, error); else Assert.Null(error);
                Assert.Equal(1, commands.Executions);
                Assert.Equal(outcome == "complete" ? 3 : outcome == "early" ? 1 : 2, Assert.Single(commands.Readers).Reads);
                Assert.True(commands.Readers[0].Disposed);
                Assert.Equal(ConnectionState.Open, context.Database.GetDbConnection().State);
                if (async) await context.Database.CloseConnectionAsync(); else context.Database.CloseConnection();
                Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
                commands.BeforeRead = null;
                commands.ReturnedRows = [9];
                Assert.Equal(new[] { 9 }, async ? await query.ToArrayAsync() : query.ToArray());
                Assert.Equal(2, commands.Executions);
                Assert.All(commands.Readers, reader => Assert.True(reader.Disposed));
                Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
            }
        }

        [Theory]
        [MemberData(nameof(ModelMaterializationTests.TrackingCases), MemberType = typeof(ModelMaterializationTests))]
        public async Task Partial_entity_enumeration_keeps_identity_scope_and_already_tracked_rows(
            OceanBaseCompatibilityMode mode, bool async, QueryTrackingBehavior tracking)
        {
            var commands = new CommandProbe(null) { ReturnedRows = [1, 1, 2] };
            await using var context = CreateEnumerationContext(mode, commands);
            context.ChangeTracker.QueryTrackingBehavior = tracking;
            var query = context.Set<Row>().OrderBy(r => r.Id);
            Row first;
            if (async)
            {
                await using var iterator = query.AsAsyncEnumerable().GetAsyncEnumerator();
                Assert.True(await iterator.MoveNextAsync());
                first = iterator.Current;
                Assert.True(await iterator.MoveNextAsync());
                Assert.Equal(tracking != QueryTrackingBehavior.NoTracking, ReferenceEquals(first, iterator.Current));
            }
            else
            {
                using var iterator = query.AsEnumerable().GetEnumerator();
                Assert.True(iterator.MoveNext());
                first = iterator.Current;
                Assert.True(iterator.MoveNext());
                Assert.Equal(tracking != QueryTrackingBehavior.NoTracking, ReferenceEquals(first, iterator.Current));
            }
            Assert.Equal(1, first.Id);
            Assert.Equal(tracking == QueryTrackingBehavior.TrackAll ? 1 : 0, context.ChangeTracker.Entries().Count());
            Assert.Equal(2, Assert.Single(commands.Readers).Reads);
            Assert.True(commands.Readers[0].Disposed);
            Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
            commands.ReturnedRows = [1];
            var again = Assert.Single(async ? await query.ToArrayAsync() : query.ToArray());
            Assert.Equal(tracking == QueryTrackingBehavior.TrackAll, ReferenceEquals(first, again));
            Assert.All(commands.Readers, reader => Assert.True(reader.Disposed));
            Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Query_command_timeout_and_inflight_cancellation_follow_ef_contract(
            OceanBaseCompatibilityMode mode, bool async)
        {
            using var cancellation = new CancellationTokenSource();
            var failure = new OperationCanceledException("Synthetic command cancellation", cancellation.Token);
            var commands = new CommandProbe(failure);
            await using var context = CreateContext(mode, commands);
            context.Database.SetCommandTimeout(17);
            var query = context.Set<Row>().Select(row => row.Id);

            if (async)
            {
                var actual = await Assert.ThrowsAsync<OperationCanceledException>(
                    () => query.ToListAsync(cancellation.Token));
                Assert.Same(failure, actual);
                Assert.Equal(cancellation.Token, actual.CancellationToken);
                Assert.Equal(cancellation.Token, Assert.Single(commands.Tokens));
            }
            else
            {
                var actual = Assert.Throws<OperationCanceledException>(() => query.ToList());
                Assert.Same(failure, actual);
                Assert.Equal(cancellation.Token, actual.CancellationToken);
                Assert.Empty(commands.Tokens);
            }

            Assert.Equal(17, commands.CommandTimeout);
            Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public async Task Pre_cancelled_query_does_not_open_or_execute(OceanBaseCompatibilityMode mode)
        {
            var commands = new CommandProbe(null);
            await using var context = CreateContext(mode, commands);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => context.Set<Row>().Select(row => row.Id).ToListAsync(cancellation.Token));

            Assert.Equal(0, commands.Executions);
            Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Failed_query_command_pipeline_can_be_reused(
            OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new CommandProbe(new OperationCanceledException("first"));
            await using var context = CreateContext(mode, commands);
            var query = context.Set<Row>().Select(row => row.Id);

            if (async)
            {
                await Assert.ThrowsAsync<OperationCanceledException>(() => query.ToListAsync());
            }
            else
            {
                Assert.Throws<OperationCanceledException>(() => query.ToList());
            }
            Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
            commands.Failure = null;
            commands.ReturnedRows = [42];

            var values = async ? await query.ToListAsync() : query.ToList();
            Assert.Equal([42], values);
            Assert.Equal(2, commands.Executions);
            Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Successful_query_disposes_reader_closes_and_reuses_connection(
            OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new CommandProbe(null) { ReturnedRows = [1, 2] };
            await using var context = CreateContext(mode, commands);
            var native = Assert.IsType<OceanBaseConnection>(context.Database.GetDbConnection());
            var configured = new OceanBaseConnectionStringBuilder(native.ConnectionString);
            Assert.Equal(mode.ToString(), configured.CompatibilityMode.ToString());
            Assert.False(configured.UseAffectedRows);
            var query = context.Set<Row>().Select(row => row.Id);

            var first = async ? await query.ToListAsync() : query.ToList();
            Assert.Equal([1, 2], first);
            Assert.True(Assert.Single(commands.Readers).Disposed);
            Assert.Equal(ConnectionState.Closed, native.State);

            commands.ReturnedRows = [3];
            var second = async ? await query.ToListAsync() : query.ToList();
            Assert.Equal([3], second);
            Assert.Equal(2, commands.Executions);
            Assert.All(commands.Readers, reader => Assert.True(reader.Disposed));
            Assert.Same(native, context.Database.GetDbConnection());
            Assert.Equal(ConnectionState.Closed, native.State);
        }

        private static TestContext CreateContext(OceanBaseCompatibilityMode mode, CommandProbe commands)
            => new(new DbContextOptionsBuilder<TestContext>()
                .UseOceanBase("Server=localhost;Database=test", mode)
                .AddInterceptors(new NoNetwork(), commands).Options);

        private static TestContext CreateEnumerationContext(OceanBaseCompatibilityMode mode, CommandProbe commands, bool retries = false)
            => new(new DbContextOptionsBuilder<TestContext>()
                .UseOceanBase("Server=offline;Database=app", mode, options =>
                {
                    if (retries) options.EnableRetryOnFailure(1, TimeSpan.Zero);
                })
                .ReplaceService<IRelationalConnection, TransactionTests.TestConnection>()
                .AddInterceptors(commands).Options);

        private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
        {
            public DbSet<Row> Rows => Set<Row>();
        }

        private sealed class Row
        {
            public int Id { get; set; }
        }

        private sealed class NoNetwork : DbConnectionInterceptor
        {
            public override InterceptionResult ConnectionOpening(
                DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
                => InterceptionResult.Suppress();

            public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
                DbConnection connection, ConnectionEventData eventData, InterceptionResult result,
                CancellationToken cancellationToken = default)
                => ValueTask.FromResult(InterceptionResult.Suppress());
        }

        private sealed class CommandProbe(Exception? failure) : DbCommandInterceptor
        {
            public Exception? Failure { get; set; } = failure;
            public int CommandTimeout { get; private set; }
            public int Executions { get; private set; }
            public List<CancellationToken> Tokens { get; } = [];
            public List<TrackingReader> Readers { get; } = [];
            public int[] ReturnedRows { get; set; } = [];
            public Action<int, CancellationToken>? BeforeRead { get; set; }

            public override InterceptionResult<DbDataReader> ReaderExecuting(
                DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
            {
                Capture(command);
                if (Failure is not null) throw Failure;
                return InterceptionResult<DbDataReader>.SuppressWithResult(CreateReader());
            }

            public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
                DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
                CancellationToken cancellationToken = default)
            {
                Capture(command);
                Tokens.Add(cancellationToken);
                if (Failure is not null) throw Failure;
                return ValueTask.FromResult(InterceptionResult<DbDataReader>.SuppressWithResult(CreateReader()));
            }

            private void Capture(DbCommand command)
            {
                CommandTimeout = command.CommandTimeout;
                Executions++;
            }

            private DbDataReader CreateReader()
            {
                var table = new DataTable();
                table.Columns.Add("Id", typeof(int));
                foreach (var value in ReturnedRows) table.Rows.Add(value);
                var reader = new TrackingReader(table.CreateDataReader(), BeforeRead);
                Readers.Add(reader);
                return reader;
            }
        }

        private sealed class TrackingReader(DbDataReader inner, Action<int, CancellationToken>? beforeRead) : DbDataReader
        {
            public bool Disposed { get; private set; }
            public int Reads { get; private set; }
            public List<CancellationToken> ReadTokens { get; } = [];
            public Exception? ReadFailure { get; private set; }
            public override bool Read()
            {
                Reads++;
                beforeRead?.Invoke(Reads, default);
                return inner.Read();
            }
            public override async Task<bool> ReadAsync(CancellationToken cancellationToken)
            {
                Reads++;
                ReadTokens.Add(cancellationToken);
                try
                {
                    beforeRead?.Invoke(Reads, cancellationToken);
                    return await inner.ReadAsync(cancellationToken);
                }
                catch (Exception error) { ReadFailure = error; throw; }
            }
            public override void Close() => inner.Close();
            protected override void Dispose(bool disposing)
            {
                if (disposing && !Disposed) { Disposed = true; inner.Dispose(); }
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
            public override long GetBytes(int ordinal, long offset, byte[]? buffer, int bufferOffset, int length)
                => inner.GetBytes(ordinal, offset, buffer, bufferOffset, length);
            public override char GetChar(int ordinal) => inner.GetChar(ordinal);
            public override long GetChars(int ordinal, long offset, char[]? buffer, int bufferOffset, int length)
                => inner.GetChars(ordinal, offset, buffer, bufferOffset, length);
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
}
