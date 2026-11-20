using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;

namespace OceanBase.EntityFrameworkCore.Query
{
    internal sealed class OceanBaseEvaluatableExpressionFilter(
        EvaluatableExpressionFilterDependencies dependencies,
        RelationalEvaluatableExpressionFilterDependencies relationalDependencies)
        : RelationalEvaluatableExpressionFilter(dependencies, relationalDependencies)
    {
        public override bool IsEvaluatableExpression(Expression expression, IModel model)
            => !(expression is MethodCallExpression call && call.Method.DeclaringType == typeof(OceanBaseDbFunctionsExtensions))
                && base.IsEvaluatableExpression(expression, model);
    }
}
