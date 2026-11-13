using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests;

public sealed class SplitQueryOptionsTests
{
    public static IEnumerable<object[]> Modes()
    { foreach (var mode in Enum.GetValues<OceanBaseCompatibilityMode>()) yield return [mode]; }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Configuration_preserves_clones_and_service_reuse_but_separates_compiled_cache_keys(OceanBaseCompatibilityMode mode)
    {
        using var plain = Create(mode);
        var configured = new DbContextOptionsBuilder<Context>().UseOceanBase("Server=offline", mode,
            options => options.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
        using var split = new Context(configured.Options);
        using var clone = new Context(new DbContextOptionsBuilder<Context>(configured.Options).UseOceanBase("Server=another", mode).Options);
        Assert.Null(RelationalOptionsExtension.Extract(plain.GetService<IDbContextOptions>()).QuerySplittingBehavior);
        Assert.Equal(QuerySplittingBehavior.SplitQuery, RelationalOptionsExtension.Extract(clone.GetService<IDbContextOptions>()).QuerySplittingBehavior);
        Assert.Same(plain.GetService<ISqlGenerationHelper>(), split.GetService<ISqlGenerationHelper>());
        var expression = plain.Set<Row>().AsQueryable().Expression;
        object Key(DbContext context) => context.GetService<ICompiledQueryCacheKeyGenerator>().GenerateCacheKey(expression, async: false);
        Assert.NotEqual(Key(plain), Key(split));
        Assert.Equal(Key(split), Key(clone));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Effective_query_overrides_control_buffering_without_changing_retry_buffering(OceanBaseCompatibilityMode mode)
    {
        foreach (var globalSplit in new[] { false, true })
        foreach (var retry in new[] { false, true })
        foreach (var async in new[] { false, true })
        foreach (var querySplit in new[] { false, true })
        {
            using var context = new Context(new DbContextOptionsBuilder<Context>().UseOceanBase("Server=offline", mode, options =>
            {
                if (globalSplit) options.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                if (retry) options.EnableRetryOnFailure();
            }).Options);
            var query = querySplit ? context.Set<Row>().AsSplitQuery() : context.Set<Row>().AsSingleQuery();
            var compilation = Assert.IsAssignableFrom<RelationalQueryCompilationContext>(context.GetService<IQueryCompilationContextFactory>().Create(async));
            compilation.CreateQueryExecutor<IEnumerable<Row>>(query.Expression);
            Assert.Equal(querySplit ? QuerySplittingBehavior.SplitQuery : QuerySplittingBehavior.SingleQuery, compilation.QuerySplittingBehavior);
            Assert.Equal(querySplit || retry, compilation.IsBuffering);
        }
    }

    [Fact]
    public void Invalid_query_splitting_enum_rejects_during_configuration()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new DbContextOptionsBuilder().UseOceanBase("Server=offline",
            OceanBaseCompatibilityMode.MySql, options => options.UseQuerySplittingBehavior((QuerySplittingBehavior)42)));

#pragma warning disable EF9100 // Preserve EF10's existing experimental entry point; this does not claim compiled-model support.
    [Theory]
    [MemberData(nameof(Modes))]
    public void Precompilation_factory_retains_the_effective_buffering_context(OceanBaseCompatibilityMode mode)
    {
        using var context = Create(mode);
        foreach (var async in new[] { false, true })
        {
            var compilation = context.GetService<IQueryCompilationContextFactory>().CreatePrecompiled(async);
            Assert.IsAssignableFrom<RelationalQueryCompilationContext>(compilation);
            Assert.True(compilation.IsPrecompiling);
            Assert.False(compilation.IsBuffering);
        }
    }
#pragma warning restore EF9100
    private static Context Create(OceanBaseCompatibilityMode mode)
        => new(new DbContextOptionsBuilder<Context>().UseOceanBase("Server=offline", mode).Options);
    private sealed class Context(DbContextOptions<Context> options) : DbContext(options)
    { protected override void OnModelCreating(ModelBuilder model) => model.Entity<Row>(); }
    private sealed class Row { public int Id { get; set; } }
}
