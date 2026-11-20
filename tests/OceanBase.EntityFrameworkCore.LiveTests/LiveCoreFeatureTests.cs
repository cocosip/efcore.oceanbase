using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace OceanBase.EntityFrameworkCore.LiveTests
{
    public sealed class LiveCoreFeatureTests
    {
        public static IEnumerable<object[]> Cases()
        {
            foreach (var mode in new[] { OceanBaseCompatibilityMode.MySql, OceanBaseCompatibilityMode.Oracle })
            foreach (var async in new[] { false, true })
                yield return [mode, async];
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public Task Bulk_updates_and_deletes_preserve_original_values_and_affected_rows(OceanBaseCompatibilityMode mode, bool async)
            => WithTables(mode, async, Kind.Bulk, [""], async (context, stage) =>
            {
                stage("bulk seed");
                context.AddRange(new BulkRow { Id = 1, SourceId = 2, Count = 10, Previous = 100, Name = "one" },
                    new BulkRow { Id = 2, SourceId = 1, Count = 20, Previous = 200, Name = "two" },
                    new BulkRow { Id = 3, SourceId = 2, Count = 30, Previous = 300, Name = "three" });
                Assert.Equal(3, await Save(context, async));

                stage("filtered ExecuteUpdate and fresh values");
                var filtered = context.Set<BulkRow>().Where(row => row.Id <= 2);
                var name = "bound";
                Assert.Equal(2, async
                    ? await filtered.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Count, row => row.Count + 1)
                        .SetProperty(row => row.Previous, row => row.Count).SetProperty(row => row.Name, name))
                    : filtered.ExecuteUpdate(setters => setters.SetProperty(row => row.Count, row => row.Count + 1)
                        .SetProperty(row => row.Previous, row => row.Count).SetProperty(row => row.Name, name)));
                var rows = await Read(context.Set<BulkRow>().AsNoTracking().OrderBy(row => row.Id), async);
                Assert.Equal(new[] { (11, 10, "bound"), (21, 20, "bound"), (30, 300, "three") },
                    rows.Select(row => (row.Count, row.Previous, row.Name)));

                stage("joined ExecuteUpdate with key correlation");
                var joined = context.Set<BulkRow>().Where(row => row.Id == 1)
                    .Join(context.Set<BulkRow>(), row => row.SourceId, source => source.Id,
                        (row, source) => new { Target = row, Source = source });
                Assert.Equal(1, async
                    ? await joined.ExecuteUpdateAsync(setters => setters.SetProperty(pair => pair.Target.Count, pair => pair.Source.Count + 5)
                        .SetProperty(pair => pair.Target.Name, pair => pair.Source.Name))
                    : joined.ExecuteUpdate(setters => setters.SetProperty(pair => pair.Target.Count, pair => pair.Source.Count + 5)
                        .SetProperty(pair => pair.Target.Name, pair => pair.Source.Name)));
                rows = await Read(context.Set<BulkRow>().AsNoTracking().OrderBy(row => row.Id), async);
                Assert.Equal(new[] { (26, 10), (21, 20), (30, 300) }, rows.Select(row => (row.Count, row.Previous)));
                Assert.Equal("bound", rows[0].Name);

                stage("cyclic two-column assignment uses original values");
                Assert.Equal(3, async
                    ? await context.Set<BulkRow>().ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Count, row => row.Previous)
                        .SetProperty(row => row.Previous, row => row.Count))
                    : context.Set<BulkRow>().ExecuteUpdate(setters => setters.SetProperty(row => row.Count, row => row.Previous)
                        .SetProperty(row => row.Previous, row => row.Count)));
                rows = await Read(context.Set<BulkRow>().AsNoTracking().OrderBy(row => row.Id), async);
                Assert.Equal(new[] { (10, 26), (20, 21), (300, 30) }, rows.Select(row => (row.Count, row.Previous)));

                stage("filtered ExecuteDelete and surviving row");
                var deleted = context.Set<BulkRow>().Where(row => row.Id >= 2);
                Assert.Equal(2, async ? await deleted.ExecuteDeleteAsync() : deleted.ExecuteDelete());
                Assert.Equal(1, Assert.Single(await Read(context.Set<BulkRow>().AsNoTracking(), async)).Id);
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public Task Tpt_identity_three_table_CRUD_and_rollback_remain_consistent(OceanBaseCompatibilityMode mode, bool async)
            => WithTables(mode, async, Kind.Hierarchy, ["A", "B", "C"], async (context, stage) =>
            {
                stage("TPT root identity propagation");
                var sensor = new Sensor { Name = "sensor", Serial = 7, Reading = 2 };
                context.Add(sensor);
                Assert.Equal(3, await Save(context, async));
                Assert.True(sensor.Id > 0);
                var id = sensor.Id;
                context.ChangeTracker.Clear();
                var root = Assert.Single(await Read(context.Set<Asset>(), async));
                var loaded = Assert.IsType<Sensor>(root);
                Assert.Equal((id, "sensor", 7, 2), (loaded.Id, loaded.Name, loaded.Serial, loaded.Reading));
                Assert.Equal(new[] { 1, 1, 1 }, await TableCounts(context, async, "A", "B", "C"));

                stage("TPT root and leaf owning-table updates");
                loaded.Name = "changed";
                loaded.Reading = null;
                Assert.Equal(2, await Save(context, async));
                context.ChangeTracker.Clear();
                loaded = Assert.Single(await Read(context.Set<Sensor>(), async));
                Assert.Equal("changed", loaded.Name);
                Assert.Null(loaded.Reading);

                stage("TPT rollback restores all tables after insert and update");
                await using (var transaction = async ? await context.Database.BeginTransactionAsync() : context.Database.BeginTransaction())
                {
                    loaded.Name = "rolled-back";
                    loaded.Reading = 99;
                    context.Add(new Sensor { Name = "transient", Serial = 8, Reading = 9 });
                    Assert.Equal(5, await Save(context, async));
                    Assert.Equal(new[] { 2, 2, 2 }, await TableCounts(context, async, "A", "B", "C"));
                    if (async) await transaction.RollbackAsync();
                    else transaction.Rollback();
                }
                context.ChangeTracker.Clear();
                loaded = Assert.Single(await Read(context.Set<Sensor>(), async));
                Assert.Equal((id, "changed", 7), (loaded.Id, loaded.Name, loaded.Serial));
                Assert.Null(loaded.Reading);
                Assert.Equal(new[] { 1, 1, 1 }, await TableCounts(context, async, "A", "B", "C"));

                stage("TPT reverse dependency delete");
                context.Remove(loaded);
                Assert.Equal(3, await Save(context, async));
                Assert.Equal(new[] { 0, 0, 0 }, await TableCounts(context, async, "A", "B", "C"));
                Assert.Empty(await Read(context.Set<Asset>().AsNoTracking(), async));
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public Task Entity_and_optional_table_splitting_preserve_CRUD_and_shared_concurrency(OceanBaseCompatibilityMode mode, bool async)
            => WithTables(mode, async, Kind.Splitting, ["A", "B", "C"], async (context, stage) =>
            {
                stage("entity split identity and fresh materialization");
                var row = new SplitRow { Name = "row", Text = "detail" };
                context.Add(row);
                Assert.Equal(2, await Save(context, async));
                Assert.True(row.Id > 0);
                context.ChangeTracker.Clear();
                row = Assert.Single(await Read(context.Set<SplitRow>(), async));
                Assert.Equal(("row", "detail"), (row.Name, row.Text));
                row.Name = "changed";
                row.Text = null;
                Assert.Equal(2, await Save(context, async));
                context.ChangeTracker.Clear();
                row = Assert.Single(await Read(context.Set<SplitRow>(), async));
                Assert.Equal("changed", row.Name);
                Assert.Null(row.Text);
                context.Remove(row);
                Assert.Equal(2, await Save(context, async));
                Assert.Equal(new[] { 0, 0 }, await TableCounts(context, async, "A", "B"));

                stage("optional shared row absent and present");
                context.AddRange(new Owner { Name = "absent", Version = 1 },
                    new Owner { Name = "present", Version = 1, Detail = new Detail { Text = "detail", Version = 1 } });
                await Save(context, async);
                context.ChangeTracker.Clear();
                var owners = await Read(context.Set<Owner>().Include(owner => owner.Detail).OrderBy(owner => owner.Id), async);
                var absent = Assert.Single(owners, owner => owner.Name == "absent");
                Assert.Null(absent.Detail);
                var present = Assert.Single(owners, owner => owner.Name == "present");
                Assert.NotNull(present.Detail);
                Assert.Equal(present.Id, present.Detail.Id);
                Assert.Same(present, present.Detail.Owner);

                stage("shared row concurrency uses the original shared token");
                await using (var stale = new Context(context.Options, context.Table, Kind.Splitting))
                {
                    var staleOwner = Assert.Single(await Read(stale.Set<Owner>().Include(owner => owner.Detail)
                        .Where(owner => owner.Id == present.Id), async));
                    present.Name = "winner";
                    present.Detail.Text = "updated";
                    present.Version = present.Detail.Version = 2;
                    await Save(context, async);
                    staleOwner.Name = "loser";
                    staleOwner.Version = staleOwner.Detail!.Version = 3;
                    await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => Save(stale, async));
                }
                context.ChangeTracker.Clear();
                present = Assert.Single(await Read(context.Set<Owner>().Include(owner => owner.Detail)
                    .Where(owner => owner.Id == present.Id), async));
                Assert.Equal(("winner", 2, "updated"), (present.Name, present.Version, present.Detail!.Text));

                stage("optional shared dependent removal preserves its owner");
                // The application-managed shared token also changes when its dependent is removed.
                present.Version = present.Detail!.Version = 3;
                context.Remove(present.Detail!);
                await Save(context, async);
                context.ChangeTracker.Clear();
                owners = await Read(context.Set<Owner>().Include(owner => owner.Detail), async);
                Assert.Equal(2, owners.Count);
                Assert.All(owners, owner => Assert.Null(owner.Detail));
                present = Assert.Single(owners, owner => owner.Id == present.Id);
                Assert.Equal(("winner", 3), (present.Name, present.Version));
                Assert.Equal(new[] { 0, 0, 2 }, await TableCounts(context, async, "A", "B", "C"));
                context.RemoveRange(owners);
                await Save(context, async);
                Assert.Equal(new[] { 0, 0, 0 }, await TableCounts(context, async, "A", "B", "C"));
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public Task EnsureCreated_seeding_commits_and_rolls_back_inside_the_existing_namespace(OceanBaseCompatibilityMode mode, bool async)
        {
            var calls = 0;
            var abort = false;
            return LiveStorageTable.WithTable<Context>(mode, async, false, (options, table) =>
                new Context(new DbContextOptionsBuilder<Context>(options).ReplaceService<IModelCacheKeyFactory, CacheKey>()
                    .UseSeeding((db, performed) =>
                    {
                        Assert.False(async);
                        Assert.False(performed);
                        Assert.NotNull(db.Database.CurrentTransaction);
                        calls++;
                        db.Add(new SeedRow { Id = abort ? 2 : 1, Name = abort ? "rollback" : "committed" });
                        db.SaveChanges();
                        if (abort) throw new SeedAbort();
                    })
                    .UseAsyncSeeding(async (db, performed, token) =>
                    {
                        Assert.True(async);
                        Assert.False(performed);
                        Assert.NotNull(db.Database.CurrentTransaction);
                        calls++;
                        db.Add(new SeedRow { Id = abort ? 2 : 1, Name = abort ? "rollback" : "committed" });
                        await db.SaveChangesAsync(token);
                        if (abort) throw new SeedAbort();
                    }).Options, table, Kind.Seeding), async (context, _, stage) =>
                {
                    stage("existing namespace EnsureCreated invokes matching callback and commits");
                    Assert.False(async ? await context.Database.EnsureCreatedAsync() : context.Database.EnsureCreated());
                    Assert.Equal(1, calls);
                    Assert.Null(context.Database.CurrentTransaction);
                    context.ChangeTracker.Clear();
                    var committed = Assert.Single(await Read(context.Set<SeedRow>().AsNoTracking(), async));
                    Assert.Equal((1, "committed"), (committed.Id, committed.Name));

                    stage("throwing seeding callback rolls back its completed write");
                    abort = true;
                    await Assert.ThrowsAsync<SeedAbort>(async () =>
                    {
                        if (async) await context.Database.EnsureCreatedAsync();
                        else context.Database.EnsureCreated();
                    });
                    Assert.Equal(2, calls);
                    Assert.Null(context.Database.CurrentTransaction);
                    context.ChangeTracker.Clear();
                    committed = Assert.Single(await Read(context.Set<SeedRow>().AsNoTracking(), async));
                    Assert.Equal((1, "committed"), (committed.Id, committed.Name));
                });
        }

        private static Task WithTables(OceanBaseCompatibilityMode mode, bool async, Kind kind, string[] suffixes,
            Func<Context, Action<string>, Task> run)
            => LiveStorageTable.WithTable<Context>(mode, async, false, (options, table) =>
                new Context(new DbContextOptionsBuilder<Context>(options).ReplaceService<IModelCacheKeyFactory, CacheKey>().Options,
                    table, kind), (context, _, stage) => run(context, stage), suffixes);

        private static Task<int> Save(DbContext context, bool async)
            => async ? context.SaveChangesAsync() : Task.FromResult(context.SaveChanges());

        private static Task<List<T>> Read<T>(IQueryable<T> query, bool async)
            => async ? query.ToListAsync() : Task.FromResult(query.ToList());

        private static async Task<int[]> TableCounts(Context context, bool async, params string[] suffixes)
        {
            var helper = context.GetService<ISqlGenerationHelper>();
            var counts = new List<int>();
            foreach (var suffix in suffixes)
            {
                var sql = "SELECT COUNT(*) AS " + helper.DelimitIdentifier("Value") + " FROM "
                    + helper.DelimitIdentifier(context.Table + suffix);
                counts.Add(Assert.Single(await Read(context.Database.SqlQueryRaw<int>(sql), async)));
            }
            return counts.ToArray();
        }

        private enum Kind { Bulk, Hierarchy, Splitting, Seeding }

        private sealed class Context(DbContextOptions<Context> options, string table, Kind kind) : DbContext(options)
        {
            public DbContextOptions<Context> Options { get; } = options;
            public string Table { get; } = table;
            public Kind Kind { get; } = kind;

            protected override void OnModelCreating(ModelBuilder model)
            {
                switch (Kind)
                {
                    case Kind.Bulk:
                        model.Entity<BulkRow>().ToTable(Table);
                        model.Entity<BulkRow>().Property(row => row.Id).ValueGeneratedNever();
                        model.Entity<BulkRow>().Property(row => row.Name).HasMaxLength(40);
                        break;
                    case Kind.Hierarchy:
                        model.Entity<Asset>().UseTptMappingStrategy().ToTable(Table + "A");
                        model.Entity<Asset>().Property(row => row.Name).HasMaxLength(40);
                        model.Entity<Device>().ToTable(Table + "B");
                        model.Entity<Sensor>().ToTable(Table + "C");
                        break;
                    case Kind.Splitting:
                        model.Entity<SplitRow>().ToTable(Table + "A");
                        model.Entity<SplitRow>().Property(row => row.Name).HasMaxLength(40);
                        model.Entity<SplitRow>().Property(row => row.Text).HasMaxLength(40);
                        model.Entity<SplitRow>().SplitToTable(Table + "B", fragment =>
                        {
                            fragment.Property(row => row.Id).HasColumnName("DetailId");
                            fragment.Property(row => row.Text);
                        });
                        model.Entity<Owner>().ToTable(Table + "C").Property(row => row.Name).HasMaxLength(40);
                        model.Entity<Detail>().ToTable(Table + "C").Property(row => row.Text).HasMaxLength(40);
                        model.Entity<Owner>().Property(row => row.Version).IsConcurrencyToken();
                        model.Entity<Detail>().Property(row => row.Version).IsConcurrencyToken();
                        model.Entity<Owner>().HasOne(row => row.Detail).WithOne(row => row.Owner).HasForeignKey<Detail>(row => row.Id);
                        break;
                    case Kind.Seeding:
                        model.Entity<SeedRow>().ToTable(Table);
                        model.Entity<SeedRow>().Property(row => row.Id).ValueGeneratedNever();
                        model.Entity<SeedRow>().Property(row => row.Name).HasMaxLength(40);
                        break;
                }
            }
        }

        private sealed class CacheKey : IModelCacheKeyFactory
        {
            public object Create(DbContext context, bool designTime)
                => context is Context core ? (context.GetType(), core.Table, core.Kind, designTime) : (object)(context.GetType(), designTime);
        }

        private sealed class BulkRow
        {
            public int Id { get; set; }
            public int SourceId { get; set; }
            public int Count { get; set; }
            public int Previous { get; set; }
            public string Name { get; set; } = "";
        }
        private abstract class Asset { public int Id { get; set; } public string? Name { get; set; } }
        private class Device : Asset { public int Serial { get; set; } }
        private sealed class Sensor : Device { public int? Reading { get; set; } }
        private sealed class SplitRow { public int Id { get; set; } public string? Name { get; set; } public string? Text { get; set; } }
        private sealed class Owner
        {
            public int Id { get; set; }
            public string? Name { get; set; }
            public int Version { get; set; }
            public Detail? Detail { get; set; }
        }
        private sealed class Detail
        {
            public int Id { get; set; }
            public string? Text { get; set; }
            public int Version { get; set; }
            public Owner Owner { get; set; } = null!;
        }
        private sealed class SeedRow { public int Id { get; set; } public string Name { get; set; } = ""; }
        private sealed class SeedAbort : Exception;
    }
}
