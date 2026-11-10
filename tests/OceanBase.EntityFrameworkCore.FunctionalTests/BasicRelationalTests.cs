using System;
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
    public sealed class BasicRelationalTests
    {
        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Query_and_write_apply_bounded_binary_and_custom_converters(OceanBaseCompatibilityMode mode, bool async)
        {
            var bytes = new byte[] { 0, 0x80, 0xff, 0 };
            using var table = new DataTable();
            table.Columns.Add("Id", mode == OceanBaseCompatibilityMode.Oracle ? typeof(decimal) : typeof(int));
            table.Columns.Add("Payload", typeof(byte[]));
            table.Columns.Add("Optional", typeof(byte[]));
            table.Columns.Add("Hex", typeof(byte[]));
            table.Rows.Add(17, bytes, DBNull.Value, bytes);
            var commands = new CommandCapture { Results = table };
            await using var context = CreateContext(mode, commands);
            var query = context.Set<BinaryRow>().Select(row => new { row.Id, row.Payload, row.Optional, row.Hex });

            var row = Assert.Single(async ? await query.ToListAsync() : query.ToList());
            Assert.Equal(bytes, row.Payload);
            Assert.Null(row.Optional);
            Assert.Equal("0080FF00", row.Hex);
            var mapping = context.Model.FindEntityType(typeof(BinaryRow))!.FindProperty(nameof(BinaryRow.Payload))!
                .GetRelationalTypeMapping();
            Assert.Equal(mode == OceanBaseCompatibilityMode.Oracle ? "RAW(8)" : "VARBINARY(8)", mapping.StoreType);
            context.Add(new BinaryRow { Id = row.Id, Payload = row.Payload, Hex = row.Hex });

            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            Assert.Equal(2, commands.Values.OfType<byte[]>().Count(value => value.SequenceEqual(bytes)));
            Assert.Contains(DBNull.Value, commands.Values);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Oversized_binary_write_is_rejected_before_command_execution(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new CommandCapture();
            await using var context = CreateContext(mode, commands);
            context.Add(new BinaryRow { Id = 1, Payload = new byte[9], Hex = "01" });

            var exception = async
                ? await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync())
                : Assert.Throws<DbUpdateException>(() => context.SaveChanges());
            Assert.IsType<ArgumentException>(exception.InnerException);
            Assert.Empty(commands.Sql);
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
        public async Task Generated_binary_and_guid_outputs_apply_provider_converters(
            OceanBaseCompatibilityMode mode, bool guid, bool async)
        {
            var token = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
            var bytes = new byte[] { 0, 0x80, 0xff };
            object output = guid ? mode == OceanBaseCompatibilityMode.Oracle ? token.ToByteArray() : token.ToString() : bytes;
            using var table = new DataTable();
            table.Columns.Add("Data", output.GetType());
            table.Rows.Add(output);
            var commands = new CommandCapture { Results = table, OutputValue = output };
            await using var context = CreateContext(mode, commands);
            var binaryEntity = new GeneratedBinary { Id = 1 };
            var guidEntity = new GeneratedGuid { Id = 1 };
            context.Add(guid ? (object)guidEntity : binaryEntity);

            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            if (guid) Assert.Equal(token, guidEntity.Data);
            else Assert.Equal(bytes, binaryEntity.Data);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Query_and_write_apply_temporal_transport_conversions(OceanBaseCompatibilityMode mode, bool async)
        {
            var oracle = mode == OceanBaseCompatibilityMode.Oracle;
            var stamp = new DateTime(2026, 11, 10, 12, 30, 45);
            var zoned = new DateTimeOffset(stamp, TimeSpan.FromHours(8));
            using var table = new DataTable();
            table.Columns.Add("Id", oracle ? typeof(decimal) : typeof(int));
            table.Columns.Add("Date", typeof(DateTime));
            table.Columns.Add("Stamp", oracle ? typeof(OceanBaseTimestamp) : typeof(DateTime));
            table.Columns.Add("Zoned", oracle ? typeof(OceanBaseTimestampWithTimeZone) : typeof(DateTime));
            table.Columns.Add("Clock", oracle ? typeof(OceanBaseIntervalDayToSecond) : typeof(TimeSpan));
            table.Columns.Add("Duration", oracle ? typeof(OceanBaseIntervalDayToSecond) : typeof(TimeSpan));
            table.Columns.Add("OptionalDate", typeof(DateTime));
            table.Rows.Add(19, new DateTime(2026, 11, 10), oracle ? new OceanBaseTimestamp(stamp) : stamp,
                oracle ? new OceanBaseTimestampWithTimeZone(zoned) : new DateTime(2026, 11, 10, 4, 30, 45),
                oracle ? new OceanBaseIntervalDayToSecond(false, 0, 12, 30, 45, leadingPrecision: 1, scale: 7) : new TimeSpan(12, 30, 45),
                oracle ? new OceanBaseIntervalDayToSecond(true, 2, 3, 4, 5, scale: 7) : -new TimeSpan(2, 3, 4, 5), DBNull.Value);
            var commands = new CommandCapture { Results = table };
            await using var context = CreateContext(mode, commands);
            var query = context.Set<TemporalRow>().Select(row => new
                { row.Id, row.Date, row.Stamp, row.Zoned, row.Clock, row.Duration, row.OptionalDate });

            var row = Assert.Single(async ? await query.ToListAsync() : query.ToList());

            Assert.Equal(new DateOnly(2026, 11, 10), row.Date);
            Assert.Equal(stamp, row.Stamp);
            Assert.Equal(zoned.UtcTicks, row.Zoned.UtcTicks);
            Assert.Equal(oracle ? TimeSpan.FromHours(8) : TimeSpan.Zero, row.Zoned.Offset);
            Assert.Equal(new TimeOnly(12, 30, 45), row.Clock);
            Assert.Equal(-new TimeSpan(2, 3, 4, 5), row.Duration);
            Assert.Null(row.OptionalDate);
            context.Add(new TemporalRow { Id = row.Id, Date = row.Date, Stamp = row.Stamp, Zoned = row.Zoned,
                Clock = row.Clock, Duration = row.Duration });
            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            Assert.Contains(commands.Values, value => Equals(value, new DateTime(2026, 11, 10)));
            Assert.Contains(commands.Values, value => Equals(value, stamp));
            Assert.Contains(commands.Values, value => Equals(value, DBNull.Value));
            if (oracle)
            {
                Assert.Contains(commands.Values, value => value is OceanBaseIntervalDayToSecond interval && interval.ToTimeSpan() == row.Duration);
                Assert.Contains(commands.Values, value => value is DateTimeOffset date && date.Offset == TimeSpan.FromHours(8));
            }
            else
            {
                Assert.Contains(commands.Values, value => Equals(value, new DateTime(2026, 11, 10, 4, 30, 45)));
                Assert.Contains(commands.Values, value => Equals(value, row.Duration));
            }
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task Oracle_generated_temporal_values_use_exact_public_conversions(bool zoned, bool async)
        {
            var stamp = new DateTime(2026, 11, 10, 12, 30, 45).AddTicks(1234567);
            var date = new DateTimeOffset(stamp, TimeSpan.FromMinutes(-330));
            var commands = new CommandCapture { OutputValue = zoned ? new OceanBaseTimestampWithTimeZone(date) : new OceanBaseTimestamp(stamp) };
            await using var context = CreateContext(OceanBaseCompatibilityMode.Oracle, commands);
            var plainEntity = new GeneratedStamp { Id = 1 };
            var zonedEntity = new GeneratedZoned { Id = 1 };
            context.Add(zoned ? (object)zonedEntity : plainEntity);

            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            if (zoned)
            {
                Assert.Equal(date.UtcTicks, zonedEntity.Stamp.UtcTicks);
                Assert.Equal(date.Offset, zonedEntity.Stamp.Offset);
            }
            else
            {
                Assert.Equal(stamp, plainEntity.Stamp);
            }
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task Oracle_generated_keys_accept_exact_connector_numbers(bool exactNumber, bool async)
        {
            var commands = new CommandCapture { OutputValue = exactNumber ? new OceanBaseDecimal(42m) : 42m };
            await using var context = CreateContext(OceanBaseCompatibilityMode.Oracle, commands);
            var entity = new KeyOnly();
            context.Add(entity);

            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            Assert.Equal(42, entity.Id);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task Oracle_generated_integer_keys_reject_fraction_and_overflow(bool overflow, bool async)
        {
            var commands = new CommandCapture { OutputValue = overflow ? (decimal)int.MaxValue + 1 : 1.5m };
            await using var context = CreateContext(OceanBaseCompatibilityMode.Oracle, commands);
            context.Add(new KeyOnly());

            var exception = async
                ? await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync())
                : Assert.Throws<DbUpdateException>(() => context.SaveChanges());

            if (overflow)
            {
                Assert.IsType<OverflowException>(exception.InnerException);
            }
            else
            {
                Assert.IsType<InvalidCastException>(exception.InnerException);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Oracle_generated_Boolean_values_apply_converter_after_numeric_normalization(bool async)
        {
            var commands = new CommandCapture { OutputValue = new OceanBaseDecimal(1m) };
            await using var context = CreateContext(OceanBaseCompatibilityMode.Oracle, commands);
            var entity = new GeneratedFlag { Id = 5 };
            context.Add(entity);

            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            Assert.True(entity.Enabled);
            Assert.Contains(commands.Sql, sql => sql.Contains("RETURNING \"Enabled\"", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Query_and_write_apply_scalar_converters(OceanBaseCompatibilityMode mode, bool async)
        {
            var oracle = mode == OceanBaseCompatibilityMode.Oracle;
            var token = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
            using var table = new DataTable();
            table.Columns.Add("Id", oracle ? typeof(decimal) : typeof(int));
            table.Columns.Add("Count", oracle ? typeof(decimal) : typeof(uint));
            table.Columns.Add("Enabled", oracle ? typeof(decimal) : typeof(long));
            table.Columns.Add("OptionalCount", oracle ? typeof(decimal) : typeof(int));
            table.Columns.Add("State", oracle ? typeof(decimal) : typeof(int));
            table.Columns.Add("Token", oracle ? typeof(byte[]) : typeof(string));
            table.Rows.Add(17, uint.MaxValue, 1, DBNull.Value, 2, oracle ? token.ToByteArray() : token.ToString());
            var commands = new CommandCapture { Results = table };
            await using var context = CreateContext(mode, commands);
            var query = context.Set<ScalarRow>().Select(row => new
                { row.Id, row.Count, row.Enabled, row.OptionalCount, row.State, row.Token });

            var row = Assert.Single(async ? await query.ToListAsync() : query.ToList());

            Assert.Equal(17, row.Id);
            Assert.Equal(uint.MaxValue, row.Count);
            Assert.True(row.Enabled);
            Assert.Null(row.OptionalCount);
            Assert.Equal(RowState.Active, row.State);
            Assert.Equal(token, row.Token);
            context.Add(new ScalarRow { Id = row.Id, Count = row.Count, Enabled = row.Enabled, State = row.State, Token = row.Token });
            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            Assert.Contains(commands.Values, value => Equals(value, uint.MaxValue));
            Assert.Contains(commands.Values, value => Equals(value, 2));
            Assert.Contains(commands.Values, value => Equals(value, oracle ? 1m : (object)true));
            Assert.Contains(commands.Values, value => Equals(value, DBNull.Value));
            if (oracle)
            {
                Assert.Contains(commands.Values, value => value is byte[] bytes && bytes.SequenceEqual(token.ToByteArray()));
            }
            else
            {
                Assert.Contains(token.ToString(), commands.Values);
            }
        }

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
                modelBuilder.Entity<ScalarRow>().Property(entity => entity.Id).ValueGeneratedNever();
                modelBuilder.Entity<ScalarRow>().Property(entity => entity.State).HasConversion<int>();
                modelBuilder.Entity<GeneratedFlag>().Property(entity => entity.Id).ValueGeneratedNever();
                modelBuilder.Entity<GeneratedFlag>().Property(entity => entity.Enabled).HasComputedColumnSql("1");
                modelBuilder.Entity<TemporalRow>().Property(entity => entity.Id).ValueGeneratedNever();
                modelBuilder.Entity<GeneratedStamp>().Property(entity => entity.Id).ValueGeneratedNever();
                modelBuilder.Entity<GeneratedStamp>().Property(entity => entity.Stamp).HasComputedColumnSql("CURRENT_TIMESTAMP");
                modelBuilder.Entity<GeneratedZoned>().Property(entity => entity.Id).ValueGeneratedNever();
                modelBuilder.Entity<GeneratedZoned>().Property(entity => entity.Stamp).HasComputedColumnSql("CURRENT_TIMESTAMP");
                modelBuilder.Entity<BinaryRow>().Property(entity => entity.Id).ValueGeneratedNever();
                modelBuilder.Entity<BinaryRow>().Property(entity => entity.Payload).HasMaxLength(8);
                modelBuilder.Entity<BinaryRow>().Property(entity => entity.Hex).HasMaxLength(8)
                    .HasConversion(value => Convert.FromHexString(value), value => Convert.ToHexString(value));
                modelBuilder.Entity<GeneratedBinary>().Property(entity => entity.Id).ValueGeneratedNever();
                modelBuilder.Entity<GeneratedBinary>().Property(entity => entity.Data).HasMaxLength(8).HasComputedColumnSql("NULL");
                modelBuilder.Entity<GeneratedGuid>().Property(entity => entity.Id).ValueGeneratedNever();
                modelBuilder.Entity<GeneratedGuid>().Property(entity => entity.Data).HasComputedColumnSql("NULL");
            }
        }

        private enum RowState { Inactive = 0, Active = 2 }

        private sealed class BinaryRow
        {
            public int Id { get; set; }
            public byte[] Payload { get; set; } = [];
            public byte[]? Optional { get; set; }
            public string Hex { get; set; } = string.Empty;
        }

        private sealed class GeneratedBinary
        {
            public int Id { get; set; }
            public byte[] Data { get; set; } = [];
        }

        private sealed class GeneratedGuid
        {
            public int Id { get; set; }
            public Guid Data { get; set; }
        }

        private sealed class TemporalRow
        {
            public int Id { get; set; }
            public DateOnly Date { get; set; }
            public DateTime Stamp { get; set; }
            public DateTimeOffset Zoned { get; set; }
            public TimeOnly Clock { get; set; }
            public TimeSpan Duration { get; set; }
            public DateOnly? OptionalDate { get; set; }
        }

        private sealed class GeneratedStamp
        {
            public int Id { get; set; }
            public DateTime Stamp { get; set; }
        }

        private sealed class GeneratedZoned
        {
            public int Id { get; set; }
            public DateTimeOffset Stamp { get; set; }
        }

        private sealed class GeneratedFlag
        {
            public int Id { get; set; }
            public bool Enabled { get; set; }
        }

        private sealed class ScalarRow
        {
            public int Id { get; set; }
            public uint Count { get; set; }
            public bool Enabled { get; set; }
            public int? OptionalCount { get; set; }
            public RowState State { get; set; }
            public Guid Token { get; set; }
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
            public DataTable? Results { get; init; }
            public object OutputValue { get; init; } = 42m;

            public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
            {
                Sql.Add(command.CommandText);
                foreach (DbParameter parameter in command.Parameters)
                {
                    if (parameter.Direction == ParameterDirection.Output)
                    {
                        // Oracle numeric outputs may be returned as decimal by the connector.
                        parameter.Value = OutputValue;
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
                if (Results is not null)
                {
                    return InterceptionResult<DbDataReader>.SuppressWithResult(Results.CreateDataReader());
                }

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
