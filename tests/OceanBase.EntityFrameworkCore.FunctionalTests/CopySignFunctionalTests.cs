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

public sealed class CopySignFunctionalTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var mode in Enum.GetValues<OceanBaseCompatibilityMode>())
        foreach (var async in new[] { false, true })
            yield return [mode, async];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task CopySign_sync_and_async_materialization_keeps_sql_shape(
        OceanBaseCompatibilityMode mode, bool async)
    {
        using var table = new DataTable();
        table.Columns.Add("Value", typeof(object));
        table.Rows.Add(2.5d);
        table.Rows.Add(DBNull.Value);
        var capture = new Capture(table);
        await using var context = Create(mode, capture);
        var query = context.Rows
            .Where(row => Math.CopySign(row.Input!.Value, row.Sign!.Value) > 0)
            .Select(row => (double?)Math.CopySign(row.Input!.Value, row.Sign!.Value));

        var result = async ? await query.ToListAsync() : query.ToList();

        Assert.Equal(new double?[] { 2.5d, null }, result);
        var sql = Assert.Single(capture.Sql);
        Assert.Contains("ABS(", sql);
        Assert.Contains("CASE", sql);
        Assert.Contains("<", sql);
        Assert.Contains("IS NULL", sql);
    }

    private static TestContext Create(OceanBaseCompatibilityMode mode, Capture capture)
        => new(new DbContextOptionsBuilder<TestContext>()
            .UseOceanBase("Server=offline;Database=copy_sign", mode)
            .AddInterceptors(new NoNetwork(), capture).Options);

    private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
    {
        public DbSet<Row> Rows => Set<Row>();
    }

    private sealed class Row
    {
        public int Id { get; set; }
        public double? Input { get; set; }
        public double? Sign { get; set; }
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
