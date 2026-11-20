using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Xunit;

#pragma warning disable EF1001 // EF9-only collection options infrastructure.

namespace OceanBase.EntityFrameworkCore.UnitTests;

public sealed class GuidCollectionRootQueryTests
{
    private static readonly Guid Token = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql)]
    [InlineData(OceanBaseCompatibilityMode.Oracle)]
    public void Guid_collection_roots_keep_the_declared_carrier_and_empty_metadata(
        OceanBaseCompatibilityMode mode)
    {
        using var context = CreateContext(mode);
        Guid?[] values = [Token, null, Guid.Empty];
        var query = context.Rows.SelectMany(row => values.Where(value => value != row.Token));

        var sql = query.ToQueryString();

        Assert.Contains("UNION ALL", sql, StringComparison.OrdinalIgnoreCase);
        if (mode == OceanBaseCompatibilityMode.Oracle) Assert.Contains("RAW", sql, StringComparison.OrdinalIgnoreCase);
        else
        {
            Assert.Contains(Token.ToString(), sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(" AS CHAR)", sql, StringComparison.OrdinalIgnoreCase);
        }
        Assert.DoesNotContain("numeric, Boolean or text", sql, StringComparison.OrdinalIgnoreCase);

        values = [];
        sql = query.ToQueryString();
        Assert.Contains("WHERE 1 = 0", sql, StringComparison.Ordinal);
        Assert.Contains(mode == OceanBaseCompatibilityMode.Oracle ? "RAW" : "NULL AS `Value`", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, ParameterizedCollectionTranslationMode.Constantize)]
    [InlineData(OceanBaseCompatibilityMode.MySql, ParameterizedCollectionTranslationMode.Parameterize)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, ParameterizedCollectionTranslationMode.Constantize)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, ParameterizedCollectionTranslationMode.Parameterize)]
    public void Captured_guid_roots_translate_and_rebind(
        OceanBaseCompatibilityMode mode, ParameterizedCollectionTranslationMode translation)
    {
        using var context = CreateContext(mode, translation);
        Guid?[] values = [Token, null, Guid.Empty];
        var query = context.Rows.Where(row => values.Contains(row.Token));

        foreach (var input in new Guid?[][] { values, [Token], [] })
        {
            values = input;
            var sql = query.ToQueryString();
            Assert.DoesNotContain("JSON", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("IN ()", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(input.Length == 0, sql.Contains("WHERE 1 = 0", StringComparison.Ordinal)
                || sql.Contains("WHERE 0", StringComparison.Ordinal));
        }
    }

    private static TestContext CreateContext(OceanBaseCompatibilityMode mode,
        ParameterizedCollectionTranslationMode? translation = null)
    {
        var builder = new DbContextOptionsBuilder<TestContext>()
            .UseOceanBase("Server=offline;Database=guid_roots", mode)
            .EnableServiceProviderCaching(false);
        if (translation.HasValue)
        {
            var extension = builder.Options.Extensions.OfType<RelationalOptionsExtension>().Single()
                .WithParameterizedCollectionTranslationMode(translation.Value);
            ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(extension);
        }

        return new TestContext(builder.Options);
    }

    private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
    {
        public DbSet<Row> Rows => Set<Row>();
    }

    private sealed class Row
    {
        public int Id { get; set; }
        public Guid? Token { get; set; }
    }
}
