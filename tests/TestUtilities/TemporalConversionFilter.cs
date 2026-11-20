using System;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;

namespace OceanBase.EntityFrameworkCore.TestUtilities;

// EF Core 8 can evaluate a conversion of a standalone captured value before SQL translation.
internal sealed class TemporalConversionFilter(
    EvaluatableExpressionFilterDependencies dependencies,
    RelationalEvaluatableExpressionFilterDependencies relationalDependencies)
    : RelationalEvaluatableExpressionFilter(dependencies, relationalDependencies)
{
    public override bool IsEvaluatableExpression(Expression expression, IModel model)
        => !(expression is MethodCallExpression call
            && (call.Method.DeclaringType == typeof(TimeOnly) && call.Method.Name == nameof(TimeOnly.FromTimeSpan)
                || call.Method.DeclaringType == typeof(DateOnly) && call.Method.Name == nameof(DateOnly.ToDateTime)))
            && base.IsEvaluatableExpression(expression, model);
}
