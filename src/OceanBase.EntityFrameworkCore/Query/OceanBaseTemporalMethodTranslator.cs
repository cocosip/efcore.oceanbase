using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBaseConnector;
using OceanBase.EntityFrameworkCore.Storage;

namespace OceanBase.EntityFrameworkCore.Query
{
    internal sealed class OceanBaseTemporalMethodTranslator(
        ISqlExpressionFactory sqlExpressionFactory, IRelationalTypeMappingSource mappingSource,
        OceanBaseCompatibilityMode mode) : IMethodCallTranslator
    {
        internal const string DateOnlyAddDaysMarker = "__oceanbase_internal_dateonly_add_days";
        internal const string DateOnlyAddMonthsMarker = "__oceanbase_internal_dateonly_add_months";
        internal const string DateOnlyAddYearsMarker = "__oceanbase_internal_dateonly_add_years";
        internal const string DateTimeAddMonthsMarker = "__oceanbase_internal_datetime_add_months";
        internal const string DateTimeAddYearsMarker = "__oceanbase_internal_datetime_add_years";
        internal const string DateTimeAddDurationMarker = "__oceanbase_internal_datetime_add_duration";
        internal const string DateTimeSubtractDurationMarker = "__oceanbase_internal_datetime_subtract_duration";
        internal const string DateTimeAddDaysMarker = "__oceanbase_internal_datetime_add_days";
        internal const string DateTimeAddHoursMarker = "__oceanbase_internal_datetime_add_hours";
        internal const string DateTimeAddMinutesMarker = "__oceanbase_internal_datetime_add_minutes";
        internal const string DateTimeAddSecondsMarker = "__oceanbase_internal_datetime_add_seconds";
        internal const string DateTimeAddMillisecondsMarker = "__oceanbase_internal_datetime_add_milliseconds";
        internal const string DateTimeAddMicrosecondsMarker = "__oceanbase_internal_datetime_add_microseconds";
        internal const string DateTimeAddTicksMarker = "__oceanbase_internal_datetime_add_ticks";
        private static readonly MethodInfo DateOnlyAddDays = typeof(DateOnly).GetMethod(nameof(DateOnly.AddDays), [typeof(int)])!;
        private static readonly MethodInfo DateOnlyAddMonths = typeof(DateOnly).GetMethod(nameof(DateOnly.AddMonths), [typeof(int)])!;
        private static readonly MethodInfo DateOnlyAddYears = typeof(DateOnly).GetMethod(nameof(DateOnly.AddYears), [typeof(int)])!;
        private static readonly MethodInfo DateTimeAddMonths = typeof(DateTime).GetMethod(nameof(DateTime.AddMonths), [typeof(int)])!;
        private static readonly MethodInfo DateTimeAddYears = typeof(DateTime).GetMethod(nameof(DateTime.AddYears), [typeof(int)])!;
        private static readonly MethodInfo DateTimeAddDuration = typeof(DateTime).GetMethod(nameof(DateTime.Add), [typeof(TimeSpan)])!;
        private static readonly MethodInfo DateTimeSubtractDuration = typeof(DateTime).GetMethod(nameof(DateTime.Subtract), [typeof(TimeSpan)])!;
        private static readonly MethodInfo DateTimeAddDays = typeof(DateTime).GetMethod(nameof(DateTime.AddDays), [typeof(double)])!;
        private static readonly MethodInfo DateTimeAddHours = typeof(DateTime).GetMethod(nameof(DateTime.AddHours), [typeof(double)])!;
        private static readonly Dictionary<MethodInfo, string> NumericAddMarkers = new()
        {
            [DateTimeAddDays] = DateTimeAddDaysMarker,
            [DateTimeAddHours] = DateTimeAddHoursMarker,
            [typeof(DateTime).GetMethod(nameof(DateTime.AddMinutes), [typeof(double)])!] = DateTimeAddMinutesMarker,
            [typeof(DateTime).GetMethod(nameof(DateTime.AddSeconds), [typeof(double)])!] = DateTimeAddSecondsMarker,
            [typeof(DateTime).GetMethod(nameof(DateTime.AddMilliseconds), [typeof(double)])!] = DateTimeAddMillisecondsMarker,
            [typeof(DateTime).GetMethod(nameof(DateTime.AddMicroseconds), [typeof(double)])!] = DateTimeAddMicrosecondsMarker,
            [typeof(DateTime).GetMethod(nameof(DateTime.AddTicks), [typeof(long)])!] = DateTimeAddTicksMarker
        };

        public SqlExpression? Translate(SqlExpression? instance, MethodInfo method, IReadOnlyList<SqlExpression> arguments,
            IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        {
            var numericAdd = NumericAddMarkers.TryGetValue(method, out var numericMarker);
            var ticks = numericMarker == DateTimeAddTicksMarker;
            if (instance is not null && arguments.Count == 1 && (numericAdd || method == DateTimeAddDuration || method == DateTimeSubtractDuration))
            {
                instance = sqlExpressionFactory.ApplyDefaultTypeMapping(instance);
                var duration = sqlExpressionFactory.ApplyDefaultTypeMapping(arguments[0]);
                if (instance.Type != typeof(DateTime) || instance.TypeMapping is not { } stampMapping
                    || !IsDateTimeCalendarMapping(stampMapping, mode)
                    || duration.TypeMapping is not { } durationMapping
                    || (numericAdd ? ticks ? duration.Type != typeof(long) || !IsTickOffsetMapping(durationMapping, mode)
                        : duration.Type != typeof(double) || !IsNumericOffsetMapping(durationMapping, mode)
                        : duration.Type != typeof(TimeSpan) || !IsDurationMapping(durationMapping, mode)))
                    return null;

                // Arithmetic can add fractions finer than the input column, including Oracle DATE.
                var resultMapping = mappingSource.FindMapping(typeof(DateTime),
                    mode == OceanBaseCompatibilityMode.MySql ? "DATETIME(6)" : "TIMESTAMP(7)")!;
                if (stampMapping.Converter is { } converter)
                    resultMapping = (RelationalTypeMapping)resultMapping.WithComposedConverter(converter);
                var durationMarker = numericAdd ? numericMarker!
                    : method == DateTimeAddDuration ? DateTimeAddDurationMarker : DateTimeSubtractDurationMarker;
                return sqlExpressionFactory.Function(durationMarker,
                    [instance, duration], nullable: true, argumentsPropagateNullability: [false, false], typeof(DateTime), resultMapping);
            }

            var dateTime = method == DateTimeAddMonths || method == DateTimeAddYears;
            if (instance is null || (!dateTime && method != DateOnlyAddDays && method != DateOnlyAddMonths && method != DateOnlyAddYears)
                || arguments.Count != 1)
                return null;

            instance = sqlExpressionFactory.ApplyDefaultTypeMapping(instance);
            var days = sqlExpressionFactory.ApplyDefaultTypeMapping(arguments[0]);
            if (instance.Type != (dateTime ? typeof(DateTime) : typeof(DateOnly)) || days.Type != typeof(int)
                || instance.TypeMapping is not RelationalTypeMapping dateMapping
                || !(dateTime ? IsDateTimeCalendarMapping(dateMapping, mode) : IsDateOnlyMapping(dateMapping))
                || days.TypeMapping is not RelationalTypeMapping daysMapping
                || daysMapping.Converter?.ConvertsNulls == true
                || (daysMapping.Converter?.ProviderClrType ?? daysMapping.ClrType) != typeof(int))
                return null;

            var marker = dateTime
                ? method == DateTimeAddMonths ? DateTimeAddMonthsMarker : DateTimeAddYearsMarker
                : method == DateOnlyAddDays
                ? DateOnlyAddDaysMarker
                : method == DateOnlyAddMonths ? DateOnlyAddMonthsMarker : DateOnlyAddYearsMarker;
            return sqlExpressionFactory.Function(marker,
                [instance, days], nullable: true,
                argumentsPropagateNullability: [false, false], instance.Type, dateMapping);
        }

        internal static bool IsDateTimeCalendarMapping(RelationalTypeMapping mapping, OceanBaseCompatibilityMode mode)
        {
            var providerType = mapping.Converter?.ProviderClrType ?? mapping.ClrType;
            if ((Nullable.GetUnderlyingType(mapping.ClrType) ?? mapping.ClrType) != typeof(DateTime) || mapping.Converter?.ConvertsNulls == true
                || (Nullable.GetUnderlyingType(providerType) ?? providerType) != typeof(DateTime))
                return false;

            var store = mapping.StoreTypeNameBase.Trim().ToUpperInvariant();
            return mode == OceanBaseCompatibilityMode.MySql ? store == "DATETIME"
                : (store is "DATE" or "TIMESTAMP")
                    && !mapping.StoreType.Contains("TIME ZONE", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsDurationMapping(RelationalTypeMapping mapping, OceanBaseCompatibilityMode mode)
        {
            var providerType = mapping.Converter?.ProviderClrType ?? mapping.ClrType;
            if ((Nullable.GetUnderlyingType(mapping.ClrType) ?? mapping.ClrType) != typeof(TimeSpan)
                || mapping.Converter?.ConvertsNulls == true) return false;
            providerType = Nullable.GetUnderlyingType(providerType) ?? providerType;
            var store = mapping.StoreTypeNameBase.Trim().ToUpperInvariant();
            return mode == OceanBaseCompatibilityMode.MySql
                ? providerType == typeof(TimeSpan) && store == "TIME"
                : providerType == typeof(OceanBaseIntervalDayToSecond) && store.StartsWith("INTERVAL DAY", StringComparison.Ordinal);
        }

        internal static bool IsNumericOffsetMapping(RelationalTypeMapping mapping, OceanBaseCompatibilityMode mode)
            => mapping.ClrType == typeof(double) && mapping.Converter?.ConvertsNulls != true
                && (mapping.Converter?.ProviderClrType ?? mapping.ClrType) == typeof(double)
                && mapping.StoreTypeNameBase.Trim().Equals(mode == OceanBaseCompatibilityMode.MySql ? "DOUBLE" : "BINARY_DOUBLE",
                    StringComparison.OrdinalIgnoreCase);

        internal static bool IsTickOffsetMapping(RelationalTypeMapping mapping, OceanBaseCompatibilityMode mode)
            => mapping is OceanBaseIntegerTypeMapping<long> integer && integer.CompatibilityMode == mode
                && mapping.ClrType == typeof(long) && mapping.Converter?.ConvertsNulls != true
                && (mapping.Converter?.ProviderClrType ?? mapping.ClrType) == typeof(long);

        private bool IsDateOnlyMapping(RelationalTypeMapping mapping)
        {
            if (mapping.Converter?.ConvertsNulls == true || (Nullable.GetUnderlyingType(mapping.ClrType) ?? mapping.ClrType) != typeof(DateOnly)) return false;
            var providerType = mapping.Converter?.ProviderClrType ?? mapping.ClrType;
            providerType = Nullable.GetUnderlyingType(providerType) ?? providerType;
            return providerType == typeof(DateTime)
                && mapping.StoreTypeNameBase.Trim().Equals("DATE", StringComparison.OrdinalIgnoreCase);
        }
    }
}
