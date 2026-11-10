using System;
using System.Collections.Generic;
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

        public SqlExpression? Translate(SqlExpression? instance, MemberInfo member, Type returnType,
            IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        {
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
    }
}
