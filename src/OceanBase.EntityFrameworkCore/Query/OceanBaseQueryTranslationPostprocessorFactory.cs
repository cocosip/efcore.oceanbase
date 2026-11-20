using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace OceanBase.EntityFrameworkCore.Query
{
    internal sealed class OceanBaseQueryTranslationPostprocessorFactory(
        QueryTranslationPostprocessorDependencies dependencies,
        RelationalQueryTranslationPostprocessorDependencies relationalDependencies) : IQueryTranslationPostprocessorFactory
    {
        public QueryTranslationPostprocessor Create(QueryCompilationContext queryCompilationContext)
            => new Postprocessor(dependencies, relationalDependencies, (RelationalQueryCompilationContext)queryCompilationContext);

        private sealed class Postprocessor(QueryTranslationPostprocessorDependencies dependencies,
            RelationalQueryTranslationPostprocessorDependencies relationalDependencies,
            RelationalQueryCompilationContext queryCompilationContext)
            : RelationalQueryTranslationPostprocessor(dependencies, relationalDependencies, queryCompilationContext)
        {
            private readonly Dictionary<string, (Type ElementType, IReadOnlyDictionary<string, SqlExpression> Columns)> _emptyColumns = [];

            protected override Expression ProcessTypeMappings(Expression expression)
            {
                expression = new OceanBaseCollectionProjectionTypeMappingNormalizer(RelationalDependencies.TypeMappingSource,
                    RelationalQueryCompilationContext.Model).Normalize(expression);
                var jsonCollections = new JsonCollectionProjectionVisitor();
                jsonCollections.Visit(expression);
                if (jsonCollections.RootAliases.Count != 0)
                    new JsonCollectionConverterGuard(jsonCollections).Visit(expression);
                var mapped = new NumericCollectionTypeMappingPostprocessor(Dependencies, RelationalDependencies,
                    RelationalQueryCompilationContext).Process(expression);
                var metadata = new MetadataVisitor();
                metadata.Visit(mapped);
                foreach (var values in metadata.EmptyRoots)
                {
                    var elementType = (Type)values.FindAnnotation(
                        OceanBaseQueryableMethodTranslatingExpressionVisitorFactory.EmptyElementTypeAnnotation)!.Value!;
                    var mapping = metadata.Mappings.GetValueOrDefault((values.Alias, "Value"))
                        ?? RelationalDependencies.TypeMappingSource.FindMapping(elementType);
                    if (mapping is null || (Nullable.GetUnderlyingType(mapping.ClrType) ?? mapping.ClrType)
                            != (Nullable.GetUnderlyingType(elementType) ?? elementType)
                        || !OceanBaseCollectionParameterExpandingVisitor.IsSupportedCollectionElement(mapping))
                        throw new NotSupportedException("Empty inline collection roots require supported numeric, Boolean, text, binary or temporal elements with non-null-converting storage carriers.");
                    var ordinal = RelationalDependencies.TypeMappingSource.FindMapping(typeof(int))!;
                    _emptyColumns.Add(values.Alias, (elementType, new Dictionary<string, SqlExpression>
                    {
                        ["_ord"] = RelationalDependencies.SqlExpressionFactory.Constant(null, typeof(int), ordinal),
                        ["Value"] = RelationalDependencies.SqlExpressionFactory.Constant(null, elementType, mapping)
                    }));
                }
                return mapped;
            }

            protected override Expression Prune(Expression query)
            {
                var pruned = base.Prune(query);
                if (_emptyColumns.Count == 0) return pruned;
                var remaining = new MetadataVisitor();
                remaining.Visit(pruned);
                // EF's pruning reconstructs VALUES without annotations; match the surviving column identities.
                return new EmptyColumnsVisitor(_emptyColumns, remaining.Mappings.Keys.ToHashSet()).Visit(pruned);
            }
        }

        private sealed class NumericCollectionTypeMappingPostprocessor(QueryTranslationPostprocessorDependencies dependencies,
            RelationalQueryTranslationPostprocessorDependencies relationalDependencies,
            RelationalQueryCompilationContext queryCompilationContext)
            : RelationalTypeMappingPostprocessor(dependencies, relationalDependencies, queryCompilationContext)
        {
            protected override Expression VisitExtension(Expression node)
            {
                if (node is TableValuedFunctionExpression function
                    && function.FindAnnotation(OceanBaseQueryableMethodTranslatingExpressionVisitorFactory.JsonCollectionAnnotation) is not null
                    && TryGetInferredTypeMapping(function.Alias, "Value", out var element)
                    && element.Converter is not null)
                    ThrowContextualConverter();
                return base.VisitExtension(node);
            }

            protected override ValuesExpression ApplyTypeMappingsOnValuesExpression(ValuesExpression values)
            {
                if (values.ValuesParameter is not { } parameter) return base.ApplyTypeMappingsOnValuesExpression(values);
                // EF10 requires an inferred comparison mapping even for already mapped standalone roots.
                var element = TryGetInferredTypeMapping(values.Alias, "Value", out var inferred) ? inferred
                    : parameter.TypeMapping?.ElementTypeMapping as RelationalTypeMapping
                        ?? RelationalDependencies.TypeMappingSource.FindMapping(
                            OceanBaseCollectionParameterExpandingVisitor.ElementType(parameter.Type)!);
                if (element is null || !OceanBaseCollectionParameterExpandingVisitor.IsSupportedCollectionElement(element))
                    return base.ApplyTypeMappingsOnValuesExpression(values);
                var collection = RelationalDependencies.TypeMappingSource.FindMapping(parameter.Type,
                    QueryCompilationContext.Model, element)
                    ?? throw new InvalidOperationException("A supported collection mapping is required for the query root.");
                return values.Update(parameter.ApplyTypeMapping(collection));
            }
        }

        private abstract class QueryVisitor : ExpressionVisitor
        {
            protected override Expression VisitExtension(Expression node)
                => node is ShapedQueryExpression shaped
                    ? shaped.UpdateQueryExpression(Visit(shaped.QueryExpression)) : base.VisitExtension(node);
        }

        private sealed class JsonCollectionProjectionVisitor : QueryVisitor
        {
            public HashSet<string> RootAliases { get; } = [];
            public Dictionary<(string Alias, string Name), List<SqlExpression>> Projections { get; } = [];

            protected override Expression VisitExtension(Expression node)
            {
                switch (node)
                {
                    case TableValuedFunctionExpression function when function.FindAnnotation(
                        OceanBaseQueryableMethodTranslatingExpressionVisitorFactory.JsonCollectionAnnotation) is not null:
                        RootAliases.Add(function.Alias);
                        break;
                    case SelectExpression { Alias: { } alias } select:
                        AddProjections(alias, select);
                        break;
                    case SetOperationBase operation:
                        foreach (var (left, right) in operation.Source1.Projection.Zip(operation.Source2.Projection))
                            Projections[(operation.Alias, left.Alias)] = [left.Expression, right.Expression];
                        break;
                }
                return base.VisitExtension(node);
            }

            private void AddProjections(string alias, SelectExpression select)
            {
                foreach (var projection in select.Projection)
                    Projections[(alias, projection.Alias)] = [projection.Expression];
            }
        }

        private sealed class JsonCollectionConverterGuard(JsonCollectionProjectionVisitor projections) : QueryVisitor
        {
            protected override Expression VisitExtension(Expression node)
            {
                // EF can infer one mapping from a different use of the same column.
                switch (node)
                {
                    case SqlBinaryExpression { OperatorType: not (ExpressionType.AndAlso or ExpressionType.OrElse) } binary:
                        Check(binary.Left, binary.Right);
                        break;
                    case InExpression { Subquery: { Projection: [var projection] } } membership:
                        Check(membership.Item, projection.Expression);
                        break;
                    case SqlExpression { TypeMapping.Converter: not null } scalar
                        when scalar.Type == typeof(int) || scalar.Type == typeof(long):
                        if (Inspect(scalar).HasJsonValue) ThrowContextualConverter();
                        break;
                }
                return base.VisitExtension(node);
            }

            private void Check(SqlExpression left, SqlExpression right)
            {
                var lhs = Inspect(left);
                var rhs = Inspect(right);
                if ((lhs.HasConverter && rhs.HasJsonValue) || (rhs.HasConverter && lhs.HasJsonValue))
                    ThrowContextualConverter();
            }

            private JsonCollectionOperandVisitor Inspect(SqlExpression expression)
            {
                var operand = new JsonCollectionOperandVisitor(projections);
                operand.Visit(expression);
                return operand;
            }
        }

        private sealed class JsonCollectionOperandVisitor(JsonCollectionProjectionVisitor projections) : ExpressionVisitor
        {
            private readonly HashSet<(string Alias, string Name)> _visitedColumns = [];
            public bool HasJsonValue { get; private set; }
            public bool HasConverter { get; private set; }

            protected override Expression VisitExtension(Expression node)
            {
                if (node is SqlExpression { Type: var type } && type == typeof(bool)) return node;
                HasConverter |= node is SqlExpression { TypeMapping.Converter: not null };
                switch (node)
                {
                    case ColumnExpression column when _visitedColumns.Add((column.TableAlias, column.Name)):
                        HasJsonValue |= column.Name == "Value" && projections.RootAliases.Contains(column.TableAlias);
                        if (projections.Projections.TryGetValue((column.TableAlias, column.Name), out var sources))
                            foreach (var source in sources) Visit(source);
                        return node;
                    case ScalarSubqueryExpression { Subquery.Projection: [var projection] }:
                        Visit(projection.Expression);
                        return node;
                    case CaseExpression conditional:
                        foreach (var clause in conditional.WhenClauses) Visit(clause.Result);
                        Visit(conditional.ElseResult);
                        return node;
                }
                return base.VisitExtension(node);
            }
        }

        private static void ThrowContextualConverter()
            => throw new NotSupportedException("Single JSON collection parameters cannot use a contextual value converter.");

        private sealed class MetadataVisitor : QueryVisitor
        {
            public List<ValuesExpression> EmptyRoots { get; } = [];
            public Dictionary<(string Alias, string Name), RelationalTypeMapping> Mappings { get; } = [];

            protected override Expression VisitExtension(Expression node)
            {
                if (node is ValuesExpression { RowValues.Count: 0 } values
                    && values.FindAnnotation(OceanBaseQueryableMethodTranslatingExpressionVisitorFactory.EmptyElementTypeAnnotation) is not null)
                    EmptyRoots.Add(values);
                if (node is ColumnExpression { TypeMapping: { } mapping } column)
                    Mappings[(column.TableAlias, column.Name)] = mapping;
                return base.VisitExtension(node);
            }
        }

        private sealed class EmptyColumnsVisitor(
            Dictionary<string, (Type ElementType, IReadOnlyDictionary<string, SqlExpression> Columns)> emptyColumns,
            HashSet<(string Alias, string Name)> references) : QueryVisitor
        {
            protected override Expression VisitExtension(Expression node)
            {
                if (node is ValuesExpression { RowValues.Count: 0 } values
                    && emptyColumns.TryGetValue(values.Alias, out var metadata))
                {
                    if (references.Contains((values.Alias, "_ord")) && !values.ColumnNames.Contains("_ord"))
                    {
                        if (!values.ColumnNames.SequenceEqual(new[] { "Value" }) || references.Contains((values.Alias, "Value")))
                            throw new NotSupportedException("Cannot recover the retained empty inline collection ordinal column.");
                        // EF10 misnames the retained prefix when only the trailing Value column is pruned.
                        var corrected = new ValuesExpression(values.Alias, values.RowValues, new[] { "_ord" });
                        foreach (var annotation in values.GetAnnotations())
                            corrected = (ValuesExpression)corrected.AddAnnotation(annotation.Name, annotation.Value);
                        values = corrected;
                    }
                    return values.AddAnnotation(OceanBaseQueryableMethodTranslatingExpressionVisitorFactory.EmptyElementTypeAnnotation,
                            metadata.ElementType)
                        .AddAnnotation(OceanBaseCollectionParameterExpandingVisitor.EmptyColumnsAnnotation,
                            Array.AsReadOnly(values.ColumnNames.Select(name => metadata.Columns[name]).ToArray()));
                }
                return base.VisitExtension(node);
            }
        }
    }
}
