using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;

namespace OceanBase.EntityFrameworkCore.Query
{
    internal sealed class OceanBaseCompiledQueryCacheKeyGenerator(
        CompiledQueryCacheKeyGeneratorDependencies dependencies,
        RelationalCompiledQueryCacheKeyGeneratorDependencies relationalDependencies)
        : RelationalCompiledQueryCacheKeyGenerator(dependencies, relationalDependencies)
    {
        private static readonly ExtremaArraySnapshot Snapshot = new();

        public override object GenerateCacheKey(Expression query, bool async)
            => (base.GenerateCacheKey(Snapshot.Visit(query), async),
                RelationalOptionsExtension.Extract(RelationalDependencies.ContextOptions).ParameterizedCollectionMode);

        private sealed class ExtremaArraySnapshot : ExpressionVisitor
        {
            protected override Expression VisitMethodCall(MethodCallExpression node)
            {
                var visited = (MethodCallExpression)base.VisitMethodCall(node);
                // EF compares arrays structurally; a cached key must not observe later mutations.
                return OceanBaseSqlTranslatingExpressionVisitorFactory.GetConstantValues(visited) is { } values
                    ? visited.Update(visited.Object, [visited.Arguments[0], Expression.Constant(values.Clone(), values.GetType())])
                    : visited;
            }
        }
    }
}
