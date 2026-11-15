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
