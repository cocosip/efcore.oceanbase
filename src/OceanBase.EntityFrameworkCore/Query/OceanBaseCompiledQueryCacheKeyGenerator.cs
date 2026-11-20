using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;

namespace OceanBase.EntityFrameworkCore.Query
{
    internal sealed class OceanBaseCompiledQueryCacheKeyGenerator(
        CompiledQueryCacheKeyGeneratorDependencies dependencies,
        RelationalCompiledQueryCacheKeyGeneratorDependencies relationalDependencies)
        : RelationalCompiledQueryCacheKeyGenerator(dependencies, relationalDependencies)
    {
        private static readonly ConstantArraySnapshot Snapshot = new();

        public override object GenerateCacheKey(Expression query, bool async)
            => base.GenerateCacheKey(Snapshot.Visit(query), async);

        private sealed class ConstantArraySnapshot : ExpressionVisitor
        {
            protected override Expression VisitMethodCall(MethodCallExpression node)
            {
                var visited = (MethodCallExpression)base.VisitMethodCall(node);
                if (OceanBaseExplicitTrimTranslator.IsArrayTrim(visited.Method)
                    && visited.Arguments[0] is ConstantExpression { Value: char[] characters })
                    return visited.Update(visited.Object, [Expression.Constant(characters.Clone(), typeof(char[]))]);
                // EF compares arrays structurally; a cached key must not observe later mutations.
                return visited;
            }
        }
    }
}
