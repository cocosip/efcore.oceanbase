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

public sealed class DateTimeCalendarAddFunctionalTests
{
    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task DateTime_AddMonths_materializes_server_value_and_null(
        OceanBaseCompatibilityMode mode, bool async)
        => await Run(mode, async, years: false);

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task DateTime_AddYears_materializes_server_value_and_null(
        OceanBaseCompatibilityMode mode, bool async)
        => await Run(mode, async, years: true);

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task Numeric_day_hour_additions_refresh_parameters_and_materialize_promoted_results(
        OceanBaseCompatibilityMode mode, bool async)
    {
        using var table = new DataTable();
        table.Columns.Add("Value", typeof(object));
        var expected = new DateTime(2024, 3, 1, 1, 0, 0).AddTicks(mode == OceanBaseCompatibilityMode.MySql ? 10 : 1);
        table.Rows.Add(expected);
        table.Rows.Add(DBNull.Value);
        var capture = new Capture(table);
        await using var context = Create(mode, capture);
        var amount = 0.5;
        var query = context.Rows.Where(row => row.Stamp.AddDays(amount) > new DateTime(2000, 1, 1))
            .OrderBy(row => row.Id).Select(row => (DateTime?)row.OptionalStamp!.Value.AddHours(amount));
        var initial = async ? await query.ToListAsync() : query.ToList();
        Assert.Equal([expected, null], initial);
        Assert.Equal(DateTimeKind.Unspecified, initial[0]!.Value.Kind);
        Assert.All(Assert.Single(capture.Parameters), value => Assert.Equal(0.5, Assert.IsType<double>(value)));
        Assert.NotEmpty(capture.Parameters[0]);
        amount = -900.25;
        Assert.Equal([expected, null], async ? await query.ToListAsync() : query.ToList());
        Assert.All(capture.Parameters[1], value => Assert.Equal(-900.25, Assert.IsType<double>(value)));
        Assert.Equal(capture.Sql[0], capture.Sql[1]);
        Assert.DoesNotContain("__oceanbase_internal", capture.Sql[0]);
        Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? " AS DATETIME(6))" : " AS TIMESTAMP(7))", capture.Sql[0]);
    }

    [Fact]
    public void Oracle_interpolated_native_parameters_keep_caller_owned_names()
    {
        using var table = new DataTable();
        table.Columns.Add("Value", typeof(int));
        table.Rows.Add(1);
        var capture = new Capture(table);
        using var context = Create(OceanBaseCompatibilityMode.Oracle, capture);
        var parameter = new OceanBaseParameter { ParameterName = "_ob_value", Value = 1 };
        Assert.Equal(1, context.Database.SqlQuery<int>($"SELECT {parameter} AS \"Value\" FROM DUAL").Single());
        Assert.Contains(":" + parameter.ParameterName, Assert.Single(capture.Sql));
        Assert.DoesNotContain(":_ob__ob_value", capture.Sql[0]);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task Minute_through_tick_additions_keep_Double_and_Int64_parameters(
        OceanBaseCompatibilityMode mode, bool async)
    {
        using var table = new DataTable();
        table.Columns.Add("Value", typeof(object));
        var expected = new DateTime(2024, 3, 1).AddTicks(mode == OceanBaseCompatibilityMode.MySql ? 10 : 1);
        table.Rows.Add(expected);
        table.Rows.Add(DBNull.Value);
        var capture = new Capture(table);
        await using var context = Create(mode, capture);
        var amount = 0.5;
        var ticks = 9007199254740993L;
        var query = context.Rows.Where(row => row.Stamp.AddMinutes(amount).AddSeconds(amount)
                .AddMilliseconds(amount).AddMicroseconds(amount).AddTicks(ticks) > new DateTime(2000, 1, 1))
            .OrderBy(row => row.Id).Select(row => (DateTime?)row.OptionalStamp!.Value.AddTicks(ticks));
        Assert.Equal([expected, null], async ? await query.ToListAsync() : query.ToList());
        Assert.Contains((object)amount, capture.Parameters[0]);
        Assert.Contains((object)ticks, capture.Parameters[0]);
        amount = -1.25;
        ticks = -9007199254740993L;
        Assert.Equal([expected, null], async ? await query.ToListAsync() : query.ToList());
        Assert.Contains((object)amount, capture.Parameters[1]);
        Assert.Contains((object)ticks, capture.Parameters[1]);
        Assert.Equal(capture.Sql[0], capture.Sql[1]);
        Assert.DoesNotContain("__oceanbase_internal", capture.Sql[0]);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task DateTimeOffset_duration_arithmetic_binds_full_range_ticks(OceanBaseCompatibilityMode mode, bool async)
    {
        var oracle = mode == OceanBaseCompatibilityMode.Oracle;
        var expected = new DateTimeOffset(2024, 3, 1, 2, 3, 4, oracle ? TimeSpan.FromMinutes(-30) : TimeSpan.Zero);
        using var table = new DataTable();
        table.Columns.Add("Value", oracle ? typeof(OceanBaseTimestampWithTimeZone) : typeof(DateTime));
        table.Rows.Add(oracle ? (object)new OceanBaseTimestampWithTimeZone(expected) : expected.UtcDateTime);
        table.Rows.Add(DBNull.Value);
        var capture = new Capture(table);
        await using var context = Create(mode, capture);
        var duration = TimeSpan.FromTicks(9_007_199_254_740_990L);
        var query = context.Rows.Where(row => row.Zoned!.Value.Add(duration).UtcTicks > 0)
            .OrderBy(row => row.Id).Select(row => (DateTimeOffset?)row.Zoned!.Value.Subtract(duration));

        foreach (var ticks in new[] { duration.Ticks, long.MinValue })
        {
            duration = TimeSpan.FromTicks(ticks);
            var result = async ? await query.ToListAsync() : query.ToList();
            Assert.Equal(2, result.Count);
            Assert.True(result[0]?.EqualsExact(expected));
            Assert.Null(result[1]);
            Assert.All(capture.Parameters[^1], value => Assert.Equal(ticks, value));
        }
        Assert.Equal(capture.Sql[0], capture.Sql[1]);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task DateTimeOffset_AddTicks_materializes_timezone_and_null(OceanBaseCompatibilityMode mode, bool async)
    {
        var oracle = mode == OceanBaseCompatibilityMode.Oracle;
        var expected = new DateTimeOffset(2024, 2, 29, 23, 59, 59, oracle ? TimeSpan.FromMinutes(-30) : TimeSpan.Zero)
            .AddTicks(oracle ? 9999999 : 9999990);
        using var table = new DataTable();
        table.Columns.Add("Value", oracle ? typeof(OceanBaseTimestampWithTimeZone) : typeof(DateTime));
        table.Rows.Add(oracle ? (object)new OceanBaseTimestampWithTimeZone(expected) : expected.UtcDateTime);
        table.Rows.Add(DBNull.Value);
        var capture = new Capture(table);
        await using var context = Create(mode, capture);
        var adjustment = -20L;
        var query = context.Rows.OrderBy(row => row.Id).Select(row => (DateTimeOffset?)row.Zoned!.Value.AddTicks(adjustment));

        var result = async ? await query.ToListAsync() : query.ToList();
        Assert.Equal(2, result.Count);
        Assert.True(result[0]?.EqualsExact(expected));
        Assert.Null(result[1]);
        Assert.Contains(oracle ? "FROM_TZ" : "TIMESTAMPADD", Assert.Single(capture.Sql));
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task DateTimeOffset_differences_materialize_exact_ticks_and_nulls(
        OceanBaseCompatibilityMode mode, bool async)
    {
        using var table = new DataTable();
        table.Columns.Add("Value", typeof(object));
        var ticks = mode == OceanBaseCompatibilityMode.MySql ? -39_000_000_000_000_010L : -39_000_000_000_000_019L;
        table.Rows.Add(mode == OceanBaseCompatibilityMode.MySql ? (object)ticks : (decimal)ticks);
        table.Rows.Add(DBNull.Value);
        var capture = new Capture(table);
        await using var context = Create(mode, capture);
        var anchor = new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.FromHours(6));
        var query = context.Rows.OrderBy(row => row.Id).Select(row => (TimeSpan?)row.Zoned!.Value.Subtract(anchor));

        Assert.Equal([TimeSpan.FromTicks(ticks), null], async ? await query.ToListAsync() : query.ToList());
        var sql = Assert.Single(capture.Sql);
        Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "DATEDIFF" : "SYS_EXTRACT_UTC", sql);
        Assert.Contains("Zoned", sql);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task Date_differences_materialize_exact_long_durations_and_refresh_parameters(
        OceanBaseCompatibilityMode mode, bool async)
    {
        using var table = new DataTable();
        table.Columns.Add("Value", typeof(object));
        const long ticks = 3155378975999999990L;
        table.Rows.Add(mode == OceanBaseCompatibilityMode.MySql ? (object)ticks : (decimal)ticks);
        table.Rows.Add(DBNull.Value);
        var capture = new Capture(table);
        await using var context = Create(mode, capture);
        var anchor = new DateTime(2000, 1, 1);
        var minimum = TimeSpan.FromDays(1000);
        var query = context.Rows.Where(row => row.Stamp.Subtract(anchor) > minimum)
            .OrderBy(row => row.Id).Select(row => (TimeSpan?)row.OptionalStamp!.Value.Subtract(anchor));
        Assert.Equal([TimeSpan.FromTicks(ticks), null], async ? await query.ToListAsync() : query.ToList());
        Assert.Contains((object)minimum.Ticks, capture.Parameters[0]);
        anchor = new DateTime(1900, 1, 1);
        minimum = -minimum;
        Assert.Equal([TimeSpan.FromTicks(ticks), null], async ? await query.ToListAsync() : query.ToList());
        Assert.Contains((object)minimum.Ticks, capture.Parameters[1]);
        Assert.Equal(capture.Sql[0], capture.Sql[1]);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task TimeOnly_additions_refresh_full_duration_and_floating_offsets(OceanBaseCompatibilityMode mode, bool async)
    {
        using var table = new DataTable();
        table.Columns.Add("Value", typeof(object));
        var expected = new TimeOnly(12, 34, 56).Add(TimeSpan.FromTicks(mode == OceanBaseCompatibilityMode.MySql ? 1234560 : 1234567));
        table.Rows.Add(mode == OceanBaseCompatibilityMode.MySql ? (object)expected.ToTimeSpan()
            : new OceanBaseIntervalDayToSecond(false, 0, 12, 34, 56, 123456700, 1, 7));
        table.Rows.Add(DBNull.Value);
        var capture = new Capture(table);
        await using var context = Create(mode, capture);
        var amount = TimeSpan.MaxValue;
        var hours = 0.5;
        var query = context.Rows.Where(row => row.Clock.AddHours(hours).AddMinutes(hours).Ticks > 0)
            .OrderBy(row => row.Id).Select(row => (TimeOnly?)row.OptionalClock!.Value.Add(amount));
        Assert.Equal([expected, null], async ? await query.ToListAsync() : query.ToList());
        Assert.Contains((object)long.MaxValue, capture.Parameters[0]);
        Assert.Contains((object)hours, capture.Parameters[0]);
        amount = TimeSpan.MinValue;
        hours = -1.25;
        Assert.Equal([expected, null], async ? await query.ToListAsync() : query.ToList());
        Assert.Contains((object)long.MinValue, capture.Parameters[1]);
        Assert.Contains((object)hours, capture.Parameters[1]);
        Assert.Equal(capture.Sql[0], capture.Sql[1]);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task Temporal_conversions_materialize_own_carriers_and_refresh_bounds(OceanBaseCompatibilityMode mode, bool async)
    {
        using var table = new DataTable();
        table.Columns.Add("Value", typeof(object));
        var capture = new Capture(table);
        await using var context = Create(mode, capture);
        var day = new DateOnly(2024, 2, 29);
        var clock = new TimeOnly(12, 34, 56).Add(TimeSpan.FromTicks(mode == OceanBaseCompatibilityMode.MySql ? 1234560 : 1234567));
        var stamp = day.ToDateTime(clock);
        Set(day.ToDateTime(TimeOnly.MinValue));
        Assert.Equal([day, null], await Read(context.Rows.Where(row => DateOnly.FromDateTime(row.Stamp).Year > 2000)
            .Select(row => (DateOnly?)DateOnly.FromDateTime(row.OptionalStamp!.Value))));
        Set(mode == OceanBaseCompatibilityMode.MySql ? (object)clock.ToTimeSpan()
            : new OceanBaseIntervalDayToSecond(false, 0, 12, 34, 56, 123456700, 1, 7));
        Assert.Equal([clock, null], await Read(context.Rows.Select(row => (TimeOnly?)TimeOnly.FromDateTime(row.OptionalStamp!.Value))));
        Assert.Equal([clock.ToTimeSpan(), null], await Read(context.Rows.Select(row => (TimeSpan?)row.OptionalClock!.Value.ToTimeSpan())));
        var amount = TimeSpan.MaxValue;
        var start = new TimeOnly(22, 0);
        var end = new TimeOnly(2, 0);
        var query = context.Rows.Where(row => row.Clock.IsBetween(start, end))
            .Select(row => (TimeOnly?)TimeOnly.FromTimeSpan(EF.Parameter(amount)));
        var index = capture.Parameters.Count;
        Assert.Equal([clock, null], await Read(query));
        Assert.Contains((object)long.MaxValue, capture.Parameters[index]);
        Assert.Contains(capture.Parameters[index], value => Bound(value) == TimeSpan.FromHours(22));
        amount = TimeSpan.MinValue;
        start = new TimeOnly(20, 0);
        Assert.Equal([clock, null], await Read(query));
        Assert.Contains((object)long.MinValue, capture.Parameters[index + 1]);
        Assert.Contains(capture.Parameters[index + 1], value => Bound(value) == TimeSpan.FromHours(20));
        Assert.Equal(capture.Sql[index], capture.Sql[index + 1]);
        Set(stamp);
        var combined = await Read(context.Rows.Select(row => (DateTime?)row.Day.ToDateTime(row.OptionalClock!.Value)));
        Assert.Equal([stamp, null], combined);
        Assert.Equal(DateTimeKind.Unspecified, combined[0]!.Value.Kind);
        Set(true);
        Assert.Equal([true, null], await Read(context.Rows.Select(row => (bool?)row.OptionalClock!.Value.IsBetween(start, end))));

        void Set(object value)
        {
            table.Rows.Clear();
            table.Rows.Add(value);
            table.Rows.Add(DBNull.Value);
        }

        Task<T[]> Read<T>(IQueryable<T> value) => async ? value.ToArrayAsync() : Task.FromResult(value.ToArray());
        TimeSpan? Bound(object? value) => value is TimeSpan span ? span
            : value is OceanBaseIntervalDayToSecond interval ? interval.ToTimeSpan() : null;
    }

    private static async Task Run(OceanBaseCompatibilityMode mode, bool async, bool years)
    {
        using var table = new DataTable();
        table.Columns.Add("Value", typeof(object));
        var fraction = mode == OceanBaseCompatibilityMode.MySql ? 1234560 : 1234567;
        var expected = new DateTime(years ? 2025 : 2024, years ? 2 : 3, years ? 28 : 29, 12, 34, 56).AddTicks(fraction);
        table.Rows.Add(expected);
        table.Rows.Add(DBNull.Value);
        var capture = new Capture(table);
        await using var context = Create(mode, capture);
        var amount = 1;
        var query = years
            ? context.Rows.Where(row => row.Stamp.AddYears(amount) > new DateTime(2000, 1, 1))
                .OrderBy(row => row.OptionalStamp!.Value.AddYears(amount))
                .Select(row => (DateTime?)row.OptionalStamp!.Value.AddYears(amount))
            : context.Rows.Where(row => row.Stamp.AddMonths(amount) > new DateTime(2000, 1, 1))
                .OrderBy(row => row.OptionalStamp!.Value.AddMonths(amount))
                .Select(row => (DateTime?)row.OptionalStamp!.Value.AddMonths(amount));

        var result = async ? await query.ToListAsync() : query.ToList();
        Assert.Equal([expected, null], result);
        Assert.All(Assert.Single(capture.Parameters), value => Assert.Equal(1, Assert.IsType<int>(value)));
        amount = -1;
        table.Rows.Clear();
        table.Rows.Add(new DateTime(2023, 12, 31, 23, 59, 59).AddTicks(fraction));
        table.Rows.Add(DBNull.Value);
        var rebound = async ? await query.ToListAsync() : query.ToList();
        Assert.Equal([new DateTime(2023, 12, 31, 23, 59, 59).AddTicks(fraction), null], rebound);
        Assert.All(capture.Parameters[1], value => Assert.Equal(-1, Assert.IsType<int>(value)));
        Assert.NotEmpty(capture.Parameters[0]);
        Assert.Equal(capture.Sql[0], capture.Sql[1]);

        var sql = capture.Sql[0];
        Assert.DoesNotContain("__oceanbase_internal", sql);
        Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "DATE_ADD" : "ADD_MONTHS", sql);
        Assert.Contains("OptionalStamp", sql);
        if (mode == OceanBaseCompatibilityMode.Oracle)
        {
            Assert.Contains("LEAST(EXTRACT(DAY FROM", sql);
            Assert.Contains("LAST_DAY(ADD_MONTHS(TRUNC(", sql);
            Assert.Contains(" AS TIMESTAMP(7)) - CAST(TRUNC(", sql);
            if (years) Assert.Contains(" * 12", sql);
        }
        else Assert.Contains(years ? " YEAR)" : " MONTH)", sql);
    }

    private static TestContext Create(OceanBaseCompatibilityMode mode, Capture capture)
        => new(new DbContextOptionsBuilder<TestContext>()
            .UseOceanBase("Server=offline;Database=datetime_calendar_add", mode)
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
        public TimeOnly Clock { get; set; }
        public TimeOnly? OptionalClock { get; set; }
        public DateOnly Day { get; set; }
        public DateTimeOffset? Zoned { get; set; }
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
        public List<object?[]> Parameters { get; } = [];

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Sql.Add(command.CommandText);
            Parameters.Add(command.Parameters.Cast<DbParameter>().Select(parameter => parameter.Value).ToArray());
            return InterceptionResult<DbDataReader>.SuppressWithResult(table.CreateDataReader());
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(ReaderExecuting(command, eventData, result));
    }
}
