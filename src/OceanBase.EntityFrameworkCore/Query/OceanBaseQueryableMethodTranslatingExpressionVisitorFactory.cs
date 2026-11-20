using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Internal;
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
        internal const string JsonCollectionAnnotation = "OceanBase:JsonCollection";

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

            protected override Expression VisitMethodCall(MethodCallExpression methodCallExpression)
            {
                if (!methodCallExpression.Method.IsGenericMethod
                    || methodCallExpression.Method.GetGenericMethodDefinition() != OceanBaseDefaultIfEmptyProjection.DefaultRowsMethod)
                    return base.VisitMethodCall(methodCallExpression);

                return Visit(methodCallExpression.Arguments[0]) is ShapedQueryExpression source
                    ? TranslateDefaultIfEmpty(source, null) ?? QueryCompilationContext.NotTranslatedExpression
                    : QueryCompilationContext.NotTranslatedExpression;
            }

            protected override ShapedQueryExpression? TranslateGroupBy(ShapedQueryExpression source, LambdaExpression keySelector,
                LambdaExpression? elementSelector, LambdaExpression? resultSelector)
            {
                var grouped = base.TranslateGroupBy(source, keySelector, elementSelector, resultSelector: null);
                if (grouped?.ShaperExpression is not RelationalGroupByShaperExpression group) return grouped;
                var select = (SelectExpression)group.GroupingEnumerable.QueryExpression;
                if (select.Predicate is { } predicate)
                {
                    var keys = ((SelectExpression)grouped.QueryExpression).GroupBy;
                    var corrected = new GroupKeyNullMatchingVisitor(keys, RelationalDependencies.SqlExpressionFactory).Visit(predicate);
                    // GROUP BY has a NULL group even with relational null semantics enabled.
                    ReplacingExpressionVisitor.Replace(predicate, corrected, select);
                }
                if (resultSelector is null) return grouped;
                var parameter = Expression.Parameter(group.Type, "group");
                var body = ReplacingExpressionVisitor.Replace(resultSelector.Parameters,
                    new Expression[] { Expression.Property(parameter, "Key"), parameter }, resultSelector.Body);
                return TranslateSelect(grouped, Expression.Lambda(body, parameter));
            }

            private sealed class GroupKeyNullMatchingVisitor(IReadOnlyList<SqlExpression> keys, ISqlExpressionFactory sql) : ExpressionVisitor
            {
                protected override Expression VisitExtension(Expression node)
                {
                    if (node is SqlBinaryExpression { OperatorType: ExpressionType.Equal,
                        Left: ColumnExpression { IsNullable: true } left, Right: ColumnExpression { IsNullable: true } right } equality
                        && keys.Contains(left))
                        return sql.OrElse(equality, sql.AndAlso(sql.IsNull(left), sql.IsNull(right)));
                    return base.VisitExtension(node);
                }
            }

            protected override ShapedQueryExpression? TranslateDefaultIfEmpty(ShapedQueryExpression source, Expression? defaultValue)
            {
                if (defaultValue is not null) return null;
                var projection = new OceanBaseDefaultIfEmptyProjection(source);
                projection.Capture();
                projection.AddPresence(RelationalDependencies.SqlExpressionFactory);
                if (projection.RequiresPushdown) ((SelectExpression)source.QueryExpression).PushdownIntoSubquery();
                var translated = base.TranslateDefaultIfEmpty(source, defaultValue);
                if (translated is null) return null;
                projection.MakeClientProjectionsNullable();
                projection.ApplyScalarDefault(RelationalDependencies.SqlExpressionFactory);
                return translated.UpdateShaperExpression(projection.Shape(translated.ShaperExpression));
            }

            protected override ShapedQueryExpression TranslateSelect(ShapedQueryExpression source, LambdaExpression selector)
            {
                var input = new OceanBaseDefaultIfEmptyProjection(source);
                input.Capture();
                var translated = base.TranslateSelect(source, selector);
                if (input.HasDefaultReference)
                {
                    var output = new OceanBaseDefaultIfEmptyProjection(translated);
                    output.Capture();
                    output.ResetProjectionMapping();
                }
                return translated;
            }

            protected override ShapedQueryExpression TranslateCast(ShapedQueryExpression source, Type resultType)
            {
                if (!resultType.IsValueType && OceanBaseDefaultIfEmptyProjection.TryDefaultReference(source.ShaperExpression, out var conditional)
                    && conditional.IfFalse is UnaryExpression { NodeType: ExpressionType.Convert, Type: var boxedType } conversion
                    && boxedType == typeof(object) && conversion.Operand.Type == resultType)
                    return source.UpdateShaperExpression(Expression.Condition(conditional.Test,
                        Expression.Constant(null, resultType), conversion.Operand));
                return base.TranslateCast(source, resultType);
            }

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
                if (inlineQueryRootExpression.Values.Count != 0)
                    return base.TranslateInlineQueryRoot(inlineQueryRootExpression);

                var elementType = inlineQueryRootExpression.ElementType;
                var alias = _queryCompilationContext.SqlAliasManager.GenerateTableAlias("values");
                var values = new ValuesExpression(alias, Array.Empty<RowValueExpression>(), ["_ord", "Value"])
                    .AddAnnotation(EmptyElementTypeAnnotation, elementType);
                return ShapeCollection(values, elementType, nullable: false);
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

                var elementMapping = parameter.TypeMapping?.ElementTypeMapping as RelationalTypeMapping
                    ?? RelationalDependencies.TypeMappingSource.FindMapping(
                        Nullable.GetUnderlyingType(elementType) ?? elementType, RelationalDependencies.Model, null);
                if (elementMapping is null
                    || (!OceanBaseCollectionParameterExpandingVisitor.IsSupportedNumeric(elementMapping)
                        && !OceanBaseCollectionParameterExpandingVisitor.IsSupportedBoolean(elementMapping)
                        && !OceanBaseCollectionParameterExpandingVisitor.IsSupportedText(elementMapping)
                        && !OceanBaseCollectionParameterExpandingVisitor.IsSupportedBinary(elementMapping)
                        && !OceanBaseCollectionParameterExpandingVisitor.IsSupportedTemporal(elementMapping)))
                {
                    return null;
                }

                var scalarType = UnwrapNullable(elementType);
#pragma warning disable EF1001 // EF9 stores collection policy on the relational options extension.
                var collectionMode = RelationalOptionsExtension.Extract(_queryCompilationContext.ContextOptions)
                    .ParameterizedCollectionTranslationMode;
                var explicitParameter = _queryCompilationContext.ParametersToNotConstantize.Contains(parameter.Name);
                var jsonRequested = explicitParameter || collectionMode == ParameterizedCollectionTranslationMode.Parameterize;
#pragma warning restore EF1001
                if (jsonRequested && (scalarType == typeof(int) || scalarType == typeof(long))
                    && elementMapping.Converter is null
                    && RelationalDependencies.TypeMappingSource.FindMapping(parameter.Type, RelationalDependencies.Model,
                        elementMapping) is { Converter.ProviderClrType: var carrier } mapping && carrier == typeof(string))
                {
                    var jsonParameter = parameter.ApplyTypeMapping(mapping);
                    var jsonRows = new TableValuedFunctionExpression(tableAlias, "JSON_TABLE", [jsonParameter])
                        .AddAnnotation(JsonCollectionAnnotation, scalarType == typeof(int));
                    return ShapeCollection(jsonRows, elementType, Nullable.GetUnderlyingType(elementType) is not null);
                }

                if (explicitParameter) return null;
                var values = new ValuesExpression(tableAlias, parameter, ["_ord", "Value"]);
                return ShapeCollection(values, elementType, !elementType.IsValueType || Nullable.GetUnderlyingType(elementType) is not null);
            }

            private ShapedQueryExpression ShapeCollection(TableExpressionBase values, Type elementType, bool nullable)
            {
                var alias = values.Alias!;
                var valueColumn = new ColumnExpression("Value", alias, UnwrapNullable(elementType), null, nullable);
                var ordinalMapping = RelationalDependencies.TypeMappingSource.FindMapping(
                    typeof(int), RelationalDependencies.Model, null)
                    ?? throw new InvalidOperationException("An integer mapping is required for collection ordinals.");
                var ordinalColumn = new ColumnExpression("_ord", alias, typeof(int), ordinalMapping, nullable: false);

#pragma warning disable EF1001 // EF9's parameter-root shape is an internal provider contract.
                var select = new SelectExpression(
                    new List<TableExpressionBase> { values },
                    valueColumn,
                    new List<(ColumnExpression, Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer)>
                    {
                        (ordinalColumn, ordinalMapping.Comparer)
                    },
                    _queryCompilationContext.SqlAliasManager);
                select.AppendOrdering(new OrderingExpression(ordinalColumn, ascending: true));

                Expression projection = new ProjectionBindingExpression(
                    select,
                    new ProjectionMember(),
                    nullable ? MakeNullable(elementType) : elementType);
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
