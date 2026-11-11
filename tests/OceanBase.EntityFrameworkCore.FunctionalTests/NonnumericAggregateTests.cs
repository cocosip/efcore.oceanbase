using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.FunctionalTests;

public sealed class NonnumericAggregateTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var mode in new[] { OceanBaseCompatibilityMode.MySql, OceanBaseCompatibilityMode.Oracle })
        foreach (var async in new[] { false, true })
        foreach (var type in new[] { typeof(string), typeof(DateTime), typeof(DateTimeOffset), typeof(DateOnly), typeof(TimeOnly), typeof(TimeSpan) })
        {
            yield return [mode, async, type];
            if (type.IsValueType) yield return [mode, async, typeof(Nullable<>).MakeGenericType(type)];
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Native_extrema_use_current_bindings_original_readers_and_inherited_empty_shaping(
        OceanBaseCompatibilityMode mode, bool async, Type type)
        => await (Task)typeof(NonnumericAggregateTests).GetMethod(nameof(CheckNative), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(type).Invoke(null, [mode, async])!;

    private static async Task CheckNative<T>(OceanBaseCompatibilityMode mode, bool async)
    {
        using var table = new DataTable();
        table.Columns.Add("Value", typeof(object));
        var capture = new Capture(table);
        await using var context = new NativeContext<T>(Options<NativeContext<T>>(mode, capture));
        var sample = Sample(Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T), mode);
        table.Rows.Add(sample.Provider);
        var threshold = 2;
        var query = context.Rows.Where(row => row.Id > threshold).Select(row => row.Value);
        foreach (var current in new[] { 2, 7 })
        foreach (var minimum in new[] { true, false })
        {
            threshold = current;
            capture.Sql.Clear(); capture.Inputs.Clear();
            var result = minimum ? async ? await query.MinAsync() : query.Min()
                : async ? await query.MaxAsync() : query.Max();
            Assert.Equal((T)sample.Value, result);
            Assert.Equal(current, Assert.Single(capture.Inputs).Value);
            Assert.Contains(minimum ? "MIN(" : "MAX(", Assert.Single(capture.Sql));
            Assert.DoesNotContain("COALESCE", capture.Sql[0]);
            Assert.DoesNotContain("CAST(", capture.Sql[0]);
        }

        table.Rows.Clear(); table.Rows.Add(DBNull.Value);
        foreach (var minimum in new[] { true, false })
        {
            if (default(T) is null)
            {
                var result = minimum ? async ? await query.MinAsync() : query.Min()
                    : async ? await query.MaxAsync() : query.Max();
                Assert.Null(result);
            }
            else if (async)
            {
                if (minimum) await Assert.ThrowsAsync<InvalidOperationException>(() => query.MinAsync());
                else await Assert.ThrowsAsync<InvalidOperationException>(() => query.MaxAsync());
            }
            else if (minimum) Assert.Throws<InvalidOperationException>(() => query.Min());
            else Assert.Throws<InvalidOperationException>(() => query.Max());
        }
        table.Rows.Clear();
        var grouped = context.Rows.GroupBy(row => row.Key).Select(group => group.Min(row => row.Value));
        Assert.Empty(async ? await grouped.ToListAsync() : grouped.ToList());
    }

    private static (object Value, object Provider) Sample(Type type, OceanBaseCompatibilityMode mode)
    {
        var oracle = mode == OceanBaseCompatibilityMode.Oracle;
        var stamp = new DateTime(2026, 11, 11, 12, 30, 45).AddTicks(1234560);
        if (type == typeof(string)) return ("hé字🛰", "hé字🛰");
        if (type == typeof(DateTime)) return (stamp, oracle ? new OceanBaseTimestamp(stamp) : stamp);
        if (type == typeof(DateOnly)) return (DateOnly.FromDateTime(stamp), stamp.Date);
        if (type == typeof(DateTimeOffset))
        {
            var zoned = new DateTimeOffset(stamp, TimeSpan.FromHours(8));
            return oracle ? (zoned, new OceanBaseTimestampWithTimeZone(zoned))
                : (zoned.ToUniversalTime(), DateTime.SpecifyKind(zoned.UtcDateTime, DateTimeKind.Unspecified));
        }
        var clock = new TimeSpan(0, 12, 30, 45).Add(TimeSpan.FromTicks(1234560));
        if (type == typeof(TimeOnly)) return (TimeOnly.FromTimeSpan(clock),
            oracle ? new OceanBaseIntervalDayToSecond(false, 0, 12, 30, 45, 123456000, scale: 9) : clock);
        var duration = -(new TimeSpan(2, 3, 4, 5) + TimeSpan.FromTicks(1234560));
        return (duration, oracle ? new OceanBaseIntervalDayToSecond(true, 2, 3, 4, 5, 123456000, scale: 9) : duration);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task Grouped_converted_extrema_decode_once_and_keep_current_having_filter_parameters(OceanBaseCompatibilityMode mode, bool async)
    {
        using var table = new DataTable();
        foreach (var name in new[] { "MinText", "MaxText", "MinStamp", "MaxStamp" }) table.Columns.Add(name, typeof(object));
        var stamp = new DateTime(2026, 11, 11, 12, 30, 45).AddTicks(1234567);
        table.Rows.Add("x" + new string('a', 16), "x" + new string('z', 16), mode == OceanBaseCompatibilityMode.Oracle ? new OceanBaseTimestamp(stamp.AddDays(1)) : stamp.AddDays(1),
            mode == OceanBaseCompatibilityMode.Oracle ? new OceanBaseTimestamp(stamp.AddDays(2)) : stamp.AddDays(2));
        var capture = new Capture(table);
        await using var context = new ConvertedContext(Options<ConvertedContext>(mode, capture));
        var needle = "left";
        var threshold = new DateTime(2000, 1, 1);
        var query = context.Rows.Where(row => row.Text != needle).GroupBy(row => row.Key)
            .Where(group => group.Max(row => row.Stamp) > threshold).OrderBy(group => group.Min(row => row.Text))
            .Select(group => new { MinText = group.Min(row => row.Text), MaxText = group.Max(row => row.Text),
                MinStamp = group.Min(row => row.Stamp), MaxStamp = group.Max(row => row.Stamp) });
        foreach (var current in new[] { "left", "right" })
        {
            needle = current;
            threshold = threshold.AddDays(3);
            capture.Sql.Clear(); capture.Inputs.Clear();
            var result = Assert.Single(async ? await query.ToListAsync() : query.ToList());
            Assert.Equal(new string('a', 16), result.MinText); Assert.Equal(new string('z', 16), result.MaxText);
            Assert.Equal(stamp, result.MinStamp); Assert.Equal(stamp.AddDays(1), result.MaxStamp);
            Assert.Contains(capture.Inputs, parameter => Equals(parameter.Value, "x" + current));
            Assert.Contains(capture.Inputs, parameter => Equals(parameter.Value, threshold.AddDays(1)));
            Assert.Contains("HAVING", Assert.Single(capture.Sql)); Assert.Contains("ORDER BY", capture.Sql[0]);
        }
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task Unsupported_extremum_carriers_and_null_converters_fail_before_commands(OceanBaseCompatibilityMode mode, bool async)
    {
        using var table = new DataTable(); table.Columns.Add("Value", typeof(object));
        var capture = new Capture(table);
        await using var context = new InvalidContext(Options<InvalidContext>(mode, capture), mode);
        if (async)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.Rows.MinAsync(row => row.Text));
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.Rows.MaxAsync(row => row.Stamp));
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.Rows.MinAsync(row => row.Numeric));
            if (mode == OceanBaseCompatibilityMode.Oracle) await Assert.ThrowsAsync<InvalidOperationException>(() => context.Rows.MaxAsync(row => row.Lob));
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => context.Rows.Min(row => row.Text));
            Assert.Throws<InvalidOperationException>(() => context.Rows.Max(row => row.Stamp));
            Assert.Throws<InvalidOperationException>(() => context.Rows.Min(row => row.Numeric));
            if (mode == OceanBaseCompatibilityMode.Oracle) Assert.Throws<InvalidOperationException>(() => context.Rows.Max(row => row.Lob));
        }
        Assert.Empty(capture.Sql);
    }

    private static DbContextOptions<T> Options<T>(OceanBaseCompatibilityMode mode, Capture capture) where T : DbContext
        => new DbContextOptionsBuilder<T>().UseOceanBase("Server=offline;Database=extrema", mode).EnableServiceProviderCaching(false)
            .AddInterceptors(new NoNetwork(), capture).Options;

    private sealed class NativeContext<T>(DbContextOptions<NativeContext<T>> options) : DbContext(options)
    {
        public DbSet<Row<T>> Rows => Set<Row<T>>();
    }
    private sealed class Row<T>
    {
        public int Id { get; set; }
        public int Key { get; set; }
        public T Value { get; set; } = default!;
    }
    private sealed class ConvertedContext(DbContextOptions<ConvertedContext> options) : DbContext(options)
    {
        public DbSet<ConvertedRow> Rows => Set<ConvertedRow>();
        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<ConvertedRow>().Property(row => row.Text).HasMaxLength(8).HasConversion(value => "x" + value, value => value.Substring(1));
            builder.Entity<ConvertedRow>().Property(row => row.Stamp).HasPrecision(3).HasConversion(new ValueConverter<DateTime?, DateTime?>(
                value => value.HasValue ? value.Value.AddDays(1) : null, value => value.HasValue ? value.Value.AddDays(-1) : null));
        }
    }
    private sealed class ConvertedRow
    {
        public int Id { get; set; }
        public int Key { get; set; }
        public string? Text { get; set; }
        public DateTime? Stamp { get; set; }
    }
    private sealed class InvalidContext(DbContextOptions<InvalidContext> options, OceanBaseCompatibilityMode mode) : DbContext(options)
    {
        public DbSet<InvalidRow> Rows => Set<InvalidRow>();
        protected override void OnModelCreating(ModelBuilder builder)
        {
#pragma warning disable EF1001
            builder.Entity<InvalidRow>().Property(row => row.Text).HasConversion(new ValueConverter<string, string>(value => value ?? "missing", value => value, convertsNulls: true));
            builder.Entity<InvalidRow>().Property(row => row.Stamp).HasConversion(new ValueConverter<DateTime, DateTime>(value => value, value => value, convertsNulls: true));
#pragma warning restore EF1001
            builder.Entity<InvalidRow>().Property(row => row.Numeric).HasConversion(value => int.Parse(value), value => value.ToString());
            builder.Entity<InvalidRow>().Property(row => row.Lob).HasColumnType(mode == OceanBaseCompatibilityMode.Oracle ? "CLOB" : "LONGTEXT");
        }
    }
    private sealed class InvalidRow
    {
        public int Id { get; set; }
        public string Text { get; set; } = "";
        public DateTime Stamp { get; set; }
        public string Numeric { get; set; } = "";
        public string Lob { get; set; } = "";
    }
    private sealed class NoNetwork : DbConnectionInterceptor
    {
        public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
            => InterceptionResult.Suppress();
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default) => ValueTask.FromResult(InterceptionResult.Suppress());
    }
    private sealed class Capture(DataTable table) : DbCommandInterceptor
    {
        public List<string> Sql { get; } = [];
        public List<DbParameter> Inputs { get; } = [];
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Sql.Add(command.CommandText); Inputs.AddRange(command.Parameters.Cast<DbParameter>());
            return InterceptionResult<DbDataReader>.SuppressWithResult(table.CreateDataReader());
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) => ValueTask.FromResult(ReaderExecuting(command, eventData, result));
    }
}
