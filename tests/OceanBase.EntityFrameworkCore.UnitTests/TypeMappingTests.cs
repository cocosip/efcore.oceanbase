using System;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBase.EntityFrameworkCore;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests
{
    public sealed class TypeMappingTests
    {
        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, typeof(int), "INT")]
        [InlineData(OceanBaseCompatibilityMode.MySql, typeof(long), "BIGINT")]
        [InlineData(OceanBaseCompatibilityMode.MySql, typeof(decimal), "DECIMAL(65,30)")]
        [InlineData(OceanBaseCompatibilityMode.MySql, typeof(string), "LONGTEXT")]
        [InlineData(OceanBaseCompatibilityMode.MySql, typeof(byte[]), "BLOB")]
        [InlineData(OceanBaseCompatibilityMode.MySql, typeof(DateTime), "DATETIME(6)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(int), "NUMBER(10)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(long), "NUMBER(19)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(string), "NVARCHAR2(2000)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(byte[]), "BLOB")]
        public void Maps_common_clr_types_to_oceanbase_store_types(
            OceanBaseCompatibilityMode mode,
            Type clrType,
            string storeType)
        {
            using var context = new TestContext(
                new DbContextOptionsBuilder<TestContext>()
                    .UseOceanBase("Server=localhost;Database=test", mode)
                    .Options);

            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(clrType);

            Assert.NotNull(mapping);
            Assert.Equal(storeType, mapping!.StoreType);
            Assert.NotNull(mapping.DbType);
        }

        [Fact]
        public void Maps_nullable_scalar_types_to_the_underlying_store_type()
        {
            using var context = new TestContext(
                new DbContextOptionsBuilder<TestContext>()
                    .UseOceanBase("Server=localhost;Database=test", OceanBaseCompatibilityMode.MySql)
                    .Options);

            Assert.Equal("INT", context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(int?))!.StoreType);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, false, "VARCHAR(64)", DbType.String)]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, true, "CHAR(64)", DbType.AnsiStringFixedLength)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, false, "NVARCHAR2(64)", DbType.String)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, false, "VARCHAR2(64 CHAR)", DbType.AnsiString)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, true, "NCHAR(64)", DbType.StringFixedLength)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, true, "CHAR(64 CHAR)", DbType.AnsiStringFixedLength)]
        public void String_facets_reach_store_type_and_connector_parameter(
            OceanBaseCompatibilityMode mode, bool unicode, bool fixedLength, string storeType, DbType dbType)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(
                typeof(string), storeTypeName: null, unicode: unicode, size: 64, fixedLength: fixedLength)!;
            using var command = new OceanBaseConnector.OceanBaseCommand();
            var parameter = mapping.CreateParameter(command, "value", "OceanBase");

            Assert.Equal(storeType, mapping.StoreType);
            Assert.Equal(unicode, mapping.IsUnicode);
            Assert.Equal(fixedLength, mapping.IsFixedLength);
            Assert.Equal(dbType, parameter.DbType);
            Assert.Equal(64, parameter.Size);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, "DECIMAL(18,6)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NUMBER(18,6)")]
        public void Decimal_facets_reach_store_type_and_connector_parameter(OceanBaseCompatibilityMode mode, string storeType)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(
                typeof(decimal), storeTypeName: null, precision: 18, scale: 6)!;
            using var command = new OceanBaseConnector.OceanBaseCommand();
            var parameter = mapping.CreateParameter(command, "value", 123.456789m);

            Assert.Equal(storeType, mapping.StoreType);
            Assert.Equal(18, mapping.Precision);
            Assert.Equal(6, mapping.Scale);
            Assert.Equal((byte)18, parameter.Precision);
            Assert.Equal((byte)6, parameter.Scale);
            Assert.Equal(123.456789m, parameter.Value);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, "DECIMAL(20,4)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NUMBER(20,4)")]
        public void Explicit_decimal_store_type_is_preserved(OceanBaseCompatibilityMode mode, string storeType)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(decimal), storeType)!;
            using var command = new OceanBaseConnector.OceanBaseCommand();
            var parameter = mapping.CreateParameter(command, "value", 12.3456m);

            Assert.Equal(storeType, mapping.StoreType);
            Assert.Equal((byte)20, parameter.Precision);
            Assert.Equal((byte)4, parameter.Scale);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, "VARCHAR(255)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NVARCHAR2(255)")]
        public void String_keys_use_bounded_storage(OceanBaseCompatibilityMode mode, string storeType)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(string), storeTypeName: null, keyOrIndex: true)!;

            Assert.Equal(storeType, mapping.StoreType);
            Assert.Equal(255, mapping.Size);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, 0, 0)]
        [InlineData(OceanBaseCompatibilityMode.MySql, 66, 0)]
        [InlineData(OceanBaseCompatibilityMode.MySql, 20, 31)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, 39, 0)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, 10, 128)]
        public void Invalid_decimal_facets_fail_clearly(OceanBaseCompatibilityMode mode, int precision, int scale)
        {
            using var context = CreateContext(mode);
            Assert.Throws<ArgumentOutOfRangeException>(() => context.GetService<IRelationalTypeMappingSource>()
                .FindMapping(typeof(decimal), storeTypeName: null, precision: precision, scale: scale));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, "VARCHAR(32)", 32, false, true)]
        [InlineData(OceanBaseCompatibilityMode.MySql, "CHAR(16)", 16, true, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NVARCHAR2(32)", 32, false, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "VARCHAR2(32 CHAR)", 32, false, false)]
        public void Explicit_string_type_preserves_facets(
            OceanBaseCompatibilityMode mode, string storeType, int size, bool fixedLength, bool unicode)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(string), storeType)!;

            Assert.Equal(storeType, mapping.StoreType);
            Assert.Equal(size, mapping.Size);
            Assert.Equal(fixedLength, mapping.IsFixedLength);
            Assert.Equal(unicode, mapping.IsUnicode);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, 20000, "LONGTEXT")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, 3000, "NCLOB")]
        public void Oversized_variable_strings_use_lob_storage(OceanBaseCompatibilityMode mode, int size, string storeType)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(string), null, size: size)!;
            Assert.Equal(storeType, mapping.StoreType);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, 256)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, 1001)]
        public void Oversized_fixed_strings_do_not_silently_change_storage(OceanBaseCompatibilityMode mode, int size)
        {
            using var context = CreateContext(mode);
            Assert.Throws<ArgumentOutOfRangeException>(() => context.GetService<IRelationalTypeMappingSource>()
                .FindMapping(typeof(string), null, size: size, fixedLength: true));
        }

        [Theory]
        [InlineData(10, -2, 1200)]
        [InlineData(10, 11, 0)]
        public void Oracle_scale_can_be_negative_or_exceed_precision(int precision, int scale, int value)
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.Oracle);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(
                typeof(decimal), null, precision: precision, scale: scale)!;
            using var command = new OceanBaseConnector.OceanBaseCommand();
            var parameter = (OceanBaseConnector.OceanBaseParameter)mapping.CreateParameter(command, "value", (decimal)value);

            Assert.Equal($"NUMBER({precision},{scale})", mapping.StoreType);
            Assert.Equal(scale, parameter.DecimalScale);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Null_output_parameters_preserve_facets_and_direction(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var source = context.GetService<IRelationalTypeMappingSource>();
            using var command = new OceanBaseConnector.OceanBaseCommand();
            var text = source.FindMapping(typeof(string), null, size: 32)!
                .CreateParameter(command, "text", null, nullable: true, direction: ParameterDirection.Output);
            var number = source.FindMapping(typeof(decimal), null, precision: 18, scale: 4)!
                .CreateParameter(command, "number", null, nullable: true, direction: ParameterDirection.Output);

            Assert.Null(text.Value);
            Assert.Null(number.Value);
            var nullInput = source.FindMapping(typeof(string), null, size: 32)!.CreateParameter(command, "input", null);
            Assert.Equal(DBNull.Value, nullInput.Value);
            Assert.Equal(32, text.Size);
            Assert.Equal((byte)18, number.Precision);
            Assert.Equal((byte)4, number.Scale);
            Assert.Equal(ParameterDirection.Output, text.Direction);
            Assert.Equal(ParameterDirection.Output, number.Direction);
        }

        private static TestContext CreateContext(OceanBaseCompatibilityMode mode)
            => new(new DbContextOptionsBuilder<TestContext>().UseOceanBase("Server=localhost;Database=test", mode).Options);

        private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options);
    }
}
