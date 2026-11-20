using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OceanBase.EntityFrameworkCore.Query;
using Xunit;

#pragma warning disable EF1001 // EF9-only collection options infrastructure.

namespace OceanBase.EntityFrameworkCore.UnitTests;

public sealed class BooleanCollectionRootQueryTests
{
    public static IEnumerable<object[]> Modes()
    {
        foreach (var mode in new[] { OceanBaseCompatibilityMode.MySql, OceanBaseCompatibilityMode.Oracle })
        foreach (var relationalNulls in new[] { false, true })
            yield return new object[] { mode, relationalNulls };
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Captured_boolean_roots_keep_current_values_duplicates_nulls_and_typed_empty_rows(
        OceanBaseCompatibilityMode mode, bool relationalNulls)
    {
        using var context = Create(mode, relationalNulls);
        bool?[] values = [];
        var query = context.Rows.SelectMany(row => values.Where(value => value != row.Optional));
        foreach (var input in new bool?[][] { [], [true, null, false, true], [null, null], [false], [] })
        {
            values = input;
            var sql = query.ToQueryString();
            Assert.Equal(input.Length == 0, sql.Contains("WHERE 1 = 0", StringComparison.Ordinal));
            Assert.Equal(Math.Max(0, input.Length - 1), sql.Split("UNION ALL SELECT").Length - 1);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? " AS SIGNED)" : " AS NUMBER)", sql);
            Assert.DoesNotContain("UNION ALL VALUES", sql);
            Assert.DoesNotContain("JSON", sql, StringComparison.OrdinalIgnoreCase);
            if (mode == OceanBaseCompatibilityMode.Oracle)
                Assert.True(sql.Split("FROM DUAL").Length - 1 >= Math.Max(1, input.Length), sql);
        }
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Inline_boolean_roots_preserve_the_element_type_through_empty_pruning(
        OceanBaseCompatibilityMode mode, bool relationalNulls)
    {
        using var context = Create(mode, relationalNulls);
        var emptyQueries = new IQueryable[]
        {
            context.Rows.Select(row => new bool[] { }.Count(value => row.Id > 0)),
            context.Rows.Select(row => new bool?[] { }.Count(value => row.Id > 0))
        };
        foreach (var query in emptyQueries)
        {
            var sql = query.ToQueryString();
            Assert.True(sql.Contains("WHERE 1 = 0", StringComparison.Ordinal), sql);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "CAST(NULL AS SIGNED)" : "CAST(NULL AS NUMBER)", sql);
        }
        var nonempty = context.Rows.SelectMany(row => new bool[] { true, false, true }.Where(value => value == row.Value))
            .ToQueryString();
        Assert.Contains("UNION ALL SELECT", nonempty);
        Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? " AS SIGNED)" : " AS NUMBER)", nonempty);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql)]
    [InlineData(OceanBaseCompatibilityMode.Oracle)]
    public void Null_converting_boolean_roots_remain_rejected(OceanBaseCompatibilityMode mode)
    {
        using var context = Create(mode, false);
        var source = context.GetService<IRelationalTypeMappingSource>();
        var mapping = (RelationalTypeMapping)source.FindMapping(typeof(int))!.WithComposedConverter(
            new ValueConverter<bool, int>(
                value => value ? 1 : 0,
                value => value != 0,
                convertsNulls: true));
        var parameter = new SqlParameterExpression("values", typeof(bool[]),
            source.FindMapping(typeof(bool[]), context.Model, mapping));
        var values = new Dictionary<string, object?> { ["values"] = new bool[] { true } };
        var visitor = new OceanBaseCollectionParameterExpandingVisitor(context.GetService<ISqlExpressionFactory>(),
            values, () => { }, source, mode);
        var message = Assert.Throws<NotSupportedException>(() => visitor.Visit(
            new TableValuedFunctionExpression("v", "OceanBaseCollection", [parameter])
                .AddAnnotation(OceanBaseQueryableMethodTranslatingExpressionVisitorFactory.ParameterCollectionAnnotation, new[] { "Value" })));
        Assert.Contains("Boolean", message.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql)]
    [InlineData(OceanBaseCompatibilityMode.Oracle)]
    public void Captured_boolean_roots_rebind_current_values_and_empty_metadata(
        OceanBaseCompatibilityMode mode)
    {
        using var context = Create(mode, false);
        bool[] values = [true, false, true];
        var query = context.Rows.Where(row => values.Contains(row.Value));

        foreach (var input in new[] { values, new[] { false }, Array.Empty<bool>() })
        {
            values = input;
            var sql = query.ToQueryString();
            Assert.DoesNotContain("JSON", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(input.Length == 0, sql.Contains("WHERE 1 = 0", StringComparison.Ordinal)
                || sql.Contains("WHERE 0", StringComparison.Ordinal));
        }

        bool?[] nullable = [true, null, false];
        var nullableQuery = context.Rows.Where(row => nullable.Contains(row.Optional));
        foreach (var input in new bool?[][] { nullable, [null], [] })
        {
            nullable = input;
            var sql = nullableQuery.ToQueryString();
            Assert.DoesNotContain("JSON", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(input.Length == 0, sql.Contains("WHERE 1 = 0", StringComparison.Ordinal)
                || sql.Contains("WHERE 0", StringComparison.Ordinal));
        }

        List<bool?> list = [true, null];
        var listQuery = context.Rows.Where(row => list.Contains(row.Optional));
        list = [];
        var listSql = listQuery.ToQueryString();
        Assert.True(listSql.Contains("WHERE 1 = 0", StringComparison.Ordinal)
            || listSql.Contains("WHERE 0", StringComparison.Ordinal), listSql);

    }

    private static TestContext Create(OceanBaseCompatibilityMode mode, bool relationalNulls)
    {
        var builder = new DbContextOptionsBuilder<TestContext>()
            .UseOceanBase("Server=offline;Database=boolean_roots", mode)
            .EnableServiceProviderCaching(false);
        var extension = builder.Options.Extensions.OfType<RelationalOptionsExtension>().Single()
            .WithUseRelationalNulls(relationalNulls);
        ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(extension);
        return new TestContext(builder.Options);
    }

    private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
    {
        public DbSet<Row> Rows => Set<Row>();
    }

    private sealed class Row
    {
        public int Id { get; set; }
        public bool Value { get; set; }
        public bool? Optional { get; set; }
    }
}
