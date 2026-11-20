using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace OceanBase.EntityFrameworkCore.LiveTests
{
    public sealed class LiveAmbientTransactionTests
    {
        public static IEnumerable<object[]> Cases()
        {
            foreach (var mode in Enum.GetValues<OceanBaseCompatibilityMode>())
            foreach (var async in new[] { false, true })
            foreach (var complete in new[] { false, true })
            foreach (var ownership in new[] { "automatic", "scope", "outer", "native" })
                yield return [mode, async, complete, ownership];
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public Task Ambient_scope_enlists_multiple_saves_and_preserves_connection_ownership(
            OceanBaseCompatibilityMode mode, bool async, bool complete, string ownership)
            => LiveStorageTable.WithTable<Context>(mode, async, false, (options, table) =>
                new Context(new DbContextOptionsBuilder<Context>(options)
                    .ReplaceService<IModelCacheKeyFactory, CacheKey>().Options, table), async (context, _, stage) =>
                {
                    stage("enlist connection in a single-resource ambient scope");
                    if (ownership == "outer")
                    {
                        if (async) await context.Database.OpenConnectionAsync();
                        else context.Database.OpenConnection();
                    }
                    else if (ownership == "native")
                    {
                        if (async) await context.Database.GetDbConnection().OpenAsync();
                        else context.Database.GetDbConnection().Open();
                    }
                    using (var scope = new TransactionScope(TransactionScopeOption.Required,
                        new TransactionOptions { IsolationLevel = System.Transactions.IsolationLevel.ReadCommitted },
                        TransactionScopeAsyncFlowOption.Enabled))
                    {
                        if (ownership == "scope")
                        {
                            if (async) await context.Database.OpenConnectionAsync();
                            else context.Database.OpenConnection();
                        }
                        for (var id = 1; id <= 2; id++)
                        {
                            context.Add(new Row { Id = id });
                            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
                            Assert.Null(context.Database.CurrentTransaction);
                            Assert.Equal(ConnectionState.Open, context.Database.GetDbConnection().State);
                        }
                        Assert.Equal(2, async ? await context.Set<Row>().CountAsync() : context.Set<Row>().Count());
                        using (var suppressed = new TransactionScope(TransactionScopeOption.Suppress,
                            TransactionScopeAsyncFlowOption.Enabled))
                        {
                            if (async) await Assert.ThrowsAsync<InvalidOperationException>(() => context.Database.OpenConnectionAsync());
                            else Assert.Throws<InvalidOperationException>(() => context.Database.OpenConnection());
                        }
                        using (var nested = new TransactionScope(TransactionScopeOption.RequiresNew,
                            TransactionScopeAsyncFlowOption.Enabled))
                        {
                            if (async) await Assert.ThrowsAsync<InvalidOperationException>(() => context.Database.OpenConnectionAsync());
                            else Assert.Throws<InvalidOperationException>(() => context.Database.OpenConnection());
                        }
                        Assert.Equal(2, async ? await context.Set<Row>().CountAsync() : context.Set<Row>().Count());
                        if (async) await Assert.ThrowsAsync<InvalidOperationException>(() => context.Database.BeginTransactionAsync());
                        else Assert.Throws<InvalidOperationException>(() => context.Database.BeginTransaction());
                        if (complete) scope.Complete();
                    }
                    stage("verify commit or rollback and reuse the connection");
                    if (ownership != "automatic")
                    {
                        Assert.Equal(ConnectionState.Open, context.Database.GetDbConnection().State);
                        if (ownership == "native")
                        {
                            if (async) await context.Database.GetDbConnection().CloseAsync();
                            else context.Database.GetDbConnection().Close();
                        }
                        else if (async) await context.Database.CloseConnectionAsync();
                        else context.Database.CloseConnection();
                    }
                    Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
                    context.ChangeTracker.Clear();
                    Assert.Equal(complete ? new[] { 1, 2 } : Array.Empty<int>(), async
                        ? await context.Set<Row>().OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync()
                        : context.Set<Row>().OrderBy(row => row.Id).Select(row => row.Id).ToArray());
                    context.Add(new Row { Id = 3 });
                    Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
                });

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, true)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, true)]
        public Task Explicit_enlistment_keeps_connection_until_transaction_completion(
            OceanBaseCompatibilityMode mode, bool async, bool complete)
            => LiveStorageTable.WithTable<Context>(mode, async, false, (options, table) =>
                new Context(new DbContextOptionsBuilder<Context>(options)
                    .ReplaceService<IModelCacheKeyFactory, CacheKey>().Options, table), async (context, _, stage) =>
                {
                    stage("explicit single-resource enlistment");
                    using (var transaction = new CommittableTransaction(new TransactionOptions
                        { IsolationLevel = System.Transactions.IsolationLevel.ReadCommitted }))
                    {
                        if (async) await context.Database.OpenConnectionAsync();
                        else context.Database.OpenConnection();
                        context.Database.EnlistTransaction(transaction);
                        for (var id = 1; id <= 2; id++)
                        {
                            context.Add(new Row { Id = id });
                            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
                        }
                        if (async) await context.Database.CloseConnectionAsync();
                        else context.Database.CloseConnection();
                        Assert.Equal(ConnectionState.Open, context.Database.GetDbConnection().State);
                        if (complete) transaction.Commit();
                        else transaction.Rollback();
                    }
                    stage("verify explicit enlistment and context reuse");
                    Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
                    context.ChangeTracker.Clear();
                    Assert.Equal(complete ? 2 : 0, async ? await context.Set<Row>().CountAsync() : context.Set<Row>().Count());
                    context.Add(new Row { Id = 3 });
                    Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
                });

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public Task Reset_in_ambient_scope_preserves_work_and_allows_context_reuse(OceanBaseCompatibilityMode mode, bool async)
            => LiveStorageTable.WithTable<Context>(mode, async, false, (options, table) =>
                new Context(new DbContextOptionsBuilder<Context>(options)
                    .ReplaceService<IModelCacheKeyFactory, CacheKey>().Options, table), async (context, _, stage) =>
                {
                    stage("reset connection during ambient scope");
                    var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
                    try
                    {
                        context.Add(new Row { Id = 1 });
                        Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
                        var connection = context.GetService<Microsoft.EntityFrameworkCore.Storage.IRelationalConnection>();
                        if (async) await ((IResettableService)connection).ResetStateAsync(default);
                        else ((IResettableService)connection).ResetState();
                        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
                        scope.Complete();
                    }
                    finally
                    {
                        scope.Dispose();
                    }
                    stage("verify commit and reuse after reset");
                    context.ChangeTracker.Clear();
                    Assert.Equal(1, async ? await context.Set<Row>().CountAsync() : context.Set<Row>().Count());
                    context.Add(new Row { Id = 2 });
                    Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
                });

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public Task Ambient_write_failure_is_not_partially_retried(OceanBaseCompatibilityMode mode, bool async)
            => LiveStorageTable.WithTable<Context>(mode, async, false, (options, table) =>
                new Context(new DbContextOptionsBuilder<Context>(options)
                    .ReplaceService<IModelCacheKeyFactory, CacheKey>().Options, table), async (context, _, stage) =>
                {
                    stage("reject replay of a partially executed ambient transaction");
                    await using var retryContext = new Context(new DbContextOptionsBuilder<Context>()
                        .UseOceanBase(context.Database.GetConnectionString()!, mode,
                            options => options.EnableRetryOnFailure(2, TimeSpan.Zero, [1, 1062]))
                        .ReplaceService<IModelCacheKeyFactory, CacheKey>().Options, context.Table);
                    var strategy = retryContext.Database.CreateExecutionStrategy();
                    var duplicate = mode == OceanBaseCompatibilityMode.MySql
                        ? "INSERT INTO `" + context.Table + "` (`Id`) VALUES (1)"
                        : "INSERT INTO \"" + context.Table + "\" (\"Id\") VALUES (1)";
                    TransactionScope? scope = null;
                    var attempts = 0;
                    async Task Execute()
                    {
                        attempts++;
                        scope ??= new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
                        retryContext.Add(new Row { Id = 1 });
                        if (async) await retryContext.SaveChangesAsync();
                        else retryContext.SaveChanges();
                        if (async) await retryContext.Database.ExecuteSqlRawAsync(duplicate);
                        else retryContext.Database.ExecuteSqlRaw(duplicate);
                    }
                    try
                    {
                        var failure = async
                            ? await Record.ExceptionAsync(() => strategy.ExecuteAsync(Execute))
                            : Record.Exception(() => strategy.Execute(() => Execute().GetAwaiter().GetResult()));
                        var native = Assert.IsType<OceanBaseConnector.OceanBaseException>(failure);
                        Assert.Equal(mode == OceanBaseCompatibilityMode.MySql ? 1062 : 1, native.ServerErrorCode);
                        Assert.Equal(1, attempts);
                    }
                    finally
                    {
                        scope?.Dispose();
                    }
                    stage("verify rollback after non-retried ambient failure");
                    Assert.Equal(0, async ? await context.Set<Row>().CountAsync() : context.Set<Row>().Count());
                });

        private sealed class Row
        {
            public int Id { get; set; }
        }

        private sealed class Context(DbContextOptions<Context> options, string table) : DbContext(options)
        {
            public string Table => table;
            protected override void OnModelCreating(ModelBuilder modelBuilder)
                => modelBuilder.Entity<Row>().ToTable(table).Property(row => row.Id).ValueGeneratedNever();
        }

        private sealed class CacheKey : IModelCacheKeyFactory
        {
            public object Create(DbContext context, bool designTime) => (((Context)context).Table, designTime);
        }
    }
}
