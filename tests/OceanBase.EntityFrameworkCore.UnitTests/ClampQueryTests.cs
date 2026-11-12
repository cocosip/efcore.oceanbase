using System;
using System.Linq.Expressions;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests;

public sealed class ClampQueryTests
{
    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql)]
    [InlineData(OceanBaseCompatibilityMode.Oracle)]
    public void Clamp_translates_numeric_overloads_with_provider_case(
        OceanBaseCompatibilityMode mode)
    {
        using var context = CreateContext(mode);
        var sql = context.Rows.Where(row =>
            Math.Clamp(row.Integer, -10, 10) > -11
            && decimal.Clamp(row.Decimal, -10m, 10m) > -11m
            && Math.Clamp(row.Double, -10d, 10d) > -11d
            && Math.Clamp(row.Single, -10f, 10f) > -11f).ToQueryString();

        Assert.Contains("CASE", sql, StringComparison.Ordinal);
        Assert.Equal(8, sql.Split("WHEN", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("GREATEST", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LEAST", sql, StringComparison.OrdinalIgnoreCase);

        var converted = context.Rows.Where(row => Math.Clamp(row.Converted, -10, 10) > 0).ToQueryString();
        Assert.Contains("CASE", converted, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql)]
    [InlineData(OceanBaseCompatibilityMode.Oracle)]
    public void Every_supported_clamp_overload_translates_in_a_predicate(OceanBaseCompatibilityMode mode)
    {
        using var context = CreateContext(mode);
        var row = Expression.Parameter(typeof(Row), "row");
        foreach (var (owner, type, property) in new[]
        {
            (typeof(Math), typeof(sbyte), nameof(Row.SByte)),
            (typeof(Math), typeof(byte), nameof(Row.Byte)),
            (typeof(Math), typeof(short), nameof(Row.Int16)),
            (typeof(Math), typeof(ushort), nameof(Row.UInt16)),
            (typeof(Math), typeof(int), nameof(Row.Integer)),
            (typeof(Math), typeof(uint), nameof(Row.UInt32)),
            (typeof(Math), typeof(long), nameof(Row.Int64)),
            (typeof(Math), typeof(ulong), nameof(Row.UInt64)),
            (typeof(Math), typeof(float), nameof(Row.Single)),
            (typeof(Math), typeof(double), nameof(Row.Double)),
            (typeof(decimal), typeof(decimal), nameof(Row.Decimal))
        })
        {
            var method = owner.GetMethod("Clamp", [type, type, type])!;
            var value = Expression.Property(row, property);
            var lower = Expression.Constant(Convert.ChangeType(type == typeof(byte) || type == typeof(ushort)
                || type == typeof(uint) || type == typeof(ulong) ? 0 : -1, type), type);
            var upper = Expression.Constant(Convert.ChangeType(1, type), type);
            var zero = Expression.Constant(Convert.ChangeType(0, type), type);
            var call = Expression.Call(method, value, lower, upper);
            var predicate = Expression.Lambda<Func<Row, bool>>(Expression.GreaterThan(call, zero), row);

            var sql = context.Rows.Where(predicate).ToQueryString();

            Assert.Contains("CASE", sql, StringComparison.Ordinal);
            Assert.Contains("WHEN", sql, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql)]
    [InlineData(OceanBaseCompatibilityMode.Oracle)]
    public void Clamp_rejects_reversed_constant_bounds(OceanBaseCompatibilityMode mode)
    {
        using var context = CreateContext(mode);

        Assert.Throws<InvalidOperationException>(() => context.Rows
            .Where(row => Math.Clamp(row.Integer, 1, -1) > 0).ToQueryString());
    }

    private static TestContext CreateContext(OceanBaseCompatibilityMode mode)
        => new(new DbContextOptionsBuilder<TestContext>()
            .UseOceanBase("Server=offline;Database=clamp", mode)
            .EnableServiceProviderCaching(false).Options);

    private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
    {
        public DbSet<Row> Rows => Set<Row>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Row>().Property(row => row.Converted).HasConversion<long>();
    }

    private sealed class Row
    {
        public int Id { get; set; }
        public sbyte SByte { get; set; }
        public byte Byte { get; set; }
        public short Int16 { get; set; }
        public ushort UInt16 { get; set; }
        public int Integer { get; set; }
        public uint UInt32 { get; set; }
        public long Int64 { get; set; }
        public ulong UInt64 { get; set; }
        public decimal Decimal { get; set; }
        public double Double { get; set; }
        public float Single { get; set; }
        public int Converted { get; set; }
    }
}
