using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
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
    public void Oracle_TimeOnly_collection_roots_use_the_interval_mapping()
    {
        using var context = CreateContext(OceanBaseCompatibilityMode.Oracle);
        TimeOnly?[] values = [new TimeOnly(1, 2, 3), null];
        var query = context.Rows.SelectMany(row => values.Where(value => value != row.OccurredTime));

        var sql = query.ToQueryString();

        Assert.Contains("CAST(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("INTERVAL DAY", sql, StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public void Oracle_TimeSpan_collection_roots_use_the_interval_mapping()
    {
        using var context = CreateContext(OceanBaseCompatibilityMode.Oracle);
        TimeSpan?[] values = [new TimeSpan(1, 2, 3), null];
        var query = context.Rows.SelectMany(row => values.Where(value => value != row.Duration));

        var sql = query.ToQueryString();

        Assert.Contains("CAST(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("INTERVAL DAY", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Oracle_interval_json_mapping_preserves_duration()
    {
        using var context = CreateContext(OceanBaseCompatibilityMode.Oracle);
        var mapping = context.GetService<IRelationalTypeMappingSource>()
            .FindMapping(typeof(TimeSpan), "INTERVAL DAY(9) TO SECOND(6)")!;
        foreach (var value in new[] { new TimeSpan(1, 2, 3, 4, 500), -new TimeSpan(1, 2, 3, 4, 500) })
        {
            var json = mapping.JsonValueReaderWriter!.ToJsonString(value);

            Assert.Equal(value, mapping.JsonValueReaderWriter.FromJsonString(json));
        }
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, ParameterTranslationMode.Constant)]
    [InlineData(OceanBaseCompatibilityMode.MySql, ParameterTranslationMode.MultipleParameters)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, ParameterTranslationMode.Constant)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, ParameterTranslationMode.MultipleParameters)]
    public void Explicit_temporal_parameter_roots_rebind_and_preserve_empty_metadata(
        OceanBaseCompatibilityMode mode, ParameterTranslationMode translation)
    {
        using var context = CreateContext(mode, translation);
        DateTime?[] dateTimes = [new DateTime(2026, 11, 12, 1, 2, 3), null];
        DateTimeOffset?[] offsets = [new DateTimeOffset(2026, 11, 12, 1, 2, 3, TimeSpan.FromHours(8)), null];
        DateOnly?[] dates = [new DateOnly(2026, 11, 12), null];
        TimeOnly?[] times = [new TimeOnly(1, 2, 3), null];
        List<TimeSpan?> durations = [new TimeSpan(1, 2, 3), null];

        var dateTimeQuery = context.Rows.Where(row => EF.Parameter(dateTimes).Contains(row.OccurredAt));
        var offsetQuery = context.Rows.Where(row => EF.Parameter(offsets).Contains(row.OccurredAtOffset));
        var dateQuery = context.Rows.Where(row => EF.Parameter(dates).Contains(row.OccurredOn));
        var timeQuery = context.Rows.Where(row => EF.Parameter(times).Contains(row.OccurredTime));
        var durationQuery = context.Rows.Where(row => EF.Parameter(durations).Contains(row.Duration));

        AssertTemporal(dateTimeQuery, () => dateTimes = []);
        AssertTemporal(offsetQuery, () => offsets = []);
        AssertTemporal(dateQuery, () => dates = []);
        AssertTemporal(timeQuery, () => times = []);
        AssertTemporal(durationQuery, () => durations = []);

        DateTime?[] unsupported = [new DateTime(2026, 11, 12)];
        using var unconfigured = CreateContext(mode, null);
        Assert.Throws<InvalidOperationException>(() => unconfigured.Rows
            .Where(row => EF.Parameter(unsupported).Contains(row.OccurredAt)).ToQueryString());
        using var singleParameter = CreateContext(mode, ParameterTranslationMode.Parameter);
        Assert.Throws<InvalidOperationException>(() => singleParameter.Rows
            .Where(row => EF.Parameter(unsupported).Contains(row.OccurredAt)).ToQueryString());
    }

    private static void AssertTemporal(IQueryable query, Action clear)
    {
        var sql = query.ToQueryString();
        Assert.DoesNotContain("JSON", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.True(sql.Contains(" IN (", StringComparison.Ordinal)
            || sql.Contains(" = ", StringComparison.Ordinal)
            || sql.Contains(" IS NULL", StringComparison.Ordinal));
        clear();
        sql = query.ToQueryString();
        Assert.True(sql.Contains("WHERE 1 = 0", StringComparison.Ordinal)
            || sql.Contains("WHERE 0", StringComparison.Ordinal));
    }

    private static TestContext CreateContext(OceanBaseCompatibilityMode mode, ParameterTranslationMode? translation = null)
    {
        var builder = new DbContextOptionsBuilder<TestContext>()
            .UseOceanBase("Server=localhost;Database=temporal_roots", mode);
        if (translation.HasValue)
        {
            var extension = builder.Options.Extensions.OfType<RelationalOptionsExtension>().Single()
                .WithUseParameterizedCollectionMode(translation.Value);
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
        public DateTime? OccurredAt { get; set; }
        public DateTimeOffset? OccurredAtOffset { get; set; }
        public DateOnly? OccurredOn { get; set; }
        public TimeOnly? OccurredTime { get; set; }
        public TimeSpan? Duration { get; set; }
    }
}
