using System;
using System.Collections.Generic;
using System.Data;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests
{
    public sealed class ScalarMappingTests
    {
        public static IEnumerable<object[]> IntegerCases()
        {
            var types = new (Type Type, object Minimum, object Maximum, string MySql, string Oracle)[]
            {
                (typeof(sbyte), sbyte.MinValue, sbyte.MaxValue, "TINYINT", "NUMBER(3)"),
                (typeof(byte), byte.MinValue, byte.MaxValue, "TINYINT UNSIGNED", "NUMBER(3)"),
                (typeof(short), short.MinValue, short.MaxValue, "SMALLINT", "NUMBER(5)"),
                (typeof(ushort), ushort.MinValue, ushort.MaxValue, "SMALLINT UNSIGNED", "NUMBER(5)"),
                (typeof(int), int.MinValue, int.MaxValue, "INT", "NUMBER(10)"),
                (typeof(uint), uint.MinValue, uint.MaxValue, "INT UNSIGNED", "NUMBER(10)"),
                (typeof(long), long.MinValue, long.MaxValue, "BIGINT", "NUMBER(19)"),
                (typeof(ulong), ulong.MinValue, ulong.MaxValue, "BIGINT UNSIGNED", "NUMBER(20)")
            };
            foreach (var (type, minimum, maximum, mysql, oracle) in types)
            {
                yield return [OceanBaseCompatibilityMode.MySql, type, minimum, maximum, mysql];
                yield return [OceanBaseCompatibilityMode.Oracle, type, minimum, maximum, oracle];
            }
        }

        [Theory]
        [MemberData(nameof(IntegerCases))]
        public void Integer_mappings_preserve_range_parameters_and_Oracle_reader_values(
            OceanBaseCompatibilityMode mode, Type type, object minimum, object maximum, string storeType)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(type)!;
            Assert.Equal(storeType, mapping.StoreType);
            using var command = new OceanBaseCommand();
            foreach (var value in new[] { minimum, maximum })
            {
                Assert.Equal(value, mapping.CreateParameter(command, "value", value).Value);
                var providerValue = mode == OceanBaseCompatibilityMode.Oracle ? Convert.ToDecimal(value) : value;
                Assert.Equal(value, Read(mapping, providerValue));
            }
        }

        [Theory]
        [MemberData(nameof(IntegerCases))]
        public void Floating_integer_reads_require_exact_in_range_values(
            OceanBaseCompatibilityMode mode, Type type, object minimum, object maximum, string storeType)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(type, storeType)!;
            foreach (var fraction in new object[] { 1.5d, 1.5f, -0.5d, -0.5f, double.Epsilon, float.Epsilon })
            {
                Assert.Throws<InvalidCastException>(() => Read(mapping, fraction));
            }

            foreach (var exact in new object[] { 5d, 5f })
            {
                var result = Read(mapping, exact);
                Assert.IsType(type, result);
                Assert.Equal(5m, Convert.ToDecimal(result));
            }

            Assert.Equal(minimum, Read(mapping, Convert.ToSingle(minimum)));
            Assert.Equal(minimum, Read(mapping, Convert.ToDouble(minimum)));

            var outside = Convert.ToDouble(maximum) + 1d;
            foreach (var value in new object[] { outside, (float)outside, double.NaN, double.PositiveInfinity,
                double.NegativeInfinity, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                Assert.Throws<OverflowException>(() => Read(mapping, value));
            }
        }

        [Theory]
        [MemberData(nameof(IntegerCases))]
        public void Nullable_integer_mappings_keep_endpoints_exact_and_null_parameters_unset(
            OceanBaseCompatibilityMode mode, Type type, object minimum, object maximum, string storeType)
        {
            using var context = CreateContext(mode);
            var nullableType = typeof(Nullable<>).MakeGenericType(type);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(nullableType)!;
            Assert.Equal(storeType, mapping.StoreType);
            using var command = new OceanBaseCommand();
            foreach (var value in new[] { minimum, maximum })
            {
                Assert.Equal(value, mapping.CreateParameter(command, "endpoint", value, nullable: true).Value);
                Assert.Equal(value, Read(mapping, new OceanBaseDecimal(Convert.ToDecimal(value))));
            }

            Assert.Equal(DBNull.Value, mapping.CreateParameter(command, "null", null, nullable: true).Value);
            Assert.Null(mapping.CreateParameter(command, "output", null, direction: ParameterDirection.Output).Value);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Integer_reading_rejects_fraction_and_overflow(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(int))!;
            Assert.Throws<InvalidCastException>(() => Read(mapping, 1.5m));
            Assert.Throws<OverflowException>(() => Read(mapping, (decimal)int.MaxValue + 1));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Integer_reader_and_parameters_survive_converter_composition(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var mapping = (RelationalTypeMapping)context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(int))!
                .WithComposedConverter(new EnumToNumberConverter<DayOfWeek, int>());
            using var command = new OceanBaseCommand();

            Assert.Equal(DayOfWeek.Friday, Read(mapping, new OceanBaseDecimal(5m)));
            Assert.Equal(DayOfWeek.Friday, Read(mapping, 5d));
            Assert.Equal(DayOfWeek.Friday, Read(mapping, 5f));
            Assert.Throws<InvalidCastException>(() => Read(mapping, 1.5d));
            Assert.Throws<InvalidCastException>(() => Read(mapping, 1.5f));
            Assert.Equal(5, mapping.CreateParameter(command, "day", DayOfWeek.Friday).Value);
            Assert.Equal(DBNull.Value, mapping.CreateParameter(command, "day", null, nullable: true).Value);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Integer_reading_checks_exact_connector_decimal_values(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(ulong))!;

            Assert.Equal(ulong.MaxValue, Read(mapping, new OceanBaseDecimal(18446744073709551615m)));
            Assert.Throws<OverflowException>(() => Read(mapping, new OceanBaseDecimal(18446744073709551616m)));
            Assert.Throws<InvalidCastException>(() => Read(mapping, new OceanBaseDecimal(1.5m)));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Guid_mapping_pairs_parameter_and_reader_conversion(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(Guid))!;
            var guid = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
            using var command = new OceanBaseCommand();
            var parameter = mapping.CreateParameter(command, "value", guid);
            Assert.NotNull(mapping.Converter);
            Assert.Equal(guid, mapping.Converter!.ConvertFromProvider(parameter.Value));
            if (mode == OceanBaseCompatibilityMode.Oracle)
            {
                Assert.Equal(DbType.Binary, parameter.DbType);
                Assert.Equal(16, parameter.Size);
                Assert.Equal(guid.ToByteArray(), Assert.IsType<byte[]>(parameter.Value));
                Assert.Equal("HEXTORAW('33221100554477668899AABBCCDDEEFF')", mapping.GenerateSqlLiteral(guid));
            }
            else
            {
                Assert.Equal(DbType.AnsiStringFixedLength, parameter.DbType);
                Assert.Equal(36, parameter.Size);
                Assert.Equal(guid.ToString(), parameter.Value);
            }

            var empty = mapping.CreateParameter(command, "empty", Guid.Empty);
            Assert.Equal(Guid.Empty, Read(mapping, empty.Value!));
            Assert.Equal(DBNull.Value, mapping.CreateParameter(command, "null", null, nullable: true).Value);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Boolean_parameters_and_reader_forms_are_numeric_safe(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(bool))!;
            using var command = new OceanBaseCommand();
            var parameter = mapping.CreateParameter(command, "value", true);
            Assert.Equal(mode == OceanBaseCompatibilityMode.Oracle ? (object)0m : false,
                mapping.CreateParameter(command, "false", false).Value);
            var nullableMapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(bool?))!;
            Assert.Equal(DBNull.Value, nullableMapping.CreateParameter(command, "null", null, nullable: true).Value);
            Assert.False(Assert.IsType<bool>(Read(mapping, false)));
            Assert.False(Assert.IsType<bool>(Read(mapping, 0m)));
            Assert.False(Assert.IsType<bool>(Read(mapping, new OceanBaseDecimal(0m))));
            if (mode == OceanBaseCompatibilityMode.Oracle)
            {
                Assert.Equal(DbType.Decimal, parameter.DbType);
                Assert.Equal(1m, parameter.Value);
            }
            else
            {
                Assert.Equal(true, Read(mapping, 1L));
                Assert.Equal(false, Read(mapping, (sbyte)0));
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, "CONVERT(X'615C2762' USING utf8mb4)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "N'a\\''b'")]
        public void String_literals_preserve_backslash_and_quote(OceanBaseCompatibilityMode mode, string expected)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(string))!;
            Assert.Equal(expected, mapping.GenerateSqlLiteral("a\\'b"));
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
    }
}
