using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.FunctionalTests
{
    public sealed class NumericCollectionRootTests
    {
        public static IEnumerable<object[]> Modes()
        {
            foreach (var mode in new[] { OceanBaseCompatibilityMode.MySql, OceanBaseCompatibilityMode.Oracle })
            foreach (var async in new[] { false, true })
            foreach (var relationalNulls in new[] { false, true })
            foreach (var translation in new[] { ParameterTranslationMode.Constant, ParameterTranslationMode.MultipleParameters })
                yield return new object[] { mode, async, relationalNulls, translation };
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Numeric_roots_bind_exact_values_and_materialize_nullable_native_results(
            OceanBaseCompatibilityMode mode, bool async, bool relationalNulls, ParameterTranslationMode translation)
        {
            await Check(decimal.MinValue, decimal.MaxValue, 0.0000000000000000000000000001m, DbType.Decimal, "DECIMAL(65,30)", "NUMBER");
            await Check(float.MinValue, float.MaxValue, float.Epsilon, DbType.Single, "FLOAT", "BINARY_FLOAT");
            await Check(double.MinValue, double.MaxValue, double.Epsilon, DbType.Double, "DOUBLE", "BINARY_DOUBLE");

            async Task Check<T>(T minimum, T maximum, T tiny, DbType dbType, string mysql, string oracle) where T : struct
            {
                using var table = new DataTable();
                // Decimal mappings retain GetDecimal; floating mappings own native GetValue conversion.
                table.Columns.Add("Value", typeof(T) == typeof(decimal) ? typeof(decimal) : typeof(object));
                var capture = new Capture(table);
                await using var context = Create<T>(mode, capture, relationalNulls, translation);
                T?[] values = [];
                var query = context.Rows.SelectMany(row => values.Where(value => row.Id > 0));
                foreach (var input in new T?[][] { [], [minimum, maximum, tiny, minimum, null], [null, null], [tiny], [] })
                {
                    values = input;
                    capture.Inputs.Clear();
                    capture.Sql.Clear();
                    table.Rows.Clear();
                    foreach (var value in input)
                        table.Rows.Add(value is null ? DBNull.Value : value.Value);
                    // Reader rows establish shaping independently of UNION execution on a server.
                    Assert.Equal(input, async ? await query.ToListAsync() : query.ToList());
                    Assert.Equal(input, values);
                    var sql = Assert.Single(capture.Sql);
                    Assert.Contains(" AS " + (mode == OceanBaseCompatibilityMode.MySql ? mysql : oracle) + ")", sql);
                    Assert.Equal(Math.Max(0, input.Length - 1), sql.Split("UNION ALL SELECT").Length - 1);
                    Assert.Equal(input.Length == 0, sql.Contains("WHERE 1 = 0", StringComparison.Ordinal));
                    var nonnull = capture.Inputs.Where(parameter => parameter.Value is not DBNull).ToArray();
                    if (translation == ParameterTranslationMode.Constant) Assert.Empty(capture.Inputs);
                    else
                    {
                        Assert.Equal(input.Where(value => value.HasValue).Select(value => (object)value!.Value), nonnull.Select(parameter => parameter.Value));
                        Assert.All(capture.Inputs, parameter => Assert.Equal(dbType, parameter.DbType));
                        Assert.All(nonnull, parameter => Assert.IsType<T>(parameter.Value));
                        Assert.All(capture.Inputs, parameter => { Assert.Equal(0, parameter.Precision); Assert.Equal(0, parameter.Scale); });
                    }
                }
                if (typeof(T) != typeof(decimal))
                {
                    values = [tiny];
                    table.Rows.Clear();
                    table.Rows.Add(new OceanBaseDecimal(17.25m));
                    Assert.Equal((T)Convert.ChangeType(17.25m, typeof(T)), Assert.Single(async ? await query.ToListAsync() : query.ToList()));
                    table.Rows.Clear();
                    table.Rows.Add(new OceanBaseDecimal(BigInteger.One, -400));
                    if (async) await Assert.ThrowsAsync<OverflowException>(() => query.ToListAsync());
                    else Assert.Throws<OverflowException>(() => query.ToList());
                }
            }
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Contextual_numeric_converters_keep_provider_bindings_readers_and_empty_bodies(
            OceanBaseCompatibilityMode mode, bool async, bool relationalNulls, ParameterTranslationMode translation)
        {
            using var table = new DataTable();
            table.Columns.Add("Value", typeof(double));
            table.Rows.Add(17.25d);
            table.Rows.Add(DBNull.Value);
            var capture = new Capture(table);
            await using var context = Create<decimal>(mode, capture, relationalNulls, translation);
            decimal?[] values = [null, 0m, 17.25m, 17.25m, decimal.MaxValue];
            var query = context.Set<ConvertedRow>().Where(row => values.Where(value => value > row.Value)
                .Contains(row.Value)).Select(row => row.Value);
            foreach (var input in new decimal?[][] { values, [], [null, null], [17.25m], [] })
            {
                values = input;
                capture.Inputs.Clear();
                capture.Sql.Clear();
                Assert.Equal(new decimal?[] { 17.25m, null }, async ? await query.ToListAsync() : query.ToList());
                var sql = Assert.Single(capture.Sql);
                Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? " AS DOUBLE)" : " AS BINARY_DOUBLE)", sql);
                Assert.Equal(input.Length == 0, sql.Contains("WHERE 1 = 0", StringComparison.Ordinal));
                var expected = translation == ParameterTranslationMode.Constant ? []
                    : input.Where(value => value.HasValue).Select(value => (double)value!.Value).ToArray();
                Assert.Equal(expected, capture.Inputs.Where(parameter => parameter.Value is not DBNull)
                    .Select(parameter => Assert.IsType<double>(parameter.Value)));
                Assert.All(capture.Inputs, parameter => Assert.Equal(DbType.Double, parameter.DbType));
            }

            using var decimalTable = new DataTable();
            decimalTable.Columns.Add("Value", typeof(decimal));
            decimalTable.Rows.Add(17m);
            var decimalCapture = new Capture(decimalTable);
            await using var integerContext = Create<int>(mode, decimalCapture, relationalNulls, translation);
            int?[] integers = [null, 17, int.MaxValue, 17];
            var integerQuery = integerContext.Set<IntegerConvertedRow>().Where(row => integers.Where(value => value > row.Value)
                .Contains(row.Value)).Select(row => row.Value);
            Assert.Equal(17, Assert.Single(async ? await integerQuery.ToListAsync() : integerQuery.ToList()));
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? " AS DECIMAL(65,30))" : " AS NUMBER)", Assert.Single(decimalCapture.Sql));
            Assert.All(decimalCapture.Inputs, parameter => { Assert.Equal(DbType.Decimal, parameter.DbType); Assert.Equal(0, parameter.Precision); Assert.Equal(0, parameter.Scale); });
            if (translation == ParameterTranslationMode.MultipleParameters)
                Assert.Equal(new decimal[] { 17m, int.MaxValue, 17m }, decimalCapture.Inputs.Where(parameter => parameter.Value is not DBNull)
                    .Select(parameter => Assert.IsType<decimal>(parameter.Value)));

            using var singleTable = new DataTable();
            singleTable.Columns.Add("Value", typeof(object));
            singleTable.Rows.Add(new OceanBaseDecimal(17.25m));
            var singleCapture = new Capture(singleTable);
            await using var singleContext = Create<double>(mode, singleCapture, relationalNulls, translation);
            double?[] doubles = [null, 0d, 17.25d, float.MaxValue, 17.25d];
            var singleQuery = singleContext.Set<SingleConvertedRow>().Where(row => doubles.Where(value => value > row.Value)
                .Contains(row.Value)).Select(row => row.Value);
            Assert.Equal(17.25d, Assert.Single(async ? await singleQuery.ToListAsync() : singleQuery.ToList()));
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? " AS FLOAT)" : " AS BINARY_FLOAT)", Assert.Single(singleCapture.Sql));
            Assert.All(singleCapture.Inputs, parameter => Assert.Equal(DbType.Single, parameter.DbType));
            if (translation == ParameterTranslationMode.MultipleParameters)
                Assert.Equal(new float[] { 0f, 17.25f, float.MaxValue, 17.25f }, singleCapture.Inputs.Where(parameter => parameter.Value is not DBNull)
                    .Select(parameter => Assert.IsType<float>(parameter.Value)));
            singleCapture.Sql.Clear();
            doubles = [double.MaxValue];
            if (async) await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => singleQuery.ToListAsync());
            else Assert.Throws<ArgumentOutOfRangeException>(() => singleQuery.ToList());
            Assert.Empty(singleCapture.Sql);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Nonfinite_numeric_parameters_reject_before_command_execution(OceanBaseCompatibilityMode mode, bool async)
        {
            using var table = new DataTable();
            table.Columns.Add("Value", typeof(double));
            var capture = new Capture(table);
            await using var context = Create<double>(mode, capture, false, ParameterTranslationMode.MultipleParameters);
            double[] values = [];
            var query = context.Rows.SelectMany(row => values.Where(value => row.Id > 0));
            foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                values = [1d, invalid];
                if (async) await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => query.ToListAsync());
                else Assert.Throws<ArgumentOutOfRangeException>(() => query.ToList());
                Assert.Empty(capture.Sql);
            }
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Projected_contextual_elements_use_their_own_readers_converters_and_current_bindings(
            OceanBaseCompatibilityMode mode, bool async, bool relationalNulls, ParameterTranslationMode translation)
        {
            using var table = new DataTable();
            table.Columns.Add("Value", typeof(object));
            var capture = new Capture(table);
            await using var context = Create<decimal>(mode, capture, relationalNulls, translation);
            decimal?[] values = [];
            var many = context.Set<ConvertedRow>().SelectMany(row => values.Where(value => value > row.Other));
            var scalar = context.Set<ConvertedRow>().Select(row => values.Where(value => value > row.Other).FirstOrDefault());
            foreach (var input in new decimal?[][] { [], [17.25m, null, 17.25m], [null, null], [18.5m], [] })
            {
                values = input;
                foreach (var item in new (IQueryable<decimal?> Query, bool Scalar)[]
                {
                    (many, false), (scalar, true),
                    (context.Set<ConvertedRow>().Join(values, row => row.Other, value => value, (row, value) => value), false)
                })
                {
                    capture.Inputs.Clear();
                    capture.Sql.Clear();
                    table.Rows.Clear();
                    var expected = item.Scalar ? new decimal?[] { input.FirstOrDefault() } : input;
                    foreach (var value in expected) table.Rows.Add(value.HasValue ? new OceanBaseDecimal(value.Value + 100m) : DBNull.Value);
                    Assert.Equal(expected, async ? await item.Query.ToListAsync() : item.Query.ToList());
                    var sql = Assert.Single(capture.Sql);
                    Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? " AS DOUBLE)" : " AS BINARY_DOUBLE)", sql);
                    Assert.Equal(input.Length == 0, sql.Contains("WHERE 1 = 0", StringComparison.Ordinal));
                    Assert.Equal(Math.Max(0, input.Length - 1), sql.Split("UNION ALL SELECT").Length - 1);
                    var expectedParameters = translation == ParameterTranslationMode.Constant ? []
                        : input.Where(value => value.HasValue).Select(value => (double)value!.Value + 100d).ToArray();
                    Assert.Equal(expectedParameters, capture.Inputs.Where(parameter => parameter.Value is not DBNull)
                        .Select(parameter => Assert.IsType<double>(parameter.Value)));
                    Assert.All(capture.Inputs, parameter => Assert.Equal(DbType.Double, parameter.DbType));
                }
            }

            values = [17.25m, null, 17.25m];
            using var pairTable = new DataTable();
            pairTable.Columns.Add("First", typeof(object));
            pairTable.Columns.Add("Second", typeof(object));
            pairTable.Rows.Add(new OceanBaseDecimal(17.25m), new OceanBaseDecimal(117.25m));
            var pairCapture = new Capture(pairTable);
            await using var pairContext = Create<decimal>(mode, pairCapture, relationalNulls, translation);
            var pairQuery = pairContext.Set<ConvertedRow>().Select(row => new
            {
                First = values.Where(value => value > row.Value).FirstOrDefault(),
                Second = values.Where(value => value > row.Other).FirstOrDefault()
            });
            var pair = Assert.Single(async ? await pairQuery.ToListAsync() : pairQuery.ToList());
            Assert.Equal(17.25m, pair.First);
            Assert.Equal(17.25m, pair.Second);
            if (translation == ParameterTranslationMode.MultipleParameters)
                Assert.Equal(new[] { 17.25d, 17.25d, 117.25d, 117.25d }, pairCapture.Inputs
                    .Where(parameter => parameter.Value is not DBNull).Select(parameter => Assert.IsType<double>(parameter.Value)));

            table.Rows.Clear();
            table.Rows.Add(117.25m);
            table.Rows.Add(DBNull.Value);
            table.Rows.Add(117.25m);
            foreach (var sameStore in new[]
            {
                context.Set<SameStoreRow>().SelectMany(row => values.Where(value => value > row.Value)),
                context.Set<SameStoreRow>().Join(new decimal?[] { 17.25m, null, 17.25m }, row => row.Value, value => value, (row, value) => value)
            })
            {
                capture.Inputs.Clear();
                capture.Sql.Clear();
                Assert.Equal(values, async ? await sameStore.ToListAsync() : sameStore.ToList());
                Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? " AS DECIMAL(65,30))" : " AS NUMBER)", Assert.Single(capture.Sql));
                Assert.All(capture.Inputs, parameter => Assert.Equal(DbType.Decimal, parameter.DbType));
                Assert.All(capture.Inputs.Where(parameter => parameter.Value is not DBNull),
                    parameter => Assert.Equal(117.25m, Assert.IsType<decimal>(parameter.Value)));
            }
        }

        private static TestContext<T> Create<T>(OceanBaseCompatibilityMode mode, Capture capture,
            bool relationalNulls, ParameterTranslationMode translation) where T : struct
        {
            var builder = new DbContextOptionsBuilder<TestContext<T>>()
                .UseOceanBase("Server=offline;Database=numeric_roots", mode).EnableServiceProviderCaching(false)
                .AddInterceptors(new NoNetwork(), capture);
            ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(
                builder.Options.Extensions.OfType<RelationalOptionsExtension>().Single()
                    .WithUseRelationalNulls(relationalNulls).WithUseParameterizedCollectionMode(translation));
            return new TestContext<T>(builder.Options, mode);
        }

        private sealed class TestContext<T>(DbContextOptions<TestContext<T>> options, OceanBaseCompatibilityMode mode)
            : DbContext(options) where T : struct
        {
            public DbSet<Row<T>> Rows => Set<Row<T>>();
            protected override void OnModelCreating(ModelBuilder builder)
            {
                builder.Entity<ConvertedRow>().Property(row => row.Value).HasConversion(
                    new ValueConverter<decimal?, double?>(value => value.HasValue ? (double?)value.Value : null,
                        value => value.HasValue ? (decimal?)value.Value : null));
                builder.Entity<ConvertedRow>().Property(row => row.Other).HasConversion(
                    new ValueConverter<decimal?, double?>(value => value.HasValue ? (double?)value.Value + 100d : null,
                        value => value.HasValue ? (decimal?)(value.Value - 100d) : null));
                builder.Entity<SameStoreRow>().Property(row => row.Value).HasConversion(
                    new ValueConverter<decimal?, decimal?>(value => value + 100m, value => value - 100m));
                builder.Entity<IntegerConvertedRow>().Property(row => row.Value).HasConversion(
                    new ValueConverter<int?, decimal?>(value => value.HasValue ? (decimal?)value.Value : null,
                        value => value.HasValue ? (int?)checked((int)value.Value) : null))
                    .HasColumnType(mode == OceanBaseCompatibilityMode.MySql ? "DECIMAL(5,2)" : "NUMBER(5,2)");
                builder.Entity<SingleConvertedRow>().Property(row => row.Value).HasConversion(
                    new ValueConverter<double?, float?>(value => value.HasValue ? (float?)value.Value : null,
                        value => value.HasValue ? (double?)value.Value : null))
                    .HasColumnType(mode == OceanBaseCompatibilityMode.MySql ? "DOUBLE" : "BINARY_DOUBLE");
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
            public decimal? Value { get; set; }
            public decimal? Other { get; set; }
        }

        private sealed class IntegerConvertedRow
        {
            public int Id { get; set; }
            public int? Value { get; set; }
        }

        private sealed class SameStoreRow
        {
            public int Id { get; set; }
            public decimal? Value { get; set; }
        }

        private sealed class SingleConvertedRow
        {
            public int Id { get; set; }
            public double? Value { get; set; }
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
