using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace OceanBase.EntityFrameworkCore.FunctionalTests;

public sealed class TimeSpanTotalFunctionalTests
{
    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task TimeSpan_total_members_materialize_fractional_results(
        OceanBaseCompatibilityMode mode, bool async)
    {
        var duration = -TimeSpan.FromHours(2) - TimeSpan.FromMinutes(3) - TimeSpan.FromSeconds(4)
            - TimeSpan.FromMilliseconds(500);
        using var table = new DataTable();
        foreach (var column in new[] { "TotalDays", "TotalHours", "TotalMinutes", "TotalSeconds",
            "TotalMilliseconds", "TotalMicroseconds", "TotalNanoseconds" })
            table.Columns.Add(column, typeof(double));
        table.Rows.Add(duration.TotalDays, duration.TotalHours, duration.TotalMinutes, duration.TotalSeconds,
            duration.TotalMilliseconds, duration.TotalMicroseconds, duration.TotalNanoseconds);

        var capture = new Capture(table);
        await using var context = Create(mode, capture);
        var query = context.Rows.Select(row => new
        {
            row.Duration.TotalDays,
            row.Duration.TotalHours,
            row.Duration.TotalMinutes,
            row.Duration.TotalSeconds,
            row.Duration.TotalMilliseconds,
            row.Duration.TotalMicroseconds,
            row.Duration.TotalNanoseconds
        });
        var result = Assert.Single(async ? await query.ToListAsync() : query.ToList());

        Assert.Equal(duration.TotalDays, result.TotalDays);
        Assert.Equal(duration.TotalHours, result.TotalHours);
        Assert.Equal(duration.TotalMinutes, result.TotalMinutes);
        Assert.Equal(duration.TotalSeconds, result.TotalSeconds);
        Assert.Equal(duration.TotalMilliseconds, result.TotalMilliseconds);
        Assert.Equal(duration.TotalMicroseconds, result.TotalMicroseconds);
        Assert.Equal(duration.TotalNanoseconds, result.TotalNanoseconds);
        var sql = Assert.Single(capture.Sql);
        Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "TIME_TO_SEC(" : "EXTRACT(DAY FROM", sql);
        Assert.Contains("1000000000", sql);
        Assert.DoesNotContain("__oceanbase_internal", sql);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false, 500)]
    [InlineData(OceanBaseCompatibilityMode.MySql, false, -500)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true, 500)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true, -500)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false, 500)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false, -500)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true, 500)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true, -500)]
    public async Task TimeSpan_total_members_preserve_pure_subsecond_sign(
        OceanBaseCompatibilityMode mode, bool async, int milliseconds)
    {
        var duration = TimeSpan.FromMilliseconds(milliseconds);
        using var table = new DataTable();
        foreach (var column in new[] { "TotalSeconds", "TotalMilliseconds", "TotalMicroseconds", "TotalNanoseconds" })
            table.Columns.Add(column, typeof(double));
        table.Rows.Add(duration.TotalSeconds, duration.TotalMilliseconds, duration.TotalMicroseconds,
            duration.TotalNanoseconds);

        var capture = new Capture(table);
        await using var context = Create(mode, capture);
        var query = context.Rows.Select(row => new
        {
            row.Duration.TotalSeconds,
            row.Duration.TotalMilliseconds,
            row.Duration.TotalMicroseconds,
            row.Duration.TotalNanoseconds
        });
        var result = Assert.Single(async ? await query.ToListAsync() : query.ToList());

        Assert.Equal(duration.TotalSeconds, result.TotalSeconds);
        Assert.Equal(duration.TotalMilliseconds, result.TotalMilliseconds);
        Assert.Equal(duration.TotalMicroseconds, result.TotalMicroseconds);
        Assert.Equal(duration.TotalNanoseconds, result.TotalNanoseconds);
        var sql = Assert.Single(capture.Sql);
        if (mode == OceanBaseCompatibilityMode.MySql)
        {
            Assert.Contains("CAST(`r`.`Duration` AS CHAR)", sql);
            Assert.Contains("LIKE '-%'", sql);
        }
        else
            Assert.Contains("EXTRACT(SECOND FROM", sql);
    }

    private static TestContext Create(OceanBaseCompatibilityMode mode, Capture capture)
        => new(new DbContextOptionsBuilder<TestContext>()
            .UseOceanBase("Server=offline;Database=timespan_total", mode)
            .EnableServiceProviderCaching(false)
            .AddInterceptors(new NoNetwork(), capture).Options);

    private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
    {
        public DbSet<Row> Rows => Set<Row>();
    }

    private sealed class Row
    {
        public int Id { get; set; }
        public TimeSpan Duration { get; set; }
    }

    private sealed class NoNetwork : DbConnectionInterceptor
    {
        public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData,
            InterceptionResult result) => InterceptionResult.Suppress();

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,
            ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(InterceptionResult.Suppress());
    }

    private sealed class Capture(DataTable table) : DbCommandInterceptor
    {
        public List<string> Sql { get; } = [];

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Sql.Add(command.CommandText);
            return InterceptionResult<DbDataReader>.SuppressWithResult(table.CreateDataReader());
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(ReaderExecuting(command, eventData, result));
    }
}
