using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace OceanBase.EntityFrameworkCore.Query
{
    internal sealed class OceanBaseDefaultIfEmptyProjection(ShapedQueryExpression source) : ExpressionVisitor
    {
        internal static readonly MethodInfo DefaultRowsMethod = ((Func<IQueryable<int>, IQueryable<int>>)DefaultRows).Method
            .GetGenericMethodDefinition();

        // EF9 expands the pending selector before preserving a provider method as a separate query operation.
        private static IQueryable<T> DefaultRows<T>(IQueryable<T> _)
            => throw new InvalidOperationException("Default rows must be translated into SQL.");

        private readonly SelectExpression _select = (SelectExpression)source.QueryExpression;
        private readonly Dictionary<ProjectionBindingExpression, Expression> _bindings = [];
        private ProjectionBindingExpression? _presence;
        private static readonly MemberInfo PresenceMember = typeof(RowPresence).GetProperty(nameof(RowPresence.Present))!;
        public bool RequiresPushdown { get; private set; }
        public bool HasDefaultReference { get; private set; }
        private bool RequiresPresence { get; set; }

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
            // The marker must be inside the source so NULL extension can distinguish a missing row.
            RequiresPushdown = true;
        }

        public Expression Shape(Expression shaper)
            => _presence is null ? shaper : Expression.Condition(
                Expression.Equal(_presence, Expression.Constant(null, typeof(int?))),
                Expression.Constant(shaper.Type.IsValueType ? Activator.CreateInstance(shaper.Type) : null, shaper.Type), shaper);

        public void ApplyScalarDefault(ISqlExpressionFactory sql)
        {
            if (_presence is not null || !source.ShaperExpression.Type.IsValueType
                || Nullable.GetUnderlyingType(source.ShaperExpression.Type) is not null
                || source.ShaperExpression is not UnaryExpression
                    { NodeType: ExpressionType.Convert, Operand: ProjectionBindingExpression { ProjectionMember: { } member } binding }
                || _select.GetProjection(binding) is not SqlExpression projection)
                return;

            var defaultValue = Activator.CreateInstance(source.ShaperExpression.Type);
            _select.ReplaceProjection(new Dictionary<ProjectionMember, Expression>
            {
                [member] = sql.Coalesce(projection, sql.Constant(defaultValue, projection.Type, projection.TypeMapping), projection.TypeMapping)
            });
        }

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

        public void MakeClientProjectionsNullable()
        {
            // EF9's ApplyDefaultIfEmpty makes member mappings nullable and leaves client projections untouched.
            if (_presence?.Index is not { } markerIndex) return;
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
