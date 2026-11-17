using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.FunctionalTests;

public sealed class DateTimeMemberFunctionalTests
{
    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task DateTime_Date_translates_and_materializes(OceanBaseCompatibilityMode mode, bool async)
    {
        var target = new DateTime(2026, 11, 12);
        using var table = new DataTable();
        table.Columns.Add("Value", typeof(object));
        table.Rows.Add(new DateTime(2026, 11, 12));
        var capture = new Capture(table);
        await using var context = Create(mode, capture);
        var query = context.Rows
            .Where(row => row.OptionalStamp!.Value.Date == target)
            .Select(row => row.Stamp.Date);

        var result = async ? await query.ToListAsync() : query.ToList();

        Assert.Equal(new DateTime(2026, 11, 12), Assert.Single(result));
        var sql = Assert.Single(capture.Sql);
        Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "DATE(`r`.`OptionalStamp`)" : "TRUNC(\"r\".\"OptionalStamp\")", sql);
        Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "DATE(`r`.`Stamp`)" : "TRUNC(\"r\".\"Stamp\")", sql);
        Assert.Contains(capture.Inputs, parameter => Equals(parameter.Value, target));
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task DateTime_clock_members_materialize_server_values(OceanBaseCompatibilityMode mode, bool async)
    {
        using var table = new DataTable();
        table.Columns.Add("Now", typeof(object));
        table.Columns.Add("UtcNow", typeof(object));
        table.Columns.Add("Today", typeof(object));
        table.Rows.Add(new DateTime(2026, 11, 16, 10, 20, 30), new DateTime(2026, 11, 16, 2, 20, 30),
            new DateTime(2026, 11, 16));
        var capture = new Capture(table);
        await using var context = Create(mode, capture);
        var query = context.Rows.Where(row => row.Zoned.UtcDateTime > new DateTime(2026, 1, 1)).Select(row => new
        {
            Now = DateTime.Now,
            UtcNow = DateTime.UtcNow,
            Today = DateTime.Today
        });

        var result = Assert.Single(async ? await query.ToListAsync() : query.ToList());
        Assert.Equal(new DateTime(2026, 11, 16, 10, 20, 30), result.Now);
        Assert.Equal(new DateTime(2026, 11, 16, 2, 20, 30), result.UtcNow);
        Assert.Equal(new DateTime(2026, 11, 16), result.Today);
        var sql = Assert.Single(capture.Sql);
        Assert.DoesNotContain("__oceanbase_internal", sql);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task DateTimeOffset_utc_now_materializes_server_value(OceanBaseCompatibilityMode mode, bool async)
    {
        using var table = new DataTable();
        table.Columns.Add("Value", typeof(object));
        table.Rows.Add(new DateTime(2026, 11, 16, 2, 20, 30));
        var capture = new Capture(table);
        await using var context = Create(mode, capture);
        var query = context.Rows.Where(row => DateTimeOffset.UtcNow > row.Zoned)
            .Select(_ => DateTimeOffset.UtcNow);

        var result = Assert.Single(async ? await query.ToListAsync() : query.ToList());
        Assert.Equal(new DateTimeOffset(2026, 11, 16, 2, 20, 30, TimeSpan.Zero), result);
        var sql = Assert.Single(capture.Sql);
        Assert.DoesNotContain("__oceanbase_internal", sql);
        Assert.Contains(mode == OceanBaseCompatibilityMode.MySql
            ? "UTC_TIMESTAMP(6)"
            : "CAST(SYS_EXTRACT_UTC(SYSTIMESTAMP) AS TIMESTAMP(7))", sql);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task DateTimeOffset_parts_materialize_server_values(OceanBaseCompatibilityMode mode, bool async)
    {
        using var table = new DataTable();
        table.Columns.Add("Date", typeof(object));
        table.Columns.Add("TimeOfDay", typeof(object));
        table.Columns.Add("UtcDateTime", typeof(object));
        foreach (var name in new[] { "Year", "Month", "Day", "Hour", "Minute", "Second", "Millisecond", "Microsecond", "Nanosecond", "DayOfYear" })
            table.Columns.Add(name, typeof(int));
        var expected = new DateTimeOffset(2026, 11, 16, 10, 20, 30, TimeSpan.FromHours(8)).AddTicks(1234560);
        var timeOfDay = expected.TimeOfDay;
        table.Rows.Add(expected.Date, mode == OceanBaseCompatibilityMode.Oracle
            ? new OceanBaseIntervalDayToSecond(false, 0, 10, 20, 30, 123456000, 9, 6)
            : timeOfDay, expected.UtcDateTime, expected.Year, expected.Month, expected.Day, expected.Hour, expected.Minute,
            expected.Second, expected.Millisecond, expected.Microsecond, expected.Nanosecond, expected.DayOfYear);
        var capture = new Capture(table);
        await using var context = Create(mode, capture);
        var query = context.Rows.Where(row => row.Zoned.UtcDateTime > new DateTime(2026, 1, 1)).Select(row => new
        {
            Date = row.Zoned.Date,
            TimeOfDay = row.Zoned.TimeOfDay,
            UtcDateTime = row.Zoned.UtcDateTime,
            row.Zoned.Year,
            row.Zoned.Month,
            row.Zoned.Day,
            row.Zoned.Hour,
            row.Zoned.Minute,
            row.Zoned.Second,
            row.Zoned.Millisecond,
            row.Zoned.Microsecond,
            row.Zoned.Nanosecond,
            row.Zoned.DayOfYear
        });

        var result = Assert.Single(async ? await query.ToListAsync() : query.ToList());
        Assert.Equal(expected.Date, result.Date);
        Assert.Equal(timeOfDay, result.TimeOfDay);
        Assert.Equal(expected.UtcDateTime, result.UtcDateTime);
        Assert.Equal(expected.Year, result.Year);
        Assert.Equal(expected.Microsecond, result.Microsecond);
        Assert.Equal(expected.Nanosecond, result.Nanosecond);
        Assert.Equal(expected.DayOfYear, result.DayOfYear);
        var sql = Assert.Single(capture.Sql);
        Assert.DoesNotContain("__oceanbase_internal", sql);
        if (mode == OceanBaseCompatibilityMode.MySql)
            Assert.Contains("WHERE `r`.`Zoned` >", sql);
        else
            Assert.Contains("SYS_EXTRACT_UTC", sql);
    }

    private static TestContext Create(OceanBaseCompatibilityMode mode, Capture capture)
        => new(new DbContextOptionsBuilder<TestContext>()
            .UseOceanBase("Server=offline;Database=datetime_member", mode)
            .EnableServiceProviderCaching(false)
            .AddInterceptors(new NoNetwork(), capture).Options);

    private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
    {
        public DbSet<Row> Rows => Set<Row>();
    }

    private sealed class Row
    {
        public int Id { get; set; }
        public DateTime Stamp { get; set; }
        public DateTime? OptionalStamp { get; set; }
        public DateTimeOffset Zoned { get; set; }
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
        public List<DbParameter> Inputs { get; } = [];

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Sql.Add(command.CommandText);
            Inputs.AddRange(command.Parameters.Cast<DbParameter>());
            return InterceptionResult<DbDataReader>.SuppressWithResult(table.CreateDataReader());
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(ReaderExecuting(command, eventData, result));
    }
}
