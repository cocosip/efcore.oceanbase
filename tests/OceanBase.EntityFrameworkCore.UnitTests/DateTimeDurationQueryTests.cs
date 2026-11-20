using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OceanBase.EntityFrameworkCore.Query;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests
{
    public sealed class DateTimeDurationQueryTests
    {
        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Duration_arithmetic_translates_predicate_projection_and_composition(OceanBaseCompatibilityMode mode, bool subtract)
        {
            using var context = Create(mode);
            var duration = new TimeSpan(1, 2, 3, 4).Add(TimeSpan.FromTicks(1234560));
            var query = subtract
                ? context.Rows.Where(row => row.Stamp.Subtract(duration) > new DateTime(2000, 1, 1))
                    .OrderBy(row => row.OptionalStamp!.Value.Subtract(row.Duration))
                    .Select(row => (DateTime?)row.OptionalStamp!.Value.Subtract(duration))
                : context.Rows.Where(row => row.Stamp.Add(duration) > new DateTime(2000, 1, 1))
                    .OrderBy(row => row.OptionalStamp!.Value.Add(row.Duration))
                    .Select(row => (DateTime?)row.OptionalStamp!.Value.Add(duration));
            var sql = query.ToQueryString();
            Assert.DoesNotContain("__oceanbase_internal", sql);
            Assert.Contains("OptionalStamp", sql);
            Assert.Contains("Duration", sql);
            if (mode == OceanBaseCompatibilityMode.MySql)
            {
                Assert.Contains("TIMESTAMPADD(MICROSECOND, ", sql);
                Assert.Contains("TIME_TO_SEC(", sql);
                Assert.Contains(" * 1000000", sql);
                Assert.Contains(" * ABS(MICROSECOND(", sql);
                Assert.Contains(" AS CHAR) LIKE '-%' THEN -1 ELSE 1 END)", sql);
                if (subtract) Assert.Contains("-(TIME_TO_SEC(", sql);
            }
            else
            {
                Assert.Contains(" AS TIMESTAMP(7))", sql);
                Assert.Contains(subtract ? " - " : " + ", sql);
            }
        }

        [Fact]
        public void Oracle_DATE_arithmetic_promotes_result_mapping_before_fractional_composition()
        {
            using var context = Create(OceanBaseCompatibilityMode.Oracle);
            var source = context.GetService<IRelationalTypeMappingSource>();
            var methods = context.GetService<IMethodCallTranslatorProvider>();
            var factory = context.GetService<ISqlExpressionFactory>();
            var logger = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>();
            var mapping = source.FindMapping(typeof(DateTime), "DATE")!;
            var input = new ColumnExpression("DateStamp", "r", typeof(DateTime), mapping, true);
            foreach (var name in new[] { nameof(DateTime.Add), nameof(DateTime.Subtract) })
            {
                var result = Assert.IsType<SqlFunctionExpression>(methods.Translate(context.Model, input,
                    typeof(DateTime).GetMethod(name, [typeof(TimeSpan)])!,
                    [factory.Constant(TimeSpan.FromTicks(1), source.FindMapping(typeof(TimeSpan)))], logger));
                Assert.Equal("TIMESTAMP(7)", result.TypeMapping!.StoreType);
            }
            var sql = context.Rows.Where(row => row.DateStamp.Add(TimeSpan.FromTicks(1)).Nanosecond == 100)
                .Select(row => row.DateStamp.Subtract(TimeSpan.FromTicks(1))).ToQueryString();
            Assert.DoesNotContain(" AS DATE)", sql);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Duration_arithmetic_rejects_incompatible_carriers(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var source = context.GetService<IRelationalTypeMappingSource>();
            var factory = context.GetService<ISqlExpressionFactory>();
            var methods = context.GetService<IMethodCallTranslatorProvider>();
            var logger = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>();
            var date = source.FindMapping(typeof(DateTime))!;
            var duration = source.FindMapping(typeof(TimeSpan))!;
            var text = (RelationalTypeMapping)source.FindMapping(typeof(string))!.WithComposedConverter(
                new ValueConverter<TimeSpan, string>(value => value.ToString(), value => TimeSpan.Parse(value)));
            var nullDate = (RelationalTypeMapping)date.WithComposedConverter(
                new ValueConverter<DateTime, DateTime>(value => value, value => value, convertsNulls: true));
            var nullDuration = mode == OceanBaseCompatibilityMode.MySql
                ? (RelationalTypeMapping)duration.WithComposedConverter(
                    new ValueConverter<TimeSpan, TimeSpan>(value => value, value => value, convertsNulls: true))
                : (RelationalTypeMapping)source.FindMapping(typeof(OceanBaseIntervalDayToSecond))!.WithComposedConverter(
                    new ValueConverter<TimeSpan, OceanBaseIntervalDayToSecond>(value => default, value => TimeSpan.Zero, convertsNulls: true));
            Assert.True(nullDuration.Converter!.ConvertsNulls);
            var narrow = source.FindMapping(typeof(DateTime), mode == OceanBaseCompatibilityMode.MySql ? "DATETIME(3)" : "TIMESTAMP(3)")!;
            foreach (var name in new[] { nameof(DateTime.Add), nameof(DateTime.Subtract) })
            {
                var method = typeof(DateTime).GetMethod(name, [typeof(TimeSpan)])!;
                var input = new ColumnExpression("Stamp", "r", typeof(DateTime), date, true);
                Assert.NotNull(methods.Translate(context.Model, input, method, [factory.Constant(TimeSpan.Zero, duration)], logger));
                Assert.Null(methods.Translate(context.Model, input, method, [factory.Constant(TimeSpan.Zero, text)], logger));
                Assert.Null(methods.Translate(context.Model, input, method, [factory.Constant(TimeSpan.Zero, nullDuration)], logger));
                Assert.Null(methods.Translate(context.Model, new ColumnExpression("Stamp", "r", typeof(DateTime), nullDate, true),
                    method, [factory.Constant(TimeSpan.Zero, duration)], logger));
                var promoted = Assert.IsType<SqlFunctionExpression>(methods.Translate(context.Model,
                    new ColumnExpression("Stamp", "r", typeof(DateTime), narrow, true), method,
                    [factory.Constant(TimeSpan.Zero, duration)], logger));
                Assert.Equal(mode == OceanBaseCompatibilityMode.MySql ? "DATETIME(6)" : "TIMESTAMP(7)", promoted.TypeMapping!.StoreType);
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Duration_renderer_rejects_malformed_markers(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var factory = context.GetService<ISqlExpressionFactory>();
            var source = context.GetService<IRelationalTypeMappingSource>();
            var mapping = source.FindMapping(typeof(DateTime))!;
            var stamp = new ColumnExpression("Stamp", "r", typeof(DateTime), mapping, true);
            var duration = factory.Constant(TimeSpan.Zero, source.FindMapping(typeof(TimeSpan)));
            foreach (var marker in new[] { OceanBaseTemporalMethodTranslator.DateTimeAddDurationMarker,
                OceanBaseTemporalMethodTranslator.DateTimeSubtractDurationMarker })
            {
                SqlExpression[] invalid =
                [
                    factory.Function(marker, [stamp], true, [false], typeof(DateTime), mapping),
                    factory.Function(marker, [stamp, duration], false, [false, false], typeof(DateTime), mapping),
                    factory.Function(marker, [stamp, duration], true, [true, true], typeof(DateTime), mapping),
                    factory.Function(marker, [stamp, factory.Constant(1)], true, [false, false], typeof(DateTime), mapping),
                    factory.Function(marker, [stamp, duration], true, [false, false], typeof(int), source.FindMapping(typeof(int)))
                ];
                foreach (var expression in invalid)
                {
                    var generator = new OceanBaseQuerySqlGenerator(context.GetService<QuerySqlGeneratorDependencies>(), mode);
                    Assert.Contains("Invalid internal DateTime duration-add", Assert.Throws<InvalidOperationException>(
                        () => generator.GetCommand(expression)).Message);
                }
            }
        }

        private static Context Create(OceanBaseCompatibilityMode mode)
            => new(new DbContextOptionsBuilder<Context>().UseOceanBase("Server=offline;Database=duration_query", mode).Options, mode);

        private sealed class Context(DbContextOptions<Context> options, OceanBaseCompatibilityMode mode) : DbContext(options)
        {
            public DbSet<Row> Rows => Set<Row>();
            protected override void OnModelCreating(ModelBuilder model)
            {
                if (mode == OceanBaseCompatibilityMode.Oracle) model.Entity<Row>().Property(row => row.DateStamp).HasColumnType("DATE");
            }
        }

        private sealed class Row
        {
            public int Id { get; set; }
            public DateTime Stamp { get; set; }
            public DateTime DateStamp { get; set; }
            public DateTime? OptionalStamp { get; set; }
            public TimeSpan Duration { get; set; }
        }
    }
}
