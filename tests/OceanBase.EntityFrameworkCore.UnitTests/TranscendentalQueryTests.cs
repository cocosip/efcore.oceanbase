using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
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
using OceanBase.EntityFrameworkCore.Storage;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests;

public sealed class TranscendentalQueryTests
{
    private static readonly string[] Names = ["Sqrt", "Exp", "Log", "LogBase", "Log10", "Pow"];

    public static IEnumerable<object[]> ExactMethods()
    {
        foreach (var mode in new[] { OceanBaseCompatibilityMode.MySql, OceanBaseCompatibilityMode.Oracle })
        foreach (var nulls in new[] { false, true })
        foreach (var single in new[] { false, true })
        foreach (var name in Names)
            yield return [mode, nulls, single, name];
    }

    private static MethodInfo Method(string name, Type type)
        => (type == typeof(float) ? typeof(MathF) : typeof(Math)).GetMethod(name == "LogBase" ? "Log" : name,
            name is "LogBase" or "Pow" ? [type, type] : [type])!;

    [Theory]
    [MemberData(nameof(ExactMethods))]
    public void All_twelve_identities_require_server_predicate_order_and_projection(
        OceanBaseCompatibilityMode mode, bool nulls, bool single, string name)
    {
        using var context = Context(mode, nulls);
        var type = single ? typeof(float) : typeof(double);
        var row = Expression.Parameter(typeof(Row), "r");
        var x = Expression.Property(row, single ? "Single" : "Double");
        var args = name is "LogBase" or "Pow" ? new Expression[] { x, Expression.Constant(Convert.ChangeType(.5, type)) } : [x];
        var method = Method(name, type);
        Assert.Equal(type, method.ReturnType);
        var call = Expression.Call(method, args);
        var filtered = context.Rows.Where(Expression.Lambda<Func<Row, bool>>(Expression.GreaterThan(call, Expression.Default(type)), row));
        var ordered = Expression.Call(typeof(Queryable), "OrderBy", [typeof(Row), type], filtered.Expression,
            Expression.Quote(Expression.Lambda(call, row)));
        var projected = Expression.Call(typeof(Queryable), "Select", [typeof(Row), type], ordered,
            Expression.Quote(Expression.Lambda(call, row)));
        var sql = filtered.Provider.CreateQuery(projected).ToQueryString();
        var native = name switch { "Sqrt" => "SQRT(", "Exp" => "EXP(", "Pow" => "POWER(",
            "Log10" => mode == OceanBaseCompatibilityMode.MySql ? "LOG10(" : "LOG(",
            "LogBase" => mode == OceanBaseCompatibilityMode.Oracle ? "LOG(" : "LN(", _ => "LN(" };
        Assert.Contains(native, sql);
        Assert.Contains("WHERE", sql);
        Assert.Contains("ORDER BY", sql);
        Assert.Equal(single ? 3 : 0, sql.Split("CAST(", StringSplitOptions.None).Length - 1);
        if (single) Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? " AS FLOAT)" : " AS BINARY_FLOAT)", sql);
        if (name == "LogBase" && mode == OceanBaseCompatibilityMode.MySql)
        {
            Assert.Equal(6, sql.Split("LN(", StringSplitOptions.None).Length - 1);
            Assert.Contains(" / LN(", sql);
            Assert.DoesNotContain(" DIV ", sql);
        }
        Assert.DoesNotContain("__oceanbase_internal", sql);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql)]
    [InlineData(OceanBaseCompatibilityMode.Oracle)]
    public void Arguments_keep_independent_carriers_converters_and_result_owner(OceanBaseCompatibilityMode mode)
    {
        using var context = Context(mode);
        var source = context.GetService<IRelationalTypeMappingSource>();
        var factory = context.GetService<ISqlExpressionFactory>();
        var logger = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>();
        var translator = new OceanBaseMathTranslator(factory, source, mode);
        ValueConverter[] converters =
        [
            new ValueConverter<double, double>(v => v * -2 + 10, v => (v - 10) / -2),
            new ValueConverter<double, float>(v => (float)v + 10, v => v - 10d),
            new ValueConverter<double, float?>(v => (float)v + 10, v => v!.Value - 10d),
            new ValueConverter<float, double>(v => v + 10d, v => (float)(v - 10)),
            new ValueConverter<double, decimal>(v => (decimal)v + 10, v => (double)(v - 10)),
            new ValueConverter<double, decimal?>(v => (decimal)v + 10, v => (double)(v!.Value - 10)),
            new ValueConverter<double, long>(v => (long)v + 10, v => v - 10d),
            new ValueConverter<double, ulong>(v => (ulong)v, v => v),
            new ValueConverter<double, int>(v => (int)v, v => v),
            new ValueConverter<double, uint>(v => (uint)v, v => v),
            new ValueConverter<double, short>(v => (short)v, v => v),
            new ValueConverter<double, ushort>(v => (ushort)v, v => v),
            new ValueConverter<double, byte>(v => (byte)v, v => v),
            new ValueConverter<double, sbyte>(v => (sbyte)v, v => v)
        ];
        foreach (var converter in converters)
        foreach (var name in Names)
        {
            var mapping = (RelationalTypeMapping)source.FindMapping(converter.ProviderClrType)!.WithComposedConverter(converter);
            var x = factory.Constant(Convert.ChangeType(2, converter.ModelClrType), mapping);
            var secondaryConverter = new ValueConverter<double, double>(v => v * 3 - 4, v => (v + 4) / 3);
            var secondaryMapping = (RelationalTypeMapping)source.FindMapping(typeof(double))!.WithComposedConverter(
                converter.ModelClrType == typeof(float)
                    ? new ValueConverter<float, double>(v => v * 3d - 4, v => (float)((v + 4) / 3)) : secondaryConverter);
            var b = factory.Constant(Convert.ChangeType(.5, converter.ModelClrType), secondaryMapping);
            var result = translator.Translate(null, Method(name, converter.ModelClrType), name is "LogBase" or "Pow" ? [x, b] : [x], logger)!;
            Assert.NotNull(result);
            Assert.Equal(converter.ModelClrType, result.Type);
            Assert.Same(converter, result.TypeMapping!.Converter);
            var carrier = Nullable.GetUnderlyingType(converter.ProviderClrType) ?? converter.ProviderClrType;
            if (carrier == typeof(decimal))
            {
                Assert.NotSame(mapping, result.TypeMapping);
                Assert.Equal(nameof(DbDataReader.GetDecimal), mapping.GetDataReaderMethod().Name);
                Assert.Equal(nameof(DbDataReader.GetValue), result.TypeMapping.GetDataReaderMethod().Name);
                Assert.Equal(mapping.StoreType, result.TypeMapping.StoreType);
            }
            else Assert.Same(mapping, result.TypeMapping);
            var functions = Functions(result).ToArray();
            Assert.All(functions, f => { Assert.True(f.IsNullable); Assert.All(f.ArgumentsPropagateNullability!, Assert.False); });
            Assert.Contains(functions.SelectMany(f => f.Arguments!), a => ReferenceEquals(a, x));
            if (name is "LogBase" or "Pow") Assert.Contains(functions.SelectMany(f => f.Arguments!), a => ReferenceEquals(a, b));
            if (name == "LogBase" && mode == OceanBaseCompatibilityMode.Oracle)
            {
                var log = Assert.Single(functions, f => f.Name == "LOG");
                Assert.Same(b, log.Arguments![0]); Assert.Same(x, log.Arguments[1]);
            }
            if (name == "Log10" && mode == OceanBaseCompatibilityMode.Oracle)
            {
                var ten = Assert.IsType<SqlConstantExpression>(Assert.Single(functions, f => f.Name == "LOG").Arguments![0]);
                Assert.Equal(10, ten.Value); Assert.Equal(typeof(int), ten.Type); Assert.Null(ten.TypeMapping!.Converter);
            }
            Assert.Equal(carrier == typeof(float), functions.Any(f => f.Name.Contains("__oceanbase_internal", StringComparison.Ordinal)));
            var sql = Render(context, mode, result);
            Assert.Equal(carrier == typeof(float), sql.Contains("CAST(", StringComparison.Ordinal));
            Assert.DoesNotContain("__oceanbase_internal", sql);
        }
    }

    private static IEnumerable<SqlFunctionExpression> Functions(SqlExpression expression)
    {
        if (expression is SqlFunctionExpression function)
        {
            yield return function;
            foreach (var child in function.Arguments!.SelectMany(Functions)) yield return child;
        }
        if (expression is SqlBinaryExpression binary)
            foreach (var child in Functions(binary.Left).Concat(Functions(binary.Right))) yield return child;
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public void Native_domain_nulls_survive_nonnullable_inputs_composition_and_both_null_semantics(OceanBaseCompatibilityMode mode, bool nulls)
    {
        using var context = Context(mode, nulls);
        IQueryable<double?>[] results = [context.Rows.Select(r => (double?)Math.Sqrt(r.Double)),
            context.Rows.Select(r => (double?)Math.Exp(r.Optional!.Value)), context.Rows.Select(r => (double?)Math.Log(r.Double)),
            context.Rows.Select(r => (double?)Math.Log(r.Double, .5)), context.Rows.Select(r => (double?)Math.Log10(r.Double)),
            context.Rows.Select(r => (double?)Math.Pow(r.Double, -2))];
        foreach (var query in results)
        {
            Assert.Contains(" IS NULL", query.Where(v => v == null).ToQueryString());
            Assert.Contains(" IS NOT NULL", query.Where(v => v != null).ToQueryString());
            Assert.Contains("COALESCE(", query.Select(v => v ?? -1).ToQueryString());
        }
        IQueryable<float?>[] singles = [context.Rows.Select(r => (float?)MathF.Sqrt(r.Single)),
            context.Rows.Select(r => (float?)MathF.Exp(r.Single)), context.Rows.Select(r => (float?)MathF.Log(r.Single)),
            context.Rows.Select(r => (float?)MathF.Log(r.Single, .5f)), context.Rows.Select(r => (float?)MathF.Log10(r.Single)),
            context.Rows.Select(r => (float?)MathF.Pow(r.Single, -2))];
        foreach (var query in singles)
        {
            Assert.Contains(" IS NULL", query.Where(v => v == null).ToQueryString());
            Assert.Contains(" IS NOT NULL", query.Where(v => v != null).ToQueryString());
            Assert.Contains("COALESCE(CAST(", query.Select(v => v ?? -1).ToQueryString());
        }
        var sql = context.Rows.Where(r => !(Math.Log(r.Optional!.Value) == 1))
            .Select(r => new { A = r.Id > 0 ? (double?)Math.Pow(r.Double, -2) : null,
                B = Math.Log(Math.Sqrt(r.Double), Math.Exp(r.Double)) }).ToQueryString();
        Assert.Contains("CASE", sql); Assert.Contains("POWER(", sql); Assert.Contains("SQRT(", sql); Assert.Contains("EXP(", sql);
        Assert.Equal(!nulls, sql.Contains("IS NULL", StringComparison.Ordinal));
        var grouped = context.Rows.GroupBy(r => MathF.Log10(r.Single))
            .Where(g => g.Count() > 1 && g.Key > 0).Select(g => new { g.Key, Count = g.Count() }).ToQueryString();
        Assert.Contains("GROUP BY", grouped); Assert.Contains("HAVING", grouped); Assert.Contains("CAST(", grouped);
        var nested = context.Rows.Where(r => MathF.Exp(MathF.Log(r.Single, .5f)) > 0).Select(r => r.Id).ToQueryString();
        Assert.Equal(2, nested.Split("CAST(", StringSplitOptions.None).Length - 1);
        Assert.Contains("EXP(CAST(", nested);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql)]
    [InlineData(OceanBaseCompatibilityMode.Oracle)]
    public void Width_normalization_uses_provider_carrier_without_copying_declaration_or_narrowing_double(OceanBaseCompatibilityMode mode)
    {
        using var context = Context(mode);
        var source = context.GetService<IRelationalTypeMappingSource>();
        var factory = context.GetService<ISqlExpressionFactory>();
        var translator = new OceanBaseMathTranslator(factory, source, mode);
        var logger = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>();
        foreach (var type in new[] { typeof(float), typeof(double) })
        foreach (var declaration in mode == OceanBaseCompatibilityMode.MySql
            ? new[] { "FLOAT", "FLOAT(0)", "FLOAT(24) UNSIGNED", "FLOAT(25)", "FLOAT(53) UNSIGNED", "DOUBLE PRECISION UNSIGNED" }
            : ["BINARY_FLOAT", "BINARY_DOUBLE"])
        foreach (var name in Names)
        {
            var mapping = source.FindMapping(type, storeTypeName: declaration)!;
            var x = factory.Constant(Convert.ChangeType(2, type), mapping);
            var b = factory.Constant(Convert.ChangeType(.5, type), source.FindMapping(type));
            var result = translator.Translate(null, Method(name, type), name is "LogBase" or "Pow" ? [x, b] : [x], logger)!;
            Assert.Same(mapping, result.TypeMapping);
            Assert.Same(mapping, x.TypeMapping);
            var sql = Render(context, mode, result);
            Assert.Equal(type == typeof(float), sql.Contains("CAST(", StringComparison.Ordinal));
            if (type == typeof(float))
            {
                Assert.Equal(1, sql.Split("CAST(", StringSplitOptions.None).Length - 1);
                Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? " AS FLOAT)" : " AS BINARY_FLOAT)", sql);
            }
            if (name == "LogBase" && mode == OceanBaseCompatibilityMode.MySql)
                Assert.Contains(type == typeof(float) ? "CAST(LN(2E0) / LN(0.5E0) AS FLOAT)" : "LN(2E0) / LN(0.5E0)", sql);
            var ordinary = new SqlUnaryExpression(ExpressionType.Convert, x, type, mapping);
            Assert.Contains(" AS " + mapping.StoreType + ")", Render(context, mode, factory.Function("USER_NUMERIC", [ordinary], true, [false], type, mapping)));
        }
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql)]
    [InlineData(OceanBaseCompatibilityMode.Oracle)]
    public void Finite_extremes_domains_and_near_one_bases_remain_unclipped_native_operands(OceanBaseCompatibilityMode mode)
    {
        using var context = Context(mode);
        var source = context.GetService<IRelationalTypeMappingSource>();
        var factory = context.GetService<ISqlExpressionFactory>();
        var translator = new OceanBaseMathTranslator(factory, source, mode);
        var logger = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>();
        foreach (var type in new[] { typeof(float), typeof(double) })
        foreach (var value in type == typeof(float)
            ? new object[] { float.MinValue, -float.Epsilon, -0f, 0f, float.Epsilon, MathF.BitDecrement(1f), 1f, MathF.BitIncrement(1f), float.MaxValue }
            : [double.MinValue, -1E100d, -double.Epsilon, -0d, 0d, double.Epsilon, Math.BitDecrement(1d), 1d, Math.BitIncrement(1d), 1E100d, double.MaxValue])
        foreach (var name in Names)
        {
            var x = factory.Constant(value, source.FindMapping(type));
            foreach (var baseValue in new[] { -.5, 0, .5, 1, type == typeof(float) ? MathF.BitIncrement(1f) : Math.BitIncrement(1d), 2 })
            {
                var b = factory.Constant(Convert.ChangeType(baseValue, type), source.FindMapping(type));
                var result = translator.Translate(null, Method(name, type), name is "LogBase" or "Pow" ? [x, b] : [x], logger)!;
                Assert.NotNull(result);
                Assert.Contains(Functions(result).SelectMany(f => f.Arguments!), a => ReferenceEquals(a, x));
                if (name is "LogBase" or "Pow")
                    Assert.Contains(Functions(result).SelectMany(f => f.Arguments!), a => ReferenceEquals(a, b));
                if (name == "Pow") Assert.DoesNotContain(Functions(result), f => f.Name == "LN");
                Assert.DoesNotContain("CASE", Render(context, mode, result));
            }
        }
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql)]
    [InlineData(OceanBaseCompatibilityMode.Oracle)]
    public void Invalid_carriers_declarations_secondary_arguments_and_unowned_decimal_readers_reject(OceanBaseCompatibilityMode mode)
    {
        using var context = Context(mode);
        var source = context.GetService<IRelationalTypeMappingSource>();
        var factory = context.GetService<ISqlExpressionFactory>();
        var translator = new OceanBaseMathTranslator(factory, source, mode);
        var logger = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>();
        var valid = factory.Constant(2d, source.FindMapping(typeof(double)));
#pragma warning disable EF1001
        var nulls = new ValueConverter<double, double>(v => v, v => v, convertsNulls: true);
#pragma warning restore EF1001
        RelationalTypeMapping[] invalid = [new DoubleTypeMapping("VARCHAR(10)"), new DoubleTypeMapping(mode == OceanBaseCompatibilityMode.MySql ? "FLOAT(54)" : "BINARY_DOUBLE UNSIGNED"),
            new DoubleTypeMapping("DOUBLE trailing"), new DecimalTypeMapping(mode == OceanBaseCompatibilityMode.MySql ? "DECIMAL" : "NUMBER"),
            (RelationalTypeMapping)source.FindMapping(typeof(string))!.WithComposedConverter(new ValueConverter<double, string>(v => v.ToString(), v => double.Parse(v))),
            (RelationalTypeMapping)new BoolTypeMapping("BOOLEAN").WithComposedConverter(new ValueConverter<double, bool>(v => v != 0, v => v ? 1 : 0)),
            (RelationalTypeMapping)source.FindMapping(typeof(double))!.WithComposedConverter(nulls)];
        foreach (var mapping in invalid)
        {
            var operand = factory.Constant(2d, mapping);
            Assert.Null(translator.Translate(null, Method("Pow", typeof(double)), [operand, valid], logger));
            // A valid physical decimal secondary mapping is allowed; its reader is not used for result decoding.
            if (mapping is not DecimalTypeMapping)
                Assert.Null(translator.Translate(null, Method("LogBase", typeof(double)), [valid, operand], logger));
        }
        var customDecimal = (RelationalTypeMapping)new DecimalTypeMapping(mode == OceanBaseCompatibilityMode.MySql ? "DECIMAL" : "NUMBER")
            .WithComposedConverter(new ValueConverter<double, decimal>(v => (decimal)v, v => (double)v));
        Assert.Null(translator.Translate(null, Method("Exp", typeof(double)), [factory.Constant(2d, customDecimal)], logger));
        foreach (var declaration in mode == OceanBaseCompatibilityMode.MySql ? new[] { "DECIMAL(66,0)", "DECIMAL trailing" }
            : ["NUMBER(39)", "NUMBER trailing"])
        {
            var badDecimal = (RelationalTypeMapping)new DecimalTypeMapping(declaration)
                .WithComposedConverter(new ValueConverter<double, decimal>(v => (decimal)v, v => (double)v));
            Assert.Null(translator.Translate(null, Method("LogBase", typeof(double)), [valid, factory.Constant(2d, badDecimal)], logger));
        }
        Assert.Null(translator.Translate(null, Method("Pow", typeof(double)), [valid], logger));
        Assert.Null(translator.Translate(null, Method("Log", typeof(double)), [valid, valid], logger));
        Assert.Null(translator.Translate(valid, Method("Sqrt", typeof(double)), [valid], logger));
        foreach (var method in new[] { typeof(Math).GetMethod("Log2", [typeof(double)])!,
            typeof(double).GetMethod("Sqrt", [typeof(double)])!, typeof(TranscendentalQueryTests).GetMethod(nameof(Sqrt))! })
            Assert.Null(translator.Translate(null, method, [valid], logger));
    }

    public static double Sqrt(double value) => value;

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql)]
    [InlineData(OceanBaseCompatibilityMode.Oracle)]
    public void Private_single_marker_validates_shape_and_preserves_ordinary_functions(OceanBaseCompatibilityMode mode)
    {
        using var context = Context(mode);
        var source = context.GetService<IRelationalTypeMappingSource>();
        var factory = context.GetService<ISqlExpressionFactory>();
        var single = source.FindMapping(typeof(float))!;
        var wide = source.FindMapping(typeof(double))!;
        var body = factory.Constant(2d, wide);
        var textModel = (RelationalTypeMapping)single.WithComposedConverter(
            new ValueConverter<string, float>(v => float.Parse(v), v => v.ToString()));
        foreach (var broken in new[] {
            factory.Function(OceanBaseMathTranslator.SingleResultMarker, [], true, [], typeof(float), single),
            factory.Function(OceanBaseMathTranslator.SingleResultMarker, [body, body], true, [false, false], typeof(float), single),
            factory.Function(OceanBaseMathTranslator.SingleResultMarker, [body], true, [false], typeof(double), wide),
            factory.Function(OceanBaseMathTranslator.SingleResultMarker, [body], true, [false], typeof(string), textModel),
            factory.Function(OceanBaseMathTranslator.SingleResultMarker, [factory.Constant(2f, single)], true, [false], typeof(float), single),
            factory.Function(OceanBaseMathTranslator.SingleResultMarker, [body], false, [false], typeof(float), single),
            factory.Function(OceanBaseMathTranslator.SingleResultMarker, [body], true, [true], typeof(float), single) })
            Assert.Throws<InvalidOperationException>(() => Render(context, mode, broken));
        var ordinary = factory.Function("USER_NUMERIC", [body], true, [false], typeof(double), wide);
        Assert.Contains("USER_NUMERIC(2", Render(context, mode, ordinary));
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, "DECIMAL(5,2) UNSIGNED")]
    [InlineData(OceanBaseCompatibilityMode.Oracle, "NUMBER")]
    [InlineData(OceanBaseCompatibilityMode.Oracle, "NUMBER(10,-2)")]
    public void Decimal_result_reader_is_opt_in_immutable_and_survives_facet_and_converter_clones(OceanBaseCompatibilityMode mode, string declaration)
    {
        using var context = Context(mode);
        var source = context.GetService<IRelationalTypeMappingSource>();
        var original = Assert.IsType<OceanBaseDecimalTypeMapping>(source.FindMapping(typeof(decimal), storeTypeName: declaration));
        var converter = new ValueConverter<double, decimal>(v => (decimal)v + 10m, v => (double)(v - 10m));
        var converted = Assert.IsType<OceanBaseDecimalTypeMapping>(original.WithComposedConverter(converter));
        var result = converted.AsQueryResult();
        Assert.NotSame(converted, result); Assert.Same(result, result.AsQueryResult());
        Assert.Same(converter, result.Converter); Assert.Equal(converted.ClrType, result.ClrType);
        Assert.Equal(converted.StoreType, result.StoreType); Assert.Equal(converted.Precision, result.Precision);
        Assert.Equal(converted.Scale, result.Scale); Assert.Equal(converted.DbType, result.DbType);
        Assert.Equal(converted.Comparer.GetType(), result.Comparer.GetType()); Assert.Same(converted.JsonValueReaderWriter, result.JsonValueReaderWriter);
        Assert.Equal(nameof(DbDataReader.GetDecimal), original.GetDataReaderMethod().Name);
        Assert.Equal(nameof(DbDataReader.GetDecimal), converted.GetDataReaderMethod().Name);
        var expression = Expression.Parameter(typeof(object));
        var read = Expression.Lambda<Func<object, decimal>>(result.CustomizeDataReaderExpression(expression), expression).Compile();
        foreach (var value in new object[] { 2m, 2d, 2f, 2, new OceanBaseDecimal(2m), .1d, 1d / 3, double.Epsilon })
            Assert.Equal(value is OceanBaseDecimal ? 2m : Convert.ToDecimal(value), read(value));
        foreach (var value in new object[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity,
            float.NaN, float.PositiveInfinity, float.NegativeInfinity, double.MaxValue,
            new OceanBaseDecimal(System.Numerics.BigInteger.One, -30), new OceanBaseDecimal(System.Numerics.BigInteger.One, 29) })
            Assert.Throws<OverflowException>(() => read(value));
        var facetClone = result.WithPrecisionAndScale(12, 4);
        Assert.Equal(nameof(DbDataReader.GetValue), facetClone.GetDataReaderMethod().Name);
        Assert.Same(converter, facetClone.Converter);
        var converterClone = original.AsQueryResult().WithComposedConverter(converter);
        Assert.Equal(nameof(DbDataReader.GetValue), ((RelationalTypeMapping)converterClone).GetDataReaderMethod().Name);
        using var command = new OceanBaseCommand();
        Assert.Equal(converted.CreateParameter(command, "x", 2d).Value, result.CreateParameter(command, "y", 2d).Value);
        Assert.Equal(converted.GenerateSqlLiteral(2d), result.GenerateSqlLiteral(2d));
    }

    private static string Render(TestContext context, OceanBaseCompatibilityMode mode, SqlExpression expression)
        => new OceanBaseQuerySqlGenerator(context.GetService<QuerySqlGeneratorDependencies>(), mode)
            .GetCommand(new ValuesExpression("v", [new RowValueExpression([expression])], ["Value"])).CommandText;

    private static TestContext Context(OceanBaseCompatibilityMode mode, bool nulls = false)
    {
        var builder = new DbContextOptionsBuilder<TestContext>().UseOceanBase("Server=localhost;Database=test", mode);
        ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(
            builder.Options.Extensions.OfType<RelationalOptionsExtension>().Single().WithUseRelationalNulls(nulls));
        return new TestContext(builder.Options);
    }

    private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
    {
        public DbSet<Row> Rows => Set<Row>();
    }

    private sealed class Row
    {
        public int Id { get; set; }
        public float Single { get; set; }
        public double Double { get; set; }
        public double? Optional { get; set; }
    }
}
