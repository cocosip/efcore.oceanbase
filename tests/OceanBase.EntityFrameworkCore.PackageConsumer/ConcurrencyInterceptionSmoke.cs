using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OceanBase.EntityFrameworkCore;

namespace PackageConsumer
{
    internal static class ConcurrencyInterceptionSmoke
    {
        public static void Run(OceanBaseCompatibilityMode mode)
        {
            foreach (var async in new[] { false, true })
                foreach (var suppress in new[] { false, true })
                {
                    var interceptor = new ConflictInterceptor(suppress);
                    using var context = new ConflictContext(new DbContextOptionsBuilder<ConflictContext>()
                        .UseOceanBase("Server=offline;Database=offline", mode)
                        .AddInterceptors(new NoNetwork(), new StaleWrite(), interceptor).Options);
                    context.Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
                    var row = new ConflictRow { Id = 7, Name = "before", Version = 11 };
                    context.Attach(row);
                    row.Name = "after";
                    using var source = new CancellationTokenSource();
                    DbUpdateConcurrencyException? thrown = null;
                    try
                    {
                        var count = async ? context.SaveChangesAsync(source.Token).GetAwaiter().GetResult() : context.SaveChanges();
                        if (!suppress || count != 1) throw new Exception("Packed concurrency suppression returned an unexpected result.");
                    }
                    catch (DbUpdateConcurrencyException error) { thrown = error; }
                    var payload = interceptor.Payload;
                    if (payload is null || !ReferenceEquals(payload.Context, context)
                        || payload.Entries.Count != 1 || !ReferenceEquals(payload.Entries[0].Entity, row)
                        || (!suppress && !ReferenceEquals(thrown, payload.Exception)) || (suppress && thrown is not null)
                        || interceptor.SyncCalls != (async ? 0 : 1) || interceptor.AsyncCalls != (async ? 1 : 0)
                        || (async && interceptor.Token != source.Token)
                        || context.Entry(row).State != (suppress ? EntityState.Unchanged : EntityState.Modified))
                    {
                        throw new Exception("Packed concurrency interception did not preserve the public EF contract.");
                    }
                }
        }

        private sealed class ConflictContext(DbContextOptions<ConflictContext> options) : DbContext(options)
        {
            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity<ConflictRow>().Property(row => row.Id).ValueGeneratedNever();
                modelBuilder.Entity<ConflictRow>().Property(row => row.Version).IsConcurrencyToken();
            }
        }

        private sealed class ConflictRow
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public int Version { get; set; }
        }

        private sealed class ConflictInterceptor(bool suppress) : SaveChangesInterceptor
        {
            public ConcurrencyExceptionEventData? Payload { get; private set; }
            public int SyncCalls { get; private set; }
            public int AsyncCalls { get; private set; }
            public CancellationToken Token { get; private set; }
            public override InterceptionResult ThrowingConcurrencyException(ConcurrencyExceptionEventData eventData, InterceptionResult result)
            { SyncCalls++; Payload = eventData; return suppress ? InterceptionResult.Suppress() : result; }
            public override ValueTask<InterceptionResult> ThrowingConcurrencyExceptionAsync(ConcurrencyExceptionEventData eventData,
                InterceptionResult result, CancellationToken cancellationToken = default)
            { AsyncCalls++; Payload = eventData; Token = cancellationToken; return ValueTask.FromResult(suppress ? InterceptionResult.Suppress() : result); }
        }

        private sealed class StaleWrite : DbCommandInterceptor
        {
            public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData,
                InterceptionResult<int> result) => InterceptionResult<int>.SuppressWithResult(0);
            public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
                InterceptionResult<int> result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(0));
        }

        private sealed class NoNetwork : DbConnectionInterceptor
        {
            public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData,
                InterceptionResult result) => InterceptionResult.Suppress();
            public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
                InterceptionResult result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(InterceptionResult.Suppress());
        }
    }
}
