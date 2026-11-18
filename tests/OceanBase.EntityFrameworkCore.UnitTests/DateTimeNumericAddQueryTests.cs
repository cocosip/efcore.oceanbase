using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OceanBase.EntityFrameworkCore.Query;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests
{
    public sealed class DateTimeNumericAddQueryTests
    {
        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Numeric_addition_translates_predicates_columns_parameters_and_composition(
            OceanBaseCompatibilityMode mode, bool hours)
        {
            using var context = Create(mode);
            var amount = -1.25;
            var query = hours
                ? context.Rows.Where(row => row.Stamp.AddHours(amount) > new DateTime(2000, 1, 1))
                    .OrderBy(row => row.OptionalStamp!.Value.AddHours(row.Amount))
                    .Select(row => (DateTime?)row.OptionalStamp!.Value.AddHours(0.5).AddHours(row.Amount))
                : context.Rows.Where(row => row.Stamp.AddDays(amount) > new DateTime(2000, 1, 1))
                    .OrderBy(row => row.OptionalStamp!.Value.AddDays(row.Amount))
                    .Select(row => (DateTime?)row.OptionalStamp!.Value.AddDays(0.5).AddDays(row.Amount));
            var sql = query.ToQueryString();
            Assert.DoesNotContain("__oceanbase_internal", sql);
            Assert.Contains("OptionalStamp", sql);
            Assert.Contains("Amount", sql);
            Assert.Contains(hours ? "36000000000" : "864000000000", sql);
            Assert.Contains("CASE WHEN ABS(", sql);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "TIMESTAMPADD(MICROSECOND" : "NUMTODSINTERVAL", sql);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? " AS DATETIME(6))" : " AS TIMESTAMP(7))", sql);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "MOD(" : " / 10000000", sql);
            Assert.DoesNotContain("TIME_TO_SEC", sql);
            amount = 900.5;
            var refreshed = query.ToQueryString();
            Assert.Contains("900.5", refreshed);
            Assert.Equal(sql[sql.IndexOf("SELECT", StringComparison.Ordinal)..],
                refreshed[refreshed.IndexOf("SELECT", StringComparison.Ordinal)..]);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Numeric_addition_promotes_low_precision_results_and_preserves_converter_ownership(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var source = context.GetService<IRelationalTypeMappingSource>();
            var methods = context.GetService<IMethodCallTranslatorProvider>();
            var factory = context.GetService<ISqlExpressionFactory>();
            var logger = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>();
            var converter = new ValueConverter<DateTime, DateTime>(value => value.AddDays(1), value => value.AddDays(-1));
            var narrow = source.FindMapping(typeof(DateTime), mode == OceanBaseCompatibilityMode.MySql ? "DATETIME(0)" : "DATE")!;
            var mapping = (RelationalTypeMapping)narrow.WithComposedConverter(converter);
            foreach (var name in new[] { nameof(DateTime.AddDays), nameof(DateTime.AddHours) })
            {
                var result = Assert.IsType<SqlFunctionExpression>(methods.Translate(context.Model,
                    new ColumnExpression("Stamp", "r", typeof(DateTime), mapping, true),
                    typeof(DateTime).GetMethod(name, [typeof(double)])!,
                    [factory.Constant(0.5, source.FindMapping(typeof(double)))], logger));
                Assert.Equal(mode == OceanBaseCompatibilityMode.MySql ? "DATETIME(6)" : "TIMESTAMP(7)", result.TypeMapping!.StoreType);
                Assert.Same(converter, result.TypeMapping.Converter);
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Numeric_addition_rejects_incompatible_carriers_and_null_converters(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var source = context.GetService<IRelationalTypeMappingSource>();
            var methods = context.GetService<IMethodCallTranslatorProvider>();
            var factory = context.GetService<ISqlExpressionFactory>();
            var logger = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>();
            var date = source.FindMapping(typeof(DateTime))!;
            var number = source.FindMapping(typeof(double))!;
            var textNumber = (RelationalTypeMapping)source.FindMapping(typeof(string))!.WithComposedConverter(
                new ValueConverter<double, string>(value => value.ToString(), value => double.Parse(value)));
            var textDate = (RelationalTypeMapping)source.FindMapping(typeof(string))!.WithComposedConverter(
                new ValueConverter<DateTime, string>(value => value.ToString(), value => DateTime.Parse(value)));
            var nullNumber = (RelationalTypeMapping)number.WithComposedConverter(
                new ValueConverter<double, double>(value => value, value => value, convertsNulls: true));
            foreach (var name in new[] { nameof(DateTime.AddDays), nameof(DateTime.AddHours) })
            {
                var method = typeof(DateTime).GetMethod(name, [typeof(double)])!;
                var input = new ColumnExpression("Stamp", "r", typeof(DateTime), date, true);
                Assert.NotNull(methods.Translate(context.Model, input, method, [factory.Constant(0.5, number)], logger));
                foreach (var invalid in new[] { textNumber, nullNumber })
                    Assert.Null(methods.Translate(context.Model, input, method, [factory.Constant(0.5, invalid)], logger));
                Assert.Null(methods.Translate(context.Model, new ColumnExpression("Stamp", "r", typeof(DateTime), textDate, true),
                    method, [factory.Constant(0.5, number)], logger));
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Numeric_renderer_rejects_malformed_markers(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var factory = context.GetService<ISqlExpressionFactory>();
            var source = context.GetService<IRelationalTypeMappingSource>();
            var date = source.FindMapping(typeof(DateTime))!;
            var stamp = new ColumnExpression("Stamp", "r", typeof(DateTime), date, true);
            var amount = factory.Constant(0.5, source.FindMapping(typeof(double)));
            foreach (var marker in new[] { OceanBaseTemporalMethodTranslator.DateTimeAddDaysMarker,
                OceanBaseTemporalMethodTranslator.DateTimeAddHoursMarker })
            {
                SqlExpression[] invalid =
                [
                    factory.Function(marker, [stamp], true, [false], typeof(DateTime), date),
                    factory.Function(marker, [stamp, amount], false, [false, false], typeof(DateTime), date),
                    factory.Function(marker, [stamp, amount], true, [true, true], typeof(DateTime), date),
                    factory.Function(marker, [stamp, factory.Constant(1)], true, [false, false], typeof(DateTime), date),
                    factory.Function(marker, [stamp, amount], true, [false, false], typeof(int), source.FindMapping(typeof(int)))
                ];
                foreach (var expression in invalid)
                {
                    var generator = new OceanBaseQuerySqlGenerator(context.GetService<QuerySqlGeneratorDependencies>(), mode);
                    Assert.Contains("Invalid internal DateTime numeric-add", Assert.Throws<InvalidOperationException>(
                        () => generator.GetCommand(expression)).Message);
                }
            }
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public void Oracle_generated_parameter_collisions_keep_both_values(bool reverse, bool cloneMapping)
        {
            using var context = Create(OceanBaseCompatibilityMode.Oracle);
            var factory = context.GetService<ISqlExpressionFactory>();
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(int))!;
            var digit = new SqlParameterExpression("8_anchor", typeof(int), mapping);
            var ordinary = new SqlParameterExpression("_ob_8_anchor", typeof(int), mapping);
            var repeatedInput = reverse ? digit : ordinary;
            var repeatedMapping = cloneMapping ? mapping.WithStoreTypeAndSize(mapping.StoreType, null) : mapping;
            if (cloneMapping) Assert.NotSame(mapping, repeatedMapping);
            var secondOccurrence = new SqlParameterExpression(repeatedInput.InvariantName, repeatedInput.Name,
                repeatedInput.Type, cloneMapping ? !repeatedInput.IsNullable : repeatedInput.IsNullable,
                repeatedInput.TranslationMode, repeatedMapping);
            var values = new Dictionary<string, object?> { ["8_anchor"] = 11, ["_ob_8_anchor"] = 22 };
            var processor = context.GetService<IRelationalParameterBasedSqlProcessorFactory>()
                .Create(new RelationalParameterBasedSqlProcessorParameters(false, ParameterTranslationMode.MultipleParameters));
            var processed = processor.Process(reverse ? factory.Add(ordinary, factory.Add(digit, secondOccurrence))
                : factory.Add(digit, factory.Add(ordinary, secondOccurrence)), new ParametersCacheDecorator(values));
            var addition = Assert.IsType<SqlBinaryExpression>(processed);
            var repeated = Assert.IsType<SqlBinaryExpression>(addition.Right);
            Assert.Equal(Assert.IsType<SqlParameterExpression>(repeated.Left).Name,
                Assert.IsType<SqlParameterExpression>(repeated.Right).Name);
            Assert.Same(repeatedMapping, Assert.IsType<SqlParameterExpression>(repeated.Right).TypeMapping);
            Assert.NotEqual(Assert.IsType<SqlParameterExpression>(addition.Left).Name,
                Assert.IsType<SqlParameterExpression>(repeated.Left).Name);
            var generator = new OceanBaseQuerySqlGenerator(context.GetService<QuerySqlGeneratorDependencies>(), OceanBaseCompatibilityMode.Oracle);
            var sql = generator.GetCommand(processed);
            using var command = new OceanBaseCommand();
            foreach (var parameter in sql.Parameters) parameter.AddDbParameter(command, values);
            Assert.Equal(2, command.Parameters.Count);
            Assert.NotEqual(command.Parameters[0].ParameterName, command.Parameters[1].ParameterName);
            Assert.Equal(reverse ? 22 : 11, command.Parameters[0].Value);
            Assert.Equal(reverse ? 11 : 22, command.Parameters[1].Value);
        }

        [Fact]
        public void Oracle_repeated_parameters_with_different_converters_keep_distinct_bound_values()
        {
            using var context = Create(OceanBaseCompatibilityMode.Oracle);
            var factory = context.GetService<ISqlExpressionFactory>();
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(int))!;
            var shiftedMapping = (RelationalTypeMapping)mapping.WithComposedConverter(
                new ValueConverter<int, int>(value => value + 100, value => value - 100));
            var ordinary = new SqlParameterExpression("8_amount", typeof(int), mapping);
            var shifted = new SqlParameterExpression("8_amount", typeof(int), shiftedMapping);
            var values = new Dictionary<string, object?> { ["8_amount"] = 11 };
            var processor = context.GetService<IRelationalParameterBasedSqlProcessorFactory>()
                .Create(new RelationalParameterBasedSqlProcessorParameters(false, ParameterTranslationMode.MultipleParameters));
            var processed = processor.Process(factory.Add(ordinary, factory.Add(shifted, factory.Add(ordinary, shifted))),
                new ParametersCacheDecorator(values));
            var generator = new OceanBaseQuerySqlGenerator(context.GetService<QuerySqlGeneratorDependencies>(), OceanBaseCompatibilityMode.Oracle);
            using var command = new OceanBaseCommand();
            foreach (var parameter in generator.GetCommand(processed).Parameters) parameter.AddDbParameter(command, values);
            Assert.Equal(2, command.Parameters.Count);
            Assert.Equal(11, command.Parameters[0].Value);
            Assert.Equal(111, command.Parameters[1].Value);
        }

        private static Context Create(OceanBaseCompatibilityMode mode)
            => new(new DbContextOptionsBuilder<Context>().UseOceanBase("Server=offline;Database=numeric_date_add", mode).Options);

        private sealed class Context(DbContextOptions<Context> options) : DbContext(options)
        {
            public DbSet<Row> Rows => Set<Row>();
        }

        private sealed class Row
        {
            public int Id { get; set; }
            public DateTime Stamp { get; set; }
            public DateTime? OptionalStamp { get; set; }
            public double Amount { get; set; }
        }
    }
}
