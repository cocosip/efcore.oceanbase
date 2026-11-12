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

        private static readonly Dictionary<MemberInfo, (Type Receiver, string Unit, string MySqlFunction)> Parts = new()
        {
            [typeof(DateTime).GetProperty(nameof(DateTime.Year))!] = (typeof(DateTime), "YEAR", "YEAR"),
            [typeof(DateTime).GetProperty(nameof(DateTime.Month))!] = (typeof(DateTime), "MONTH", "MONTH"),
            [typeof(DateTime).GetProperty(nameof(DateTime.Day))!] = (typeof(DateTime), "DAY", "DAYOFMONTH"),
            [typeof(DateTime).GetProperty(nameof(DateTime.Hour))!] = (typeof(DateTime), "HOUR", "HOUR"),
            [typeof(DateTime).GetProperty(nameof(DateTime.Minute))!] = (typeof(DateTime), "MINUTE", "MINUTE"),
            [typeof(DateTime).GetProperty(nameof(DateTime.Second))!] = (typeof(DateTime), "SECOND", "SECOND"),
            [typeof(DateOnly).GetProperty(nameof(DateOnly.Year))!] = (typeof(DateOnly), "YEAR", "YEAR"),
            [typeof(DateOnly).GetProperty(nameof(DateOnly.Month))!] = (typeof(DateOnly), "MONTH", "MONTH"),
            [typeof(DateOnly).GetProperty(nameof(DateOnly.Day))!] = (typeof(DateOnly), "DAY", "DAYOFMONTH"),
            [typeof(TimeOnly).GetProperty(nameof(TimeOnly.Hour))!] = (typeof(TimeOnly), "HOUR", "HOUR"),
            [typeof(TimeOnly).GetProperty(nameof(TimeOnly.Minute))!] = (typeof(TimeOnly), "MINUTE", "MINUTE"),
            [typeof(TimeOnly).GetProperty(nameof(TimeOnly.Second))!] = (typeof(TimeOnly), "SECOND", "SECOND")
        };

        private static readonly Dictionary<MemberInfo, bool> CalendarParts = new()
        {
            [typeof(DateTime).GetProperty(nameof(DateTime.DayOfYear))!] = false,
            [typeof(DateOnly).GetProperty(nameof(DateOnly.DayOfYear))!] = false,
            [typeof(DateTime).GetProperty(nameof(DateTime.DayOfWeek))!] = true,
            [typeof(DateOnly).GetProperty(nameof(DateOnly.DayOfWeek))!] = true
        };
        private static readonly MemberInfo DateTimeDate = typeof(DateTime).GetProperty(nameof(DateTime.Date))!;

        public SqlExpression? Translate(SqlExpression? instance, MemberInfo member, Type returnType,
            IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        {
            if (instance is not null && member == DateTimeDate && returnType == typeof(DateTime))
            {
                instance = factory.ApplyDefaultTypeMapping(instance);
                var dateMapping = instance.TypeMapping;
                if (dateMapping is null || !SupportsDate(dateMapping) || dateMapping.Converter?.ConvertsNulls == true)
                    return null;

                var resultMapping = (RelationalTypeMapping)mappingSource.FindMapping(typeof(DateTime),
                    mode == OceanBaseCompatibilityMode.MySql ? "DATETIME(0)" : "DATE")!;
                if (dateMapping.Converter is { } converter)
                    resultMapping = (RelationalTypeMapping)resultMapping.WithComposedConverter(converter);
                return factory.Function(mode == OceanBaseCompatibilityMode.MySql ? "DATE" : "TRUNC", [instance],
                    nullable: true, argumentsPropagateNullability: [false], typeof(DateTime), resultMapping);
            }

            if (instance is not null && CalendarParts.TryGetValue(member, out var weekday)
                && returnType == (weekday ? typeof(DayOfWeek) : typeof(int)))
            {
                instance = factory.ApplyDefaultTypeMapping(instance);
                return instance.TypeMapping is { } calendarMapping && SupportsCarrier(member.DeclaringType!, calendarMapping)
                    ? TranslateCalendarPart(instance, weekday)
                    : null;
            }

            if (instance is null || returnType != typeof(int) || member is not PropertyInfo { PropertyType: var propertyType }
                || propertyType != typeof(int) || !Parts.TryGetValue(member, out var part)
                || member.DeclaringType != part.Receiver)
                return null;

            instance = factory.ApplyDefaultTypeMapping(instance);
            var mapping = instance.TypeMapping;
            if (mapping is null || !SupportsCarrier(part.Receiver, mapping))
                return null;

            var intMapping = mappingSource.FindMapping(typeof(int))!;
            if (mode == OceanBaseCompatibilityMode.MySql)
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
                : receiver == typeof(DateOnly)
                    ? providerType == typeof(DateTime) && store == "DATE"
                    : mode == OceanBaseCompatibilityMode.MySql
                        ? providerType == typeof(TimeSpan) && store == "TIME"
                        : providerType == typeof(OceanBaseIntervalDayToSecond) && store.StartsWith("INTERVAL DAY", StringComparison.Ordinal);
        }

        private static bool SupportsDate(RelationalTypeMapping mapping)
        {
            var store = mapping.StoreTypeNameBase.Trim().ToUpperInvariant();
            var providerType = mapping.Converter?.ProviderClrType ?? mapping.ClrType;
            return providerType == typeof(DateTime) && store is "DATE" or "DATETIME" or "TIMESTAMP";
        }
    }
}
