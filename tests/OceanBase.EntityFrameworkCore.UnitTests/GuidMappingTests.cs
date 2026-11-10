using System;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests
{
    public sealed class GuidMappingTests
    {
        private static readonly Guid Token = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, "VARCHAR(36)", DbType.AnsiString, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, " binary ( 16 ) ", DbType.Binary, true)]
        [InlineData(OceanBaseCompatibilityMode.MySql, "VARBINARY(16)", DbType.Binary, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, " char(36 byte) ", DbType.AnsiStringFixedLength, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "CHAR(36 CHAR)", DbType.AnsiStringFixedLength, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "VARCHAR2(36)", DbType.AnsiString, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "VARCHAR2(36 CHAR)", DbType.AnsiString, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NCHAR(36)", DbType.String, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NVARCHAR2(36)", DbType.String, false)]
        public void Explicit_declaration_controls_Guid_transport_and_literals(
            OceanBaseCompatibilityMode mode, string storeType, DbType dbType, bool fixedLength)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(Guid), storeType)!;
            using var command = new OceanBaseCommand();
            var parameter = mapping.CreateParameter(command, "token", Token);
            Assert.Equal(storeType.Trim(), mapping.StoreType);
            Assert.Equal(dbType, parameter.DbType);
            Assert.Equal(fixedLength, mapping.IsFixedLength);
            Assert.Equal(dbType == DbType.Binary ? 16 : 36, parameter.Size);
            if (dbType == DbType.Binary)
            {
                Assert.Equal(new byte[] { 0x33, 0x22, 0x11, 0, 0x55, 0x44, 0x77, 0x66, 0x88, 0x99, 0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0xff },
                    Assert.IsType<byte[]>(parameter.Value));
                Assert.Equal("X'33221100554477668899AABBCCDDEEFF'", mapping.GenerateSqlLiteral(Token));
            }
            else
            {
                Assert.Equal("00112233-4455-6677-8899-aabbccddeeff", parameter.Value);
                Assert.Equal((dbType is DbType.String or DbType.StringFixedLength ? "N" : string.Empty)
                    + "'00112233-4455-6677-8899-aabbccddeeff'", mapping.GenerateSqlLiteral(Token));
            }

            Assert.Equal(Token, mapping.Converter!.ConvertFromProvider(parameter.Value));
            Assert.Equal(DBNull.Value, mapping.CreateParameter(command, "token", null, nullable: true).Value);
            Assert.Equal("NULL", mapping.GenerateSqlLiteral(null));
            var composed = (RelationalTypeMapping)mapping.WithComposedConverter(
                new ValueConverter<GuidValue, Guid>(value => value.Value, value => new GuidValue(value)));
            var composedParameter = composed.CreateParameter(command, "token", new GuidValue(Token));
            Assert.Equal(new GuidValue(Token), composed.Converter!.ConvertFromProvider(composedParameter.Value));
            Assert.Equal(mapping.StoreType, composed.StoreType);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, "CHAR(32)")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "BINARY(20)")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "BINARY")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "RAW(16)")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "VARCHAR2(36)")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "LONGTEXT")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "CHAR(36) ignored")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "RAW(17)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "CHAR(40)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "CHAR(36 FOO)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "BINARY(16)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "VARCHAR(36)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NCLOB")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NUMBER(38)")]
        public void Unsupported_Guid_declarations_reject_instead_of_using_the_default(
            OceanBaseCompatibilityMode mode, string storeType)
        {
            using var context = CreateContext(mode);
            Assert.ThrowsAny<ArgumentException>(() => context.GetService<IRelationalTypeMappingSource>()
                .FindMapping(typeof(Guid), storeType));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, "VARCHAR(36)", 12, null, null, null, null)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "VARCHAR2(36 CHAR)", 12, null, null, null, null)]
        [InlineData(OceanBaseCompatibilityMode.MySql, null, 32, null, null, null, null)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, null, null, true, null, null, null)]
        [InlineData(OceanBaseCompatibilityMode.MySql, "BINARY(16)", null, null, true, null, null)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NCHAR(36)", null, null, false, null, null)]
        [InlineData(OceanBaseCompatibilityMode.MySql, "VARCHAR(36)", null, true, null, null, null)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "RAW(16)", null, null, null, 3, null)]
        [InlineData(OceanBaseCompatibilityMode.MySql, null, null, null, null, null, 0)]
        public void Conflicting_Guid_facets_reject(OceanBaseCompatibilityMode mode, string? storeType,
            int? size, bool? fixedLength, bool? unicode, int? precision, int? scale)
        {
            using var context = CreateContext(mode);
            Assert.ThrowsAny<ArgumentException>(() => context.GetService<IRelationalTypeMappingSource>()
                .FindMapping(typeof(Guid), storeType, size: size, fixedLength: fixedLength,
                    unicode: unicode, precision: precision, scale: scale));
        }

        [Fact]
        public void Variable_unicode_Guid_default_uses_matching_string_binding()
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.MySql);
            var mapping = context.GetService<IRelationalTypeMappingSource>()
                .FindMapping(typeof(Guid), null, fixedLength: false, unicode: true)!;
            using var command = new OceanBaseCommand();
            Assert.Equal("VARCHAR(36)", mapping.StoreType);
            Assert.Equal(DbType.String, mapping.CreateParameter(command, "token", Token).DbType);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, "VARCHAR(36)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "VARCHAR2(36 CHAR)")]
        public void Conflicting_text_lengths_also_reject_for_string_properties(
            OceanBaseCompatibilityMode mode, string storeType)
        {
            using var context = CreateContext(mode);
            Assert.ThrowsAny<ArgumentException>(() => context.GetService<IRelationalTypeMappingSource>()
                .FindMapping(typeof(string), storeType, size: 12));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, typeof(Guid), "VARCHAR(36)", 36)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(Guid), "VARCHAR2(36 CHAR)", 36)]
        [InlineData(OceanBaseCompatibilityMode.MySql, typeof(string), "CHAR(36)", 36)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(string), "NVARCHAR2(36)", 36)]
        [InlineData(OceanBaseCompatibilityMode.MySql, typeof(byte[]), "VARBINARY(16)", 16)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(byte[]), "RAW(16)", 16)]
        public void Property_mapping_preserves_length_conflicts_before_parsed_facets_override_them(
            OceanBaseCompatibilityMode mode, Type clrType, string storeType, int size)
        {
            using var context = CreateContext(mode);
            var modelBuilder = new ModelBuilder();
            var property = modelBuilder.Entity("FacetRow").Property(clrType, "Value")
                .HasColumnType(storeType).HasMaxLength(12).Metadata;
            var source = context.GetService<IRelationalTypeMappingSource>();
            Assert.ThrowsAny<ArgumentException>(() => source.FindMapping((IProperty)property));
            property.SetMaxLength(size);
            Assert.Equal(size, source.FindMapping((IProperty)property)!.Size);
        }

        private static TestContext CreateContext(OceanBaseCompatibilityMode mode)
            => new(new DbContextOptionsBuilder<TestContext>().UseOceanBase("Server=localhost;Database=test", mode).Options);

        private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options);
        private sealed record GuidValue(Guid Value);
    }
}
