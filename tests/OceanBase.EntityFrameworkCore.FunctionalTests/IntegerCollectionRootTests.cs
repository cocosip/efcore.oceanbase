using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.FunctionalTests
{
    public sealed class IntegerCollectionRootTests
    {
        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, true)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, true)]
        public async Task Native_integer_roots_bind_exact_current_carriers_and_read_checked_nullable_values(
            OceanBaseCompatibilityMode mode, bool async, bool relationalNulls)
        {
            await Check<sbyte>(sbyte.MinValue, sbyte.MaxValue, DbType.SByte);
            await Check<byte>(byte.MinValue, byte.MaxValue, DbType.Byte);
            await Check<short>(short.MinValue, short.MaxValue, DbType.Int16);
            await Check<ushort>(ushort.MinValue, ushort.MaxValue, DbType.UInt16);
            await Check<int>(int.MinValue, int.MaxValue, DbType.Int32);
            await Check<uint>(uint.MinValue, uint.MaxValue, DbType.UInt32);
            await Check<long>(long.MinValue, long.MaxValue, DbType.Int64);
            await Check<ulong>(ulong.MinValue, ulong.MaxValue, DbType.UInt64);

            async Task Check<T>(T minimum, T maximum, DbType dbType) where T : struct
            {
                using var table = new DataTable();
                table.Columns.Add("Value", typeof(object));
                var capture = new Capture(table);
                await using var context = Create<T>(mode, capture, relationalNulls);
                T?[] values = [];
                var query = context.Rows.SelectMany(row => values.Where(value => row.Id > 0));
                foreach (var input in new T?[][] { [], [minimum, maximum, minimum, null], [null, null], [maximum], [] })
                {
                    values = input;
                    capture.Inputs.Clear();
                    capture.Sql.Clear();
                    table.Rows.Clear();
                    if (input.Length != 0)
                    {
                        table.Rows.Add(new OceanBaseDecimal(Convert.ToDecimal(minimum)));
                        table.Rows.Add(new OceanBaseDecimal(Convert.ToDecimal(maximum)));
                        table.Rows.Add(DBNull.Value);
                    }
                    // Supplied native rows verify shaping; UNION cardinality/results require live execution.
                    var rows = async ? await query.ToListAsync() : query.ToList();
                    Assert.Equal(input.Length == 0 ? [] : new T?[] { minimum, maximum, null }, rows);
                    Assert.Equal(input, values);
                    var sql = Assert.Single(capture.Sql);
                    Assert.Equal(input.Length == 0, sql.Contains("WHERE 1 = 0", StringComparison.Ordinal));
                    Assert.Equal(Math.Max(0, input.Length - 1), sql.Split("UNION ALL SELECT").Length - 1);
                    var actual = capture.Inputs.Where(parameter => parameter.Value is not DBNull).ToArray();
                    Assert.Equal(input.Where(value => value.HasValue).Select(value => (object)value!.Value), actual.Select(parameter => parameter.Value));
                    Assert.All(capture.Inputs, parameter => Assert.Equal(dbType, parameter.DbType));
                    Assert.All(actual, parameter => Assert.IsType<T>(parameter.Value));
                    if (input.Length == 0) Assert.Empty(capture.Inputs);
                }
                values = [maximum];
                table.Rows.Clear();
                table.Rows.Add(new OceanBaseDecimal(Convert.ToDecimal(maximum) + 1m));
                if (async) await Assert.ThrowsAsync<OverflowException>(() => query.ToListAsync());
                else Assert.Throws<OverflowException>(() => query.ToList());
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Converted_integer_root_uses_unsigned_provider_cast_binds_and_reader_converter(
            OceanBaseCompatibilityMode mode, bool async)
        {
            using var table = new DataTable();
            table.Columns.Add("Value", typeof(object));
            table.Rows.Add(new OceanBaseDecimal(9223372036854775825m));
            var capture = new Capture(table);
            await using var context = Create<int>(mode, capture);
            int[] values = [0, 17, int.MaxValue, 17];
            var query = context.Set<ConvertedRow>().Where(row => values.Where(value => value > row.Value)
                .Contains(row.Value)).Select(row => row.Value);
            Assert.Equal(17, Assert.Single(async ? await query.ToListAsync() : query.ToList()));
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? " AS UNSIGNED)" : " AS NUMBER)", Assert.Single(capture.Sql));
            Assert.Equal(new ulong[] { 9223372036854775808, 9223372036854775825, 9223372039002259455, 9223372036854775825 },
                capture.Inputs.Select(parameter => Assert.IsType<ulong>(parameter.Value)));
            Assert.All(capture.Inputs, parameter => Assert.Equal(DbType.UInt64, parameter.DbType));

            values = [];
            capture.Inputs.Clear();
            capture.Sql.Clear();
            _ = async ? await query.ToListAsync() : query.ToList();
            Assert.Empty(capture.Inputs);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "CAST(NULL AS UNSIGNED)" : "CAST(NULL AS NUMBER)", Assert.Single(capture.Sql));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Unsigned_enum_roots_keep_native_uint64_parameters_and_enum_materialization(
            OceanBaseCompatibilityMode mode, bool async)
        {
            using var table = new DataTable();
            table.Columns.Add("Value", typeof(object));
            table.Rows.Add(new OceanBaseDecimal(18446744073709551615m));
            var capture = new Capture(table);
            await using var context = Create<UnsignedState>(mode, capture);
            UnsignedState[] values = [UnsignedState.Zero, UnsignedState.Maximum, UnsignedState.Maximum];
            var query = context.Rows.Where(row => values.Where(value => value != row.Value).Contains(row.Value))
                .Select(row => row.Value);
            Assert.Equal(UnsignedState.Maximum, Assert.Single(async ? await query.ToListAsync() : query.ToList()));
            Assert.Equal(new ulong[] { 0, ulong.MaxValue, ulong.MaxValue },
                capture.Inputs.Select(parameter => Assert.IsType<ulong>(parameter.Value)));
            Assert.All(capture.Inputs, parameter => Assert.Equal(DbType.UInt64, parameter.DbType));
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? " AS UNSIGNED)" : " AS NUMBER)", Assert.Single(capture.Sql));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, true)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, true)]
        public async Task Nullable_converter_roots_bind_unsigned_values_and_preserve_null_materialization(
            OceanBaseCompatibilityMode mode, bool async, bool relationalNulls)
        {
            using var table = new DataTable();
            table.Columns.Add("Value", typeof(object));
            table.Rows.Add(new OceanBaseDecimal(9223372036854775825m));
            table.Rows.Add(DBNull.Value);
            var capture = new Capture(table);
            await using var context = Create<int>(mode, capture, relationalNulls);
            int?[] values = [null, 0, 17, 17];
            var query = context.Set<NullableConvertedRow>().Where(row => values.Where(value => value > row.Value)
                .Contains(row.Value)).Select(row => row.Value);
            foreach (var input in new int?[][] { values, [], [17, null], [null, null], [] })
            {
                values = input;
                capture.Inputs.Clear();
                capture.Sql.Clear();
                Assert.Equal(new int?[] { 17, null }, async ? await query.ToListAsync() : query.ToList());
                var nonnull = capture.Inputs.Where(parameter => parameter.Value is not DBNull).ToArray();
                var expected = input.Length == 4 ? new ulong[] { 9223372036854775808, 9223372036854775825, 9223372036854775825 }
                    : input.Length == 2 && input[0] == 17 ? new ulong[] { 9223372036854775825 } : [];
                Assert.Equal(expected, nonnull.Select(parameter => Assert.IsType<ulong>(parameter.Value)));
                Assert.All(capture.Inputs, parameter => Assert.Equal(DbType.UInt64, parameter.DbType));
                Assert.Equal(input.Length == 0, Assert.Single(capture.Sql).Contains("WHERE 1 = 0", StringComparison.Ordinal));
                if (input.Length == 0) Assert.Empty(capture.Inputs);
            }
        }

        private enum UnsignedState : ulong { Zero = 0, Maximum = ulong.MaxValue }

        private static TestContext<T> Create<T>(OceanBaseCompatibilityMode mode, Capture capture,
            bool relationalNulls = false) where T : struct
        {
            var builder = new DbContextOptionsBuilder<TestContext<T>>()
                .UseOceanBase("Server=offline;Database=integer_roots", mode).EnableServiceProviderCaching(false)
                .AddInterceptors(new NoNetwork(), capture);
            ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(
                builder.Options.Extensions.OfType<RelationalOptionsExtension>().Single().WithUseRelationalNulls(relationalNulls));
            return new TestContext<T>(builder.Options);
        }

        private sealed class TestContext<T>(DbContextOptions<TestContext<T>> options) : DbContext(options) where T : struct
        {
            public DbSet<Row<T>> Rows => Set<Row<T>>();
            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity<ConvertedRow>().Property(row => row.Value).HasConversion(
                    value => (ulong)value + 9223372036854775808UL,
                    value => checked((int)(value - 9223372036854775808UL)));
                modelBuilder.Entity<NullableConvertedRow>().Property(row => row.Value).HasConversion(
                    new ValueConverter<int?, ulong?>(
                        value => value.HasValue ? (ulong?)((ulong)value.Value + 9223372036854775808UL) : null,
                        value => value.HasValue ? (int?)checked((int)(value.Value - 9223372036854775808UL)) : null));
            }
        }

        private sealed class Row<T> where T : struct
        {
            public int Id { get; set; }
            public T Value { get; set; }
        }

        private sealed class ConvertedRow
        {
            public int Id { get; set; }
            public int Value { get; set; }
        }

        private sealed class NullableConvertedRow
        {
            public int Id { get; set; }
            public int? Value { get; set; }
        }

        private sealed class NoNetwork : DbConnectionInterceptor
        {
            public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
                => InterceptionResult.Suppress();
            public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
                InterceptionResult result, CancellationToken cancellationToken = default) => ValueTask.FromResult(InterceptionResult.Suppress());
        }

        private sealed class Capture(DataTable table) : DbCommandInterceptor
        {
            public List<string> Sql { get; } = [];
            public List<OceanBaseParameter> Inputs { get; } = [];
            public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData,
                InterceptionResult<DbDataReader> result)
            {
                Sql.Add(command.CommandText);
                Inputs.AddRange(command.Parameters.Cast<OceanBaseParameter>());
                return InterceptionResult<DbDataReader>.SuppressWithResult(table.CreateDataReader());
            }
            public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
                InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(ReaderExecuting(command, eventData, result));
        }
    }
}
