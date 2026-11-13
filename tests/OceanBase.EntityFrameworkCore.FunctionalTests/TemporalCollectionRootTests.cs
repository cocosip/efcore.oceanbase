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
using Microsoft.EntityFrameworkCore.Query;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.FunctionalTests;

public sealed class TemporalCollectionRootTests
{
    public static IEnumerable<object[]> ExplicitModes()
    {
        foreach (var mode in new[] { OceanBaseCompatibilityMode.MySql, OceanBaseCompatibilityMode.Oracle })
        foreach (var async in new[] { false, true })
        foreach (var translation in new[] { ParameterTranslationMode.Constant, ParameterTranslationMode.MultipleParameters })
            yield return new object[] { mode, async, translation };
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public Task DateTime_roots_bind_and_decode(OceanBaseCompatibilityMode mode, bool async)
    {
        DateTime?[] values = [new DateTime(2026, 11, 12, 1, 2, 3), null];
        return Run(mode, async, mode == OceanBaseCompatibilityMode.Oracle ? "TIMESTAMP" : "DATETIME", typeof(DateTime),
            context => context.Rows.SelectMany(row => values.Where(value => value != row.OccurredAt)),
            () => values = [], new DateTime(2026, 11, 13, 4, 5, 6));
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public Task DateTimeOffset_roots_bind_and_decode(OceanBaseCompatibilityMode mode, bool async)
    {
        DateTimeOffset?[] values = [new DateTimeOffset(2026, 11, 12, 1, 2, 3, TimeSpan.FromHours(8)), null];
        return Run(mode, async, mode == OceanBaseCompatibilityMode.Oracle ? "TIMESTAMP" : "DATETIME", typeof(DateTimeOffset),
            context => context.Rows.SelectMany(row => values.Where(value => value != row.OccurredAtOffset)),
            () => values = [], new DateTimeOffset(2026, 11, 13, 4, 5, 6,
                mode == OceanBaseCompatibilityMode.Oracle ? TimeSpan.FromHours(8) : TimeSpan.Zero));
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public Task DateOnly_roots_bind_and_decode(OceanBaseCompatibilityMode mode, bool async)
    {
        DateOnly?[] values = [new DateOnly(2026, 11, 12), null];
        return Run(mode, async, "DATE", typeof(DateOnly),
            context => context.Rows.SelectMany(row => values.Where(value => value != row.OccurredOn)),
            () => values = [], new DateOnly(2026, 11, 13));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task MySql_TimeOnly_roots_bind_and_decode(bool async)
    {
        TimeOnly?[] values = [new TimeOnly(1, 2, 3), null];
        return Run(OceanBaseCompatibilityMode.MySql, async, "TIME", typeof(TimeOnly),
            context => context.Rows.SelectMany(row => values.Where(value => value != row.OccurredTime)),
            () => values = [], new TimeOnly(4, 5, 6));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Oracle_TimeOnly_roots_bind_and_decode(bool async)
    {
        TimeOnly?[] values = [new TimeOnly(1, 2, 3), null];
        return Run(OceanBaseCompatibilityMode.Oracle, async, "INTERVAL DAY", typeof(OceanBaseIntervalDayToSecond),
            context => context.Rows.SelectMany(row => values.Where(value => value != row.OccurredTime)),
            () => values = [], new TimeOnly(4, 5, 6));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task MySql_TimeSpan_roots_bind_and_decode(bool async)
    {
        TimeSpan?[] values = [new TimeSpan(1, 2, 3), null];
        return Run(OceanBaseCompatibilityMode.MySql, async, "TIME", typeof(TimeSpan),
            context => context.Rows.SelectMany(row => values.Where(value => value != row.Duration)),
            () => values = [], new TimeSpan(4, 5, 6));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Oracle_TimeSpan_roots_bind_and_decode(bool async)
    {
        TimeSpan?[] values = [new TimeSpan(1, 2, 3), null];
        return Run(OceanBaseCompatibilityMode.Oracle, async, "INTERVAL DAY", typeof(OceanBaseIntervalDayToSecond),
            context => context.Rows.SelectMany(row => values.Where(value => value != row.Duration)),
            () => values = [], new TimeSpan(4, 5, 6));
    }

    [Theory]
    [MemberData(nameof(ExplicitModes))]
    public Task Explicit_DateTime_parameter_roots_rebind_and_decode(
        OceanBaseCompatibilityMode mode, bool async, ParameterTranslationMode translation)
    {
        DateTime?[] values = [new DateTime(2026, 11, 12, 1, 2, 3), null];
        return Run(mode, async, mode == OceanBaseCompatibilityMode.Oracle ? "TIMESTAMP" : "DATETIME", typeof(DateTime),
            context => context.Rows.Where(row => EF.Parameter(values).Contains(row.OccurredAt))
                .Select(row => row.OccurredAt), () => values = [], new DateTime(2026, 11, 13, 4, 5, 6), translation, false);
    }

    [Theory]
    [MemberData(nameof(ExplicitModes))]
    public Task Explicit_TimeSpan_parameter_roots_rebind_and_decode(
        OceanBaseCompatibilityMode mode, bool async, ParameterTranslationMode translation)
    {
        TimeSpan?[] values = [new TimeSpan(1, 2, 3), null];
        return Run(mode, async, mode == OceanBaseCompatibilityMode.Oracle ? "INTERVAL DAY" : "TIME",
            mode == OceanBaseCompatibilityMode.Oracle ? typeof(OceanBaseIntervalDayToSecond) : typeof(TimeSpan),
            context => context.Rows.Where(row => EF.Parameter(values).Contains(row.Duration))
                .Select(row => row.Duration), () => values = [], new TimeSpan(4, 5, 6), translation, false);
    }

    private static async Task Run<T>(
        OceanBaseCompatibilityMode mode, bool async, string castType, Type providerValueType,
        Func<TestContext, IQueryable<T>> queryFactory, Action empty, object expected,
        ParameterTranslationMode? translation = null, bool requireCast = true)
    {
        using var table = new DataTable();
        table.Columns.Add("Value", typeof(object));
        table.Rows.Add(providerValueType == typeof(OceanBaseIntervalDayToSecond)
            ? new OceanBaseIntervalDayToSecond(false, 0, 4, 5, 6, 0, 9, 6)
        : providerValueType == typeof(TimeOnly) || providerValueType == typeof(TimeSpan)
            ? new TimeSpan(4, 5, 6)
            : providerValueType == typeof(DateOnly)
                ? new DateTime(2026, 11, 13)
            : providerValueType == typeof(DateTimeOffset) && mode == OceanBaseCompatibilityMode.Oracle
                ? new DateTimeOffset(2026, 11, 13, 4, 5, 6, TimeSpan.FromHours(8))
                : new DateTime(2026, 11, 13, 4, 5, 6));
        table.Rows.Add(DBNull.Value);
        var capture = new Capture(table);
        await using var context = Create(mode, capture, translation);
        var query = queryFactory(context);

        var result = async ? await query.ToListAsync() : query.ToList();

        Assert.Equal(2, result.Count);
        Assert.Equal(expected, result[0]);
        Assert.Null(result[1]);
        if (requireCast)
            Assert.Contains(castType, Assert.Single(capture.Sql), StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(capture.Inputs);
        if (mode == OceanBaseCompatibilityMode.Oracle && providerValueType == typeof(OceanBaseIntervalDayToSecond))
        {
            Assert.All(capture.Inputs, parameter =>
            {
                Assert.Equal(DbType.Object, parameter.DbType);
                Assert.Equal(OceanBaseDbType.IntervalDayToSecond,
                    ((OceanBaseParameter)parameter).OceanBaseDbType);
            });
        }
        empty();
        capture.Sql.Clear();
        capture.Inputs.Clear();
        table.Rows.Clear();
        result = async ? await query.ToListAsync() : query.ToList();
        Assert.Empty(result);
        Assert.Empty(capture.Inputs);
        var emptySql = Assert.Single(capture.Sql);
        Assert.True(emptySql.Contains("WHERE 1 = 0", StringComparison.Ordinal)
            || emptySql.Contains("WHERE 0", StringComparison.Ordinal));
    }

    private static TestContext Create(OceanBaseCompatibilityMode mode, Capture capture,
        ParameterTranslationMode? translation = null)
    {
        var builder = new DbContextOptionsBuilder<TestContext>()
            .UseOceanBase("Server=offline;Database=temporal_roots", mode)
            .EnableServiceProviderCaching(false)
            .AddInterceptors(new NoNetwork(), capture);
        if (translation.HasValue)
        {
            var extension = builder.Options.Extensions.OfType<RelationalOptionsExtension>().Single()
                .WithUseParameterizedCollectionMode(translation.Value);
            ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(extension);
        }
        return new TestContext(builder.Options);
    }

    private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
    {
        public DbSet<Row> Rows => Set<Row>();
    }

    private sealed class Row
    {
        public int Id { get; set; }
        public DateTime? OccurredAt { get; set; }
        public DateTimeOffset? OccurredAtOffset { get; set; }
        public DateOnly? OccurredOn { get; set; }
        public TimeOnly? OccurredTime { get; set; }
        public TimeSpan? Duration { get; set; }
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
