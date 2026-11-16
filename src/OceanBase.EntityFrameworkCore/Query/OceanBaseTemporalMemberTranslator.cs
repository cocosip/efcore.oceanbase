using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBaseConnector;

namespace OceanBase.EntityFrameworkCore.Query
{
    internal sealed class OceanBaseTemporalMemberTranslator(
        ISqlExpressionFactory factory, IRelationalTypeMappingSource mappingSource,
        OceanBaseCompatibilityMode mode) : IMemberTranslator
    {
        internal const string ExtractMarker = "__oceanbase_internal_extract";
        internal const string TotalMarker = "__oceanbase_internal_total";
        internal const string TimeOfDayMarker = "__oceanbase_internal_time_of_day";
        internal const string DayNumberMarker = "__oceanbase_internal_day_number";
        internal const string TicksMarker = "__oceanbase_internal_ticks";
        internal const string DateTimeTicksMarker = "__oceanbase_internal_datetime_ticks";
        internal const string DateTimeNowMarker = "__oceanbase_internal_datetime_now";
        internal const string DateTimeUtcNowMarker = "__oceanbase_internal_datetime_utc_now";
        internal const string DateTimeTodayMarker = "__oceanbase_internal_datetime_today";
        internal const string DateTimeOffsetUtcDateTimeMarker = "__oceanbase_internal_datetimeoffset_utc_datetime";
        internal const string DateTimeOffsetUtcNowMarker = "__oceanbase_internal_datetimeoffset_utc_now";

        private static readonly Dictionary<MemberInfo, (Type Receiver, string Unit, string MySqlFunction)> Parts = new()
        {
            [typeof(DateTime).GetProperty(nameof(DateTime.Year))!] = (typeof(DateTime), "YEAR", "YEAR"),
            [typeof(DateTime).GetProperty(nameof(DateTime.Month))!] = (typeof(DateTime), "MONTH", "MONTH"),
            [typeof(DateTime).GetProperty(nameof(DateTime.Day))!] = (typeof(DateTime), "DAY", "DAYOFMONTH"),
            [typeof(DateTime).GetProperty(nameof(DateTime.Hour))!] = (typeof(DateTime), "HOUR", "HOUR"),
            [typeof(DateTime).GetProperty(nameof(DateTime.Minute))!] = (typeof(DateTime), "MINUTE", "MINUTE"),
            [typeof(DateTime).GetProperty(nameof(DateTime.Second))!] = (typeof(DateTime), "SECOND", "SECOND"),
            [typeof(DateTime).GetProperty(nameof(DateTime.Millisecond))!] = (typeof(DateTime), "MILLISECOND", "MICROSECOND"),
            [typeof(DateTime).GetProperty(nameof(DateTime.Microsecond))!] = (typeof(DateTime), "MICROSECOND", "MICROSECOND"),
            [typeof(DateTime).GetProperty(nameof(DateTime.Nanosecond))!] = (typeof(DateTime), "NANOSECOND", "MICROSECOND"),
            [typeof(DateTimeOffset).GetProperty(nameof(DateTimeOffset.Year))!] = (typeof(DateTimeOffset), "YEAR", "YEAR"),
            [typeof(DateTimeOffset).GetProperty(nameof(DateTimeOffset.Month))!] = (typeof(DateTimeOffset), "MONTH", "MONTH"),
            [typeof(DateTimeOffset).GetProperty(nameof(DateTimeOffset.Day))!] = (typeof(DateTimeOffset), "DAY", "DAYOFMONTH"),
            [typeof(DateTimeOffset).GetProperty(nameof(DateTimeOffset.Hour))!] = (typeof(DateTimeOffset), "HOUR", "HOUR"),
            [typeof(DateTimeOffset).GetProperty(nameof(DateTimeOffset.Minute))!] = (typeof(DateTimeOffset), "MINUTE", "MINUTE"),
            [typeof(DateTimeOffset).GetProperty(nameof(DateTimeOffset.Second))!] = (typeof(DateTimeOffset), "SECOND", "SECOND"),
            [typeof(DateTimeOffset).GetProperty(nameof(DateTimeOffset.Millisecond))!] = (typeof(DateTimeOffset), "MILLISECOND", "MICROSECOND"),
            [typeof(DateTimeOffset).GetProperty(nameof(DateTimeOffset.Microsecond))!] = (typeof(DateTimeOffset), "MICROSECOND", "MICROSECOND"),
            [typeof(DateTimeOffset).GetProperty(nameof(DateTimeOffset.Nanosecond))!] = (typeof(DateTimeOffset), "NANOSECOND", "MICROSECOND"),
            [typeof(DateOnly).GetProperty(nameof(DateOnly.Year))!] = (typeof(DateOnly), "YEAR", "YEAR"),
            [typeof(DateOnly).GetProperty(nameof(DateOnly.Month))!] = (typeof(DateOnly), "MONTH", "MONTH"),
            [typeof(DateOnly).GetProperty(nameof(DateOnly.Day))!] = (typeof(DateOnly), "DAY", "DAYOFMONTH"),
            [typeof(TimeOnly).GetProperty(nameof(TimeOnly.Hour))!] = (typeof(TimeOnly), "HOUR", "HOUR"),
            [typeof(TimeOnly).GetProperty(nameof(TimeOnly.Minute))!] = (typeof(TimeOnly), "MINUTE", "MINUTE"),
            [typeof(TimeOnly).GetProperty(nameof(TimeOnly.Second))!] = (typeof(TimeOnly), "SECOND", "SECOND"),
            [typeof(TimeOnly).GetProperty(nameof(TimeOnly.Millisecond))!] = (typeof(TimeOnly), "MILLISECOND", "MICROSECOND"),
            [typeof(TimeOnly).GetProperty(nameof(TimeOnly.Microsecond))!] = (typeof(TimeOnly), "MICROSECOND", "MICROSECOND"),
            [typeof(TimeOnly).GetProperty(nameof(TimeOnly.Nanosecond))!] = (typeof(TimeOnly), "NANOSECOND", "MICROSECOND"),
            [typeof(TimeSpan).GetProperty(nameof(TimeSpan.Days))!] = (typeof(TimeSpan), "DAY", "DAY"),
            [typeof(TimeSpan).GetProperty(nameof(TimeSpan.Hours))!] = (typeof(TimeSpan), "HOUR", "HOUR"),
            [typeof(TimeSpan).GetProperty(nameof(TimeSpan.Minutes))!] = (typeof(TimeSpan), "MINUTE", "MINUTE"),
            [typeof(TimeSpan).GetProperty(nameof(TimeSpan.Seconds))!] = (typeof(TimeSpan), "SECOND", "SECOND"),
            [typeof(TimeSpan).GetProperty(nameof(TimeSpan.Milliseconds))!] = (typeof(TimeSpan), "MILLISECOND", "MICROSECOND"),
            [typeof(TimeSpan).GetProperty(nameof(TimeSpan.Microseconds))!] = (typeof(TimeSpan), "MICROSECOND", "MICROSECOND"),
            [typeof(TimeSpan).GetProperty(nameof(TimeSpan.Nanoseconds))!] = (typeof(TimeSpan), "NANOSECOND", "MICROSECOND")
        };

        private static readonly Dictionary<MemberInfo, bool> CalendarParts = new()
        {
            [typeof(DateTime).GetProperty(nameof(DateTime.DayOfYear))!] = false,
            [typeof(DateOnly).GetProperty(nameof(DateOnly.DayOfYear))!] = false,
            [typeof(DateTime).GetProperty(nameof(DateTime.DayOfWeek))!] = true,
            [typeof(DateTimeOffset).GetProperty(nameof(DateTimeOffset.DayOfYear))!] = false,
            [typeof(DateTimeOffset).GetProperty(nameof(DateTimeOffset.DayOfWeek))!] = true,
            [typeof(DateOnly).GetProperty(nameof(DateOnly.DayOfWeek))!] = true
        };
        private static readonly Dictionary<MemberInfo, string> TotalMembers = new()
        {
            [typeof(TimeSpan).GetProperty(nameof(TimeSpan.TotalDays))!] = "DAY",
            [typeof(TimeSpan).GetProperty(nameof(TimeSpan.TotalHours))!] = "HOUR",
            [typeof(TimeSpan).GetProperty(nameof(TimeSpan.TotalMinutes))!] = "MINUTE",
            [typeof(TimeSpan).GetProperty(nameof(TimeSpan.TotalSeconds))!] = "SECOND",
            [typeof(TimeSpan).GetProperty(nameof(TimeSpan.TotalMilliseconds))!] = "MILLISECOND",
            [typeof(TimeSpan).GetProperty(nameof(TimeSpan.TotalMicroseconds))!] = "MICROSECOND",
            [typeof(TimeSpan).GetProperty(nameof(TimeSpan.TotalNanoseconds))!] = "NANOSECOND"
        };
        private static readonly MemberInfo DateTimeDate = typeof(DateTime).GetProperty(nameof(DateTime.Date))!;
        private static readonly MemberInfo DateTimeOffsetDate = typeof(DateTimeOffset).GetProperty(nameof(DateTimeOffset.Date))!;
        private static readonly MemberInfo DateTimeTimeOfDay = typeof(DateTime).GetProperty(nameof(DateTime.TimeOfDay))!;
        private static readonly MemberInfo DateTimeOffsetTimeOfDay = typeof(DateTimeOffset).GetProperty(nameof(DateTimeOffset.TimeOfDay))!;
        private static readonly MemberInfo DateOnlyDayNumber = typeof(DateOnly).GetProperty(nameof(DateOnly.DayNumber))!;
        private static readonly MemberInfo TimeSpanTicks = typeof(TimeSpan).GetProperty(nameof(TimeSpan.Ticks))!;
        private static readonly MemberInfo TimeOnlyTicks = typeof(TimeOnly).GetProperty(nameof(TimeOnly.Ticks))!;
        private static readonly MemberInfo DateTimeTicks = typeof(DateTime).GetProperty(nameof(DateTime.Ticks))!;
        private static readonly MemberInfo DateTimeOffsetUtcTicks = typeof(DateTimeOffset).GetProperty(nameof(DateTimeOffset.UtcTicks))!;
        private static readonly MemberInfo DateTimeNow = typeof(DateTime).GetProperty(nameof(DateTime.Now))!;
        private static readonly MemberInfo DateTimeUtcNow = typeof(DateTime).GetProperty(nameof(DateTime.UtcNow))!;
        private static readonly MemberInfo DateTimeToday = typeof(DateTime).GetProperty(nameof(DateTime.Today))!;
        private static readonly MemberInfo DateTimeOffsetUtcDateTime = typeof(DateTimeOffset).GetProperty(nameof(DateTimeOffset.UtcDateTime))!;
        private static readonly MemberInfo DateTimeOffsetUtcNow = typeof(DateTimeOffset).GetProperty(nameof(DateTimeOffset.UtcNow))!;

        public SqlExpression? Translate(SqlExpression? instance, MemberInfo member, Type returnType,
            IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        {
            if (instance is not null && (member == DateTimeDate || member == DateTimeOffsetDate)
                && returnType == typeof(DateTime))
            {
                instance = factory.ApplyDefaultTypeMapping(instance);
                var dateMapping = instance.TypeMapping;
                if (dateMapping is null || !SupportsDate(member.DeclaringType!, dateMapping)
                    || dateMapping.Converter?.ConvertsNulls == true)
                    return null;

                var resultMapping = (RelationalTypeMapping)mappingSource.FindMapping(typeof(DateTime),
                    mode == OceanBaseCompatibilityMode.MySql ? "DATETIME(0)" : "DATE")!;
                if (member == DateTimeDate && dateMapping.Converter is { } converter)
                    resultMapping = (RelationalTypeMapping)resultMapping.WithComposedConverter(converter);
                return factory.Function(mode == OceanBaseCompatibilityMode.MySql ? "DATE" : "TRUNC", [instance],
                    nullable: true, argumentsPropagateNullability: [false], typeof(DateTime), resultMapping);
            }

            if (instance is not null && (member == DateTimeTimeOfDay || member == DateTimeOffsetTimeOfDay)
                && returnType == typeof(TimeSpan))
            {
                instance = factory.ApplyDefaultTypeMapping(instance);
                var dateMapping = instance.TypeMapping;
                if (dateMapping is null || !SupportsCarrier(member.DeclaringType!, dateMapping)
                    || dateMapping.Converter?.ConvertsNulls == true)
                    return null;

                return factory.Function(TimeOfDayMarker, [instance], nullable: true,
                    argumentsPropagateNullability: [false], typeof(TimeSpan), mappingSource.FindMapping(typeof(TimeSpan)));
            }

            if (instance is not null && member == DateTimeOffsetUtcDateTime
                && returnType == typeof(DateTime))
            {
                instance = factory.ApplyDefaultTypeMapping(instance);
                var offsetMapping = instance.TypeMapping;
                if (offsetMapping is null || offsetMapping.Converter?.ConvertsNulls == true
                    || !SupportsDate(typeof(DateTimeOffset), offsetMapping))
                    return null;

                var storeType = mode == OceanBaseCompatibilityMode.MySql ? "DATETIME(6)" : "TIMESTAMP(7)";
                return factory.Function(DateTimeOffsetUtcDateTimeMarker, [instance], nullable: true,
                    argumentsPropagateNullability: [false], typeof(DateTime), mappingSource.FindMapping(typeof(DateTime), storeType));
            }

            if (instance is null && member == DateTimeOffsetUtcNow && returnType == typeof(DateTimeOffset))
            {
                var storeType = mode == OceanBaseCompatibilityMode.MySql ? "DATETIME(6)" : "TIMESTAMP(7) WITH TIME ZONE";
                return factory.Function(DateTimeOffsetUtcNowMarker, [], nullable: false,
                    argumentsPropagateNullability: [], typeof(DateTimeOffset),
                    mappingSource.FindMapping(typeof(DateTimeOffset), storeType));
            }

            if (instance is not null && member == DateOnlyDayNumber && instance.Type == typeof(DateOnly)
                && returnType == typeof(int))
            {
                instance = factory.ApplyDefaultTypeMapping(instance);
                var dateMapping = instance.TypeMapping;
                if (dateMapping is null || dateMapping.Converter?.ConvertsNulls == true
                    || !SupportsCarrier(typeof(DateOnly), dateMapping))
                    return null;

                return factory.Function(DayNumberMarker, [instance], nullable: true,
                    argumentsPropagateNullability: [false], typeof(int), mappingSource.FindMapping(typeof(int)));
            }

            if (instance is not null && member == TimeSpanTicks && instance.Type == typeof(TimeSpan)
                && returnType == typeof(long))
            {
                instance = factory.ApplyDefaultTypeMapping(instance);
                var durationMapping = instance.TypeMapping;
                if (durationMapping is null || durationMapping.Converter?.ConvertsNulls == true
                    || !SupportsCarrier(typeof(TimeSpan), durationMapping))
                    return null;

                return factory.Function(TicksMarker, [instance], nullable: true,
                    argumentsPropagateNullability: [false], typeof(long), mappingSource.FindMapping(typeof(long)));
            }

            if (instance is not null && member == TimeOnlyTicks && instance.Type == typeof(TimeOnly)
                && returnType == typeof(long))
            {
                instance = factory.ApplyDefaultTypeMapping(instance);
                var clockMapping = instance.TypeMapping;
                if (clockMapping is null || clockMapping.Converter?.ConvertsNulls == true
                    || !SupportsCarrier(typeof(TimeOnly), clockMapping))
                    return null;

                return factory.Function(TicksMarker, [instance], nullable: true,
                    argumentsPropagateNullability: [false], typeof(long), mappingSource.FindMapping(typeof(long)));
            }

            if (instance is not null && member == DateTimeTicks && instance.Type == typeof(DateTime)
                && returnType == typeof(long))
            {
                instance = factory.ApplyDefaultTypeMapping(instance);
                var dateMapping = instance.TypeMapping;
                if (dateMapping is null || dateMapping.Converter?.ConvertsNulls == true
                    || !SupportsDate(typeof(DateTime), dateMapping))
                    return null;

                return factory.Function(DateTimeTicksMarker, [instance], nullable: true,
                    argumentsPropagateNullability: [false], typeof(long), mappingSource.FindMapping(typeof(long)));
            }

            if (instance is not null && member == DateTimeOffsetUtcTicks && instance.Type == typeof(DateTimeOffset)
                && returnType == typeof(long))
            {
                instance = factory.ApplyDefaultTypeMapping(instance);
                var offsetMapping = instance.TypeMapping;
                if (offsetMapping is null || offsetMapping.Converter?.ConvertsNulls == true
                    || !SupportsDate(typeof(DateTimeOffset), offsetMapping))
                    return null;

                var storeType = mode == OceanBaseCompatibilityMode.MySql ? "DATETIME(6)" : "TIMESTAMP(7)";
                var utcDateTime = factory.Function(DateTimeOffsetUtcDateTimeMarker, [instance], nullable: true,
                    argumentsPropagateNullability: [false], typeof(DateTime), mappingSource.FindMapping(typeof(DateTime), storeType));
                return factory.Function(DateTimeTicksMarker, [utcDateTime], nullable: true,
                    argumentsPropagateNullability: [false], typeof(long), mappingSource.FindMapping(typeof(long)));
            }

            if (instance is null && returnType == typeof(DateTime)
                && member is MemberInfo clock
                && (clock == DateTimeNow || clock == DateTimeUtcNow || clock == DateTimeToday))
            {
                var storeType = clock == DateTimeToday
                    ? mode == OceanBaseCompatibilityMode.MySql ? "DATETIME(0)" : "DATE"
                    : mode == OceanBaseCompatibilityMode.MySql ? "DATETIME(6)" : "TIMESTAMP(7)";
                var marker = clock == DateTimeNow ? DateTimeNowMarker
                    : clock == DateTimeUtcNow ? DateTimeUtcNowMarker : DateTimeTodayMarker;
                return factory.Function(marker, [], nullable: false, argumentsPropagateNullability: [],
                    typeof(DateTime), mappingSource.FindMapping(typeof(DateTime), storeType));
            }

            if (instance is not null && CalendarParts.TryGetValue(member, out var weekday)
                && returnType == (weekday ? typeof(DayOfWeek) : typeof(int)))
            {
                instance = factory.ApplyDefaultTypeMapping(instance);
                return instance.TypeMapping is { } calendarMapping && SupportsCarrier(member.DeclaringType!, calendarMapping)
                    ? TranslateCalendarPart(instance, weekday)
                    : null;
            }

            if (instance is not null && returnType == typeof(double)
                && member.DeclaringType == typeof(TimeSpan) && TotalMembers.TryGetValue(member, out var totalUnit))
            {
                instance = factory.ApplyDefaultTypeMapping(instance);
                var totalMapping = instance.TypeMapping;
                if (totalMapping is null || totalMapping.Converter?.ConvertsNulls == true
                    || !SupportsCarrier(typeof(TimeSpan), totalMapping))
                    return null;

                return factory.Function(TotalMarker, [factory.Fragment(totalUnit), instance], nullable: true,
                    argumentsPropagateNullability: [false, false], typeof(double), mappingSource.FindMapping(typeof(double)));
            }

            if (instance is null || returnType != typeof(int) || member is not PropertyInfo { PropertyType: var propertyType }
                || propertyType != typeof(int) || !Parts.TryGetValue(member, out var part)
                || member.DeclaringType != part.Receiver)
                return null;

            instance = factory.ApplyDefaultTypeMapping(instance);
            var mapping = instance.TypeMapping;
            if (mapping is null || mapping.Converter?.ConvertsNulls == true || !SupportsCarrier(part.Receiver, mapping))
                return null;

            var intMapping = mappingSource.FindMapping(typeof(int))!;
            if (part.Receiver == typeof(TimeSpan))
            {
                return factory.Function(ExtractMarker, [factory.Fragment(part.Unit), instance], nullable: true,
                    argumentsPropagateNullability: [false, false], typeof(int), intMapping);
            }

            if (mode == OceanBaseCompatibilityMode.MySql && part.Unit is not ("MILLISECOND" or "MICROSECOND" or "NANOSECOND"))
                return factory.Function(part.MySqlFunction, [instance], nullable: true,
                    argumentsPropagateNullability: [false], typeof(int), intMapping);

            if (part.Receiver == typeof(DateTime) && part.Unit is "HOUR" or "MINUTE" or "SECOND"
                && mapping.StoreTypeNameBase.Trim().Equals("DATE", StringComparison.OrdinalIgnoreCase))
                instance = factory.Convert(instance, typeof(DateTime), mappingSource.FindMapping(typeof(DateTime)));

            var fractional = part.Unit == "SECOND";
            var extracted = factory.Function(ExtractMarker, [factory.Fragment(part.Unit), instance], nullable: true,
                argumentsPropagateNullability: [false, false], fractional ? typeof(decimal) : typeof(int),
                fractional ? mappingSource.FindMapping(typeof(decimal)) : intMapping);
            return fractional
                ? factory.Function("TRUNC", [extracted], nullable: true,
                    argumentsPropagateNullability: [false], typeof(int), intMapping)
                : extracted;
        }

        private SqlExpression TranslateCalendarPart(SqlExpression instance, bool weekday)
        {
            var intMapping = mappingSource.FindMapping(typeof(int))!;
            var one = factory.Constant(1, intMapping);
            if (mode == OceanBaseCompatibilityMode.MySql)
            {
                var part = factory.Function(weekday ? "DAYOFWEEK" : "DAYOFYEAR", [instance], nullable: true,
                    argumentsPropagateNullability: [false], typeof(int), intMapping);
                return weekday ? factory.Subtract(part, one, intMapping) : part;
            }

            var dateMapping = mappingSource.FindMapping(typeof(DateTime), "DATE")!;
            SqlExpression Truncate(string formatLiteral) => factory.Function("TRUNC", [instance, factory.Fragment(formatLiteral)],
                nullable: true, argumentsPropagateNullability: [false, false], typeof(DateTime), dateMapping);
            // Oracle DATE subtraction returns a number of days, independent of the operand mapping.
            var elapsed = new SqlBinaryExpression(ExpressionType.Subtract, Truncate("'DD'"),
                Truncate(weekday ? "'IW'" : "'YYYY'"), typeof(int), intMapping);
            var shifted = factory.Add(elapsed, one, intMapping);
            return weekday ? factory.Modulo(shifted, factory.Constant(7, intMapping), intMapping) : shifted;
        }

        private bool SupportsCarrier(Type receiver, RelationalTypeMapping mapping)
        {
            var store = mapping.StoreTypeNameBase.Trim().ToUpperInvariant();
            var providerType = mapping.Converter?.ProviderClrType ?? receiver;
            return receiver == typeof(DateTime)
                ? providerType == typeof(DateTime) && (mode == OceanBaseCompatibilityMode.MySql
                    ? store == "DATETIME" : store is "DATE" or "TIMESTAMP")
                : receiver == typeof(DateTimeOffset)
                    ? (mode == OceanBaseCompatibilityMode.MySql
                        ? providerType == typeof(DateTime) && store == "DATETIME"
                        : providerType == typeof(DateTimeOffset) && store == "TIMESTAMP")
                : receiver == typeof(DateOnly)
                    ? providerType == typeof(DateTime) && store == "DATE"
                    : mode == OceanBaseCompatibilityMode.MySql
                        ? providerType == typeof(TimeSpan) && store == "TIME"
                        : providerType == typeof(OceanBaseIntervalDayToSecond) && store.StartsWith("INTERVAL DAY", StringComparison.Ordinal);
        }

        private bool SupportsDate(Type receiver, RelationalTypeMapping mapping)
        {
            var store = mapping.StoreTypeNameBase.Trim().ToUpperInvariant();
            var providerType = mapping.Converter?.ProviderClrType ?? mapping.ClrType;
            return receiver == typeof(DateTime)
                ? providerType == typeof(DateTime) && (store is "DATE" or "DATETIME" or "TIMESTAMP")
                : receiver == typeof(DateTimeOffset) && (mode == OceanBaseCompatibilityMode.MySql
                    ? providerType == typeof(DateTime) && store == "DATETIME"
                    : providerType == typeof(DateTimeOffset) && store == "TIMESTAMP");
        }
    }
}
