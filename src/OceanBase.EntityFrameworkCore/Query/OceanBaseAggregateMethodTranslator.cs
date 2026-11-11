using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBase.EntityFrameworkCore.Storage;
using OceanBaseConnector;

namespace OceanBase.EntityFrameworkCore.Query;

internal sealed class OceanBaseAggregateMethodTranslator(ISqlExpressionFactory sqlExpressionFactory,
    IRelationalTypeMappingSource mappingSource) : IAggregateMethodCallTranslator
{
    public SqlExpression? Translate(MethodInfo method, EnumerableExpression source, IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (method.DeclaringType != typeof(Queryable) || source.Selector is not SqlExpression selector)
            return null;
        var identity = method.IsGenericMethod ? method.GetGenericMethodDefinition() : method;
        var function = QueryableMethods.IsSumWithoutSelector(identity) || QueryableMethods.IsSumWithSelector(identity) ? "SUM"
            : QueryableMethods.IsAverageWithoutSelector(identity) || QueryableMethods.IsAverageWithSelector(identity) ? "AVG"
            : identity == QueryableMethods.MinWithoutSelector || identity == QueryableMethods.MinWithSelector ? "MIN"
            : identity == QueryableMethods.MaxWithoutSelector || identity == QueryableMethods.MaxWithSelector ? "MAX" : null;
        if (function is null)
            return null;
        if (!OceanBaseMathTranslator.IsNumeric(selector.Type))
        {
            var type = Nullable.GetUnderlyingType(selector.Type) ?? selector.Type;
            if (function is "MIN" or "MAX" && (type == typeof(string) || type == typeof(DateTime)
                || type == typeof(DateTimeOffset) || type == typeof(DateOnly) || type == typeof(TimeOnly) || type == typeof(TimeSpan)))
            {
                var owned = sqlExpressionFactory.ApplyDefaultTypeMapping(selector).TypeMapping;
                var supported = type == typeof(string)
                    ? owned is OceanBaseStringTypeMapping && owned.StoreTypeNameBase.Trim().ToUpperInvariant() is not ("CLOB" or "NCLOB")
                    : owned is OceanBaseTemporalTypeMapping<DateTime> or OceanBaseTemporalTypeMapping<DateTimeOffset>
                        or OceanBaseTemporalTypeMapping<TimeSpan> or OceanBaseTemporalTypeMapping<OceanBaseIntervalDayToSecond>;
                if (!supported || owned?.Converter?.ConvertsNulls == true)
                    throw new InvalidOperationException($"OceanBase {function} requires supported text or temporal storage and a converter that does not convert nulls; Oracle LOB extrema are unsupported.");
            }
            // EF already owns native nonnumeric MIN/MAX construction and result cardinality.
            return null;
        }

        selector = sqlExpressionFactory.ApplyDefaultTypeMapping(selector);
        var mapping = selector.TypeMapping;
        if (!OceanBaseMathTranslator.IsValidNumeric(mapping, mappingSource))
            throw new InvalidOperationException($"OceanBase {function} requires a supported numeric mapping and a converter that does not convert nulls.");
        var carrier = Nullable.GetUnderlyingType(mapping!.Converter?.ProviderClrType ?? mapping.ClrType)
            ?? mapping.Converter?.ProviderClrType ?? mapping.ClrType;
        var model = Nullable.GetUnderlyingType(selector.Type) ?? selector.Type;
        var promoted = function == "AVG" && (model == typeof(int) || model == typeof(long));
        if (!promoted && carrier == typeof(decimal) && mapping is not OceanBaseDecimalTypeMapping)
            throw new InvalidOperationException($"OceanBase {function} requires the provider decimal result reader; custom decimal mappings are unsupported.");
        var resultType = promoted ? typeof(double) : selector.Type;
        var resultMapping = promoted ? mappingSource.FindMapping(typeof(double))!
            : mapping is OceanBaseDecimalTypeMapping decimalMapping ? decimalMapping.AsQueryResult() : mapping;
        SqlExpression operand = selector;
        if (source.Predicate is not null)
            operand = sqlExpressionFactory.Case([new CaseWhenClause(source.Predicate, operand)], null);
        if (source.IsDistinct)
            operand = new DistinctExpression(operand);
        // SUM keeps its model mapping so EF's empty-zero compensation applies the original converter.
        var aggregate = sqlExpressionFactory.Function(function, [operand], nullable: true,
            argumentsPropagateNullability: [false], resultType, resultMapping);
        return !promoted && carrier == typeof(float)
            ? sqlExpressionFactory.Function(OceanBaseMathTranslator.SingleResultMarker, [aggregate], nullable: true,
                argumentsPropagateNullability: [false], resultType, resultMapping)
            : aggregate;
    }
}
