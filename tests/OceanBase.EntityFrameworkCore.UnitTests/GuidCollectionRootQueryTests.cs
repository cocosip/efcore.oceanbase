using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests;

public sealed class GuidCollectionRootQueryTests
{
    private static readonly Guid Token = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, "CHAR")]
    [InlineData(OceanBaseCompatibilityMode.Oracle, "RAW")]
    public void Guid_collection_roots_keep_the_declared_carrier_and_empty_metadata(
        OceanBaseCompatibilityMode mode, string castType)
    {
        using var context = CreateContext(mode);
        Guid?[] values = [Token, null, Guid.Empty];
        var query = context.Rows.SelectMany(row => values.Where(value => value != row.Token));

        var sql = query.ToQueryString();

        Assert.Contains("UNION ALL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(castType, sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("numeric, Boolean or text", sql, StringComparison.OrdinalIgnoreCase);

        values = [];
        sql = query.ToQueryString();
        Assert.Contains("WHERE 1 = 0", sql, StringComparison.Ordinal);
        Assert.Contains(castType, sql, StringComparison.OrdinalIgnoreCase);
    }

    private static TestContext CreateContext(OceanBaseCompatibilityMode mode)
        => new(new DbContextOptionsBuilder<TestContext>()
            .UseOceanBase("Server=offline;Database=guid_roots", mode)
            .EnableServiceProviderCaching(false).Options);

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
