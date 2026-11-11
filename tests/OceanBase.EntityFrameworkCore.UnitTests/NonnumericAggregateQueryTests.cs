using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OceanBase.EntityFrameworkCore.Query;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests;

public sealed class NonnumericAggregateQueryTests
{
    private static readonly Type[] Types = [typeof(string), typeof(DateTime), typeof(DateTime?), typeof(DateTimeOffset),
        typeof(DateTimeOffset?), typeof(DateOnly), typeof(DateOnly?), typeof(TimeOnly), typeof(TimeOnly?), typeof(TimeSpan), typeof(TimeSpan?)];

    public static IEnumerable<object[]> Methods()
    {
        foreach (var mode in new[] { OceanBaseCompatibilityMode.MySql, OceanBaseCompatibilityMode.Oracle })
        foreach (var type in Types)
        foreach (var selector in new[] { false, true })
        foreach (var minimum in new[] { false, true })
            yield return [mode, type, selector, minimum];
    }

    [Theory]
    [MemberData(nameof(Methods))]
    public void Native_extrema_retain_mapping_reader_and_filtered_distinct_shape(
        OceanBaseCompatibilityMode mode, Type type, bool selector, bool minimum)
    {
        using var context = Create(mode);
        var mappings = context.GetService<IRelationalTypeMappingSource>();
        var factory = context.GetService<ISqlExpressionFactory>();
        var mapping = mappings.FindMapping(type)!;
        var operand = new ColumnExpression("Value", "r", Nullable.GetUnderlyingType(type) ?? type, mapping, true);
        var integer = mappings.FindMapping(typeof(int));
        var predicate = factory.GreaterThan(new ColumnExpression("Id", "r", typeof(int), integer, false), factory.Constant(2, integer));
        var definition = minimum ? selector ? QueryableMethods.MinWithSelector : QueryableMethods.MinWithoutSelector
            : selector ? QueryableMethods.MaxWithSelector : QueryableMethods.MaxWithoutSelector;
        var method = definition.MakeGenericMethod(selector ? [typeof(Row), type] : [type]);
        var result = Assert.IsType<SqlFunctionExpression>(context.GetService<IAggregateMethodCallTranslatorProvider>()
            .Translate(context.Model, method, new EnumerableExpression(operand).ApplyPredicate(predicate).SetDistinct(true), [],
                context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>()));
        Assert.Equal(minimum ? "MIN" : "MAX", result.Name);
        Assert.Same(mapping, result.TypeMapping);
        Assert.Same(mapping.Converter, result.TypeMapping!.Converter);
        Assert.Equal(mapping.GetDataReaderMethod(), result.TypeMapping.GetDataReaderMethod());
        Assert.Equal(Nullable.GetUnderlyingType(type) ?? type, result.Type);
        Assert.True(result.IsNullable);
        Assert.False(Assert.Single(result.ArgumentsPropagateNullability!));
        var filtered = Assert.IsType<CaseExpression>(Assert.IsType<DistinctExpression>(Assert.Single(result.Arguments!)).Operand);
        Assert.Same(operand, Assert.Single(filtered.WhenClauses).Result);
        Assert.Null(filtered.ElseResult);

        var group = Expression.Parameter(typeof(IGrouping<int, Row>), "g");
        var row = Expression.Parameter(typeof(Row), "row");
        var property = Expression.Call(typeof(EF), nameof(EF.Property), [type], row, Expression.Constant(Name(type)));
        var rows = Expression.Call(QueryableMethods.AsQueryable.MakeGenericMethod(typeof(Row)), group);
        var selection = Expression.Quote(Expression.Lambda(property, row));
        var call = selector ? Expression.Call(method, rows, selection)
            : Expression.Call(method, Expression.Call(QueryableMethods.Select.MakeGenericMethod(typeof(Row), type), rows, selection));
        var groups = context.Set<Row>().GroupBy(item => item.Key);
        var projection = Expression.Call(QueryableMethods.Select.MakeGenericMethod(typeof(IGrouping<int, Row>), type),
            groups.Expression, Expression.Quote(Expression.Lambda(call, group)));
        var sql = groups.Provider.CreateQuery(projection).ToQueryString();
        Assert.Contains(result.Name + "(", sql);
        Assert.Contains("GROUP BY", sql);
        Assert.DoesNotContain("COALESCE", sql);
        Assert.DoesNotContain("CAST(", sql);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql)]
    [InlineData(OceanBaseCompatibilityMode.Oracle)]
    public void Owned_nonnumeric_extrema_reject_invalid_mappings_before_inherited_fallback(OceanBaseCompatibilityMode mode)
    {
        using var context = Create(mode);
        var mappings = context.GetService<IRelationalTypeMappingSource>();
#pragma warning disable EF1001
        var nullText = new ValueConverter<string, string>(value => value ?? "missing", value => value, convertsNulls: true);
        var nullDate = new ValueConverter<DateTime, DateTime>(value => value, value => value, convertsNulls: true);
#pragma warning restore EF1001
        var invalid = new List<(Type Type, RelationalTypeMapping Mapping)>
        {
            (typeof(string), (RelationalTypeMapping)mappings.FindMapping(typeof(string))!.WithComposedConverter(nullText)),
            (typeof(DateTime), (RelationalTypeMapping)mappings.FindMapping(typeof(DateTime))!.WithComposedConverter(nullDate)),
            (typeof(string), (RelationalTypeMapping)mappings.FindMapping(typeof(int))!.WithComposedConverter(
                new ValueConverter<string, int>(value => int.Parse(value), value => value.ToString()))),
            (typeof(DateTime), (RelationalTypeMapping)mappings.FindMapping(typeof(long))!.WithComposedConverter(
                new ValueConverter<DateTime, long>(value => value.Ticks, value => new DateTime(value)))),
            (typeof(string), new StringTypeMapping("unowned", System.Data.DbType.String)),
            (typeof(DateTime), new DateTimeTypeMapping("unowned"))
        };
        if (mode == OceanBaseCompatibilityMode.Oracle)
            foreach (var store in new[] { "CLOB", "NCLOB" })
                invalid.Add((typeof(string), mappings.FindMapping(typeof(string), storeTypeName: store)!));
        var service = context.GetService<IAggregateMethodCallTranslatorProvider>();
        foreach (var item in invalid)
        foreach (var method in new[] { QueryableMethods.MinWithoutSelector, QueryableMethods.MaxWithoutSelector })
            Assert.Contains("OceanBase", Assert.Throws<InvalidOperationException>(() => service.Translate(context.Model,
                method.MakeGenericMethod(item.Type), new EnumerableExpression(new ColumnExpression("Value", "r", item.Type, item.Mapping, true)),
                [], context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>())).Message);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public void Filtered_group_extrema_compose_with_having_order_and_contextual_converters(OceanBaseCompatibilityMode mode, bool nulls)
    {
        using var context = Create(mode, nulls);
        var threshold = 2;
        var query = context.Set<Row>().GroupBy(row => row.Key).Where(group => group.Min(row => row.Stamp) > new DateTime(2000, 1, 1))
            .OrderBy(group => group.Max(row => row.Text)).Select(group => new
            {
                Text = group.Where(row => row.Id > threshold).Select(row => row.Text).Distinct().Min(),
                Stamp = group.Where(row => row.Id > threshold).Select(row => row.Stamp).Distinct().Max()
            });
        var sql = query.ToQueryString();
        Assert.Contains("MIN(CASE", sql);
        Assert.Contains("MAX(CASE", sql);
        Assert.Contains("HAVING", sql);
        Assert.Contains("ORDER BY", sql);
        Assert.DoesNotContain("COALESCE", sql);
        Assert.DoesNotContain("CAST(", sql);
    }

    private static string Name(Type type) => (Nullable.GetUnderlyingType(type) ?? type).Name + (Nullable.GetUnderlyingType(type) is null ? "Value" : "Optional");

    private static TestContext Create(OceanBaseCompatibilityMode mode, bool nulls = false)
    {
        var options = new DbContextOptionsBuilder<TestContext>().UseOceanBase("Server=offline;Database=extrema", mode)
            .EnableServiceProviderCaching(false);
        ((IDbContextOptionsBuilderInfrastructure)options).AddOrUpdateExtension(
            options.Options.Extensions.OfType<RelationalOptionsExtension>().Single().WithUseRelationalNulls(nulls));
        return new TestContext(options.Options);
    }

    private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            foreach (var type in Types) builder.Entity<Row>().Property(type, Name(type));
            builder.Entity<Row>().Property(row => row.Text).HasMaxLength(8).HasConversion(value => "x" + value, value => value.Substring(1));
            builder.Entity<Row>().Property(row => row.Stamp).HasConversion(new ValueConverter<DateTime?, DateTime?>(
                value => value.HasValue ? value.Value.AddDays(1) : null, value => value.HasValue ? value.Value.AddDays(-1) : null));
        }
    }

    private sealed class Row
    {
        public int Id { get; set; }
        public int Key { get; set; }
        public string? Text { get; set; }
        public DateTime? Stamp { get; set; }
    }
}
