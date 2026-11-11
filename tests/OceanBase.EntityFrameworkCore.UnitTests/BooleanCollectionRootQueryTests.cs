using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OceanBase.EntityFrameworkCore.Query;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests;

public sealed class BooleanCollectionRootQueryTests
{
    public static IEnumerable<object[]> Modes()
    {
        foreach (var mode in new[] { OceanBaseCompatibilityMode.MySql, OceanBaseCompatibilityMode.Oracle })
        foreach (var relationalNulls in new[] { false, true })
        foreach (var translation in new ParameterTranslationMode?[] { null, ParameterTranslationMode.Constant,
                     ParameterTranslationMode.MultipleParameters })
            yield return new object[] { mode, relationalNulls, translation! };
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Captured_boolean_roots_keep_current_values_duplicates_nulls_and_typed_empty_rows(
        OceanBaseCompatibilityMode mode, bool relationalNulls, ParameterTranslationMode? translation)
    {
        using var context = Create(mode, relationalNulls, translation);
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
        OceanBaseCompatibilityMode mode, bool relationalNulls, ParameterTranslationMode? translation)
    {
        using var context = Create(mode, relationalNulls, translation);
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
        using var context = Create(mode, false, null);
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
            new RelationalParameterBasedSqlProcessorParameters(false, ParameterTranslationMode.MultipleParameters),
            new ParametersCacheDecorator(values), source, mode);
        var message = Assert.Throws<NotSupportedException>(() => visitor.Visit(
            new ValuesExpression("v", parameter, new[] { "Value" })));
        Assert.Contains("Boolean", message.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static TestContext Create(OceanBaseCompatibilityMode mode, bool relationalNulls,
        ParameterTranslationMode? translation)
    {
        var builder = new DbContextOptionsBuilder<TestContext>()
            .UseOceanBase("Server=offline;Database=boolean_roots", mode)
            .EnableServiceProviderCaching(false);
        var extension = builder.Options.Extensions.OfType<RelationalOptionsExtension>().Single()
            .WithUseRelationalNulls(relationalNulls);
        if (translation.HasValue) extension = extension.WithUseParameterizedCollectionMode(translation.Value);
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
