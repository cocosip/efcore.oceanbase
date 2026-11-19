using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace OceanBase.EntityFrameworkCore.LiveTests;

public sealed class LiveSequenceKeyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Oracle_reads_shared_sequence_defaults_and_propagates_Tpc_keys(bool async)
        => LiveStorageTable.WithTable<StoreKeyContext>(OceanBaseCompatibilityMode.Oracle, async, false,
            (options, name) => new StoreKeyContext(new DbContextOptionsBuilder<StoreKeyContext>(options)
                .ReplaceService<IModelCacheKeyFactory, KeyModelCacheKey>().Options, name),
            async (context, _, stage) =>
            {
                stage("insert both TPC types and a dependent");
                var left = new LeftRow();
                var right = new RightRow();
                var link = new Link { Id = 1, Principal = left };
                context.AddRange(right, link);
                Assert.True(context.Entry(left).Property(row => row.Id).IsTemporary);
                Assert.True(context.Entry(right).Property(row => row.Id).IsTemporary);
                Assert.Equal(3, async ? await context.SaveChangesAsync() : context.SaveChanges());
                Assert.Equal(new long[] { 101, 105 }, new[] { left.Id, right.Id }.Order());
                Assert.Equal(left.Id, link.PrincipalId);

                stage("reload stored keys and relationship");
                context.ChangeTracker.Clear();
                var keys = context.Set<Row>().Select(row => row.Id).OrderBy(id => id);
                Assert.Equal(new long[] { 101, 105 }, async ? await keys.ToArrayAsync() : keys.ToArray());
                var links = context.Set<Link>().Include(item => item.Principal);
                Assert.Equal(left.Id, (async ? await links.SingleAsync() : links.Single()).Principal.Id);
            }, ["L", "R", "D"], "S", async ? ApplyModelMigration : null);

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public Task HiLo_allocates_disjoint_keys_for_a_Tpc_hierarchy(OceanBaseCompatibilityMode mode, bool async)
        => LiveStorageTable.WithTable<KeyContext>(mode, async, false,
            (options, name) => new KeyContext(new DbContextOptionsBuilder<KeyContext>(options)
                .ReplaceService<IModelCacheKeyFactory, KeyModelCacheKey>().Options, name),
            async (context, _, stage) =>
            {
                stage("allocate keys across contexts and TPC tables");
                await using var other = context.CreatePeer();
                if (async)
                {
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                        await other.AddAsync(new RightRow(), new CancellationToken(true)));
                }

                var first = new LeftRow();
                var second = new RightRow();
                await Add(context, first);
                await Add(context, second);
                Assert.Equal(101L, first.Id);
                Assert.Equal(102L, second.Id);

                await using (var transaction = async ? await other.Database.BeginTransactionAsync() : other.Database.BeginTransaction())
                {
                    var rolledBack = new RightRow();
                    await Add(other, rolledBack);
                    Assert.Equal(105L, rolledBack.Id);
                    Assert.Equal(1, await Save(other));
                    if (async) await transaction.RollbackAsync();
                    else transaction.Rollback();
                    other.ChangeTracker.Clear();
                }

                await Add(other, new LeftRow());
                Assert.Equal(1, await Save(other));
                for (var i = 0; i < 3; i++) await Add(context, new LeftRow());
                await Add(context, new RightRow { Id = 10000 });
                Assert.Equal(6, await Save(context));

                stage("reload and verify persisted keys and rollback");
                await using var read = context.CreatePeer();
                var keys = read.Set<Row>().Select(row => row.Id).OrderBy(id => id);
                Assert.Equal(new long[] { 101, 102, 103, 104, 106, 109, 10000 },
                    async ? await keys.ToArrayAsync() : keys.ToArray());

                async Task Add(KeyContext owner, Row row)
                {
                    if (async) await owner.AddAsync(row);
                    else owner.Add(row);
                }

                Task<int> Save(KeyContext owner) => async ? owner.SaveChangesAsync() : Task.FromResult(owner.SaveChanges());
            }, ["L", "R"], "S");

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql)]
    [InlineData(OceanBaseCompatibilityMode.Oracle)]
    public Task HiLo_bounds_Int32_keys(OceanBaseCompatibilityMode mode)
        => LiveStorageTable.WithTable<IntKeyContext>(mode, true, false,
            (options, name) => new IntKeyContext(new DbContextOptionsBuilder<IntKeyContext>(options)
                .ReplaceService<IModelCacheKeyFactory, KeyModelCacheKey>().Options, name),
            async (context, _, stage) =>
            {
                stage("persist final Int32 keys and reject out-of-range sequence values");
                await context.AddAsync(new IntRow());
                await context.AddAsync(new IntRow());
                await Assert.ThrowsAsync<OverflowException>(async () => await context.AddAsync(new IntRow()));
                Assert.Equal(2, await context.SaveChangesAsync());
                context.ChangeTracker.Clear();
                Assert.Equal(new[] { int.MaxValue - 1, int.MaxValue }, await context.Set<IntRow>()
                    .Select(row => row.Id).OrderBy(id => id).ToArrayAsync());

                stage("reject mismatched database sequence increment");
                var sql = context.GetService<Microsoft.EntityFrameworkCore.Storage.ISqlGenerationHelper>();
                var sequence = sql.DelimitIdentifier(context.Name + "S");
                // The fixture owns this sequence. A schema change must be observed on the next allocation.
                var alter = "ALTER SEQUENCE " + sequence + " INCREMENT BY 1";
                await context.Database.ExecuteSqlRawAsync(alter);
                await Assert.ThrowsAsync<InvalidOperationException>(async () => await context.AddAsync(new IntRow()));
            }, sequenceSuffix: "S", createObjects: ApplyModelMigration);

    private static async Task ApplyModelMigration(DbContext context)
    {
        var model = context.GetService<IDesignTimeModel>().Model;
        var operations = context.GetService<IMigrationsModelDiffer>().GetDifferences(null, model.GetRelationalModel());
        foreach (var command in context.GetService<IMigrationsSqlGenerator>().Generate(operations, model))
            await context.Database.ExecuteSqlRawAsync(command.CommandText);
    }

    private sealed class KeyContext(DbContextOptions<KeyContext> options, string name) : DbContext(options)
    {
        internal string Name => name;
        internal KeyContext CreatePeer() => new(options, name);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasSequence<long>(name + "S").StartsAt(101).IncrementsBy(4);
            var root = modelBuilder.Entity<Row>().UseTpcMappingStrategy();
            root.Property(row => row.Id).UseOceanBaseHiLo(name + "S");
            modelBuilder.Entity<LeftRow>().ToTable(name + "L");
            modelBuilder.Entity<RightRow>().ToTable(name + "R");
        }
    }

    private sealed class KeyModelCacheKey : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
            => (context.GetType(), context switch
            {
                KeyContext key => key.Name,
                StoreKeyContext key => key.Name,
                _ => ((IntKeyContext)context).Name
            }, designTime);
    }

    private sealed class StoreKeyContext(DbContextOptions<StoreKeyContext> options, string name) : DbContext(options)
    {
        internal string Name => name;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasSequence<long>(name + "S").StartsAt(101).IncrementsBy(4);
            modelBuilder.Entity<Row>().UseTpcMappingStrategy().Property(row => row.Id)
                .HasDefaultValueSql("\"" + name + "S\".NEXTVAL").ValueGeneratedOnAdd();
            modelBuilder.Entity<LeftRow>().ToTable(name + "L");
            modelBuilder.Entity<RightRow>().ToTable(name + "R");
            var link = modelBuilder.Entity<Link>().ToTable(name + "D");
            link.Property(item => item.Id).ValueGeneratedNever();
            link.HasOne(item => item.Principal).WithMany().HasForeignKey(item => item.PrincipalId);
        }
    }

    private sealed class IntKeyContext(DbContextOptions<IntKeyContext> options, string name) : DbContext(options)
    {
        internal string Name => name;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasSequence<long>(name + "S").StartsAt(int.MaxValue - 1).IncrementsBy(4);
            modelBuilder.Entity<IntRow>().ToTable(name).Property(row => row.Id).UseOceanBaseHiLo(name + "S");
        }
    }

    private abstract class Row { public long Id { get; set; } }
    private sealed class LeftRow : Row;
    private sealed class RightRow : Row;
    private sealed class IntRow { public int Id { get; set; } }
    private sealed class Link
    {
        public int Id { get; set; }
        public long PrincipalId { get; set; }
        public LeftRow Principal { get; set; } = null!;
    }
}
