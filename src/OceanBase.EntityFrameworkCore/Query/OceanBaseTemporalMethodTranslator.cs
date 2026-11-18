using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
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
        internal const string TimeOnlyFromTicksMarker = "__oceanbase_internal_timeonly_from_ticks";
        internal const string TimeOnlyHoursToTicksMarker = "__oceanbase_internal_timeonly_hours_to_ticks";
        internal const string TimeOnlyMinutesToTicksMarker = "__oceanbase_internal_timeonly_minutes_to_ticks";
        private static readonly MethodInfo TimeOnlyAdd = typeof(TimeOnly).GetMethod(nameof(TimeOnly.Add), [typeof(TimeSpan)])!;
        private static readonly MethodInfo TimeOnlyAddHours = typeof(TimeOnly).GetMethod(nameof(TimeOnly.AddHours), [typeof(double)])!;
        private static readonly MethodInfo TimeOnlyAddMinutes = typeof(TimeOnly).GetMethod(nameof(TimeOnly.AddMinutes), [typeof(double)])!;
        private static readonly MethodInfo DateOnlyFromDateTime = typeof(DateOnly).GetMethod(nameof(DateOnly.FromDateTime), [typeof(DateTime)])!;
        private static readonly MethodInfo TimeOnlyFromDateTime = typeof(TimeOnly).GetMethod(nameof(TimeOnly.FromDateTime), [typeof(DateTime)])!;
        private static readonly MethodInfo TimeOnlyFromTimeSpan = typeof(TimeOnly).GetMethod(nameof(TimeOnly.FromTimeSpan), [typeof(TimeSpan)])!;
        private static readonly MethodInfo TimeOnlyToTimeSpan = typeof(TimeOnly).GetMethod(nameof(TimeOnly.ToTimeSpan), Type.EmptyTypes)!;
        private static readonly MethodInfo DateOnlyToDateTime = typeof(DateOnly).GetMethod(nameof(DateOnly.ToDateTime), [typeof(TimeOnly)])!;
        private static readonly MethodInfo DateOnlyToDateTimeKind = typeof(DateOnly).GetMethod(nameof(DateOnly.ToDateTime), [typeof(TimeOnly), typeof(DateTimeKind)])!;
        private static readonly MethodInfo TimeOnlyIsBetween = typeof(TimeOnly).GetMethod(nameof(TimeOnly.IsBetween), [typeof(TimeOnly), typeof(TimeOnly)])!;
        private static readonly MethodInfo DateOnlyAddDays = typeof(DateOnly).GetMethod(nameof(DateOnly.AddDays), [typeof(int)])!;
        private static readonly MethodInfo DateOnlyAddMonths = typeof(DateOnly).GetMethod(nameof(DateOnly.AddMonths), [typeof(int)])!;
        private static readonly MethodInfo DateOnlyAddYears = typeof(DateOnly).GetMethod(nameof(DateOnly.AddYears), [typeof(int)])!;
        private static readonly MethodInfo DateTimeAddMonths = typeof(DateTime).GetMethod(nameof(DateTime.AddMonths), [typeof(int)])!;
        private static readonly MethodInfo DateTimeAddYears = typeof(DateTime).GetMethod(nameof(DateTime.AddYears), [typeof(int)])!;
        private static readonly MethodInfo DateTimeAddDuration = typeof(DateTime).GetMethod(nameof(DateTime.Add), [typeof(TimeSpan)])!;
        private static readonly MethodInfo DateTimeSubtractDuration = typeof(DateTime).GetMethod(nameof(DateTime.Subtract), [typeof(TimeSpan)])!;
        private static readonly MethodInfo DateTimeSubtractDateTime = typeof(DateTime).GetMethod(nameof(DateTime.Subtract), [typeof(DateTime)])!;
        private static readonly MethodInfo DateTimeOffsetSubtractDateTimeOffset = typeof(DateTimeOffset)
            .GetMethod(nameof(DateTimeOffset.Subtract), [typeof(DateTimeOffset)])!;
        private static readonly TimeSpanToTicksConverter DurationTicksConverter = new();
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
            if (TranslateConversion(instance, method, arguments) is { } conversion) return conversion;

            if (instance is not null && arguments.Count == 1
                && (method == TimeOnlyAdd || method == TimeOnlyAddHours || method == TimeOnlyAddMinutes))
            {
                instance = sqlExpressionFactory.ApplyDefaultTypeMapping(instance);
                var amount = sqlExpressionFactory.ApplyDefaultTypeMapping(arguments[0]);
                if (instance.TypeMapping is not { } clockMapping || !IsClockMapping(clockMapping, mode)) return null;
                var integer = mappingSource.FindMapping(typeof(long))!;
                SqlExpression offset;
                if (method == TimeOnlyAdd)
                {
                    // Scalar durations use ticks so the clock operation does not inherit TIME storage limits.
                    var ticksMapping = (RelationalTypeMapping)integer.WithComposedConverter(DurationTicksConverter);
                    if (amount is SqlParameterExpression parameter) amount = parameter.ApplyTypeMapping(ticksMapping);
                    else if (amount is SqlConstantExpression constant) amount = constant.ApplyTypeMapping(ticksMapping);
                    if (IsTickDurationMapping(amount.TypeMapping)) offset = sqlExpressionFactory.Convert(amount, typeof(long), integer);
                    else if (amount.TypeMapping is { } durationMapping && IsDurationMapping(durationMapping, mode))
                    {
                        var expected = mappingSource.FindMapping(typeof(TimeSpan), durationMapping.StoreType)?.Converter;
                        if (!ExpressionEqualityComparer.Instance.Equals(durationMapping.Converter?.ConvertToProviderExpression, expected?.ConvertToProviderExpression)
                            || !ExpressionEqualityComparer.Instance.Equals(durationMapping.Converter?.ConvertFromProviderExpression, expected?.ConvertFromProviderExpression))
                            throw new NotSupportedException("TimeOnly addition cannot use custom converted native duration carriers.");
                        offset = sqlExpressionFactory.Function(OceanBaseTemporalMemberTranslator.TicksMarker, [amount],
                            nullable: true, argumentsPropagateNullability: [false], typeof(long), integer);
                    }
                    else return null;
                }
                else
                {
                    if (amount.Type != typeof(double) || amount.TypeMapping is not { } numericMapping || !IsNumericOffsetMapping(numericMapping, mode)) return null;
                    offset = sqlExpressionFactory.Function(method == TimeOnlyAddHours ? TimeOnlyHoursToTicksMarker : TimeOnlyMinutesToTicksMarker,
                        [amount], nullable: true, argumentsPropagateNullability: [false], typeof(long), integer);
                }
                var clockTicks = sqlExpressionFactory.Function(OceanBaseTemporalMemberTranslator.TicksMarker, [instance],
                    nullable: true, argumentsPropagateNullability: [false], typeof(long), integer);
                var day = sqlExpressionFactory.Constant(TimeSpan.TicksPerDay, integer);
                var wrapped = sqlExpressionFactory.Modulo(sqlExpressionFactory.Add(sqlExpressionFactory.Add(clockTicks,
                    sqlExpressionFactory.Modulo(offset, day)), day), day);
                return sqlExpressionFactory.Function(TimeOnlyFromTicksMarker, [wrapped], nullable: true,
                    argumentsPropagateNullability: [false], typeof(TimeOnly), clockMapping.WithPrecisionAndScale(
                        mode == OceanBaseCompatibilityMode.MySql ? 6 : 7, null));
            }

            if (instance is not null && method == DateTimeSubtractDateTime && arguments.Count == 1)
            {
                var left = sqlExpressionFactory.ApplyDefaultTypeMapping(instance);
                var right = sqlExpressionFactory.ApplyDefaultTypeMapping(arguments[0]);
                left = MapScalar(left, right.TypeMapping);
                right = MapScalar(right, left.TypeMapping);
                if (left.TypeMapping is not { } leftMapping || right.TypeMapping is not { } rightMapping
                    || !IsDateTimeCalendarMapping(leftMapping, mode) || !IsDateTimeCalendarMapping(rightMapping, mode))
                    return null;
                if (!Equals(leftMapping.Converter, rightMapping.Converter))
                    throw new NotSupportedException("Date differences require matching DateTime converters.");
                var integer = mappingSource.FindMapping(typeof(long))!;
                var result = (RelationalTypeMapping)integer.WithComposedConverter(DurationTicksConverter);
                // Subtract exact calendar ticks, avoiding the MySQL TIME storage limit for query results.
                return new SqlBinaryExpression(ExpressionType.Subtract,
                    CalendarTicks(left), CalendarTicks(right), typeof(TimeSpan), result);

                SqlExpression CalendarTicks(SqlExpression date)
                    => sqlExpressionFactory.Function(OceanBaseTemporalMemberTranslator.DateTimeTicksMarker,
                        [date], nullable: true, argumentsPropagateNullability: [false], typeof(long), integer);

                SqlExpression MapScalar(SqlExpression value, RelationalTypeMapping? owner)
                {
                    if (value is not (SqlConstantExpression or SqlParameterExpression) || owner is null
                        || !IsDateTimeCalendarMapping(owner, mode)) return value;
                    // Contextual converters must not narrow a fractional scalar to a DATE column's precision.
                    var mapping = mappingSource.FindMapping(typeof(DateTime),
                        mode == OceanBaseCompatibilityMode.MySql ? "DATETIME(6)" : "TIMESTAMP(7)")!;
                    if (owner.Converter is { } converter) mapping = (RelationalTypeMapping)mapping.WithComposedConverter(converter);
                    return value is SqlConstantExpression constant ? constant.ApplyTypeMapping(mapping)
                        : ((SqlParameterExpression)value).ApplyTypeMapping(mapping);
                }
            }

            if (instance is not null && method == DateTimeOffsetSubtractDateTimeOffset && arguments.Count == 1)
            {
                var left = sqlExpressionFactory.ApplyDefaultTypeMapping(instance);
                var right = sqlExpressionFactory.ApplyDefaultTypeMapping(arguments[0]);
                left = MapOffsetScalar(left, right);
                right = MapOffsetScalar(right, left);
                if (OceanBaseTemporalMemberTranslator.TranslateUtcTicks(sqlExpressionFactory, mappingSource, mode, left) is not { } leftTicks
                    || OceanBaseTemporalMemberTranslator.TranslateUtcTicks(sqlExpressionFactory, mappingSource, mode, right) is not { } rightTicks)
                    return null;

                var leftConverter = left.TypeMapping?.Converter;
                var rightConverter = right.TypeMapping?.Converter;
                if (!ExpressionEqualityComparer.Instance.Equals(leftConverter?.ConvertToProviderExpression, rightConverter?.ConvertToProviderExpression)
                    || !ExpressionEqualityComparer.Instance.Equals(leftConverter?.ConvertFromProviderExpression, rightConverter?.ConvertFromProviderExpression))
                    throw new NotSupportedException("DateTimeOffset differences require matching converters.");

                var integer = mappingSource.FindMapping(typeof(long))!;
                var result = (RelationalTypeMapping)integer.WithComposedConverter(DurationTicksConverter);
                return new SqlBinaryExpression(ExpressionType.Subtract, leftTicks, rightTicks, typeof(TimeSpan), result);

                SqlExpression MapOffsetScalar(SqlExpression value, SqlExpression owner)
                {
                    if (owner is SqlConstantExpression or SqlParameterExpression || owner.TypeMapping is not { } mapping
                        || value is not (SqlConstantExpression or SqlParameterExpression)) return value;
                    var scalarMapping = mapping.WithPrecisionAndScale(mode == OceanBaseCompatibilityMode.MySql ? 6 : 7, null);
                    return value is SqlConstantExpression constant ? constant.ApplyTypeMapping(scalarMapping)
                        : ((SqlParameterExpression)value).ApplyTypeMapping(scalarMapping);
                }
            }

            var numericAdd = NumericAddMarkers.TryGetValue(method, out var numericMarker);
            var ticks = numericMarker == DateTimeAddTicksMarker;
            if (instance is not null && arguments.Count == 1 && (numericAdd || method == DateTimeAddDuration || method == DateTimeSubtractDuration))
            {
                instance = sqlExpressionFactory.ApplyDefaultTypeMapping(instance);
                var duration = sqlExpressionFactory.ApplyDefaultTypeMapping(arguments[0]);
                if (!numericAdd && IsTickDurationMapping(duration.TypeMapping))
                {
                    duration = sqlExpressionFactory.Convert(duration, typeof(long), mappingSource.FindMapping(typeof(long)));
                    if (method == DateTimeSubtractDuration) duration = sqlExpressionFactory.Negate(duration);
                    numericAdd = ticks = true;
                    numericMarker = DateTimeAddTicksMarker;
                }
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

        private SqlExpression? TranslateConversion(SqlExpression? instance, MethodInfo method, IReadOnlyList<SqlExpression> arguments)
        {
            var factory = sqlExpressionFactory;
            if (instance is null && arguments.Count == 1 && (method == DateOnlyFromDateTime || method == TimeOnlyFromDateTime))
            {
                var stamp = factory.ApplyDefaultTypeMapping(arguments[0]);
                if (stamp.TypeMapping is not { } stampMapping || !IsDateTimeCalendarMapping(stampMapping, mode)) return null;
                return method == DateOnlyFromDateTime
                    ? factory.Function(mode == OceanBaseCompatibilityMode.MySql ? "DATE" : "TRUNC", [stamp],
                        nullable: true, argumentsPropagateNullability: [false], typeof(DateOnly), mappingSource.FindMapping(typeof(DateOnly)))
                    : factory.Function(OceanBaseTemporalMemberTranslator.TimeOfDayMarker, [stamp],
                        nullable: true, argumentsPropagateNullability: [false], typeof(TimeOnly), mappingSource.FindMapping(typeof(TimeOnly)));
            }

            if (instance is null && arguments.Count == 1 && method == TimeOnlyFromTimeSpan)
            {
                var amount = factory.ApplyDefaultTypeMapping(arguments[0]);
                var integer = mappingSource.FindMapping(typeof(long))!;
                var tickMapping = (RelationalTypeMapping)integer.WithComposedConverter(DurationTicksConverter);
                if (amount is SqlConstantExpression constant) amount = constant.ApplyTypeMapping(tickMapping);
                else if (amount is SqlParameterExpression parameter) amount = parameter.ApplyTypeMapping(tickMapping);
                SqlExpression ticks;
                if (IsTickDurationMapping(amount.TypeMapping)) ticks = factory.Convert(amount, typeof(long), integer);
                else if (amount.TypeMapping is { } durationMapping && IsDurationMapping(durationMapping, mode))
                    ticks = factory.Function(OceanBaseTemporalMemberTranslator.TicksMarker, [amount], nullable: true,
                        argumentsPropagateNullability: [false], typeof(long), integer);
                else return null;
                // FromTimeSpan validates a clock range; unlike Add it must never wrap invalid durations.
                var valid = factory.AndAlso(factory.GreaterThanOrEqual(ticks, factory.Constant(0L, integer)),
                    factory.LessThan(ticks, factory.Constant(TimeSpan.TicksPerDay, integer)));
                var guarded = factory.Case([new CaseWhenClause(valid, ticks)], new SqlConstantExpression(null, typeof(long), integer));
                return factory.Function(TimeOnlyFromTicksMarker, [guarded], nullable: true,
                    argumentsPropagateNullability: [false], typeof(TimeOnly), mappingSource.FindMapping(typeof(TimeOnly)));
            }

            if (instance is null) return null;
            if (method == TimeOnlyToTimeSpan && arguments.Count == 0)
            {
                instance = factory.ApplyDefaultTypeMapping(instance);
                return instance.TypeMapping is { } clockMapping && IsClockMapping(clockMapping, mode)
                    ? factory.Convert(instance, typeof(TimeSpan), mappingSource.FindMapping(typeof(TimeSpan))) : null;
            }

            if ((method == DateOnlyToDateTime && arguments.Count == 1 || method == DateOnlyToDateTimeKind && arguments.Count == 2)
                && (arguments.Count == 1 || arguments[1] is SqlConstantExpression { Value: DateTimeKind.Unspecified }))
            {
                instance = factory.ApplyDefaultTypeMapping(instance);
                var clock = factory.ApplyDefaultTypeMapping(arguments[0]);
                if (instance.TypeMapping is not { } dayMapping || !IsDateOnlyMapping(dayMapping)
                    || clock.TypeMapping is not { } clockMapping || !IsClockMapping(clockMapping, mode)) return null;
                var timestamp = mappingSource.FindMapping(typeof(DateTime), mode == OceanBaseCompatibilityMode.MySql ? "DATETIME(6)" : "TIMESTAMP(7)")!;
                var date = factory.Convert(instance, typeof(DateTime), timestamp);
                var duration = factory.Convert(clock, typeof(TimeSpan), mappingSource.FindMapping(typeof(TimeSpan)));
                return factory.Function(DateTimeAddDurationMarker, [date, duration], nullable: true,
                    argumentsPropagateNullability: [false, false], typeof(DateTime), timestamp);
            }

            if (method != TimeOnlyIsBetween || arguments.Count != 2) return null;
            instance = factory.ApplyDefaultTypeMapping(instance);
            if (instance.TypeMapping is not { } owner || !IsClockMapping(owner, mode)) return null;
            var scalarMapping = owner.WithPrecisionAndScale(mode == OceanBaseCompatibilityMode.MySql ? 6 : 7, null);
            var start = MapBound(arguments[0]);
            var end = MapBound(arguments[1]);
            if (start is null || end is null) return null;
            var normal = factory.AndAlso(factory.LessThanOrEqual(start, end),
                factory.AndAlso(factory.GreaterThanOrEqual(instance, start), factory.LessThan(instance, end)));
            var wrapped = factory.AndAlso(factory.GreaterThan(start, end),
                factory.OrElse(factory.GreaterThanOrEqual(instance, start), factory.LessThan(instance, end)));
            // EF compensates nullable comparison values to false; guard the method's NULL result explicitly.
            var missing = factory.OrElse(factory.IsNull(instance), factory.OrElse(factory.IsNull(start), factory.IsNull(end)));
            return factory.Case([new CaseWhenClause(missing,
                factory.Constant(null, typeof(bool), mappingSource.FindMapping(typeof(bool))))], factory.OrElse(normal, wrapped));

            SqlExpression? MapBound(SqlExpression bound)
            {
                if (bound is SqlConstantExpression constant) return constant.ApplyTypeMapping(scalarMapping);
                if (bound is SqlParameterExpression parameter) return parameter.ApplyTypeMapping(scalarMapping);
                bound = factory.ApplyDefaultTypeMapping(bound);
                if (bound.TypeMapping is not { } mapping || !IsClockMapping(mapping, mode)) return null;
                if (!ExpressionEqualityComparer.Instance.Equals(owner.Converter?.ConvertToProviderExpression, mapping.Converter?.ConvertToProviderExpression)
                    || !ExpressionEqualityComparer.Instance.Equals(owner.Converter?.ConvertFromProviderExpression, mapping.Converter?.ConvertFromProviderExpression))
                    throw new NotSupportedException("TimeOnly.IsBetween requires matching clock converters.");
                return bound;
            }
        }

        internal static bool IsClockMapping(RelationalTypeMapping mapping, OceanBaseCompatibilityMode mode)
        {
            if ((Nullable.GetUnderlyingType(mapping.ClrType) ?? mapping.ClrType) != typeof(TimeOnly) || mapping.Converter?.ConvertsNulls == true) return false;
            var provider = mapping.Converter?.ProviderClrType ?? mapping.ClrType;
            var store = mapping.StoreTypeNameBase.Trim().ToUpperInvariant();
            return mode == OceanBaseCompatibilityMode.MySql
                ? (Nullable.GetUnderlyingType(provider) ?? provider) == typeof(TimeSpan) && store == "TIME"
                : (Nullable.GetUnderlyingType(provider) ?? provider) == typeof(OceanBaseIntervalDayToSecond) && store.StartsWith("INTERVAL DAY", StringComparison.Ordinal);
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

        internal static bool IsTickDurationMapping(RelationalTypeMapping? mapping)
            => mapping is OceanBaseIntegerTypeMapping<long>
                && mapping.ClrType == typeof(TimeSpan) && mapping.Converter is TimeSpanToTicksConverter;

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
