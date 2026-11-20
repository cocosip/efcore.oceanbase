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
using Microsoft.EntityFrameworkCore.Storage;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.LiveTests;

public sealed class LiveTransactionTests
{
    public static IEnumerable<object[]> WriteCases()
    {
        foreach (var mode in Enum.GetValues<OceanBaseCompatibilityMode>())
        foreach (var callerTransaction in new[] { false, true })
        {
            yield return [mode, callerTransaction, false, false];
            yield return [mode, callerTransaction, true, false];
            yield return [mode, callerTransaction, true, true];
        }
    }

    [Theory]
    [MemberData(nameof(WriteCases))]
    public async Task SaveChanges_rolls_back_completed_commands_after_later_failure(
        OceanBaseCompatibilityMode mode, bool callerTransaction, bool async, bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        var writes = new CompletedWrites();
        await LiveStorageTable.WithTable<Context>(mode, async, false, (options, table) =>
                new Context(new DbContextOptionsBuilder<Context>(options).AddInterceptors(writes)
                    .ReplaceService<IModelCacheKeyFactory, CacheKey>().Options, table), async (context, _, stage) =>
                {
                    stage("seed the committed anchor");
                    context.Add(new Row { Id = 1, Label = "anchor" });
                    Assert.Equal(1, await Save());

                    await using var transaction = !callerTransaction ? null : async
                        ? await context.Database.BeginTransactionAsync()
                        : context.Database.BeginTransaction();
                    if (transaction is not null)
                    {
                        stage("write before the automatic savepoint");
                        context.Add(new Row { Id = 2, Label = "outer" });
                        Assert.Equal(1, await Save());
                    }

                    stage(cancel ? "cancel after the first completed INSERT" : "fail a later INSERT at the unique constraint");
                    var parent = new Row { Id = 3, Label = "parent" };
                    var child = new Row { Id = 4, Parent = parent, Label = cancel ? "child" : "anchor" };
                    context.AddRange(child, parent);
                    writes.Observing = true;
                    if (cancel) writes.AfterFirstWrite = cancellation.Cancel;
                    try
                    {
                        if (cancel)
                        {
                            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Save(cancellation.Token));
                            Assert.Equal(cancellation.Token, error.CancellationToken);
                        }
                        else
                        {
                            var error = await Assert.ThrowsAsync<DbUpdateException>(() => Save());
                            Assert.Equal(mode == OceanBaseCompatibilityMode.MySql ? 1062 : 1,
                                Assert.IsType<OceanBaseException>(error.InnerException).ServerErrorCode);
                            Assert.Same(child, Assert.Single(error.Entries).Entity);
                        }
                    }
                    finally
                    {
                        writes.Observing = false;
                    }

                    Assert.Equal(1, writes.Count);
                    var nativeTransaction = Assert.IsType<OceanBaseTransaction>(writes.Transaction);
                    if (transaction is not null) Assert.Same(transaction.GetDbTransaction(), nativeTransaction);

                    stage("check rollback and reuse the context");
                    context.ChangeTracker.Clear();
                    Assert.Same(transaction, context.Database.CurrentTransaction);
                    Assert.Equal(callerTransaction ? ConnectionState.Open : ConnectionState.Closed,
                        context.Database.GetDbConnection().State);
                    Assert.Equal(callerTransaction ? [1, 2] : new[] { 1 }, await Read());
                    context.Add(new Row { Id = 5, Label = "recovered" });
                    Assert.Equal(1, await Save());

                    if (transaction is not null)
                    {
                        stage("commit the recovered caller transaction");
                        if (async) await transaction.CommitAsync();
                        else transaction.Commit();
                    }

                    Assert.Null(context.Database.CurrentTransaction);
                    Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
                    Assert.Equal(callerTransaction ? [1, 2, 5] : new[] { 1, 5 }, await Read());

                    Task<int> Save(CancellationToken token = default) => async
                        ? context.SaveChangesAsync(token)
                        : Task.FromResult(context.SaveChanges());

                    Task<int[]> Read()
                    {
                        var query = context.Set<Row>().AsNoTracking().OrderBy(row => row.Id).Select(row => row.Id);
                        return async ? query.ToArrayAsync() : Task.FromResult(query.ToArray());
                    }
                });
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public Task Borrowed_transaction_can_rollback_after_EF_releases_it(OceanBaseCompatibilityMode mode, bool async)
        => LiveStorageTable.WithTable<Context>(mode, async, false, (options, table) =>
            new Context(new DbContextOptionsBuilder<Context>(options)
                .ReplaceService<IModelCacheKeyFactory, CacheKey>().Options, table), async (context, _, stage) =>
            {
                stage("open a caller-owned native transaction");
                var connection = context.Database.GetDbConnection();
                if (async) await connection.OpenAsync();
                else connection.Open();
                await using var physical = async ? await connection.BeginTransactionAsync() : connection.BeginTransaction();
                var transaction = async ? await context.Database.UseTransactionAsync(physical) : context.Database.UseTransaction(physical);
                Assert.NotNull(transaction);
                Assert.Same(physical, transaction.GetDbTransaction());
                try
                {
                    stage("write and read inside the borrowed transaction");
                    context.Add(new Row { Id = 1, Label = "rollback" });
                    Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
                    Assert.Equal(1, async ? await context.Set<Row>().CountAsync() : context.Set<Row>().Count());
                }
                finally
                {
                    if (async) await transaction.DisposeAsync();
                    else transaction.Dispose();
                }

                stage("roll back the released native transaction");
                Assert.Null(context.Database.CurrentTransaction);
                Assert.Equal(ConnectionState.Open, connection.State);
                if (async)
                {
                    await physical.RollbackAsync();
                    await connection.CloseAsync();
                }
                else
                {
                    physical.Rollback();
                    connection.Close();
                }

                context.ChangeTracker.Clear();
                Assert.False(async ? await context.Set<Row>().AnyAsync() : context.Set<Row>().Any());
            });

    private sealed class CompletedWrites : DbCommandInterceptor
    {
        public bool Observing { get; set; }
        public Action? AfterFirstWrite { get; set; }
        public int Count { get; private set; }
        public DbTransaction? Transaction { get; private set; }

        public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
        {
            if (Observing && eventData.CommandSource == CommandSource.SaveChanges && result == 1 && ++Count == 1)
            {
                Transaction = command.Transaction;
                AfterFirstWrite?.Invoke();
            }
            return result;
        }

        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(NonQueryExecuted(command, eventData, result));
    }

    private sealed class Context(DbContextOptions<Context> options, string table) : DbContext(options)
    {
        public string Table { get; } = table;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var entity = modelBuilder.Entity<Row>().ToTable(Table);
            entity.Property(row => row.Id).ValueGeneratedNever();
            entity.Property(row => row.Label).HasMaxLength(32);
            entity.HasIndex(row => row.Label).HasDatabaseName(Table + "_U").IsUnique();
            entity.HasOne(row => row.Parent).WithMany().HasForeignKey(row => row.ParentId)
                .HasConstraintName(Table + "_F").OnDelete(DeleteBehavior.NoAction);
        }
    }

    private sealed class CacheKey : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => (((Context)context).Table, designTime);
    }

    private sealed class Row
    {
        public int Id { get; set; }
        public int? ParentId { get; set; }
        public Row? Parent { get; set; }
        public string Label { get; set; } = "";
    }
}
