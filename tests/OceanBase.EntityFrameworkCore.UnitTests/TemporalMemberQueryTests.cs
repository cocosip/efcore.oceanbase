using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
    public sealed class TemporalMemberQueryTests
    {
        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Twelve_parts_translate_in_query_positions(OceanBaseCompatibilityMode mode, bool relationalNulls)
        {
            using var context = CreateContext(mode, relationalNulls);
            var oracle = mode == OceanBaseCompatibilityMode.Oracle;
            var parts = new (IQueryable Query, string Sql)[]
            {
                (context.Rows.Where(row => row.Stamp.Year == 2024), oracle ? "EXTRACT(YEAR FROM" : "YEAR("),
                (context.Rows.Where(row => row.Stamp.Month == 2), oracle ? "EXTRACT(MONTH FROM" : "MONTH("),
                (context.Rows.Where(row => row.Stamp.Day == 29), oracle ? "EXTRACT(DAY FROM" : "DAYOFMONTH("),
                (context.Rows.OrderBy(row => row.Stamp.Hour), oracle ? "EXTRACT(HOUR FROM" : "HOUR("),
                (context.Rows.OrderBy(row => row.Stamp.Minute), oracle ? "EXTRACT(MINUTE FROM" : "MINUTE("),
                (context.Rows.Select(row => row.Stamp.Second), oracle ? "TRUNC(EXTRACT(SECOND FROM" : "SECOND("),
                (context.Rows.Where(row => row.Date.Year == 2024), oracle ? "EXTRACT(YEAR FROM" : "YEAR("),
                (context.Rows.Where(row => row.Date.Month == 2), oracle ? "EXTRACT(MONTH FROM" : "MONTH("),
                (context.Rows.GroupBy(row => row.Date.Day).Select(group => group.Key), oracle ? "EXTRACT(DAY FROM" : "DAYOFMONTH("),
                (context.Rows.OrderBy(row => row.Clock.Hour), oracle ? "EXTRACT(HOUR FROM" : "HOUR("),
                (context.Rows.OrderBy(row => row.Clock.Minute), oracle ? "EXTRACT(MINUTE FROM" : "MINUTE("),
                (context.Rows.Select(row => row.Clock.Second), oracle ? "TRUNC(EXTRACT(SECOND FROM" : "SECOND(")
            };
            foreach (var (query, expected) in parts)
            {
                var sql = query.ToQueryString();
                Assert.Contains(expected, sql);
                Assert.DoesNotContain("__oceanbase_internal", sql);
            }
            using var opposite = CreateContext(oracle ? OceanBaseCompatibilityMode.MySql : OceanBaseCompatibilityMode.Oracle);
            Assert.Contains(oracle ? "SECOND(" : "TRUNC(EXTRACT(SECOND FROM", opposite.Rows.Select(row => row.Stamp.Second).ToQueryString());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void DateTime_clock_members_translate_to_server_functions(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var sql = context.Rows.Select(row => new
            {
                Now = DateTime.Now,
                UtcNow = DateTime.UtcNow,
                Today = DateTime.Today
            }).ToQueryString();

            Assert.DoesNotContain("__oceanbase_internal", sql);
            if (mode == OceanBaseCompatibilityMode.MySql)
            {
                Assert.Contains("CURRENT_TIMESTAMP(6)", sql);
                Assert.Contains("UTC_TIMESTAMP(6)", sql);
                Assert.Contains("CURDATE()", sql);
            }
            else
            {
                Assert.Contains("CAST(CURRENT_TIMESTAMP AS TIMESTAMP)", sql);
                Assert.Contains("CAST(SYS_EXTRACT_UTC(SYSTIMESTAMP) AS TIMESTAMP)", sql);
                Assert.Contains("TRUNC(CURRENT_DATE)", sql);
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Nullable_parts_keep_result_null_tests_and_composition(OceanBaseCompatibilityMode mode, bool relationalNulls)
        {
            using var context = CreateContext(mode, relationalNulls);
            var sql = context.Rows.Select(row => new
            {
                Part = (int?)row.OptionalStamp!.Value.Second,
                Fallback = row.OptionalDate!.Value.Day + ((int?)row.OptionalClock!.Value.Minute ?? 0),
                Choice = row.Id > 0 ? row.Stamp.Year : row.Date.Year,
                Match = row.OptionalStamp!.Value.Year == 2024,
                Negated = !(row.OptionalStamp!.Value.Month == 2)
            }).ToQueryString();
            Assert.Contains(mode == OceanBaseCompatibilityMode.Oracle ? "TRUNC(EXTRACT(SECOND FROM" : "SECOND(", sql);
            Assert.Contains("COALESCE(", sql);
            Assert.Contains("CASE", sql);
            var nullSql = context.Rows.Select(row => (int?)row.OptionalStamp!.Value.Second)
                .Where(part => part == null).ToQueryString();
            Assert.Contains(" IS NULL", nullSql);
            Assert.Contains(mode == OceanBaseCompatibilityMode.Oracle ? "TRUNC(EXTRACT(SECOND FROM" : "SECOND(", nullSql);
            Assert.DoesNotContain("WHERE 0 = 1", nullSql);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Exact_members_keep_input_mapping_and_use_integer_outputs(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var factory = context.GetService<ISqlExpressionFactory>();
            var source = context.GetService<IRelationalTypeMappingSource>();
            var members = context.GetService<IMemberTranslatorProvider>();
            var logger = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>();
            foreach (var (type, name, storeType) in new (Type, string, string)[]
            {
                (typeof(DateTime), "Year", mode == OceanBaseCompatibilityMode.Oracle ? "TIMESTAMP(3)" : "DATETIME(3)"),
                (typeof(DateTime), "Second", mode == OceanBaseCompatibilityMode.Oracle ? "DATE" : "DATETIME(3)"),
                (typeof(DateOnly), "Day", "DATE"),
                (typeof(TimeOnly), "Second", mode == OceanBaseCompatibilityMode.Oracle ? "INTERVAL DAY(1) TO SECOND(3)" : "TIME(3)")
            })
            {
                var mapping = source.FindMapping(type, storeType)!;
                var input = factory.Constant(type == typeof(DateTime) ? new DateTime(2024, 2, 29, 23, 59, 59).AddTicks(9999999)
                    : type == typeof(DateOnly) ? new DateOnly(2024, 2, 29) : new TimeOnly(23, 59, 59), mapping);
                var result = members.Translate(input, type.GetProperty(name)!, typeof(int), logger)!;
                Assert.Equal(typeof(int), result.Type);
                Assert.Equal(typeof(int), result.TypeMapping!.ClrType);
                Assert.Null(result.TypeMapping.Converter);
                var outer = Assert.IsType<SqlFunctionExpression>(result);
                Assert.True(outer.IsNullable);
                Assert.All(outer.ArgumentsPropagateNullability!, Assert.False);
                var extract = mode == OceanBaseCompatibilityMode.Oracle && name == "Second"
                    ? Assert.IsType<SqlFunctionExpression>(Assert.Single(outer.Arguments!)) : outer;
                if (mode == OceanBaseCompatibilityMode.Oracle && name == "Second")
                {
                    Assert.Equal("TRUNC", outer.Name);
                    Assert.Equal(typeof(decimal), extract.Type);
                    Assert.Null(extract.TypeMapping!.Converter);
                }
                Assert.True(extract.IsNullable);
                Assert.All(extract.ArgumentsPropagateNullability!, Assert.False);
                var mappedInput = extract.Arguments![^1];
                if (mode == OceanBaseCompatibilityMode.Oracle && type == typeof(DateTime) && storeType == "DATE")
                {
                    var cast = Assert.IsType<SqlUnaryExpression>(mappedInput);
                    Assert.Same(mapping, cast.Operand.TypeMapping);
                    Assert.Equal("TIMESTAMP(7)", cast.TypeMapping!.StoreType);
                }
                else Assert.Same(mapping, mappedInput.TypeMapping);
            }
            Assert.Null(members.Translate(factory.Constant(DateTime.UtcNow, source.FindMapping(typeof(DateTime))!),
                typeof(DateTimeOffset).GetProperty(nameof(DateTimeOffset.Year))!, typeof(int), logger));
            var fractional = new[] { nameof(DateTime.Millisecond), nameof(DateTime.Microsecond), nameof(DateTime.Nanosecond) };
            foreach (var name in fractional)
            {
                var result = Assert.IsType<SqlFunctionExpression>(members.Translate(
                    factory.Constant(DateTime.UtcNow, source.FindMapping(typeof(DateTime))!),
                    typeof(DateTime).GetProperty(name)!, typeof(int), logger));
                Assert.Equal(OceanBaseTemporalMemberTranslator.ExtractMarker, result.Name);
                Assert.Equal(typeof(int), result.Type);
                Assert.Equal(typeof(int), result.TypeMapping!.ClrType);
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Fractional_temporal_members_translate_in_projection_and_predicate(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var sql = context.Rows
                .Where(row => row.Stamp.Millisecond >= 123 && row.Clock.Nanosecond == 700)
                .Select(row => new
                {
                    StampMilliseconds = row.Stamp.Millisecond,
                    StampMicroseconds = row.Stamp.Microsecond,
                    StampNanoseconds = row.Stamp.Nanosecond,
                    ClockMilliseconds = row.Clock.Millisecond,
                    ClockMicroseconds = row.Clock.Microsecond,
                    ClockNanoseconds = row.Clock.Nanosecond
                }).ToQueryString();

            Assert.DoesNotContain("__oceanbase_internal", sql);
            Assert.Equal(sql.Count(c => c == '('), sql.Count(c => c == ')'));
            if (mode == OceanBaseCompatibilityMode.MySql)
            {
                Assert.Contains("MICROSECOND", sql);
                Assert.Contains("DIV 1000", sql);
                Assert.Contains("MOD(MICROSECOND", sql);
            }
            else
            {
                Assert.Contains("EXTRACT(SECOND FROM", sql);
                Assert.Contains("* 1000", sql);
                Assert.Contains("* 1000000", sql);
                Assert.Contains("* 1000000000", sql);
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Non_temporal_converted_carriers_do_not_translate(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            IQueryable[] unsupported =
            [
                context.Rows.Where(row => row.TextStamp.Year == 2024),
                context.Rows.Where(row => row.TextStamp.Millisecond == 1),
                context.Rows.Where(row => row.TextDate.Month == 2),
                context.Rows.Where(row => row.TextClock.Second == 59),
                context.Rows.Where(row => row.TextClock.Nanosecond == 100),
                context.Rows.Where(row => row.Zoned.Year == 2024)
            ];
            foreach (var query in unsupported)
                Assert.Contains("could not be translated", Assert.Throws<InvalidOperationException>(() => query.ToQueryString()).Message);
            Assert.Contains("TextStamp", context.Rows.Select(row => row.TextStamp.Year).ToQueryString());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Numeric_and_binary_provider_carriers_do_not_translate(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var source = context.GetService<IRelationalTypeMappingSource>();
            var factory = context.GetService<ISqlExpressionFactory>();
            var members = context.GetService<IMemberTranslatorProvider>();
            var logger = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>();
            var numeric = (RelationalTypeMapping)source.FindMapping(typeof(long))!.WithComposedConverter(
                new ValueConverter<DateTime, long>(value => value.Ticks, value => new DateTime(value)));
            var binary = (RelationalTypeMapping)source.FindMapping(typeof(byte[]))!.WithComposedConverter(
                new ValueConverter<DateTime, byte[]>(value => BitConverter.GetBytes(value.Ticks),
                    value => new DateTime(BitConverter.ToInt64(value))));
            foreach (var mapping in new[] { numeric, binary })
                Assert.Null(members.Translate(factory.Constant(new DateTime(2024, 2, 29), mapping),
                    typeof(DateTime).GetProperty(nameof(DateTime.Year))!, typeof(int), logger));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Midnight_and_endpoint_inputs_keep_temporal_part_sql(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var first = DateOnly.MinValue;
            var last = DateOnly.MaxValue;
            var midnight = TimeOnly.MinValue;
            var endOfDay = mode == OceanBaseCompatibilityMode.Oracle
                ? TimeOnly.MaxValue : new TimeOnly(TimeOnly.MaxValue.Ticks - 9);
            var sql = context.Rows.Where(row => row.Date >= first && row.Date <= last
                    && row.Clock >= midnight && row.Clock <= endOfDay)
                .Select(row => new { row.Date.Year, row.Date.Day, row.Clock.Hour, row.Clock.Second })
                .ToQueryString();
            Assert.Contains(mode == OceanBaseCompatibilityMode.Oracle ? "EXTRACT(YEAR FROM" : "YEAR(", sql);
            Assert.Contains(mode == OceanBaseCompatibilityMode.Oracle ? "TRUNC(EXTRACT(SECOND FROM" : "SECOND(", sql);
            Assert.Contains("midnight", sql);
            Assert.Contains("endOfDay", sql);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Plugin_precedes_temporal_translator_and_provider_is_scoped(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            using var other = CreateContext(mode);
            var factory = context.GetService<ISqlExpressionFactory>();
            var source = context.GetService<IRelationalTypeMappingSource>();
            var logger = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>();
            var provider = new OceanBaseMemberTranslatorProvider(
                new RelationalMemberTranslatorProviderDependencies(factory, [new OverridePlugin(factory)]), source, mode);
            var input = factory.Constant(new DateTime(2024, 2, 29), source.FindMapping(typeof(DateTime)));
            Assert.Equal(31, Assert.IsType<SqlConstantExpression>(provider.Translate(input,
                typeof(DateTime).GetProperty(nameof(DateTime.Day))!, typeof(int), logger)).Value);
            Assert.NotSame(context.GetService<IMemberTranslatorProvider>(), other.GetService<IMemberTranslatorProvider>());
        }

        [Fact]
        public void Oracle_DATE_cast_applies_only_to_DateTime_clock_parts()
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.Oracle);
            var sql = context.Rows.Select(row => new
            {
                DateYear = row.DateStamp.Year, DateMonth = row.DateStamp.Month, DateDay = row.DateStamp.Day,
                DateHour = row.DateStamp.Hour, DateMinute = row.DateStamp.Minute, DateSecond = row.DateStamp.Second,
                TimestampHour = row.Stamp.Hour, CalendarDay = row.Date.Day, ClockHour = row.Clock.Hour
            }).ToQueryString();
            foreach (var unit in new[] { "YEAR", "MONTH", "DAY" })
                Assert.Contains($"EXTRACT({unit} FROM \"r\".\"DateStamp\")", sql);
            foreach (var unit in new[] { "HOUR", "MINUTE", "SECOND" })
                Assert.Contains($"EXTRACT({unit} FROM CAST(\"r\".\"DateStamp\" AS TIMESTAMP(7)))", sql);
            Assert.Contains("EXTRACT(HOUR FROM \"r\".\"Stamp\")", sql);
            Assert.Contains("EXTRACT(DAY FROM \"r\".\"Date\")", sql);
            Assert.Contains("EXTRACT(HOUR FROM \"r\".\"Clock\")", sql);
            Assert.DoesNotContain("__oceanbase_internal", sql);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Value_preserving_temporal_converter_keeps_its_input_carrier(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var source = context.GetService<IRelationalTypeMappingSource>();
            var factory = context.GetService<ISqlExpressionFactory>();
            var mapping = (RelationalTypeMapping)source.FindMapping(typeof(DateTime))!.WithComposedConverter(
                new ValueConverter<DateTime, DateTime>(value => new DateTime(value.Ticks), value => new DateTime(value.Ticks)));
            var input = factory.Constant(new DateTime(2024, 2, 29), mapping);
            var part = Assert.IsType<SqlFunctionExpression>(context.GetService<IMemberTranslatorProvider>().Translate(input,
                typeof(DateTime).GetProperty(nameof(DateTime.Year))!, typeof(int),
                context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>()));
            Assert.Same(mapping, part.Arguments![^1].TypeMapping);
            Assert.Null(part.TypeMapping!.Converter);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Gregorian_calendar_parts_translate_in_predicates_and_ordering(OceanBaseCompatibilityMode mode, bool relationalNulls)
        {
            using var context = CreateContext(mode, relationalNulls);
            var sql = context.Rows.Where(row => row.DateStamp.DayOfWeek == DayOfWeek.Sunday
                    && row.Date.DayOfYear == 60 && row.OptionalDate!.Value.DayOfWeek == DayOfWeek.Thursday)
                .OrderBy(row => row.Stamp.DayOfYear).Select(row => row.Date.DayOfWeek).ToQueryString();
            DayOfWeek? missingDay = null;
            var nullSql = context.Rows.Where(row => (DayOfWeek?)row.OptionalDate!.Value.DayOfWeek == missingDay).ToQueryString();
            if (mode == OceanBaseCompatibilityMode.Oracle)
            {
                Assert.Contains("(TRUNC(\"r\".\"Date\", 'DD') - TRUNC(\"r\".\"Date\", 'YYYY')) + 1", sql);
                Assert.Contains("MOD((TRUNC(\"r\".\"DateStamp\", 'DD') - TRUNC(\"r\".\"DateStamp\", 'IW')) + 1, 7)", sql);
                Assert.Contains("TRUNC(\"r\".\"Stamp\", 'YYYY')", sql);
                Assert.Contains("WHERE TRUNC(\"r\".\"OptionalDate\", 'DD') IS NULL OR TRUNC(\"r\".\"OptionalDate\", 'IW') IS NULL", nullSql);
            }
            else
            {
                Assert.Contains("DAYOFYEAR(`r`.`Date`)", sql);
                Assert.Contains("DAYOFYEAR(`r`.`Stamp`)", sql);
                Assert.Contains("DAYOFWEEK(`r`.`DateStamp`) - 1", sql);
                Assert.Contains("DAYOFWEEK(`r`.`OptionalDate`) - 1", sql);
                Assert.Contains("WHERE DAYOFWEEK(`r`.`OptionalDate`) IS NULL", nullSql);
            }
            Assert.DoesNotContain("TO_CHAR", sql);
            Assert.Throws<InvalidOperationException>(() => context.Rows.Where(row => row.TextStamp.DayOfWeek == DayOfWeek.Sunday).ToQueryString());
            Assert.Throws<InvalidOperationException>(() => context.Rows.Where(row => row.TextDate.DayOfYear == 60).ToQueryString());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void DateTime_Date_uses_date_carrier_and_preserves_supported_converter(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var sql = context.Rows.Select(row => new { Date = row.Stamp.Date, Optional = row.OptionalStamp!.Value.Date })
                .Where(row => row.Date == new DateTime(2024, 2, 29)).ToQueryString();
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "DATE(`r`.`Stamp`)" : "TRUNC(\"r\".\"Stamp\")", sql);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "DATE(`r`.`OptionalStamp`)" : "TRUNC(\"r\".\"OptionalStamp\")", sql);

            var source = context.GetService<IRelationalTypeMappingSource>();
            var factory = context.GetService<ISqlExpressionFactory>();
            var logger = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>();
            var converter = new ValueConverter<DateTime, DateTime>(value => value, value => value);
            var mapping = (RelationalTypeMapping)source.FindMapping(typeof(DateTime),
                mode == OceanBaseCompatibilityMode.Oracle ? "TIMESTAMP(3)" : "DATETIME(3)")!
                .WithComposedConverter(converter);
            var input = factory.Constant(new DateTime(2024, 2, 29, 23, 59, 59), mapping);
            var result = Assert.IsType<SqlFunctionExpression>(context.GetService<IMemberTranslatorProvider>().Translate(
                input, typeof(DateTime).GetProperty(nameof(DateTime.Date))!, typeof(DateTime), logger));
            Assert.Equal(mode == OceanBaseCompatibilityMode.MySql ? "DATE" : "TRUNC", result.Name);
            Assert.Same(mapping, Assert.Single(result.Arguments!).TypeMapping);
            Assert.Same(converter, result.TypeMapping!.Converter);
            Assert.Equal(mode == OceanBaseCompatibilityMode.MySql ? "DATETIME" : "DATE",
                result.TypeMapping.StoreTypeNameBase, ignoreCase: true);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void DateTime_Date_rejects_unowned_and_null_converting_carriers(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var source = context.GetService<IRelationalTypeMappingSource>();
            var factory = context.GetService<ISqlExpressionFactory>();
            var logger = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>();
            var members = context.GetService<IMemberTranslatorProvider>();
            var binary = (RelationalTypeMapping)source.FindMapping(typeof(byte[]))!.WithComposedConverter(
                new ValueConverter<DateTime, byte[]>(value => BitConverter.GetBytes(value.Ticks), value => new DateTime(BitConverter.ToInt64(value))));
            var nullConverter = (RelationalTypeMapping)source.FindMapping(typeof(DateTime))!.WithComposedConverter(
                new ValueConverter<DateTime, DateTime>(value => value, value => value, convertsNulls: true));
            Assert.Null(members.Translate(factory.Constant(DateTime.UtcNow, binary),
                typeof(DateTime).GetProperty(nameof(DateTime.Date))!, typeof(DateTime), logger));
            Assert.Null(members.Translate(factory.Constant(DateTime.UtcNow, nullConverter),
                typeof(DateTime).GetProperty(nameof(DateTime.Date))!, typeof(DateTime), logger));
            Assert.Null(members.Translate(factory.Constant(DateTimeOffset.UtcNow, source.FindMapping(typeof(DateTimeOffset))!),
                typeof(DateTimeOffset).GetProperty(nameof(DateTimeOffset.Date))!, typeof(DateTime), logger));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void DateTime_TimeOfDay_uses_the_existing_time_carrier(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var sql = context.Rows
                .Where(row => row.Stamp.TimeOfDay >= TimeSpan.FromHours(12))
                .Select(row => new { Stamp = row.Stamp.TimeOfDay, Date = row.DateStamp.TimeOfDay })
                .ToQueryString();

            Assert.DoesNotContain("__oceanbase_internal", sql);
            if (mode == OceanBaseCompatibilityMode.MySql)
            {
                Assert.Contains("TIME(`r`.`Stamp`)", sql);
                Assert.Contains("TIME(`r`.`DateStamp`)", sql);
            }
            else
            {
                Assert.Contains("(\"r\".\"Stamp\" - CAST(TRUNC(\"r\".\"Stamp\") AS TIMESTAMP))", sql);
                Assert.Contains("NUMTODSINTERVAL((\"r\".\"DateStamp\" - TRUNC(\"r\".\"DateStamp\")), 'DAY')", sql);
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void DateTime_TimeOfDay_rejects_unowned_and_null_converting_carriers(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var source = context.GetService<IRelationalTypeMappingSource>();
            var factory = context.GetService<ISqlExpressionFactory>();
            var members = context.GetService<IMemberTranslatorProvider>();
            var logger = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>();
            var timeOfDay = typeof(DateTime).GetProperty(nameof(DateTime.TimeOfDay))!;
            var binary = (RelationalTypeMapping)source.FindMapping(typeof(byte[]))!.WithComposedConverter(
                new ValueConverter<DateTime, byte[]>(value => BitConverter.GetBytes(value.Ticks),
                    value => new DateTime(BitConverter.ToInt64(value))));
            var nullConverter = (RelationalTypeMapping)source.FindMapping(typeof(DateTime))!.WithComposedConverter(
                new ValueConverter<DateTime, DateTime>(value => value, value => value, convertsNulls: true));

            Assert.Null(members.Translate(factory.Constant(DateTime.UtcNow, binary), timeOfDay, typeof(TimeSpan), logger));
            Assert.Null(members.Translate(factory.Constant(DateTime.UtcNow, nullConverter), timeOfDay, typeof(TimeSpan), logger));
            Assert.Null(members.Translate(factory.Constant(DateTimeOffset.UtcNow, source.FindMapping(typeof(DateTimeOffset))!),
                typeof(DateTimeOffset).GetProperty(nameof(DateTimeOffset.TimeOfDay))!, typeof(TimeSpan), logger));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void DateOnly_DayNumber_translates_in_projection_and_predicate(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var sql = context.Rows
                .Where(row => row.Date.DayNumber > 700000)
                .Select(row => row.Date.DayNumber)
                .ToQueryString();

            Assert.DoesNotContain("__oceanbase_internal", sql);
            if (mode == OceanBaseCompatibilityMode.MySql)
                Assert.Contains("DATEDIFF(`r`.`Date`, '0001-01-01')", sql);
            else
                Assert.Contains("TRUNC(\"r\".\"Date\") - DATE '0001-01-01'", sql);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void DateOnly_DayNumber_rejects_unowned_and_null_converting_carriers(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var source = context.GetService<IRelationalTypeMappingSource>();
            var factory = context.GetService<ISqlExpressionFactory>();
            var members = context.GetService<IMemberTranslatorProvider>();
            var logger = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>();
            var dayNumber = typeof(DateOnly).GetProperty(nameof(DateOnly.DayNumber))!;
            var text = (RelationalTypeMapping)source.FindMapping(typeof(string))!.WithComposedConverter(
                new ValueConverter<DateOnly, string>(value => value.ToString(), value => DateOnly.Parse(value)));
            var nullConverter = (RelationalTypeMapping)source.FindMapping(typeof(DateTime))!.WithComposedConverter(
                new ValueConverter<DateOnly, DateTime>(value => value.ToDateTime(TimeOnly.MinValue),
                    value => DateOnly.FromDateTime(value), convertsNulls: true));

            Assert.Null(members.Translate(factory.Constant(new DateOnly(2024, 2, 29), text), dayNumber, typeof(int), logger));
            Assert.Null(members.Translate(factory.Constant(new DateOnly(2024, 2, 29), nullConverter), dayNumber, typeof(int), logger));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void TimeSpan_total_members_translate_in_projection_and_predicate(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var query = context.Rows
                .Where(row => row.Duration.TotalDays > 1)
                .OrderBy(row => row.Duration.TotalNanoseconds)
                .Select(row => new
                {
                    row.Duration.TotalDays,
                    row.Duration.TotalHours,
                    row.Duration.TotalMinutes,
                    row.Duration.TotalSeconds,
                    row.Duration.TotalMilliseconds,
                    row.Duration.TotalMicroseconds,
                    row.Duration.TotalNanoseconds
                });

            var sql = query.ToQueryString();
            Assert.DoesNotContain("__oceanbase_internal", sql);
            Assert.Equal(sql.Count(c => c == '('), sql.Count(c => c == ')'));
            if (mode == OceanBaseCompatibilityMode.MySql)
            {
                Assert.Contains("TIME_TO_SEC", sql);
                Assert.Contains("MICROSECOND", sql);
            }
            else
            {
                Assert.Contains("EXTRACT(DAY FROM", sql);
                Assert.Contains("EXTRACT(SECOND FROM", sql);
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void TimeSpan_total_members_reject_null_converting_carriers(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var source = context.GetService<IRelationalTypeMappingSource>();
            var factory = context.GetService<ISqlExpressionFactory>();
            var members = context.GetService<IMemberTranslatorProvider>();
            var logger = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>();
            var mapping = mode == OceanBaseCompatibilityMode.MySql
                ? (RelationalTypeMapping)source.FindMapping(typeof(TimeSpan))!.WithComposedConverter(
                    new ValueConverter<TimeSpan, TimeSpan>(value => value, value => value, convertsNulls: true))
                : (RelationalTypeMapping)source.FindMapping(typeof(OceanBaseIntervalDayToSecond))!.WithComposedConverter(
                    new ValueConverter<TimeSpan, OceanBaseIntervalDayToSecond>(
                        value => new OceanBaseIntervalDayToSecond(false, 0, 0, 0, 0),
                        value => value.ToTimeSpan(), convertsNulls: true));

            Assert.Null(members.Translate(factory.Constant(TimeSpan.FromSeconds(1), mapping),
                typeof(TimeSpan).GetProperty(nameof(TimeSpan.TotalSeconds))!, typeof(double), logger));

            var text = (RelationalTypeMapping)source.FindMapping(typeof(string))!.WithComposedConverter(
                new ValueConverter<TimeSpan, string>(value => value.ToString(), value => TimeSpan.Parse(value)));
            Assert.Null(members.Translate(factory.Constant(TimeSpan.FromSeconds(1), text),
                typeof(TimeSpan).GetProperty(nameof(TimeSpan.TotalSeconds))!, typeof(double), logger));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void TimeSpan_Ticks_translate_with_100ns_precision(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var sql = context.Rows
                .Where(row => row.Duration.Ticks > TimeSpan.TicksPerDay)
                .OrderBy(row => row.Duration.Ticks)
                .Select(row => row.Duration.Ticks)
                .ToQueryString();

            Assert.DoesNotContain("__oceanbase_internal", sql);
            if (mode == OceanBaseCompatibilityMode.MySql)
            {
                Assert.Contains("TIME_TO_SEC", sql);
                Assert.Contains("MICROSECOND", sql);
                Assert.Contains("10000000", sql);
            }
            else
            {
                Assert.Contains("EXTRACT(DAY FROM", sql);
                Assert.Contains("EXTRACT(SECOND FROM", sql);
                Assert.Contains("864000000000", sql);
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void TimeSpan_Ticks_reject_non_interval_and_null_converting_carriers(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var source = context.GetService<IRelationalTypeMappingSource>();
            var factory = context.GetService<ISqlExpressionFactory>();
            var members = context.GetService<IMemberTranslatorProvider>();
            var logger = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>();
            var ticks = typeof(TimeSpan).GetProperty(nameof(TimeSpan.Ticks))!;
            var mapping = mode == OceanBaseCompatibilityMode.MySql
                ? (RelationalTypeMapping)source.FindMapping(typeof(TimeSpan))!.WithComposedConverter(
                    new ValueConverter<TimeSpan, TimeSpan>(value => value, value => value, convertsNulls: true))
                : (RelationalTypeMapping)source.FindMapping(typeof(OceanBaseIntervalDayToSecond))!.WithComposedConverter(
                    new ValueConverter<TimeSpan, OceanBaseIntervalDayToSecond>(
                        value => new OceanBaseIntervalDayToSecond(false, 0, 0, 0, 0),
                        value => value.ToTimeSpan(), convertsNulls: true));

            Assert.Null(members.Translate(factory.Constant(TimeSpan.FromSeconds(1), mapping), ticks, typeof(long), logger));

            var text = (RelationalTypeMapping)source.FindMapping(typeof(string))!.WithComposedConverter(
                new ValueConverter<TimeSpan, string>(value => value.ToString(), value => TimeSpan.Parse(value)));
            Assert.Null(members.Translate(factory.Constant(TimeSpan.FromSeconds(1), text), ticks, typeof(long), logger));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void TimeOnly_Ticks_translate_from_midnight_in_both_modes(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var sql = context.Rows
                .Where(row => row.Clock.Ticks > TimeSpan.TicksPerHour)
                .OrderBy(row => row.Clock.Ticks)
                .Select(row => row.Clock.Ticks)
                .ToQueryString();

            Assert.DoesNotContain("__oceanbase_internal", sql);
            if (mode == OceanBaseCompatibilityMode.MySql)
            {
                Assert.Contains("TIME_TO_SEC", sql);
                Assert.Contains("MICROSECOND", sql);
            }
            else
            {
                Assert.Contains("EXTRACT(HOUR FROM", sql);
                Assert.Contains("EXTRACT(SECOND FROM", sql);
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void DateTime_Ticks_translate_from_the_gregorian_epoch(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var sql = context.Rows
                .Where(row => row.Stamp.Ticks > TimeSpan.TicksPerDay)
                .OrderBy(row => row.Stamp.Ticks)
                .Select(row => row.Stamp.Ticks)
                .ToQueryString();

            Assert.DoesNotContain("__oceanbase_internal", sql);
            if (mode == OceanBaseCompatibilityMode.MySql)
            {
                Assert.Contains("DATEDIFF(`r`.`Stamp`, '0001-01-01')", sql);
                Assert.Contains("TIME_TO_SEC(TIME(`r`.`Stamp`))", sql);
                Assert.Contains("MICROSECOND(TIME(`r`.`Stamp`))", sql);
            }
            else
            {
                Assert.Contains("TRUNC(\"r\".\"Stamp\") - DATE '0001-01-01'", sql);
                Assert.Contains("EXTRACT(SECOND FROM", sql);
                var dateSql = context.Rows.Select(row => row.DateStamp.Ticks).ToQueryString();
                Assert.Contains("TRUNC((\"r\".\"DateStamp\" - TRUNC(\"r\".\"DateStamp\")) * 864000000000)", dateSql);
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void DateTime_Ticks_reject_unowned_and_null_converting_carriers(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var source = context.GetService<IRelationalTypeMappingSource>();
            var factory = context.GetService<ISqlExpressionFactory>();
            var members = context.GetService<IMemberTranslatorProvider>();
            var logger = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>();
            var ticks = typeof(DateTime).GetProperty(nameof(DateTime.Ticks))!;
            var binary = (RelationalTypeMapping)source.FindMapping(typeof(byte[]))!.WithComposedConverter(
                new ValueConverter<DateTime, byte[]>(value => BitConverter.GetBytes(value.Ticks),
                    value => new DateTime(BitConverter.ToInt64(value))));
            var nullConverter = (RelationalTypeMapping)source.FindMapping(typeof(DateTime))!.WithComposedConverter(
                new ValueConverter<DateTime, DateTime>(value => value, value => value, convertsNulls: true));

            Assert.Null(members.Translate(factory.Constant(DateTime.UtcNow, binary), ticks, typeof(long), logger));
            Assert.Null(members.Translate(factory.Constant(DateTime.UtcNow, nullConverter), ticks, typeof(long), logger));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void TimeSpan_component_members_translate_with_signed_duration_semantics(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var query = context.Rows
                .Where(row => row.Duration.Days != 0 && row.Duration.Milliseconds < 0)
                .OrderBy(row => row.Duration.Nanoseconds)
                .Select(row => new
                {
                    row.Duration.Days,
                    row.Duration.Hours,
                    row.Duration.Minutes,
                    row.Duration.Seconds,
                    row.Duration.Milliseconds,
                    row.Duration.Microseconds,
                    row.Duration.Nanoseconds
                });

            var sql = query.ToQueryString();
            Assert.DoesNotContain("__oceanbase_internal", sql);
            Assert.Equal(sql.Count(c => c == '('), sql.Count(c => c == ')'));
            if (mode == OceanBaseCompatibilityMode.MySql)
            {
                Assert.Contains("TIME_TO_SEC", sql);
                Assert.Contains("DIV 86400", sql);
                Assert.Contains("CAST(`r`.`Duration` AS CHAR)", sql);
                Assert.Contains("LIKE '-%'", sql);
                Assert.Contains("MICROSECOND", sql);
            }
            else
            {
                Assert.Contains("EXTRACT(DAY FROM", sql);
                Assert.Contains("EXTRACT(HOUR FROM", sql);
                Assert.Contains("EXTRACT(SECOND FROM", sql);
                Assert.Contains("* 1000", sql);
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void TimeSpan_component_members_reject_non_interval_carriers(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var source = context.GetService<IRelationalTypeMappingSource>();
            var factory = context.GetService<ISqlExpressionFactory>();
            var members = context.GetService<IMemberTranslatorProvider>();
            var logger = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>();
            var mapping = mode == OceanBaseCompatibilityMode.MySql
                ? (RelationalTypeMapping)source.FindMapping(typeof(TimeSpan))!.WithComposedConverter(
                    new ValueConverter<TimeSpan, TimeSpan>(value => value, value => value, convertsNulls: true))
                : (RelationalTypeMapping)source.FindMapping(typeof(OceanBaseIntervalDayToSecond))!.WithComposedConverter(
                    new ValueConverter<TimeSpan, OceanBaseIntervalDayToSecond>(
                        value => new OceanBaseIntervalDayToSecond(false, 0, 0, 0, 0),
                        value => value.ToTimeSpan(), convertsNulls: true));

            Assert.Null(members.Translate(factory.Constant(TimeSpan.FromSeconds(1), mapping),
                typeof(TimeSpan).GetProperty(nameof(TimeSpan.Hours))!, typeof(int), logger));

            var text = (RelationalTypeMapping)source.FindMapping(typeof(string))!.WithComposedConverter(
                new ValueConverter<TimeSpan, string>(value => value.ToString(), value => TimeSpan.Parse(value)));
            Assert.Null(members.Translate(factory.Constant(TimeSpan.FromSeconds(1), text),
                typeof(TimeSpan).GetProperty(nameof(TimeSpan.Milliseconds))!, typeof(int), logger));
        }

        private static TestContext CreateContext(OceanBaseCompatibilityMode mode, bool relationalNulls = false)
        {
            var builder = new DbContextOptionsBuilder<TestContext>().UseOceanBase("Server=localhost;Database=test", mode);
            ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(
                builder.Options.Extensions.OfType<RelationalOptionsExtension>().Single().WithUseRelationalNulls(relationalNulls));
            return new TestContext(builder.Options, mode);
        }

        private sealed class TestContext(DbContextOptions<TestContext> options, OceanBaseCompatibilityMode mode) : DbContext(options)
        {
            public DbSet<Row> Rows => Set<Row>();
            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity<Row>().Property(row => row.Stamp).HasPrecision(3);
                modelBuilder.Entity<Row>().Property(row => row.DateStamp).HasColumnType(
                    mode == OceanBaseCompatibilityMode.Oracle ? "DATE" : "DATETIME(0)");
                modelBuilder.Entity<Row>().Property(row => row.TextStamp).HasConversion(
                    value => value.ToString("O"), value => DateTime.Parse(value));
                modelBuilder.Entity<Row>().Property(row => row.TextDate).HasConversion(
                    value => value.ToString("O"), value => DateOnly.Parse(value));
                modelBuilder.Entity<Row>().Property(row => row.TextClock).HasConversion(
                    value => value.ToString("O"), value => TimeOnly.Parse(value));
            }
        }

        private sealed class OverridePlugin(ISqlExpressionFactory factory) : IMemberTranslatorPlugin, IMemberTranslator
        {
            public IEnumerable<IMemberTranslator> Translators => [this];
            public SqlExpression? Translate(SqlExpression? instance, MemberInfo member, Type returnType,
                IDiagnosticsLogger<DbLoggerCategory.Query> logger)
                => factory.Constant(31);
        }

        private sealed class Row
        {
            public int Id { get; set; }
            public DateTime Stamp { get; set; }
            public DateTime DateStamp { get; set; }
            public DateOnly Date { get; set; }
            public TimeOnly Clock { get; set; }
            public DateTime? OptionalStamp { get; set; }
            public DateOnly? OptionalDate { get; set; }
            public TimeOnly? OptionalClock { get; set; }
            public DateTime TextStamp { get; set; }
            public DateOnly TextDate { get; set; }
            public TimeOnly TextClock { get; set; }
            public DateTimeOffset Zoned { get; set; }
            public TimeSpan Duration { get; set; }
        }
    }
}
