using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace OceanBase.EntityFrameworkCore.Query
{
    internal sealed class OceanBaseDefaultIfEmptyProjection(ShapedQueryExpression source) : ExpressionVisitor
    {
        private readonly SelectExpression _select = (SelectExpression)source.QueryExpression;
        private readonly Dictionary<ProjectionBindingExpression, Expression> _nullable = [];
        public bool RequiresPushdown { get; private set; }

        public static LambdaExpression PreserveDefaultRows(LambdaExpression selector, IModel model)
            => (LambdaExpression)new LiftingBarrier(model).Visit(selector);

        public void Capture() => Visit(source.ShaperExpression);

        public void Restore()
        {
            // SQL types erase Nullable<T>; remove only the compensation added by this operation.
            var replacements = new Dictionary<Expression, Expression>(ReferenceEqualityComparer.Instance);
            foreach (var (binding, original) in _nullable)
            {
                var current = _select.GetProjection(binding);
                if (!ReferenceEquals(current, original)
                    && current is SqlFunctionExpression { Name: "COALESCE", Arguments: [var value, _] })
                    replacements[current] = value;
            }
            if (replacements.Count > 0) new Replacer(replacements).Visit(_select);
        }

        protected override Expression VisitUnary(UnaryExpression node)
        {
            // A nonnullable result uses a nullable reader binding followed by a CLR conversion.
            if (node is { NodeType: ExpressionType.Convert, Operand: ProjectionBindingExpression binding })
            {
                Capture(binding, node.Type);
                return node;
            }
            return base.VisitUnary(node);
        }

        protected override Expression VisitExtension(Expression node)
        {
            if (node is ProjectionBindingExpression binding) Capture(binding, binding.Type);
            // Structural shapers have their own missing-entity/key contract.
            return node;
        }

        private void Capture(ProjectionBindingExpression binding, Type resultType)
        {
            if (!ReferenceEquals(binding.QueryExpression, _select)) return;
            var projection = _select.GetProjection(binding);
            if (projection is SqlExpression and not ColumnExpression) RequiresPushdown = true;
            if (Nullable.GetUnderlyingType(resultType) is not null) _nullable[binding] = projection;
        }

        private sealed class Replacer(Dictionary<Expression, Expression> replacements) : ExpressionVisitor
        {
            public override Expression? Visit(Expression? node)
                => node is not null && replacements.TryGetValue(node, out var replacement) ? replacement : base.Visit(node);
        }

        private sealed class LiftingBarrier(IModel model) : ExpressionVisitor
        {
            protected override Expression VisitMethodCall(MethodCallExpression node)
            {
                var visited = (MethodCallExpression)base.VisitMethodCall(node);
                if (node.Method.IsGenericMethod && node.Method.GetGenericMethodDefinition() == QueryableMethods.DefaultIfEmptyWithoutArgument
                    && (model.FindEntityType(node.Method.GetGenericArguments()[0]) is null || HasObjectProjection(node.Arguments[0])))
                {
                    // Lifting loses the row before scalar COALESCE can run; a true filter keeps EF's real default-row translation.
                    var type = node.Method.GetGenericArguments()[0];
                    return Expression.Call(QueryableMethods.Where.MakeGenericMethod(type), visited,
                        Expression.Lambda(Expression.Constant(true), Expression.Parameter(type, "defaultRow")));
                }
                return visited;
            }

            private static bool HasObjectProjection(Expression source)
            {
                while (source is MethodCallExpression { Arguments.Count: > 0 } call)
                {
                    if (call.Method.IsGenericMethod && call.Method.GetGenericMethodDefinition() == QueryableMethods.Select
                        && call.Arguments[1] is UnaryExpression { Operand: LambdaExpression { Body: NewExpression or MemberInitExpression } })
                        return true;
                    source = call.Arguments[0];
                }
                return false;
            }
        }
    }
}
