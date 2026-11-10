using System;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBase.EntityFrameworkCore.Storage;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests
{
    public sealed class TemporalQueryMappingTests
    {
        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, typeof(DateTime), "DATETIME(3)")]
        [InlineData(OceanBaseCompatibilityMode.MySql, typeof(DateTimeOffset), "DATETIME(3)")]
        [InlineData(OceanBaseCompatibilityMode.MySql, typeof(TimeOnly), "TIME(3)")]
        [InlineData(OceanBaseCompatibilityMode.MySql, typeof(TimeSpan), "TIME(3)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(DateTime), "TIMESTAMP(3)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(DateTimeOffset), "TIMESTAMP(3) WITH TIME ZONE")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(TimeOnly), "INTERVAL DAY(1) TO SECOND(3)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(TimeSpan), "INTERVAL DAY(1) TO SECOND(3)")]
        public void Query_transport_keeps_fractional_values_that_cannot_be_written_to_the_column(
            OceanBaseCompatibilityMode mode, Type type, string storeType)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(type, storeType)!;
            var duration = TimeSpan.FromHours(1).Add(TimeSpan.FromTicks(1234560));
            var stamp = new DateTime(2026, 11, 10, 1, 0, 0).AddTicks(1234560);
            object value = type == typeof(DateTime) ? stamp : type == typeof(DateTimeOffset)
                ? new DateTimeOffset(stamp, TimeSpan.Zero) : type == typeof(TimeOnly)
                    ? TimeOnly.FromTimeSpan(duration) : duration;
            using var command = new OceanBaseCommand();
            var parameter = mapping.CreateParameter(command, "value", value);
            var precision = mode == OceanBaseCompatibilityMode.MySql ? 6 : 7;
            Assert.Equal(precision, parameter.Scale);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? ".123456" : ".1234560", mapping.GenerateSqlLiteral(value));
            Assert.Equal(storeType, mapping.StoreType);
            Assert.Equal(3, mapping.Precision);
            var inputOutput = mapping.CreateParameter(command, "both", value, direction: ParameterDirection.InputOutput);
            Assert.Equal(ParameterDirection.InputOutput, inputOutput.Direction);
            Assert.Equal(parameter.Scale, inputOutput.Scale);
            Assert.Equal(parameter.Precision, inputOutput.Precision);
            Assert.Equal(parameter.Value, inputOutput.Value);
            Assert.ThrowsAny<ArgumentException>(() => Assert.IsAssignableFrom<IOceanBaseWriteValidation>(mapping).ValidateWriteValue(value));
            var output = mapping.CreateParameter(command, "output", null, direction: ParameterDirection.Output);
            Assert.Equal((byte)3, output.Scale);
            var reduced = mapping.WithPrecisionAndScale(1, null);
            var reducedInput = reduced.CreateParameter(command, "reduced", value, direction: ParameterDirection.InputOutput);
            Assert.Equal(parameter.Value, reducedInput.Value);
            Assert.Equal(parameter.Scale, reducedInput.Scale);
            Assert.Equal(parameter.Precision, reducedInput.Precision);
            var reducedOutput = reduced.CreateParameter(command, "output", null, direction: ParameterDirection.Output);
            Assert.Equal((byte)1, reducedOutput.Scale);
            Assert.Equal(output.Precision, reducedOutput.Precision);
        }

        [Fact]
        public void Oracle_DATE_comparands_use_lossless_timestamp_transport()
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.Oracle);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(DateTime), "DATE")!;
            var value = new DateTime(2026, 11, 10, 12, 0, 0).AddTicks(1);
            using var command = new OceanBaseCommand();
            var parameter = mapping.CreateParameter(command, "date", value);
            Assert.Equal(DbType.DateTime2, parameter.DbType);
            Assert.Equal((byte)7, parameter.Scale);
            Assert.Equal(value, parameter.Value);
            Assert.Equal("TIMESTAMP '2026-11-10 12:00:00.0000001'", mapping.GenerateSqlLiteral(value));
            Assert.Throws<ArgumentException>(() => Assert.IsAssignableFrom<IOceanBaseWriteValidation>(mapping).ValidateWriteValue(value));
            var output = mapping.CreateParameter(command, "date", null, direction: ParameterDirection.Output);
            Assert.Equal(DbType.Date, output.DbType);
            Assert.Equal((byte)0, output.Scale);
        }

        [Fact]
        public void Oracle_interval_comparands_do_not_inherit_column_leading_precision()
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.Oracle);
            var mapping = context.GetService<IRelationalTypeMappingSource>()
                .FindMapping(typeof(TimeSpan), "INTERVAL DAY(1) TO SECOND(3)")!;
            var value = TimeSpan.FromDays(100).Add(TimeSpan.FromTicks(1));
            using var command = new OceanBaseCommand();
            var parameter = mapping.CreateParameter(command, "duration", value);
            Assert.Equal((byte)9, parameter.Precision);
            Assert.Equal((byte)7, parameter.Scale);
            Assert.Equal(value, Assert.IsType<OceanBaseIntervalDayToSecond>(parameter.Value).ToTimeSpan());
            Assert.Equal("INTERVAL '+000000100 00:00:00.0000001' DAY(9) TO SECOND(7)", mapping.GenerateSqlLiteral(value));
            Assert.ThrowsAny<ArgumentException>(() => Assert.IsAssignableFrom<IOceanBaseWriteValidation>(mapping).ValidateWriteValue(value));
            var output = mapping.CreateParameter(command, "duration", null, direction: ParameterDirection.Output);
            Assert.Equal((byte)1, output.Precision);
            Assert.Equal((byte)3, output.Scale);
        }

        private static TestContext CreateContext(OceanBaseCompatibilityMode mode)
            => new(new DbContextOptionsBuilder<TestContext>().UseOceanBase("Server=localhost;Database=test", mode).Options);

        private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options);
    }
}
