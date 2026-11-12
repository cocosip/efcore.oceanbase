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

public sealed class GuidCollectionRootTests
{
    private static readonly Guid Token = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public Task Guid_roots_bind_and_decode(OceanBaseCompatibilityMode mode, bool async)
    {
        Guid?[] values = [Token, null];
        return Run(mode, async,
            context => context.Rows.SelectMany(row => values.Where(value => value != row.Token)),
            () => values = []);
    }

    private static async Task Run(OceanBaseCompatibilityMode mode, bool async,
        Func<TestContext, IQueryable<Guid?>> queryFactory, Action empty)
    {
        using var table = new DataTable();
        table.Columns.Add("Value", typeof(object));
        table.Rows.Add(mode == OceanBaseCompatibilityMode.Oracle
            ? new byte[] { 0x33, 0x22, 0x11, 0, 0x55, 0x44, 0x77, 0x66, 0x88, 0x99, 0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0xff }
            : Token.ToString());
        table.Rows.Add(DBNull.Value);
        var capture = new Capture(table);
        await using var context = Create(mode, capture);
        var query = queryFactory(context);

        var result = async ? await query.ToListAsync() : query.ToList();

        Assert.Equal([Token, null], result);
        var sql = Assert.Single(capture.Sql);
        Assert.Contains(mode == OceanBaseCompatibilityMode.Oracle ? "RAW" : "CHAR", sql,
            StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(capture.Inputs);
        Assert.Contains(capture.Inputs, parameter => parameter.Value is string or byte[]);

        empty();
        capture.Sql.Clear();
        capture.Inputs.Clear();
        table.Rows.Clear();
        result = async ? await query.ToListAsync() : query.ToList();

        Assert.Empty(result);
        Assert.Empty(capture.Inputs);
        Assert.Contains("WHERE 1 = 0", Assert.Single(capture.Sql));
    }

    private static TestContext Create(OceanBaseCompatibilityMode mode, Capture capture)
        => new(new DbContextOptionsBuilder<TestContext>()
            .UseOceanBase("Server=offline;Database=guid_roots", mode)
            .EnableServiceProviderCaching(false)
            .AddInterceptors(new NoNetwork(), capture).Options);

    private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
    {
        public DbSet<Row> Rows => Set<Row>();
    }

    private sealed class Row
    {
        public int Id { get; set; }
        public Guid? Token { get; set; }
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
