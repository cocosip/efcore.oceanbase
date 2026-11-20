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
        private static readonly ConstantArraySnapshot Snapshot = new();

        public override object GenerateCacheKey(Expression query, bool async)
            => (base.GenerateCacheKey(Snapshot.Visit(query), async),
                RelationalOptionsExtension.Extract(RelationalDependencies.ContextOptions).ParameterizedCollectionTranslationMode);

        private sealed class ConstantArraySnapshot : ExpressionVisitor
        {
            protected override Expression VisitMethodCall(MethodCallExpression node)
            {
                var visited = (MethodCallExpression)base.VisitMethodCall(node);
                if (OceanBaseExplicitTrimTranslator.IsArrayTrim(visited.Method)
                    && visited.Arguments[0] is ConstantExpression { Value: char[] characters })
                    return visited.Update(visited.Object, [Expression.Constant(characters.Clone(), typeof(char[]))]);
                // EF compares arrays structurally; a cached key must not observe later mutations.
                return OceanBaseSqlTranslatingExpressionVisitorFactory.GetConstantValues(visited) is { } values
                    ? visited.Update(visited.Object, [visited.Arguments[0], Expression.Constant(values.Clone(), values.GetType())])
                    : visited;
            }
        }
    }
}
