using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBaseConnector;
using OceanBase.EntityFrameworkCore.Storage;

namespace OceanBase.EntityFrameworkCore.Query
{
    internal sealed class OceanBaseCollectionParameterExpandingVisitor(
        ISqlExpressionFactory factory, IReadOnlyDictionary<string, object?> parameters, Action disableCaching,
        IRelationalTypeMappingSource mappingSource,
        OceanBaseCompatibilityMode compatibilityMode,
        bool constantizeByDefault = true) : ExpressionVisitor
    {
        internal const string EmptyColumnsAnnotation = "OceanBase:EmptyValuesColumns";
        private readonly Dictionary<string, List<object?>> _snapshots = [];
        private readonly HashSet<string> _generated = new(StringComparer.Ordinal);
        private HashSet<string>? _usedNames;
        private Dictionary<string, object?>? _expandedValues;

        internal IReadOnlyDictionary<string, object?> ParameterValues => _expandedValues ?? parameters;

        internal Expression AttachBindings(Expression expression)
            => _expandedValues is null ? expression : new BindingVisitor(_generated, _expandedValues).Visit(expression);

        protected override Expression VisitExtension(Expression node)
        {
            if (node is TableValuedFunctionExpression { Arguments: [SqlParameterExpression root] } collection
                && collection.FindAnnotation(OceanBaseQueryableMethodTranslatingExpressionVisitorFactory.ParameterCollectionAnnotation)
                    ?.Value is IReadOnlyList<string> columns)
            {
                if (root.TypeMapping?.ElementTypeMapping is not RelationalTypeMapping elementMapping
                    || !IsSupportedCollectionElement(elementMapping))
                    throw new NotSupportedException("General collection roots require supported numeric, Boolean, text, binary or temporal elements with non-null-converting storage carriers.");
                if (!(columns.SequenceEqual(new[] { "Value" })
                    || columns.SequenceEqual(new[] { "_ord", "Value" })))
                    throw new NotSupportedException("Unsupported general collection column layout.");
                var rootValues = Snapshot(root);
                var ordinalMapping = mappingSource.FindMapping(typeof(int))
                    ?? throw new InvalidOperationException("An integer mapping is required for collection ordinals.");
                var rows = new List<RowValueExpression>(rootValues.Count);
                var elementType = ScalarType(root, elementMapping);
                for (var index = 0; index < rootValues.Count; index++)
                {
                    var scalar = Expand(rootValues[index], elementType, elementMapping);
                    if (index == 0)
                        scalar = new SqlUnaryExpression(ExpressionType.Convert, scalar, scalar.Type, scalar.TypeMapping);
                    rows.Add(new RowValueExpression(columns.Count == 1 ? [scalar]
                        : [factory.Constant(index, ordinalMapping), scalar]));
                }
                if (rows.Count != 0)
                    return new ValuesExpression(collection.Alias, rows, columns);
                var prototypes = columns.Select(column => column == "_ord"
                    ? factory.Constant(null, typeof(int?), ordinalMapping)
                    : factory.Constant(null, NullableType(elementMapping.ClrType), elementMapping)).ToArray();
                return new TableValuedFunctionExpression(collection.Alias, "OceanBaseEmptyCollection", Array.AsReadOnly<SqlExpression>(prototypes))
                    .AddAnnotation(EmptyColumnsAnnotation, columns);
            }

            if (node is not InExpression membership)
                return base.VisitExtension(node);

            if (membership.Subquery is
                {
                    Tables: [TableValuedFunctionExpression { Arguments: [SqlParameterExpression parameter] } table],
                    Projection: [{ Expression: ColumnExpression { Name: "Value" } column }],
                    Predicate: null, GroupBy.Count: 0, Having: null, Limit: null, Offset: null, IsDistinct: false
                }
                && column.TableAlias == table.Alias
                && table.FindAnnotation(OceanBaseQueryableMethodTranslatingExpressionVisitorFactory.ParameterCollectionAnnotation) is not null
                && parameter.TypeMapping?.ElementTypeMapping is RelationalTypeMapping element
                && IsSupportedCollectionElement(element))
            {
                var elementType = ScalarType(parameter, element);
                var values = Snapshot(parameter).Select(value => Expand(value, elementType, element)).ToArray();
                return factory.In((SqlExpression)Visit(membership.Item), values);
            }

            if (membership.ValuesParameter is not { } source)
                return base.VisitExtension(node);
            // An IN parameter usually carries the scalar mapping directly.
            var mapping = source.TypeMapping?.ElementTypeMapping as RelationalTypeMapping
                ?? membership.Item.TypeMapping ?? source.TypeMapping;
            if (mapping is null || !IsSupportedCollectionElement(mapping)
                || constantizeByDefault && compatibilityMode != OceanBaseCompatibilityMode.Oracle)
                return base.VisitExtension(node);

            var type = ScalarType(source, mapping);
            var scalars = Snapshot(source).Select(value => Expand(value, type, mapping)).ToArray();
            return membership.Update((SqlExpression)Visit(membership.Item), scalars);
        }

        private SqlExpression Expand(object? value, Type type, RelationalTypeMapping mapping)
        {
            if (constantizeByDefault)
                return factory.Constant(value, value is null ? NullableType(type) : Nullable.GetUnderlyingType(type) ?? type, mapping);

            _expandedValues ??= new Dictionary<string, object?>(parameters, StringComparer.Ordinal);
            _usedNames ??= new HashSet<string>(parameters.Keys, StringComparer.OrdinalIgnoreCase);
            var name = "__ob_collection_" + _generated.Count;
            while (!_usedNames.Add(name))
                name += "_";
            _expandedValues.Add(name, value);
            _generated.Add(name);
            var parameterType = value is null ? NullableType(type) : type;
            return new SqlParameterExpression(name, parameterType, mapping);
        }

        private sealed class BindingVisitor(HashSet<string> names, IReadOnlyDictionary<string, object?> values) : ExpressionVisitor
        {
            protected override Expression VisitExtension(Expression node)
                => node is SqlParameterExpression parameter && names.Contains(parameter.Name)
                    ? new CollectionBinding(parameter, values[parameter.Name])
                    : base.VisitExtension(node);
        }

        // EF8 command binding receives the original query parameter dictionary.
        internal sealed class CollectionBinding(SqlParameterExpression parameter, object? value)
            : SqlExpression(parameter.Type, parameter.TypeMapping)
        {
            internal string Name => parameter.Name;
            internal IRelationalParameter CreateParameter(string name)
                => new BoundCollectionParameter(Name, name, parameter.TypeMapping!, value, parameter.IsNullable);

            protected override Expression VisitChildren(ExpressionVisitor visitor) => this;

            protected override void Print(ExpressionPrinter expressionPrinter) => expressionPrinter.Visit(parameter);
        }

        private sealed class BoundCollectionParameter(string invariantName, string name, RelationalTypeMapping mapping,
            object? value, bool nullable) : IRelationalParameter
        {
            public string InvariantName => invariantName;

            public void AddDbParameter(DbCommand command, object? _) => Bind(command);

            public void AddDbParameter(DbCommand command, IReadOnlyDictionary<string, object?>? _) => Bind(command);

            private void Bind(DbCommand command)
                => command.Parameters.Add(mapping.CreateParameter(command, name, value, nullable));
        }

        internal static bool IsSupportedInteger(RelationalTypeMapping mapping)
            => mapping.Converter?.ConvertsNulls != true
                && IsIntegerType(mapping.Converter?.ModelClrType ?? mapping.ClrType)
                && IsIntegerType(mapping.Converter?.ProviderClrType ?? mapping.ClrType)
                && (mapping is OceanBaseIntegerTypeMapping<sbyte> or OceanBaseIntegerTypeMapping<byte>
                    or OceanBaseIntegerTypeMapping<short> or OceanBaseIntegerTypeMapping<ushort>
                    or OceanBaseIntegerTypeMapping<int> or OceanBaseIntegerTypeMapping<uint>
                    or OceanBaseIntegerTypeMapping<long> or OceanBaseIntegerTypeMapping<ulong>);

        internal static bool IsSupportedNumeric(RelationalTypeMapping mapping)
            => IsSupportedInteger(mapping)
                || mapping.Converter?.ConvertsNulls != true
                && IsNumericType(mapping.Converter?.ModelClrType ?? mapping.ClrType)
                && IsNumericType(mapping.Converter?.ProviderClrType ?? mapping.ClrType)
                && (mapping is OceanBaseDecimalTypeMapping or OceanBaseFloatingPointTypeMapping<float>
                    or OceanBaseFloatingPointTypeMapping<double>);

        private static bool IsIntegerType(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (type.IsEnum) type = Enum.GetUnderlyingType(type);
            return Type.GetTypeCode(type) is TypeCode.SByte or TypeCode.Byte or TypeCode.Int16
                or TypeCode.UInt16 or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64;
        }

        private static bool IsNumericType(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (type.IsEnum) type = Enum.GetUnderlyingType(type);
            return Type.GetTypeCode(type) is TypeCode.SByte or TypeCode.Byte or TypeCode.Int16
                or TypeCode.UInt16 or TypeCode.UInt32 or TypeCode.Int32 or TypeCode.Int64 or TypeCode.UInt64
                or TypeCode.Decimal or TypeCode.Single or TypeCode.Double;
        }

        internal static bool IsSupportedText(RelationalTypeMapping mapping)
            => mapping.Converter?.ConvertsNulls != true
                && mapping is OceanBaseStringTypeMapping
                && OceanBaseOracleStringVisitor.IsProviderString(mapping);

        internal static bool IsSupportedBinary(RelationalTypeMapping mapping)
            => mapping.Converter?.ConvertsNulls != true
                && mapping is OceanBaseByteArrayTypeMapping
                && mapping.JsonValueReaderWriter is not null;

        internal static bool IsSupportedBoolean(RelationalTypeMapping mapping)
        {
            var modelType = mapping.Converter?.ModelClrType ?? mapping.ClrType;
            var providerType = mapping.Converter?.ProviderClrType ?? mapping.ClrType;
            var providerCode = Type.GetTypeCode(Nullable.GetUnderlyingType(providerType) ?? providerType);
            return mapping.Converter?.ConvertsNulls != true
                && (Nullable.GetUnderlyingType(modelType) ?? modelType) == typeof(bool)
                && (mapping is OceanBaseIntegerTypeMapping<sbyte> or OceanBaseIntegerTypeMapping<byte>
                    or OceanBaseIntegerTypeMapping<short> or OceanBaseIntegerTypeMapping<ushort>
                    or OceanBaseIntegerTypeMapping<int> or OceanBaseIntegerTypeMapping<uint>
                    or OceanBaseIntegerTypeMapping<long> or OceanBaseIntegerTypeMapping<ulong>
                    or OceanBaseIntegerTypeMapping<bool> or OceanBaseIntegerTypeMapping<decimal>)
                && (providerCode is TypeCode.Boolean or TypeCode.SByte or TypeCode.Byte or TypeCode.Int16
                    or TypeCode.UInt16 or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64
                    or TypeCode.Decimal);
        }

        internal static bool IsSupportedTemporal(RelationalTypeMapping mapping)
        {
            var modelType = Nullable.GetUnderlyingType(mapping.Converter?.ModelClrType ?? mapping.ClrType)
                ?? mapping.Converter?.ModelClrType ?? mapping.ClrType;
            var temporalModel = modelType == typeof(DateTime) || modelType == typeof(DateTimeOffset)
                || modelType == typeof(DateOnly) || modelType == typeof(TimeOnly) || modelType == typeof(TimeSpan);
            var temporalMapping = mapping is OceanBaseTemporalTypeMapping<DateTime>
                or OceanBaseTemporalTypeMapping<DateTimeOffset>
                or OceanBaseTemporalTypeMapping<TimeSpan>
                or OceanBaseTemporalTypeMapping<OceanBaseIntervalDayToSecond>;
            return mapping.Converter?.ConvertsNulls != true && temporalModel && temporalMapping
                && mapping.JsonValueReaderWriter is not null;
        }

        internal static bool IsSupportedCollectionElement(RelationalTypeMapping mapping)
            => IsSupportedNumeric(mapping) || IsSupportedBoolean(mapping) || IsSupportedText(mapping)
                || IsSupportedBinary(mapping) || IsSupportedTemporal(mapping);

        private List<object?> Snapshot(SqlParameterExpression source)
        {
            disableCaching();
            var identity = source.Name;
            if (!_snapshots.TryGetValue(identity, out var values))
            {
                if (parameters[source.Name] is { } value && value is not IEnumerable)
                    throw new InvalidOperationException("Collection expansion requires an enumerable parameter value.");
                values = ((IEnumerable?)parameters[source.Name])?.Cast<object?>().ToList() ?? [];
                _snapshots.Add(identity, values);
            }
            return values;
        }

        internal static Type? ElementType(Type collectionType)
            => collectionType.IsArray ? collectionType.GetElementType()
                : collectionType.GetInterfaces().Append(collectionType)
                    .FirstOrDefault(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    ?.GenericTypeArguments[0];

        private static Type NullableType(Type type)
            => type.IsValueType && Nullable.GetUnderlyingType(type) is null ? typeof(Nullable<>).MakeGenericType(type) : type;

        private static Type ScalarType(SqlParameterExpression source, RelationalTypeMapping mapping)
        {
            var elementType = ElementType(source.Type);
            var type = mapping.ClrType;
            if (Nullable.GetUnderlyingType(elementType ?? type) is not null && type.IsValueType
                && Nullable.GetUnderlyingType(type) is null)
                type = typeof(Nullable<>).MakeGenericType(type);
            return type;
        }
    }
}
