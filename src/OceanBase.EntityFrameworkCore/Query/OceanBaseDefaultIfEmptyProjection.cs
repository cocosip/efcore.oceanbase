using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace OceanBase.EntityFrameworkCore.Query
{
    internal sealed class OceanBaseDefaultIfEmptyProjection(ShapedQueryExpression source) : ExpressionVisitor
    {
        private readonly SelectExpression _select = (SelectExpression)source.QueryExpression;
        private readonly Dictionary<ProjectionBindingExpression, Expression> _nullable = [];
        private readonly Dictionary<ProjectionBindingExpression, Expression> _bindings = [];
        private ProjectionBindingExpression? _presence;
        private static readonly MemberInfo PresenceMember = typeof(RowPresence).GetProperty(nameof(RowPresence.Present))!;
        public bool RequiresPushdown { get; private set; }
        public bool HasDefaultReference { get; private set; }
        private bool RequiresPresence { get; set; }

        public static LambdaExpression PreserveDefaultRows(LambdaExpression selector, IModel model)
            => (LambdaExpression)new LiftingBarrier(model).Visit(selector);

        public void Capture()
        {
            RequiresPresence = source.ShaperExpression is NewExpression or MemberInitExpression
                or UnaryExpression { NodeType: ExpressionType.Convert, Type.IsValueType: false };
            Visit(source.ShaperExpression);
        }

        public void AddPresence(ISqlExpressionFactory sql)
        {
            if (!RequiresPresence) return;
            var marker = sql.ApplyDefaultTypeMapping(sql.Constant(1));
            var mapping = new Dictionary<ProjectionMember, Expression>();
            var maxIndex = -1;
            foreach (var (binding, value) in _bindings)
            {
                if (binding.ProjectionMember is { } member) mapping[member] = value;
                else maxIndex = Math.Max(maxIndex, binding.Index!.Value);
            }
            if (maxIndex >= 0)
            {
                var projections = new List<Expression>();
                for (var i = 0; i <= maxIndex; i++)
                    projections.Add(_select.GetProjection(new ProjectionBindingExpression(_select, i, typeof(object))));
                _presence = new ProjectionBindingExpression(_select, projections.Count, typeof(int?));
                projections.Add(marker);
                _select.ReplaceProjection(projections);
            }
            else
            {
                var member = new ProjectionMember().Append(PresenceMember);
                while (mapping.ContainsKey(member)) member = member.Append(PresenceMember);
                mapping[member] = marker;
                _select.ReplaceProjection(mapping);
                _presence = new ProjectionBindingExpression(_select, member, typeof(int?));
            }
            foreach (var (binding, value) in _bindings) _nullable[binding] = value;
            _nullable[_presence] = marker;
            // The marker must be inside the source so NULL extension can distinguish a missing row.
            RequiresPushdown = true;
        }

        public Expression Shape(Expression shaper)
            => _presence is null ? shaper : Expression.Condition(
                Expression.Equal(_presence, Expression.Constant(null, typeof(int?))),
                Expression.Constant(shaper.Type.IsValueType ? Activator.CreateInstance(shaper.Type) : null, shaper.Type), shaper);

        internal static bool TryDefaultReference(Expression? expression, out ConditionalExpression conditional)
        {
            // EF can project the presence predicate itself while composing a join or SELECT.
            if (expression is ConditionalExpression { IfTrue: ConstantExpression { Value: null } } candidate
                && !candidate.Type.IsValueType)
            {
                conditional = candidate;
                return true;
            }
            conditional = null!;
            return false;
        }

        private sealed class RowPresence { public int Present { get; } }

        public void ResetProjectionMapping()
        {
            var mapping = new Dictionary<ProjectionMember, Expression>();
            foreach (var (binding, value) in _bindings)
            {
                if (binding.ProjectionMember is not { } member) return;
                mapping[member] = value;
            }
            if (mapping.Count == 0) return;
            // EF's dictionary replacement retains old client projections after member translation succeeds.
            _select.ReplaceProjection(Array.Empty<Expression>());
            _select.ReplaceProjection(mapping);
        }

        public void Restore()
        {
            if (_presence?.Index is { } markerIndex)
            {
                var projections = new List<Expression>();
                for (var i = 0; i <= markerIndex; i++)
                {
                    var value = _select.GetProjection(new ProjectionBindingExpression(_select, i, typeof(object)));
                    projections.Add(value switch
                    {
                        ColumnExpression column => column.MakeNullable(),
                        StructuralTypeProjectionExpression structural => structural.MakeNullable(),
                        _ => value
                    });
                }
                _select.ReplaceProjection(projections);
            }
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

        protected override Expression VisitConditional(ConditionalExpression node)
        {
            if (TryDefaultReference(node, out _)) HasDefaultReference = true;
            return base.VisitConditional(node);
        }

        protected override Expression VisitExtension(Expression node)
        {
            if (node is ProjectionBindingExpression binding) Capture(binding, binding.Type);
            // Structural shapers have their own missing-entity/key contract.
            return node is StructuralTypeShaperExpression or IncludeExpression or CollectionResultExpression ? base.VisitExtension(node) : node;
        }

        private void Capture(ProjectionBindingExpression binding, Type resultType)
        {
            if (!ReferenceEquals(binding.QueryExpression, _select)) return;
            var projection = _select.GetProjection(binding);
            _bindings[binding] = projection;
            if (resultType == typeof(object)) RequiresPresence = true;
            if (projection is SqlExpression and not ColumnExpression) RequiresPushdown = true;
            if (resultType.IsValueType && Nullable.GetUnderlyingType(resultType) is null
                && projection is SqlExpression and not ColumnExpression { IsNullable: false }) RequiresPresence = true;
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
        }

        internal static bool HasObjectProjection(Expression source)
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
