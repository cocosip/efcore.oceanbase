using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OceanBase.EntityFrameworkCore.Query;
using Xunit;

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
        var query = context.Rows.SelectMany(row => values.Where(value => value != row.Payload));

        var sql = query.ToQueryString();

        Assert.Contains("UNION ALL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CAST(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "BINARY" : "BLOB", sql,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(mode == OceanBaseCompatibilityMode.Oracle,
            sql.Contains("FROM DUAL", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, "BINARY")]
    [InlineData(OceanBaseCompatibilityMode.Oracle, "BLOB")]
    public void Originally_empty_inline_binary_roots_keep_typed_zero_row_sql(
        OceanBaseCompatibilityMode mode, string castType)
    {
        using var context = CreateContext(mode);
        var query = context.Rows.SelectMany(row => new byte[][] { }.Where(value => value != row.Payload));

        var sql = query.ToQueryString();

        Assert.Contains("WHERE 1 = 0", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"CAST(NULL AS {castType})", sql, StringComparison.OrdinalIgnoreCase);
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
    [InlineData(OceanBaseCompatibilityMode.MySql, ParameterTranslationMode.Constant)]
    [InlineData(OceanBaseCompatibilityMode.MySql, ParameterTranslationMode.MultipleParameters)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, ParameterTranslationMode.Constant)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, ParameterTranslationMode.MultipleParameters)]
    public void Explicit_parameter_binary_roots_translate_and_rebind(
        OceanBaseCompatibilityMode mode, ParameterTranslationMode translation)
    {
        using var context = CreateContext(mode, translation);
        byte[]?[] values = [new byte[] { 1, 2 }, null, new byte[] { 3, 4 }];
        var query = context.Rows.Where(row => EF.Parameter(values).Contains(row.Payload));

        foreach (var input in new byte[]?[][] { values, [new byte[] { 7 }], [] })
        {
            values = input;
            var sql = query.ToQueryString();
            Assert.DoesNotContain("JSON", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("IN ()", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(input.Length == 0, sql.Contains("WHERE 1 = 0", StringComparison.Ordinal)
                || sql.Contains("WHERE 0", StringComparison.Ordinal));
        }
    }

    private static TestContext CreateContext(OceanBaseCompatibilityMode mode,
        ParameterTranslationMode? translation = null)
    {
        var builder = new DbContextOptionsBuilder<TestContext>()
            .UseOceanBase("Server=localhost;Database=binary_roots", mode)
            .EnableServiceProviderCaching(false);
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
        public byte[] Payload { get; set; } = [];
    }
}
