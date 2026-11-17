using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace OceanBase.EntityFrameworkCore.Query
{
    internal sealed class OceanBaseTemporalMethodTranslator(
        ISqlExpressionFactory sqlExpressionFactory, OceanBaseCompatibilityMode mode) : IMethodCallTranslator
    {
        internal const string DateOnlyAddDaysMarker = "__oceanbase_internal_dateonly_add_days";
        internal const string DateOnlyAddMonthsMarker = "__oceanbase_internal_dateonly_add_months";
        internal const string DateOnlyAddYearsMarker = "__oceanbase_internal_dateonly_add_years";
        internal const string DateTimeAddMonthsMarker = "__oceanbase_internal_datetime_add_months";
        internal const string DateTimeAddYearsMarker = "__oceanbase_internal_datetime_add_years";
        private static readonly MethodInfo DateOnlyAddDays = typeof(DateOnly).GetMethod(nameof(DateOnly.AddDays), [typeof(int)])!;
        private static readonly MethodInfo DateOnlyAddMonths = typeof(DateOnly).GetMethod(nameof(DateOnly.AddMonths), [typeof(int)])!;
        private static readonly MethodInfo DateOnlyAddYears = typeof(DateOnly).GetMethod(nameof(DateOnly.AddYears), [typeof(int)])!;
        private static readonly MethodInfo DateTimeAddMonths = typeof(DateTime).GetMethod(nameof(DateTime.AddMonths), [typeof(int)])!;
        private static readonly MethodInfo DateTimeAddYears = typeof(DateTime).GetMethod(nameof(DateTime.AddYears), [typeof(int)])!;

        public SqlExpression? Translate(SqlExpression? instance, MethodInfo method, IReadOnlyList<SqlExpression> arguments,
            IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        {
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
