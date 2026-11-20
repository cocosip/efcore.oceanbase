using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace OceanBase.EntityFrameworkCore.Query;

internal sealed class OceanBaseBooleanTranslator(
    ISqlExpressionFactory sqlExpressionFactory,
    IRelationalTypeMappingSource mappingSource) : IMethodCallTranslator
{
    private static readonly MethodInfo BooleanMethod =
        typeof(Convert).GetMethod(nameof(Convert.ToBoolean), [typeof(bool)])!;
    private static readonly HashSet<MethodInfo> NumericMethods =
    [
        typeof(Convert).GetMethod(nameof(Convert.ToBoolean), [typeof(sbyte)])!,
        typeof(Convert).GetMethod(nameof(Convert.ToBoolean), [typeof(byte)])!,
        typeof(Convert).GetMethod(nameof(Convert.ToBoolean), [typeof(short)])!,
        typeof(Convert).GetMethod(nameof(Convert.ToBoolean), [typeof(ushort)])!,
        typeof(Convert).GetMethod(nameof(Convert.ToBoolean), [typeof(int)])!,
        typeof(Convert).GetMethod(nameof(Convert.ToBoolean), [typeof(uint)])!,
        typeof(Convert).GetMethod(nameof(Convert.ToBoolean), [typeof(long)])!,
        typeof(Convert).GetMethod(nameof(Convert.ToBoolean), [typeof(ulong)])!,
        typeof(Convert).GetMethod(nameof(Convert.ToBoolean), [typeof(float)])!,
        typeof(Convert).GetMethod(nameof(Convert.ToBoolean), [typeof(double)])!,
        typeof(Convert).GetMethod(nameof(Convert.ToBoolean), [typeof(decimal)])!
    ];

    public SqlExpression? Translate(SqlExpression? instance, MethodInfo method, IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (instance is not null || arguments.Count != 1)
            return null;

        var operand = sqlExpressionFactory.ApplyDefaultTypeMapping(arguments[0]);
        if (method == BooleanMethod)
        {
            if (operand.Type != typeof(bool) || operand.TypeMapping is not RelationalTypeMapping booleanMapping)
                return null;
            return OceanBaseSqlTranslatingExpressionVisitorFactory.IsCanonicalBoolean(booleanMapping)
                ? operand
                : sqlExpressionFactory.Function(OceanBaseSqlTranslatingExpressionVisitorFactory.BooleanValueMarker,
                    [operand], nullable: true, argumentsPropagateNullability: [true], typeof(bool),
                    mappingSource.FindMapping(typeof(bool)));
        }
        if (!NumericMethods.Contains(method))
            return null;
        if (operand.TypeMapping is not RelationalTypeMapping mapping
            || !OceanBaseMathTranslator.IsValidNumeric(mapping, mappingSource))
            return null;

        var carrier = mapping.Converter?.ProviderClrType ?? mapping.ClrType;
        carrier = Nullable.GetUnderlyingType(carrier) ?? carrier;
        var zero = sqlExpressionFactory.Constant(Convert.ChangeType(0, carrier), mapping);
        return sqlExpressionFactory.NotEqual(operand, zero);
    }
}
