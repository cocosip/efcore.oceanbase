using System;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OceanBase.EntityFrameworkCore.Storage;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests
{
    public sealed class DecimalMappingTests
    {
        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, " decimal(8) ", 8, 0)]
        [InlineData(OceanBaseCompatibilityMode.MySql, "NUMERIC(8,3) UNSIGNED", 8, 3)]
        [InlineData(OceanBaseCompatibilityMode.MySql, "DECIMAL", 10, 0)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, " number(8) ", 8, 0)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NUMBER(8,-2)", 8, -2)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "DECIMAL(8,11)", 8, 11)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NUMERIC(8,3)", 8, 3)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, " decimal ", 38, 0)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NUMERIC", 38, 0)]
        public void Full_declarations_set_physical_facets_without_restricting_query_inputs(
            OceanBaseCompatibilityMode mode, string storeType, int precision, int scale)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(decimal), storeType)!;
            Assert.Equal(storeType.Trim(), mapping.StoreType);
            Assert.Equal(precision, mapping.Precision);
            Assert.Equal(scale, mapping.Scale);
            Assert.Null(mapping.Size);
            using var command = new OceanBaseCommand();
            var value = decimal.MaxValue / 10;
            foreach (var direction in new[] { ParameterDirection.Input, ParameterDirection.InputOutput })
            {
                var input = (OceanBaseParameter)mapping.CreateParameter(command, "value", value, direction: direction);
                Assert.Equal(value, input.Value);
                Assert.Equal((byte)0, input.Precision);
                Assert.Null(input.DecimalScale);
            }
            Assert.Equal("7922816251426433759354395033.5", mapping.GenerateSqlLiteral(value));
            var output = (OceanBaseParameter)mapping.CreateParameter(command, "output", null, direction: ParameterDirection.Output);
            Assert.Equal(precision, output.Precision);
            Assert.Equal(scale, output.DecimalScale);
            Assert.Null(output.Value);
            Assert.Equal(DBNull.Value, mapping.CreateParameter(command, "null", null).Value);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, "DECIMAL(5,2)", "999.9900", "1000", "0.001")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "NUMERIC(5,2) UNSIGNED", "999.99", "-1", "0.001")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NUMBER(5,2)", "-999.9900", "1000", "0.001")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NUMBER(2,-2)", "9900", "10000", "9901")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NUMBER(2,5)", "0.00099", "0.001", "0.000001")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NUMBER(38,-84)", "0", "1", "0.1")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NUMBER(1,127)", "0", "1", "0.0000000000000000000000000001")]
        public void Only_exact_current_writes_are_accepted_after_conversion(
            OceanBaseCompatibilityMode mode, string storeType, string valid, string overflow, string rounding)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(decimal), storeType)!;
            var converted = (RelationalTypeMapping)mapping.WithComposedConverter(
                new ValueConverter<Amount, decimal>(amount => amount.Value, value => new Amount(value)));
            var validation = Assert.IsAssignableFrom<IOceanBaseWriteValidation>(converted);
            decimal Parse(string text) => decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
            validation.ValidateWriteValue(new Amount(Parse(valid)));
            validation.ValidateWriteValue(null);
            foreach (var invalid in new[] { overflow, rounding })
            {
                var amount = new Amount(Parse(invalid));
                Assert.ThrowsAny<ArgumentException>(() => validation.ValidateWriteValue(amount));
                using var command = new OceanBaseCommand();
                Assert.Equal(amount.Value, converted.CreateParameter(command, "predicate", amount).Value);
                Assert.NotEmpty(converted.GenerateSqlLiteral(amount));
            }
        }

        [Theory]
        [InlineData(" decimal ")]
        [InlineData("NUMERIC")]
        public void Oracle_bare_fixed_point_aliases_preserve_defaults_after_conversion(string storeType)
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.Oracle);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(decimal), storeType)!;
            var converted = (RelationalTypeMapping)mapping.WithComposedConverter(
                new ValueConverter<Amount, decimal>(amount => amount.Value, value => new Amount(value)));
            Assert.Equal(storeType.Trim(), converted.StoreType);
            Assert.Equal(38, converted.Precision);
            Assert.Equal(0, converted.Scale);
            var validation = Assert.IsAssignableFrom<IOceanBaseWriteValidation>(converted);
            foreach (var value in new[] { decimal.MinValue, decimal.MaxValue, 1.0000m, 0m })
            {
                validation.ValidateWriteValue(new Amount(value));
            }

            validation.ValidateWriteValue(null);
            Assert.Throws<ArgumentException>(() => validation.ValidateWriteValue(new Amount(0.1m)));
            using var command = new OceanBaseCommand();
            foreach (var direction in new[] { ParameterDirection.Input, ParameterDirection.InputOutput })
            {
                var parameter = (OceanBaseParameter)converted.CreateParameter(command, "query", new Amount(0.1m), direction: direction);
                Assert.Equal(0.1m, parameter.Value);
                Assert.Equal((byte)0, parameter.Precision);
                Assert.Null(parameter.DecimalScale);
            }

            var output = (OceanBaseParameter)converted.CreateParameter(command, "output", null, direction: ParameterDirection.Output);
            Assert.Equal((byte)38, output.Precision);
            Assert.Equal(0, output.DecimalScale);
        }

        [Fact]
        public void Oracle_bare_NUMBER_keeps_unspecified_facets_and_accepts_fractional_writes()
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.Oracle);
            var mapping = (RelationalTypeMapping)context.GetService<IRelationalTypeMappingSource>()
                .FindMapping(typeof(decimal), " number ")!.WithComposedConverter(
                    new ValueConverter<Amount, decimal>(amount => amount.Value, value => new Amount(value)));
            Assert.Equal("number", mapping.StoreType);
            Assert.Null(mapping.Precision);
            Assert.Null(mapping.Scale);
            Assert.IsAssignableFrom<IOceanBaseWriteValidation>(mapping).ValidateWriteValue(new Amount(0.1m));
            using var command = new OceanBaseCommand();
            var output = (OceanBaseParameter)mapping.CreateParameter(command, "output", null, direction: ParameterDirection.Output);
            Assert.Equal((byte)0, output.Precision);
            Assert.Null(output.DecimalScale);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, "DECIMAL(2,1) junk")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "DECIMAL(2,1,0)")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "DECIMAL(2,-1)")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "NUMERIC(2,3)")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "DECIMAL(66)")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "DECIMAL(2,1) ZEROFILL")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NUMBER(39)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NUMBER(2,-85)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NUMBER(2,128)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NUMBER(2,1) UNSIGNED")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NUMBER(2,,1)")]
        public void Malformed_or_unsupported_declarations_reject(OceanBaseCompatibilityMode mode, string storeType)
        {
            using var context = CreateContext(mode);
            Assert.ThrowsAny<ArgumentException>(() => context.GetService<IRelationalTypeMappingSource>()
                .FindMapping(typeof(decimal), storeType));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, "DECIMAL(8,3)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NUMBER(8,3)")]
        public void Property_and_direct_mapping_reject_conflicting_facets(OceanBaseCompatibilityMode mode, string storeType)
        {
            using var context = CreateContext(mode);
            var source = context.GetService<IRelationalTypeMappingSource>();
            Assert.ThrowsAny<ArgumentException>(() => source.FindMapping(typeof(decimal), storeType, precision: 9, scale: 3));
            Assert.ThrowsAny<ArgumentException>(() => source.FindMapping(typeof(decimal), storeType, precision: 8, scale: 2));
            Assert.ThrowsAny<ArgumentException>(() => source.FindMapping(typeof(decimal), storeType, size: 8));
            Assert.ThrowsAny<ArgumentException>(() => source.FindMapping(typeof(decimal), storeType, fixedLength: true));
            var builder = new ModelBuilder();
            var property = builder.Entity("Price").Property<decimal>("Value").HasColumnType(storeType).HasPrecision(9, 3).Metadata;
            Assert.ThrowsAny<ArgumentException>(() => source.FindMapping((IProperty)property));
            property.SetPrecision(8);
            property.SetScale(2);
            Assert.ThrowsAny<ArgumentException>(() => source.FindMapping((IProperty)property));
            property.SetScale(3);
            Assert.Equal(8, source.FindMapping((IProperty)property)!.Precision);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, "NUMERIC(8,3) UNSIGNED", "NUMERIC(10,4) UNSIGNED")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "NUMERIC UNSIGNED", "NUMERIC(10,4) UNSIGNED")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NUMBER(8,3)", "NUMBER(10,4)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "DECIMAL", "DECIMAL(10,4)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NUMERIC", "NUMERIC(10,4)")]
        public void Clone_updates_physical_declaration_and_keeps_converter(OceanBaseCompatibilityMode mode, string storeType, string expected)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(decimal), storeType)!;
            var converted = (RelationalTypeMapping)mapping.WithComposedConverter(
                new ValueConverter<Amount, decimal>(amount => amount.Value, value => new Amount(value)));
            var clone = converted.WithPrecisionAndScale(10, 4);
            Assert.Equal(expected, clone.StoreType);
            Assert.Equal(10, clone.Precision);
            Assert.Equal(4, clone.Scale);
            Assert.IsAssignableFrom<IOceanBaseWriteValidation>(clone).ValidateWriteValue(new Amount(0.0001m));
            Assert.ThrowsAny<ArgumentException>(() => Assert.IsAssignableFrom<IOceanBaseWriteValidation>(converted)
                .ValidateWriteValue(new Amount(0.0001m)));
            using var command = new OceanBaseCommand();
            Assert.Equal(0.0001m, clone.CreateParameter(command, "value", new Amount(0.0001m)).Value);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, "NUMERIC", "NUMERIC(12,4)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NUMBER", "NUMBER(12,4)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "DECIMAL", "DECIMAL(12,4)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NUMERIC", "NUMERIC(12,4)")]
        public void Bare_declarations_apply_configured_facets_and_clone_rejects_forbidden_size(
            OceanBaseCompatibilityMode mode, string storeType, string expected)
        {
            using var context = CreateContext(mode);
            var source = context.GetService<IRelationalTypeMappingSource>();
            var mapping = source.FindMapping(typeof(decimal), storeType, precision: 12, scale: 4)!;
            Assert.Equal(expected, mapping.StoreType);
            Assert.ThrowsAny<ArgumentException>(() => mapping.Clone(
                new RelationalTypeMappingInfo(typeof(decimal), size: 4, precision: 13, scale: 5)));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Defaults_support_all_CLR_decimal_values(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(decimal))!;
            var validation = Assert.IsAssignableFrom<IOceanBaseWriteValidation>(mapping);
            foreach (var value in new[] { decimal.MinValue, decimal.MaxValue, 0m, 0.0000000000000000000000000001m })
            {
                validation.ValidateWriteValue(value);
            }
        }

        [Theory]
        [InlineData("NUMERIC UNSIGNED")]
        [InlineData("DECIMAL\tUNSIGNED")]
        public void Direct_bare_unsigned_clone_preserves_one_unsigned_modifier(string storeType)
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.MySql);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(decimal), storeType)!;
            var clone = mapping.WithPrecisionAndScale(10, 4);
            Assert.Equal(storeType.StartsWith("NUMERIC", StringComparison.Ordinal) ? "NUMERIC(10,4) UNSIGNED"
                : "DECIMAL(10,4) UNSIGNED", clone.StoreType);
            Assert.Throws<ArgumentOutOfRangeException>(() => Assert.IsAssignableFrom<IOceanBaseWriteValidation>(clone)
                .ValidateWriteValue(-1m));
        }

        private static TestContext CreateContext(OceanBaseCompatibilityMode mode)
            => new(new DbContextOptionsBuilder<TestContext>().UseOceanBase("Server=localhost;Database=test", mode).Options);

        private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options);
        private sealed record Amount(decimal Value);
    }
}
