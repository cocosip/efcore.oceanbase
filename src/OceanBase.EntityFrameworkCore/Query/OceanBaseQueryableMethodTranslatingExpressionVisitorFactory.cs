using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
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
        internal const string ParameterCollectionAnnotation = "OceanBase:CollectionColumns";
        internal const string JsonCollectionAnnotation = "OceanBase:JsonCollection";

        public QueryableMethodTranslatingExpressionVisitor Create(QueryCompilationContext queryCompilationContext)
            => new Visitor(dependencies, relationalDependencies, (RelationalQueryCompilationContext)queryCompilationContext);

        private sealed class Visitor : RelationalQueryableMethodTranslatingExpressionVisitor
        {
            private readonly RelationalQueryCompilationContext _compilation;
            private readonly StrongBox<int> _emptyCollectionAliasCount;

            public Visitor(QueryableMethodTranslatingExpressionVisitorDependencies dependencies,
                RelationalQueryableMethodTranslatingExpressionVisitorDependencies relationalDependencies,
                RelationalQueryCompilationContext queryCompilationContext)
                : base(dependencies, relationalDependencies, queryCompilationContext)
            {
                _compilation = queryCompilationContext;
                _emptyCollectionAliasCount = new();
            }

            private Visitor(Visitor parent) : base(parent)
            {
                _compilation = parent._compilation;
                _emptyCollectionAliasCount = parent._emptyCollectionAliasCount;
            }

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
                var body = ReplacingExpressionVisitor.Replace(resultSelector.Parameters[0],
                    Expression.Property(parameter, "Key"), resultSelector.Body);
                body = ReplacingExpressionVisitor.Replace(resultSelector.Parameters[1], parameter, body);
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
                // EF needs the element mapping's KeyComparer before its final type-mapping pass.
                => base.TranslateDistinct((ShapedQueryExpression)new OceanBaseCollectionProjectionTypeMappingNormalizer(
                    RelationalDependencies.TypeMappingSource, RelationalDependencies.Model)
                    .Normalize(source, applyProjectionMappings: true));

            protected override NonQueryExpression? TranslateExecuteUpdate(ShapedQueryExpression source, LambdaExpression setPropertyCalls)
            {
                if (_compilation is not OceanBaseQueryCompilationContextFactory.Context context)
                    return base.TranslateExecuteUpdate(source, setPropertyCalls);
                var previous = context.TranslatingUpdate;
                context.TranslatingUpdate = true;
                try
                {
                    return base.TranslateExecuteUpdate(source, setPropertyCalls);
                }
                finally
                {
                    context.TranslatingUpdate = previous;
                }
            }

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

            protected override ShapedQueryExpression? VisitInlineQueryRoot(InlineQueryRootExpression inlineQueryRootExpression)
            {
                if (inlineQueryRootExpression.Values.Count != 0)
                    return base.VisitInlineQueryRoot(inlineQueryRootExpression);

                var elementType = inlineQueryRootExpression.ElementType;
                // EF8 scans structurally equal table expressions as a single mapping target.
                var alias = "v" + (_emptyCollectionAliasCount.Value++).ToString(CultureInfo.InvariantCulture);
                var values = new TableValuedFunctionExpression(alias, "OceanBaseEmptyCollection", [])
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

                var values = new TableValuedFunctionExpression(tableAlias, "OceanBaseCollection", [parameter])
                    .AddAnnotation(ParameterCollectionAnnotation, new[] { "_ord", "Value" });
                return ShapeCollection(values, elementType, !elementType.IsValueType || Nullable.GetUnderlyingType(elementType) is not null);
            }

            protected override Expression ApplyInferredTypeMappings(Expression expression,
                IReadOnlyDictionary<(TableExpressionBase, string), RelationalTypeMapping?> inferredTypeMappings)
            {
                var mappings = new OceanBaseCollectionProjectionTypeMappingNormalizer(RelationalDependencies.TypeMappingSource,
                    RelationalDependencies.Model);
                expression = mappings.Normalize(expression);
                var applier = new CollectionTypeMappingApplier(RelationalDependencies, inferredTypeMappings, mappings);
                var mapped = applier.Visit(expression);
                if (applier.HasJsonCollection) new OceanBaseJsonCollectionConverterGuard().Visit(mapped);
                return mapped;
            }

            private sealed class CollectionTypeMappingApplier(RelationalQueryableMethodTranslatingExpressionVisitorDependencies dependencies,
                IReadOnlyDictionary<(TableExpressionBase, string), RelationalTypeMapping?> inferredTypeMappings,
                OceanBaseCollectionProjectionTypeMappingNormalizer mappings)
                : RelationalInferredTypeMappingApplier(dependencies.Model, dependencies.SqlExpressionFactory, inferredTypeMappings)
            {
                public bool HasJsonCollection { get; private set; }

                protected override Expression VisitExtension(Expression node)
                {
                    if (node is ColumnExpression { TypeMapping: null } column)
                    {
                        var table = column.Table is JoinExpressionBase join ? join.Table : column.Table;
                        if (mappings.GetContextualMapping(table, column.Name) is { } contextual)
                            return column.ApplyTypeMapping(contextual);
                        if (TryGetInferredTypeMapping(table, column.Name, out var inferred))
                            return column.ApplyTypeMapping(inferred);
                        if (dependencies.TypeMappingSource.FindMapping(column.Type, Model) is { } fallback)
                            return column.ApplyTypeMapping(fallback);
                    }

                    if (node is TableValuedFunctionExpression collection)
                    {
                        if (collection.FindAnnotation(EmptyElementTypeAnnotation)?.Value is Type emptyType)
                        {
                            var element = FindElementMapping(collection, emptyType);
                            var ordinal = dependencies.TypeMappingSource.FindMapping(typeof(int))!;
                            return collection.Update([
                                dependencies.SqlExpressionFactory.Constant(null, typeof(int?), ordinal),
                                dependencies.SqlExpressionFactory.Constant(null, MakeNullable(emptyType), element)]);
                        }

                        if (collection.FindAnnotation(ParameterCollectionAnnotation) is not null
                            && collection.Arguments is [SqlParameterExpression parameter])
                        {
                            var elementType = UnwrapNullable(OceanBaseCollectionParameterExpandingVisitor.ElementType(parameter.Type)!);
                            var element = FindElementMapping(collection, elementType);
                            var mapping = dependencies.TypeMappingSource.FindMapping(parameter.Type, Model, element)
                                ?? throw new InvalidOperationException("A supported collection mapping is required for the query root.");
                            var mappedParameter = parameter.ApplyTypeMapping(mapping);
                            if ((elementType == typeof(int) || elementType == typeof(long)) && element.Converter is null
                                && mapping.Converter?.ProviderClrType == typeof(string))
                            {
                                HasJsonCollection = true;
                                return new TableValuedFunctionExpression(collection.Alias, "JSON_TABLE", [mappedParameter])
                                    .AddAnnotation(JsonCollectionAnnotation, elementType == typeof(int));
                            }

                            return collection.Update([mappedParameter]);
                        }
                    }

                    return base.VisitExtension(node);
                }

                protected override ValuesExpression ApplyTypeMappingsOnValuesExpression(ValuesExpression values, bool stripOrdering)
                {
                    // EF8 can still reference _ord from an ordering after its mapping pass.
                    if (mappings.GetContextualMapping(values, "Value") is not { } contextual)
                        return base.ApplyTypeMappingsOnValuesExpression(values, stripOrdering: false);

                    var rows = new RowValueExpression[values.RowValues.Count];
                    for (var index = 0; index < rows.Length; index++)
                    {
                        var row = values.RowValues[index];
                        rows[index] = new RowValueExpression([row.Values[0],
                            dependencies.SqlExpressionFactory.ApplyTypeMapping(row.Values[1], contextual)]);
                    }
                    return values.Update(rows);
                }

                private RelationalTypeMapping FindElementMapping(TableExpressionBase table, Type elementType)
                {
                    elementType = UnwrapNullable(elementType);
                    var mapping = mappings.GetContextualMapping(table, "Value")
                        ?? (TryGetInferredTypeMapping(table, "Value", out var inferred)
                            ? inferred : dependencies.TypeMappingSource.FindMapping(elementType, Model));
                    if (mapping is { Converter.ConvertsNulls: true }
                        && (mapping.Converter.ProviderClrType == typeof(string)))
                    {
                        throw new InvalidOperationException("Oracle queries with null-converting string value converters are not supported.");
                    }
                    if (mapping is null || UnwrapNullable(mapping.ClrType) != elementType
                        || !OceanBaseCollectionParameterExpandingVisitor.IsSupportedCollectionElement(mapping))
                    {
                        throw new NotSupportedException("Collection roots require supported numeric, Boolean, text, binary or temporal elements with non-null-converting storage carriers.");
                    }

                    return mapping;
                }
            }

            private ShapedQueryExpression ShapeCollection(TableExpressionBase values, Type elementType, bool nullable)
            {
                var ordinalMapping = RelationalDependencies.TypeMappingSource.FindMapping(
                    typeof(int), RelationalDependencies.Model, null)
                    ?? throw new InvalidOperationException("An integer mapping is required for collection ordinals.");

#pragma warning disable EF1001 // EF8's scalar-table constructor is required to attach the ordinal identifier.
                var select = new SelectExpression(values, "Value", elementType, columnTypeMapping: null, isColumnNullable: nullable,
                    identifierColumnName: "_ord", identifierColumnType: typeof(int), identifierColumnTypeMapping: ordinalMapping);
#pragma warning restore EF1001
                var ordinalColumn = select.CreateColumnExpression(values, "_ord", typeof(int), ordinalMapping, columnNullable: false);
                select.AppendOrdering(new OrderingExpression(ordinalColumn, ascending: true));

                Expression projection = new ProjectionBindingExpression(
                    select,
                    new ProjectionMember(),
                    nullable ? MakeNullable(elementType) : elementType);
                if (projection.Type != elementType)
                {
                    projection = Expression.Convert(projection, elementType);
                }

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
