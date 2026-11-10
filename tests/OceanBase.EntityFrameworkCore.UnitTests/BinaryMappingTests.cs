using System;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OceanBase.EntityFrameworkCore.Storage;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests
{
    public sealed class BinaryMappingTests
    {
        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, null, false, false, "LONGBLOB", null)]
        [InlineData(OceanBaseCompatibilityMode.MySql, 8, false, false, "VARBINARY(8)", 8)]
        [InlineData(OceanBaseCompatibilityMode.MySql, 8, true, false, "BINARY(8)", 8)]
        [InlineData(OceanBaseCompatibilityMode.MySql, 262144, false, false, "VARBINARY(262144)", 262144)]
        [InlineData(OceanBaseCompatibilityMode.MySql, 262145, false, false, "LONGBLOB", 262145)]
        [InlineData(OceanBaseCompatibilityMode.MySql, null, false, true, "VARBINARY(255)", 255)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, null, false, false, "BLOB", null)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, 8, false, false, "RAW(8)", 8)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, 2000, false, false, "RAW(2000)", 2000)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, 2001, false, false, "BLOB", 2001)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, null, false, true, "RAW(255)", 255)]
        public void Binary_facets_select_storage_and_parameter_metadata(OceanBaseCompatibilityMode mode,
            int? size, bool fixedLength, bool key, string storeType, int? expectedSize)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(byte[]),
                storeTypeName: null, size: size, fixedLength: fixedLength, keyOrIndex: key)!;
            using var command = new OceanBaseCommand();
            var bytes = new byte[] { 0, 0x80, 0xff };
            var parameter = mapping.CreateParameter(command, "data", bytes);

            Assert.Equal(storeType, mapping.StoreType);
            Assert.Equal(expectedSize, mapping.Size);
            Assert.Equal(fixedLength, mapping.IsFixedLength);
            Assert.Equal(DbType.Binary, parameter.DbType);
            Assert.Equal(expectedSize ?? 0, parameter.Size);
            Assert.Equal(bytes, Assert.IsType<byte[]>(parameter.Value));
            Assert.Null(Assert.IsType<OceanBaseParameter>(parameter).OceanBaseDbType);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, "BINARY", 1, true)]
        [InlineData(OceanBaseCompatibilityMode.MySql, "binary(8)", 8, true)]
        [InlineData(OceanBaseCompatibilityMode.MySql, "VARBINARY(8)", 8, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, "TINYBLOB", null, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, "BLOB", null, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, "MEDIUMBLOB", null, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, "LONGBLOB", null, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "RAW(8)", 8, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "BLOB", null, false)]
        public void Explicit_binary_declarations_are_preserved(OceanBaseCompatibilityMode mode,
            string storeType, int? size, bool fixedLength)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(byte[]), storeType)!;
            Assert.Equal(storeType, mapping.StoreType);
            Assert.Equal(size, mapping.Size);
            Assert.Equal(fixedLength, mapping.IsFixedLength);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, "RAW(8)")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "VARBINARY")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "VARBINARY(0)")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "VARBINARY(262145)")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "BINARY(256)")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "BLOB(8)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "RAW")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "RAW(2001)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "VARBINARY(8)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "BLOB trailing")]
        public void Invalid_binary_declarations_fail_clearly(OceanBaseCompatibilityMode mode, string storeType)
        {
            using var context = CreateContext(mode);
            Assert.ThrowsAny<ArgumentException>(() => context.GetService<IRelationalTypeMappingSource>()
                .FindMapping(typeof(byte[]), storeType));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Invalid_facets_and_lob_keys_are_rejected(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var source = context.GetService<IRelationalTypeMappingSource>();
            var storeType = mode == OceanBaseCompatibilityMode.Oracle ? "RAW(8)" : "VARBINARY(8)";
            Assert.ThrowsAny<ArgumentException>(() => source.FindMapping(typeof(byte[]), storeType, size: 4));
            Assert.ThrowsAny<ArgumentException>(() => source.FindMapping(typeof(byte[]), storeType, fixedLength: true));
            Assert.ThrowsAny<ArgumentException>(() => source.FindMapping(typeof(byte[]), storeTypeName: null, size: 0));
            Assert.ThrowsAny<ArgumentException>(() => source.FindMapping(typeof(byte[]), storeTypeName: null, size: -1));
            Assert.ThrowsAny<ArgumentException>(() => source.FindMapping(typeof(byte[]), storeTypeName: null, precision: 4));
            Assert.ThrowsAny<ArgumentException>(() => source.FindMapping(typeof(byte[]), storeTypeName: null, scale: 1));
            Assert.ThrowsAny<ArgumentException>(() => source.FindMapping(typeof(byte[]), "BLOB", keyOrIndex: true));
            if (mode == OceanBaseCompatibilityMode.Oracle)
            {
                Assert.ThrowsAny<ArgumentException>(() => source.FindMapping(typeof(byte[]), storeTypeName: null,
                    size: 8, fixedLength: true));
            }
            else
            {
                Assert.ThrowsAny<ArgumentException>(() => source.FindMapping(typeof(byte[]), storeTypeName: null,
                    size: 536870911));
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Bounded_values_reject_truncation_and_preserve_null_and_output_facets(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(byte[]),
                storeTypeName: null, size: 4)!;
            using var command = new OceanBaseCommand();
            var bytes = new byte[] { 0, 0x80, 0xff, 0 };
            Assert.Equal(bytes, Assert.IsType<byte[]>(mapping.CreateParameter(command, "data", bytes).Value));
            var validation = Assert.IsAssignableFrom<IOceanBaseWriteValidation>(mapping);
            validation.ValidateWriteValue(bytes);
            Assert.Throws<ArgumentException>(() => validation.ValidateWriteValue(new byte[5]));
            Assert.Equal(new byte[5], Assert.IsType<byte[]>(mapping.CreateParameter(command, "data", new byte[5]).Value));
            Assert.Equal(DBNull.Value, mapping.CreateParameter(command, "data", null, nullable: true).Value);
            Assert.Equal("NULL", mapping.GenerateSqlLiteral(null));
            var output = mapping.CreateParameter(command, "data", null, direction: ParameterDirection.Output);
            Assert.Equal(ParameterDirection.Output, output.Direction);
            Assert.Equal(4, output.Size);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, "TINYBLOB", 256)]
        [InlineData(OceanBaseCompatibilityMode.MySql, "BLOB", 65536)]
        [InlineData(OceanBaseCompatibilityMode.MySql, "MEDIUMBLOB", 16777216)]
        public void Explicit_lob_capacity_is_validated_before_execution(OceanBaseCompatibilityMode mode, string storeType, int size)
        {
            using var context = CreateContext(mode);
            var source = context.GetService<IRelationalTypeMappingSource>();
            Assert.ThrowsAny<ArgumentException>(() => source.FindMapping(typeof(byte[]), storeType, size: size));
            if (size <= 65536)
            {
                using var command = new OceanBaseCommand();
                var mapping = source.FindMapping(typeof(byte[]), storeType)!;
                Assert.Throws<ArgumentException>(() => ((IOceanBaseWriteValidation)mapping).ValidateWriteValue(new byte[size]));
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Clone_and_converter_composition_keep_binary_validation(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var source = context.GetService<IRelationalTypeMappingSource>();
            var mapping = source.FindMapping(typeof(byte[]), storeTypeName: null, size: 4)!;
            var storeType = mode == OceanBaseCompatibilityMode.Oracle ? "RAW(8)" : "VARBINARY(8)";
            var clone = (RelationalTypeMapping)mapping.WithStoreTypeAndSize(storeType, 8)
                .WithComposedConverter(new ValueConverter<string, byte[]>(value => Convert.FromHexString(value),
                    value => Convert.ToHexString(value)));
            using var command = new OceanBaseCommand();
            Assert.Equal(8, clone.CreateParameter(command, "data", "0001020304050607").Size);
            Assert.Throws<ArgumentException>(() => ((IOceanBaseWriteValidation)clone).ValidateWriteValue("000102030405060708"));
            Assert.ThrowsAny<ArgumentException>(() => mapping.WithPrecisionAndScale(3, null));
        }

        [Fact]
        public void Mysql_blob_whitespace_cannot_bypass_key_validation()
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.MySql);
            var source = context.GetService<IRelationalTypeMappingSource>();
            Assert.ThrowsAny<ArgumentException>(() => source.FindMapping(typeof(byte[]), "BLOB ", keyOrIndex: true));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, "BLOB ", "BLOB", "X'01'")]
        [InlineData(OceanBaseCompatibilityMode.MySql, " LONGBLOB", "LONGBLOB", "X'01'")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, " BLOB ", "BLOB", "HEXTORAW('01')")]
        public void Binary_declaration_whitespace_is_normalized_for_parameters_and_literals(
            OceanBaseCompatibilityMode mode, string declaration, string storeType, string literal)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(byte[]), declaration)!;
            using var command = new OceanBaseCommand();
            Assert.Equal(storeType, mapping.StoreType);
            Assert.Equal(new byte[] { 1 }, Assert.IsType<byte[]>(mapping.CreateParameter(command, "data", new byte[] { 1 }).Value));
            Assert.Equal(literal, mapping.GenerateSqlLiteral(new byte[] { 1 }));
        }

        [Fact]
        public void Explicit_binary_key_keeps_the_store_default_length()
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.MySql);
            var mapping = context.GetService<IRelationalTypeMappingSource>()
                .FindMapping(typeof(byte[]), "BINARY", keyOrIndex: true)!;
            Assert.Equal("BINARY", mapping.StoreType);
            Assert.Equal(1, mapping.Size);
        }

        [Fact]
        public void Unbounded_mysql_binary_does_not_have_the_old_blob_64k_limit()
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.MySql);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(byte[]))!;
            using var command = new OceanBaseCommand();
            var bytes = new byte[65536];
            Assert.Equal("LONGBLOB", mapping.StoreType);
            Assert.Equal(bytes, Assert.IsType<byte[]>(mapping.CreateParameter(command, "data", bytes).Value));
            Assert.Equal("X''", mapping.GenerateSqlLiteral(Array.Empty<byte>()));
        }

        [Fact]
        public void Oracle_rejects_unverified_empty_binary_inputs_and_large_raw_literals()
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.Oracle);
            var source = context.GetService<IRelationalTypeMappingSource>();
            using var command = new OceanBaseCommand();
            foreach (var storeType in new[] { "RAW(8)", "BLOB" })
            {
                var mapping = source.FindMapping(typeof(byte[]), storeType)!;
                Assert.Throws<NotSupportedException>(() => mapping.CreateParameter(command, "data", Array.Empty<byte>()));
                Assert.Throws<NotSupportedException>(() => mapping.GenerateSqlLiteral(Array.Empty<byte>()));
            }

            var blob = source.FindMapping(typeof(byte[]), "BLOB")!;
            Assert.Throws<NotSupportedException>(() => blob.GenerateSqlLiteral(new byte[2001]));
            Assert.Equal("HEXTORAW('0080FF')", blob.GenerateSqlLiteral(new byte[] { 0, 0x80, 0xff }));
        }

        private static TestContext CreateContext(OceanBaseCompatibilityMode mode)
            => new(new DbContextOptionsBuilder<TestContext>().UseOceanBase("Server=localhost;Database=test", mode).Options);

        private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options);
    }
}
