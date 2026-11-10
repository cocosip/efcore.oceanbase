using System;
using System.Data;
using System.Globalization;
using System.Linq.Expressions;
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
    public sealed class FloatingPointMappingTests
    {
        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, typeof(double), " float(24) unsigned ", 24)]
        [InlineData(OceanBaseCompatibilityMode.MySql, typeof(double), "FLOAT(0)", 0)]
        [InlineData(OceanBaseCompatibilityMode.MySql, typeof(float), "FLOAT(53)", 53)]
        [InlineData(OceanBaseCompatibilityMode.MySql, typeof(float), "DOUBLE PRECISION", null)]
        [InlineData(OceanBaseCompatibilityMode.MySql, typeof(double), "DOUBLE UNSIGNED", null)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(double), " binary_float ", null)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(float), "BINARY_DOUBLE", null)]
        public void Explicit_native_types_preserve_declaration_and_CLR_parameter_binding(
            OceanBaseCompatibilityMode mode, Type type, string storeType, int? precision)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(type, storeType)!;
            using var command = new OceanBaseCommand();
            object value = type == typeof(float) ? (object)1.25f : 1.25d;
            var parameter = mapping.CreateParameter(command, "value", value);
            Assert.Equal(storeType.Trim(), mapping.StoreType);
            Assert.Equal(precision, mapping.Precision);
            Assert.Equal(type == typeof(float) ? DbType.Single : DbType.Double, parameter.DbType);
            Assert.Equal(value, parameter.Value);
            Assert.Equal(0, parameter.Size);
            Assert.Equal((byte)0, parameter.Precision);
            Assert.Equal((byte)0, parameter.Scale);
            Assert.Equal(DBNull.Value, mapping.CreateParameter(command, "value", null).Value);
            var output = mapping.CreateParameter(command, "value", null, direction: ParameterDirection.Output);
            Assert.Null(output.Value);
            Assert.Equal(ParameterDirection.Output, output.Direction);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, typeof(float), typeof(double), "FLOAT(53)")]
        [InlineData(OceanBaseCompatibilityMode.MySql, typeof(double), typeof(float), "FLOAT")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(float), typeof(double), "BINARY_DOUBLE")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(double), typeof(float), "BINARY_FLOAT")]
        public void Alternate_reader_widths_are_converted_instead_of_directly_cast(
            OceanBaseCompatibilityMode mode, Type type, Type readerType, string storeType)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(type, storeType)!;
            object value = readerType == typeof(float) ? (object)1.25f : 1.25d;
            object expected = type == typeof(float) ? (object)1.25f : 1.25d;
            Assert.Equal(expected, Read(mapping, value));
            if (type == typeof(float))
            {
                Assert.Throws<OverflowException>(() => Read(mapping, double.MaxValue));
                Assert.Equal(float.PositiveInfinity, Read(mapping, double.PositiveInfinity));
                var composed = (RelationalTypeMapping)mapping.WithComposedConverter(
                    new ValueConverter<FloatValue, float>(value => value.Value, value => new FloatValue(value)));
                Assert.Equal(new FloatValue(1.25f), Read(composed, 1.25d));
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, "FLOAT UNSIGNED")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "BINARY_FLOAT")]
        public void Narrow_store_capacity_is_checked_only_for_written_values(
            OceanBaseCompatibilityMode mode, string storeType)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(double), storeType)!;
            using var command = new OceanBaseCommand();
            Assert.Equal(double.MaxValue, mapping.CreateParameter(command, "limit", double.MaxValue).Value);
            Assert.NotEmpty(mapping.GenerateSqlLiteral(double.MaxValue));
            Assert.Equal(-1d, mapping.CreateParameter(command, "limit", -1d).Value);
            var validation = Assert.IsAssignableFrom<IOceanBaseWriteValidation>(mapping);
            validation.ValidateWriteValue((double)float.MaxValue);
            Assert.Throws<ArgumentOutOfRangeException>(() => validation.ValidateWriteValue(double.MaxValue));
            if (mode == OceanBaseCompatibilityMode.MySql)
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => validation.ValidateWriteValue(-1d));
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Non_finite_inputs_reject_for_parameters_and_literals(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            foreach (var type in new[] { typeof(float), typeof(double) })
            {
                var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(type)!;
                using var command = new OceanBaseCommand();
                foreach (var number in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
                {
                    object value = type == typeof(float) ? (object)(float)number : number;
                    Assert.Throws<ArgumentOutOfRangeException>(() => mapping.CreateParameter(command, "value", value));
                    Assert.Throws<ArgumentOutOfRangeException>(() => mapping.GenerateSqlLiteral(value));
                }
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, typeof(float), "0.10000000149011612E0")]
        [InlineData(OceanBaseCompatibilityMode.MySql, typeof(double), "0.1E0")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(float), "0.1F")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(double), "0.1D")]
        public void Floating_literals_preserve_type_and_round_trip_value(
            OceanBaseCompatibilityMode mode, Type type, string expected)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(type)!;
            var priorCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                object value = type == typeof(float) ? (object)0.1f : 0.1d;
                Assert.Equal(expected, mapping.GenerateSqlLiteral(value));
            }
            finally { CultureInfo.CurrentCulture = priorCulture; }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, "FLOAT(54)")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "FLOAT(7,2)")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "FLOAT ZEROFILL")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "REAL")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "BINARY_DOUBLE")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "DOUBLE(24)")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "DECIMAL(10,2)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "FLOAT(24)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "BINARY_FLOAT(24)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "BINARY_DOUBLE UNSIGNED")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "DOUBLE")]
        public void Unsupported_floating_declarations_reject(OceanBaseCompatibilityMode mode, string storeType)
        {
            using var context = CreateContext(mode);
            Assert.ThrowsAny<ArgumentException>(() => context.GetService<IRelationalTypeMappingSource>()
                .FindMapping(typeof(double), storeType));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, "FLOAT(24)", null, 53, null, null)]
        [InlineData(OceanBaseCompatibilityMode.MySql, "FLOAT(24)", 24, null, null, null)]
        [InlineData(OceanBaseCompatibilityMode.MySql, null, null, null, 0, null)]
        [InlineData(OceanBaseCompatibilityMode.MySql, null, null, null, null, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, null, null, 24, null, null)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, null, 8, null, null, null)]
        public void Unsupported_floating_facets_reject(OceanBaseCompatibilityMode mode, string? storeType,
            int? size, int? precision, int? scale, bool? fixedLength)
        {
            using var context = CreateContext(mode);
            Assert.ThrowsAny<ArgumentException>(() => context.GetService<IRelationalTypeMappingSource>()
                .FindMapping(typeof(double), storeType, size: size, precision: precision, scale: scale, fixedLength: fixedLength));
        }

        [Fact]
        public void Floating_precision_and_clone_change_storage_width_together()
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.MySql);
            var mapping = context.GetService<IRelationalTypeMappingSource>()
                .FindMapping(typeof(double), null, precision: 24)!;
            Assert.Equal("FLOAT(24)", mapping.StoreType);
            var clone = mapping.WithPrecisionAndScale(53, null);
            Assert.Equal("FLOAT(53)", clone.StoreType);
            using var command = new OceanBaseCommand();
            Assert.Equal(double.MaxValue, clone.CreateParameter(command, "value", double.MaxValue).Value);
            Assert.IsAssignableFrom<IOceanBaseWriteValidation>(clone).ValidateWriteValue(double.MaxValue);
            Assert.Throws<ArgumentOutOfRangeException>(() => Assert.IsAssignableFrom<IOceanBaseWriteValidation>(mapping)
                .ValidateWriteValue(double.MaxValue));
        }

        [Fact]
        public void Floating_precision_clone_does_not_erase_an_incompatible_size()
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.MySql);
            var mapping = context.GetService<IRelationalTypeMappingSource>()
                .FindMapping(typeof(double), "FLOAT(24)")!;
            Assert.ThrowsAny<ArgumentException>(() => mapping.WithTypeMappingInfo(
                new RelationalTypeMappingInfo(typeof(double), size: 1, precision: 53)));
        }

        [Fact]
        public void Configured_precision_is_applied_to_a_bare_FLOAT_declaration()
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.MySql);
            var mapping = context.GetService<IRelationalTypeMappingSource>()
                .FindMapping(typeof(double), "FLOAT UNSIGNED", precision: 53)!;
            Assert.Equal("FLOAT(53) UNSIGNED", mapping.StoreType);
            Assert.Equal(53, mapping.Precision);
            Assert.IsAssignableFrom<IOceanBaseWriteValidation>(mapping).ValidateWriteValue(double.MaxValue);
        }

        [Fact]
        public void Property_precision_conflict_is_not_hidden_by_the_parsed_FLOAT_precision()
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.MySql);
            var modelBuilder = new ModelBuilder();
            var property = modelBuilder.Entity("FloatRow").Property<double>("Value")
                .HasColumnType("FLOAT(24)").HasPrecision(53).Metadata;
            var source = context.GetService<IRelationalTypeMappingSource>();
            Assert.ThrowsAny<ArgumentException>(() => source.FindMapping((IProperty)property));
            property.SetPrecision(24);
            Assert.Equal(24, source.FindMapping((IProperty)property)!.Precision);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, typeof(float))]
        [InlineData(OceanBaseCompatibilityMode.MySql, typeof(double))]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(float))]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(double))]
        public void Finite_boundaries_and_subnormal_values_preserve_client_transport(
            OceanBaseCompatibilityMode mode, Type type)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(type)!;
            using var command = new OceanBaseCommand();
            foreach (var number in type == typeof(float)
                ? new double[] { float.MinValue, float.MaxValue, float.Epsilon, -float.Epsilon, 0d }
                : new[] { double.MinValue, double.MaxValue, double.Epsilon, -double.Epsilon, 0d })
            {
                object value = type == typeof(float) ? (object)(float)number : number;
                Assert.Equal(value, mapping.CreateParameter(command, "value", value).Value);
                Assert.Equal(value, Read(mapping, value));
                Assert.IsAssignableFrom<IOceanBaseWriteValidation>(mapping).ValidateWriteValue(value);
            }
        }

        private static object Read(RelationalTypeMapping mapping, object value)
        {
            using var table = new DataTable();
            table.Columns.Add("Value", value.GetType());
            table.Rows.Add(value);
            using var reader = table.CreateDataReader();
            Assert.True(reader.Read());
            var read = Expression.Call(Expression.Constant(reader), mapping.GetDataReaderMethod(), Expression.Constant(0));
            var expression = mapping.CustomizeDataReaderExpression(read);
            var result = Expression.Lambda<Func<object>>(Expression.Convert(expression, typeof(object))).Compile()();
            return mapping.Converter is { } converter ? converter.ConvertFromProvider(result)! : result;
        }

        private static TestContext CreateContext(OceanBaseCompatibilityMode mode)
            => new(new DbContextOptionsBuilder<TestContext>().UseOceanBase("Server=localhost;Database=test", mode).Options);

        private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options);
        private sealed record FloatValue(float Value);
    }
}
