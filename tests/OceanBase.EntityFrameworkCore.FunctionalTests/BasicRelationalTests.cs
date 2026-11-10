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
    public sealed class BasicRelationalTests
    {
        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task SaveChanges_binds_values_and_propagates_generated_keys(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new CommandCapture();
            await using var context = CreateContext(mode, commands);
            var customer = new Customer { Name = "O'Reilly", Version = 1 };
            context.Add(customer);

            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            Assert.Equal(42, customer.Id);
            Assert.Contains(commands.Sql, sql => sql.StartsWith("INSERT INTO", StringComparison.Ordinal));
            Assert.Contains(commands.Values, value => Equals(value, "O'Reilly"));
            Assert.All(commands.Sql, sql => Assert.DoesNotContain("O'Reilly", sql));
            if (mode == OceanBaseCompatibilityMode.MySql)
            {
                Assert.Contains(commands.Sql, sql => sql.Contains("LAST_INSERT_ID()", StringComparison.Ordinal));
            }
            else
            {
                Assert.Contains(commands.Sql, sql => sql.Contains("RETURNING \"Id\" INTO :ob_out_0", StringComparison.Ordinal));
                Assert.DoesNotContain(commands.Sql, sql => sql.Contains("LAST_INSERT_ID", StringComparison.Ordinal));
            }

            customer.Name = "Updated";
            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            Assert.Contains(commands.Sql, sql => sql.StartsWith("UPDATE", StringComparison.Ordinal));

            context.Remove(customer);
            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            Assert.Contains(commands.Sql, sql => sql.StartsWith("DELETE", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Zero_affected_rows_throws_concurrency_exception(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new CommandCapture { AffectedRows = 0 };
            await using var context = CreateContext(mode, commands);
            var customer = new Customer { Id = 1, Name = "Previous", Version = 1 };
            context.Attach(customer);
            customer.Name = "Updated";

            if (async)
            {
                await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => context.SaveChangesAsync());
            }
            else
            {
                Assert.Throws<DbUpdateConcurrencyException>(() => context.SaveChanges());
            }

            Assert.Contains(commands.Sql, sql => sql.Contains("Version", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Query_materializes_scalar_results(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new CommandCapture();
            await using var context = CreateContext(mode, commands);
            var minimumId = 3;
            var query = context.Set<Customer>().Where(customer => customer.Id > minimumId).Select(customer => customer.Id);

            var values = async ? await query.ToListAsync() : query.ToList();

            Assert.Equal([42], values);
            Assert.Contains(commands.Sql, sql => sql.Contains("WHERE", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public async Task SaveChangesAsync_preserves_cancellation(OceanBaseCompatibilityMode mode)
        {
            var commands = new CommandCapture();
            await using var context = CreateContext(mode, commands);
            context.Add(new Customer { Name = "Cancelled" });
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.SaveChangesAsync(cancellation.Token));
            Assert.Empty(commands.Sql);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Default_only_insert_uses_mode_specific_syntax(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new CommandCapture();
            await using var context = CreateContext(mode, commands);
            var entity = new KeyOnly();
            context.Add(entity);

            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            Assert.Equal(42, entity.Id);
            Assert.Contains(commands.Sql, sql => sql.Contains(
                mode == OceanBaseCompatibilityMode.MySql ? "() VALUES ()" : "(\"Id\") VALUES (DEFAULT)", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task Unsupported_MySql_generated_keys_fail_before_writes(bool stringKey, bool async)
        {
            var commands = new CommandCapture();
            await using var context = CreateContext(OceanBaseCompatibilityMode.MySql, commands);
            if (stringKey)
            {
                context.Add(new StringDefaultKey());
            }
            else
            {
                context.Add(new NumericDefaultKey());
            }

            if (async)
            {
                await Assert.ThrowsAsync<NotSupportedException>(() => context.SaveChangesAsync());
            }
            else
            {
                Assert.Throws<NotSupportedException>(() => context.SaveChanges());
            }

            Assert.Empty(commands.Sql);
        }

        private static TestContext CreateContext(OceanBaseCompatibilityMode mode, CommandCapture commands)
        {
            var context = new TestContext(new DbContextOptionsBuilder<TestContext>()
                .UseOceanBase("Server=localhost;Database=test", mode)
                .AddInterceptors(new NoNetworkConnection(), commands).Options);
            // These tests exercise EF commands; transaction semantics are verified separately against OceanBase.
            context.Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
            return context;
        }

        private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
        {
            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity<Customer>().ToTable("Customers");
                modelBuilder.Entity<Customer>().Property(customer => customer.Version).IsConcurrencyToken();
                modelBuilder.Entity<KeyOnly>().ToTable("Keys");
                modelBuilder.Entity<StringDefaultKey>().Property(entity => entity.Id).HasDefaultValueSql("UUID()");
                modelBuilder.Entity<NumericDefaultKey>().Property(entity => entity.Id).HasDefaultValueSql("123");
            }
        }

        private sealed class Customer
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public int Version { get; set; }
        }

        private sealed class KeyOnly
        {
            public int Id { get; set; }
        }

        private sealed class StringDefaultKey
        {
            public string Id { get; set; } = null!;
        }

        private sealed class NumericDefaultKey
        {
            public int Id { get; set; }
        }

        private sealed class NoNetworkConnection : DbConnectionInterceptor
        {
            public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
                => InterceptionResult.Suppress();

            public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
                DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(InterceptionResult.Suppress());
        }

        private sealed class CommandCapture : DbCommandInterceptor
        {
            public List<string> Sql { get; } = [];
            public List<object?> Values { get; } = [];
            public int AffectedRows { get; init; } = 1;

            public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
            {
                Sql.Add(command.CommandText);
                foreach (DbParameter parameter in command.Parameters)
                {
                    if (parameter.Direction == ParameterDirection.Output)
                    {
                        // Oracle numeric outputs may be returned as decimal by the connector.
                        parameter.Value = 42m;
                    }
                    else
                    {
                        Values.Add(parameter.Value);
                    }
                }

                return InterceptionResult<int>.SuppressWithResult(AffectedRows);
            }

            public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
                DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(NonQueryExecuting(command, eventData, result));

            public override InterceptionResult<DbDataReader> ReaderExecuting(
                DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
            {
                Sql.Add(command.CommandText);
                var table = new DataTable();
                table.Columns.Add("Id", typeof(int));
                table.Rows.Add(42);
                return InterceptionResult<DbDataReader>.SuppressWithResult(table.CreateDataReader());
            }

            public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
                DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(ReaderExecuting(command, eventData, result));
        }
    }
}
