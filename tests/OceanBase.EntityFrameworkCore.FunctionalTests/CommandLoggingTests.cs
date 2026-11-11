using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Xunit;

namespace OceanBase.EntityFrameworkCore.FunctionalTests
{
    public sealed class CommandLoggingTests
    {
        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, true)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, true)]
        public async Task Commands_are_logged_with_parameter_values_redacted_unless_enabled(
            OceanBaseCompatibilityMode mode, bool async, bool sensitive)
        {
            var messages = new List<string>();
            var commands = new CommandProbe();
            await using var context = CreateContext(mode, commands, messages, sensitive);
            var secret = "secret @p ?";
            var query = context.Set<Row>().Where(row => row.Name == secret).Select(row => row.Id);

            var values = async ? await query.ToListAsync() : query.ToList();

            Assert.Equal([42], values);
            var log = string.Join(Environment.NewLine, messages);
            Assert.Contains("SELECT", log, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Executed DbCommand", log, StringComparison.Ordinal);
            Assert.Equal(async ? 0 : 1, commands.SyncExecutions);
            Assert.Equal(async ? 1 : 0, commands.AsyncExecutions);
            if (sensitive) Assert.Contains(secret, log, StringComparison.Ordinal);
            else Assert.DoesNotContain(secret, log, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, true)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, true)]
        public async Task Command_failures_are_logged_and_preserve_parameter_redaction(
            OceanBaseCompatibilityMode mode, bool async, bool sensitive)
        {
            var messages = new List<string>();
            var failure = new InvalidOperationException("synthetic provider failure");
            var commands = new CommandProbe(failure);
            await using var context = CreateContext(mode, commands, messages, sensitive);
            var secret = "failure secret @p ?";
            var query = context.Set<Row>().Where(row => row.Name == secret).Select(row => row.Id);

            if (async)
            {
                Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => query.ToListAsync()));
            }
            else
            {
                Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => query.ToList()));
            }

            var log = string.Join(Environment.NewLine, messages);
            Assert.Contains("synthetic provider failure", log, StringComparison.Ordinal);
            Assert.Contains("Failed executing DbCommand", log, StringComparison.Ordinal);
            Assert.Equal(async ? 0 : 1, commands.SyncExecutions);
            Assert.Equal(async ? 1 : 0, commands.AsyncExecutions);
            if (sensitive) Assert.Contains(secret, log, StringComparison.Ordinal);
            else Assert.DoesNotContain(secret, log, StringComparison.Ordinal);
        }

        private static LoggingContext CreateContext(
            OceanBaseCompatibilityMode mode, CommandProbe commands, List<string> messages, bool sensitive)
        {
            var builder = new DbContextOptionsBuilder<LoggingContext>()
                .UseOceanBase("Server=localhost;Database=test", mode)
                .AddInterceptors(new NoNetwork(), commands)
                .LogTo(messages.Add, LogLevel.Information);
            if (sensitive) builder.EnableSensitiveDataLogging();
            return new LoggingContext(builder.Options);
        }

        private sealed class LoggingContext(DbContextOptions<LoggingContext> options) : DbContext(options)
        {
            public DbSet<Row> Rows => Set<Row>();
        }

        private sealed class Row
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
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

        private sealed class CommandProbe(Exception? failure = null) : DbCommandInterceptor
        {
            public Exception? Failure { get; } = failure;
            public int SyncExecutions { get; private set; }
            public int AsyncExecutions { get; private set; }

            public override InterceptionResult<DbDataReader> ReaderExecuting(
                DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
            {
                SyncExecutions++;
                if (Failure is not null) throw Failure;
                return InterceptionResult<DbDataReader>.SuppressWithResult(CreateReader());
            }

            public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
                DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
                CancellationToken cancellationToken = default)
            {
                AsyncExecutions++;
                if (Failure is not null) throw Failure;
                return ValueTask.FromResult(InterceptionResult<DbDataReader>.SuppressWithResult(CreateReader()));
            }

            private static DbDataReader CreateReader()
            {
                var table = new DataTable();
                table.Columns.Add("Id", typeof(int));
                table.Rows.Add(42);
                return table.CreateDataReader();
            }
        }
    }
}
