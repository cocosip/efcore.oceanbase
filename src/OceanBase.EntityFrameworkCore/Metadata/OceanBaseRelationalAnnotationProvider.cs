using System.Collections.Generic;
using System.Linq;
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
            if (designTime && column.DefaultValue is null && column.DefaultValueSql is null && column.ComputedColumnSql is null
                && column.PropertyMappings.Any(mapping => mapping.Property.IsPrimaryKey()
                    && mapping.Property.ValueGenerated == ValueGenerated.OnAdd
                    && (mapping.TypeMapping.Converter?.ProviderClrType ?? mapping.Property.ClrType) is var type
                    && (type == typeof(int) || type == typeof(long))))
                yield return new Annotation(IdentityAnnotation, true);
        }
    }
}
