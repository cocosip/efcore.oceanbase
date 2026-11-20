using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
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
    public sealed class DateTimeNumericAddQueryTests
    {
        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Date_difference_translates_long_durations_members_and_composition(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var anchor = new DateTime(2000, 1, 1);
            var duration = TimeSpan.FromDays(1000);
            var query = context.Rows.Where(row => row.Stamp.Subtract(anchor) > duration)
                .OrderBy(row => row.OptionalStamp!.Value.Subtract(row.Stamp).Ticks)
                .Select(row => new
                {
                    Difference = (TimeSpan?)row.OptionalStamp!.Value.Subtract(anchor),
                    Days = row.Stamp.Subtract(anchor).Days,
                    Hours = row.Stamp.Subtract(anchor).Hours,
                    Nanoseconds = row.Stamp.Subtract(anchor).Nanoseconds,
                    TotalDays = row.Stamp.Subtract(anchor).TotalDays,
                    Recombined = anchor.Add(row.Stamp.Subtract(anchor)),
                    Inverse = row.Stamp.Subtract(row.Stamp.Subtract(anchor))
                });
            var sql = query.ToQueryString();
            Assert.DoesNotContain("__oceanbase_internal", sql);
            Assert.DoesNotContain("SEC_TO_TIME", sql);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "DATEDIFF(" : "EXTRACT(SECOND FROM", sql);
            anchor = new DateTime(1900, 1, 1);
            Assert.Contains("1900", query.ToQueryString());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Date_difference_normalizes_mixed_duration_carriers(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var anchor = new DateTime(2000, 1, 1);
            var query = context.Rows.Where(row => row.Stamp.Subtract(anchor) > row.Duration!.Value)
                .Select(row => new
                {
                    Coalesced = (TimeSpan?)row.OptionalStamp!.Value.Subtract(anchor) ?? row.Duration,
                    Conditional = row.Id == 1 ? row.Duration : (TimeSpan?)row.Stamp.Subtract(anchor)
                });
            var sql = query.ToQueryString();
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "TIME_TO_SEC(" : "EXTRACT(DAY FROM", sql);
            Assert.DoesNotContain("__oceanbase_internal", sql);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Date_difference_rejects_set_operations_with_native_duration_carriers(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var anchor = new DateTime(2000, 1, 1);
            var query = context.Rows.Select(row => row.Stamp.Subtract(anchor))
                .Union(context.Rows.Select(row => row.Duration!.Value));
            Assert.Contains("duration", Assert.Throws<NotSupportedException>(() => query.ToQueryString()).Message);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Date_difference_contextualizes_scalar_converters_and_rejects_incompatible_columns(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var anchor = new DateTime(2000, 1, 1);
            var query = context.Rows.Where(row => row.ConvertedStamp.Subtract(anchor) > TimeSpan.Zero);
            Assert.Contains("2000-01-08", query.ToQueryString());
            Assert.Throws<NotSupportedException>(() => context.Rows.Where(row => row.ConvertedStamp.Subtract(row.Stamp) > TimeSpan.Zero).ToQueryString());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Date_difference_rejects_duration_operators_and_custom_native_converters(OceanBaseCompatibilityMode mode, bool converted)
        {
            using var context = Create(mode);
            var anchor = new DateTime(2000, 1, 1);
            if (converted)
                Assert.Throws<NotSupportedException>(() => context.Rows.Select(row => row.Id == 1
                    ? row.ConvertedDuration : row.Stamp.Subtract(anchor)).ToQueryString());
            else
                Assert.Throws<NotSupportedException>(() => context.Rows.Where(row => row.Stamp.Subtract(anchor) + row.Duration!.Value > TimeSpan.Zero).ToQueryString());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Date_difference_normalizes_CompareTo_and_rejects_mixed_membership(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var anchor = new DateTime(2000, 1, 1);
            var sql = context.Rows.Select(row => row.Stamp.Subtract(anchor).CompareTo(row.Duration!.Value)).ToQueryString();
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "TIME_TO_SEC(`r`.`Duration`)" : "EXTRACT(DAY FROM \"r\".\"Duration\")", sql);
            Assert.Contains("THEN NULL", sql);
            Assert.Throws<NotSupportedException>(() => context.Rows.Where(row => context.Rows.Select(value => value.Duration!.Value)
                .Contains(row.Stamp.Subtract(anchor))).ToQueryString());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Date_difference_preserves_fractional_scalars_against_whole_second_columns(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var anchor = new DateTime(2000, 1, 1).AddTicks(mode == OceanBaseCompatibilityMode.MySql ? 10 : 1);
            var source = context.GetService<IRelationalTypeMappingSource>();
            var methods = context.GetService<IMethodCallTranslatorProvider>();
            var factory = context.GetService<ISqlExpressionFactory>();
            var narrow = source.FindMapping(typeof(DateTime), mode == OceanBaseCompatibilityMode.MySql ? "DATETIME(0)" : "DATE")!;
            var result = Assert.IsType<SqlBinaryExpression>(methods.Translate(context.Model,
                new ColumnExpression("DateStamp", "r", typeof(DateTime), narrow, true),
                typeof(DateTime).GetMethod(nameof(DateTime.Subtract), [typeof(DateTime)])!,
                [factory.Constant(anchor, source.FindMapping(typeof(DateTime)))],
                context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>()));
            Assert.Equal(mode == OceanBaseCompatibilityMode.MySql ? "DATETIME(6)" : "TIMESTAMP(7)",
                Assert.IsType<SqlFunctionExpression>(result.Right).Arguments![0].TypeMapping!.StoreType);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Minute_through_tick_additions_translate_server_side(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            foreach (var name in new[] { nameof(DateTime.AddMinutes), nameof(DateTime.AddSeconds),
                nameof(DateTime.AddMilliseconds), nameof(DateTime.AddMicroseconds), nameof(DateTime.AddTicks) })
            {
                var ticks = name == nameof(DateTime.AddTicks);
                var row = Expression.Parameter(typeof(Row), "row");
                var amount = ticks ? (Expression)Expression.Property(row, nameof(Row.TickAmount))
                    : Expression.Property(row, nameof(Row.Amount));
                var call = Expression.Call(Expression.Property(row, nameof(Row.Stamp)), name, null, amount);
                var predicate = Expression.Lambda<Func<Row, bool>>(Expression.GreaterThan(call,
                    Expression.Constant(new DateTime(2000, 1, 1))), row);
                var sql = context.Rows.Where(predicate).Select(value => value.Id).ToQueryString();
                Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "TIMESTAMPADD(MICROSECOND" : "NUMTODSINTERVAL", sql);
                Assert.DoesNotContain("__oceanbase_internal", sql);
                Assert.DoesNotContain("TIME_TO_SEC", sql);
                Assert.Contains(ticks ? "TickAmount" : "Amount", sql);
                if (ticks)
                {
                    Assert.DoesNotContain("TRUNC", sql);
                    Assert.Contains("BETWEEN -3155378975999999999 AND 3155378975999999999", sql);
                }
            }
            var tickAmount = 9007199254740991L;
            var query = context.Rows.Where(row => row.Stamp.AddTicks(tickAmount).AddMicroseconds(0.5) > row.Stamp)
                .Select(row => (DateTime?)row.OptionalStamp!.Value.AddTicks(-10));
            Assert.Contains(tickAmount.ToString(), query.ToQueryString());
            tickAmount = -9007199254740991L;
            Assert.Contains(tickAmount.ToString(), query.ToQueryString());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Numeric_addition_translates_predicates_columns_parameters_and_composition(
            OceanBaseCompatibilityMode mode, bool hours)
        {
            using var context = Create(mode);
            var amount = -1.25;
            var query = hours
                ? context.Rows.Where(row => row.Stamp.AddHours(amount) > new DateTime(2000, 1, 1))
                    .OrderBy(row => row.OptionalStamp!.Value.AddHours(row.Amount))
                    .Select(row => (DateTime?)row.OptionalStamp!.Value.AddHours(0.5).AddHours(row.Amount))
                : context.Rows.Where(row => row.Stamp.AddDays(amount) > new DateTime(2000, 1, 1))
                    .OrderBy(row => row.OptionalStamp!.Value.AddDays(row.Amount))
                    .Select(row => (DateTime?)row.OptionalStamp!.Value.AddDays(0.5).AddDays(row.Amount));
            var sql = query.ToQueryString();
            Assert.DoesNotContain("__oceanbase_internal", sql);
            Assert.Contains("OptionalStamp", sql);
            Assert.Contains("Amount", sql);
            Assert.Contains(hours ? "36000000000" : "864000000000", sql);
            Assert.Contains("CASE WHEN ABS(", sql);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "TIMESTAMPADD(MICROSECOND" : "NUMTODSINTERVAL", sql);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? " AS DATETIME(6))" : " AS TIMESTAMP(7))", sql);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "MOD(" : " / 10000000", sql);
            Assert.DoesNotContain("TIME_TO_SEC", sql);
            amount = 900.5;
            var refreshed = query.ToQueryString();
            Assert.Contains("900.5", refreshed);
            Assert.Equal(sql[sql.IndexOf("SELECT", StringComparison.Ordinal)..],
                refreshed[refreshed.IndexOf("SELECT", StringComparison.Ordinal)..]);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Numeric_addition_promotes_low_precision_results_and_preserves_converter_ownership(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var source = context.GetService<IRelationalTypeMappingSource>();
            var methods = context.GetService<IMethodCallTranslatorProvider>();
            var factory = context.GetService<ISqlExpressionFactory>();
            var logger = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>();
            var converter = new ValueConverter<DateTime, DateTime>(value => value.AddDays(1), value => value.AddDays(-1));
            var narrow = source.FindMapping(typeof(DateTime), mode == OceanBaseCompatibilityMode.MySql ? "DATETIME(0)" : "DATE")!;
            var mapping = (RelationalTypeMapping)narrow.WithComposedConverter(converter);
            foreach (var name in new[] { nameof(DateTime.AddDays), nameof(DateTime.AddHours), nameof(DateTime.AddMinutes),
                nameof(DateTime.AddSeconds), nameof(DateTime.AddMilliseconds), nameof(DateTime.AddMicroseconds), nameof(DateTime.AddTicks) })
            {
                var ticks = name == nameof(DateTime.AddTicks);
                var result = Assert.IsType<SqlFunctionExpression>(methods.Translate(context.Model,
                    new ColumnExpression("Stamp", "r", typeof(DateTime), mapping, true),
                    typeof(DateTime).GetMethod(name, [ticks ? typeof(long) : typeof(double)])!,
                    [ticks ? factory.Constant(10L, source.FindMapping(typeof(long)))
                        : factory.Constant(0.5, source.FindMapping(typeof(double)))], logger));
                Assert.Equal(mode == OceanBaseCompatibilityMode.MySql ? "DATETIME(6)" : "TIMESTAMP(7)", result.TypeMapping!.StoreType);
                Assert.Same(converter, result.TypeMapping.Converter);
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Numeric_addition_rejects_incompatible_carriers_and_null_converters(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var source = context.GetService<IRelationalTypeMappingSource>();
            var methods = context.GetService<IMethodCallTranslatorProvider>();
            var factory = context.GetService<ISqlExpressionFactory>();
            var logger = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>();
            var date = source.FindMapping(typeof(DateTime))!;
            var number = source.FindMapping(typeof(double))!;
            var textNumber = (RelationalTypeMapping)source.FindMapping(typeof(string))!.WithComposedConverter(
                new ValueConverter<double, string>(value => value.ToString(), value => double.Parse(value)));
            var textDate = (RelationalTypeMapping)source.FindMapping(typeof(string))!.WithComposedConverter(
                new ValueConverter<DateTime, string>(value => value.ToString(), value => DateTime.Parse(value)));
            var nullNumber = (RelationalTypeMapping)number.WithComposedConverter(
                new ValueConverter<double, double>(value => value, value => value, convertsNulls: true));
            foreach (var name in new[] { nameof(DateTime.AddDays), nameof(DateTime.AddHours), nameof(DateTime.AddMinutes),
                nameof(DateTime.AddSeconds), nameof(DateTime.AddMilliseconds), nameof(DateTime.AddMicroseconds) })
            {
                var method = typeof(DateTime).GetMethod(name, [typeof(double)])!;
                var input = new ColumnExpression("Stamp", "r", typeof(DateTime), date, true);
                Assert.NotNull(methods.Translate(context.Model, input, method, [factory.Constant(0.5, number)], logger));
                foreach (var invalid in new[] { textNumber, nullNumber })
                    Assert.Null(methods.Translate(context.Model, input, method, [factory.Constant(0.5, invalid)], logger));
                Assert.Null(methods.Translate(context.Model, new ColumnExpression("Stamp", "r", typeof(DateTime), textDate, true),
                    method, [factory.Constant(0.5, number)], logger));
            }
            var tickMethod = typeof(DateTime).GetMethod(nameof(DateTime.AddTicks), [typeof(long)])!;
            var tickInput = new ColumnExpression("Stamp", "r", typeof(DateTime), date, true);
            var tickMapping = source.FindMapping(typeof(long))!;
            var nullTicks = (RelationalTypeMapping)tickMapping.WithComposedConverter(
                new ValueConverter<long, long>(value => value, value => value, convertsNulls: true));
            var textTicks = (RelationalTypeMapping)source.FindMapping(typeof(string))!.WithComposedConverter(
                new ValueConverter<long, string>(value => value.ToString(), value => long.Parse(value)));
            foreach (var invalid in new[] { nullTicks, textTicks })
                Assert.Null(methods.Translate(context.Model, tickInput, tickMethod, [factory.Constant(10L, invalid)], logger));
            Assert.Null(methods.Translate(context.Model, tickInput, tickMethod, [factory.Constant(10.0, number)], logger));
            Assert.Null(methods.Translate(context.Model, new ColumnExpression("Stamp", "r", typeof(DateTime), textDate, true),
                tickMethod, [factory.Constant(10L, tickMapping)], logger));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Numeric_renderer_rejects_malformed_markers(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var factory = context.GetService<ISqlExpressionFactory>();
            var source = context.GetService<IRelationalTypeMappingSource>();
            var date = source.FindMapping(typeof(DateTime))!;
            var stamp = new ColumnExpression("Stamp", "r", typeof(DateTime), date, true);
            var amount = factory.Constant(0.5, source.FindMapping(typeof(double)));
            foreach (var marker in new[] { OceanBaseTemporalMethodTranslator.DateTimeAddDaysMarker,
                OceanBaseTemporalMethodTranslator.DateTimeAddHoursMarker, OceanBaseTemporalMethodTranslator.DateTimeAddMinutesMarker,
                OceanBaseTemporalMethodTranslator.DateTimeAddSecondsMarker, OceanBaseTemporalMethodTranslator.DateTimeAddMillisecondsMarker,
                OceanBaseTemporalMethodTranslator.DateTimeAddMicrosecondsMarker, OceanBaseTemporalMethodTranslator.DateTimeAddTicksMarker })
            {
                SqlExpression[] invalid =
                [
                    factory.Function(marker, [stamp], true, [false], typeof(DateTime), date),
                    factory.Function(marker, [stamp, amount], false, [false, false], typeof(DateTime), date),
                    factory.Function(marker, [stamp, amount], true, [true, true], typeof(DateTime), date),
                    factory.Function(marker, [stamp, factory.Constant(1)], true, [false, false], typeof(DateTime), date),
                    factory.Function(marker, [stamp, amount], true, [false, false], typeof(int), source.FindMapping(typeof(int)))
                ];
                foreach (var expression in invalid)
                {
                    var generator = new OceanBaseQuerySqlGenerator(context.GetService<QuerySqlGeneratorDependencies>(), mode);
                    Assert.Contains("Invalid internal DateTime numeric-add", Assert.Throws<InvalidOperationException>(
                        () => generator.GetCommand(expression)).Message);
                }
            }
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public void Oracle_generated_parameter_collisions_keep_both_values(bool reverse, bool cloneMapping)
        {
            using var context = Create(OceanBaseCompatibilityMode.Oracle);
            var factory = context.GetService<ISqlExpressionFactory>();
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(int))!;
            var digit = new SqlParameterExpression("8_anchor", typeof(int), mapping);
            var ordinary = new SqlParameterExpression("_ob_8_anchor", typeof(int), mapping);
            var repeatedInput = reverse ? digit : ordinary;
            var repeatedMapping = cloneMapping ? mapping.WithStoreTypeAndSize(mapping.StoreType, null) : mapping;
            if (cloneMapping) Assert.NotSame(mapping, repeatedMapping);
            var secondOccurrence = new SqlParameterExpression(repeatedInput.Name, repeatedInput.Type,
                cloneMapping ? !repeatedInput.IsNullable : repeatedInput.IsNullable, repeatedMapping);
            var values = new Dictionary<string, object?> { ["8_anchor"] = 11, ["_ob_8_anchor"] = 22 };
            var processor = context.GetService<IRelationalParameterBasedSqlProcessorFactory>()
                .Create(new RelationalParameterBasedSqlProcessorParameters(false, new HashSet<string>()));
            var expression = reverse ? factory.Add(ordinary, factory.Add(digit, secondOccurrence))
                : factory.Add(digit, factory.Add(ordinary, secondOccurrence));
            var processed = processor.Optimize(ProjectScalar(expression), values, out _);
            var addition = Assert.IsType<SqlBinaryExpression>(
                Assert.Single(Assert.IsType<SelectExpression>(processed).Projection).Expression);
            var repeated = Assert.IsType<SqlBinaryExpression>(addition.Right);
            Assert.Equal(Assert.IsType<SqlParameterExpression>(repeated.Left).Name,
                Assert.IsType<SqlParameterExpression>(repeated.Right).Name);
            // EF patch versions may canonicalize equivalent repeated-parameter mappings.
            var actualMapping = Assert.IsAssignableFrom<RelationalTypeMapping>(Assert.IsType<SqlParameterExpression>(repeated.Right).TypeMapping);
            Assert.Equal(repeatedMapping.GetType(), actualMapping.GetType());
            Assert.Equal(repeatedMapping.ClrType, actualMapping.ClrType);
            Assert.Equal(repeatedMapping.StoreType, actualMapping.StoreType);
            Assert.Equal(repeatedMapping.DbType, actualMapping.DbType);
            Assert.Equal(repeatedMapping.Size, actualMapping.Size);
            Assert.Equal(repeatedMapping.Precision, actualMapping.Precision);
            Assert.Equal(repeatedMapping.Scale, actualMapping.Scale);
            Assert.Equal(repeatedMapping.IsUnicode, actualMapping.IsUnicode);
            Assert.Equal(repeatedMapping.IsFixedLength, actualMapping.IsFixedLength);
            Assert.Equal(repeatedMapping.Converter, actualMapping.Converter);
            Assert.NotEqual(Assert.IsType<SqlParameterExpression>(addition.Left).Name,
                Assert.IsType<SqlParameterExpression>(repeated.Left).Name);
            var generator = new OceanBaseQuerySqlGenerator(context.GetService<QuerySqlGeneratorDependencies>(), OceanBaseCompatibilityMode.Oracle);
            var sql = generator.GetCommand(processed);
            using var command = new OceanBaseCommand();
            foreach (var parameter in sql.Parameters) parameter.AddDbParameter(command, values);
            Assert.Equal(2, command.Parameters.Count);
            Assert.NotEqual(command.Parameters[0].ParameterName, command.Parameters[1].ParameterName);
            Assert.Equal(reverse ? 22 : 11, command.Parameters[0].Value);
            Assert.Equal(reverse ? 11 : 22, command.Parameters[1].Value);
            Assert.All(command.Parameters.Cast<OceanBaseParameter>(), parameter => Assert.Equal(mapping.DbType, parameter.DbType));
        }

        [Fact]
        public void Oracle_repeated_parameters_with_different_converters_keep_distinct_bound_values()
        {
            using var context = Create(OceanBaseCompatibilityMode.Oracle);
            var factory = context.GetService<ISqlExpressionFactory>();
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(int))!;
            var shiftedMapping = (RelationalTypeMapping)mapping.WithComposedConverter(
                new ValueConverter<int, int>(value => value + 100, value => value - 100));
            var ordinary = new SqlParameterExpression("8_amount", typeof(int), mapping);
            var shifted = new SqlParameterExpression("8_amount", typeof(int), shiftedMapping);
            var values = new Dictionary<string, object?> { ["8_amount"] = 11 };
            var processor = context.GetService<IRelationalParameterBasedSqlProcessorFactory>()
                .Create(new RelationalParameterBasedSqlProcessorParameters(false, new HashSet<string>()));
            var expression = factory.Add(ordinary, factory.Add(shifted, factory.Add(ordinary, shifted)));
            var processed = processor.Optimize(ProjectScalar(expression), values, out _);
            var generator = new OceanBaseQuerySqlGenerator(context.GetService<QuerySqlGeneratorDependencies>(), OceanBaseCompatibilityMode.Oracle);
            using var command = new OceanBaseCommand();
            foreach (var parameter in generator.GetCommand(processed).Parameters) parameter.AddDbParameter(command, values);
            Assert.Equal(2, command.Parameters.Count);
            Assert.Equal(11, command.Parameters[0].Value);
            Assert.Equal(111, command.Parameters[1].Value);
        }

        private static SelectExpression ProjectScalar(SqlExpression expression)
            => new(null, [], null, [], null, [new ProjectionExpression(expression, "Value")], false, [], null, null);

        private static Context Create(OceanBaseCompatibilityMode mode)
            => new(new DbContextOptionsBuilder<Context>().UseOceanBase("Server=offline;Database=numeric_date_add", mode).Options);

        private sealed class Context(DbContextOptions<Context> options) : DbContext(options)
        {
            public DbSet<Row> Rows => Set<Row>();
            protected override void OnModelCreating(ModelBuilder model)
            {
                model.Entity<Row>().Property(row => row.ConvertedStamp).HasConversion(value => value.AddDays(7), value => value.AddDays(-7));
                model.Entity<Row>().Property(row => row.ConvertedDuration).HasConversion(value => value.Negate(), value => value.Negate());
            }
        }

        private sealed class Row
        {
            public int Id { get; set; }
            public DateTime Stamp { get; set; }
            public DateTime? OptionalStamp { get; set; }
            public double Amount { get; set; }
            public long TickAmount { get; set; }
            public TimeSpan? Duration { get; set; }
            public TimeSpan ConvertedDuration { get; set; }
            public DateTime ConvertedStamp { get; set; }
        }
    }
}
