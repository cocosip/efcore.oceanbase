using System;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests
{
    public sealed class ConnectionOwnershipTests
    {
        public static TheoryData<OceanBaseCompatibilityMode, bool> Modes
        {
            get
            {
                var data = new TheoryData<OceanBaseCompatibilityMode, bool>();
                foreach (var mode in Enum.GetValues<OceanBaseCompatibilityMode>())
                {
                    data.Add(mode, false);
                    data.Add(mode, true);
                }

                return data;
            }
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Caller_owned_connection_survives_context_disposal_after_provider_open(
            OceanBaseCompatibilityMode mode, bool async)
        {
            using var supplied = new OceanBaseConnection("Server=localhost;Database=test");
            var context = Create(supplied, mode);
            var relational = context.GetService<IRelationalConnection>();

            if (async) await relational.OpenAsync(CancellationToken.None);
            else relational.Open();

            if (async) await context.DisposeAsync();
            else context.Dispose();

            supplied.ConnectionString = "Server=localhost;Database=still-usable";
            Assert.Equal(ConnectionState.Closed, supplied.State);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Provider_owned_connection_is_disposed_after_provider_open(
            OceanBaseCompatibilityMode mode, bool async)
        {
            var context = Create(null, mode);
            var native = Assert.IsType<OceanBaseConnection>(context.Database.GetDbConnection());
            var relational = context.GetService<IRelationalConnection>();

            if (async) await relational.OpenAsync(CancellationToken.None);
            else relational.Open();

            if (async) await context.DisposeAsync();
            else context.Dispose();

            Assert.Throws<ObjectDisposedException>(
                () => native.ConnectionString = "Server=localhost;Database=disposed");
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Provider_owned_connection_is_disposed_after_command_failure(
            OceanBaseCompatibilityMode mode, bool async)
        {
            var context = Create(null, mode, new CommandFailure());
            var native = Assert.IsType<OceanBaseConnection>(context.Database.GetDbConnection());
            var query = context.Set<Row>().Select(row => row.Id);

            if (async)
                await Assert.ThrowsAsync<InvalidOperationException>(() => query.ToListAsync());
            else
                Assert.Throws<InvalidOperationException>(() => query.ToList());

            if (async) await context.DisposeAsync();
            else context.Dispose();

            Assert.Throws<ObjectDisposedException>(
                () => native.ConnectionString = "Server=localhost;Database=disposed");
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Caller_owned_connection_survives_command_failure_and_context_disposal(
            OceanBaseCompatibilityMode mode, bool async)
        {
            using var supplied = new OceanBaseConnection("Server=localhost;Database=test");
            var context = Create(supplied, mode, new CommandFailure());
            var query = context.Set<Row>().Select(row => row.Id);

            if (async)
                await Assert.ThrowsAsync<InvalidOperationException>(() => query.ToListAsync());
            else
                Assert.Throws<InvalidOperationException>(() => query.ToList());

            if (async) await context.DisposeAsync();
            else context.Dispose();

            supplied.ConnectionString = "Server=localhost;Database=still-usable";
        }

        private static TestContext Create(
            OceanBaseConnection? connection,
            OceanBaseCompatibilityMode mode,
            params IInterceptor[] interceptors)
        {
            var builder = new DbContextOptionsBuilder<TestContext>();
            if (connection is null)
                builder.UseOceanBase("Server=localhost;Database=test", mode);
            else
                builder.UseOceanBase(connection, mode);

            return new TestContext(builder.AddInterceptors(new SuppressNetwork()).AddInterceptors(interceptors).Options);
        }

        private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
        {
            public DbSet<Row> Rows => Set<Row>();
        }

        private sealed class Row
        {
            public int Id { get; set; }
        }

        private sealed class SuppressNetwork : DbConnectionInterceptor
        {
            public override InterceptionResult ConnectionOpening(
                DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
                => InterceptionResult.Suppress();

            public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
                DbConnection connection, ConnectionEventData eventData, InterceptionResult result,
                CancellationToken cancellationToken = default)
                => ValueTask.FromResult(InterceptionResult.Suppress());
        }

        private sealed class CommandFailure : DbCommandInterceptor
        {
            public override InterceptionResult<DbDataReader> ReaderExecuting(
                DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
                => throw new InvalidOperationException("synthetic command failure");

            public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
                DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
                CancellationToken cancellationToken = default)
                => ValueTask.FromException<InterceptionResult<DbDataReader>>(
                    new InvalidOperationException("synthetic command failure"));
        }
    }
}
