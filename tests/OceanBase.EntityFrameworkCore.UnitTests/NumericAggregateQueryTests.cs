using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OceanBase.EntityFrameworkCore.Query;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests;

public sealed class NumericAggregateQueryTests
{
    [Fact]
    public void Oracle_single_literals_accept_ef_zero_but_reject_narrowing_overflow()
    {
        using var context = Context(OceanBaseCompatibilityMode.Oracle);
        var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(float))!;
        Assert.Equal("0F", mapping.GenerateSqlLiteral(0));
        Assert.Equal("1.5F", mapping.GenerateSqlLiteral(1.5f));
        Assert.Throws<ArgumentOutOfRangeException>(() => mapping.GenerateSqlLiteral(double.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => mapping.GenerateSqlLiteral(double.PositiveInfinity));
    }

    private static readonly Type[] NumericTypes = [typeof(sbyte), typeof(byte), typeof(short), typeof(ushort), typeof(int),
        typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal)];

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql)]
    [InlineData(OceanBaseCompatibilityMode.Oracle)]
    public void Resolved_service_rejects_invalid_owned_mappings_before_inherited_fallback(OceanBaseCompatibilityMode mode)
    {
        using var context = Context(mode);
        var factory = context.GetService<ISqlExpressionFactory>();
        var source = context.GetService<IRelationalTypeMappingSource>();
#pragma warning disable EF1001
        var nulls = new ValueConverter<double, double>(v => v, v => v, convertsNulls: true);
#pragma warning restore EF1001
        RelationalTypeMapping[] invalid = [new DoubleTypeMapping("VARCHAR(10)"),
            new DoubleTypeMapping(mode == OceanBaseCompatibilityMode.MySql ? "FLOAT(54)" : "BINARY_DOUBLE UNSIGNED"),
            new DoubleTypeMapping("DOUBLE trailing"),
            (RelationalTypeMapping)source.FindMapping(typeof(double))!.WithComposedConverter(nulls),
            (RelationalTypeMapping)source.FindMapping(typeof(string))!.WithComposedConverter(new ValueConverter<double, string>(v => v.ToString(), v => double.Parse(v))),
            (RelationalTypeMapping)new DecimalTypeMapping(mode == OceanBaseCompatibilityMode.MySql ? "DECIMAL" : "NUMBER")
                .WithComposedConverter(new ValueConverter<double, decimal>(v => (decimal)v, v => (double)v))];
        var service = context.GetService<IAggregateMethodCallTranslatorProvider>();
        Assert.IsType<OceanBaseAggregateMethodCallTranslatorProvider>(service);
        foreach (var mapping in invalid)
        foreach (var name in new[] { "Sum", "Average", "Min", "Max" })
        {
            var error = Assert.Throws<InvalidOperationException>(() => service.Translate(context.Model, Method(name, typeof(double), false),
                new EnumerableExpression(factory.Constant(2d, mapping)), [], context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>()));
            Assert.Contains("OceanBase", error.Message);
        }
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql)]
    [InlineData(OceanBaseCompatibilityMode.Oracle)]
    public void Plugin_priority_exact_boundaries_and_inherited_counts_survive(OceanBaseCompatibilityMode mode)
    {
        using var context = Context(mode);
        var factory = context.GetService<ISqlExpressionFactory>();
        var mappings = context.GetService<IRelationalTypeMappingSource>();
        var logger = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>();
        var operand = factory.Constant(2, mappings.FindMapping(typeof(int)));
        var enumerable = new EnumerableExpression(operand);
        var sentinel = factory.Constant(99, mappings.FindMapping(typeof(int)));
        var provider = new OceanBaseAggregateMethodCallTranslatorProvider(
            new RelationalAggregateMethodCallTranslatorProviderDependencies(factory, [new Plugin(sentinel)], mappings), mappings);
        Assert.Same(sentinel, provider.Translate(context.Model, Method("Sum", typeof(int), false), enumerable, [], logger));
        Assert.Same(sentinel, provider.Translate(context.Model, Method("Sum", typeof(int), false),
            new EnumerableExpression(factory.Constant(2, new IntTypeMapping("invalid"))), [], logger));
        var translator = new OceanBaseAggregateMethodTranslator(factory, mappings);
        Assert.Null(translator.Translate(typeof(Enumerable).GetMethods().Single(m => m.Name == "Sum" && !m.IsGenericMethod
            && m.GetParameters()[0].ParameterType == typeof(IEnumerable<int>)), enumerable, [], logger));
        Assert.Null(translator.Translate(typeof(Queryable).GetMethods().Single(m => m.Name == "Min" && m.GetParameters().Length == 2
            && m.GetParameters()[1].ParameterType.IsGenericType && m.GetParameters()[1].ParameterType.GetGenericTypeDefinition() == typeof(IComparer<>))
            .MakeGenericMethod(typeof(int)), enumerable, [], logger));
        Assert.Null(translator.Translate(QueryableMethods.MinWithoutSelector.MakeGenericMethod(typeof(string)),
            new EnumerableExpression(factory.Constant("text", mappings.FindMapping(typeof(string)))), [], logger));
        foreach (var method in new[] { QueryableMethods.CountWithoutPredicate.MakeGenericMethod(typeof(int)),
            QueryableMethods.LongCountWithoutPredicate.MakeGenericMethod(typeof(int)) })
        {
            Assert.Null(translator.Translate(method, enumerable, [], logger));
            Assert.Equal("COUNT", Assert.IsType<SqlFunctionExpression>(context.GetService<IAggregateMethodCallTranslatorProvider>()
                .Translate(context.Model, method, enumerable, [], logger)).Name);
        }
        var options = new DbContextOptionsBuilder<TestContext>().UseOceanBase("Server=localhost;Database=test", mode)
            .ReplaceService<IAggregateMethodCallTranslatorProvider, OverrideProvider>().Options;
        using var overridden = new TestContext(options, mode);
        var sql = overridden.Set<Row>().GroupBy(r => r.Id).Select(g => g.Sum(r => r.Int)).ToQueryString();
        Assert.Contains("99", sql); Assert.DoesNotContain("SUM(", sql);
    }

    private sealed class OverrideProvider(RelationalAggregateMethodCallTranslatorProviderDependencies dependencies)
        : RelationalAggregateMethodCallTranslatorProvider(dependencies)
    {
        public override SqlExpression? Translate(IModel model, MethodInfo method, EnumerableExpression source,
            IReadOnlyList<SqlExpression> arguments, IDiagnosticsLogger<DbLoggerCategory.Query> logger)
            => Dependencies.SqlExpressionFactory.Constant(99);
    }

    private sealed class Plugin(SqlExpression result) : IAggregateMethodCallTranslatorPlugin, IAggregateMethodCallTranslator
    {
        public IEnumerable<IAggregateMethodCallTranslator> Translators => [this];
        public SqlExpression? Translate(MethodInfo method, EnumerableExpression source, IReadOnlyList<SqlExpression> arguments,
            IDiagnosticsLogger<DbLoggerCategory.Query> logger) => result;
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public void Filtered_distinct_nullable_having_order_and_composed_selectors_keep_native_shapes(OceanBaseCompatibilityMode mode, bool nulls)
    {
        using var context = Context(mode, nulls);
        var query = context.Set<Row>().GroupBy(r => r.Optional).Where(g => g.Sum(r => r.Int) > 100000)
            .OrderBy(g => g.Average(r => r.Int)).Select(g => new
            {
                Sum = g.Where(r => r.Int > 0).Select(r => r.Optional).Distinct().Sum(),
                Average = g.Where(r => r.Int > 0).Select(r => r.Optional).Distinct().Average(),
                Min = g.Min(r => r.Optional), Max = g.Max(r => r.Optional),
                Default = g.Average(r => r.Optional) ?? -1,
                Composed = g.Sum(r => MathF.Sqrt(r.Single))
            });
        var sql = query.ToQueryString();
        Assert.Contains("SUM(DISTINCT (CASE", sql);
        Assert.Contains("AVG(DISTINCT (CASE", sql);
        Assert.Contains("HAVING", sql); Assert.Contains("ORDER BY", sql); Assert.Contains("GROUP BY", sql);
        Assert.Contains("SQRT(", sql);
        Assert.DoesNotContain("AVG(CAST(", sql);
        Assert.DoesNotContain("COALESCE(MIN(", sql); Assert.DoesNotContain("COALESCE(MAX(", sql);
        Assert.DoesNotContain("DECIMAL(", sql);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql)]
    [InlineData(OceanBaseCompatibilityMode.Oracle)]
    public void Promoted_average_accepts_custom_decimal_input_without_using_its_result_reader(OceanBaseCompatibilityMode mode)
    {
        using var context = Context(mode);
        var factory = context.GetService<ISqlExpressionFactory>();
        var converter = new ValueConverter<int, decimal>(v => v + 10m, v => (int)(v - 10));
        var mapping = (RelationalTypeMapping)new DecimalTypeMapping(mode == OceanBaseCompatibilityMode.MySql ? "DECIMAL" : "NUMBER")
            .WithComposedConverter(converter);
        var operand = factory.Constant(2, mapping);
        var result = Assert.IsType<SqlFunctionExpression>(context.GetService<IAggregateMethodCallTranslatorProvider>()
            .Translate(context.Model, Method("Average", typeof(int), false), new EnumerableExpression(operand), [],
                context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>()));
        Assert.Same(operand, Assert.Single(result.Arguments!));
        Assert.Equal(typeof(double), result.Type); Assert.Null(result.TypeMapping!.Converter);
        Assert.Equal(nameof(DbDataReader.GetValue), result.TypeMapping.GetDataReaderMethod().Name);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql)]
    [InlineData(OceanBaseCompatibilityMode.Oracle)]
    public void Compensated_single_sum_marker_requires_original_type_and_all_mapping_owners(OceanBaseCompatibilityMode mode)
    {
        using var context = Context(mode);
        var factory = context.GetService<ISqlExpressionFactory>();
        var mapping = (RelationalTypeMapping)context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(float))!
            .WithComposedConverter(new ValueConverter<decimal, float>(v => (float)v + 10, v => (decimal)(v - 10)));
        var sum = factory.Function("SUM", [factory.Constant(2m, mapping)], true, [false], typeof(decimal), mapping);
        var zero = factory.Constant(0, mapping);
        Assert.Equal(typeof(decimal), sum.Type); Assert.Same(mapping, sum.TypeMapping);
        Assert.Equal(0, Assert.IsType<SqlConstantExpression>(zero).Value); Assert.Same(mapping, zero.TypeMapping);
        var compensated = factory.Function("COALESCE", [sum, zero], false, [false, false], typeof(int), mapping);
        var marker = factory.Function(OceanBaseMathTranslator.SingleResultMarker, [compensated], true, [false], typeof(decimal), mapping);
        var sql = Render(context, mode, marker);
        Assert.Contains("CAST(COALESCE(SUM(", sql);
        Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "10E0" : "10F", sql);
        var unmappedZero = factory.Constant(0);
        var otherSum = factory.Function("SUM", [factory.Constant(2f)], true, [false], typeof(float), mapping);
        foreach (var body in new SqlExpression[] { factory.Constant(2),
            factory.Function("COALESCE", [sum, unmappedZero], false, [false, false], typeof(int), mapping),
            factory.Function("COALESCE", [otherSum, zero], false, [false, false], typeof(int), mapping) })
            Assert.Throws<InvalidOperationException>(() => Render(context, mode,
                factory.Function(OceanBaseMathTranslator.SingleResultMarker, [body], true, [false], typeof(decimal), mapping)));
    }

    private static string Render(TestContext context, OceanBaseCompatibilityMode mode, SqlExpression expression)
        => new OceanBaseQuerySqlGenerator(context.GetService<QuerySqlGeneratorDependencies>(), mode)
            .GetCommand(new ValuesExpression("v", [new RowValueExpression([expression])], ["Value"])).CommandText;

    public static IEnumerable<object[]> AggregateMethods()
    {
        foreach (var mode in new[] { OceanBaseCompatibilityMode.MySql, OceanBaseCompatibilityMode.Oracle })
        foreach (var numeric in NumericTypes)
        foreach (var nullable in new[] { false, true })
        foreach (var selector in new[] { false, true })
        foreach (var name in new[] { "Sum", "Average", "Min", "Max" })
        {
            if (name is "Sum" or "Average" && numeric != typeof(int) && numeric != typeof(long)
                && numeric != typeof(float) && numeric != typeof(double) && numeric != typeof(decimal)) continue;
            yield return [mode, nullable ? typeof(Nullable<>).MakeGenericType(numeric) : numeric, selector, name];
        }
    }

    private static MethodInfo Method(string name, Type type, bool selector)
        => name switch
        {
            "Sum" => selector ? QueryableMethods.GetSumWithSelector(type).MakeGenericMethod(typeof(Row)) : QueryableMethods.GetSumWithoutSelector(type),
            "Average" => selector ? QueryableMethods.GetAverageWithSelector(type).MakeGenericMethod(typeof(Row)) : QueryableMethods.GetAverageWithoutSelector(type),
            "Min" => selector ? QueryableMethods.MinWithSelector.MakeGenericMethod(typeof(Row), type) : QueryableMethods.MinWithoutSelector.MakeGenericMethod(type),
            _ => selector ? QueryableMethods.MaxWithSelector.MakeGenericMethod(typeof(Row), type) : QueryableMethods.MaxWithoutSelector.MakeGenericMethod(type)
        };

    [Theory]
    [MemberData(nameof(AggregateMethods))]
    public void Exact_numeric_overloads_keep_original_selector_and_result_reader(OceanBaseCompatibilityMode mode, Type type, bool selector, string name)
    {
        using var context = Context(mode);
        var factory = context.GetService<ISqlExpressionFactory>();
        var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(type)!;
        var model = Nullable.GetUnderlyingType(type) ?? type;
        var operand = factory.Constant(Convert.ChangeType(2, model), mapping);
        var method = Method(name, type, selector);
        var result = context.GetService<IAggregateMethodCallTranslatorProvider>().Translate(context.Model, method,
            new EnumerableExpression(operand), [], context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>())!;
        Assert.NotNull(result);
        var native = Assert.IsType<SqlFunctionExpression>(result);
        if (model == typeof(float)) native = Assert.IsType<SqlFunctionExpression>(native.Arguments![0]);
        Assert.Equal(name == "Average" ? "AVG" : name.ToUpperInvariant(), native.Name);
        Assert.Same(operand, Assert.Single(native.Arguments!));
        Assert.True(native.IsNullable);
        Assert.False(Assert.Single(native.ArgumentsPropagateNullability!));
        var promoted = name == "Average" && (model == typeof(int) || model == typeof(long));
        Assert.Equal(promoted ? typeof(double) : model, result.Type);
        Assert.Null(result.TypeMapping!.Converter);
        if (model == typeof(decimal))
        {
            Assert.NotSame(mapping, result.TypeMapping);
            Assert.Equal(nameof(DbDataReader.GetValue), result.TypeMapping.GetDataReaderMethod().Name);
            Assert.Equal(nameof(DbDataReader.GetDecimal), mapping.GetDataReaderMethod().Name);
            Assert.Equal(mapping.StoreType, result.TypeMapping.StoreType);
        }
        else if (!promoted) Assert.Same(mapping, result.TypeMapping);

        var group = Expression.Parameter(typeof(IGrouping<int, Row>), "g");
        var row = Expression.Parameter(typeof(Row), "r");
        var property = Expression.Call(typeof(EF), nameof(EF.Property), [type], row,
            Expression.Constant(model.Name + (Nullable.GetUnderlyingType(type) is null ? "Value" : "Nullable")));
        var rows = Expression.Call(QueryableMethods.AsQueryable.MakeGenericMethod(typeof(Row)), group);
        Expression call = selector ? Expression.Call(method, rows, Expression.Quote(Expression.Lambda(property, row)))
            : Expression.Call(method, Expression.Call(QueryableMethods.Select.MakeGenericMethod(typeof(Row), type), rows,
                Expression.Quote(Expression.Lambda(property, row))));
        var groups = context.Set<Row>().GroupBy(r => r.Id);
        var projection = Expression.Call(QueryableMethods.Select.MakeGenericMethod(typeof(IGrouping<int, Row>), method.ReturnType),
            groups.Expression, Expression.Quote(Expression.Lambda(call, group)));
        var sql = groups.Provider.CreateQuery(projection).ToQueryString();
        Assert.Contains(native.Name + "(", sql); Assert.Contains("GROUP BY", sql);
        if (promoted) Assert.DoesNotContain("CAST(", sql);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql)]
    [InlineData(OceanBaseCompatibilityMode.Oracle)]
    public void Mapped_carriers_keep_input_converter_and_independently_promote_integer_average(OceanBaseCompatibilityMode mode)
    {
        using var context = Context(mode);
        var mappings = context.GetService<IRelationalTypeMappingSource>();
        var factory = context.GetService<ISqlExpressionFactory>();
        ValueConverter[] converters = [
            new ValueConverter<int, float>(v => v + 10f, v => (int)(v - 10)),
            new ValueConverter<long, float?>(v => v + 10f, v => (long)(v!.Value - 10)),
            new ValueConverter<decimal, float>(v => (float)v + 10, v => (decimal)(v - 10)),
            new ValueConverter<double, float>(v => (float)v + 10, v => v - 10d),
            new ValueConverter<float, double>(v => v + 10d, v => (float)(v - 10)),
            new ValueConverter<decimal, decimal>(v => -2 * v + 10, v => (v - 10) / -2),
            new ValueConverter<double, decimal?>(v => (decimal)v + 10, v => (double)(v!.Value - 10)),
            new ValueConverter<int, long>(v => v + 10L, v => (int)(v - 10))];
        foreach (var converter in converters)
        foreach (var name in new[] { "Sum", "Average", "Min", "Max" })
        {
            var carrier = Nullable.GetUnderlyingType(converter.ProviderClrType) ?? converter.ProviderClrType;
            var declarations = carrier == typeof(float)
                ? mode == OceanBaseCompatibilityMode.MySql ? new[] { "FLOAT", "FLOAT(24) UNSIGNED", "FLOAT(53)", "DOUBLE PRECISION" }
                    : ["BINARY_FLOAT", "BINARY_DOUBLE"]
                : new[] { mappings.FindMapping(carrier)!.StoreType };
            foreach (var declaration in declarations)
            {
                var mapping = (RelationalTypeMapping)mappings.FindMapping(carrier, storeTypeName: declaration)!.WithComposedConverter(converter);
                var operand = factory.Constant(Convert.ChangeType(2, converter.ModelClrType), mapping);
                var result = context.GetService<IAggregateMethodCallTranslatorProvider>().Translate(context.Model,
                    Method(name, converter.ModelClrType, false), new EnumerableExpression(operand), [],
                    context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>())!;
                var promoted = name == "Average" && converter.ModelClrType is var model && (model == typeof(int) || model == typeof(long));
                Assert.Equal(promoted ? typeof(double) : converter.ModelClrType, result.Type);
                if (promoted) Assert.Null(result.TypeMapping!.Converter);
                else Assert.Same(converter, result.TypeMapping!.Converter);
                var outer = Assert.IsType<SqlFunctionExpression>(result);
                var normalized = !promoted && carrier == typeof(float);
                Assert.Equal(normalized, outer.Name == OceanBaseMathTranslator.SingleResultMarker);
                var native = normalized ? Assert.IsType<SqlFunctionExpression>(outer.Arguments![0]) : outer;
                Assert.Same(operand, Assert.Single(native.Arguments!));
                if (!promoted) Assert.Same(result.TypeMapping, native.TypeMapping);
            }
        }
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public void Integer_average_is_native(OceanBaseCompatibilityMode mode, bool nulls)
    {
        using var context = Context(mode, nulls);
        var average = context.Set<Row>().GroupBy(r => r.Id).Select(g => g.Average(r => r.Int)).ToQueryString();
        Assert.Contains("AVG(", average);
        Assert.DoesNotContain("CAST(", average);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public void Single_normalization_uses_fixed_result_target(OceanBaseCompatibilityMode mode, bool nulls)
    {
        using var context = Context(mode, nulls);
        var single = context.Set<Row>().GroupBy(r => r.Id).Select(g => new
        {
            Sum = g.Sum(r => r.Single), Average = g.Average(r => r.Single),
            Min = g.Min(r => r.Single), Max = g.Max(r => r.Single)
        }).ToQueryString();
        Assert.Equal(4, single.Split(mode == OceanBaseCompatibilityMode.MySql ? " AS FLOAT)" : " AS BINARY_FLOAT)").Length - 1);
        Assert.Contains("CAST(COALESCE(SUM(", single);
        Assert.DoesNotContain(" AS " + (mode == OceanBaseCompatibilityMode.MySql ? "DOUBLE" : "BINARY_DOUBLE") + ")", single);
    }

    private static TestContext Context(OceanBaseCompatibilityMode mode, bool nulls = false)
    {
        var builder = new DbContextOptionsBuilder<TestContext>().UseOceanBase("Server=localhost;Database=test", mode);
        ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(
            builder.Options.Extensions.OfType<RelationalOptionsExtension>().Single().WithUseRelationalNulls(nulls));
        return new TestContext(builder.Options, mode);
    }

    private sealed class TestContext(DbContextOptions<TestContext> options, OceanBaseCompatibilityMode mode) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<Row>().Property(r => r.Single).HasColumnType(mode == OceanBaseCompatibilityMode.MySql ? "DOUBLE" : "BINARY_DOUBLE");
            foreach (var type in NumericTypes)
            {
                builder.Entity<Row>().Property(type, type.Name + "Value");
                builder.Entity<Row>().Property(typeof(Nullable<>).MakeGenericType(type), type.Name + "Nullable");
            }
        }
    }

    private sealed class Row
    {
        public int Id { get; set; }
        public int Int { get; set; }
        public float Single { get; set; }
        public int? Optional { get; set; }
    }
}
