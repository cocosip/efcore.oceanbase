using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.FunctionalTests;

public sealed class BooleanConversionTests
{
    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task Convert_to_boolean_binds_numeric_and_converted_results(
        OceanBaseCompatibilityMode mode, bool async)
    {
        using var table = new DataTable();
        table.Columns.Add("Numeric", typeof(object));
        table.Columns.Add("Nullable", typeof(object));
        table.Columns.Add("Converted", typeof(object));
        table.Rows.Add(
            mode == OceanBaseCompatibilityMode.Oracle ? new OceanBaseDecimal(1m) : true,
            DBNull.Value,
            mode == OceanBaseCompatibilityMode.Oracle ? new OceanBaseDecimal(0m) : false);
        var capture = new Capture(table);
        await using var context = Create(mode, capture);

        var query = context.Rows
            .Where(row => Convert.ToBoolean(row.Raw))
            .Select(row => new
            {
                Numeric = Convert.ToBoolean(row.Raw),
                Nullable = Convert.ToBoolean(row.OptionalRaw),
                Converted = Convert.ToBoolean(row.Converted)
            });

        var result = Assert.Single(async ? await query.ToListAsync() : query.ToList());
        Assert.True(result.Numeric);
        Assert.False(result.Nullable);
        Assert.False(result.Converted);
        var sql = Assert.Single(capture.Sql);
        Assert.Contains("<> 0", sql);
        Assert.Contains("CASE", sql);
        Assert.Contains(mode == OceanBaseCompatibilityMode.Oracle ? "N'Y'" : "'Y'", sql);
    }

    private static TestContext Create(OceanBaseCompatibilityMode mode, Capture capture)
        => new(new DbContextOptionsBuilder<TestContext>()
            .UseOceanBase("Server=offline;Database=boolean_conversion", mode)
            .EnableServiceProviderCaching(false)
            .AddInterceptors(new NoNetwork(), capture).Options);

    private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
    {
        public DbSet<Row> Rows => Set<Row>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Row>().Property(row => row.Converted)
                .HasConversion(new BoolToStringConverter("N", "Y"));
    }

    private sealed class Row
    {
        public int Id { get; set; }
        public int Raw { get; set; }
        public int? OptionalRaw { get; set; }
        public bool Converted { get; set; }
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
