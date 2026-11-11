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
                        throw new NotSupportedException("Empty inline collection roots require supported numeric, Boolean or text elements with non-null-converting storage carriers.");
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
