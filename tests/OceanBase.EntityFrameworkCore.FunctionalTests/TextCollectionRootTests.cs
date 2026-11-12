using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.FunctionalTests;

public sealed class TextCollectionRootTests
{
    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task Native_text_roots_bind_current_values_and_read_nullable_rows(
        OceanBaseCompatibilityMode mode, bool async)
    {
        using var table = new DataTable();
        table.Columns.Add("Value", typeof(object));
        var capture = new Capture(table);
        await using var context = Create(mode, capture);
        string?[] values = [];
        var query = context.Rows.SelectMany(row => values.Where(value => value != row.Text));

        foreach (var input in new string?[][] { ["one", null, "two"], [], ["again", "again"] })
        {
            values = input;
            capture.Sql.Clear();
            capture.Inputs.Clear();
            table.Rows.Clear();
            if (input.Length != 0)
            {
                table.Rows.Add("reader");
                table.Rows.Add(DBNull.Value);
            }

            var result = async ? await query.ToListAsync() : query.ToList();
            Assert.Equal(input.Length == 0 ? [] : new string?[] { "reader", null }, result);
            Assert.Equal(input, values);
            var sql = Assert.Single(capture.Sql);
            Assert.DoesNotContain("UNION ALL VALUES", sql);
            Assert.Equal(input.Length == 0, sql.Contains("WHERE 1 = 0", StringComparison.Ordinal));
            Assert.Equal(Math.Max(0, input.Length - 1), sql.Split("UNION ALL SELECT").Length - 1);
            Assert.All(capture.Inputs, parameter => Assert.Equal(DbType.String, parameter.DbType));
            Assert.All(capture.Inputs, parameter => Assert.Contains(parameter.Value, input.Where(value => value is not null)));
            if (input.Length == 0) Assert.Empty(capture.Inputs);
        }
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false, ParameterTranslationMode.Constant)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true, ParameterTranslationMode.Constant)]
    [InlineData(OceanBaseCompatibilityMode.MySql, false, ParameterTranslationMode.MultipleParameters)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true, ParameterTranslationMode.MultipleParameters)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false, ParameterTranslationMode.Constant)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true, ParameterTranslationMode.Constant)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false, ParameterTranslationMode.MultipleParameters)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true, ParameterTranslationMode.MultipleParameters)]
    public async Task Explicit_parameter_text_roots_bind_current_values_and_read_nullable_rows(
        OceanBaseCompatibilityMode mode, bool async, ParameterTranslationMode translation)
    {
        using var table = new DataTable();
        table.Columns.Add("Text", typeof(object));
        var capture = new Capture(table);
        await using var context = CreateExplicit(mode, capture, translation);
        string?[] values = ["one", null, "two"];
        var query = context.Rows.Where(row => EF.Parameter(values).Contains(row.Text)).Select(row => row.Text);

        foreach (var input in new string?[][] { values, ["one"], ["", null], [] })
        {
            values = input;
            capture.Sql.Clear();
            capture.Inputs.Clear();
            table.Rows.Clear();
            if (input.Length != 0)
            {
                table.Rows.Add("reader");
                table.Rows.Add(DBNull.Value);
            }

            var result = async ? await query.ToListAsync() : query.ToList();
            Assert.Equal(input.Length == 0 ? [] : new string?[] { "reader", null }, result);
            var sql = Assert.Single(capture.Sql);
            Assert.DoesNotContain("IN ()", sql);
            Assert.Equal(input.Length == 0, sql.Contains("WHERE 1 = 0", StringComparison.Ordinal)
                || sql.Contains("WHERE 0", StringComparison.Ordinal));
            Assert.All(capture.Inputs, parameter => Assert.Equal(DbType.String, parameter.DbType));
        }
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false, ParameterTranslationMode.Constant)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true, ParameterTranslationMode.Constant)]
    [InlineData(OceanBaseCompatibilityMode.MySql, false, ParameterTranslationMode.MultipleParameters)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true, ParameterTranslationMode.MultipleParameters)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false, ParameterTranslationMode.Constant)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true, ParameterTranslationMode.Constant)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false, ParameterTranslationMode.MultipleParameters)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true, ParameterTranslationMode.MultipleParameters)]
    public async Task Explicit_parameter_converted_text_roots_bind_provider_values(
        OceanBaseCompatibilityMode mode, bool async, ParameterTranslationMode translation)
    {
        using var table = new DataTable();
        table.Columns.Add("Text", typeof(object));
        table.Rows.Add("reader!");
        var capture = new Capture(table);
        await using var context = CreateExplicit(mode, capture, translation);
        List<string?> values = ["one", "two"];
        var query = context.ConvertedRows
            .Where(row => EF.Parameter(values).Contains(row.Text)).Select(row => row.Text);

        var result = async ? await query.ToListAsync() : query.ToList();
        Assert.Equal(new[] { "reader" }, result);
        Assert.Equal(new object?[] { "one!", "two!" }, capture.Inputs.Select(parameter => parameter.Value));
        Assert.All(capture.Inputs, parameter => Assert.Equal(DbType.String, parameter.DbType));
        Assert.DoesNotContain("IN ()", Assert.Single(capture.Sql));
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task Converted_text_roots_bind_provider_values_and_decode_reader_values(
        OceanBaseCompatibilityMode mode, bool async)
    {
        using var table = new DataTable();
        table.Columns.Add("Value", typeof(object));
        table.Rows.Add("reader!");
        var capture = new Capture(table);
        await using var context = Create(mode, capture);
        string?[] values = ["one", "two"];
        var query = context.ConvertedRows.SelectMany(row => values.Where(value => value != row.Text));

        var result = async ? await query.ToListAsync() : query.ToList();
        Assert.Equal(new[] { "reader" }, result);
        Assert.Equal(new object?[] { "one!", "two!" }, capture.Inputs.Select(parameter => parameter.Value));
        Assert.All(capture.Inputs, parameter => Assert.Equal(DbType.String, parameter.DbType));
        Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "AS CHAR" : "AS NVARCHAR2", Assert.Single(capture.Sql));

        values = [];
        capture.Sql.Clear();
        capture.Inputs.Clear();
        table.Rows.Clear();
        var empty = async ? await query.ToListAsync() : query.ToList();
        Assert.Empty(empty);
        Assert.Empty(capture.Inputs);
        Assert.Contains("WHERE 1 = 0", Assert.Single(capture.Sql));
    }

    private static TestContext Create(OceanBaseCompatibilityMode mode, Capture capture)
        => new(Options(mode, capture));

    private static TestContext CreateExplicit(OceanBaseCompatibilityMode mode, Capture capture,
        ParameterTranslationMode translation)
        => new(Options(mode, capture, translation));

    private static DbContextOptions<TestContext> Options(OceanBaseCompatibilityMode mode, Capture capture,
        ParameterTranslationMode? collectionMode = null)
    {
        var builder = new DbContextOptionsBuilder<TestContext>().UseOceanBase("Server=offline;Database=text_roots", mode)
            .EnableServiceProviderCaching(false).AddInterceptors(new NoNetwork(), capture);
        if (collectionMode is { } configured)
        {
            var extension = builder.Options.Extensions.OfType<RelationalOptionsExtension>().Single()
                .WithUseParameterizedCollectionMode(configured);
            ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(extension);
        }
        return builder.Options;
    }

    private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
    {
        public DbSet<Row> Rows => Set<Row>();
        public DbSet<ConvertedRow> ConvertedRows => Set<ConvertedRow>();

        protected override void OnModelCreating(ModelBuilder builder)
            => builder.Entity<ConvertedRow>().Property(row => row.Text)
                .HasConversion(value => value + "!", value => value.Substring(0, value.Length - 1));
    }

    private sealed class Row
    {
        public int Id { get; set; }
        public string? Text { get; set; }
    }

    private sealed class ConvertedRow
    {
        public int Id { get; set; }
        public string? Text { get; set; }
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
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(ReaderExecuting(command, eventData, result));
    }
}
