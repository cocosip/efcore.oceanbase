using System;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBase.EntityFrameworkCore;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests;

public sealed class TypeMappingTests
{
    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, typeof(int), "INT")]
    [InlineData(OceanBaseCompatibilityMode.MySql, typeof(long), "BIGINT")]
    [InlineData(OceanBaseCompatibilityMode.MySql, typeof(decimal), "DECIMAL")]
    [InlineData(OceanBaseCompatibilityMode.MySql, typeof(string), "TEXT")]
    [InlineData(OceanBaseCompatibilityMode.MySql, typeof(byte[]), "BLOB")]
    [InlineData(OceanBaseCompatibilityMode.MySql, typeof(DateTime), "DATETIME")]
    [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(int), "NUMBER(10)")]
    [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(long), "NUMBER(19)")]
    [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(string), "CLOB")]
    [InlineData(OceanBaseCompatibilityMode.Oracle, typeof(byte[]), "BLOB")]
    public void Maps_common_clr_types_to_oceanbase_store_types(
        OceanBaseCompatibilityMode mode,
        Type clrType,
        string storeType)
    {
        using var context = new TestContext(
            new DbContextOptionsBuilder<TestContext>()
                .UseOceanBase("Server=localhost;Database=test", mode)
                .Options);

        var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(clrType);

        Assert.NotNull(mapping);
        Assert.Equal(storeType, mapping!.StoreType);
        Assert.NotNull(mapping.DbType);
    }

    [Fact]
    public void Maps_nullable_scalar_types_to_the_underlying_store_type()
    {
        using var context = new TestContext(
            new DbContextOptionsBuilder<TestContext>()
                .UseOceanBase("Server=localhost;Database=test", OceanBaseCompatibilityMode.MySql)
                .Options);

        Assert.Equal("INT", context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(int?))!.StoreType);
    }

    private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options);
}
