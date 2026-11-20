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

public sealed class ScalarExtremaNumberFunctionalTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var mode in Enum.GetValues<OceanBaseCompatibilityMode>())
        foreach (var async in new[] { false, true })
            yield return [mode, async];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task MinNumber_and_MaxNumber_materialize_finite_values(
        OceanBaseCompatibilityMode mode, bool async)
    {
        using var table = new DataTable();
        table.Columns.Add("MinSingle", typeof(object));
        table.Columns.Add("MaxDouble", typeof(object));
        table.Rows.Add(1.25f, 3.5d);
        var capture = new Capture(table);
        await using var context = Create(mode, capture);

        var query = context.Rows.Select(row => new
        {
            MinSingle = float.MinNumber(row.Single, 1f),
            MaxDouble = double.MaxNumber(row.Double, 2d)
        });
        var result = async ? await query.ToListAsync() : query.ToList();

        var row = Assert.Single(result);
        Assert.Equal(1.25f, row.MinSingle);
        Assert.Equal(3.5d, row.MaxDouble);
        var sql = Assert.Single(capture.Sql);
        Assert.Contains("LEAST(", sql, StringComparison.Ordinal);
        Assert.Contains("GREATEST(", sql, StringComparison.Ordinal);
    }

    private static TestContext Create(OceanBaseCompatibilityMode mode, Capture capture)
        => new(new DbContextOptionsBuilder<TestContext>()
            .UseOceanBase("Server=offline;Database=scalar_extrema_number", mode)
            .AddInterceptors(new NoNetwork(), capture).Options);

    private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
    {
        public DbSet<Row> Rows => Set<Row>();
    }

    private sealed class Row
    {
        public int Id { get; set; }
        public float Single { get; set; }
        public double Double { get; set; }
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
