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
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.FunctionalTests;

public sealed class BooleanCollectionRootTests
{
    public static IEnumerable<object[]> ExplicitModes()
    {
        foreach (var mode in new[] { OceanBaseCompatibilityMode.MySql, OceanBaseCompatibilityMode.Oracle })
        foreach (var async in new[] { false, true })
        foreach (var relationalNulls in new[] { false, true })
        foreach (var translation in new[] { ParameterTranslationMode.Constant, ParameterTranslationMode.MultipleParameters })
            yield return new object[] { mode, async, relationalNulls, translation };
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, false, true)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true, true)]
    public async Task Native_boolean_roots_bind_current_values_and_read_nullable_rows(
        OceanBaseCompatibilityMode mode, bool async, bool relationalNulls)
    {
        using var table = new DataTable();
        table.Columns.Add("Value", typeof(object));
        var capture = new Capture(table);
        await using var context = Create<NativeRow>(mode, capture, relationalNulls);
        bool?[] values = [];
        var query = context.Rows.SelectMany(row => values.Where(value => value != row.Optional));

        foreach (var input in new bool?[][] { [true, null, false, true], [], [false, false], [] })
        {
            values = input;
            capture.Inputs.Clear();
            capture.Sql.Clear();
            table.Rows.Clear();
            if (input.Length != 0)
            {
                table.Rows.Add(mode == OceanBaseCompatibilityMode.Oracle ? new OceanBaseDecimal(1m) : true);
                table.Rows.Add(mode == OceanBaseCompatibilityMode.Oracle ? new OceanBaseDecimal(0m) : false);
                table.Rows.Add(DBNull.Value);
            }

            var result = async ? await query.ToListAsync() : query.ToList();
            Assert.Equal(input.Length == 0 ? [] : new bool?[] { true, false, null }, result);
            var sql = Assert.Single(capture.Sql);
            Assert.Equal(input.Length == 0, sql.Contains("WHERE 1 = 0", StringComparison.Ordinal));
            Assert.Equal(Math.Max(0, input.Length - 1), sql.Split("UNION ALL SELECT").Length - 1);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? " AS SIGNED)" : " AS NUMBER)", sql);
            Assert.DoesNotContain("UNION ALL VALUES", sql);
            Assert.All(capture.Inputs, parameter => Assert.Equal(
                mode == OceanBaseCompatibilityMode.MySql ? DbType.Boolean : DbType.Decimal, parameter.DbType));
            var expected = input.Where(value => value.HasValue).Select(value => (object)(mode == OceanBaseCompatibilityMode.MySql
                ? value.GetValueOrDefault() : value.GetValueOrDefault() ? 1m : 0m));
            Assert.Equal(expected, capture.Inputs.Where(parameter => parameter.Value is not DBNull)
                .Select(parameter => parameter.Value));
            if (input.Length == 0) Assert.Empty(capture.Inputs);
        }
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task Converted_boolean_roots_keep_provider_values_and_decode_reader_values(
        OceanBaseCompatibilityMode mode, bool async)
    {
        using var table = new DataTable();
        table.Columns.Add("Value", typeof(object));
        table.Rows.Add(new OceanBaseDecimal(1m));
        table.Rows.Add(new OceanBaseDecimal(0m));
        table.Rows.Add(DBNull.Value);
        var capture = new Capture(table);
        await using var context = Create<ConvertedRow>(mode, capture);
        bool?[] values = [true, false, true];
        var query = context.ConvertedRows.SelectMany(row => values.Where(value => value != row.Flag));

        var result = async ? await query.ToListAsync() : query.ToList();
        Assert.Equal(new bool?[] { true, false, null }, result);
        Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? " AS SIGNED)" : " AS NUMBER)",
            Assert.Single(capture.Sql));
        Assert.Equal(mode == OceanBaseCompatibilityMode.MySql ? new object?[] { true, false, true }
            : new object?[] { 1m, 0m, 1m }, capture.Inputs.Select(parameter => parameter.Value));
        Assert.All(capture.Inputs, parameter => Assert.Equal(
            mode == OceanBaseCompatibilityMode.MySql ? DbType.Boolean : DbType.Decimal, parameter.DbType));

        values = [];
        capture.Inputs.Clear();
        capture.Sql.Clear();
        table.Rows.Clear();
        var empty = async ? await query.ToListAsync() : query.ToList();
        Assert.Empty(empty);
        Assert.Empty(capture.Inputs);
        Assert.Contains("WHERE 1 = 0", Assert.Single(capture.Sql));
    }

    [Theory]
    [MemberData(nameof(ExplicitModes))]
    public async Task Explicit_parameter_boolean_roots_rebind_and_bind_current_values(
        OceanBaseCompatibilityMode mode, bool async, bool relationalNulls, ParameterTranslationMode translation)
    {
        using var table = new DataTable();
        table.Columns.Add("Value", typeof(object));
        table.Rows.Add(true);
        table.Rows.Add(false);
        table.Rows.Add(DBNull.Value);
        var capture = new Capture(table);
        await using var context = Create<NativeRow>(mode, capture, relationalNulls, translation);
        bool?[] values = [true, null, false, true];
        var query = context.Rows.Where(row => EF.Parameter(values).Contains(row.Optional))
            .Select(row => row.Optional);

        foreach (var input in new bool?[][] { values, [null], [] })
        {
            values = input;
            capture.Inputs.Clear();
            capture.Sql.Clear();
            var result = async ? await query.ToListAsync() : query.ToList();
            Assert.Equal(new bool?[] { true, false, null }, result);
            var sql = Assert.Single(capture.Sql);
            Assert.DoesNotContain("JSON", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(input.Length == 0, sql.Contains("WHERE 1 = 0", StringComparison.Ordinal)
                || sql.Contains("WHERE 0", StringComparison.Ordinal));
            Assert.All(capture.Inputs, parameter => Assert.Equal(
                mode == OceanBaseCompatibilityMode.MySql ? DbType.Boolean : DbType.Decimal, parameter.DbType));
            Assert.All(capture.Inputs, parameter => Assert.True(
                parameter.Value is DBNull || input.Contains(parameter.Value is bool value ? value : null)));
        }

        using var convertedTable = new DataTable();
        convertedTable.Columns.Add("Value", typeof(object));
        convertedTable.Rows.Add(new OceanBaseDecimal(1m));
        convertedTable.Rows.Add(new OceanBaseDecimal(0m));
        convertedTable.Rows.Add(DBNull.Value);
        var convertedCapture = new Capture(convertedTable);
        await using var convertedContext = Create<ConvertedRow>(mode, convertedCapture, relationalNulls, translation);
        bool?[] convertedValues = [true, null, false];
        var convertedQuery = convertedContext.ConvertedRows
            .Where(row => EF.Parameter(convertedValues).Contains(row.Flag))
            .Select(row => row.Flag);
        var convertedResult = async ? await convertedQuery.ToListAsync() : convertedQuery.ToList();
        Assert.Equal(new bool?[] { true, false, null }, convertedResult);
        Assert.All(convertedCapture.Inputs, parameter => Assert.Equal(
            mode == OceanBaseCompatibilityMode.MySql ? DbType.Boolean : DbType.Decimal, parameter.DbType));
    }

    private static TestContext<T> Create<T>(OceanBaseCompatibilityMode mode, Capture capture,
        bool relationalNulls = false, ParameterTranslationMode? translation = null) where T : class
    {
        var builder = new DbContextOptionsBuilder<TestContext<T>>()
            .UseOceanBase("Server=offline;Database=boolean_roots", mode)
            .EnableServiceProviderCaching(false)
            .AddInterceptors(new NoNetwork(), capture);
        var extension = builder.Options.Extensions.OfType<RelationalOptionsExtension>().Single()
            .WithUseRelationalNulls(relationalNulls);
        if (translation.HasValue) extension = extension.WithUseParameterizedCollectionMode(translation.Value);
        ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(extension);
        return new TestContext<T>(builder.Options);
    }

    private sealed class TestContext<T>(DbContextOptions<TestContext<T>> options) : DbContext(options) where T : class
    {
        public DbSet<NativeRow> Rows => Set<NativeRow>();
        public DbSet<ConvertedRow> ConvertedRows => Set<ConvertedRow>();

        protected override void OnModelCreating(ModelBuilder builder)
            => builder.Entity<ConvertedRow>().Property(row => row.Flag).HasConversion(
                new ValueConverter<bool?, int?>(
                    value => value.HasValue ? (int?)(value.Value ? 1 : 0) : null,
                    value => value.HasValue && value.Value != 0));
    }

    private sealed class NativeRow
    {
        public int Id { get; set; }
        public bool? Optional { get; set; }
    }

    private sealed class ConvertedRow
    {
        public int Id { get; set; }
        public bool? Flag { get; set; }
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
