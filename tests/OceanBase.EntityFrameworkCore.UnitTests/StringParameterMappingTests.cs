using System;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests
{
    public sealed class StringParameterMappingTests
    {
        [Theory]
        [InlineData("NCHAR(4)", OceanBaseDbType.NChar, DbType.String)]
        [InlineData("NVARCHAR2(4)", OceanBaseDbType.NVarchar2, DbType.String)]
        public void Oracle_national_parameters_select_native_character_set_and_preserve_query_length(
            string storeType, OceanBaseDbType providerType, DbType dbType)
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.Oracle);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(string), storeType)!;
            using var command = new OceanBaseCommand();
            foreach (var direction in new[] { ParameterDirection.Input, ParameterDirection.InputOutput })
            {
                var input = (OceanBaseParameter)mapping.CreateParameter(command, "caption", "海洋🌊数据库", direction: direction);
                Assert.Equal(providerType, input.OceanBaseDbType);
                Assert.Equal(dbType, input.DbType);
                Assert.Equal("海洋🌊数据库", input.Value);
                Assert.Equal("海洋🌊数据库".Length, input.Size);
            }

            Assert.Equal(4, mapping.Size);
            Assert.Equal(providerType == OceanBaseDbType.NChar, mapping.IsFixedLength);
            var output = (OceanBaseParameter)mapping.CreateParameter(command, "caption", null, direction: ParameterDirection.Output);
            Assert.Equal(providerType, output.OceanBaseDbType);
            Assert.Equal(4, output.Size);
            Assert.Null(output.Value);
            var nullInput = (OceanBaseParameter)mapping.CreateParameter(command, "caption", null);
            Assert.Equal(providerType, nullInput.OceanBaseDbType);
            Assert.Equal(DBNull.Value, nullInput.Value);
            Assert.Equal(4, nullInput.Size);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, "VARCHAR(4)")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "CHAR(4)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "VARCHAR2(4 CHAR)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "CHAR(4 CHAR)")]
        public void Ordinary_text_uses_generic_binding_with_lossless_query_size(OceanBaseCompatibilityMode mode, string storeType)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(string), storeType)!;
            using var command = new OceanBaseCommand();
            var input = (OceanBaseParameter)mapping.CreateParameter(command, "caption", "longer query");
            Assert.Null(input.OceanBaseDbType);
            Assert.Equal(12, input.Size);
            Assert.Equal("longer query", input.Value);
        }

        [Fact]
        public void National_binding_survives_composed_converter_and_clone()
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.Oracle);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(string), "NVARCHAR2(4)")!;
            var converted = (RelationalTypeMapping)mapping.WithComposedConverter(
                new ValueConverter<Caption, string>(caption => caption.Value, value => new Caption(value)));
            using var command = new OceanBaseCommand();
            var input = (OceanBaseParameter)converted.CreateParameter(command, "caption", new Caption("longer query"));
            Assert.Equal(OceanBaseDbType.NVarchar2, input.OceanBaseDbType);
            Assert.Equal("longer query", input.Value);
            Assert.Equal(12, input.Size);
        }

        [Fact]
        public void Unicode_Guid_text_binding_uses_national_type()
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.Oracle);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(Guid), "NVARCHAR2(36)")!;
            var value = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
            using var command = new OceanBaseCommand();
            var input = (OceanBaseParameter)mapping.CreateParameter(command, "token", value);
            Assert.Equal(OceanBaseDbType.NVarchar2, input.OceanBaseDbType);
            Assert.Equal(value.ToString(), input.Value);
            Assert.Equal(36, input.Size);
        }

        private static TestContext CreateContext(OceanBaseCompatibilityMode mode)
            => new(new DbContextOptionsBuilder<TestContext>().UseOceanBase("Server=localhost;Database=test", mode).Options);

        private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options);
        private sealed record Caption(string Value);
    }
}
