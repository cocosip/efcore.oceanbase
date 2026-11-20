using System;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.LiveTests
{
    public sealed class LiveInFlightSaveChangesTests
    {
        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public Task Cancel_waiting_update_and_recover(OceanBaseCompatibilityMode mode)
        {
            var entered = new CommandStart();
            return LiveStorageTable.WithTable<ProbeContext>(mode, true, false,
                (options, name) => new(new DbContextOptionsBuilder<ProbeContext>(options)
                    .ReplaceService<IModelCacheKeyFactory, ProbeCacheKey>().AddInterceptors(entered).Options, name),
                async (context, _, stage) =>
                {
                    stage("seed and lock the owned row");
                    context.Add(new Row { Id = 1, Value = "anchor" });
                    await context.SaveChangesAsync();
                    context.ChangeTracker.Clear();

                    var secrets = new ConfigurationBuilder().AddUserSecrets<LiveInFlightSaveChangesTests>().Build();
                    var configured = LiveTestConfiguration.ConnectionString(secrets.GetConnectionString("OceanBase" + mode)!);
                    var observerOptions = new DbContextOptionsBuilder<ProbeContext>().UseOceanBase(configured, mode)
                        .ReplaceService<IModelCacheKeyFactory, ProbeCacheKey>().Options;
                    await using var holder = new ProbeContext(observerOptions, context.Table);
                    await using var held = await holder.Database.BeginTransactionAsync();
                    Assert.Equal(1, await holder.Set<Row>().Where(item => item.Id == 1).ExecuteUpdateAsync(update => update.SetProperty(item => item.Value, "held")));

                    var waiting = new Row { Id = 1, Value = "blocked" };
                    context.Attach(waiting).Property(item => item.Value).IsModified = true;
                    context.Database.AutoTransactionBehavior = AutoTransactionBehavior.Always;
                    context.Database.SetCommandTimeout(15);

                    stage("cancel the in-flight SaveChanges update while the other session holds its row lock");
                    using var cancellation = new CancellationTokenSource();
                    entered.Armed = true;
                    var update = context.SaveChangesAsync(cancellation.Token);
                    Exception? error;
                    try
                    {
                        await entered.Task.WaitAsync(TimeSpan.FromSeconds(8));
                        await Task.Delay(350);
                        Assert.False(update.IsCompleted);
                        cancellation.Cancel();
                        error = await Record.ExceptionAsync(() => update.WaitAsync(TimeSpan.FromSeconds(8)));
                    }
                    finally
                    {
                        cancellation.Cancel();
                        await held.RollbackAsync();
                        await Record.ExceptionAsync(() => update.WaitAsync(TimeSpan.FromSeconds(8)));
                    }

                    Assert.NotNull(error);
                    Assert.IsNotType<TimeoutException>(error);
                    if (error is DbUpdateException writeError)
                    {
                        Assert.Same(waiting, Assert.Single(writeError.Entries).Entity);
                        var nativeError = Assert.IsType<OceanBaseException>(writeError.InnerException);
                        Assert.True(nativeError.IsExecutionOutcomeUnknown);
                        Assert.Equal(cancellation.Token,
                            Assert.IsAssignableFrom<OperationCanceledException>(nativeError.InnerException).CancellationToken);
                    }
                    else
                    {
                        Assert.Equal(cancellation.Token,
                            Assert.IsAssignableFrom<OperationCanceledException>(error).CancellationToken);
                    }

                    stage("check atomicity and reuse the EF connection");
                    context.ChangeTracker.Clear();
                    await context.Database.CloseConnectionAsync();
                    Assert.Null(context.Database.CurrentTransaction);
                    var row = await context.Set<Row>().SingleAsync();
                    Assert.Equal("anchor", row.Value);
                    row.Value = "after";
                    Assert.Equal(1, await context.SaveChangesAsync());
                    Assert.Equal("after", await holder.Set<Row>().AsNoTracking().Select(item => item.Value).SingleAsync());
                });
        }

        private sealed class CommandStart : DbCommandInterceptor
        {
            private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal bool Armed { get; set; }
            internal Task Task => _entered.Task;

            public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
                DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
                CancellationToken cancellationToken = default)
            {
                if (Armed && eventData.CommandSource == CommandSource.SaveChanges)
                    _entered.TrySetResult();
                return ValueTask.FromResult(result);
            }
        }

        private sealed class ProbeContext(DbContextOptions<ProbeContext> options, string table) : DbContext(options)
        {
            internal string Table { get; } = table;
            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                var row = modelBuilder.Entity<Row>().ToTable(Table);
                row.Property(item => item.Id).ValueGeneratedNever();
                row.Property(item => item.Value).HasMaxLength(32);
            }
        }

        private sealed class ProbeCacheKey : IModelCacheKeyFactory
        {
            public object Create(DbContext context, bool designTime) => (((ProbeContext)context).Table, designTime);
        }

        private sealed class Row
        {
            public int Id { get; set; }
            public string Value { get; set; } = "";
        }
    }
}
