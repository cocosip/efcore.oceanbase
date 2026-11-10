using System;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OceanBase.EntityFrameworkCore.Storage;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests
{
    public sealed class StringMappingTests
    {
        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, "CHAR", 1, true, true)]
        [InlineData(OceanBaseCompatibilityMode.MySql, " char ( 4 ) ", 4, true, true)]
        [InlineData(OceanBaseCompatibilityMode.MySql, "VARCHAR(16383)", 16383, false, true)]
        [InlineData(OceanBaseCompatibilityMode.MySql, "TEXT", null, false, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NCHAR", 1, true, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "CHAR", 1, true, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, " varchar2 ( 4 byte ) ", 4, false, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "CHAR(2000 CHAR)", 2000, true, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NCHAR(1000)", 1000, true, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NVARCHAR2(2000)", 2000, false, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "CLOB", null, false, false)]
        public void Full_declarations_preserve_normalized_facets(OceanBaseCompatibilityMode mode,
            string declaration, int? size, bool fixedLength, bool unicode)
        {
            using var context = CreateContext(mode);
            var mapping = Source(context).FindMapping(typeof(string), declaration)!;
            Assert.Equal(declaration.Trim(), mapping.StoreType);
            Assert.Equal(size, mapping.Size);
            Assert.Equal(fixedLength, mapping.IsFixedLength);
            Assert.Equal(unicode, mapping.IsUnicode);
            using var command = new OceanBaseCommand();
            Assert.Equal(size ?? 0, mapping.CreateParameter(command, "text", null, direction: ParameterDirection.Output).Size);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, "VARCHAR")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "VARCHAR(0)")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "VARCHAR(16384)")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "CHAR(256)")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "CHAR(4) ignored")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "VARCHAR(4,2)")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "VARCHAR(4 CHAR)")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "TEXT(4)")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "NVARCHAR2(4)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "VARCHAR2")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "VARCHAR2(4001)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NVARCHAR2(2001)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NCHAR(1001)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "CHAR(2001)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "CHAR(-1)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "VARCHAR2(9999999999999999999)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "VARCHAR2(4 FOO)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NVARCHAR2(4 CHAR)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NCLOB(4)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "VARCHAR(4)")]
        public void Invalid_declarations_reject_instead_of_silently_mapping(OceanBaseCompatibilityMode mode, string declaration)
        {
            using var context = CreateContext(mode);
            Assert.ThrowsAny<ArgumentException>(() => Source(context).FindMapping(typeof(string), declaration));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Conflicting_facets_and_lob_indexes_reject_on_direct_and_property_paths(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var source = Source(context);
            var declaration = mode == OceanBaseCompatibilityMode.Oracle ? "NVARCHAR2(4)" : "VARCHAR(4)";
            Assert.ThrowsAny<ArgumentException>(() => source.FindMapping(typeof(string), declaration, fixedLength: true));
            Assert.ThrowsAny<ArgumentException>(() => source.FindMapping(typeof(string), declaration, size: 3));
            Assert.ThrowsAny<ArgumentException>(() => source.FindMapping(typeof(string), declaration, precision: 4));
            Assert.ThrowsAny<ArgumentException>(() => source.FindMapping(typeof(string), declaration, scale: 1));
            Assert.ThrowsAny<ArgumentException>(() => source.FindMapping(typeof(string), "CHAR", size: 4));
            Assert.ThrowsAny<ArgumentException>(() => source.FindMapping(typeof(string),
                mode == OceanBaseCompatibilityMode.Oracle ? " NCLOB " : " LONGTEXT ", keyOrIndex: true));
            Assert.ThrowsAny<ArgumentException>(() => source.FindMapping(typeof(string), null, size: 20000, keyOrIndex: true));
            var builder = new ModelBuilder(new ConventionSet());
            var entity = builder.Entity<TextRow>();
            entity.Property(row => row.Text).HasColumnType(declaration).IsFixedLength();
            Assert.ThrowsAny<ArgumentException>(() => source.FindMapping((IProperty)entity.Metadata.FindProperty(nameof(TextRow.Text))!));
            if (mode == OceanBaseCompatibilityMode.Oracle)
            {
                Assert.ThrowsAny<ArgumentException>(() => source.FindMapping(typeof(string), declaration, unicode: false));
                entity.Property(row => row.Text).IsFixedLength(false).IsUnicode(false);
                Assert.ThrowsAny<ArgumentException>(() => source.FindMapping((IProperty)entity.Metadata.FindProperty(nameof(TextRow.Text))!));
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Converted_writes_count_characters_while_queries_keep_lossless_transport(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var mapping = Source(context).FindMapping(typeof(string), null, size: 2)!;
            var validation = Assert.IsAssignableFrom<IOceanBaseWriteValidation>(mapping);
            foreach (var value in new object?[] { null, DBNull.Value, "", "海🌊", "a\u0301" }) validation.ValidateWriteValue(value);
            Assert.Throws<ArgumentException>(() => validation.ValidateWriteValue("海🌊洋"));
            using var command = new OceanBaseCommand();
            var parameter = mapping.CreateParameter(command, "text", "longer🌊query");
            Assert.Equal("longer🌊query", parameter.Value);
            Assert.Equal("longer🌊query".Length, parameter.Size);
            Assert.Contains("longer🌊query", mapping.GenerateSqlLiteral("longer🌊query"));
            var converted = (RelationalTypeMapping)mapping.WithStoreTypeAndSize(
                mode == OceanBaseCompatibilityMode.Oracle ? "NVARCHAR2(4)" : "VARCHAR(4)", 4)
                .WithComposedConverter(new ValueConverter<Caption, string>(value => value.Value + "!", value => new Caption(value)));
            var convertedValidation = Assert.IsAssignableFrom<IOceanBaseWriteValidation>(converted);
            convertedValidation.ValidateWriteValue(new Caption("海🌊洋"));
            Assert.Throws<ArgumentException>(() => convertedValidation.ValidateWriteValue(new Caption("海🌊洋河")));
            Assert.Equal("海🌊洋!", converted.CreateParameter(command, "text", new Caption("海🌊洋")).Value);
            Assert.ThrowsAny<ArgumentException>(() => mapping.WithPrecisionAndScale(3, null));
            Assert.ThrowsAny<ArgumentException>(() => mapping.WithStoreTypeAndSize("CHAR(2) ignored", 2));
            var lob = Source(context).FindMapping(typeof(string), mode == OceanBaseCompatibilityMode.Oracle ? "NCLOB" : "LONGTEXT", size: 2)!;
            Assert.Throws<ArgumentException>(() => ((IOceanBaseWriteValidation)lob).ValidateWriteValue("abc"));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Invalid_unicode_never_becomes_replacement_text(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var mapping = Source(context).FindMapping(typeof(string), null, size: 4)!;
            using var command = new OceanBaseCommand();
            foreach (var value in new[] { "\ud800", "x\udc00", "\ud800x", "\\\udfff" })
            {
                Assert.Throws<ArgumentException>(() => mapping.CreateParameter(command, "text", value));
                Assert.Throws<ArgumentException>(() => mapping.GenerateSqlLiteral(value));
                Assert.Throws<ArgumentException>(() => ((IOceanBaseWriteValidation)mapping).ValidateWriteValue(value));
            }
        }

        private static IRelationalTypeMappingSource Source(DbContext context) => context.GetService<IRelationalTypeMappingSource>();
        private static TestContext CreateContext(OceanBaseCompatibilityMode mode)
            => new(new DbContextOptionsBuilder<TestContext>().UseOceanBase("Server=localhost;Database=test", mode).Options);
        private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options);
        private sealed class TextRow { public string Text { get; set; } = string.Empty; }
        private sealed record Caption(string Value);
    }
}
