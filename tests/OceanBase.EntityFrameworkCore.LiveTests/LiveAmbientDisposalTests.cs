using System;
using System.Linq;
using System.Threading.Tasks;
using System.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.LiveTests
{
    public sealed class LiveAmbientDisposalTests
    {
        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, true)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, true)]
        public Task Reset_of_borrowed_ambient_connection_blocks_suppressed_reuse_and_preserves_caller_ownership(
            OceanBaseCompatibilityMode mode, bool async, bool complete)
            => LiveStorageTable.WithTable<Context>(mode, async, false, (options, table) =>
                new Context(new DbContextOptionsBuilder<Context>(options)
                    .ReplaceService<IModelCacheKeyFactory, CacheKey>().Options, table), async (context, _, stage) =>
                {
                    stage("reset borrowed connection during ambient transaction");
                    await using var native = new OceanBaseConnection(context.Database.GetConnectionString()!);
                    if (async) await native.OpenAsync();
                    else native.Open();
                    var borrowed = new Context(new DbContextOptionsBuilder<Context>().UseOceanBase(native, mode)
                        .UseModel(context.Model).Options, context.Table);
                    try
                    {
                        using (var scope = new TransactionScope(TransactionScopeOption.Required,
                            new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted }, TransactionScopeAsyncFlowOption.Enabled))
                        {
                            borrowed.Add(new Row { Id = 1 });
                            Assert.Equal(1, async ? await borrowed.SaveChangesAsync() : borrowed.SaveChanges());
                            await using var replacement = new OceanBaseConnection(context.Database.GetConnectionString()!);
                            Assert.Throws<InvalidOperationException>(() => borrowed.Database.SetDbConnection(replacement));
                            var connection = borrowed.GetService<IRelationalConnection>();
                            if (async) await ((IResettableService)connection).ResetStateAsync(default);
                            else ((IResettableService)connection).ResetState();
                            Assert.Same(native, borrowed.Database.GetDbConnection());
                            Assert.Equal(System.Data.ConnectionState.Open, native.State);
                            using (var suppressed = new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled))
                            {
                                if (async) await Assert.ThrowsAsync<InvalidOperationException>(() => borrowed.Set<Row>().CountAsync());
                                else Assert.Throws<InvalidOperationException>(() => borrowed.Set<Row>().Count());
                            }
                            if (complete) scope.Complete();
                        }
                        stage("reuse borrowed connection after completion and retain caller native ownership");
                        borrowed.ChangeTracker.Clear();
                        Assert.Equal(complete ? 1 : 0, async ? await borrowed.Set<Row>().CountAsync() : borrowed.Set<Row>().Count());
                    }
                    finally
                    {
                        if (async) await borrowed.DisposeAsync();
                        else borrowed.Dispose();
                    }
                    Assert.Equal(System.Data.ConnectionState.Open, native.State);
                    await using var command = native.CreateCommand();
                    command.CommandText = mode == OceanBaseCompatibilityMode.MySql ? "SELECT 1" : "SELECT 1 FROM DUAL";
                    Assert.NotNull(async ? await command.ExecuteScalarAsync() : command.ExecuteScalar());
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
        public Task Pooled_borrowed_connection_rejects_reuse_until_ambient_completion(
            OceanBaseCompatibilityMode mode, bool async, bool complete)
            => LiveStorageTable.WithTable<Context>(mode, async, false, (options, table) =>
                new Context(new DbContextOptionsBuilder<Context>(options)
                    .ReplaceService<IModelCacheKeyFactory, CacheKey>().Options, table), async (context, _, stage) =>
                {
                    stage("return borrowed connection to context pool during ambient transaction");
                    await using var native = new OceanBaseConnection(context.Database.GetConnectionString()!);
                    if (async) await native.OpenAsync();
                    else native.Open();
                    var options = new DbContextOptionsBuilder<PooledContext>().UseOceanBase(native, mode)
                        .UseModel(context.Model).Options;
                    var factory = new PooledDbContextFactory<PooledContext>(options, poolSize: 1);
                    using (var scope = new TransactionScope(TransactionScopeOption.Required,
                        new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted }, TransactionScopeAsyncFlowOption.Enabled))
                    {
                        var first = factory.CreateDbContext();
                        first.Add(new Row { Id = 1 });
                        Assert.Equal(1, async ? await first.SaveChangesAsync() : first.SaveChanges());
                        if (async) await first.DisposeAsync();
                        else first.Dispose();
                        using (var suppressed = new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled))
                        {
                            var next = factory.CreateDbContext();
                            Assert.Same(first, next);
                            try
                            {
                                Assert.Same(native, next.Database.GetDbConnection());
                                if (async) await Assert.ThrowsAsync<InvalidOperationException>(() => next.Set<Row>().CountAsync());
                                else Assert.Throws<InvalidOperationException>(() => next.Set<Row>().Count());
                            }
                            finally
                            {
                                if (async) await next.DisposeAsync();
                                else next.Dispose();
                            }
                        }
                        if (complete) scope.Complete();
                    }
                    stage("reuse pooled borrowed connection after commit or rollback");
                    var final = factory.CreateDbContext();
                    try
                    {
                        Assert.Same(native, final.Database.GetDbConnection());
                        Assert.Equal(complete ? 1 : 0, async ? await final.Set<Row>().CountAsync() : final.Set<Row>().Count());
                    }
                    finally
                    {
                        if (async) await final.DisposeAsync();
                        else final.Dispose();
                    }
                    Assert.Equal(System.Data.ConnectionState.Open, native.State);
                    await using var command = native.CreateCommand();
                    command.CommandText = mode == OceanBaseCompatibilityMode.MySql ? "SELECT 1" : "SELECT 1 FROM DUAL";
                    Assert.NotNull(async ? await command.ExecuteScalarAsync() : command.ExecuteScalar());
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
        public Task Disposing_context_before_ambient_completion_preserves_outcome(OceanBaseCompatibilityMode mode, bool async, bool complete)
            => LiveStorageTable.WithTable<Context>(mode, async, false, (options, table) =>
                new Context(new DbContextOptionsBuilder<Context>(options)
                    .ReplaceService<IModelCacheKeyFactory, CacheKey>().Options, table), async (context, _, stage) =>
                {
                    stage("dispose a provider-owned context inside ambient scope, then complete or rollback");
                    var failure = await Record.ExceptionAsync(async () =>
                    {
                        using var scope = new TransactionScope(TransactionScopeOption.Required,
                            new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted }, TransactionScopeAsyncFlowOption.Enabled);
                        var options = new DbContextOptionsBuilder<Context>()
                            .UseOceanBase(context.Database.GetConnectionString()!, mode)
                            .ReplaceService<IModelCacheKeyFactory, CacheKey>().Options;
                        var inner = new Context(options, context.Table);
                        try
                        {
                            inner.Add(new Row { Id = 1 });
                            Assert.Equal(1, async ? await inner.SaveChangesAsync() : inner.SaveChanges());
                        }
                        finally
                        {
                            if (async) await inner.DisposeAsync();
                            else inner.Dispose();
                        }
                        if (complete) scope.Complete();
                    });
                    Assert.True(failure is null,
                        $"Completion after context disposal failed: type={failure?.GetType().Name}; server-code={(failure as OceanBaseException)?.ServerErrorCode}");
                    Assert.Equal(complete ? 1 : 0, async ? await context.Set<Row>().CountAsync() : context.Set<Row>().Count());
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
        public Task Pooled_context_reuse_gets_a_separate_connection_until_old_ambient_completion(OceanBaseCompatibilityMode mode, bool async, bool complete)
            => LiveStorageTable.WithTable<Context>(mode, async, false, (options, table) =>
                new Context(new DbContextOptionsBuilder<Context>(options)
                    .ReplaceService<IModelCacheKeyFactory, CacheKey>().Options, table), async (context, _, stage) =>
                {
                    stage("return context to pool while ambient transaction remains active");
                    var options = new DbContextOptionsBuilder<PooledContext>()
                        .UseOceanBase(context.Database.GetConnectionString()!, mode).UseModel(context.Model).Options;
                    var factory = new PooledDbContextFactory<PooledContext>(options, poolSize: 1);
                    var failure = await Record.ExceptionAsync(async () =>
                    {
                        using (var scope = new TransactionScope(TransactionScopeOption.Required,
                            new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted }, TransactionScopeAsyncFlowOption.Enabled))
                        {
                            var first = factory.CreateDbContext();
                            first.Add(new Row { Id = 1 });
                            Assert.Equal(1, async ? await first.SaveChangesAsync() : first.SaveChanges());
                            if (async) await first.DisposeAsync();
                            else first.Dispose();

                            using (var suppressed = new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled))
                            {
                                var next = factory.CreateDbContext();
                                Assert.Same(first, next);
                                try
                                {
                                    Assert.Equal(0, async ? await next.Set<Row>().CountAsync() : next.Set<Row>().Count());
                                    next.Add(new Row { Id = 2 });
                                    Assert.Equal(1, async ? await next.SaveChangesAsync() : next.SaveChanges());
                                    Assert.Equal(System.Data.ConnectionState.Closed, next.Database.GetDbConnection().State);
                                }
                                finally
                                {
                                    if (async) await next.DisposeAsync();
                                    else next.Dispose();
                                }
                            }
                            if (complete) scope.Complete();
                        }
                    });
                    Assert.True(failure is null,
                        $"Pooled ambient reuse failed: type={failure?.GetType().Name}; server-code={(failure as OceanBaseException)?.ServerErrorCode}");
                    Assert.Equal(complete ? 2 : 1, async ? await context.Set<Row>().CountAsync() : context.Set<Row>().Count());
                });

        private sealed class PooledContext(DbContextOptions<PooledContext> options) : DbContext(options);

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, true)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, true)]
        public Task Explicit_enlistment_retains_connection_before_another_EF_open(OceanBaseCompatibilityMode mode, bool async, bool complete)
            => LiveStorageTable.WithTable<Context>(mode, async, false, (options, table) =>
                new Context(new DbContextOptionsBuilder<Context>(options)
                    .ReplaceService<IModelCacheKeyFactory, CacheKey>().Options, table), async (context, _, stage) =>
                {
                    stage("explicitly enlist, execute native write, close EF lease, then complete or rollback");
                    var failure = await Record.ExceptionAsync(async () =>
                    {
                        using var transaction = new CommittableTransaction(new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted });
                        if (async) await context.Database.OpenConnectionAsync();
                        else context.Database.OpenConnection();
                        context.Database.EnlistTransaction(transaction);
                        Assert.Throws<ArgumentNullException>(() => context.Database.EnlistTransaction(null));
                        Assert.Same(transaction, context.Database.GetEnlistedTransaction());
                        await using (var command = context.Database.GetDbConnection().CreateCommand())
                        {
                            command.CommandText = mode == OceanBaseCompatibilityMode.MySql
                                ? "INSERT INTO `" + context.Table + "` (`Id`) VALUES (1)"
                                : "INSERT INTO \"" + context.Table + "\" (\"Id\") VALUES (1)";
                            Assert.Equal(1, async ? await command.ExecuteNonQueryAsync() : command.ExecuteNonQuery());
                        }
                        if (async) await context.Database.CloseConnectionAsync();
                        else context.Database.CloseConnection();
                        Assert.Equal(System.Data.ConnectionState.Open, context.Database.GetDbConnection().State);
                        if (complete) transaction.Commit();
                        else transaction.Rollback();
                    });
                    Assert.True(failure is null,
                        $"Explicit enlistment failed: type={failure?.GetType().Name}; server-code={(failure as OceanBaseException)?.ServerErrorCode}");
                    Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
                    Assert.Equal(complete ? 1 : 0, async ? await context.Set<Row>().CountAsync() : context.Set<Row>().Count());
                });

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

        private sealed class Row
        {
            public int Id { get; set; }
        }
    }
}
