using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OceanBase.EntityFrameworkCore.Query;
using Xunit;

#pragma warning disable EF1001 // EF9-only collection options infrastructure.

namespace OceanBase.EntityFrameworkCore.UnitTests;

public sealed class BinaryCollectionRootQueryTests
{
    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql)]
    [InlineData(OceanBaseCompatibilityMode.Oracle)]
    public void Binary_collection_roots_translate(OceanBaseCompatibilityMode mode)
    {
        using var context = CreateContext(mode);
        byte[][] values = [new byte[] { 1, 2 }, new byte[] { 3, 4 }];
        var query = context.Rows.SelectMany(row => values.Where(value => row.Id > 0));

        var sql = query.ToQueryString();

        Assert.Contains("UNION ALL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "CAST(" : "TO_BLOB(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "BINARY" : "BLOB", sql,
            StringComparison.OrdinalIgnoreCase);
        if (mode == OceanBaseCompatibilityMode.Oracle)
        {
            Assert.Contains("TO_BLOB(", sql);
            Assert.DoesNotContain(" AS BLOB)", sql);
        }
        Assert.Equal(mode == OceanBaseCompatibilityMode.Oracle,
            sql.Contains("FROM DUAL", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, "CAST(NULL AS BINARY)")]
    [InlineData(OceanBaseCompatibilityMode.Oracle, "TO_BLOB(NULL)")]
    public void Originally_empty_inline_binary_roots_keep_typed_zero_row_sql(
        OceanBaseCompatibilityMode mode, string castType)
    {
        using var context = CreateContext(mode);
        var query = context.Rows.SelectMany(row => new byte[][] { }.Where(value => row.Id > 0));

        var sql = query.ToQueryString();

        Assert.Contains("WHERE 1 = 0", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(castType, sql, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql)]
    [InlineData(OceanBaseCompatibilityMode.Oracle)]
    public void Binary_collection_support_preserves_non_null_converters(OceanBaseCompatibilityMode mode)
    {
        using var context = CreateContext(mode);
        var source = context.GetService<IRelationalTypeMappingSource>();
        var mapping = source.FindMapping(typeof(byte[]))!;
        var converted = (RelationalTypeMapping)mapping.WithComposedConverter(new ValueConverter<string, byte[]>(
            value => Convert.FromHexString(value), value => Convert.ToHexString(value)));
        var nullConverted = (RelationalTypeMapping)mapping.WithComposedConverter(new ValueConverter<string, byte[]>(
            value => Convert.FromHexString(value), value => Convert.ToHexString(value), convertsNulls: true));

        Assert.True(OceanBaseCollectionParameterExpandingVisitor.IsSupportedBinary(converted));
        Assert.False(OceanBaseCollectionParameterExpandingVisitor.IsSupportedBinary(nullConverted));
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql)]
    [InlineData(OceanBaseCompatibilityMode.Oracle)]
    public void Captured_binary_roots_translate_and_rebind(
        OceanBaseCompatibilityMode mode)
    {
        using var context = CreateContext(mode);
        byte[]?[] values = [new byte[] { 1, 2 }, null, new byte[] { 3, 4 }];
        var query = context.Rows.Where(row => values.Contains(row.Payload));

        foreach (var input in new byte[]?[][] { values, [new byte[] { 7 }], [] })
        {
            values = input;
            if (mode == OceanBaseCompatibilityMode.Oracle && input.Length > 0)
            {
                Assert.Contains("BLOB", Assert.Throws<NotSupportedException>(() => query.ToQueryString()).Message);
                continue;
            }
            var sql = query.ToQueryString();
            Assert.DoesNotContain("JSON", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("IN ()", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(input.Length == 0, sql.Contains("WHERE 1 = 0", StringComparison.Ordinal)
                || sql.Contains("WHERE 0", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Oracle_BLOB_equality_and_membership_reject_but_NULL_tests_remain_supported()
    {
        using var context = CreateContext(OceanBaseCompatibilityMode.Oracle);
        byte[] value = [1, 2];
        byte[][] values = [value, [3, 4]];
        var rows = context.Rows;
        foreach (var query in new[] { rows.Where(row => row.Payload == value), rows.Where(row => row.Payload != value),
            rows.Where(row => values.Contains(row.Payload)), rows.Where(row => rows.Select(other => other.Payload).Contains(row.Payload)),
            rows.Where(row => rows.Select(other => other.Payload).Contains(row.Bounded)) })
            Assert.Contains("BLOB", Assert.Throws<NotSupportedException>(() => query.ToQueryString()).Message);
        Assert.Contains("IS NULL", rows.Where(row => row.Payload == null).ToQueryString());
        Assert.Contains("IS NOT NULL", rows.Where(row => row.Payload != null).ToQueryString());
        using var relational = CreateContext(OceanBaseCompatibilityMode.Oracle, relationalNulls: true);
        Assert.Contains("BLOB", Assert.Throws<NotSupportedException>(() => relational.Rows.Where(row =>
            relational.Rows.Select(other => other.Payload).Contains(row.Bounded)).ToQueryString()).Message);
    }

    private static TestContext CreateContext(OceanBaseCompatibilityMode mode, bool relationalNulls = false)
    {
        var builder = new DbContextOptionsBuilder<TestContext>()
            .UseOceanBase("Server=localhost;Database=binary_roots", mode)
            .EnableServiceProviderCaching(false);
        if (relationalNulls)
        {
            var extension = builder.Options.Extensions.OfType<RelationalOptionsExtension>().Single()
                .WithUseRelationalNulls(relationalNulls);
            ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(extension);
        }

        return new TestContext(builder.Options);
    }

    private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
    {
        public DbSet<Row> Rows => Set<Row>();
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Row>().Property(row => row.Bounded).HasMaxLength(32);
    }

    private sealed class Row
    {
        public int Id { get; set; }
        public byte[]? Payload { get; set; }
        public byte[]? Bounded { get; set; }
    }
}
