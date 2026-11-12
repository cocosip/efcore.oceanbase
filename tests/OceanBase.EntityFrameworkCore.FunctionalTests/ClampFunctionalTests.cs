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

public sealed class ClampFunctionalTests
{
    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public Task Integer_clamp_binds_current_bounds_and_materializes(
        OceanBaseCompatibilityMode mode, bool async)
    {
        var lower = -10;
        var upper = 10;
        return Run(mode, async,
            context => context.Rows.Where(row => Math.Clamp(row.Integer, lower, upper) > 0)
                .Select(row => Math.Clamp(row.Integer, lower, upper)),
            () => (lower, upper) = (-2, 2),
            () => 2,
            typeof(int));
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public Task Decimal_clamp_preserves_decimal_reader_mapping(
        OceanBaseCompatibilityMode mode, bool async)
    {
        var lower = -10m;
        var upper = 10m;
        return Run(mode, async,
            context => context.Rows.Where(row => decimal.Clamp(row.Decimal, lower, upper) > 0m)
                .Select(row => decimal.Clamp(row.Decimal, lower, upper)),
            () => (lower, upper) = (-2m, 2m),
            () => 2m,
            typeof(decimal));
    }

    private static async Task Run<T>(OceanBaseCompatibilityMode mode, bool async,
        Func<TestContext, IQueryable<T>> queryFactory, Action changeBounds,
        Func<T> changedValue, Type providerType)
    {
        using var table = new DataTable();
        table.Columns.Add("Value", typeof(object));
        table.Rows.Add(providerType == typeof(decimal) ? 7.25m : 7);
        var capture = new Capture(table);
        await using var context = Create(mode, capture);
        var query = queryFactory(context);

        var result = async ? await query.ToListAsync() : query.ToList();

        Assert.Equal(providerType == typeof(decimal) ? (T)(object)7.25m : (T)(object)7, Assert.Single(result));
        Assert.Contains("CASE", Assert.Single(capture.Sql), StringComparison.Ordinal);
        Assert.Contains(capture.Inputs, parameter => parameter.Value is int or decimal);

        changeBounds();
        capture.Sql.Clear();
        capture.Inputs.Clear();
        table.Rows[0]["Value"] = changedValue()!;
        result = async ? await query.ToListAsync() : query.ToList();

        Assert.Equal(changedValue(), Assert.Single(result));
        Assert.Contains(capture.Inputs, parameter => parameter.Value is int or decimal);
        Assert.Contains("CASE", Assert.Single(capture.Sql), StringComparison.Ordinal);
    }

    private static TestContext Create(OceanBaseCompatibilityMode mode, Capture capture)
        => new(new DbContextOptionsBuilder<TestContext>()
            .UseOceanBase("Server=offline;Database=clamp", mode)
            .EnableServiceProviderCaching(false)
            .AddInterceptors(new NoNetwork(), capture).Options);

    private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
    {
        public DbSet<Row> Rows => Set<Row>();
    }

    private sealed class Row
    {
        public int Id { get; set; }
        public int Integer { get; set; }
        public decimal Decimal { get; set; }
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
