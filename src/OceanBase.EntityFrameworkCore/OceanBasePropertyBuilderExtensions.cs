using System;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OceanBase.EntityFrameworkCore.Metadata;

namespace Microsoft.EntityFrameworkCore
{
    /// <summary>Configures OceanBase property value generation.</summary>
    public static class OceanBasePropertyBuilderExtensions
    {
        /// <summary>Allocates keys from a model sequence declared with <c>HasSequence&lt;long&gt;</c>.</summary>
        public static PropertyBuilder UseOceanBaseHiLo(this PropertyBuilder builder, string sequenceName, string? schema = null)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrWhiteSpace(sequenceName);
            if (schema is not null) ArgumentException.ThrowIfNullOrWhiteSpace(schema);

            builder.Metadata.SetAnnotation(OceanBaseRelationalAnnotationProvider.HiLoSequenceName, sequenceName);
            builder.Metadata.SetOrRemoveAnnotation(OceanBaseRelationalAnnotationProvider.HiLoSequenceSchema, schema);
            return builder.ValueGeneratedOnAdd();
        }

        /// <inheritdoc cref="UseOceanBaseHiLo(PropertyBuilder, string, string?)" />
        public static PropertyBuilder<TProperty> UseOceanBaseHiLo<TProperty>(this PropertyBuilder<TProperty> builder,
            string sequenceName, string? schema = null)
            => (PropertyBuilder<TProperty>)UseOceanBaseHiLo((PropertyBuilder)builder, sequenceName, schema);
    }
}
