using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests;

public sealed class TemporalCollectionRootQueryTests
{
    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, "DATETIME")]
    [InlineData(OceanBaseCompatibilityMode.Oracle, "TIMESTAMP")]
    public void DateTime_collection_roots_use_the_temporal_mapping(OceanBaseCompatibilityMode mode, string castType)
    {
        using var context = CreateContext(mode);
        DateTime?[] values = [new DateTime(2026, 11, 12, 1, 2, 3, 400, DateTimeKind.Unspecified), null];
        var query = context.Rows.SelectMany(row => values.Where(value => value != row.OccurredAt));

        var sql = query.ToQueryString();

        Assert.Contains("CAST(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(castType, sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("numeric, Boolean or text", sql, StringComparison.OrdinalIgnoreCase);

        values = [];
        sql = query.ToQueryString();
        Assert.Contains("WHERE 1 = 0", sql, StringComparison.Ordinal);
        Assert.Contains(castType, sql, StringComparison.OrdinalIgnoreCase);
    }

    private static TestContext CreateContext(OceanBaseCompatibilityMode mode)
        => new(new DbContextOptionsBuilder<TestContext>()
            .UseOceanBase("Server=localhost;Database=temporal_roots", mode)
            .Options);

    private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
    {
        public DbSet<Row> Rows => Set<Row>();
    }

    private sealed class Row
    {
        public int Id { get; set; }
        public DateTime? OccurredAt { get; set; }
    }
}
