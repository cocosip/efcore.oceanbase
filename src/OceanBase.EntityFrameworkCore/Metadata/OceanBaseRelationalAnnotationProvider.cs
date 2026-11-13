using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace OceanBase.EntityFrameworkCore.Metadata
{
    internal sealed class OceanBaseRelationalAnnotationProvider(RelationalAnnotationProviderDependencies dependencies)
        : RelationalAnnotationProvider(dependencies)
    {
        internal const string IdentityAnnotation = "OceanBase:Identity";

        public override IEnumerable<IAnnotation> For(IColumn column, bool designTime)
        {
            if (designTime && IsIdentity(column))
                yield return new Annotation(IdentityAnnotation, true);
        }

        internal static bool IsIdentity(IColumn column)
            => column.DefaultValue is null && column.DefaultValueSql is null && column.ComputedColumnSql is null
                && column.PropertyMappings.Any(mapping => mapping.Property.IsPrimaryKey()
                    && mapping.Property.ValueGenerated == ValueGenerated.OnAdd
                    && !IsTptKeyPropagation(mapping)
                    && (mapping.TypeMapping.Converter?.ProviderClrType ?? mapping.Property.ClrType) is var type
                    && (type == typeof(int) || type == typeof(long)));

        // TPT repeats the same key property in derived tables, but only its declaring table generates it.
        internal static bool IsTptKeyPropagation(IColumnMapping mapping)
            => mapping.Property.IsPrimaryKey() && mapping.Property.ValueGenerated == ValueGenerated.OnAdd
                && mapping.Property.DeclaringType is IEntityType declaringType && declaringType.GetMappingStrategy() == "TPT"
                && (declaringType.GetTableName() != mapping.Column.Table.Name || declaringType.GetSchema() != mapping.Column.Table.Schema);
    }
}
