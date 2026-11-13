using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBase.EntityFrameworkCore.Storage;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests;

public sealed class ExecutionStrategyTests
{
    public static IEnumerable<object[]> Modes()
    {
        foreach (var mode in Enum.GetValues<OceanBaseCompatibilityMode>())
            foreach (var async in new[] { false, true }) yield return [mode, async];
    }

    public static IEnumerable<object[]> ServerCases()
    {
        foreach (var item in Modes())
        {
            var mode = (OceanBaseCompatibilityMode)item[0];
            foreach (var code in new[] { 1205, 1213, 54, 60, 8177, 1, 1062, 1045, 1146, 2006, 2013, 0 })
                yield return [mode, item[1], code, null!, mode == OceanBaseCompatibilityMode.MySql
                    ? code is 1205 or 1213 : code is 54 or 60 or 8177];
            yield return [mode, item[1], 9999, "40001", true];
            yield return [mode, item[1], 9999, "42000", false];
            yield return [mode, item[1], 0, "40001", false];
        }
    }

    [Theory]
    [MemberData(nameof(ServerCases))]
    public async Task Known_server_failures_retry_by_mode_and_state_only(OceanBaseCompatibilityMode mode, bool async,
        int code, string? state, bool retries)
    {
        using var context = Create(mode);
        var strategy = context.Database.CreateExecutionStrategy();
        var error = ServerError(code, state);
        var attempts = 0;
        int Execute() { if (++attempts == 1) throw error; return 7; }
        if (retries)
        {
            Assert.Equal(7, async ? await strategy.ExecuteAsync(() => Task.FromResult(Execute())) : strategy.Execute(Execute));
            Assert.Equal(2, attempts);
        }
        else
        {
            Assert.Same(error, async ? await Assert.ThrowsAsync<OceanBaseException>(() => strategy.ExecuteAsync(() => Task.FromResult(Execute())))
                : Assert.Throws<OceanBaseException>(() => strategy.Execute(Execute)));
            Assert.Equal(1, attempts);
        }
        Assert.Null(ExecutionStrategy.Current);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Additional_codes_cannot_override_unknown_XA_cancellation_or_client_errors(OceanBaseCompatibilityMode mode, bool async)
    {
        foreach (var error in new Exception[] { ServerError(9999, unknown: true), ServerError(9999, xa: -7),
            ServerError(0), ServerError(mode == OceanBaseCompatibilityMode.MySql ? 1317 : 1013),
            ServerError(mode == OceanBaseCompatibilityMode.MySql ? 3024 : 1013),
            ServerError(9999, "70100"), ServerError(9999, "57014"),
            new TimeoutException(), new IOException(), new OperationCanceledException() })
        {
            using var context = Create(mode, codes: [9999, 1317, 3024, 1013]);
            var strategy = context.Database.CreateExecutionStrategy();
            var attempts = 0;
            void Execute() { attempts++; throw error; }
            var actual = async ? await Record.ExceptionAsync(() => strategy.ExecuteAsync(() => { Execute(); return Task.CompletedTask; }))
                : Record.Exception(() => strategy.Execute(Execute));
            Assert.Same(error, actual);
            Assert.Equal(1, attempts);
        }
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Defaults_do_not_retry_and_exhaustion_retains_the_last_error(OceanBaseCompatibilityMode mode, bool async)
    {
        var error = ServerError(mode == OceanBaseCompatibilityMode.MySql ? 1213 : 60);
        foreach (var enabled in new[] { false, true })
        {
            using var context = Create(mode, enabled: enabled);
            var strategy = context.Database.CreateExecutionStrategy();
            Assert.Equal(enabled, strategy.RetriesOnFailure);
            var attempts = 0;
            int Execute() { attempts++; throw error; }
            var actual = async ? await Record.ExceptionAsync(() => strategy.ExecuteAsync(() => Task.FromResult(Execute())))
                : Record.Exception(() => strategy.Execute(Execute));
            Assert.Equal(enabled ? 3 : 1, attempts);
            if (enabled) Assert.Same(error, Assert.IsType<RetryLimitExceededException>(actual).InnerException);
            else Assert.Same(error, actual);
        }
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql)]
    [InlineData(OceanBaseCompatibilityMode.Oracle)]
    public void Options_snapshot_additional_codes_and_preserve_clones_without_partitioning_singletons(OceanBaseCompatibilityMode mode)
    {
        var codes = new[] { 9999 };
        var builder = new DbContextOptionsBuilder<StrategyContext>().UseOceanBase("Server=offline;Database=app", mode,
            options => options.EnableRetryOnFailure(3, TimeSpan.Zero, codes));
        codes[0] = 8888;
        var clone = new DbContextOptionsBuilder<StrategyContext>(builder.Options).UseOceanBase("Server=other;Database=app", mode)
            .EnableOceanBaseSchemaManagement("app");
        using var first = new StrategyContext(builder.Options);
        using var second = new StrategyContext(clone.Options);
        using var plain = Create(mode, enabled: false);
        var strategy = Assert.IsType<OceanBaseRetryingExecutionStrategy>(second.Database.CreateExecutionStrategy());
        Assert.Equal(3, strategy.MaxRetryCount);
        Assert.Equal(TimeSpan.Zero, strategy.MaxRetryDelay);
        Assert.Same(first.GetService<IModelCacheKeyFactory>(), plain.GetService<IModelCacheKeyFactory>());
        Assert.Same(first.Model, plain.Model);
        var attempts = 0;
        strategy.Execute(() => { if (++attempts == 1) throw ServerError(9999); });
        Assert.Equal(2, attempts);
        Assert.Throws<OceanBaseException>(() => strategy.Execute((Action)(() => throw ServerError(8888))));
        Assert.False(plain.Database.CreateExecutionStrategy().RetriesOnFailure);

        var expression = first.Set<Row>().Select(r => r.Id).Expression;
        object Key(DbContext context) => context.GetService<ICompiledQueryCacheKeyGenerator>().GenerateCacheKey(expression, async: false);
        Assert.Equal(Key(first), Key(second));
        Assert.NotEqual(Key(first), Key(plain));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(1, -1)]
    public void Invalid_limits_reject_during_configuration(int count, int delay)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new DbContextOptionsBuilder().UseOceanBase("Server=offline",
            OceanBaseCompatibilityMode.MySql, options => options.EnableRetryOnFailure(count, TimeSpan.FromMilliseconds(delay))));

    [Fact]
    public void Client_local_additional_code_rejects_during_configuration()
        => Assert.Throws<ArgumentException>(() => new DbContextOptionsBuilder().UseOceanBase("Server=offline",
            OceanBaseCompatibilityMode.MySql, options => options.EnableRetryOnFailure(additionalErrorCodes: [0])));

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Nested_strategies_share_the_outer_budget(OceanBaseCompatibilityMode mode, bool async)
    {
        using var context = Create(mode, codes: [9999]);
        var outer = context.Database.CreateExecutionStrategy();
        var attempts = 0;
        var error = ServerError(9999);
        if (async)
            await Assert.ThrowsAsync<RetryLimitExceededException>(() => outer.ExecuteAsync(() =>
                context.Database.CreateExecutionStrategy().ExecuteAsync(() => { attempts++; return Task.FromException(error); })));
        else
            Assert.Throws<RetryLimitExceededException>(() => outer.Execute(() =>
                context.Database.CreateExecutionStrategy().Execute(() => { attempts++; throw error; })));
        Assert.Equal(3, attempts);
        Assert.Null(ExecutionStrategy.Current);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql)]
    [InlineData(OceanBaseCompatibilityMode.Oracle)]
    public async Task Cancellation_before_execution_and_between_attempts_stops_without_more_work(OceanBaseCompatibilityMode mode)
    {
        using var context = Create(mode, codes: [9999]);
        foreach (var preCancelled in new[] { false, true })
        {
            var attempts = 0;
            using var source = new CancellationTokenSource();
            if (preCancelled) source.Cancel();
            var strategy = context.Database.CreateExecutionStrategy();
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => strategy.ExecuteAsync(0, (_, _, _) =>
            {
                attempts++;
                source.Cancel();
                return Task.FromException<int>(ServerError(9999));
            }, verifySucceeded: null, cancellationToken: source.Token));
            Assert.Equal(source.Token, error.CancellationToken);
            Assert.Equal(preCancelled ? 0 : 1, attempts);
        }
    }

    private static StrategyContext Create(OceanBaseCompatibilityMode mode, bool enabled = true, IEnumerable<int>? codes = null)
        => new(new DbContextOptionsBuilder<StrategyContext>().UseOceanBase("Server=offline;Database=app", mode,
            options => { if (enabled) options.EnableRetryOnFailure(2, TimeSpan.Zero, codes); }).Options);

    private static OceanBaseException ServerError(int code, string? state = null, bool unknown = false, int? xa = null)
        => (OceanBaseException)Activator.CreateInstance(typeof(OceanBaseException), BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, args: ["Synthetic server failure.", code, state, unknown, null, xa], culture: null)!;
    private sealed class StrategyContext(DbContextOptions<StrategyContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder) => builder.Entity<Row>();
    }
    private sealed class Row { public int Id { get; set; } }
}
