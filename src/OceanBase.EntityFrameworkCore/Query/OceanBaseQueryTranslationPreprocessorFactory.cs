using System;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;

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
            {
                var normalized = base.NormalizeQueryableMethod(expression);
                return new ReferenceDefaults(Dependencies.TypeMappingSource, normalized).Visit(normalized);
            }
        }

        private sealed class ReferenceDefaults(ITypeMappingSource mappings, Expression root) : ExpressionVisitor
        {
            protected override Expression VisitMethodCall(MethodCallExpression node)
            {
                var visited = (MethodCallExpression)base.VisitMethodCall(node);
                if (!node.Method.IsGenericMethod || node.Method.DeclaringType != typeof(Queryable))
                    return visited;
                var type = node.Method.GetGenericArguments()[0];
                var method = node.Method.GetGenericMethodDefinition();
                if (method == QueryableMethods.DefaultIfEmptyWithArgument
                    || node.Method.Name is nameof(Queryable.FirstOrDefault) or nameof(Queryable.SingleOrDefault)
                        && method.GetParameters()[^1].ParameterType.IsGenericParameter)
                {
                    var single = node.Method.Name == nameof(Queryable.SingleOrDefault);
                    if (mappings.FindMapping(type) is null || single && !ReferenceEquals(node, root)) return visited;
                    var source = visited.Arguments[0];
                    if (visited.Arguments.Count == 3)
                        source = Expression.Call(QueryableMethods.Where.MakeGenericMethod(type), source, visited.Arguments[1]);
                    if (node.Method.Name == nameof(Queryable.FirstOrDefault))
                        source = Expression.Call(QueryableMethods.Take.MakeGenericMethod(type), source, Expression.Constant(1));
                    var defaulted = WithExplicitDefault(source, visited.Arguments[^1], type);
                    if (method == QueryableMethods.DefaultIfEmptyWithArgument) return defaulted;
                    var terminal = single ? QueryableMethods.SingleWithoutPredicate : QueryableMethods.FirstWithoutPredicate;
                    return Expression.Call(terminal.MakeGenericMethod(type), defaulted);
                }
                if (method != QueryableMethods.DefaultIfEmptyWithoutArgument) return visited;
                if (type.IsValueType || type == typeof(object) || !OceanBaseDefaultIfEmptyProjection.HasObjectProjection(node.Arguments[0]))
                    return visited;
                // Hide the object shape from navigation snapshots, which otherwise reconstruct it after the default row.
                var parameter = Expression.Parameter(type, "defaultProjection");
                var boxed = Expression.Call(QueryableMethods.Select.MakeGenericMethod(type, typeof(object)), visited.Arguments[0],
                    Expression.Lambda(Expression.Convert(parameter, typeof(object)), parameter));
                var defaults = Expression.Call(QueryableMethods.DefaultIfEmptyWithoutArgument.MakeGenericMethod(typeof(object)), boxed);
                return Expression.Call(QueryableMethods.Cast.MakeGenericMethod(type), defaults);
            }

            private Expression WithExplicitDefault(Expression source, Expression fallback, Type type)
            {
                var rowType = typeof(DefaultRow<>).MakeGenericType(type);
                var value = Expression.Parameter(type, "value");
                var member = rowType.GetProperty(nameof(DefaultRow<int>.Value))!;
                var row = Expression.New(rowType.GetConstructor([type])!, [value], [member]);
                var rows = Expression.Call(QueryableMethods.Select.MakeGenericMethod(type, rowType), source, Expression.Lambda(row, value));
                var defaultRows = Visit(Expression.Call(QueryableMethods.DefaultIfEmptyWithoutArgument.MakeGenericMethod(rowType), rows));
                var selected = Expression.Parameter(rowType, "defaultRow");
                var body = Expression.Condition(Expression.ReferenceEqual(selected, Expression.Constant(null, rowType)),
                    fallback, Expression.Property(selected, member));
                return Expression.Call(QueryableMethods.Select.MakeGenericMethod(rowType, type), defaultRows, Expression.Lambda(body, selected));
            }

            private sealed class DefaultRow<T>(T value)
            {
                public T Value { get; } = value;
            }
        }
    }
}
