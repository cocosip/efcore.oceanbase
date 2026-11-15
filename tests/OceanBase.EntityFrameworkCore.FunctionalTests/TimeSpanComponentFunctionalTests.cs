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

public sealed class TimeSpanComponentFunctionalTests
{
    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task TimeSpan_components_preserve_signed_parts_and_materialize(
        OceanBaseCompatibilityMode mode, bool async)
    {
        var duration = -TimeSpan.FromHours(2) - TimeSpan.FromMinutes(3) - TimeSpan.FromSeconds(4)
            - TimeSpan.FromMilliseconds(500);
        using var table = new DataTable();
        table.Columns.Add("Days", typeof(int));
        table.Columns.Add("Hours", typeof(int));
        table.Columns.Add("Minutes", typeof(int));
        table.Columns.Add("Seconds", typeof(int));
        table.Columns.Add("Milliseconds", typeof(int));
        table.Columns.Add("Microseconds", typeof(int));
        table.Columns.Add("Nanoseconds", typeof(int));
        table.Rows.Add(duration.Days, duration.Hours, duration.Minutes, duration.Seconds,
            duration.Milliseconds, duration.Microseconds, duration.Nanoseconds);

        var capture = new Capture(table);
        await using var context = Create(mode, capture);
        var query = context.Rows.Select(row => new
        {
            row.Duration.Days,
            row.Duration.Hours,
            row.Duration.Minutes,
            row.Duration.Seconds,
            row.Duration.Milliseconds,
            row.Duration.Microseconds,
            row.Duration.Nanoseconds
        });
        var result = Assert.Single(async ? await query.ToListAsync() : query.ToList());

        Assert.Equal(duration.Days, result.Days);
        Assert.Equal(duration.Hours, result.Hours);
        Assert.Equal(duration.Minutes, result.Minutes);
        Assert.Equal(duration.Seconds, result.Seconds);
        Assert.Equal(duration.Milliseconds, result.Milliseconds);
        Assert.Equal(duration.Microseconds, result.Microseconds);
        Assert.Equal(duration.Nanoseconds, result.Nanoseconds);
        var sql = Assert.Single(capture.Sql);
        if (mode == OceanBaseCompatibilityMode.MySql)
        {
            Assert.Contains("CAST(`r`.`Duration` AS CHAR)", sql);
            Assert.Contains("LIKE '-%'", sql);
            Assert.Contains("MICROSECOND(", sql);
        }
        else
        {
            Assert.Contains("EXTRACT(DAY FROM", sql);
            Assert.Contains("EXTRACT(HOUR FROM", sql);
            Assert.Contains("EXTRACT(SECOND FROM", sql);
            Assert.Contains("* 1000", sql);
        }
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
    public async Task TimeSpan_components_preserve_pure_subsecond_sign(
        OceanBaseCompatibilityMode mode, bool async, int milliseconds)
    {
        var duration = TimeSpan.FromMilliseconds(milliseconds);
        using var table = new DataTable();
        table.Columns.Add("Hours", typeof(int));
        table.Columns.Add("Minutes", typeof(int));
        table.Columns.Add("Seconds", typeof(int));
        table.Columns.Add("Milliseconds", typeof(int));
        table.Rows.Add(duration.Hours, duration.Minutes, duration.Seconds, duration.Milliseconds);

        var capture = new Capture(table);
        await using var context = Create(mode, capture);
        var query = context.Rows.Select(row => new
        {
            row.Duration.Hours,
            row.Duration.Minutes,
            row.Duration.Seconds,
            row.Duration.Milliseconds
        });
        var result = Assert.Single(async ? await query.ToListAsync() : query.ToList());

        Assert.Equal(duration.Hours, result.Hours);
        Assert.Equal(duration.Minutes, result.Minutes);
        Assert.Equal(duration.Seconds, result.Seconds);
        Assert.Equal(duration.Milliseconds, result.Milliseconds);
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
            .UseOceanBase("Server=offline;Database=timespan_component", mode)
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
