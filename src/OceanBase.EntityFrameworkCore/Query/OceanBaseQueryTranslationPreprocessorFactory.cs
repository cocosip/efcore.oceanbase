using System;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;
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
                return new ReferenceDefaults(Dependencies.TypeMappingSource, QueryCompilationContext.Model, normalized).Visit(normalized);
            }
        }

        private sealed class ReferenceDefaults(ITypeMappingSource mappings, IModel model, Expression root) : ExpressionVisitor
        {
            protected override Expression VisitUnary(UnaryExpression node)
            {
                var visited = (UnaryExpression)base.VisitUnary(node);
                if (visited.NodeType != ExpressionType.Convert || visited.Type != typeof(char?)
                    || visited.Operand is not MethodCallExpression call || call.Type != typeof(char)
                    || call.Method.DeclaringType != typeof(Queryable)
                    || call.Method.Name is not (nameof(Queryable.FirstOrDefault) or nameof(Queryable.LastOrDefault))
                    || call.Arguments.Count != 1 || call.Arguments[0] is not MethodCallExpression asQueryable
                    || asQueryable.Method.DeclaringType != typeof(Queryable)
                    || asQueryable.Method.Name != nameof(Queryable.AsQueryable)
                    || asQueryable.Arguments[0].Type != typeof(string))
                    return visited;

                // Keep CLR UTF-16 indexing after materialization, including surrogate code units.
                return Expression.Call(typeof(ReferenceDefaults), nameof(ReadCharacter), Type.EmptyTypes,
                    asQueryable.Arguments[0], Expression.Constant(call.Method.Name == nameof(Queryable.LastOrDefault)));
            }

            private static char? ReadCharacter(string? text, bool last)
                => text is null ? null : text.Length == 0 ? default(char) : last ? text[^1] : text[0];

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
                var objectProjection = OceanBaseDefaultIfEmptyProjection.HasObjectProjection(node.Arguments[0]);
                if (model.FindEntityType(type) is not null && !objectProjection) return visited;
                if (!objectProjection || type.IsValueType || type == typeof(object))
                    return Expression.Call(OceanBaseDefaultIfEmptyProjection.DefaultRowsMethod.MakeGenericMethod(type), visited.Arguments[0]);
                // Hide the object shape from navigation snapshots, which otherwise reconstruct it after the default row.
                var parameter = Expression.Parameter(type, "defaultProjection");
                var boxed = Expression.Call(QueryableMethods.Select.MakeGenericMethod(type, typeof(object)), visited.Arguments[0],
                    Expression.Lambda(Expression.Convert(parameter, typeof(object)), parameter));
                var defaults = Expression.Call(OceanBaseDefaultIfEmptyProjection.DefaultRowsMethod.MakeGenericMethod(typeof(object)), boxed);
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
