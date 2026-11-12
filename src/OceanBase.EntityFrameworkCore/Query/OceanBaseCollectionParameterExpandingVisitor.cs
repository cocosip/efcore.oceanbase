using System;
using System.Collections;
using System.Collections.Generic;
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
        ISqlExpressionFactory factory, RelationalParameterBasedSqlProcessorParameters processorParameters,
        ParametersCacheDecorator parameters, IRelationalTypeMappingSource mappingSource,
        OceanBaseCompatibilityMode compatibilityMode) : ExpressionVisitor
    {
        internal const string EmptyColumnsAnnotation = "OceanBase:EmptyValuesColumns";
        private readonly Dictionary<(string Name, string Invariant), List<object?>> _snapshots = [];
        private readonly Dictionary<RelationalTypeMapping,
            Dictionary<(string Name, string Invariant, ParameterTranslationMode Mode, int Index), SqlExpression>> _expanded
            = new(ReferenceEqualityComparer.Instance);

        protected override Expression VisitExtension(Expression node)
        {
            if (node is ValuesExpression { ValuesParameter: { } root } valuesExpression)
            {
                if (root.TypeMapping?.ElementTypeMapping is not RelationalTypeMapping elementMapping
                    || !IsSupportedCollectionElement(elementMapping))
                    throw new NotSupportedException("General collection roots require supported numeric, Boolean, text or temporal elements with non-null-converting storage carriers.");
                if (!(valuesExpression.ColumnNames.SequenceEqual(new[] { "Value" })
                    || valuesExpression.ColumnNames.SequenceEqual(new[] { "_ord", "Value" })))
                    throw new NotSupportedException("Unsupported general collection column layout.");
                var rootMode = root.TranslationMode ?? processorParameters.CollectionParameterTranslationMode;
                if (rootMode is not (ParameterTranslationMode.Constant or ParameterTranslationMode.MultipleParameters))
                    throw new NotSupportedException("General collection roots require Constant or MultipleParameters translation.");
                var rootValues = Snapshot(root);
                var ordinalMapping = mappingSource.FindMapping(typeof(int))
                    ?? throw new InvalidOperationException("An integer mapping is required for collection ordinals.");
                var rows = new List<RowValueExpression>(rootValues.Count);
                var elementType = ScalarType(root, elementMapping);
                for (var index = 0; index < rootValues.Count; index++)
                {
                    var scalar = Expand(root, elementMapping, elementType, rootMode, index, rootValues[index], "_value");
                    if (index == 0)
                        scalar = new SqlUnaryExpression(ExpressionType.Convert, scalar, scalar.Type, scalar.TypeMapping);
                    rows.Add(new RowValueExpression(valuesExpression.ColumnNames.Count == 1 ? [scalar]
                        : [factory.Constant(index, ordinalMapping), scalar]));
                }
                var result = valuesExpression.Update(rows);
                if (rows.Count == 0)
                {
                    var prototypes = valuesExpression.ColumnNames.Select(column => column == "_ord"
                        ? factory.Constant(null, typeof(int), ordinalMapping)
                        : factory.Constant(null, elementMapping.ClrType, elementMapping)).ToArray();
                    result = (ValuesExpression)result.AddAnnotation(EmptyColumnsAnnotation, Array.AsReadOnly(prototypes));
                }
                return result;
            }

            if (node is not InExpression { ValuesParameter: { } source } membership
                || compatibilityMode != OceanBaseCompatibilityMode.Oracle
                || source.TypeMapping?.ElementTypeMapping is not RelationalTypeMapping mapping
                || !OceanBaseOracleStringVisitor.IsProviderString(mapping))
                return base.VisitExtension(node);

            var values = Snapshot(source);

            var mode = source.TranslationMode ?? processorParameters.CollectionParameterTranslationMode;
            var bucket = mode != ParameterTranslationMode.MultipleParameters ? 1 : values.Count switch
            {
                <= 5 => 1,
                <= 150 => 10,
                <= 750 => 50,
                <= 2000 => 100,
                _ => 200
            };
            var count = values.Count + (bucket - values.Count % bucket) % bucket;
            var scalars = new List<SqlExpression>(count);
            var type = ScalarType(source, mapping);
            for (var index = 0; index < count; index++)
                // Pad raw originals so the scalar visitor can classify provider-empty values before EF handles NULLs.
                scalars.Add(Expand(source, mapping, type, mode, index, values[Math.Min(index, values.Count - 1)], "_text"));

            return membership.Update((SqlExpression)Visit(membership.Item), scalars);
        }

        internal static bool IsSupportedNumeric(RelationalTypeMapping mapping)
            => mapping.Converter?.ConvertsNulls != true
                && Type.GetTypeCode(Nullable.GetUnderlyingType(mapping.ClrType) ?? mapping.ClrType) is (TypeCode.SByte or TypeCode.Byte or TypeCode.Int16
                    or TypeCode.UInt16 or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64
                    or TypeCode.Decimal or TypeCode.Single or TypeCode.Double)
                && mapping is OceanBaseIntegerTypeMapping<sbyte> or OceanBaseIntegerTypeMapping<byte>
                    or OceanBaseIntegerTypeMapping<short> or OceanBaseIntegerTypeMapping<ushort>
                    or OceanBaseIntegerTypeMapping<int> or OceanBaseIntegerTypeMapping<uint>
                    or OceanBaseIntegerTypeMapping<long> or OceanBaseIntegerTypeMapping<ulong>
                    or OceanBaseDecimalTypeMapping or OceanBaseFloatingPointTypeMapping<float>
                    or OceanBaseFloatingPointTypeMapping<double>;

        internal static bool IsSupportedText(RelationalTypeMapping mapping)
            => mapping.Converter?.ConvertsNulls != true
                && mapping is OceanBaseStringTypeMapping
                && OceanBaseOracleStringVisitor.IsProviderString(mapping);

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
                || IsSupportedTemporal(mapping);

        private List<object?> Snapshot(SqlParameterExpression source)
        {
            var dictionary = parameters.GetAndDisableCaching();
            var identity = (source.Name, source.InvariantName);
            if (!_snapshots.TryGetValue(identity, out var values))
            {
                if (dictionary[source.Name] is { } value && value is not IEnumerable)
                    throw new InvalidOperationException("Collection expansion requires an enumerable parameter value.");
                values = ((IEnumerable?)dictionary[source.Name])?.Cast<object?>().ToList() ?? [];
                _snapshots.Add(identity, values);
            }
            return values;
        }

        internal static Type? ElementType(Type collectionType)
            => collectionType.IsArray ? collectionType.GetElementType()
                : collectionType.GetInterfaces().Append(collectionType)
                    .FirstOrDefault(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    ?.GenericTypeArguments[0];

        private static Type ScalarType(SqlParameterExpression source, RelationalTypeMapping mapping)
        {
            var elementType = ElementType(source.Type);
            var type = mapping.ClrType;
            if (Nullable.GetUnderlyingType(elementType ?? type) is not null && type.IsValueType
                && Nullable.GetUnderlyingType(type) is null)
                type = typeof(Nullable<>).MakeGenericType(type);
            return type;
        }

        private SqlExpression Expand(SqlParameterExpression source, RelationalTypeMapping mapping, Type type,
            ParameterTranslationMode mode, int index, object? raw, string suffix)
        {
            if (!_expanded.TryGetValue(mapping, out var expanded))
                _expanded.Add(mapping, expanded = []);
            var key = (source.Name, source.InvariantName, mode, index);
            if (expanded.TryGetValue(key, out var scalar)) return scalar;
            if (mode == ParameterTranslationMode.Constant)
                scalar = factory.Constant(raw, type, sensitive: true, mapping);
            else
            {
                var dictionary = parameters.GetAndDisableCaching();
                var name = Uniquifier.Uniquify(source.Name + suffix + index, dictionary, int.MaxValue);
                dictionary.Add(name, raw);
                scalar = new SqlParameterExpression(name, type, mapping);
            }
            expanded.Add(key, scalar);
            return scalar;
        }
    }
}
