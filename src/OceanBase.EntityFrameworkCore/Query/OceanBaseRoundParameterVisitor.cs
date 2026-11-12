using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace OceanBase.EntityFrameworkCore.Query;

internal sealed class OceanBaseRoundParameterVisitor(
    ISqlExpressionFactory factory,
    IRelationalTypeMappingSource mappingSource,
    ParametersCacheDecorator parameters,
    OceanBaseCompatibilityMode mode) : ExpressionVisitor
{
    protected override Expression VisitExtension(Expression node)
    {
        if (node is not SqlFunctionExpression
            {
                IsBuiltIn: true,
                Instance: null,
                Schema: null,
                Name: OceanBaseRoundTranslator.RuntimeMarker,
                Arguments: [SqlExpression input, SqlExpression digits, SqlExpression rule],
                Type: var type,
                IsNullable: true,
                ArgumentsPropagateNullability: [false, false, false]
            })
            return base.VisitExtension(node);

        var dictionary = parameters.GetAndDisableCaching();
        var resolvedInput = (SqlExpression)Visit(input);
        var resolvedDigits = Read(digits, dictionary, midpoint: false);
        var resolvedRule = Read(rule, dictionary, midpoint: true);
        var translator = new OceanBaseRoundTranslator(factory, mappingSource, mode);
        var translated = translator.TranslateResolved(resolvedInput, type, resolvedDigits,
            (MidpointRounding)resolvedRule);
        return translated ?? throw new InvalidOperationException(
            "Captured Math.Round digits or midpoint rule is outside the supported provider range.");
    }

    private static int Read(SqlExpression expression, IReadOnlyDictionary<string, object?> values, bool midpoint)
    {
        var value = expression switch
        {
            SqlParameterExpression parameter => values[parameter.Name],
            SqlConstantExpression constant => constant.Value,
            _ => throw new InvalidOperationException("Math.Round arguments must be constants or scalar parameters.")
        };
        if (value is null)
            throw new InvalidOperationException("Math.Round arguments cannot be null.");

        if (midpoint && value is MidpointRounding rule)
            return (int)rule;
        if (!midpoint && value is int digits)
            return digits;
        if (midpoint && value is int rawRule)
            return rawRule;

        try
        {
            return Convert.ToInt32(value);
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            throw new InvalidOperationException("Math.Round arguments must have integral values.", exception);
        }
    }
}
