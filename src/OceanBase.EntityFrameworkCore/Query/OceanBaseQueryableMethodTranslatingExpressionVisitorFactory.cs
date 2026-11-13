using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBase.EntityFrameworkCore.Internal;

namespace OceanBase.EntityFrameworkCore.Query
{
    internal sealed class OceanBaseQueryableMethodTranslatingExpressionVisitorFactory(
        QueryableMethodTranslatingExpressionVisitorDependencies dependencies,
        RelationalQueryableMethodTranslatingExpressionVisitorDependencies relationalDependencies)
        : IQueryableMethodTranslatingExpressionVisitorFactory
    {
        internal const string EmptyElementTypeAnnotation = "OceanBase:EmptyInlineElementType";

        public QueryableMethodTranslatingExpressionVisitor Create(QueryCompilationContext queryCompilationContext)
            => new Visitor(dependencies, relationalDependencies, (RelationalQueryCompilationContext)queryCompilationContext);

        private sealed class Visitor : RelationalQueryableMethodTranslatingExpressionVisitor
        {
            private readonly RelationalQueryCompilationContext _queryCompilationContext;

            public Visitor(QueryableMethodTranslatingExpressionVisitorDependencies dependencies,
                RelationalQueryableMethodTranslatingExpressionVisitorDependencies relationalDependencies,
                RelationalQueryCompilationContext queryCompilationContext)
                : base(dependencies, relationalDependencies, queryCompilationContext)
                => _queryCompilationContext = queryCompilationContext;

            private Visitor(Visitor parent) : base(parent)
                => _queryCompilationContext = parent._queryCompilationContext;

            protected override QueryableMethodTranslatingExpressionVisitor CreateSubqueryVisitor() => new Visitor(this);

            protected override ShapedQueryExpression TranslateDistinct(ShapedQueryExpression source)
                // EF10 needs the element mapping's KeyComparer before its final type-mapping pass.
                => base.TranslateDistinct((ShapedQueryExpression)new OceanBaseCollectionProjectionTypeMappingNormalizer(
                    RelationalDependencies.TypeMappingSource, RelationalDependencies.Model)
                    .Normalize(source, applyProjectionMappings: true));

            protected override bool IsValidSelectExpressionForExecuteUpdate(SelectExpression select, TableExpressionBase targetTable,
                [NotNullWhen(true)] out TableExpression? tableExpression)
            {
                // Both mode-specific generators preserve left joins without EF's UPDATE/FROM fallback.
                if (select.Offset is null && select.Limit is null
                    && !select.IsDistinct && select.GroupBy.Count == 0 && select.Having is null && select.Orderings.Count == 0
                    && select.Tables.Count > 0 && (targetTable is JoinExpressionBase join ? join.Table : targetTable) is TableExpression target)
                {
                    tableExpression = target;
                    return true;
                }
                return base.IsValidSelectExpressionForExecuteUpdate(select, targetTable, out tableExpression);
            }

            protected override ShapedQueryExpression? TranslateInlineQueryRoot(InlineQueryRootExpression inlineQueryRootExpression)
            {
                var translated = base.TranslateInlineQueryRoot(inlineQueryRootExpression);
                if (inlineQueryRootExpression.Values.Count == 0
                    && translated?.QueryExpression is SelectExpression { Tables: [ValuesExpression values] } select)
                {
#pragma warning disable EF1001 // The base translator returns a mutable SELECT; Update cannot replace its table yet.
                    select.SetTables([(ValuesExpression)values.AddAnnotation(EmptyElementTypeAnnotation, inlineQueryRootExpression.ElementType)]);
#pragma warning restore EF1001
                }
                return translated;
            }

            protected override ShapedQueryExpression? TranslatePrimitiveCollection(
                SqlExpression sqlExpression, IProperty? property, string tableAlias)
            {
                if (property is not null
                    || sqlExpression is not SqlParameterExpression parameter
                    || OceanBaseCollectionParameterExpandingVisitor.ElementType(parameter.Type) is not { } elementType)
                {
                    return null;
                }

                var configuredCollectionMode = RelationalOptionsExtension
                    .Extract(_queryCompilationContext.ContextOptions)
                    .ParameterizedCollectionMode;
                var collectionModeConfigured = _queryCompilationContext.ContextOptions
                    .FindExtension<OceanBaseOptionsExtension>()?.ParameterizedCollectionModeConfigured == true;
                if (!collectionModeConfigured || configuredCollectionMode == ParameterTranslationMode.Parameter)
                {
                    return null;
                }

                var elementMapping = parameter.TypeMapping?.ElementTypeMapping as RelationalTypeMapping
                    ?? RelationalDependencies.TypeMappingSource.FindMapping(
                        Nullable.GetUnderlyingType(elementType) ?? elementType, RelationalDependencies.Model, null);
                if (elementMapping is null
                    || (!OceanBaseCollectionParameterExpandingVisitor.IsSupportedNumeric(elementMapping)
                        && !OceanBaseCollectionParameterExpandingVisitor.IsSupportedBoolean(elementMapping)
                        && !OceanBaseCollectionParameterExpandingVisitor.IsSupportedText(elementMapping)))
                {
                    return null;
                }

                var alias = _queryCompilationContext.SqlAliasManager.GenerateTableAlias(tableAlias);
                var values = new ValuesExpression(alias, parameter, ["_ord", "Value"]);
                var valueColumn = new ColumnExpression("Value", alias, UnwrapNullable(elementType), elementMapping, parameter.IsNullable);
                var ordinalMapping = RelationalDependencies.TypeMappingSource.FindMapping(
                    typeof(int), RelationalDependencies.Model, null)
                    ?? throw new InvalidOperationException("An integer mapping is required for collection ordinals.");
                var ordinalColumn = new ColumnExpression("_ord", alias, typeof(int), ordinalMapping, nullable: false);

#pragma warning disable EF1001 // EF10's parameter-root shape is an internal provider contract.
                var select = new SelectExpression(
                    new List<TableExpressionBase> { values },
                    valueColumn,
                    new List<(ColumnExpression, Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer)>
                    {
                        (ordinalColumn, ordinalMapping.Comparer)
                    },
                    _queryCompilationContext.SqlAliasManager);
                select.AppendOrdering(new OrderingExpression(ordinalColumn, ascending: true));
                select.AddToProjection(valueColumn);

                Expression projection = new ProjectionBindingExpression(
                    select,
                    new ProjectionMember(),
                    parameter.IsNullable ? MakeNullable(elementType) : elementType);
                if (projection.Type != elementType)
                {
                    projection = Expression.Convert(projection, elementType);
                }
#pragma warning restore EF1001

                return new ShapedQueryExpression(select, projection);
            }

            private static Type UnwrapNullable(Type type)
                => Nullable.GetUnderlyingType(type) ?? type;

            private static Type MakeNullable(Type type)
                => type.IsValueType && Nullable.GetUnderlyingType(type) is null
                    ? typeof(Nullable<>).MakeGenericType(type)
                    : type;
        }
    }
}
