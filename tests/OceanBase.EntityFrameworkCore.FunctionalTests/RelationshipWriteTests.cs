using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace OceanBase.EntityFrameworkCore.FunctionalTests
{
    public sealed class RelationshipWriteTests
    {
        public static IEnumerable<object[]> Modes() => TransactionTests.Modes();

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Optional_severing_writes_shadow_FK_NULL_with_original_concurrency(OceanBaseCompatibilityMode mode, bool async)
        {
            foreach (var version in new int?[] { null, 7 })
            {
                var commands = new Commands();
                await using var context = Create(mode, commands);
                var owner = new Owner { Id = 11 };
                var item = new OptionalItem { Id = 21, Owner = owner, Version = version };
                context.Attach(item);
                Assert.Equal(11, context.Entry(item).Property<int?>("OwnerId").OriginalValue);
                item.Owner = null;
                Assert.Equal(1, await Save(context, async));
                var command = Assert.Single(commands.Items);
                Assert.Equal("UPDATE OptionalItems SET OwnerRef = ? WHERE ItemKey = ? AND Version "
                    + (version is null ? "IS NULL;" : "= ?;"), command.Sql);
                Assert.Equal(version is null ? new object?[] { DBNull.Value, 21 } : [DBNull.Value, 21, 7], command.Inputs);
                Assert.Null(context.Entry(item).Property<int?>("OwnerId").CurrentValue);
                Assert.Null(context.Entry(item).Property<int?>("OwnerId").OriginalValue);
                Assert.Equal(EntityState.Unchanged, context.Entry(item).State);
                Assert.Equal(EntityState.Unchanged, context.Entry(owner).State);
                Assert.Empty(owner.OptionalItems);
                AssertClean(context);
            }
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task ClientSetNull_updates_tracked_dependents_before_deleting_principal(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new Commands();
            await using var context = Create(mode, commands);
            var owner = new Owner { Id = 11 };
            var item = new OptionalItem { Id = 21, Owner = owner, Version = 7 };
            context.Attach(item);
            context.Remove(owner);
            Assert.Equal(2, await Save(context, async));
            Assert.Equal(new[] { "UPDATE OptionalItems SET OwnerRef = ? WHERE ItemKey = ? AND Version = ?;",
                "DELETE FROM Owners WHERE OwnerKey = ?;" }, commands.Items.Select(c => c.Sql));
            Assert.Equal(new object?[] { DBNull.Value, 21, 7 }, commands.Items[0].Inputs);
            Assert.Equal(new object?[] { 11 }, commands.Items[1].Inputs);
            Assert.Null(item.Owner);
            Assert.Null(context.Entry(item).Property<int?>("OwnerId").OriginalValue);
            Assert.Equal(EntityState.Unchanged, context.Entry(item).State);
            Assert.Equal(EntityState.Detached, context.Entry(owner).State);
            AssertCommitted(context, commands);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Required_reparenting_preserves_dependent_and_original_concurrency(OceanBaseCompatibilityMode mode, bool async)
        {
            foreach (var timing in new[] { CascadeTiming.Immediate, CascadeTiming.OnSaveChanges })
            foreach (var throughCollections in new[] { false, true })
            {
                var commands = new Commands();
                await using var context = Create(mode, commands);
                context.ChangeTracker.DeleteOrphansTiming = timing;
                var first = new Owner { Id = 11 };
                var second = new Owner { Id = 12 };
                var item = new RequiredItem { Id = 31, Owner = first, Version = 7 };
                context.Attach(item);
                context.Attach(second);
                if (throughCollections)
                {
                    first.RequiredItems.Remove(item);
                    second.RequiredItems.Add(item);
                }
                else item.Owner = second;
                context.ChangeTracker.DetectChanges();
                var revived = throughCollections && timing == CascadeTiming.Immediate;
                Assert.Equal(EntityState.Modified, context.Entry(item).State);
                Assert.Equal(revived, context.Entry(item).Property(i => i.Version).IsModified);
                Assert.Equal(7, context.Entry(item).Property(i => i.Version).OriginalValue);
                Assert.Equal(1, await Save(context, async));
                var command = Assert.Single(commands.Items);
                Assert.Equal(revived
                    ? "UPDATE RequiredItems SET OwnerRef = ?, Version = ? WHERE ItemKey = ? AND Version = ?;"
                    : "UPDATE RequiredItems SET OwnerRef = ? WHERE ItemKey = ? AND Version = ?;", command.Sql);
                Assert.Equal(revived ? new object?[] { 12, 7, 31, 7 } : [12, 31, 7], command.Inputs);
                Assert.Same(second, item.Owner);
                Assert.Same(item, Assert.Single(second.RequiredItems));
                Assert.Empty(first.RequiredItems);
                Assert.Equal(12, context.Entry(item).Property(i => i.OwnerId).OriginalValue);
                Assert.All(context.ChangeTracker.Entries(), e => Assert.Equal(EntityState.Unchanged, e.State));
                AssertClean(context);
            }
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Required_orphan_timing_deletes_the_dependent_at_save(OceanBaseCompatibilityMode mode, bool async)
        {
            foreach (var timing in new[] { CascadeTiming.Immediate, CascadeTiming.OnSaveChanges })
            {
                var commands = new Commands();
                await using var context = Create(mode, commands);
                context.ChangeTracker.DeleteOrphansTiming = timing;
                var owner = new Owner { Id = 11 };
                var item = new RequiredItem { Id = 31, Owner = owner, Version = 7 };
                context.Attach(item);
                owner.RequiredItems.Clear();
                context.ChangeTracker.DetectChanges();
                Assert.Equal(timing == CascadeTiming.Immediate ? EntityState.Deleted : EntityState.Modified, context.Entry(item).State);
                Assert.Empty(commands.Items);
                Assert.Equal(1, await Save(context, async));
                var command = Assert.Single(commands.Items);
                Assert.Equal("DELETE FROM RequiredItems WHERE ItemKey = ? AND Version = ?;", command.Sql);
                Assert.Equal(new object?[] { 31, 7 }, command.Inputs);
                Assert.Equal(EntityState.Detached, context.Entry(item).State);
                Assert.Equal(EntityState.Unchanged, context.Entry(owner).State);
                AssertClean(context);
            }
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Never_orphan_timing_rejects_before_open_and_explicit_cascade_recovers(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new Commands();
            await using var context = Create(mode, commands);
            context.ChangeTracker.DeleteOrphansTiming = CascadeTiming.Never;
            var owner = new Owner { Id = 11 };
            var item = new RequiredItem { Id = 31, Owner = owner, Version = 7 };
            context.Attach(item);
            owner.RequiredItems.Clear();
            await Assert.ThrowsAsync<InvalidOperationException>(() => Save(context, async));
            Assert.Empty(commands.Items);
            Assert.Empty(Native(context).Transactions);
            Assert.Equal(0, Native(context).Closes);
            AssertClean(context);
            context.ChangeTracker.CascadeChanges();
            Assert.Equal(1, await Save(context, async));
            var command = Assert.Single(commands.Items);
            Assert.Equal("DELETE FROM RequiredItems WHERE ItemKey = ? AND Version = ?;", command.Sql);
            Assert.Equal(new object?[] { 31, 7 }, command.Inputs);
            Assert.Equal(EntityState.Detached, context.Entry(item).State);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Unique_one_to_one_replacement_deletes_old_before_insert_and_accepts_as_requested(OceanBaseCompatibilityMode mode, bool async)
        {
            foreach (var accept in new[] { false, true })
            {
                var commands = new Commands();
                await using var context = Create(mode, commands);
                var (owner, old, replacement) = Replace(context);
                Assert.Equal(2, await Save(context, async, accept));
                AssertReplacement(commands.Items);
                Assert.Same(replacement, owner.Exclusive);
                Assert.Null(old.Owner);
                Assert.Equal(11, replacement.OwnerId);
                Assert.Equal(accept ? EntityState.Detached : EntityState.Deleted, context.Entry(old).State);
                Assert.Equal(accept ? EntityState.Unchanged : EntityState.Added, context.Entry(replacement).State);
                if (!accept)
                {
                    context.ChangeTracker.AcceptAllChanges();
                    Assert.Equal(EntityState.Detached, context.Entry(old).State);
                    Assert.Equal(EntityState.Unchanged, context.Entry(replacement).State);
                }
                AssertCommitted(context, commands);
            }
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Later_replacement_conflict_preserves_both_entries_and_can_retry(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new Commands { FailOnWrite = 2 };
            await using var context = Create(mode, commands);
            var (_, old, replacement) = Replace(context);
            var failure = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => Save(context, async));
            Assert.Same(replacement, Assert.Single(failure.Entries).Entity);
            AssertReplacement(commands.Items);
            AssertPending(context, old, replacement);
            var failed = Assert.Single(Native(context).Transactions);
            Assert.Equal(0, failed.Commits);
            Assert.True(failed.DisposedActive);
            AssertClean(context);
            commands.FailOnWrite = null;
            commands.Items.Clear();
            Assert.Equal(2, await Save(context, async));
            AssertReplacement(commands.Items);
            Assert.Equal(EntityState.Detached, context.Entry(old).State);
            Assert.Equal(EntityState.Unchanged, context.Entry(replacement).State);
            Assert.Equal(1, Native(context).Transactions[1].Commits);
            AssertClean(context);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Later_replacement_cancellation_keeps_original_token_and_pending_entries(OceanBaseCompatibilityMode mode, bool async)
        {
            using var source = new CancellationTokenSource();
            var error = new OperationCanceledException(source.Token);
            var commands = new Commands { FailOnWrite = 2, Failure = error, BeforeFailure = source.Cancel };
            await using var context = Create(mode, commands);
            var (_, old, replacement) = Replace(context);
            var failure = await Assert.ThrowsAsync<OperationCanceledException>(() => Save(context, async, token: source.Token));
            Assert.Same(error, failure);
            Assert.True(source.IsCancellationRequested);
            if (async) Assert.All(commands.Items, c => Assert.Equal(source.Token, c.Token));
            AssertPending(context, old, replacement);
            Assert.True(Assert.Single(Native(context).Transactions).DisposedActive);
            AssertClean(context);
            commands.FailOnWrite = null;
            Assert.Equal(2, await Save(context, async));
            Assert.Equal(EntityState.Detached, context.Entry(old).State);
            Assert.Equal(EntityState.Unchanged, context.Entry(replacement).State);
            AssertClean(context);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Caller_transaction_replacement_failure_rolls_back_savepoint_and_preserves_ownership(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new Commands { FailOnWrite = 2 };
            await using var context = Create(mode, commands);
            var (_, old, replacement) = Replace(context);
            await using var transaction = async ? await context.Database.BeginTransactionAsync() : context.Database.BeginTransaction();
            var physical = Assert.IsType<TransactionTests.NativeTransaction>(transaction.GetDbTransaction());
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => Save(context, async));
            AssertPending(context, old, replacement);
            Assert.Same(transaction, context.Database.CurrentTransaction);
            Assert.Equal(ConnectionState.Open, Native(context).State);
            Assert.Equal(0, physical.Commits);
            Assert.Equal(0, physical.Disposals);
            Assert.Contains(Native(context).Commands, c => c.Sql.StartsWith("ROLLBACK TO", StringComparison.Ordinal));
            Assert.All(commands.Items, c => Assert.Same(physical, c.Transaction));
            commands.FailOnWrite = null;
            commands.Items.Clear();
            Assert.Equal(2, await Save(context, async));
            AssertReplacement(commands.Items);
            Assert.Same(transaction, context.Database.CurrentTransaction);
            Assert.Equal(0, physical.Commits);
            Assert.Single(Native(context).Transactions);
            if (async) await transaction.CommitAsync(); else transaction.Commit();
            Assert.Equal(1, physical.Commits);
            AssertClean(context);
        }

        private static Task<int> Save(DbContext context, bool async, bool accept = true, CancellationToken token = default)
            => async ? context.SaveChangesAsync(accept, token) : Task.FromResult(context.SaveChanges(accept));

        private static (Owner Owner, ExclusiveItem Old, ExclusiveItem Replacement) Replace(RelationshipContext context)
        {
            var owner = new Owner { Id = 11 };
            var old = new ExclusiveItem { Id = 41, Owner = owner, Version = 7, Text = "old" };
            owner.Exclusive = old;
            context.Attach(owner);
            var replacement = new ExclusiveItem { Id = 42, Text = "replacement" };
            owner.Exclusive = replacement;
            return (owner, old, replacement);
        }

        private static void AssertReplacement(IReadOnlyList<Command> commands)
        {
            Assert.Equal(new[] { "DELETE FROM ExclusiveItems WHERE ItemKey = ? AND Version = ?;",
                "INSERT INTO ExclusiveItems (ItemKey, OwnerRef, Text, Version) VALUES (?, ?, ?, ?);" }, commands.Select(c => c.Sql));
            Assert.Equal(new object?[] { 41, 7 }, commands[0].Inputs);
            Assert.Equal(new object?[] { 42, 11, "replacement", DBNull.Value }, commands[1].Inputs);
        }

        private static void AssertPending(RelationshipContext context, ExclusiveItem old, ExclusiveItem replacement)
        {
            Assert.Equal(EntityState.Deleted, context.Entry(old).State);
            Assert.Equal(EntityState.Added, context.Entry(replacement).State);
            Assert.Equal(7, context.Entry(old).Property(i => i.Version).OriginalValue);
            Assert.Equal(11, context.Entry(replacement).Property(i => i.OwnerId).CurrentValue);
        }

        private static void AssertCommitted(RelationshipContext context, Commands commands)
        {
            var transaction = Assert.Single(Native(context).Transactions);
            Assert.Equal(1, transaction.Commits);
            Assert.Equal(1, transaction.Disposals);
            Assert.All(commands.Items, c => Assert.Same(transaction, c.Transaction));
            AssertClean(context);
        }

        private static void AssertClean(DbContext context)
        {
            Assert.Null(context.Database.CurrentTransaction);
            Assert.Equal(ConnectionState.Closed, Native(context).State);
        }

        private static TransactionTests.NativeConnection Native(DbContext context)
            => Assert.IsType<TransactionTests.NativeConnection>(context.Database.GetDbConnection());

        private static RelationshipContext Create(OceanBaseCompatibilityMode mode, Commands commands)
            => new(new DbContextOptionsBuilder<RelationshipContext>().UseOceanBase("Server=offline;Database=app", mode)
                .ReplaceService<IRelationalConnection, TransactionTests.TestConnection>()
                .AddInterceptors(commands).Options);

        private sealed class RelationshipContext(DbContextOptions<RelationshipContext> options) : DbContext(options)
        {
            protected override void OnModelCreating(ModelBuilder model)
            {
                var owner = model.Entity<Owner>();
                owner.ToTable("Owners");
                owner.Property(o => o.Id).HasColumnName("OwnerKey").ValueGeneratedNever();
                var optional = model.Entity<OptionalItem>();
                optional.ToTable("OptionalItems");
                optional.Property(i => i.Id).HasColumnName("ItemKey").ValueGeneratedNever();
                optional.Property<int?>("OwnerId").HasColumnName("OwnerRef");
                optional.Property(i => i.Version).IsConcurrencyToken();
                optional.HasOne(i => i.Owner).WithMany(o => o.OptionalItems).HasForeignKey("OwnerId").OnDelete(DeleteBehavior.ClientSetNull);
                var required = model.Entity<RequiredItem>();
                required.ToTable("RequiredItems");
                required.Property(i => i.Id).HasColumnName("ItemKey").ValueGeneratedNever();
                required.Property(i => i.OwnerId).HasColumnName("OwnerRef");
                required.Property(i => i.Version).IsConcurrencyToken();
                required.HasOne(i => i.Owner).WithMany(o => o.RequiredItems).HasForeignKey(i => i.OwnerId).OnDelete(DeleteBehavior.Cascade);
                var exclusive = model.Entity<ExclusiveItem>();
                exclusive.ToTable("ExclusiveItems");
                exclusive.Property(i => i.Id).HasColumnName("ItemKey").ValueGeneratedNever();
                exclusive.Property(i => i.OwnerId).HasColumnName("OwnerRef");
                exclusive.Property(i => i.Version).IsConcurrencyToken();
                exclusive.Property(i => i.Text).HasMaxLength(40);
                exclusive.HasOne(i => i.Owner).WithOne(o => o.Exclusive).HasForeignKey<ExclusiveItem>(i => i.OwnerId).OnDelete(DeleteBehavior.Cascade);
            }
        }

        private sealed class Owner
        {
            public int Id { get; set; }
            public List<OptionalItem> OptionalItems { get; set; } = [];
            public List<RequiredItem> RequiredItems { get; set; } = [];
            public ExclusiveItem? Exclusive { get; set; }
        }
        private sealed class OptionalItem { public int Id { get; set; } public Owner? Owner { get; set; } public int? Version { get; set; } }
        private sealed class RequiredItem { public int Id { get; set; } public int OwnerId { get; set; } public Owner Owner { get; set; } = null!; public int? Version { get; set; } }
        private sealed class ExclusiveItem { public int Id { get; set; } public int OwnerId { get; set; } public Owner Owner { get; set; } = null!; public int? Version { get; set; } public string? Text { get; set; } }

        private sealed record Command(string Sql, object?[] Inputs, DbTransaction? Transaction, CancellationToken Token);
        private sealed class Commands : DbCommandInterceptor
        {
            private int _writes;
            public List<Command> Items { get; } = [];
            public int? FailOnWrite { get; set; }
            public Exception? Failure { get; init; }
            public Action? BeforeFailure { get; init; }
            private InterceptionResult<int> Write(DbCommand command, CancellationToken token, InterceptionResult<int> result)
            {
                var parameters = command.Parameters.Cast<DbParameter>().ToArray();
                var position = 0;
                var used = new HashSet<DbParameter>();
                var inputs = Regex.Matches(command.CommandText, @"[@:]\w+|\?").Select(match =>
                {
                    var parameter = match.Value == "?" ? parameters[position++] : parameters.Single(p => p.ParameterName.TrimStart('@', ':') == match.Value.TrimStart('@', ':'));
                    used.Add(parameter);
                    return parameter.Value;
                }).ToArray();
                Assert.Equal(parameters.Length, used.Count);
                var sql = Regex.Replace(Regex.Replace(command.CommandText.Replace("`", "", StringComparison.Ordinal)
                    .Replace("\"", "", StringComparison.Ordinal).Trim(), @"[@:]\w+", "?"), @"\s+", " ");
                Items.Add(new Command(sql, inputs, command.Transaction, token));
                if (++_writes != FailOnWrite) return result;
                BeforeFailure?.Invoke();
                if (Failure is not null) throw Failure;
                return InterceptionResult<int>.SuppressWithResult(0);
            }
            public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData data, InterceptionResult<int> result)
                => Write(command, default, result);
            public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<int> result,
                CancellationToken cancellationToken = default) => ValueTask.FromResult(Write(command, cancellationToken, result));
        }
    }
}
