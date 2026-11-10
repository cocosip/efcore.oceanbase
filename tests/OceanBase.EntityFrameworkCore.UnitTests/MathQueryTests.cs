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
    public sealed class MathQueryTests
    {
        private static readonly (Type Owner, string Name, Type Input, string Property)[] Methods =
        [
            (typeof(Math), "Abs", typeof(sbyte), "SByte"),
            (typeof(Math), "Abs", typeof(short), "Int16"),
            (typeof(Math), "Abs", typeof(int), "Int32"),
            (typeof(Math), "Abs", typeof(long), "Int64"),
            (typeof(Math), "Abs", typeof(float), "Single"),
            (typeof(Math), "Abs", typeof(double), "Double"),
            (typeof(Math), "Abs", typeof(decimal), "Decimal"),
            (typeof(MathF), "Abs", typeof(float), "Single"),
            (typeof(decimal), "Abs", typeof(decimal), "Decimal"),
            (typeof(Math), "Sign", typeof(sbyte), "SByte"),
            (typeof(Math), "Sign", typeof(short), "Int16"),
            (typeof(Math), "Sign", typeof(int), "Int32"),
            (typeof(Math), "Sign", typeof(long), "Int64"),
            (typeof(Math), "Sign", typeof(float), "Single"),
            (typeof(Math), "Sign", typeof(double), "Double"),
            (typeof(Math), "Sign", typeof(decimal), "Decimal"),
            (typeof(MathF), "Sign", typeof(float), "Single"),
            (typeof(decimal), "Sign", typeof(decimal), "Decimal"),
            (typeof(Math), "Truncate", typeof(double), "Double"),
            (typeof(Math), "Truncate", typeof(decimal), "Decimal"),
            (typeof(MathF), "Truncate", typeof(float), "Single"),
            (typeof(decimal), "Truncate", typeof(decimal), "Decimal")
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
        public void Exact_method_translates_in_predicate_projection_and_order(
            OceanBaseCompatibilityMode mode, bool relationalNulls, int index)
        {
            using var context = CreateContext(mode, relationalNulls);
            var (owner, name, input, property) = Methods[index];
            var method = owner.GetMethod(name, [input])!;
            Assert.Equal(name == "Sign" ? typeof(int) : input, method.ReturnType);
            var row = Expression.Parameter(typeof(Row), "row");
            var call = Expression.Call(method, Expression.Property(row, property));
            var predicate = Expression.Lambda<Func<Row, bool>>(
                Expression.Equal(call, Expression.Default(method.ReturnType)), row);
            var filtered = context.Rows.Where(predicate);
            var ordered = Expression.Call(typeof(Queryable), nameof(Queryable.OrderBy), [typeof(Row), method.ReturnType],
                filtered.Expression, Expression.Quote(Expression.Lambda(call, row)));
            var projected = Expression.Call(typeof(Queryable), nameof(Queryable.Select), [typeof(Row), method.ReturnType],
                ordered, Expression.Quote(Expression.Lambda(call, row)));
            var sql = filtered.Provider.CreateQuery(projected).ToQueryString();
            var function = name == "Truncate" ? mode == OceanBaseCompatibilityMode.MySql ? "TRUNCATE(" : "TRUNC("
                : name.ToUpperInvariant() + "(";
            Assert.Equal(3, sql.Split(function, StringSplitOptions.None).Length - 1);
            Assert.Contains("WHERE", sql);
            Assert.Contains("ORDER BY", sql);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Full_finite_ranges_keep_source_mapping_and_checked_reader_hooks(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var source = context.GetService<IRelationalTypeMappingSource>();
            var factory = context.GetService<ISqlExpressionFactory>();
            var translator = new OceanBaseMathTranslator(factory, source, mode);
            var logger = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>();
            foreach (var (owner, name, type, _) in Methods)
            {
                var method = owner.GetMethod(name, [type])!;
                var mapping = source.FindMapping(type)!;
                foreach (var value in Values(type))
                {
                    var input = factory.Constant(value, mapping);
                    var result = Assert.IsType<SqlFunctionExpression>(translator.Translate(null, method, [input], logger));
                    Assert.Equal(method.ReturnType, result.Type);
                    Assert.True(result.IsNullable);
                    Assert.All(result.ArgumentsPropagateNullability!, Assert.False);
                    Assert.Same(mapping, result.Arguments![0].TypeMapping);
                    Assert.Equal(mapping.GenerateSqlLiteral(value), result.Arguments[0].TypeMapping!.GenerateSqlLiteral(value));
                    if (name == "Sign")
                    {
                        Assert.Equal(typeof(int), result.TypeMapping!.ClrType);
                        Assert.Null(result.TypeMapping.Converter);
                        Assert.Equal(source.FindMapping(typeof(int))!.GetDataReaderMethod(), result.TypeMapping.GetDataReaderMethod());
                    }
                    else
                    {
                        Assert.Same(mapping, result.TypeMapping);
                        Assert.Equal(mapping.GetDataReaderMethod(), result.TypeMapping!.GetDataReaderMethod());
                    }
                    if (name == "Truncate" && mode == OceanBaseCompatibilityMode.MySql)
                    {
                        var zero = Assert.IsType<SqlConstantExpression>(result.Arguments[1]);
                        Assert.Equal(0, zero.Value);
                        Assert.Equal(typeof(int), zero.TypeMapping!.ClrType);
                        Assert.Null(zero.TypeMapping.Converter);
                    }
                }
            }
        }

        private static object[] Values(Type type)
            => type == typeof(sbyte) ? [sbyte.MinValue, (sbyte)-1, (sbyte)0, sbyte.MaxValue]
                : type == typeof(short) ? [short.MinValue, (short)-1, (short)0, short.MaxValue]
                : type == typeof(int) ? [int.MinValue, -1, 0, int.MaxValue]
                : type == typeof(long) ? [long.MinValue, -1L, 0L, long.MaxValue]
                : type == typeof(float) ? [float.MinValue, -1.75f, -0f, 0f, float.MaxValue]
                : type == typeof(double) ? [double.MinValue, -1.75d, -0d, 0d, double.MaxValue]
                : [decimal.MinValue, -1.75m, 0m, decimal.MaxValue];

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Nullable_functions_keep_case_coalesce_negation_and_result_null_tests(
            OceanBaseCompatibilityMode mode, bool relationalNulls)
        {
            using var context = CreateContext(mode, relationalNulls);
            var sql = context.Rows.Where(row => !(Math.Sign(row.Optional!.Value) == 1))
                .Select(row => new
                {
                    A = row.Id > 0 ? Math.Abs(row.Int32) : row.Optional,
                    T = (decimal?)Math.Truncate(row.OptionalDecimal!.Value) ?? -1.75m,
                    S = (int?)Math.Sign(row.Optional!.Value)
                }).ToQueryString();
            Assert.Contains("ABS(", sql);
            Assert.Contains("SIGN(", sql);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "TRUNCATE(" : "TRUNC(", sql);
            Assert.Contains("CASE", sql);
            Assert.Contains("COALESCE(", sql);
            Assert.Equal(!relationalNulls, sql.Contains("IS NULL", StringComparison.Ordinal));
            IQueryable[] nullTests =
            [
                context.Rows.Select(row => (int?)Math.Abs(row.Optional!.Value)).Where(result => result == null),
                context.Rows.Select(row => (int?)Math.Sign(row.Optional!.Value)).Where(result => result == null),
                context.Rows.Select(row => (decimal?)Math.Truncate(row.OptionalDecimal!.Value)).Where(result => result == null)
            ];
            foreach (var query in nullTests)
            {
                var nullSql = query.ToQueryString();
                Assert.Contains(" IS NULL", nullSql);
                Assert.Contains("(", nullSql.Split("WHERE", StringSplitOptions.None)[1]);
                Assert.DoesNotContain("WHERE 0 = 1", nullSql);
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Mapped_literals_and_captures_keep_extrema_fraction_and_wide_float_inputs(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var minimum = int.MinValue;
            var longMinimum = long.MinValue;
            var decimalMinimum = decimal.MinValue;
            var singleMinimum = float.MinValue;
            var doubleMinimum = double.MinValue;
            var sql = context.Rows.Where(row => Math.Abs(row.Int32 + minimum) == int.MaxValue
                && Math.Sign(row.Int64 + longMinimum) == -1
                && Math.Truncate(row.Decimal + decimalMinimum) == decimal.MaxValue
                && MathF.Truncate(row.Single + singleMinimum) == float.MaxValue
                && Math.Truncate(row.Double + doubleMinimum) == double.MaxValue)
                .Select(row => new { A = Math.Abs(row.Int32 + int.MinValue), T = Math.Truncate(row.Decimal + -1.75m),
                    F = MathF.Abs(row.Single + float.MinValue), D = Math.Sign(row.Double + double.MinValue) }).ToQueryString();
            foreach (var capture in new[] { "minimum", "longMinimum", "decimalMinimum", "singleMinimum", "doubleMinimum" })
                Assert.Contains(capture, sql);
            Assert.Contains("79228162514264337593543950335", sql);
            Assert.Contains("-1.75", sql);
            Assert.Contains("E+38", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("E+308", sql, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Numeric_converters_preserve_operand_and_result_but_not_sign_or_precision(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var source = context.GetService<IRelationalTypeMappingSource>();
            var factory = context.GetService<ISqlExpressionFactory>();
            var logger = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>();
            var translator = new OceanBaseMathTranslator(factory, source, mode);
            var mappings = new[]
            {
                (RelationalTypeMapping)source.FindMapping(typeof(long))!.WithComposedConverter(
                    new ValueConverter<int, long>(value => value, value => checked((int)value))),
                (RelationalTypeMapping)source.FindMapping(typeof(decimal))!.WithComposedConverter(
                    new ValueConverter<decimal, decimal>(value => value * -2m, value => value / -2m)),
                (RelationalTypeMapping)source.FindMapping(typeof(ulong))!.WithComposedConverter(
                    new ValueConverter<long, ulong>(value => checked((ulong)value), value => checked((long)value)))
            };
            foreach (var mapping in mappings)
            foreach (var name in mapping.ClrType == typeof(decimal) ? new[] { "Abs", "Sign", "Truncate" } : new[] { "Abs", "Sign" })
            {
                var method = typeof(Math).GetMethod(name, [mapping.ClrType])!;
                var result = Assert.IsType<SqlFunctionExpression>(translator.Translate(null, method,
                    [factory.Constant(Convert.ChangeType(1, mapping.ClrType), mapping)], logger));
                Assert.Same(mapping, result.Arguments![0].TypeMapping);
                Assert.Same(name == "Sign" ? source.FindMapping(typeof(int)) : mapping, result.TypeMapping);
                if (name == "Truncate" && mode == OceanBaseCompatibilityMode.MySql)
                {
                    Assert.Equal(typeof(int), result.Arguments[1].TypeMapping!.ClrType);
                    Assert.Null(result.Arguments[1].TypeMapping!.Converter);
                }
            }
            var sql = context.Rows.Where(row => Math.Abs(row.Converted) == 123 && Math.Sign(row.Converted) == -1
                && Math.Truncate(row.Transformed) == 1.25m).ToQueryString();
            Assert.Contains("ABS(", sql);
            Assert.Contains("SIGN(", sql);
            Assert.Contains("-2.5", sql);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Strict_numeric_declarations_keep_aliases_unsignedness_and_complete_validation(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var source = context.GetService<IRelationalTypeMappingSource>();
            var factory = context.GetService<ISqlExpressionFactory>();
            var logger = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>();
            var translator = new OceanBaseMathTranslator(factory, source, mode);
            var declarations = mode == OceanBaseCompatibilityMode.MySql
                ? new[] { "bool", " BOOLEAN ", " tinyint(3) unsigned ", "MEDIUMINT", " integer(11) " }
                : [" number(10) ", "INTEGER", "smallint", "DECIMAL(10,0)", "NUMERIC(10,0)"];
            foreach (var storeType in declarations)
            {
                var mapping = source.FindMapping(typeof(int), storeTypeName: storeType)!;
                var result = Assert.IsType<SqlFunctionExpression>(translator.Translate(null,
                    typeof(Math).GetMethod("Abs", [typeof(int)])!, [factory.Constant(-1, mapping)], logger));
                Assert.Same(mapping, result.TypeMapping);
            }
            foreach (var storeType in mode == OceanBaseCompatibilityMode.MySql
                ? new[] { "INT trailing", "VARCHAR(10)", "NUMBER", "INT(256)", "DECIMAL(66,0)" }
                : ["NUMBER trailing", "VARCHAR2(10)", "FLOAT", "BINARY_DOUBLE", "NUMBER(39)"])
                Assert.Null(translator.Translate(null, typeof(Math).GetMethod("Abs", [typeof(int)])!,
                    [factory.Constant(1, new IntTypeMapping(storeType))], logger));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Fractional_numeric_declarations_and_unsigned_converter_carriers_keep_their_mapping(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var factory = context.GetService<ISqlExpressionFactory>();
            var source = context.GetService<IRelationalTypeMappingSource>();
            var logger = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>();
            var translator = new OceanBaseMathTranslator(factory, source, mode);
            foreach (var (type, declaration) in mode == OceanBaseCompatibilityMode.MySql
                ? new (Type, string)[] { (typeof(decimal), " numeric(65,30) unsigned "), (typeof(float), " float(24) unsigned "),
                    (typeof(double), " double precision unsigned ") }
                : [(typeof(decimal), " number(38,9) "), (typeof(float), " binary_float "), (typeof(double), " binary_double ")])
            {
                var mapping = source.FindMapping(type, storeTypeName: declaration)!;
                foreach (var name in new[] { "Abs", "Sign", "Truncate" })
                {
                    var method = (type == typeof(float) ? typeof(MathF) : typeof(Math)).GetMethod(name, [type])!;
                    var result = Assert.IsType<SqlFunctionExpression>(translator.Translate(null, method,
                        [factory.Constant(Convert.ChangeType(-1.75m, type), mapping)], logger));
                    Assert.Same(mapping, result.Arguments![0].TypeMapping);
                    if (name != "Sign") Assert.Same(mapping, result.TypeMapping);
                }
            }
            foreach (var converter in new ValueConverter[]
            {
                new ValueConverter<int, byte>(value => checked((byte)value), value => value),
                new ValueConverter<int, ushort>(value => checked((ushort)value), value => value),
                new ValueConverter<int, uint>(value => checked((uint)value), value => checked((int)value)),
                new ValueConverter<int, ulong>(value => checked((ulong)value), value => checked((int)value))
            })
            {
                var mapping = (RelationalTypeMapping)source.FindMapping(converter.ProviderClrType)!.WithComposedConverter(converter);
                var result = Assert.IsType<SqlFunctionExpression>(translator.Translate(null, typeof(Math).GetMethod("Abs", [typeof(int)])!,
                    [factory.Constant(1, mapping)], logger));
                Assert.Same(mapping, result.TypeMapping);
                Assert.Same(mapping, result.Arguments![0].TypeMapping);
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Functions_do_not_enable_nonfinite_query_inputs(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            foreach (var value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                Assert.Throws<ArgumentOutOfRangeException>(() => context.Rows
                    .Where(row => MathF.Abs(row.Single + value) == 1f).ToQueryString());
            foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
                Assert.Throws<ArgumentOutOfRangeException>(() => context.Rows
                    .Where(row => Math.Truncate(row.Double + value) == 1d).ToQueryString());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Unsupported_provider_carriers_null_converters_and_signatures_are_rejected(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var factory = context.GetService<ISqlExpressionFactory>();
            var source = context.GetService<IRelationalTypeMappingSource>();
            var logger = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>();
            var translator = new OceanBaseMathTranslator(factory, source, mode);
            var abs = typeof(Math).GetMethod("Abs", [typeof(decimal)])!;
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
            foreach (var mapping in mappings)
                Assert.Null(translator.Translate(null, abs, [factory.Constant(1m, mapping)], logger));
            var numeric = factory.Constant(1m, source.FindMapping(typeof(decimal)));
            MethodInfo[] unsupported =
            [
                typeof(Math).GetMethod("Round", [typeof(decimal)])!,
                typeof(Math).GetMethod("Log2", [typeof(double)])!,
                typeof(float).GetMethod("Abs", [typeof(float)])!,
                typeof(double).GetMethod("Sign", [typeof(double)])!,
                typeof(double).GetMethod("Truncate", [typeof(double)])!,
                typeof(Half).GetMethod("Abs", [typeof(Half)])!,
                typeof(Math).GetMethod("Abs", [typeof(nint)])!,
                typeof(MathQueryTests).GetMethod(nameof(Abs), BindingFlags.NonPublic | BindingFlags.Static)!
            ];
            foreach (var method in unsupported)
                Assert.Null(translator.Translate(null, method, [numeric], logger));
            Assert.Null(translator.Translate(numeric, abs, [numeric], logger));
            Assert.Null(translator.Translate(null, abs, [factory.Constant(1, source.FindMapping(typeof(int)))], logger));
            Assert.Throws<InvalidOperationException>(() => context.Rows.Where(row => Math.Abs(row.Text) == 1).ToQueryString());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Model_function_and_plugin_precede_provider_and_modes_remain_scoped(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            using var other = CreateContext(mode == OceanBaseCompatibilityMode.MySql
                ? OceanBaseCompatibilityMode.Oracle : OceanBaseCompatibilityMode.MySql);
            var factory = context.GetService<ISqlExpressionFactory>();
            var source = context.GetService<IRelationalTypeMappingSource>();
            var logger = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>();
            var method = typeof(Math).GetMethod("Abs", [typeof(int)])!;
            var provider = new OceanBaseMethodCallTranslatorProvider(
                new RelationalMethodCallTranslatorProviderDependencies(factory, [new OverridePlugin(factory)], source), mode);
            Assert.Equal(17, Assert.IsType<SqlConstantExpression>(provider.Translate(context.Model, null, method,
                [factory.Constant(-1)], logger)).Value);
            var model = new ModelBuilder();
            model.HasDbFunction(method).HasTranslation(_ => factory.Constant(23));
            Assert.Equal(23, Assert.IsType<SqlConstantExpression>(provider.Translate(
                (Microsoft.EntityFrameworkCore.Metadata.IModel)model.Model, null, method, [factory.Constant(-1)], logger)).Value);
            Assert.NotSame(context.GetService<IMethodCallTranslatorProvider>(), other.GetService<IMethodCallTranslatorProvider>());
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "TRUNC(" : "TRUNCATE(",
                other.Rows.OrderBy(row => Math.Truncate(row.Decimal)).ToQueryString());
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "TRUNCATE(" : "TRUNC(",
                context.Rows.OrderBy(row => Math.Truncate(row.Decimal)).ToQueryString());
        }

        private static int Abs(int value) => value;

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
                modelBuilder.Entity<Row>().Property(row => row.Converted).HasConversion<long>();
                modelBuilder.Entity<Row>().Property(row => row.Transformed).HasConversion(value => value * -2m, value => value / -2m);
                modelBuilder.Entity<Row>().Property(row => row.Text).HasConversion<string>();
            }
        }

        private sealed class Row
        {
            public int Id { get; set; }
            public sbyte SByte { get; set; }
            public short Int16 { get; set; }
            public int Int32 { get; set; }
            public long Int64 { get; set; }
            public float Single { get; set; }
            public double Double { get; set; }
            public decimal Decimal { get; set; }
            public int? Optional { get; set; }
            public decimal? OptionalDecimal { get; set; }
            public int Converted { get; set; }
            public decimal Transformed { get; set; }
            public int Text { get; set; }
        }
    }
}
