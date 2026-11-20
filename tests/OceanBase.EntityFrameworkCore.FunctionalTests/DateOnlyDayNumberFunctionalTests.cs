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

public sealed class DateOnlyDayNumberFunctionalTests
{
    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task DateOnly_DayNumber_materializes_as_the_zero_based_gregorian_day_number(
        OceanBaseCompatibilityMode mode, bool async)
    {
        using var table = new DataTable();
        table.Columns.Add("Value", typeof(object));
        table.Rows.Add(new DateOnly(2024, 2, 29).DayNumber);
        table.Rows.Add(DBNull.Value);

        var capture = new Capture(table);
        await using var context = Create(mode, capture);
        var query = context.Rows
            .Where(row => row.Date.DayNumber > 700000)
            .Select(row => (int?)row.Date.DayNumber);
        var result = async ? await query.ToListAsync() : query.ToList();

        Assert.Equal([738944, null], result);
        var sql = Assert.Single(capture.Sql);
        Assert.DoesNotContain("__oceanbase_internal", sql);
        Assert.Contains(mode == OceanBaseCompatibilityMode.MySql
            ? "DATEDIFF(`r`.`Date`, '0001-01-01')"
            : "TRUNC(\"r\".\"Date\") - DATE '0001-01-01'", sql);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task DateOnly_AddDays_materializes_server_value(OceanBaseCompatibilityMode mode, bool async)
    {
        using var table = new DataTable();
        table.Columns.Add("Value", typeof(object));
        table.Rows.Add(new DateTime(2024, 3, 3));
        var capture = new Capture(table);
        await using var context = Create(mode, capture);
        var query = context.Rows.Where(row => row.Date.AddDays(3) > new DateOnly(2024, 1, 1))
            .Select(row => row.Date.AddDays(3));

        var result = Assert.Single(async ? await query.ToListAsync() : query.ToList());
        Assert.Equal(new DateOnly(2024, 3, 3), result);
        var sql = Assert.Single(capture.Sql);
        Assert.DoesNotContain("__oceanbase_internal", sql);
        Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "DATE_ADD" : " + ", sql);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task DateOnly_AddMonths_materializes_server_value(OceanBaseCompatibilityMode mode, bool async)
    {
        using var table = new DataTable();
        table.Columns.Add("Value", typeof(object));
        table.Rows.Add(new DateTime(2024, 3, 29));
        var capture = new Capture(table);
        await using var context = Create(mode, capture);
        var query = context.Rows.Where(row => row.Date.AddMonths(1) > new DateOnly(2024, 1, 1))
            .Select(row => row.Date.AddMonths(1));

        var result = Assert.Single(async ? await query.ToListAsync() : query.ToList());
        Assert.Equal(new DateOnly(2024, 3, 29), result);
        var sql = Assert.Single(capture.Sql);
        Assert.DoesNotContain("__oceanbase_internal", sql);
        Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "DATE_ADD" : "ADD_MONTHS", sql);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task DateOnly_AddYears_materializes_server_value(OceanBaseCompatibilityMode mode, bool async)
    {
        using var table = new DataTable();
        table.Columns.Add("Value", typeof(object));
        table.Rows.Add(new DateTime(2025, 2, 28));
        var capture = new Capture(table);
        await using var context = Create(mode, capture);
        var query = context.Rows.Where(row => row.Date.AddYears(1) > new DateOnly(2024, 1, 1))
            .Select(row => row.Date.AddYears(1));

        var result = Assert.Single(async ? await query.ToListAsync() : query.ToList());
        Assert.Equal(new DateOnly(2025, 2, 28), result);
        var sql = Assert.Single(capture.Sql);
        Assert.DoesNotContain("__oceanbase_internal", sql);
        Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "DATE_ADD" : "ADD_MONTHS", sql);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, false, true)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true, true)]
    public async Task DateOnly_calendar_add_rebinds_amount_and_materializes_nullable_DATE(
        OceanBaseCompatibilityMode mode, bool async, bool years)
    {
        using var table = new DataTable();
        table.Columns.Add("Value", typeof(object));
        table.Rows.Add(new DateTime(2024, 2, 28));
        table.Rows.Add(DBNull.Value);
        var capture = new Capture(table);
        await using var context = Create(mode, capture);
        var amount = 2;
        var query = years
            ? context.Rows.Where(row => row.Date.AddYears(row.Id + amount) > new DateOnly(2000, 1, 1))
                .OrderBy(row => row.OptionalDate!.Value.AddYears(amount))
                .Select(row => (DateOnly?)row.OptionalDate!.Value.AddYears(amount))
            : context.Rows.Where(row => row.Date.AddMonths(amount) > new DateOnly(2000, 1, 1))
                .OrderBy(row => row.OptionalDate!.Value.AddMonths(amount))
                .Select(row => (DateOnly?)row.OptionalDate!.Value.AddMonths(amount));

        var result = async ? await query.ToListAsync() : query.ToList();
        Assert.Equal([new DateOnly(2024, 2, 28), null], result);
        Assert.NotEmpty(Assert.Single(capture.Parameters));
        Assert.All(capture.Parameters[0], value => Assert.Equal(2, Assert.IsType<int>(value)));
        amount = -1;
        table.Rows.Clear();
        table.Rows.Add(new DateTime(2023, 12, 31));
        table.Rows.Add(DBNull.Value);
        var rebound = async ? await query.ToListAsync() : query.ToList();
        Assert.Equal([new DateOnly(2023, 12, 31), null], rebound);
        Assert.All(capture.Parameters[1], value => Assert.Equal(-1, Assert.IsType<int>(value)));
        Assert.Equal(capture.Sql[0], capture.Sql[1]);

        var sql = capture.Sql[0];
        Assert.DoesNotContain("__oceanbase_internal", sql);
        Assert.DoesNotContain("TIMESTAMP", sql);
        if (mode == OceanBaseCompatibilityMode.Oracle)
        {
            Assert.Contains("ADD_MONTHS(TRUNC(\"r\".\"Date\", 'MM'), ", sql);
            Assert.Contains("LEAST(EXTRACT(DAY FROM \"r\".\"Date\") - 1", sql);
            Assert.Contains("LAST_DAY(ADD_MONTHS(TRUNC(", sql);
            if (years) Assert.Matches(@"\(""r""\.""Id"" \+ :__amount_\w+\) \* 12", sql);
        }
        else Assert.Contains(years ? " YEAR)" : " MONTH)", sql);
    }

    private static TestContext Create(OceanBaseCompatibilityMode mode, Capture capture)
        => new(new DbContextOptionsBuilder<TestContext>()
            .UseOceanBase("Server=offline;Database=dateonly_day_number", mode)
            .EnableServiceProviderCaching(false)
            .AddInterceptors(new NoNetwork(), capture).Options);

    private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
    {
        public DbSet<Row> Rows => Set<Row>();
    }

    private sealed class Row
    {
        public int Id { get; set; }
        public DateOnly Date { get; set; }
        public DateOnly? OptionalDate { get; set; }
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
