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

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, "DATETIME")]
    [InlineData(OceanBaseCompatibilityMode.Oracle, "TIMESTAMP")]
    public void DateTimeOffset_collection_roots_use_the_temporal_mapping(
        OceanBaseCompatibilityMode mode, string castType)
    {
        using var context = CreateContext(mode);
        DateTimeOffset?[] values = [new DateTimeOffset(2026, 11, 12, 1, 2, 3, TimeSpan.FromHours(8)), null];
        var query = context.Rows.SelectMany(row => values.Where(value => value != row.OccurredAtOffset));

        var sql = query.ToQueryString();

        Assert.Contains("CAST(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(castType, sql, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql)]
    [InlineData(OceanBaseCompatibilityMode.Oracle)]
    public void DateOnly_collection_roots_use_the_temporal_mapping(OceanBaseCompatibilityMode mode)
    {
        using var context = CreateContext(mode);
        DateOnly?[] values = [new DateOnly(2026, 11, 12), null];
        var query = context.Rows.SelectMany(row => values.Where(value => value != row.OccurredOn));

        var sql = query.ToQueryString();

        Assert.Contains("CAST(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DATE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MySql_TimeOnly_collection_roots_use_the_temporal_mapping()
    {
        using var context = CreateContext(OceanBaseCompatibilityMode.MySql);
        TimeOnly?[] values = [new TimeOnly(1, 2, 3), null];
        var query = context.Rows.SelectMany(row => values.Where(value => value != row.OccurredTime));

        var sql = query.ToQueryString();

        Assert.Contains("CAST(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TIME", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MySql_TimeSpan_collection_roots_use_the_temporal_mapping()
    {
        using var context = CreateContext(OceanBaseCompatibilityMode.MySql);
        TimeSpan?[] values = [new TimeSpan(1, 2, 3), null];
        var query = context.Rows.SelectMany(row => values.Where(value => value != row.Duration));

        var sql = query.ToQueryString();

        Assert.Contains("CAST(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TIME", sql, StringComparison.OrdinalIgnoreCase);
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
        public DateTimeOffset? OccurredAtOffset { get; set; }
        public DateOnly? OccurredOn { get; set; }
        public TimeOnly? OccurredTime { get; set; }
        public TimeSpan? Duration { get; set; }
    }
}
