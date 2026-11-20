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

public sealed class TemporalFractionalMemberFunctionalTests
{
    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task DateTime_and_timeonly_fractional_members_materialize(
        OceanBaseCompatibilityMode mode, bool async)
    {
        using var table = new DataTable();
        foreach (var column in new[] { "StampMilliseconds", "StampMicroseconds", "StampNanoseconds",
            "ClockMilliseconds", "ClockMicroseconds", "ClockNanoseconds" })
            table.Columns.Add(column, typeof(int));
        table.Rows.Add(123, 456, 700, 987, 654, 300);

        var capture = new Capture(table);
        await using var context = Create(mode, capture);
        var query = context.Rows
            .Where(row => row.Stamp.Millisecond >= 100 && row.Clock.Nanosecond == 300)
            .Select(row => new
            {
                StampMilliseconds = row.Stamp.Millisecond,
                StampMicroseconds = row.Stamp.Microsecond,
                StampNanoseconds = row.Stamp.Nanosecond,
                ClockMilliseconds = row.Clock.Millisecond,
                ClockMicroseconds = row.Clock.Microsecond,
                ClockNanoseconds = row.Clock.Nanosecond
            });

        var result = Assert.Single(async ? await query.ToListAsync() : query.ToList());
        Assert.Equal(123, result.StampMilliseconds);
        Assert.Equal(456, result.StampMicroseconds);
        Assert.Equal(700, result.StampNanoseconds);
        Assert.Equal(987, result.ClockMilliseconds);
        Assert.Equal(654, result.ClockMicroseconds);
        Assert.Equal(300, result.ClockNanoseconds);

        var sql = Assert.Single(capture.Sql);
        Assert.DoesNotContain("__oceanbase_internal", sql);
        if (mode == OceanBaseCompatibilityMode.MySql)
        {
            Assert.Contains("MICROSECOND", sql);
            Assert.Contains("DIV 1000", sql);
            Assert.Contains("MOD(MICROSECOND", sql);
        }
        else
        {
            Assert.Contains("EXTRACT(SECOND FROM", sql);
            Assert.Contains("* 1000", sql);
            Assert.Contains("* 1000000", sql);
            Assert.Contains("* 1000000000", sql);
        }
    }

    private static TestContext Create(OceanBaseCompatibilityMode mode, Capture capture)
        => new(new DbContextOptionsBuilder<TestContext>()
            .UseOceanBase("Server=offline;Database=temporal_fractional", mode)
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
