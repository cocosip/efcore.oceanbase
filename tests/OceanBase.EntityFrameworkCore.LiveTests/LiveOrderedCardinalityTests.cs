using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace OceanBase.EntityFrameworkCore.LiveTests
{
    public sealed class LiveOrderedCardinalityTests
    {
        private static Row[] Seed() =>
        [
            new() { Id = 1, Bucket = 2, Value = 30, Label = "alpha", Price = new Amount(12.25m) },
            new() { Id = 2, Bucket = null, Value = 10, Label = null, Price = new Amount(0m) },
            new() { Id = 3, Bucket = 1, Value = 20, Label = "beta", Price = null },
            new() { Id = 4, Bucket = 2, Value = 30, Label = "alpha", Price = new Amount(-3.50m) },
            new() { Id = 5, Bucket = 1, Value = 40, Label = "gamma", Price = new Amount(99.99m) },
            new() { Id = 6, Bucket = null, Value = 10, Label = null, Price = new Amount(0m) }
        ];

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Ordered_paging_distinct_and_offset_only_refresh_bounds_and_keep_empty_pages(OceanBaseCompatibilityMode mode, bool async)
            => await WithTable(mode, async, async (context, stage) =>
            {
                stage("deterministic three-key paging with recomposed bounds");
                var ordered = context.Set<Row>().OrderBy(row => row.Bucket ?? -1).ThenByDescending(row => row.Value).ThenBy(row => row.Id);
                var expected = Seed().OrderBy(row => row.Bucket ?? -1).ThenByDescending(row => row.Value).ThenBy(row => row.Id).ToArray();
                var skip = 0;
                var take = 0;
                foreach (var page in new[] { (0, 0), (0, 1), (1, 2), (5, 2), (6, 2), (999, 3), (0, 6) })
                {
                    (skip, take) = page;
                    Assert.Equal(expected.Skip(skip).Take(take), await List(ordered.Skip(skip).Take(take), async));
                }
                foreach (var offset in new[] { 0, 1, 6, 999 })
                {
                    skip = offset;
                    Assert.Equal(expected.Skip(skip), await List(ordered.Skip(skip), async));
                }
                Assert.Equal(expected.Take(3).Select(row => row.Id), await List(ordered.Take(3).Select(row => row.Id), async));
                Assert.Equal(Enumerable.Reverse(expected).Select(row => row.Id), await List(ordered.Reverse().Select(row => row.Id), async));
                Assert.Empty(await List(context.Set<Row>().Where(row => row.Id < 0).OrderBy(row => row.Id).Skip(1).Take(2), async));
                var floor = 0;
                var filtered = context.Set<Row>().Where(row => row.Id > floor).OrderBy(row => row.Id).Select(row => row.Id);
                foreach (var value in new[] { 0, 2, 6, 1 })
                {
                    floor = value;
                    Assert.Equal(Seed().Where(row => row.Id > floor).Select(row => row.Id), await List(filtered, async));
                }

                stage("Distinct before/after paging and nullable projections");
                var distinct = context.Set<Row>().Select(row => row.Bucket).Distinct().OrderBy(value => value ?? -1);
                var expectedDistinct = Seed().Select(row => row.Bucket).Distinct().OrderBy(value => value ?? -1).ToArray();
                Assert.Equal(expectedDistinct, await List(distinct, async));
                Assert.Equal(expectedDistinct.Skip(1).Take(1), await List(distinct.Skip(1).Take(1), async));
                Assert.Empty(await List(distinct.Skip(3), async));
                var distinctValues = context.Set<Row>().Select(row => row.Value).Distinct().OrderBy(value => value);
                Assert.Equal(new[] { 10, 20, 30, 40 }, await List(distinctValues, async));
                var pagedDistinct = ordered.Skip(0).Take(2).Select(row => row.Value).Distinct();
                Assert.Equal(new[] { 10 }, await List(pagedDistinct, async));
                Assert.Equal(6, async ? await context.Set<Row>().CountAsync() : context.Set<Row>().Count());
                Assert.Empty(context.ChangeTracker.Entries());
            });

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Nullable_order_uses_dialect_defaults_and_explicit_null_rank_is_portable(OceanBaseCompatibilityMode mode, bool async)
            => await WithTable(mode, async, async (context, stage) =>
            {
                stage("native nullable ordering versus explicit null rank");
                var ascending = context.Set<Row>().OrderBy(row => row.Bucket).ThenBy(row => row.Id).Select(row => row.Id);
                Assert.Equal(mode == OceanBaseCompatibilityMode.MySql ? new[] { 2, 6, 3, 5, 1, 4 } : new[] { 3, 5, 1, 4, 2, 6 }, await List(ascending, async));
                var descending = context.Set<Row>().OrderByDescending(row => row.Bucket).ThenBy(row => row.Id).Select(row => row.Id);
                Assert.Equal(mode == OceanBaseCompatibilityMode.MySql ? new[] { 1, 4, 3, 5, 2, 6 } : new[] { 2, 6, 1, 4, 3, 5 }, await List(descending, async));
                var explicitRank = context.Set<Row>().OrderBy(row => row.Bucket == null ? 0 : 1).ThenBy(row => row.Bucket).ThenBy(row => row.Id);
                Assert.Equal(new[] { 2, 6, 3, 5, 1, 4 }, (await List(explicitRank, async)).Select(row => row.Id));
                stage("comparer overload rejects before server execution");
                Assert.Throws<InvalidOperationException>(() => context.Set<Row>().OrderBy(row => row.Label, StringComparer.Ordinal).ToQueryString());
                Assert.Equal(6, async ? await context.Set<Row>().CountAsync() : context.Set<Row>().Count());
            });

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task First_Single_and_defaults_enforce_native_cardinality_for_each_materialized_shape(OceanBaseCompatibilityMode mode, bool async)
            => await WithTable(mode, async, async (context, stage) =>
            {
                foreach (var (low, high) in new[] { (0, 0), (2, 2), (3, 3), (1, 2) })
                {
                    stage($"cardinality range={low}..{high}");
                    var rows = context.Set<Row>().Where(row => row.Id >= low && row.Id <= high).OrderBy(row => row.Id);
                    var expected = Seed().Where(row => row.Id >= low && row.Id <= high).OrderBy(row => row.Id).ToArray();
                    await VerifyCardinality(rows, expected, async);
                    await VerifyCardinality(rows.Select(row => row.Value), expected.Select(row => row.Value).ToArray(), async);
                    await VerifyCardinality(rows.Select(row => row.Bucket), expected.Select(row => row.Bucket).ToArray(), async);
                    await VerifyCardinality(rows.Select(row => row.Label), expected.Select(row => row.Label).ToArray(), async);
                    await VerifyCardinality(rows.Select(row => row.Price), expected.Select(row => row.Price).ToArray(), async);
                    await VerifyCardinality(rows.Select(row => new Result(row.Id, row.Bucket, row.Price)),
                        expected.Select(row => new Result(row.Id, row.Bucket, row.Price)).ToArray(), async);
                    Assert.Equal(6, async ? await context.Set<Row>().CountAsync() : context.Set<Row>().Count());
                }
                stage("predicate overloads and failure recovery");
                var all = context.Set<Row>().OrderBy(row => row.Id);
                Assert.Equal(Seed()[2], async ? await all.FirstAsync(row => row.Id == 3) : all.First(row => row.Id == 3));
                Assert.Equal(Seed()[2], async ? await all.SingleAsync(row => row.Id == 3) : all.Single(row => row.Id == 3));
                Assert.Equal(Seed()[2], async ? await all.SingleOrDefaultAsync(row => row.Id == 3) : all.SingleOrDefault(row => row.Id == 3));
                Assert.Equal(Seed()[0], async ? await all.FirstAsync(row => row.Id <= 2) : all.First(row => row.Id <= 2));
                Assert.Equal(Seed()[0], async ? await all.FirstOrDefaultAsync(row => row.Id <= 2) : all.FirstOrDefault(row => row.Id <= 2));
                Assert.Null(async ? await all.FirstOrDefaultAsync(row => row.Id == 99) : all.FirstOrDefault(row => row.Id == 99));
                Assert.Null(async ? await all.SingleOrDefaultAsync(row => row.Id == 99) : all.SingleOrDefault(row => row.Id == 99));
                if (async)
                {
                    await Assert.ThrowsAsync<InvalidOperationException>(() => all.FirstAsync(row => row.Id == 99));
                    await Assert.ThrowsAsync<InvalidOperationException>(() => all.SingleAsync(row => row.Id == 99));
                    await Assert.ThrowsAsync<InvalidOperationException>(() => all.SingleAsync(row => row.Id <= 2));
                    await Assert.ThrowsAsync<InvalidOperationException>(() => all.SingleOrDefaultAsync(row => row.Id <= 2));
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => all.First(row => row.Id == 99));
                    Assert.Throws<InvalidOperationException>(() => all.Single(row => row.Id == 99));
                    Assert.Throws<InvalidOperationException>(() => all.Single(row => row.Id <= 2));
                    Assert.Throws<InvalidOperationException>(() => all.SingleOrDefault(row => row.Id <= 2));
                }
                var emptyValues = all.Where(row => row.Id < 0).Select(row => row.Value);
                Assert.Equal(-99, emptyValues.FirstOrDefault(-99));
                Assert.Equal(-99, emptyValues.SingleOrDefault(-99));
                Assert.Equal(Seed(), await List(all, async));
                Assert.Empty(context.ChangeTracker.Entries());
            });

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Duplicate_entity_rows_obey_each_tracking_identity_mode(OceanBaseCompatibilityMode mode, bool async)
            => await WithTable(mode, async, async (context, stage) =>
            {
                foreach (var tracking in Enum.GetValues<QueryTrackingBehavior>())
                {
                    stage("identity mode=" + tracking);
                    context.ChangeTracker.Clear();
                    var source = context.Set<Row>().Where(row => row.Id <= 2);
                    var duplicates = source.Concat(source).OrderBy(row => row.Id);
                    var query = tracking switch { QueryTrackingBehavior.TrackAll => duplicates.AsTracking(),
                        QueryTrackingBehavior.NoTracking => duplicates.AsNoTracking(), _ => duplicates.AsNoTrackingWithIdentityResolution() };
                    var first = await List(query, async);
                    var second = await List(query, async);
                    Assert.Equal(new[] { 1, 1, 2, 2 }, first.Select(row => row.Id));
                    Assert.Equal(first, second);
                    foreach (var pair in first.GroupBy(row => row.Id))
                    {
                        var values = pair.ToArray();
                        if (tracking == QueryTrackingBehavior.NoTracking) Assert.NotSame(values[0], values[1]);
                        else Assert.Same(values[0], values[1]);
                    }
                    if (tracking == QueryTrackingBehavior.TrackAll)
                    {
                        Assert.Same(first[0], second[0]);
                        Assert.Equal(2, context.ChangeTracker.Entries().Count());
                    }
                    else
                    {
                        Assert.NotSame(first[0], second[0]);
                        Assert.Empty(context.ChangeTracker.Entries());
                    }
                }
            });

        private static async Task VerifyCardinality<T>(IQueryable<T> query, T[] expected, bool async)
        {
            foreach (var single in new[] { false, true })
            foreach (var orDefault in new[] { false, true })
            {
                async Task<T?> Read() => (async, single, orDefault) switch
                {
                    (true, true, true) => await query.SingleOrDefaultAsync(),
                    (true, true, false) => await query.SingleAsync(),
                    (true, false, true) => await query.FirstOrDefaultAsync(),
                    (true, false, false) => await query.FirstAsync(),
                    (false, true, true) => query.SingleOrDefault(),
                    (false, true, false) => query.Single(),
                    (false, false, true) => query.FirstOrDefault(),
                    _ => query.First()
                };
                if (expected.Length == 0 && !orDefault || expected.Length > 1 && single)
                {
                    var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Read());
                    Assert.Equal((expected.Length == 0 ? CoreStrings.SequenceContainsNoElements : CoreStrings.SequenceContainsMoreThanOneElement).TrimEnd('.'),
                        error.Message.TrimEnd('.'));
                }
                else Assert.Equal(expected.Length == 0 ? default : expected[0], await Read());
            }
        }

        private static Task<T[]> List<T>(IQueryable<T> query, bool async) => async ? query.ToArrayAsync() : Task.FromResult(query.ToArray());

        private static Task WithTable(OceanBaseCompatibilityMode mode, bool async, Func<QueryContext, Action<string>, Task> run)
            => LiveStorageTable.WithTable<QueryContext>(mode, async, false,
                (options, table) => new QueryContext(new DbContextOptionsBuilder<QueryContext>(options)
                    .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).ReplaceService<IModelCacheKeyFactory, CacheKey>().Options, mode, table),
                async (context, _, stage) =>
                {
                    stage("seed verification");
                    Assert.Equal(Seed(), await List(context.Set<Row>().OrderBy(row => row.Id), async));
                    await run(context, stage);
                });

        private sealed class QueryContext(DbContextOptions<QueryContext> options, OceanBaseCompatibilityMode mode, string table) : DbContext(options)
        {
            public OceanBaseCompatibilityMode Mode => mode;
            public string Table => table;
            protected override void OnModelCreating(ModelBuilder model)
            {
                var row = model.Entity<Row>();
                row.ToTable(table);
                row.Property(value => value.Id).ValueGeneratedNever();
                row.Property(value => value.Label).HasMaxLength(16).IsUnicode(false);
                row.Property(value => value.Price).HasConversion(value => value!.Value, value => new Amount(value)).HasPrecision(9, 2);
                row.HasData(Seed());
            }
        }

        private sealed class CacheKey : IModelCacheKeyFactory
        {
            public object Create(DbContext context, bool designTime)
                => (context.GetType(), ((QueryContext)context).Mode, ((QueryContext)context).Table, designTime);
        }

        private sealed record Result(int Id, int? Bucket, Amount? Price);
        private sealed record Amount(decimal Value);
        private sealed record Row
        {
            public int Id { get; set; }
            public int? Bucket { get; set; }
            public int Value { get; set; }
            public string? Label { get; set; }
            public Amount? Price { get; set; }
        }
    }
}
