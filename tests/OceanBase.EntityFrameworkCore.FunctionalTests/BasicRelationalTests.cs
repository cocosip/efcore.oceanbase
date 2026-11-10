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
        public async Task Decimal_queries_keep_comparands_and_converted_writes_check_physical_precision(
            OceanBaseCompatibilityMode mode, bool async)
        {
            using var table = new DataTable();
            table.Columns.Add("Value", typeof(decimal));
            table.Columns.Add("Converted", typeof(decimal));
            table.Columns.Add("Optional", typeof(decimal));
            table.Rows.Add(999.99m, 2.5m, DBNull.Value);
            var commands = new CommandCapture { Results = table };
            await using var context = CreateContext(mode, commands);
            var boundary = decimal.MaxValue;
            var query = context.Set<DecimalRow>().Where(row => row.Value < boundary)
                .Select(row => new { row.Value, row.Converted, row.Optional });
            var result = Assert.Single(async ? await query.ToListAsync() : query.ToList());
            Assert.Equal(999.99m, result.Value);
            Assert.Equal(1.25m, result.Converted);
            Assert.Null(result.Optional);
            var operand = Assert.Single(commands.DecimalInputs);
            Assert.Equal(boundary, operand.Value);
            Assert.Equal((byte)0, operand.Precision);
            Assert.Null(operand.DecimalScale);
            Assert.Contains("79228162514264337593543950335.0", context.Set<DecimalRow>()
                .Select(row => row.Value == 1m ? decimal.MaxValue : row.Value).ToQueryString());
            context.Add(new DecimalRow { Id = 1, Value = 999.9900m, Converted = 1.25m });
            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            Assert.Contains(2.5m, commands.Values);
            Assert.All(commands.DecimalInputs, parameter =>
            {
                Assert.Equal((byte)0, parameter.Precision);
                Assert.Null(parameter.DecimalScale);
            });
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
        public async Task Decimal_invalid_writes_reject_before_command_execution(
            OceanBaseCompatibilityMode mode, bool converted, bool async)
        {
            var commands = new CommandCapture();
            await using var context = CreateContext(mode, commands);
            context.Add(new DecimalRow { Id = 1, Value = converted ? 1m : 0.001m,
                Converted = converted ? 500m : 1m });
            var exception = async ? await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync())
                : Assert.Throws<DbUpdateException>(() => context.SaveChanges());
            Assert.IsAssignableFrom<ArgumentException>(exception.InnerException);
            Assert.Empty(commands.Sql);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Temporal_query_comparands_keep_transport_precision_independent_of_columns(
            OceanBaseCompatibilityMode mode, bool async)
        {
            var oracle = mode == OceanBaseCompatibilityMode.Oracle;
            var stamp = new DateTime(2026, 11, 10, 12, 0, 0).AddTicks(1234560);
            var zoned = new DateTimeOffset(stamp, TimeSpan.FromHours(8));
            var clock = new TimeOnly(stamp.Ticks % TimeSpan.TicksPerDay);
            var duration = TimeSpan.FromDays(oracle ? 100 : 1).Add(TimeSpan.FromTicks(1234560));
            var commands = new CommandCapture { Results = new DataTable() };
            using var results = commands.Results;
            results.Columns.Add("Id", oracle ? typeof(decimal) : typeof(int));
            results.Rows.Add(1);
            await using var context = CreateContext(mode, commands);
            var query = context.Set<NarrowTemporalRow>().Where(row => row.Stamp < stamp && row.Zoned < zoned
                && row.Clock < clock && row.Duration < duration && row.Date < stamp).Select(row => row.Id);
            var sql = query.ToQueryString();
            Assert.Contains(".123456", sql);
            Assert.Contains(".123456", context.Set<NarrowTemporalRow>().Select(row => row.Stamp < new DateTime(2026, 11, 10, 12, 0, 0).AddTicks(1234560)).ToQueryString());
            Assert.Equal(1, Assert.Single(async ? await query.ToListAsync() : query.ToList()));
            context.Add(new NarrowTemporalRow { Id = 2, Stamp = new DateTime(2026, 11, 10),
                Date = new DateTime(2026, 11, 10), Zoned = new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero) });
            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Temporal_column_precision_still_rejects_lossy_writes_before_execution(
            OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new CommandCapture();
            await using var context = CreateContext(mode, commands);
            context.Add(new NarrowTemporalRow { Id = 1, Stamp = new DateTime(2026, 11, 10).AddTicks(10) });
            var exception = async ? await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync())
                : Assert.Throws<DbUpdateException>(() => context.SaveChanges());
            Assert.IsType<ArgumentException>(exception.InnerException);
            Assert.Empty(commands.Sql);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Floating_columns_materialize_alternate_widths_and_keep_CLR_write_binding(
            OceanBaseCompatibilityMode mode, bool async)
        {
            using var table = new DataTable();
            table.Columns.Add("Id", mode == OceanBaseCompatibilityMode.Oracle ? typeof(decimal) : typeof(int));
            table.Columns.Add("Narrow", typeof(float));
            table.Columns.Add("Wide", typeof(double));
            table.Columns.Add("Converted", typeof(float));
            table.Columns.Add("Optional", typeof(double));
            table.Rows.Add(9, 1.25f, 1.25d, 2.5f, DBNull.Value);
            var commands = new CommandCapture { Results = table };
            await using var context = CreateContext(mode, commands);
            var query = context.Set<FloatingRow>().Select(row => new { row.Id, row.Narrow, row.Wide, row.Converted, row.Optional });
            var row = Assert.Single(async ? await query.ToListAsync() : query.ToList());
            Assert.Equal(1.25d, row.Narrow);
            Assert.Equal(1.25f, row.Wide);
            Assert.Equal(1.25d, row.Converted);
            Assert.Null(row.Optional);
            context.Add(new FloatingRow { Id = row.Id, Narrow = row.Narrow, Wide = row.Wide, Converted = row.Converted });
            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            Assert.Contains(1.25d, commands.Values);
            Assert.Contains(1.25f, commands.Values);
            Assert.Contains(2.5d, commands.Values);
            Assert.Contains(DBNull.Value, commands.Values);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Floating_query_operands_do_not_inherit_column_capacity(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode, new CommandCapture());
            var limit = double.MaxValue;
            var rows = context.Set<FloatingRow>();
            Assert.Contains("1.7976931348623157E+308", rows.Where(row => row.Narrow < limit).ToQueryString());
            Assert.Contains("1.7976931348623157E+308", rows.Select(row => row.Narrow + double.MaxValue).ToQueryString());
            Assert.Contains("1.7976931348623157E+308", rows.Select(row => row.Narrow == 1 ? double.MaxValue : row.Narrow).ToQueryString());
            Assert.Contains("-1", rows.Where(row => row.Narrow < -1d).ToQueryString());
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
        public async Task Narrow_floating_write_checks_converted_values_before_execution(
            OceanBaseCompatibilityMode mode, bool converted, bool async)
        {
            var commands = new CommandCapture();
            await using var context = CreateContext(mode, commands);
            context.Add(new FloatingRow { Id = 1, Narrow = converted ? 1 : double.MaxValue,
                Converted = converted ? double.MaxValue / 2 : 1 });
            var exception = async ? await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync())
                : Assert.Throws<DbUpdateException>(() => context.SaveChanges());
            Assert.IsType<ArgumentOutOfRangeException>(exception.InnerException);
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
        public async Task Generated_floating_outputs_convert_width_and_reject_finite_overflow(
            OceanBaseCompatibilityMode mode, bool overflow, bool async)
        {
            var output = overflow ? double.MaxValue : 1.25d;
            using var table = new DataTable();
            table.Columns.Add("Value", typeof(double));
            table.Rows.Add(output);
            var commands = new CommandCapture { Results = table, OutputValue = output };
            await using var context = CreateContext(mode, commands);
            var entity = new GeneratedFloating { Id = 1 };
            context.Add(entity);
            if (overflow)
            {
                var exception = async ? await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync())
                    : Assert.Throws<DbUpdateException>(() => context.SaveChanges());
                Assert.IsType<OverflowException>(exception.InnerException);
            }
            else
            {
                Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
                Assert.Equal(1.25f, entity.Value);
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Explicit_Guid_storage_pairs_query_materialization_and_write_conversion(
            OceanBaseCompatibilityMode mode, bool async)
        {
            var token = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
            using var table = new DataTable();
            table.Columns.Add("Id", mode == OceanBaseCompatibilityMode.Oracle ? typeof(decimal) : typeof(int));
            table.Columns.Add("Text", typeof(string));
            table.Columns.Add("Binary", typeof(byte[]));
            table.Columns.Add("Optional", mode == OceanBaseCompatibilityMode.Oracle ? typeof(string) : typeof(byte[]));
            table.Rows.Add(7, "00112233-4455-6677-8899-aabbccddeeff",
                new byte[] { 0x33, 0x22, 0x11, 0, 0x55, 0x44, 0x77, 0x66, 0x88, 0x99, 0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0xff }, DBNull.Value);
            var commands = new CommandCapture { Results = table };
            await using var context = CreateContext(mode, commands);
            var query = context.Set<GuidStorageRow>().Select(row => new { row.Id, row.Text, row.Binary, row.Optional });
            var row = Assert.Single(async ? await query.ToListAsync() : query.ToList());
            Assert.Equal(token, row.Text);
            Assert.Equal(token, row.Binary);
            Assert.Null(row.Optional);
            var sql = context.Set<GuidStorageRow>().Where(row => row.Text == token && row.Binary == token).ToQueryString();
            Assert.Contains("00112233-4455-6677-8899-aabbccddeeff", sql);
            Assert.Contains("33221100554477668899AABBCCDDEEFF", sql);
            context.Add(new GuidStorageRow { Id = row.Id, Text = row.Text, Binary = row.Binary });
            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            Assert.Contains("00112233-4455-6677-8899-aabbccddeeff", commands.Values);
            Assert.Equal(token.ToByteArray(), Assert.Single(commands.Values.OfType<byte[]>()));
            Assert.Contains(DBNull.Value, commands.Values);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Generated_explicit_Guid_outputs_use_the_declared_representation(
            OceanBaseCompatibilityMode mode, bool async)
        {
            var token = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
            object output = mode == OceanBaseCompatibilityMode.Oracle ? token.ToString() : token.ToByteArray();
            using var table = new DataTable();
            table.Columns.Add("Data", output.GetType());
            table.Rows.Add(output);
            var commands = new CommandCapture { Results = table, OutputValue = output };
            await using var context = CreateContext(mode, commands);
            var entity = new GeneratedExplicitGuid { Id = 1 };
            context.Add(entity);
            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            Assert.Equal(token, entity.Data);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Query_operands_and_results_are_not_limited_to_integer_column_capacity(OceanBaseCompatibilityMode mode)
        {
            var commands = new CommandCapture();
            using var context = CreateContext(mode, commands);
            var rows = context.Set<NarrowScalarRow>();
            const long literal = 1000000000L;
            var captured = literal;
            Assert.Contains("1000000000", rows.Select(row => row.Count + literal).ToQueryString());
            Assert.Contains("1000000000", rows.Where(row => row.Count < captured).ToQueryString());
            Assert.Contains("1000000000", rows.Select(row => row.Count == 1 ? literal : row.Count).ToQueryString());
            Assert.Contains("1000000000", rows.Select(row => row.Count * literal).ToQueryString());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Binary_query_comparands_can_exceed_the_column_length(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode, new CommandCapture());
            var bytes = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 };
            var sql = context.Set<BinaryRow>().Where(row => row.Payload == bytes).ToQueryString();
            Assert.Contains("000102030405060708", sql);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Explicit_integer_and_boolean_columns_keep_converters_in_query_and_write(
            OceanBaseCompatibilityMode mode, bool async)
        {
            var oracle = mode == OceanBaseCompatibilityMode.Oracle;
            using var table = new DataTable();
            table.Columns.Add("Id", oracle ? typeof(decimal) : typeof(int));
            table.Columns.Add("Count", oracle ? typeof(decimal) : typeof(int));
            table.Columns.Add("State", oracle ? typeof(decimal) : typeof(sbyte));
            table.Columns.Add("Enabled", oracle ? typeof(decimal) : typeof(sbyte));
            table.Rows.Add(1, 123, 2, 1);
            var commands = new CommandCapture { Results = table };
            await using var context = CreateContext(mode, commands);
            var query = context.Set<NarrowScalarRow>().Select(row => new { row.Id, row.Count, row.State, row.Enabled });

            var row = Assert.Single(async ? await query.ToListAsync() : query.ToList());
            Assert.Equal(123L, row.Count);
            Assert.Equal(RowState.Active, row.State);
            Assert.True(row.Enabled);
            var entityType = context.Model.FindEntityType(typeof(NarrowScalarRow))!;
            Assert.Equal(oracle ? "NUMBER(3)" : "MEDIUMINT", entityType.FindProperty(nameof(NarrowScalarRow.Count))!.GetRelationalTypeMapping().StoreType);
            Assert.Equal(oracle ? "NUMBER(3)" : "BOOLEAN", entityType.FindProperty(nameof(NarrowScalarRow.Enabled))!.GetRelationalTypeMapping().StoreType);
            context.Add(new NarrowScalarRow { Id = row.Id, Count = row.Count, State = row.State, Enabled = row.Enabled });

            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            Assert.Contains(123L, commands.Values);
            Assert.Contains(2, commands.Values);
            Assert.Contains(oracle ? (object)1m : true, commands.Values);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Narrow_integer_columns_reject_oversized_writes_before_execution(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new CommandCapture();
            await using var context = CreateContext(mode, commands);
            context.Add(new NarrowScalarRow { Id = 1, Count = mode == OceanBaseCompatibilityMode.Oracle ? 1000 : 8388608 });
            var exception = async ? await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync())
                : Assert.Throws<DbUpdateException>(() => context.SaveChanges());
            Assert.IsType<ArgumentOutOfRangeException>(exception.InnerException);
            Assert.Empty(commands.Sql);
        }

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
                .AddInterceptors(new NoNetworkConnection(), commands).Options, mode);
            // These tests exercise EF commands; transaction semantics are verified separately against OceanBase.
            context.Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
            return context;
        }

        private sealed class TestContext(DbContextOptions<TestContext> options, OceanBaseCompatibilityMode mode) : DbContext(options)
        {
            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                var oracle = mode == OceanBaseCompatibilityMode.Oracle;
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
                modelBuilder.Entity<NarrowScalarRow>().Property(entity => entity.Id).ValueGeneratedNever();
                modelBuilder.Entity<NarrowScalarRow>().Property(entity => entity.Count).HasColumnType(oracle ? "NUMBER(3)" : "MEDIUMINT");
                modelBuilder.Entity<NarrowScalarRow>().Property(entity => entity.State).HasConversion<int>()
                    .HasColumnType(oracle ? "NUMBER(2)" : "TINYINT");
                modelBuilder.Entity<NarrowScalarRow>().Property(entity => entity.Enabled).HasColumnType(oracle ? "NUMBER(3)" : "BOOLEAN");
                modelBuilder.Entity<GuidStorageRow>().Property(entity => entity.Id).ValueGeneratedNever();
                modelBuilder.Entity<GuidStorageRow>().Property(entity => entity.Text).HasColumnType(oracle ? "VARCHAR2(36 CHAR)" : "VARCHAR(36)");
                modelBuilder.Entity<GuidStorageRow>().Property(entity => entity.Binary).HasColumnType(oracle ? "RAW(16)" : "BINARY(16)");
                modelBuilder.Entity<GuidStorageRow>().Property(entity => entity.Optional).HasColumnType(oracle ? "NVARCHAR2(36)" : "VARBINARY(16)");
                modelBuilder.Entity<GeneratedExplicitGuid>().Property(entity => entity.Id).ValueGeneratedNever();
                modelBuilder.Entity<GeneratedExplicitGuid>().Property(entity => entity.Data)
                    .HasColumnType(oracle ? "VARCHAR2(36)" : "BINARY(16)").HasComputedColumnSql("NULL");
                modelBuilder.Entity<FloatingRow>().Property(entity => entity.Id).ValueGeneratedNever();
                modelBuilder.Entity<FloatingRow>().Property(entity => entity.Narrow).HasColumnType(oracle ? "BINARY_FLOAT" : "FLOAT(24) UNSIGNED");
                modelBuilder.Entity<FloatingRow>().Property(entity => entity.Wide).HasColumnType(oracle ? "BINARY_DOUBLE" : "DOUBLE PRECISION");
                modelBuilder.Entity<FloatingRow>().Property(entity => entity.Converted)
                    .HasConversion(value => value * 2d, value => value / 2d).HasColumnType(oracle ? "BINARY_FLOAT" : "FLOAT");
                modelBuilder.Entity<GeneratedFloating>().Property(entity => entity.Id).ValueGeneratedNever();
                modelBuilder.Entity<GeneratedFloating>().Property(entity => entity.Value)
                    .HasColumnType(oracle ? "BINARY_DOUBLE" : "DOUBLE").HasComputedColumnSql("NULL");
                modelBuilder.Entity<NarrowTemporalRow>().Property(entity => entity.Id).ValueGeneratedNever();
                modelBuilder.Entity<NarrowTemporalRow>().Property(entity => entity.Stamp).HasPrecision(3);
                modelBuilder.Entity<NarrowTemporalRow>().Property(entity => entity.Zoned).HasPrecision(3);
                modelBuilder.Entity<NarrowTemporalRow>().Property(entity => entity.Clock).HasPrecision(3);
                modelBuilder.Entity<NarrowTemporalRow>().Property(entity => entity.Duration)
                    .HasColumnType(oracle ? "INTERVAL DAY(1) TO SECOND(3)" : "TIME(3)");
                modelBuilder.Entity<NarrowTemporalRow>().Property(entity => entity.Date).HasColumnType(oracle ? "DATE" : "DATETIME(0)");
                modelBuilder.Entity<DecimalRow>().Property(entity => entity.Id).ValueGeneratedNever();
                modelBuilder.Entity<DecimalRow>().Property(entity => entity.Value).HasColumnType(oracle ? "NUMBER(5,2)" : "DECIMAL(5,2)");
                modelBuilder.Entity<DecimalRow>().Property(entity => entity.Converted)
                    .HasConversion(value => value * 2m, value => value / 2m).HasPrecision(5, 2);
                modelBuilder.Entity<DecimalRow>().Property(entity => entity.Optional).HasPrecision(5, 2);
            }
        }

        private enum RowState { Inactive = 0, Active = 2 }

        private sealed class DecimalRow
        {
            public int Id { get; set; }
            public decimal Value { get; set; }
            public decimal Converted { get; set; }
            public decimal? Optional { get; set; }
        }

        private sealed class NarrowTemporalRow
        {
            public int Id { get; set; }
            public DateTime Stamp { get; set; }
            public DateTime Date { get; set; }
            public DateTimeOffset Zoned { get; set; }
            public TimeOnly Clock { get; set; }
            public TimeSpan Duration { get; set; }
        }

        private sealed class FloatingRow
        {
            public int Id { get; set; }
            public double Narrow { get; set; }
            public float Wide { get; set; }
            public double Converted { get; set; }
            public double? Optional { get; set; }
        }

        private sealed class GeneratedFloating
        {
            public int Id { get; set; }
            public float Value { get; set; }
        }

        private sealed class GuidStorageRow
        {
            public int Id { get; set; }
            public Guid Text { get; set; }
            public Guid Binary { get; set; }
            public Guid? Optional { get; set; }
        }

        private sealed class GeneratedExplicitGuid
        {
            public int Id { get; set; }
            public Guid Data { get; set; }
        }

        private sealed class NarrowScalarRow
        {
            public int Id { get; set; }
            public long Count { get; set; }
            public RowState State { get; set; }
            public bool Enabled { get; set; }
        }

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
            public List<OceanBaseParameter> DecimalInputs { get; } = [];
            public int AffectedRows { get; init; } = 1;
            public DataTable? Results { get; init; }
            public object OutputValue { get; init; } = 42m;

            public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
            {
                Sql.Add(command.CommandText);
                DecimalInputs.AddRange(command.Parameters.Cast<OceanBaseParameter>()
                    .Where(parameter => parameter.DbType == DbType.Decimal && parameter.Direction == ParameterDirection.Input));
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
                DecimalInputs.AddRange(command.Parameters.Cast<OceanBaseParameter>()
                    .Where(parameter => parameter.DbType == DbType.Decimal && parameter.Direction == ParameterDirection.Input));
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
