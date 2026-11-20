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

public sealed class TimeSpanTicksFunctionalTests
{
    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task TimeSpan_Ticks_materializes_exact_100ns_values(OceanBaseCompatibilityMode mode, bool async)
    {
        var expected = new TimeSpan(2, 3, 4, 5).Add(TimeSpan.FromTicks(1234567)).Ticks;
        using var table = new DataTable();
        table.Columns.Add("Value", typeof(object));
        table.Rows.Add(expected);
        table.Rows.Add(DBNull.Value);

        var capture = new Capture(table);
        await using var context = Create(mode, capture);
        var query = context.Rows
            .Where(row => row.Duration.Ticks != 0)
            .Select(row => (long?)row.Duration.Ticks);
        var result = async ? await query.ToListAsync() : query.ToList();

        Assert.Equal([expected, null], result);
        var sql = Assert.Single(capture.Sql);
        Assert.DoesNotContain("__oceanbase_internal", sql);
        Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "TIME_TO_SEC" : "EXTRACT(DAY FROM", sql);
        Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "MICROSECOND" : "EXTRACT(SECOND FROM", sql);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task TimeOnly_Ticks_materialize_from_midnight(OceanBaseCompatibilityMode mode, bool async)
    {
        var expected = new TimeOnly(12, 34, 56).Add(TimeSpan.FromTicks(1234567)).Ticks;
        using var table = new DataTable();
        table.Columns.Add("Value", typeof(object));
        table.Rows.Add(expected);
        table.Rows.Add(DBNull.Value);

        var capture = new Capture(table);
        await using var context = Create(mode, capture);
        var query = context.Rows
            .Where(row => row.Clock.Ticks > TimeSpan.TicksPerHour)
            .Select(row => (long?)row.Clock.Ticks);
        var result = async ? await query.ToListAsync() : query.ToList();

        Assert.Equal([expected, null], result);
        var sql = Assert.Single(capture.Sql);
        Assert.DoesNotContain("__oceanbase_internal", sql);
        Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "TIME_TO_SEC" : "EXTRACT(HOUR FROM", sql);
        Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "MICROSECOND" : "EXTRACT(SECOND FROM", sql);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task DateTime_Ticks_materialize_from_the_gregorian_epoch(OceanBaseCompatibilityMode mode, bool async)
    {
        var expected = new DateTime(2024, 2, 29, 12, 34, 56).AddTicks(1234567).Ticks;
        using var table = new DataTable();
        table.Columns.Add("Value", typeof(object));
        table.Rows.Add(expected);
        table.Rows.Add(DBNull.Value);

        var capture = new Capture(table);
        await using var context = Create(mode, capture);
        var query = context.Rows
            .Where(row => row.Stamp.Ticks > TimeSpan.TicksPerDay)
            .Select(row => (long?)row.Stamp.Ticks);
        var result = async ? await query.ToListAsync() : query.ToList();

        Assert.Equal([expected, null], result);
        var sql = Assert.Single(capture.Sql);
        Assert.DoesNotContain("__oceanbase_internal", sql);
        if (mode == OceanBaseCompatibilityMode.MySql)
        {
            Assert.Contains("DATEDIFF(`r`.`Stamp`, '0001-01-01')", sql);
            Assert.Contains("TIME_TO_SEC(TIME(`r`.`Stamp`))", sql);
        }
        else
        {
            Assert.Contains("TRUNC(\"r\".\"Stamp\") - DATE '0001-01-01'", sql);
            Assert.Contains("EXTRACT(SECOND FROM", sql);
        }
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task DateTimeOffset_UtcTicks_materialize_from_the_utc_gregorian_epoch(OceanBaseCompatibilityMode mode, bool async)
    {
        var expected = new DateTimeOffset(2024, 2, 29, 12, 34, 56, TimeSpan.FromHours(8)).AddTicks(1234567).UtcTicks;
        using var table = new DataTable();
        table.Columns.Add("Value", typeof(object));
        table.Rows.Add(expected);
        table.Rows.Add(DBNull.Value);

        var capture = new Capture(table);
        await using var context = Create(mode, capture);
        var query = context.Rows
            .Where(row => row.Zoned.UtcTicks > TimeSpan.TicksPerDay)
            .OrderBy(row => row.Zoned.UtcTicks)
            .Select(row => (long?)row.Zoned.UtcTicks);
        var result = async ? await query.ToListAsync() : query.ToList();

        Assert.Equal([expected, null], result);
        var sql = Assert.Single(capture.Sql);
        Assert.DoesNotContain("__oceanbase_internal", sql);
        if (mode == OceanBaseCompatibilityMode.MySql)
        {
            Assert.Contains("DATEDIFF(`r`.`Zoned`, '0001-01-01')", sql);
            Assert.Contains("TIME_TO_SEC(TIME(`r`.`Zoned`))", sql);
            Assert.Contains("MICROSECOND(TIME(`r`.`Zoned`))", sql);
        }
        else
        {
            Assert.Contains("SYS_EXTRACT_UTC", sql);
            Assert.Contains("TRUNC(CAST(SYS_EXTRACT_UTC", sql);
            Assert.Contains("EXTRACT(SECOND FROM", sql);
        }
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task Oracle_DateTimeOffset_TotalOffsetMinutes_materializes_and_preserves_null(
        OceanBaseCompatibilityMode mode, bool async)
    {
        using var table = new DataTable();
        table.Columns.Add("Value", typeof(object));
        table.Rows.Add(480);
        table.Rows.Add(DBNull.Value);

        var capture = new Capture(table);
        await using var context = Create(mode, capture);
        var query = context.Rows
            .Where(row => row.Zoned.TotalOffsetMinutes >= 0)
            .OrderBy(row => row.Zoned.TotalOffsetMinutes)
            .Select(row => (int?)row.Zoned.TotalOffsetMinutes);
        var result = async ? await query.ToListAsync() : query.ToList();

        Assert.Equal([480, null], result);
        var sql = Assert.Single(capture.Sql);
        Assert.DoesNotContain("__oceanbase_internal", sql);
        Assert.Contains("EXTRACT(TIMEZONE_HOUR FROM", sql);
        Assert.Contains("EXTRACT(TIMEZONE_MINUTE FROM", sql);
    }

    [Fact]
    public void MySql_DateTimeOffset_TotalOffsetMinutes_is_rejected_before_command_execution()
    {
        using var table = new DataTable();
        table.Columns.Add("Value", typeof(object));
        table.Rows.Add(0);

        var capture = new Capture(table);
        using var context = Create(OceanBaseCompatibilityMode.MySql, capture);
        Assert.Throws<InvalidOperationException>(() => context.Rows
            .Where(row => row.Zoned.TotalOffsetMinutes < 0)
            .Select(row => row.Id)
            .ToQueryString());
        Assert.Empty(capture.Sql);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task Oracle_DateTimeOffset_Offset_materializes_and_preserves_null(
        OceanBaseCompatibilityMode mode, bool async)
    {
        var positive = TimeSpan.FromHours(8);
        var negative = -TimeSpan.FromHours(5.5);
        using var table = new DataTable();
        table.Columns.Add("Value", typeof(object));
        table.Rows.Add(new OceanBaseIntervalDayToSecond(false, 0, 8, 0, 0, leadingPrecision: 2, scale: 6));
        table.Rows.Add(new OceanBaseIntervalDayToSecond(true, 0, 5, 30, 0, leadingPrecision: 2, scale: 6));
        table.Rows.Add(DBNull.Value);

        var capture = new Capture(table);
        await using var context = Create(mode, capture);
        var query = context.Rows
            .Where(row => row.Zoned.Offset != TimeSpan.Zero)
            .OrderBy(row => row.Zoned.Offset)
            .Select(row => (TimeSpan?)row.Zoned.Offset);
        var result = async ? await query.ToListAsync() : query.ToList();

        Assert.Equal([positive, negative, null], result);
        var sql = Assert.Single(capture.Sql);
        Assert.DoesNotContain("__oceanbase_internal", sql);
        Assert.Contains("NUMTODSINTERVAL", sql);
        Assert.Contains("EXTRACT(TIMEZONE_HOUR FROM", sql);
        Assert.Contains("EXTRACT(TIMEZONE_MINUTE FROM", sql);
    }

    [Fact]
    public void MySql_DateTimeOffset_Offset_is_rejected_before_command_execution()
    {
        using var table = new DataTable();
        table.Columns.Add("Value", typeof(object));
        table.Rows.Add(TimeSpan.Zero);

        var capture = new Capture(table);
        using var context = Create(OceanBaseCompatibilityMode.MySql, capture);
        Assert.Throws<InvalidOperationException>(() => context.Rows
            .Where(row => row.Zoned.Offset < TimeSpan.Zero)
            .Select(row => row.Id)
            .ToQueryString());
        Assert.Empty(capture.Sql);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task Oracle_DateTimeOffset_Ticks_materialize_local_wall_clock_values_and_null(
        OceanBaseCompatibilityMode mode, bool async)
    {
        var positive = new DateTimeOffset(2024, 2, 29, 12, 34, 56, TimeSpan.FromHours(8)).AddTicks(1234567).Ticks;
        var negative = new DateTimeOffset(2024, 2, 29, 12, 34, 56, TimeSpan.FromHours(-5.5)).AddTicks(1234567).Ticks;
        using var table = new DataTable();
        table.Columns.Add("Value", typeof(object));
        table.Rows.Add(positive);
        table.Rows.Add(negative);
        table.Rows.Add(DBNull.Value);

        var capture = new Capture(table);
        await using var context = Create(mode, capture);
        var query = context.Rows
            .Where(row => row.Zoned.Ticks != 0)
            .OrderBy(row => row.Zoned.Ticks)
            .Select(row => (long?)row.Zoned.Ticks);
        var result = async ? await query.ToListAsync() : query.ToList();

        Assert.Equal([positive, negative, null], result);
        var sql = Assert.Single(capture.Sql);
        Assert.DoesNotContain("SYS_EXTRACT_UTC", sql);
        Assert.DoesNotContain("__oceanbase_internal", sql);
        Assert.Contains("TRUNC(CAST", sql);
        Assert.Contains("EXTRACT(SECOND FROM", sql);
    }

    [Fact]
    public void MySql_DateTimeOffset_Ticks_is_rejected_before_command_execution()
    {
        using var table = new DataTable();
        table.Columns.Add("Value", typeof(object));
        table.Rows.Add(0L);

        var capture = new Capture(table);
        using var context = Create(OceanBaseCompatibilityMode.MySql, capture);
        Assert.Throws<InvalidOperationException>(() => context.Rows
            .Where(row => row.Zoned.Ticks < 0)
            .Select(row => row.Id)
            .ToQueryString());
        Assert.Empty(capture.Sql);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task Oracle_DateTimeOffset_DateTime_materializes_local_wall_clock_values_and_null(
        OceanBaseCompatibilityMode mode, bool async)
    {
        var positive = new DateTimeOffset(2024, 2, 29, 12, 34, 56, TimeSpan.FromHours(8)).DateTime;
        var negative = new DateTimeOffset(2024, 2, 29, 12, 34, 56, TimeSpan.FromHours(-5.5)).DateTime;
        using var table = new DataTable();
        table.Columns.Add("Value", typeof(object));
        table.Rows.Add(positive);
        table.Rows.Add(negative);
        table.Rows.Add(DBNull.Value);

        var capture = new Capture(table);
        await using var context = Create(mode, capture);
        var query = context.Rows
            .Where(row => row.Zoned.DateTime > new DateTime(2024, 1, 1))
            .OrderBy(row => row.Zoned.DateTime)
            .Select(row => (DateTime?)row.Zoned.DateTime);
        var result = async ? await query.ToListAsync() : query.ToList();

        Assert.Equal([positive, negative, null], result);
        Assert.Equal(DateTimeKind.Unspecified, result[0]!.Value.Kind);
        Assert.Equal(DateTimeKind.Unspecified, result[1]!.Value.Kind);
        var sql = Assert.Single(capture.Sql);
        Assert.DoesNotContain("SYS_EXTRACT_UTC", sql);
        Assert.DoesNotContain("__oceanbase_internal", sql);
        Assert.Contains("CAST", sql);
        Assert.Contains("AS TIMESTAMP", sql);
    }

    [Fact]
    public void MySql_DateTimeOffset_DateTime_is_rejected_before_command_execution()
    {
        using var table = new DataTable();
        table.Columns.Add("Value", typeof(object));
        table.Rows.Add(DateTime.UtcNow);

        var capture = new Capture(table);
        using var context = Create(OceanBaseCompatibilityMode.MySql, capture);
        Assert.Throws<InvalidOperationException>(() => context.Rows
            .Where(row => row.Zoned.DateTime > DateTime.UnixEpoch)
            .Select(row => row.Id)
            .ToQueryString());
        Assert.Empty(capture.Sql);
    }

    private static TestContext Create(OceanBaseCompatibilityMode mode, Capture capture)
        => new(new DbContextOptionsBuilder<TestContext>()
            .UseOceanBase("Server=offline;Database=timespan_ticks", mode)
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
        public DateTimeOffset Zoned { get; set; }
        public TimeSpan Duration { get; set; }
        public TimeOnly Clock { get; set; }
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
