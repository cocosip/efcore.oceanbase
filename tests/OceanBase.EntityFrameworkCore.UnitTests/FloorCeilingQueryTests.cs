using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OceanBase.EntityFrameworkCore.Query;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests
{
    public sealed class FloorCeilingQueryTests
    {
        private static readonly (Type Owner, string Name, Type Input, string Property)[] Methods =
        [
            (typeof(Math), "Floor", typeof(double), "Double"),
            (typeof(Math), "Floor", typeof(decimal), "Decimal"),
            (typeof(MathF), "Floor", typeof(float), "Single"),
            (typeof(decimal), "Floor", typeof(decimal), "Decimal"),
            (typeof(Math), "Ceiling", typeof(double), "Double"),
            (typeof(Math), "Ceiling", typeof(decimal), "Decimal"),
            (typeof(MathF), "Ceiling", typeof(float), "Single"),
            (typeof(decimal), "Ceiling", typeof(decimal), "Decimal")
        ];

        public static IEnumerable<object[]> ExactMethods()
        {
            foreach (var mode in new[] { OceanBaseCompatibilityMode.MySql, OceanBaseCompatibilityMode.Oracle })
            foreach (var nulls in new[] { false, true })
            for (var index = 0; index < Methods.Length; index++)
                yield return [mode, nulls, index];
        }

        [Theory]
        [MemberData(nameof(ExactMethods))]
        public void Exact_methods_translate_in_predicate_projection_and_order(
            OceanBaseCompatibilityMode mode, bool relationalNulls, int index)
        {
            using var context = CreateContext(mode, relationalNulls);
            var (owner, name, input, property) = Methods[index];
            var method = owner.GetMethod(name, [input])!;
            Assert.Equal(input, method.ReturnType);
            var row = Expression.Parameter(typeof(Row), "row");
            var call = Expression.Call(method, Expression.Property(row, property));
            var predicate = Expression.Lambda<Func<Row, bool>>(Expression.Equal(call, Expression.Default(input)), row);
            var filtered = context.Rows.Where(predicate);
            var ordered = Expression.Call(typeof(Queryable), nameof(Queryable.OrderBy), [typeof(Row), input],
                filtered.Expression, Expression.Quote(Expression.Lambda(call, row)));
            var projected = Expression.Call(typeof(Queryable), nameof(Queryable.Select), [typeof(Row), input],
                ordered, Expression.Quote(Expression.Lambda(call, row)));
            var sql = filtered.Provider.CreateQuery(projected).ToQueryString();
            if (mode == OceanBaseCompatibilityMode.MySql)
            {
                Assert.Equal(3, sql.Split("CASE", StringSplitOptions.None).Length - 1);
                Assert.Contains(name == "Floor" ? " < TRUNCATE(" : " > TRUNCATE(", sql);
                Assert.Contains(name == "Floor" ? " - 1" : " + 1", sql);
                Assert.DoesNotContain("FLOOR(", sql);
                Assert.DoesNotContain("CEIL(", sql);
            }
            else
                Assert.Equal(3, sql.Split(name == "Floor" ? "FLOOR(" : "CEIL(", StringSplitOptions.None).Length - 1);
            Assert.Contains("WHERE", sql);
            Assert.Contains("ORDER BY", sql);
            Assert.DoesNotContain("CAST(", sql);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Full_finite_inputs_keep_raw_mapping_reader_and_guarded_arithmetic(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var source = context.GetService<IRelationalTypeMappingSource>();
            var factory = context.GetService<ISqlExpressionFactory>();
            var logger = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>();
            var translator = new OceanBaseMathTranslator(factory, source, mode);
            foreach (var (owner, name, type, _) in Methods)
            foreach (var value in Values(type))
            {
                var mapping = source.FindMapping(type)!;
                var input = factory.Constant(value, mapping);
                var result = translator.Translate(null, owner.GetMethod(name, [type])!, [input], logger)!;
                AssertShape(result, input, mapping, source, mode, name);
                Assert.Equal(value, Assert.IsType<SqlConstantExpression>(input).Value);
                if (value is float single)
                    Assert.Equal(BitConverter.SingleToInt32Bits(single), BitConverter.SingleToInt32Bits((float)Assert.IsType<SqlConstantExpression>(input).Value!));
                if (value is double wide)
                    Assert.Equal(BitConverter.DoubleToInt64Bits(wide), BitConverter.DoubleToInt64Bits((double)Assert.IsType<SqlConstantExpression>(input).Value!));
                Assert.Equal(mapping.GetDataReaderMethod(), result.TypeMapping!.GetDataReaderMethod());
                Assert.NotNull(mapping.GenerateSqlLiteral(value));
            }
        }

        private static object[] Values(Type type)
            => type == typeof(float)
                ? [float.MinValue, -1E30f, -MathF.Pow(2, 23), MathF.BitDecrement(-1f), -1f,
                    MathF.BitIncrement(-1f), -1.75f, -float.Epsilon, -0f, 0f, float.Epsilon, 1.75f,
                    MathF.BitDecrement(1f), 1f, MathF.BitIncrement(1f), MathF.Pow(2, 23), 1E30f, float.MaxValue]
                : type == typeof(double)
                    ? [double.MinValue, -1E100d, -Math.Pow(2, 52), Math.BitDecrement(-1d), -1d,
                        Math.BitIncrement(-1d), -1.75d, -double.Epsilon, -0d, 0d, double.Epsilon, 1.75d,
                        Math.BitDecrement(1d), 1d, Math.BitIncrement(1d), Math.Pow(2, 52),
                        Math.BitDecrement((double)long.MaxValue), (double)long.MaxValue,
                        Math.BitIncrement((double)long.MaxValue), 1E100d, double.MaxValue]
                    : [decimal.MinValue, -7922816251426433759354395033.5m, -1.75m, -0.0000000000000000000000000001m,
                        0m, 0.0000000000000000000000000001m, 1.75m, 7922816251426433759354395033.5m, decimal.MaxValue];

        private static void AssertShape(SqlExpression result, SqlExpression input, RelationalTypeMapping mapping,
            IRelationalTypeMappingSource source, OceanBaseCompatibilityMode mode, string name)
        {
            Assert.NotNull(result);
            Assert.Equal(mapping.ClrType, result.Type);
            Assert.Same(mapping, result.TypeMapping);
            if (mode == OceanBaseCompatibilityMode.Oracle)
            {
                var native = Assert.IsType<SqlFunctionExpression>(result);
                Assert.Equal(name == "Floor" ? "FLOOR" : "CEIL", native.Name);
                Assert.Same(input, Assert.Single(native.Arguments!));
                Assert.True(native.IsNullable);
                Assert.All(native.ArgumentsPropagateNullability!, Assert.False);
                return;
            }

            var conditional = Assert.IsType<CaseExpression>(result);
            var branch = Assert.Single(conditional.WhenClauses);
            var comparison = Assert.IsType<SqlBinaryExpression>(branch.Test);
            Assert.Equal(name == "Floor" ? ExpressionType.LessThan : ExpressionType.GreaterThan, comparison.OperatorType);
            Assert.Same(input, comparison.Left);
            var truncated = Assert.IsType<SqlFunctionExpression>(comparison.Right);
            Assert.Equal("TRUNCATE", truncated.Name);
            Assert.Same(mapping, truncated.TypeMapping);
            Assert.Same(input, truncated.Arguments![0]);
            Assert.True(truncated.IsNullable);
            Assert.All(truncated.ArgumentsPropagateNullability!, Assert.False);
            var zero = Assert.IsType<SqlConstantExpression>(truncated.Arguments[1]);
            Assert.Equal(0, zero.Value);
            Assert.Equal(typeof(int), zero.Type);
            Assert.Same(source.FindMapping(typeof(int)), zero.TypeMapping);
            Assert.Null(zero.TypeMapping!.Converter);
            var adjustment = Assert.IsType<SqlBinaryExpression>(branch.Result);
            Assert.Equal(name == "Floor" ? ExpressionType.Subtract : ExpressionType.Add, adjustment.OperatorType);
            Assert.Same(truncated, adjustment.Left);
            Assert.Same(mapping, adjustment.TypeMapping);
            Assert.Equal(mapping.ClrType, adjustment.Type);
            var one = Assert.IsType<SqlConstantExpression>(adjustment.Right);
            var carrier = mapping.Converter?.ProviderClrType ?? mapping.ClrType;
            carrier = Nullable.GetUnderlyingType(carrier) ?? carrier;
            Assert.Equal(Convert.ChangeType(1, carrier), one.Value);
            Assert.Equal(carrier, one.Type);
            Assert.Same(source.FindMapping(carrier), one.TypeMapping);
            Assert.Null(one.TypeMapping!.Converter);
            Assert.Same(truncated, conditional.ElseResult);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Numeric_converters_keep_stored_semantics_and_fresh_carrier_constants(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var source = context.GetService<IRelationalTypeMappingSource>();
            var factory = context.GetService<ISqlExpressionFactory>();
            var logger = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>();
            var translator = new OceanBaseMathTranslator(factory, source, mode);
            ValueConverter[] converters =
            [
                new ValueConverter<decimal, decimal>(value => value * -2m + 10m, value => (value - 10m) / -2m),
                new ValueConverter<double, double>(value => value + 10d, value => value - 10d),
                new ValueConverter<float, float>(value => value * -2f, value => value / -2f),
                new ValueConverter<double, decimal>(value => (decimal)value, value => (double)value),
                new ValueConverter<double, decimal?>(value => (decimal)value, value => (double)value!.Value),
                new ValueConverter<decimal, double>(value => (double)value, value => (decimal)value),
                new ValueConverter<float, double>(value => value, value => checked((float)value)),
                new ValueConverter<double, float>(value => (float)value, value => value),
                new ValueConverter<decimal, long>(value => (long)value, value => value),
                new ValueConverter<decimal, long?>(value => (long)value, value => value!.Value),
                new ValueConverter<double, ulong>(value => (ulong)value, value => value),
                new ValueConverter<decimal, byte>(value => (byte)value, value => value),
                new ValueConverter<decimal, ushort>(value => (ushort)value, value => value),
                new ValueConverter<decimal, uint>(value => (uint)value, value => value)
            ];
            foreach (var converter in converters)
            foreach (var name in new[] { "Floor", "Ceiling" })
            {
                var mapping = (RelationalTypeMapping)source.FindMapping(converter.ProviderClrType)!.WithComposedConverter(converter);
                var input = factory.Constant(Convert.ChangeType(1, converter.ModelClrType), mapping);
                var owner = converter.ModelClrType == typeof(float) ? typeof(MathF) : typeof(Math);
                var result = translator.Translate(null, owner.GetMethod(name, [converter.ModelClrType])!, [input], logger)!;
                AssertShape(result, input, mapping, source, mode, name);
                Assert.Same(converter, result.TypeMapping!.Converter);
            }
            var sql = context.Rows.Where(row => Math.Floor(row.Transformed) == 1.23456789m
                && Math.Ceiling(row.Cross) == 1E28d).ToQueryString();
            Assert.Contains("7.53086422", sql);
            Assert.Contains("10000000000000000000000000000", sql);
            Assert.DoesNotContain("CAST(", sql);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Nullable_compositions_keep_independent_result_null_tests(OceanBaseCompatibilityMode mode, bool relationalNulls)
        {
            using var context = CreateContext(mode, relationalNulls);
            var sql = context.Rows.Where(row => !(Math.Floor(row.Optional!.Value) == 1m))
                .Select(row => new
                {
                    A = row.Id > 0 ? (decimal?)Math.Ceiling(row.Optional!.Value) : null,
                    B = (decimal?)Math.Floor(row.Optional!.Value) ?? -1.75m,
                    C = Math.Ceiling(Math.Abs(row.Decimal) + Math.Floor(row.Decimal))
                }).ToQueryString();
            Assert.Contains("CASE", sql);
            Assert.Contains("COALESCE(", sql);
            Assert.Contains("ABS(", sql);
            Assert.Equal(!relationalNulls, sql.Contains("IS NULL", StringComparison.Ordinal));
            foreach (var name in new[] { "Floor", "Ceiling" })
            foreach (var optional in new[] { false, true })
            {
                var results = optional
                    ? name == "Floor" ? context.Rows.Select(row => (decimal?)Math.Floor(row.Optional!.Value))
                        : context.Rows.Select(row => (decimal?)Math.Ceiling(row.Optional!.Value))
                    : name == "Floor" ? context.Rows.Select(row => (decimal?)Math.Floor(row.Decimal))
                        : context.Rows.Select(row => (decimal?)Math.Ceiling(row.Decimal));
                var nullSql = results
                    .Where(result => result == null).ToQueryString();
                Assert.Contains(" IS NULL", nullSql);
                Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "TRUNCATE(" : name == "Floor" ? "FLOOR(" : "CEIL(",
                    nullSql.Split("WHERE", StringSplitOptions.None)[1]);
                Assert.DoesNotContain("WHERE 0 = 1", nullSql);
                Assert.Contains(" IS NOT NULL", results.Where(result => result != null).ToQueryString());
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Narrow_declarations_keep_full_query_inputs_and_complete_validation(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var source = context.GetService<IRelationalTypeMappingSource>();
            var factory = context.GetService<ISqlExpressionFactory>();
            var logger = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>();
            var translator = new OceanBaseMathTranslator(factory, source, mode);
            foreach (var (type, declaration) in mode == OceanBaseCompatibilityMode.MySql
                ? new (Type, string)[] { (typeof(decimal), " numeric(5,2) unsigned "), (typeof(float), " float(24) unsigned "),
                    (typeof(double), " double precision unsigned ") }
                : [(typeof(decimal), " number(5,2) "), (typeof(float), " binary_float "), (typeof(double), " binary_double ")])
            foreach (var name in new[] { "Floor", "Ceiling" })
            {
                var mapping = source.FindMapping(type, storeTypeName: declaration)!;
                var input = factory.Constant(Values(type)[0], mapping);
                var method = (type == typeof(float) ? typeof(MathF) : typeof(Math)).GetMethod(name, [type])!;
                AssertShape(translator.Translate(null, method, [input], logger)!, input, mapping, source, mode, name);
            }
            foreach (var declaration in mode == OceanBaseCompatibilityMode.MySql
                ? new[] { "DECIMAL(66,0)", "DECIMAL(5,2) trailing", "VARCHAR(10)", "NUMBER" }
                : ["NUMBER(39)", "NUMBER(5,2) trailing", "VARCHAR2(10)", "BINARY_DOUBLE"])
                Assert.Null(translator.Translate(null, typeof(Math).GetMethod("Floor", [typeof(decimal)])!,
                    [factory.Constant(1m, new DecimalTypeMapping(declaration))], logger));
            foreach (var declaration in mode == OceanBaseCompatibilityMode.MySql ? new[] { "BOOL", "BOOLEAN", "TINYINT UNSIGNED" } : ["NUMBER(2)"])
            {
                var mapping = (RelationalTypeMapping)source.FindMapping(typeof(byte), storeTypeName: declaration)!
                    .WithComposedConverter(new ValueConverter<decimal, byte>(value => (byte)value, value => value));
                var input = factory.Constant(1m, mapping);
                foreach (var name in new[] { "Floor", "Ceiling" })
                    AssertShape(translator.Translate(null, typeof(Math).GetMethod(name, [typeof(decimal)])!, [input], logger)!,
                        input, mapping, source, mode, name);
            }
            var capture = decimal.MaxValue;
            var sql = context.Rows.Where(row => Math.Floor(row.Decimal + capture) == decimal.MinValue)
                .Select(row => Math.Ceiling(row.Decimal + -0.0000000000000000000000000001m)).ToQueryString();
            Assert.Contains("79228162514264337593543950335", sql);
            Assert.Contains("0.0000000000000000000000000001", sql);
            Assert.DoesNotContain("CAST(", sql);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Unsupported_mappings_methods_and_nonfinite_inputs_stay_rejected(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var source = context.GetService<IRelationalTypeMappingSource>();
            var factory = context.GetService<ISqlExpressionFactory>();
            var logger = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>();
            var translator = new OceanBaseMathTranslator(factory, source, mode);
#pragma warning disable EF1001
            var nullConverter = new ValueConverter<decimal, decimal>(value => value, value => value, convertsNulls: true);
#pragma warning restore EF1001
            RelationalTypeMapping[] mappings =
            [
                (RelationalTypeMapping)source.FindMapping(typeof(string))!.WithComposedConverter(
                    new ValueConverter<decimal, string>(value => value.ToString(), value => decimal.Parse(value))),
                (RelationalTypeMapping)source.FindMapping(typeof(byte[]))!.WithComposedConverter(
                    new ValueConverter<decimal, byte[]>(value => BitConverter.GetBytes((double)value), value => (decimal)BitConverter.ToDouble(value))),
                (RelationalTypeMapping)new TestMapping(typeof(bool), "TINYINT").WithComposedConverter(
                    new ValueConverter<decimal, bool>(value => value != 0, value => value ? 1m : 0m)),
                (RelationalTypeMapping)source.FindMapping(typeof(DateTime))!.WithComposedConverter(
                    new ValueConverter<decimal, DateTime>(value => new DateTime((long)value), value => value.Ticks)),
                (RelationalTypeMapping)new TestMapping(typeof(object), "NUMBER").WithComposedConverter(
                    new ValueConverter<decimal, object>(value => value, value => (decimal)value)),
                (RelationalTypeMapping)source.FindMapping(typeof(decimal))!.WithComposedConverter(nullConverter)
            ];
            foreach (var name in new[] { "Floor", "Ceiling" })
            {
                var method = typeof(Math).GetMethod(name, [typeof(decimal)])!;
                foreach (var mapping in mappings)
                    Assert.Null(translator.Translate(null, method, [factory.Constant(1m, mapping)], logger));
                var numeric = factory.Constant(1m, source.FindMapping(typeof(decimal)));
                Assert.Null(translator.Translate(numeric, method, [numeric], logger));
                Assert.Null(translator.Translate(null, method, [factory.Constant(1, source.FindMapping(typeof(int)))], logger));
            }
            MethodInfo[] unsupported =
            [
                typeof(double).GetMethod("Floor", [typeof(double)])!,
                typeof(float).GetMethod("Ceiling", [typeof(float)])!,
                typeof(Math).GetMethod("Round", [typeof(double)])!,
                typeof(Math).GetMethod("Log", [typeof(double)])!,
                typeof(Math).GetMethod("Pow", [typeof(double), typeof(double)])!,
                typeof(FloorCeilingQueryTests).GetMethod(nameof(Floor), BindingFlags.NonPublic | BindingFlags.Static)!
            ];
            foreach (var method in unsupported)
                Assert.Null(translator.Translate(null, method, [factory.Constant(1m)], logger));
            foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
                Assert.Throws<ArgumentOutOfRangeException>(() => context.Rows.Where(row => Math.Floor(row.Double + value) == 1d).ToQueryString());
            foreach (var value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                Assert.Throws<ArgumentOutOfRangeException>(() => context.Rows.Where(row => MathF.Ceiling(row.Single + value) == 1f).ToQueryString());
            Assert.Throws<InvalidOperationException>(() => context.Rows.Where(row => Floor(row.Decimal) == 1m).ToQueryString());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Plugin_and_model_function_precedence_remain_intact(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var source = context.GetService<IRelationalTypeMappingSource>();
            var factory = context.GetService<ISqlExpressionFactory>();
            var logger = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>();
            var provider = new OceanBaseMethodCallTranslatorProvider(
                new RelationalMethodCallTranslatorProviderDependencies(factory, [new OverridePlugin(factory)], source), mode);
            foreach (var (owner, name, type, _) in Methods)
            {
                var method = owner.GetMethod(name, [type])!;
                var input = factory.Constant(Convert.ChangeType(1, type));
                Assert.Equal(17, Assert.IsType<SqlConstantExpression>(provider.Translate(context.Model, null, method, [input], logger)).Value);
                var model = new ModelBuilder();
                model.HasDbFunction(method).HasTranslation(_ => factory.Constant(23));
                Assert.Equal(23, Assert.IsType<SqlConstantExpression>(provider.Translate(
                    (Microsoft.EntityFrameworkCore.Metadata.IModel)model.Model, null, method, [input], logger)).Value);
            }
        }

        private static decimal Floor(decimal value) => value;

        private sealed class TestMapping(Type type, string storeType) : RelationalTypeMapping(storeType, type)
        {
            protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
                => new TestMapping(parameters.CoreParameters.ClrType, parameters.StoreType);
        }

        private sealed class OverridePlugin(ISqlExpressionFactory factory) : IMethodCallTranslatorPlugin, IMethodCallTranslator
        {
            public IEnumerable<IMethodCallTranslator> Translators => [this];
            public SqlExpression? Translate(SqlExpression? instance, MethodInfo method, IReadOnlyList<SqlExpression> arguments,
                IDiagnosticsLogger<DbLoggerCategory.Query> logger) => factory.Constant(17);
        }

        private static TestContext CreateContext(OceanBaseCompatibilityMode mode, bool relationalNulls = false)
        {
            var builder = new DbContextOptionsBuilder<TestContext>().UseOceanBase("Server=localhost;Database=test", mode);
            ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(
                builder.Options.Extensions.OfType<RelationalOptionsExtension>().Single().WithUseRelationalNulls(relationalNulls));
            return new TestContext(builder.Options);
        }

        private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
        {
            public DbSet<Row> Rows => Set<Row>();
            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity<Row>().Property(row => row.Decimal).HasPrecision(5, 2);
                modelBuilder.Entity<Row>().Property(row => row.Transformed)
                    .HasConversion(value => value * -2m + 10m, value => (value - 10m) / -2m).HasPrecision(5, 2);
                modelBuilder.Entity<Row>().Property(row => row.Cross).HasConversion<decimal>();
            }
        }

        private sealed class Row
        {
            public int Id { get; set; }
            public float Single { get; set; }
            public double Double { get; set; }
            public decimal Decimal { get; set; }
            public decimal? Optional { get; set; }
            public decimal Transformed { get; set; }
            public double Cross { get; set; }
        }
    }
}
