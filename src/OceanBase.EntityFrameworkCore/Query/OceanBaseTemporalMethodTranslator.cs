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
    internal sealed class OceanBaseTemporalMethodTranslator(ISqlExpressionFactory sqlExpressionFactory) : IMethodCallTranslator
    {
        internal const string DateOnlyAddDaysMarker = "__oceanbase_internal_dateonly_add_days";
        private static readonly MethodInfo DateOnlyAddDays = typeof(DateOnly).GetMethod(nameof(DateOnly.AddDays), [typeof(int)])!;

        public SqlExpression? Translate(SqlExpression? instance, MethodInfo method, IReadOnlyList<SqlExpression> arguments,
            IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        {
            if (instance is null || method != DateOnlyAddDays || arguments.Count != 1)
                return null;

            instance = sqlExpressionFactory.ApplyDefaultTypeMapping(instance);
            var days = sqlExpressionFactory.ApplyDefaultTypeMapping(arguments[0]);
            if (instance.Type != typeof(DateOnly) || days.Type != typeof(int)
                || instance.TypeMapping is not RelationalTypeMapping dateMapping
                || !IsDateOnlyMapping(dateMapping)
                || days.TypeMapping is not RelationalTypeMapping daysMapping
                || daysMapping.Converter?.ConvertsNulls == true
                || (daysMapping.Converter?.ProviderClrType ?? daysMapping.ClrType) != typeof(int))
                return null;

            return sqlExpressionFactory.Function(DateOnlyAddDaysMarker, [instance, days], nullable: true,
                argumentsPropagateNullability: [false, false], typeof(DateOnly), dateMapping);
        }

        private bool IsDateOnlyMapping(RelationalTypeMapping mapping)
        {
            if (mapping.Converter?.ConvertsNulls == true || mapping.ClrType != typeof(DateOnly)) return false;
            var providerType = mapping.Converter?.ProviderClrType ?? mapping.ClrType;
            return providerType == typeof(DateTime)
                && mapping.StoreTypeNameBase.Trim().Equals("DATE", StringComparison.OrdinalIgnoreCase);
        }
    }
}
