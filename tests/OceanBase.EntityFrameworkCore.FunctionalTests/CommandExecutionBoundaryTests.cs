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
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.FunctionalTests
{
    public sealed class CommandExecutionBoundaryTests
    {
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
            Assert.Equal(mode.ToString(), configured.ExpectedMode.ToString());
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
                var reader = new TrackingReader(table.CreateDataReader());
                Readers.Add(reader);
                return reader;
            }
        }

        private sealed class TrackingReader(DbDataReader inner) : DbDataReader
        {
            public bool Disposed { get; private set; }
            public override bool Read() => inner.Read();
            public override Task<bool> ReadAsync(CancellationToken cancellationToken) => inner.ReadAsync(cancellationToken);
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
