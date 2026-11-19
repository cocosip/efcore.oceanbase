using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace OceanBase.EntityFrameworkCore.Metadata
{
    internal sealed class OceanBaseRelationalAnnotationProvider(RelationalAnnotationProviderDependencies dependencies)
        : RelationalAnnotationProvider(dependencies)
    {
        internal const string IdentityAnnotation = "OceanBase:Identity";
        internal const string IdentityGenerationAnnotation = "OceanBase:IdentityGeneration";
        internal const string IdentityOptionsAnnotation = "OceanBase:IdentityOptions";
        internal const string ExpressionIndexPrefix = "OceanBase:ExpressionIndex:";
        internal const string SequenceCacheAnnotation = "OceanBase:SequenceCache";
        internal const string SequenceOrderedAnnotation = "OceanBase:SequenceOrdered";
        internal const string OnUpdateSqlAnnotation = "OceanBase:OnUpdateSql";
        internal const string IndexPrefixLengthsAnnotation = "OceanBase:IndexPrefixLengths";
        internal const string DatabaseCollationAnnotation = "OceanBase:DatabaseCollation";
        internal const string HiLoSequenceName = "OceanBase:HiLoSequenceName";
        internal const string HiLoSequenceSchema = "OceanBase:HiLoSequenceSchema";

        public override IEnumerable<IAnnotation> For(IRelationalModel model, bool designTime)
        {
            if (designTime && model.Collation is { } collation)
                yield return new Annotation(DatabaseCollationAnnotation, collation);
        }

        internal static int[]? IndexPrefixLengths(object? annotation, int columnCount, bool oracle)
        {
            if (annotation is null) return null;
            if (oracle || annotation is not int[] lengths || lengths.Length != columnCount
                || lengths.Any(length => length < 0) || !lengths.Any(length => length > 0))
                throw new NotSupportedException("OceanBase index prefix lengths require a MySQL index and a nonnegative length per column.");
            return lengths;
        }

        internal static string IndexPrefixSql(int[]? lengths, int position)
            => lengths is not null && lengths[position] > 0 ? "(" + lengths[position].ToString(CultureInfo.InvariantCulture) + ")" : "";

        internal static string IdentityClause(bool oracle, bool identity, object? generation, object? options)
        {
            if (generation is null && options is null && (!oracle || !identity)) return "";
            if (!oracle || !identity || generation is not (null or "ALWAYS" or "BY DEFAULT" or "BY DEFAULT ON NULL")
                || options is not null && (options is not string text || string.IsNullOrWhiteSpace(text)))
                throw new NotSupportedException("Identity definitions require a valid Oracle identity column.");
            return " GENERATED " + ((string?)generation ?? "BY DEFAULT") + " AS IDENTITY" + (options is null ? "" : " " + (string)options);
        }

        internal static string ExpressionIndexSql(ISqlGenerationHelper sql, bool oracle, string table, string? schema, IAnnotation annotation)
        {
            if (!oracle || annotation.Name.Length == ExpressionIndexPrefix.Length || annotation.Value is not string[] members
                || members.Length < 2 || members[0] is not ("UNIQUE" or "NONUNIQUE") || members.Any(string.IsNullOrWhiteSpace))
                throw new NotSupportedException("Expression indexes require an Oracle table, a name, uniqueness and nonempty SQL members.");
            return (members[0] == "UNIQUE" ? "CREATE UNIQUE INDEX " : "CREATE INDEX ")
                + sql.DelimitIdentifier(annotation.Name[ExpressionIndexPrefix.Length..], schema) + " ON "
                + sql.DelimitIdentifier(table, schema) + " (" + string.Join(", ", members.Skip(1)) + ")";
        }

        internal static bool IsSupportedOnUpdate(string? sql, string? storeType, string? defaultSql)
        {
            const string precision = @"(?:\((?<precision>[0-6])\))?\z";
            const string timestamp = @"\ACURRENT_TIMESTAMP" + precision;
            var update = Regex.Match(sql ?? "", timestamp, RegexOptions.CultureInvariant);
            var store = Regex.Match(storeType?.Trim() ?? "", @"\A(?:DATETIME|TIMESTAMP)" + precision, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            var defaultTimestamp = Regex.Match(defaultSql?.Trim() ?? "", timestamp, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            static string Precision(Match match) => match.Groups["precision"].Success ? match.Groups["precision"].Value : "0";
            return update.Success && store.Success && Precision(update) == Precision(store)
                && (!defaultTimestamp.Success || Precision(defaultTimestamp) == Precision(store));
        }

        internal static void ValidateTpcKeyGeneration(IModel model, bool oracle, ISqlGenerationHelper sql)
        {
            foreach (var property in TpcKeyProperties(model))
            {
                var providerType = property.GetTypeMapping().Converter?.ProviderClrType ?? property.ClrType;
                var clientGuid = property.ValueGenerated == ValueGenerated.OnAdd && property.ClrType == typeof(Guid)
                    && providerType != typeof(int) && providerType != typeof(long)
                    && !property.TryGetDefaultValue(out _) && property.GetDefaultValueSql() is null;
                if (FindHiLoSequence(property) is not null
                    || (property.GetComputedColumnSql() is null && (property.ValueGenerated == ValueGenerated.Never || clientGuid))
                    || oracle && FindSharedOracleSequenceDefault(property, model, sql) is not null)
                    continue;
                throw new NotSupportedException("TPC keys must be unique across the entire hierarchy. Configure "
                    + property.DeclaringType.DisplayName() + "." + property.Name
                    + " with ValueGeneratedNever and assign unique keys without a computed expression, "
                    + "or use HiLo or client-generated Guid keys without an integer store conversion. "
                    + "Oracle also supports a shared noncyclic model sequence default across the concrete tables. "
                    + "Independent table identities are not supported.");
            }
        }

        internal static IEnumerable<IProperty> TpcKeyProperties(IModel model)
            => model.GetEntityTypes().Where(entity => entity.GetMappingStrategy() == "TPC")
                .SelectMany(entity => entity.FindPrimaryKey()?.Properties ?? []).Distinct();

        internal static ISequence? FindSharedOracleSequenceDefault(IProperty property, IModel model, ISqlGenerationHelper sql)
        {
            if (property.ValueGenerated != ValueGenerated.OnAdd
                || (property.ClrType != typeof(long) && property.ClrType != typeof(int))
                || !HasDefaultSequenceKeySentinel(property)
                || property.GetValueGeneratorFactory() is not null || property.GetTypeMapping().Converter is not null
                || property.TryGetDefaultValue(out _) || property.GetComputedColumnSql() is not null
                || property.GetDefaultValueSql() is not { } defaultSql)
                return null;

            var sequence = model.GetSequences().FirstOrDefault(sequence =>
                string.Equals(sql.DelimitIdentifier(sequence.Name, sequence.Schema) + ".NEXTVAL", defaultSql, StringComparison.Ordinal));
            if (sequence is null || sequence.Type != typeof(long) || sequence.IsCyclic || sequence.IncrementBy <= 0
                || sequence.StartValue <= 0 || sequence.MinValue is <= 0
                || sequence.StartValue < sequence.MinValue || sequence.StartValue > sequence.MaxValue
                || property.ClrType == typeof(int) && (sequence.StartValue > int.MaxValue || (sequence.MaxValue ?? long.MaxValue) > int.MaxValue))
                return null;

            var storeType = property.ClrType == typeof(int) ? "NUMBER(10)" : "NUMBER(19)";
            return property.GetTableColumnMappings().All(mapping =>
                mapping.TypeMapping.Converter is null && mapping.Column.ComputedColumnSql is null
                && mapping.Column.DefaultValueSql == defaultSql
                && string.Equals(mapping.Column.StoreType, storeType, StringComparison.OrdinalIgnoreCase)
                && (sequence.Schema is not null || mapping.Column.Table.Schema is null)) ? sequence : null;
        }

        private static bool HasDefaultSequenceKeySentinel(IProperty property)
            // Migration models use dictionary indexers, whose missing member defaults to null.
            => property.Sentinel is 0 or 0L || property.Sentinel is null && property.DeclaringType.IsPropertyBag;

        internal static ISequence? FindHiLoSequence(IProperty property)
        {
            if (property[HiLoSequenceName] is not string name) return null;

            var propertyName = property.DeclaringType.DisplayName() + "." + property.Name;
            if (!property.IsPrimaryKey() || property.ValueGenerated != ValueGenerated.OnAdd
                || (property.ClrType != typeof(int) && property.ClrType != typeof(long))
                || property.GetTypeMapping().Converter is not null || property.GetValueGeneratorFactory() is not null
                || !HasDefaultSequenceKeySentinel(property) || property.TryGetDefaultValue(out _)
                || property.GetDefaultValueSql() is not null || property.GetComputedColumnSql() is not null)
            {
                throw new NotSupportedException("OceanBase HiLo requires an Int32 or Int64 primary-key property with "
                    + "ValueGeneratedOnAdd, a default sentinel, and no converter, custom generator or server-generated expression: " + propertyName);
            }

            var sequence = property.DeclaringType.Model.FindSequence(name, property[HiLoSequenceSchema] as string);
            if (sequence is null || sequence.Type != typeof(long) || sequence.IncrementBy <= 0 || sequence.IsCyclic
                || sequence.StartValue < 1 || sequence.MinValue is < 1
                || (property.ClrType == typeof(int) && sequence.StartValue > int.MaxValue))
            {
                throw new NotSupportedException("OceanBase HiLo requires a declared ascending noncyclic Int64 sequence "
                    + "that produces positive keys within the property range: " + propertyName);
            }

            return sequence;
        }

        public override IEnumerable<IAnnotation> For(IColumn column, bool designTime)
        {
            if (!designTime) yield break;
            if (IsIdentity(column))
                yield return new Annotation(IdentityAnnotation, true);
            foreach (var name in new[] { IdentityGenerationAnnotation, IdentityOptionsAnnotation })
            {
                var values = column.PropertyMappings.Select(mapping => mapping.Property[name]).Distinct().ToArray();
                if (values.Length > 1) throw new NotSupportedException("Conflicting OceanBase identity definitions for " + column.Name + ".");
                if (values.SingleOrDefault() is { } value) yield return new Annotation(name, value);
            }
            var updates = column.PropertyMappings.Select(mapping => mapping.Property[OnUpdateSqlAnnotation])
                .Where(value => value is not null).Distinct().ToArray();
            if (updates.Length > 1)
                throw new NotSupportedException("Conflicting OceanBase ON UPDATE clauses for column " + column.Name + ".");
            if (updates.Length == 1)
                yield return new Annotation(OnUpdateSqlAnnotation, updates[0]);
        }

        public override IEnumerable<IAnnotation> For(ITableIndex index, bool designTime)
        {
            if (!designTime) yield break;
            var lengths = index.MappedIndexes.Select(mapped => mapped[IndexPrefixLengthsAnnotation]).ToArray();
            var declared = lengths.FirstOrDefault(value => value is not null);
            if (declared is null) yield break;
            if (lengths.Any(value => !System.Collections.StructuralComparisons.StructuralEqualityComparer.Equals(value, declared)))
                throw new NotSupportedException("Conflicting OceanBase prefix lengths for index " + index.Name + ".");
            yield return new Annotation(IndexPrefixLengthsAnnotation, declared);
        }

        public override IEnumerable<IAnnotation> For(ITable table, bool designTime)
        {
            if (!designTime) yield break;
            foreach (var group in table.EntityTypeMappings.SelectMany(mapping => mapping.TypeBase.GetAnnotations())
                .Where(annotation => annotation.Name.StartsWith(ExpressionIndexPrefix, StringComparison.Ordinal)).GroupBy(annotation => annotation.Name))
            {
                var first = group.First();
                if (group.Any(annotation => !System.Collections.StructuralComparisons.StructuralEqualityComparer.Equals(annotation.Value, first.Value)))
                    throw new NotSupportedException("Conflicting OceanBase expression index definitions for " + table.Name + ".");
                yield return new Annotation(first.Name, first.Value);
            }
        }

        public override IEnumerable<IAnnotation> For(ISequence sequence, bool designTime)
        {
            if (!designTime) yield break;
            foreach (var name in new[] { SequenceCacheAnnotation, SequenceOrderedAnnotation })
                if (sequence[name] is { } value) yield return new Annotation(name, value);
        }

        internal static bool IsIdentity(IColumn column)
            => column.DefaultValue is null && column.DefaultValueSql is null && column.ComputedColumnSql is null
                && column.PropertyMappings.Any(mapping => (mapping.Property.IsPrimaryKey() || mapping.Property[IdentityGenerationAnnotation] is not null)
                    && mapping.Property.ValueGenerated == ValueGenerated.OnAdd
                    && mapping.Property[HiLoSequenceName] is null
                    && !IsKeyPropagation(mapping)
                    && (mapping.TypeMapping.Converter?.ProviderClrType ?? mapping.Property.ClrType) is var type
                    && (type == typeof(int) || type == typeof(long)));

        // TPT and entity splitting repeat a key in other tables; only the primary table generates it.
        internal static bool IsKeyPropagation(IColumnMapping mapping)
            => mapping.Property.IsPrimaryKey() && mapping.Property.ValueGenerated == ValueGenerated.OnAdd
                && mapping.Property.DeclaringType is IEntityType declaringType
                && (declaringType.GetMappingStrategy() == "TPT" || declaringType.GetMappingFragments(StoreObjectType.Table).Any())
                && (declaringType.GetTableName() != mapping.Column.Table.Name || declaringType.GetSchema() != mapping.Column.Table.Schema);
    }
}
