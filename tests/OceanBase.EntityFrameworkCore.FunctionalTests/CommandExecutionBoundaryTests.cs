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
                return table.CreateDataReader();
            }
        }
    }
}
