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

public sealed class DateTimeTimeOfDayFunctionalTests
{
    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task DateTime_TimeOfDay_materializes_from_the_mode_specific_time_carrier(
        OceanBaseCompatibilityMode mode, bool async)
    {
        var expected = mode == OceanBaseCompatibilityMode.Oracle
            ? new TimeSpan(12, 34, 56).Add(TimeSpan.FromTicks(1234560))
            : new TimeSpan(12, 34, 56).Add(TimeSpan.FromMilliseconds(123));
        using var table = new DataTable();
        table.Columns.Add("Value", typeof(object));
        table.Rows.Add(mode == OceanBaseCompatibilityMode.Oracle
            ? new OceanBaseIntervalDayToSecond(false, 0, 12, 34, 56, 123456000, 9, 6)
            : expected);
        table.Rows.Add(DBNull.Value);

        var capture = new Capture(table);
        await using var context = Create(mode, capture);
        var query = context.Rows.Select(row => (TimeSpan?)row.OptionalStamp!.Value.TimeOfDay);
        var result = async ? await query.ToListAsync() : query.ToList();

        Assert.Equal([expected, null], result);
        var sql = Assert.Single(capture.Sql);
        Assert.DoesNotContain("__oceanbase_internal", sql);
        Assert.Contains(mode == OceanBaseCompatibilityMode.MySql
            ? "TIME(`r`.`OptionalStamp`)"
            : "(\"r\".\"OptionalStamp\" - CAST(TRUNC(\"r\".\"OptionalStamp\") AS TIMESTAMP))", sql);
    }

    private static TestContext Create(OceanBaseCompatibilityMode mode, Capture capture)
        => new(new DbContextOptionsBuilder<TestContext>()
            .UseOceanBase("Server=offline;Database=datetime_time_of_day", mode)
            .EnableServiceProviderCaching(false)
            .AddInterceptors(new NoNetwork(), capture).Options);

    private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
    {
        public DbSet<Row> Rows => Set<Row>();
    }

    private sealed class Row
    {
        public int Id { get; set; }
        public DateTime? OptionalStamp { get; set; }
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
