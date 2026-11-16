using System;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;

namespace OceanBase.EntityFrameworkCore.Query
{
    internal sealed class OceanBaseQueryTranslationPreprocessorFactory(
        QueryTranslationPreprocessorDependencies dependencies,
        RelationalQueryTranslationPreprocessorDependencies relationalDependencies) : IQueryTranslationPreprocessorFactory
    {
        public QueryTranslationPreprocessor Create(QueryCompilationContext context)
            => new Preprocessor(dependencies, relationalDependencies, context);

        private sealed class Preprocessor(QueryTranslationPreprocessorDependencies dependencies,
            RelationalQueryTranslationPreprocessorDependencies relationalDependencies, QueryCompilationContext context)
            : RelationalQueryTranslationPreprocessor(dependencies, relationalDependencies, context)
        {
            public override Expression NormalizeQueryableMethod(Expression expression)
                => new ReferenceDefaults().Visit(base.NormalizeQueryableMethod(expression));
        }

        private sealed class ReferenceDefaults : ExpressionVisitor
        {
            protected override Expression VisitMethodCall(MethodCallExpression node)
            {
                var visited = (MethodCallExpression)base.VisitMethodCall(node);
                if (!node.Method.IsGenericMethod || node.Method.GetGenericMethodDefinition() != QueryableMethods.DefaultIfEmptyWithoutArgument)
                    return visited;
                var type = node.Method.GetGenericArguments()[0];
                if (type.IsValueType || type == typeof(object) || !OceanBaseDefaultIfEmptyProjection.HasObjectProjection(node.Arguments[0]))
                    return visited;
                // Hide the object shape from navigation snapshots, which otherwise reconstruct it after the default row.
                var parameter = Expression.Parameter(type, "defaultProjection");
                var boxed = Expression.Call(QueryableMethods.Select.MakeGenericMethod(type, typeof(object)), visited.Arguments[0],
                    Expression.Lambda(Expression.Convert(parameter, typeof(object)), parameter));
                var defaults = Expression.Call(QueryableMethods.DefaultIfEmptyWithoutArgument.MakeGenericMethod(typeof(object)), boxed);
                return Expression.Call(QueryableMethods.Cast.MakeGenericMethod(type), defaults);
            }
        }
    }
}
