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
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests
{
    public sealed class TimeOnlyAddQueryTests
    {
        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void TimeOnly_additions_translate_predicates_parameters_columns_and_composition(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var hours = -25.5;
            var duration = TimeSpan.MaxValue;
            var query = context.Rows.Where(row => row.Clock.AddHours(hours).Ticks > 0)
                .OrderBy(row => row.Clock.AddMinutes(row.Amount))
                .Select(row => new
                {
                    Duration = (TimeOnly?)row.OptionalClock!.Value.Add(row.Duration!.Value),
                    LongDuration = row.Clock.Add(duration),
                    Composed = row.LowClock.AddHours(0.5).AddMinutes(row.Amount).Add(duration),
                    Converted = row.ConvertedClock.AddMinutes(row.Amount)
                });
            var sql = query.ToQueryString();
            Assert.DoesNotContain("__oceanbase_internal", sql);
            Assert.Contains("864000000000", sql);
            Assert.Contains("9223372036854775807", sql);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? " AS TIME(6))" : "TO SECOND(7))", sql);
            var body = sql[sql.IndexOf("SELECT", StringComparison.Ordinal)..];
            hours = 900.25;
            duration = TimeSpan.MinValue;
            var refreshed = query.ToQueryString();
            Assert.Contains("-9223372036854775808", refreshed);
            Assert.Equal(body, refreshed[refreshed.IndexOf("SELECT", StringComparison.Ordinal)..]);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void TimeOnly_add_rejects_custom_converted_duration_columns(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            Assert.Throws<NotSupportedException>(() => context.Rows.Where(row =>
                row.Clock.Add(row.ConvertedDuration).Ticks > 0).ToQueryString());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void TimeOnly_renderer_rejects_malformed_internal_expressions(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var source = context.GetService<IRelationalTypeMappingSource>();
            var factory = context.GetService<ISqlExpressionFactory>();
            foreach (var marker in new[] { OceanBaseTemporalMethodTranslator.TimeOnlyFromTicksMarker,
                OceanBaseTemporalMethodTranslator.TimeOnlyHoursToTicksMarker,
                OceanBaseTemporalMethodTranslator.TimeOnlyMinutesToTicksMarker })
            {
                var clock = marker == OceanBaseTemporalMethodTranslator.TimeOnlyFromTicksMarker;
                var result = source.FindMapping(clock ? typeof(TimeOnly) : typeof(long))!;
                var input = clock ? factory.Constant(1L, source.FindMapping(typeof(long)))
                    : factory.Constant(0.5, source.FindMapping(typeof(double)));
                SqlExpression[] invalid =
                [
                    factory.Function(marker, [], true, [], result.ClrType, result),
                    factory.Function(marker, [input], false, [false], result.ClrType, result),
                    factory.Function(marker, [input], true, [true], result.ClrType, result),
                    factory.Function(marker, [factory.Constant("bad")], true, [false], result.ClrType, result),
                    factory.Function(marker, [input], true, [false], typeof(string), source.FindMapping(typeof(string)))
                ];
                foreach (var expression in invalid)
                {
                    var generator = new OceanBaseQuerySqlGenerator(context.GetService<QuerySqlGeneratorDependencies>(), mode);
                    Assert.Throws<InvalidOperationException>(() => generator.GetCommand(expression));
                }
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void TimeOnly_translator_rejects_incompatible_clock_and_numeric_carriers(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var source = context.GetService<IRelationalTypeMappingSource>();
            var factory = context.GetService<ISqlExpressionFactory>();
            var methods = context.GetService<IMethodCallTranslatorProvider>();
            var logger = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>();
            var clock = source.FindMapping(typeof(TimeOnly))!;
            var text = (RelationalTypeMapping)source.FindMapping(typeof(string))!.WithComposedConverter(
                new ValueConverter<TimeOnly, string>(value => value.ToString(), value => TimeOnly.Parse(value)));
            var nullNumber = (RelationalTypeMapping)source.FindMapping(typeof(double))!.WithComposedConverter(
                new ValueConverter<double, double>(value => value, value => value, convertsNulls: true));
            foreach (var name in new[] { nameof(TimeOnly.AddHours), nameof(TimeOnly.AddMinutes) })
            {
                var method = typeof(TimeOnly).GetMethod(name, [typeof(double)])!;
                Assert.Null(methods.Translate(context.Model, new ColumnExpression("Clock", "r", typeof(TimeOnly), text, false),
                    method, [factory.Constant(0.5)], logger));
                Assert.Null(methods.Translate(context.Model, new ColumnExpression("Clock", "r", typeof(TimeOnly), clock, false),
                    method, [factory.Constant(0.5, nullNumber)], logger));
            }
        }

        private static Context Create(OceanBaseCompatibilityMode mode)
            => new(new DbContextOptionsBuilder<Context>().UseOceanBase("Server=offline;Database=timeonly_add", mode).Options);

        private sealed class Context(DbContextOptions<Context> options) : DbContext(options)
        {
            public DbSet<Row> Rows => Set<Row>();
            protected override void OnModelCreating(ModelBuilder model)
            {
                model.Entity<Row>().Property(row => row.LowClock).HasPrecision(0);
                model.Entity<Row>().Property(row => row.ConvertedClock).HasConversion(value => value.AddHours(1), value => value.AddHours(-1));
                model.Entity<Row>().Property(row => row.ConvertedDuration).HasConversion(value => value.Negate(), value => value.Negate());
            }
        }

        private sealed class Row
        {
            public int Id { get; set; }
            public TimeOnly Clock { get; set; }
            public TimeOnly LowClock { get; set; }
            public TimeOnly ConvertedClock { get; set; }
            public TimeOnly? OptionalClock { get; set; }
            public TimeSpan? Duration { get; set; }
            public TimeSpan ConvertedDuration { get; set; }
            public double Amount { get; set; }
        }
    }
}
