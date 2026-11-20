using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace OceanBase.EntityFrameworkCore.Query
{
    internal sealed class OceanBaseCollectionProjectionTypeMappingNormalizer(
        IRelationalTypeMappingSource mappings, IModel model) : ExpressionVisitor
    {
        private readonly Metadata _metadata = new();
        private readonly Dictionary<TableExpressionBase, RelationalTypeMapping> _contextual = new(ReferenceEqualityComparer.Instance);
        private bool _applyProjectionMappings;

        internal RelationalTypeMapping? GetContextualMapping(TableExpressionBase table, string column)
            => Root(table, column) is { } root ? _contextual.GetValueOrDefault(root) : null;

        public Expression Normalize(Expression expression, bool applyProjectionMappings = false)
        {
            _applyProjectionMappings = applyProjectionMappings;
            _metadata.Visit(expression);
            foreach (var column in _metadata.Columns)
            {
                if (Root(column) is not { } root || IsDefault(column)
                    || !OceanBaseCollectionParameterExpandingVisitor.IsSupportedCollectionElement(column.TypeMapping!)) continue;
                if (_contextual.TryGetValue(root, out var existing)) Validate(existing, column.TypeMapping!);
                else _contextual.Add(root, column.TypeMapping!);
            }
            if (_contextual.Count == 0 && !applyProjectionMappings) return expression;
            foreach (var column in _metadata.Requirements)
                if (Root(column) is { } root && _contextual.TryGetValue(root, out var mapping))
                    Validate(mapping, column.TypeMapping!);
            foreach (var scalar in _metadata.ResultRequirements)
                if (Context(scalar) is { } mapping && scalar.TypeMapping is { } actual)
                    Validate(mapping, actual);
            var normalized = Visit(expression);
            var remaining = new Metadata();
            remaining.Visit(normalized);
            foreach (var column in remaining.Columns)
                if (Root(column) is { } root && _contextual.TryGetValue(root, out var mapping))
                    Validate(mapping, column.TypeMapping!);
            return normalized;
        }

        protected override Expression VisitExtension(Expression node)
        {
            if (node is ShapedQueryExpression shaped) return shaped.UpdateQueryExpression(Visit(shaped.QueryExpression));
            if (node is ColumnExpression column && (_metadata.Projections.Contains(column) || _metadata.NullTests.Contains(column))
                && Root(column) is { } root)
            {
                if (_applyProjectionMappings)
                {
                    var projectionMapping = _contextual.GetValueOrDefault(root) ?? column.TypeMapping ?? mappings.FindMapping(column.Type, model);
                    if (projectionMapping is not null && OceanBaseCollectionParameterExpandingVisitor.IsSupportedCollectionElement(projectionMapping))
                        return column.ApplyTypeMapping(projectionMapping);
                }
                // EF defaults projections before contextual inference; readers must use the owning mapping too.
                if (_contextual.TryGetValue(root, out var contextual) && IsDefault(column))
                    return column.ApplyTypeMapping(contextual);
            }
            if (node is SqlFunctionExpression { Name: "COALESCE", Arguments: [ScalarSubqueryExpression subquery, SqlConstantExpression fallback] } coalesce
                && Context(subquery) is { } coalesceMapping
                && ReferenceEquals(coalesce.TypeMapping, mappings.FindMapping(coalesce.Type, model)))
                return coalesce.Update(coalesce.Instance, [(SqlExpression)Visit(subquery), fallback.ApplyTypeMapping(coalesceMapping)])
                    .ApplyTypeMapping(coalesceMapping);
            if (node is ScalarSubqueryExpression { Subquery.Projection: [{ Expression: ColumnExpression element }] } scalar
                && Root(element) is { } scalarRoot && _contextual.TryGetValue(scalarRoot, out var mapping)
                && ReferenceEquals(scalar.TypeMapping, mappings.FindMapping(scalar.Type, model)))
                return new ScalarSubqueryExpression((SelectExpression)Visit(scalar.Subquery)).ApplyTypeMapping(mapping);
            return base.VisitExtension(node);
        }

        private RelationalTypeMapping? Context(ScalarSubqueryExpression scalar)
            => scalar.Subquery.Projection is [{ Expression: ColumnExpression element }]
                && Root(element) is { } root ? _contextual.GetValueOrDefault(root) : null;

        private bool IsDefault(ColumnExpression column)
            => column.TypeMapping is { } current && mappings.FindMapping(column.Type, model) is { } baseline
                && current.StoreType == baseline.StoreType && ReferenceEquals(current.Converter, baseline.Converter);

        private static TableExpressionBase? Root(ColumnExpression column) => Root(column.Table, column.Name);

        private static TableExpressionBase? Root(TableExpressionBase table, string column)
        {
            while (true)
            {
                switch (table)
                {
                    case JoinExpressionBase join:
                        table = join.Table;
                        break;
                    case SelectExpression select
                        when select.Projection.FirstOrDefault(p => p.Alias == column)?.Expression is ColumnExpression forwarded:
                        (table, column) = (forwarded.Table, forwarded.Name);
                        break;
                    case TableValuedFunctionExpression collection when column == "Value"
                        && (collection.FindAnnotation(OceanBaseQueryableMethodTranslatingExpressionVisitorFactory.ParameterCollectionAnnotation) is not null
                            || collection.FindAnnotation(OceanBaseQueryableMethodTranslatingExpressionVisitorFactory.EmptyElementTypeAnnotation) is not null):
                        return collection;
                    case ValuesExpression values:
                        var index = values.ColumnNames.ToList().IndexOf("Value");
                        return column == "Value" && index >= 0 && values.RowValues.All(row => row.Values[index].TypeMapping is null)
                            ? values : null;
                    default:
                        return null;
                }
            }
        }

        private static void Validate(RelationalTypeMapping expected, RelationalTypeMapping actual)
        {
            if (!Equivalent(expected, actual))
                throw new InvalidOperationException(RelationalStrings.ConflictingTypeMappingsInferredForColumn("Value"));
        }

        private static bool Equivalent(RelationalTypeMapping expected, RelationalTypeMapping actual)
            => ReferenceEquals(expected, actual)
                || ReferenceEquals(expected.Converter, actual.Converter)
                    && (expected.StoreType == actual.StoreType
                        // Oracle NUMBER(p) and NUMBER(p,0) use the same integral carrier.
                        || expected.ClrType == actual.ClrType
                            && expected.StoreTypeNameBase == "NUMBER" && actual.StoreTypeNameBase == "NUMBER"
                            && expected.Precision == actual.Precision && expected.Precision is not null
                            && expected.Scale.GetValueOrDefault() == 0 && actual.Scale.GetValueOrDefault() == 0
                            && (expected.StoreType == actual.StoreType.Replace(",0)", ")", StringComparison.Ordinal)
                                || actual.StoreType == expected.StoreType.Replace(",0)", ")", StringComparison.Ordinal)));

        private sealed class Metadata : ExpressionVisitor
        {
            private bool _requiresMapping;
            public List<ColumnExpression> Columns { get; } = [];
            public List<ColumnExpression> Requirements { get; } = [];
            public List<ScalarSubqueryExpression> ResultRequirements { get; } = [];
            public HashSet<ColumnExpression> Projections { get; } = new(ReferenceEqualityComparer.Instance);
            public HashSet<ColumnExpression> NullTests { get; } = new(ReferenceEqualityComparer.Instance);

            protected override Expression VisitExtension(Expression node)
            {
                var previous = _requiresMapping;
                if (node is ScalarSubqueryExpression scalar && previous) ResultRequirements.Add(scalar);
                if (node is SelectExpression or ProjectionExpression or ScalarSubqueryExpression) _requiresMapping = false;
                else if (node is SqlFunctionExpression { Name: "COALESCE", Arguments: [ScalarSubqueryExpression, SqlConstantExpression] })
                    _requiresMapping = previous;
                else if (NullTestOperand(node) is { } nullable)
                {
                    _requiresMapping = false;
                    if (nullable is ColumnExpression column) NullTests.Add(column);
                }
                else if (node is SqlExpression and not ColumnExpression) _requiresMapping = true;
                try { return Collect(node); }
                finally { _requiresMapping = previous; }
            }

            private static SqlExpression? NullTestOperand(Expression node) => node switch
            {
                SqlUnaryExpression { OperatorType: ExpressionType.Equal or ExpressionType.NotEqual } unary => unary.Operand,
                SqlBinaryExpression { OperatorType: ExpressionType.Equal or ExpressionType.NotEqual,
                    Right: SqlConstantExpression { Value: null } } binary => binary.Left,
                SqlBinaryExpression { OperatorType: ExpressionType.Equal or ExpressionType.NotEqual,
                    Left: SqlConstantExpression { Value: null } } binary => binary.Right,
                _ => null
            };

            private Expression Collect(Expression node)
            {
                if (node is ShapedQueryExpression shaped)
                {
                    new ProjectionBindings(Projections).Visit(shaped.ShaperExpression);
                    return Visit(shaped.QueryExpression);
                }
                if (node is ProjectionExpression { Expression: ColumnExpression projected }) Projections.Add(projected);
                if (node is ColumnExpression { TypeMapping: not null } column)
                {
                    Columns.Add(column);
                    if (_requiresMapping) Requirements.Add(column);
                }
                return base.VisitExtension(node);
            }
        }

        private sealed class ProjectionBindings(HashSet<ColumnExpression> projections) : ExpressionVisitor
        {
            protected override Expression VisitExtension(Expression node)
            {
                if (node is ProjectionBindingExpression { QueryExpression: SelectExpression select } binding
                    && select.GetProjection(binding) is ColumnExpression column) projections.Add(column);
                return base.VisitExtension(node);
            }
        }
    }
}
