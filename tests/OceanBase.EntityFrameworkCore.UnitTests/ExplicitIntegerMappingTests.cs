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
    public sealed class ExplicitIntegerMappingTests
    {
        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, "TINYINT")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "SMALLINT UNSIGNED")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "MEDIUMINT")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "MEDIUMINT UNSIGNED")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "INTEGER")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "BIGINT")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "BIGINT UNSIGNED")]
        [InlineData(OceanBaseCompatibilityMode.MySql, " int(11) unsigned ")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NUMBER")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NUMBER(3)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, " number(6,0) ")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "DECIMAL(8,0)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NUMERIC(9)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "INTEGER")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "SMALLINT")]
        public void Explicit_integral_store_type_is_retained(OceanBaseCompatibilityMode mode, string storeType)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(int), storeType)!;
            using var command = new OceanBaseCommand();
            Assert.Equal(storeType.Trim(), mapping.StoreType);
            var parameter = mapping.CreateParameter(command, "value", 1);
            Assert.Equal(DbType.Int32, parameter.DbType);
            Assert.Equal(0, parameter.Size);
            Assert.Equal(1, parameter.Value);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, "TINYINT", -128L, 127L)]
        [InlineData(OceanBaseCompatibilityMode.MySql, "TINYINT UNSIGNED", 0L, 255L)]
        [InlineData(OceanBaseCompatibilityMode.MySql, "SMALLINT", -32768L, 32767L)]
        [InlineData(OceanBaseCompatibilityMode.MySql, "MEDIUMINT", -8388608L, 8388607L)]
        [InlineData(OceanBaseCompatibilityMode.MySql, "MEDIUMINT UNSIGNED", 0L, 16777215L)]
        [InlineData(OceanBaseCompatibilityMode.MySql, "INT UNSIGNED", 0L, 4294967295L)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NUMBER(1)", -9L, 9L)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NUMBER(3,0)", -999L, 999L)]
        public void Physical_column_bounds_apply_to_writes_without_constraining_query_values(
            OceanBaseCompatibilityMode mode, string storeType, long minimum, long maximum)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(long), storeType)!;
            using var command = new OceanBaseCommand();
            Assert.Equal(minimum, mapping.CreateParameter(command, "value", minimum).Value);
            Assert.Equal(maximum, mapping.CreateParameter(command, "value", maximum).Value);
            var validation = Assert.IsAssignableFrom<IOceanBaseWriteValidation>(mapping);
            validation.ValidateWriteValue(minimum);
            validation.ValidateWriteValue(maximum);
            Assert.Throws<ArgumentOutOfRangeException>(() => validation.ValidateWriteValue(minimum - 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => validation.ValidateWriteValue(maximum + 1));
            Assert.Equal(maximum + 1, mapping.CreateParameter(command, "value", maximum + 1).Value);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, "VARCHAR(8)")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "NUMBER(3)")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "INT(0)")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "INT(256)")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "INT ZEROFILL")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "SERIAL")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "INT trailing")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "INT UNSIGNED")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "RAW(8)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NUMBER(0)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NUMBER(39)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NUMBER(3,1)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NUMBER(3,-1)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NUMBER(3) trailing")]
        public void Unsupported_integer_declarations_are_not_silently_ignored(OceanBaseCompatibilityMode mode, string storeType)
        {
            using var context = CreateContext(mode);
            Assert.ThrowsAny<ArgumentException>(() => context.GetService<IRelationalTypeMappingSource>()
                .FindMapping(typeof(int), storeType));
        }

        [Theory]
        [InlineData("NUMBER(20)")]
        [InlineData("NUMBER(38,0)")]
        [InlineData("NUMBER")]
        public void Oracle_unsigned_64_bit_range_is_not_lost_in_bounds_checks(string storeType)
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.Oracle);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(ulong), storeType)!;
            using var command = new OceanBaseCommand();
            Assert.Equal(ulong.MaxValue, mapping.CreateParameter(command, "value", ulong.MaxValue).Value);
            var smaller = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(ulong), "NUMBER(19)")!;
            Assert.Throws<ArgumentOutOfRangeException>(() => ((IOceanBaseWriteValidation)smaller).ValidateWriteValue(ulong.MaxValue));
        }

        [Fact]
        public void Oracle_precision_facets_and_clones_change_capacity_and_parameter_metadata_together()
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.Oracle);
            var source = context.GetService<IRelationalTypeMappingSource>();
            var mapping = source.FindMapping(typeof(int), storeTypeName: null, precision: 3, scale: 0)!;
            using var command = new OceanBaseCommand();
            Assert.Equal("NUMBER(3,0)", mapping.StoreType);
            Assert.Equal(3, mapping.Precision);
            Assert.Equal((byte)3, mapping.CreateParameter(command, "value", 999).Precision);
            Assert.Throws<ArgumentOutOfRangeException>(() => ((IOceanBaseWriteValidation)mapping).ValidateWriteValue(1000));
            var clone = mapping.WithPrecisionAndScale(5, 0);
            Assert.Equal("NUMBER(5,0)", clone.StoreType);
            Assert.Equal(1000, clone.CreateParameter(command, "value", 1000).Value);
            Assert.Equal((byte)5, clone.CreateParameter(command, "value", null, direction: ParameterDirection.Output).Precision);
            Assert.ThrowsAny<ArgumentException>(() => source.FindMapping(typeof(int), "NUMBER(4)", precision: 3));
            Assert.ThrowsAny<ArgumentException>(() => clone.WithPrecisionAndScale(5, 1));
        }

        [Fact]
        public void Mysql_display_width_does_not_become_storage_capacity_or_ef_precision()
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.MySql);
            var source = context.GetService<IRelationalTypeMappingSource>();
            var mapping = source.FindMapping(typeof(int), "INT(1)")!;
            using var command = new OceanBaseCommand();
            Assert.Equal("INT(1)", mapping.StoreType);
            Assert.Equal(1000000, mapping.CreateParameter(command, "value", 1000000).Value);
            Assert.Null(mapping.Size);
            Assert.Null(mapping.Precision);
            Assert.ThrowsAny<ArgumentException>(() => source.FindMapping(typeof(int), storeTypeName: null, precision: 3));
            Assert.ThrowsAny<ArgumentException>(() => mapping.WithPrecisionAndScale(3, null));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, "TINYINT")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NUMBER(2)")]
        public void Integer_converter_clones_preserve_bounds_and_null_output_semantics(OceanBaseCompatibilityMode mode, string storeType)
        {
            using var context = CreateContext(mode);
            var mapping = (RelationalTypeMapping)context.GetService<IRelationalTypeMappingSource>()
                .FindMapping(typeof(int), storeType)!.WithComposedConverter(new EnumToNumberConverter<DayOfWeek, int>());
            using var command = new OceanBaseCommand();
            Assert.Equal(5, mapping.CreateParameter(command, "value", DayOfWeek.Friday).Value);
            Assert.Throws<ArgumentOutOfRangeException>(() => ((IOceanBaseWriteValidation)mapping).ValidateWriteValue((DayOfWeek)1000));
            Assert.Equal(DBNull.Value, mapping.CreateParameter(command, "value", null, nullable: true).Value);
            var output = mapping.CreateParameter(command, "value", null, direction: ParameterDirection.Output);
            Assert.Equal(ParameterDirection.Output, output.Direction);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, "BOOLEAN")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "TINYINT(1)")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "SMALLINT UNSIGNED")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NUMBER(3)")]
        public void Boolean_explicit_storage_keeps_zero_one_binding(OceanBaseCompatibilityMode mode, string storeType)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(bool), storeType)!;
            using var command = new OceanBaseCommand();
            Assert.Equal(storeType, mapping.StoreType);
            Assert.Equal("1", mapping.GenerateSqlLiteral(true));
            Assert.Equal("0", mapping.GenerateSqlLiteral(false));
            var parameter = mapping.CreateParameter(command, "value", true);
            Assert.Equal(mode == OceanBaseCompatibilityMode.Oracle ? (object)1m : true, parameter.Value);
        }

        private static TestContext CreateContext(OceanBaseCompatibilityMode mode)
            => new(new DbContextOptionsBuilder<TestContext>().UseOceanBase("Server=localhost;Database=test", mode).Options);

        private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options);
    }
}
