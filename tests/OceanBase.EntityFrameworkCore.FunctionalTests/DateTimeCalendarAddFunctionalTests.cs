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
