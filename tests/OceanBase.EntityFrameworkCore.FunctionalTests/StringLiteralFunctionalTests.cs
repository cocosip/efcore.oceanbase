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

public sealed class StringLiteralFunctionalTests
{
    private const string Text = "海🌊\\'\n";
    private const string OracleLiteral = @"u'\6d77\d83c\df0a\005c\0027\000a'";
    private const string MySqlLiteral = "CONVERT(X'E6B5B7F09F8C8A5C270A' USING utf8mb4)";

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task Inline_query_text_uses_the_mapping_literal_and_shapes_supplied_rows(
        OceanBaseCompatibilityMode mode, bool async)
    {
        using var table = new DataTable();
        table.Columns.Add("Name", typeof(string));
        table.Rows.Add(Text);
        var capture = new Capture(table);
        await using var context = Create(mode, capture);
        var query = context.Set<Row>().Where(row => row.Name == Text).Select(row => row.Name);
        Assert.Equal(Text, Assert.Single(async ? await query.ToListAsync() : query.ToList()));
        Assert.Contains(mode == OceanBaseCompatibilityMode.Oracle ? OracleLiteral : MySqlLiteral,
            Assert.Single(capture.Sql));
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql)]
    [InlineData(OceanBaseCompatibilityMode.Oracle)]
    public void Default_and_model_data_share_the_query_literal_contract(OceanBaseCompatibilityMode mode)
    {
        using var table = new DataTable();
        var capture = new Capture(table);
        using var context = Create(mode, capture);
        var sql = context.Database.GenerateCreateScript();
        var literal = mode == OceanBaseCompatibilityMode.Oracle ? OracleLiteral : MySqlLiteral;
        Assert.Contains(mode == OceanBaseCompatibilityMode.Oracle ? "DEFAULT " + literal : "DEFAULT (" + literal + ")", sql);
        Assert.Contains("INSERT INTO", sql);
        Assert.Contains(literal, sql[(sql.IndexOf("INSERT INTO", System.StringComparison.Ordinal))..]);
        Assert.Empty(capture.Sql);
    }

    private static TestContext Create(OceanBaseCompatibilityMode mode, Capture capture)
        => new(new DbContextOptionsBuilder<TestContext>()
            .UseOceanBase("Server=offline;Database=string_literals", mode)
            .AddInterceptors(new NoNetwork(), capture).Options);

    private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model)
        {
            var row = model.Entity<Row>();
            row.Property(r => r.Id).ValueGeneratedNever();
            row.Property(r => r.Name).HasMaxLength(40).HasDefaultValue(Text);
            row.HasData(new Row { Id = 1, Name = Text });
        }
    }

    private sealed class Row
    {
        public int Id { get; set; }
        public string? Name { get; set; }
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
            Assert.Empty(command.Parameters);
            Sql.Add(command.CommandText);
            return InterceptionResult<DbDataReader>.SuppressWithResult(table.CreateDataReader());
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(ReaderExecuting(command, eventData, result));
    }
}
