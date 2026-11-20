using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBase.EntityFrameworkCore.Query;
using OceanBase.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests
{
    public sealed class TemporalConversionQueryTests
    {
        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Date_and_clock_extraction_translate_in_predicates_and_composition(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var sql = context.Rows.Where(row => DateOnly.FromDateTime(row.Stamp).Year > 2000
                    && TimeOnly.FromDateTime(row.OptionalStamp!.Value).Ticks > 0)
                .Select(row => new
                {
                    Day = DateOnly.FromDateTime(row.Stamp).AddDays(1),
                    Clock = TimeOnly.FromDateTime(row.Stamp).AddMinutes(1),
                    Duration = TimeOnly.FromDateTime(row.Stamp).ToTimeSpan()
                }).ToQueryString();
            Assert.DoesNotContain("__oceanbase_internal", sql);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "DATE(" : "TRUNC(", sql);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "TIME(" : "TIMESTAMP", sql);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Duration_to_clock_keeps_full_scalar_range_and_does_not_wrap(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode, keepTemporalConversions: true);
            var amount = TimeSpan.MaxValue;
            var query = context.Rows.Where(row => TimeOnly.FromTimeSpan(amount).Ticks > 0
                    && TimeOnly.FromTimeSpan(row.Duration).Hour > 0)
                .Select(row => TimeOnly.FromTimeSpan(row.Stamp.Subtract(row.OtherStamp))).OrderBy(value => value);
            var initial = query.ToQueryString();
            Assert.Contains("9223372036854775807", initial);
            Assert.Contains("864000000000", initial);
            Assert.DoesNotContain("__oceanbase_internal", initial);
            amount = TimeSpan.MinValue;
            var refreshed = query.ToQueryString();
            Assert.Contains("-9223372036854775808", refreshed);
            Assert.Equal(initial[initial.IndexOf("SELECT", StringComparison.Ordinal)..],
                refreshed[refreshed.IndexOf("SELECT", StringComparison.Ordinal)..]);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Date_clock_combination_promotes_precision_and_supports_Unspecified_kind(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var clock = new TimeOnly(12, 34, 56).Add(TimeSpan.FromTicks(mode == OceanBaseCompatibilityMode.MySql ? 10 : 1));
            var sql = context.Rows.Where(row => row.Day.ToDateTime(clock) > row.Stamp)
                .Select(row => row.Day.ToDateTime(row.Clock, DateTimeKind.Unspecified).AddTicks(10)).ToQueryString();
            Assert.DoesNotContain("__oceanbase_internal", sql);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? " AS DATETIME(6))" : " AS TIMESTAMP(7))", sql);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Clock_ranges_translate_wrapping_and_contextualize_scalar_converters(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var start = new TimeOnly(22, 0);
            var end = new TimeOnly(2, 0);
            var query = context.Rows.Where(row => row.ConvertedClock.IsBetween(start, end))
                .Select(row => row.Clock.IsBetween(row.LowClock, row.Clock));
            var sql = query.ToQueryString();
            Assert.DoesNotContain("__oceanbase_internal", sql);
            Assert.Contains("23:00:00", sql);
            Assert.Contains("03:00:00", sql);
            start = new TimeOnly(21, 0);
            Assert.NotEqual(sql, query.ToQueryString());
            Assert.Throws<NotSupportedException>(() => context.Rows.Where(row =>
                row.ConvertedClock.IsBetween(row.Clock, end)).ToQueryString());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Date_clock_combination_rejects_Kinds_that_cannot_be_preserved(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            Assert.Throws<InvalidOperationException>(() => context.Rows.Where(row =>
                row.Day.ToDateTime(row.Clock, DateTimeKind.Utc) > row.Stamp).ToQueryString());
            var kind = DateTimeKind.Unspecified;
            Assert.Throws<InvalidOperationException>(() => context.Rows.Where(row =>
                row.Day.ToDateTime(row.Clock, kind) > row.Stamp).ToQueryString());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Type_changing_conversions_use_their_own_promoted_result_mapping(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var source = context.GetService<IRelationalTypeMappingSource>();
            var methods = context.GetService<IMethodCallTranslatorProvider>();
            var logger = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>();
            var low = context.Model.FindEntityType(typeof(Row))!.FindProperty(nameof(Row.LowClock))!.GetRelationalTypeMapping();
            var input = new TestColumnExpression("LowClock", "r", typeof(TimeOnly), low, true);
            var duration = methods.Translate(context.Model, input, typeof(TimeOnly).GetMethod(nameof(TimeOnly.ToTimeSpan))!, [], logger);
            Assert.NotNull(duration);
            Assert.Equal(mode == OceanBaseCompatibilityMode.MySql ? 6 : 7, duration.TypeMapping!.Precision);
            Assert.Equal(typeof(TimeSpan), duration.TypeMapping.ClrType);
            var day = new TestColumnExpression("Day", "r", typeof(DateOnly), source.FindMapping(typeof(DateOnly)), true);
            var combined = methods.Translate(context.Model, day,
                typeof(DateOnly).GetMethod(nameof(DateOnly.ToDateTime), [typeof(TimeOnly)])!, [input], logger);
            Assert.NotNull(combined);
            Assert.Equal(mode == OceanBaseCompatibilityMode.MySql ? "DATETIME(6)" : "TIMESTAMP(7)", combined.TypeMapping!.StoreType);
            Assert.Null(combined.TypeMapping.Converter);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Temporal_conversions_reject_text_carriers(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            Assert.Throws<InvalidOperationException>(() => context.Rows.Where(row => DateOnly.FromDateTime(row.TextStamp).Year > 2000).ToQueryString());
            Assert.Throws<InvalidOperationException>(() => context.Rows.Where(row => TimeOnly.FromDateTime(row.TextStamp).Ticks > 0).ToQueryString());
            Assert.Throws<InvalidOperationException>(() => context.Rows.Where(row => TimeOnly.FromTimeSpan(row.TextDuration).Ticks > 0).ToQueryString());
            Assert.Throws<InvalidOperationException>(() => context.Rows.Where(row => row.TextClock.ToTimeSpan().Ticks > 0).ToQueryString());
            Assert.Throws<InvalidOperationException>(() => context.Rows.Where(row => row.Day.ToDateTime(row.TextClock) > row.Stamp).ToQueryString());
            Assert.Throws<InvalidOperationException>(() => context.Rows.Where(row => row.TextClock.IsBetween(row.Clock, row.LowClock)).ToQueryString());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Clock_extraction_renderer_rejects_a_duration_result_mapping(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var source = context.GetService<IRelationalTypeMappingSource>();
            var factory = context.GetService<ISqlExpressionFactory>();
            var stamp = new TestColumnExpression("Stamp", "r", typeof(DateTime), source.FindMapping(typeof(DateTime)), true);
            var malformed = factory.Function(OceanBaseTemporalMemberTranslator.TimeOfDayMarker, [stamp], nullable: true,
                argumentsPropagateNullability: [false], typeof(TimeOnly), source.FindMapping(typeof(TimeSpan)));
            var generator = new OceanBaseQuerySqlGenerator(context.GetService<QuerySqlGeneratorDependencies>(), mode);
            Assert.Throws<InvalidOperationException>(() => generator.GetCommand(malformed));
        }

        private static Context Create(OceanBaseCompatibilityMode mode, bool keepTemporalConversions = false)
        {
            var builder = new DbContextOptionsBuilder<Context>().UseOceanBase("Server=offline;Database=temporal_conversion", mode);
            if (keepTemporalConversions) builder.ReplaceService<IEvaluatableExpressionFilter, TemporalConversionFilter>().EnableServiceProviderCaching(false);
            return new Context(builder.Options);
        }

        private sealed class Context(DbContextOptions<Context> options) : DbContext(options)
        {
            public DbSet<Row> Rows => Set<Row>();
            protected override void OnModelCreating(ModelBuilder model)
            {
                model.Entity<Row>().Property(row => row.LowClock).HasPrecision(0);
                model.Entity<Row>().Property(row => row.ConvertedClock).HasConversion(value => value.AddHours(1), value => value.AddHours(-1));
                model.Entity<Row>().Property(row => row.TextStamp).HasConversion(value => value.ToString("O"), value => DateTime.Parse(value));
                model.Entity<Row>().Property(row => row.TextClock).HasConversion(value => value.ToString("O"), value => TimeOnly.Parse(value));
                model.Entity<Row>().Property(row => row.TextDuration).HasConversion(value => value.ToString(), value => TimeSpan.Parse(value));
            }
        }

        private sealed class Row
        {
            public int Id { get; set; }
            public DateTime Stamp { get; set; }
            public DateTime OtherStamp { get; set; }
            public DateTime? OptionalStamp { get; set; }
            public DateOnly Day { get; set; }
            public TimeOnly Clock { get; set; }
            public TimeOnly LowClock { get; set; }
            public TimeOnly ConvertedClock { get; set; }
            public TimeSpan Duration { get; set; }
            public DateTime TextStamp { get; set; }
            public TimeOnly TextClock { get; set; }
            public TimeSpan TextDuration { get; set; }
        }
    }
}
