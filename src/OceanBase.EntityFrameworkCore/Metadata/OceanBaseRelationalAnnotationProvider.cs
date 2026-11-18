using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace OceanBase.EntityFrameworkCore.Metadata
{
    internal sealed class OceanBaseRelationalAnnotationProvider(RelationalAnnotationProviderDependencies dependencies)
        : RelationalAnnotationProvider(dependencies)
    {
        internal const string IdentityAnnotation = "OceanBase:Identity";
        internal const string OnUpdateSqlAnnotation = "OceanBase:OnUpdateSql";

        internal static bool IsSupportedOnUpdate(string? sql, string? storeType, string? defaultSql)
        {
            const string precision = @"(?:\((?<precision>[0-6])\))?\z";
            const string timestamp = @"\ACURRENT_TIMESTAMP" + precision;
            var update = Regex.Match(sql ?? "", timestamp, RegexOptions.CultureInvariant);
            var store = Regex.Match(storeType ?? "", @"\ADATETIME" + precision, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            var defaultTimestamp = Regex.Match(defaultSql?.Trim() ?? "", timestamp, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            static string Precision(Match match) => match.Groups["precision"].Success ? match.Groups["precision"].Value : "0";
            return update.Success && store.Success && Precision(update) == Precision(store)
                && (!defaultTimestamp.Success || Precision(defaultTimestamp) == Precision(store));
        }

        internal static void ValidateTpcKeyGeneration(IEnumerable<IEntityType> entityTypes)
        {
            foreach (var property in entityTypes.Where(e => e.GetMappingStrategy() == "TPC")
                .SelectMany(e => e.FindPrimaryKey()?.Properties ?? []).Distinct())
            {
                var providerType = property.GetTypeMapping().Converter?.ProviderClrType ?? property.ClrType;
                var clientGuid = property.ValueGenerated == ValueGenerated.OnAdd && property.ClrType == typeof(Guid)
                    && providerType != typeof(int) && providerType != typeof(long)
                    && !property.TryGetDefaultValue(out _) && property.GetDefaultValueSql() is null;
                if (property.GetComputedColumnSql() is null && (property.ValueGenerated == ValueGenerated.Never || clientGuid))
                    continue;
                throw new NotSupportedException("TPC keys must be unique across the entire hierarchy. Configure "
                    + property.DeclaringType.DisplayName() + "." + property.Name
                    + " with ValueGeneratedNever and assign unique keys without a computed expression, "
                    + "or use client-generated Guid keys without an integer store conversion. "
                    + "Independent table identities and store-generated TPC keys are not supported.");
            }
        }

        public override IEnumerable<IAnnotation> For(IColumn column, bool designTime)
        {
            if (!designTime) yield break;
            if (IsIdentity(column))
                yield return new Annotation(IdentityAnnotation, true);
            var updates = column.PropertyMappings.Select(mapping => mapping.Property[OnUpdateSqlAnnotation])
                .Where(value => value is not null).Distinct().ToArray();
            if (updates.Length > 1)
                throw new NotSupportedException("Conflicting OceanBase ON UPDATE clauses for column " + column.Name + ".");
            if (updates.Length == 1)
                yield return new Annotation(OnUpdateSqlAnnotation, updates[0]);
        }

        internal static bool IsIdentity(IColumn column)
            => column.DefaultValue is null && column.DefaultValueSql is null && column.ComputedColumnSql is null
                && column.PropertyMappings.Any(mapping => mapping.Property.IsPrimaryKey()
                    && mapping.Property.ValueGenerated == ValueGenerated.OnAdd
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
