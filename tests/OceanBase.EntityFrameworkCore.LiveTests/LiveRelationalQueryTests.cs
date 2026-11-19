using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Xunit;

namespace OceanBase.EntityFrameworkCore.LiveTests
{
    public sealed class LiveRelationalQueryTests
    {
        public static IEnumerable<object[]> Cases()
        {
            foreach (var mode in Enum.GetValues<OceanBaseCompatibilityMode>())
            foreach (var async in new[] { false, true })
            foreach (var relationalNulls in new[] { false, true })
                yield return [mode, async, relationalNulls];
        }

        public static IEnumerable<object[]> OracleCases() => Cases().Where(item => (OceanBaseCompatibilityMode)item[0] == OceanBaseCompatibilityMode.Oracle);

        private static Row[] Seed() =>
        [
            new() { Id = 1, ParentId = null, Bucket = null, Score = 10, Label = "one", Enabled = true, Rank = Rank.Low },
            new() { Id = 2, ParentId = 1, Bucket = 10, Score = 20, Label = "two", Enabled = false, Rank = Rank.High },
            new() { Id = 3, ParentId = 1, Bucket = 10, Score = null, Label = null, Enabled = true, Rank = Rank.Low },
            new() { Id = 4, ParentId = 2, Bucket = 20, Score = 40, Label = "four", Enabled = true, Rank = Rank.Mid },
            new() { Id = 5, ParentId = 99, Bucket = null, Score = 50, Label = "five", Enabled = false, Rank = Rank.High },
            new() { Id = 6, ParentId = null, Bucket = 20, Score = null, Label = null, Enabled = false, Rank = Rank.Mid }
        ];

        [Theory]
        [MemberData(nameof(Cases))]
        public Task Single_json_parameter_preserves_signed_integer_collection_values(
            OceanBaseCompatibilityMode mode, bool async, bool relationalNulls)
            => WithTable(mode, async, relationalNulls, async (context, stage) =>
            {
                var rows = context.Set<Row>();
                int?[] scores = [10, null, 40, 10];
                var matching = rows.OrderBy(row => row.Id).Where(row => scores.Contains(row.Score)).Select(row => row.Id);
                stage("bind one JSON parameter for nullable integer membership");
                Assert.Contains("JSON_TABLE(", matching.ToQueryString());
                Assert.Equal(relationalNulls ? new[] { 1, 4 } : [1, 3, 4, 6], await Read(matching, async));
                scores = [20];
                Assert.Equal(new[] { 2 }, await Read(matching, async));
                scores = [];
                Assert.Empty(await Read(matching, async));

                stage("read JSON ordinality and signed Int64 extrema");
                List<long?> values = [long.MinValue, null, long.MaxValue, long.MinValue];
                var singleRow = rows.Where(row => row.Id == 1);
                var projected = singleRow.Select(row => EF.Parameter(values).ElementAt(0));
                Assert.Equal(long.MinValue, Assert.Single(await Read(projected, async)));
                values[0] = long.MaxValue;
                Assert.Equal(long.MaxValue, Assert.Single(await Read(projected, async)));
                values[0] = null;
                Assert.Null(Assert.Single(await Read(projected, async)));
                values[3] = long.MaxValue;
                var duplicates = singleRow.Select(row => EF.Parameter(values).Count(value => value == long.MaxValue));
                Assert.Equal(2, Assert.Single(await Read(duplicates, async)));

                stage("join a JSON collection and execute an independent converted predicate");
                int[] keys = [4, 2, 4, 10];
                var joined = rows.Join(keys, row => row.Id, key => key, (row, key) => new { row.Rank, Key = key })
                    .Where(row => row.Rank == Rank.Mid).OrderBy(row => row.Key).Select(row => row.Key);
                Assert.Equal(new[] { 4, 4 }, await Read(joined, async));
            }, ParameterTranslationMode.Parameter);

        [Theory]
        [MemberData(nameof(Cases))]
        public Task Explicit_scalar_defaults_use_only_a_missing_row(OceanBaseCompatibilityMode mode, bool async, bool relationalNulls)
            => WithTable(mode, async, relationalNulls, async (context, stage) =>
            {
                var rows = context.Set<Row>();
                var empty = rows.Where(row => row.Id < 0);
                var presentNull = rows.Where(row => row.Id == 3);
                int? fallback = 42;
                stage("explicit DefaultIfEmpty with nullable, nonnullable and converted results");
                var padded = empty.Select(row => row.Score).DefaultIfEmpty(fallback).Where(value => value > 0);
                Assert.Equal(new int?[] { 42 }, await Read(padded, async));
                Assert.Null(Assert.Single(await Read(presentNull.Select(row => row.Score).DefaultIfEmpty(fallback), async)));
                Assert.Equal(new[] { 42 }, await Read(empty.Select(row => row.Id).DefaultIfEmpty(42), async));
                Assert.Equal(new[] { Rank.High }, await Read(empty.Select(row => row.Rank).DefaultIfEmpty(Rank.High), async));
                Assert.Equal(new[] { "none" }, await Read(empty.Select(row => row.Label).DefaultIfEmpty("none"), async));
                fallback = null;
                Assert.Null(Assert.Single(await Read(empty.Select(row => row.Score).DefaultIfEmpty(fallback), async)));

                stage("explicit FirstOrDefault in a correlated scalar projection");
                fallback = 42;
                var first = rows.OrderBy(row => row.Id).Select(parent => rows.OrderBy(child => child.Id)
                    .Select(child => child.Id == 3 ? child.Score : null)
                    .FirstOrDefault(value => parent.Id == 0, fallback));
                Assert.All(await Read(first, async), value => Assert.Equal(fallback, value));
                var correlated = rows.OrderBy(row => row.Id).Select(parent => rows.Where(child => child.Id == parent.Id + 1)
                    .Select(child => child.Score).FirstOrDefault(fallback));
                Assert.Equal(new int?[] { 20, null, 40, 50, null, 42 }, await Read(correlated, async));
                fallback = 43;
                Assert.Equal(new int?[] { 20, null, 40, 50, null, 43 }, await Read(correlated, async));

                if (async) return;
                stage("explicit terminal defaults and cardinality");
                var scores = rows.OrderBy(row => row.Id).Select(row => row.Score);
                Assert.Equal(10, scores.FirstOrDefault(fallback));
                Assert.Equal(fallback, empty.Select(row => row.Score).FirstOrDefault(fallback));
                Assert.Null(scores.FirstOrDefault(value => value == null, fallback));
                Assert.Equal(fallback, scores.FirstOrDefault(value => value > 100, fallback));
                Assert.Null(presentNull.Select(row => row.Score).SingleOrDefault(fallback));
                Assert.Equal(fallback, scores.SingleOrDefault(value => value > 100, fallback));
                Assert.Equal(20, scores.SingleOrDefault(value => value == 20, fallback));
                Assert.Throws<InvalidOperationException>(() => scores.SingleOrDefault(fallback));
                Assert.Throws<InvalidOperationException>(() => scores.SingleOrDefault(value => value > 15, fallback));
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public async Task Inner_left_cross_and_nullable_composite_joins_preserve_rows(OceanBaseCompatibilityMode mode, bool async, bool relationalNulls)
            => await WithTable(mode, async, relationalNulls, async (context, stage) =>
            {
                var rows = context.Set<Row>();
                var seed = Seed();
                stage("ordinary inner and left foreign-key joins");
                var inner = rows.Join(rows, parent => (int?)parent.Id, child => child.ParentId,
                    (parent, child) => new { Parent = parent.Id, Child = (int?)child.Id, child.Score });
                var expectedInner = seed.Join(seed, parent => (int?)parent.Id, child => child.ParentId,
                    (parent, child) => new Pair(parent.Id, child.Id, child.Score));
                Assert.Equal(Sort(expectedInner), await Read(inner.OrderBy(pair => pair.Parent).ThenBy(pair => pair.Child)
                    .Select(pair => new Pair(pair.Parent, pair.Child, pair.Score)), async));
                var left = from parent in rows
                           join child in rows on (int?)parent.Id equals child.ParentId into children
                           from child in children.DefaultIfEmpty()
                           orderby parent.Id, child.Id
                           select new Pair(parent.Id, (int?)child.Id, child.Score);
                var expectedLeft = seed.GroupJoin(seed, parent => (int?)parent.Id, child => child.ParentId,
                    (parent, children) => children.DefaultIfEmpty().Select(child => new Pair(parent.Id, child?.Id, child?.Score))).SelectMany(value => value);
                Assert.Equal(Sort(expectedLeft), await Read(left, async));
                var missing = from parent in rows
                              join child in rows.Where(child => child.Id < 0) on (int?)parent.Id equals child.ParentId into children
                              from child in children.DefaultIfEmpty()
                              orderby parent.Id
                              select new Pair(parent.Id, (int?)child.Id, child.Score);
                Assert.Equal(seed.Select(parent => new Pair(parent.Id, null, null)), await Read(missing, async));
                Assert.Empty(await Read(inner.Where(pair => pair.Parent < 0), async));

                stage("nullable scalar join versus compensated composite/predicate keys");
                var nullable = rows.Join(rows, parent => parent.Bucket, child => child.Bucket,
                    (parent, child) => new { Parent = parent.Id, Child = (int?)child.Id, child.Score });
                Assert.Equal(Sort(seed.Join(seed, parent => parent.Bucket, child => child.Bucket,
                    (parent, child) => new Pair(parent.Id, child.Id, child.Score))),
                    await Read(nullable.OrderBy(pair => pair.Parent).ThenBy(pair => pair.Child).Select(pair => new Pair(pair.Parent, pair.Child, pair.Score)), async));
                var composite = rows.Join(rows, parent => new { parent.Bucket, parent.Enabled }, child => new { child.Bucket, child.Enabled },
                    (parent, child) => new { Parent = parent.Id, Child = (int?)child.Id, child.Score });
                var expectedComposite = from parent in seed from child in seed
                                        where EqualKey(parent.Bucket, child.Bucket, relationalNulls) && parent.Enabled == child.Enabled
                                        select new Pair(parent.Id, child.Id, child.Score);
                Assert.Equal(Sort(expectedComposite), await Read(composite.OrderBy(pair => pair.Parent).ThenBy(pair => pair.Child).Select(pair => new Pair(pair.Parent, pair.Child, pair.Score)), async));
                var predicate = rows.SelectMany(parent => rows.Where(child => child.Bucket == parent.Bucket),
                    (parent, child) => new { Parent = parent.Id, Child = (int?)child.Id, child.Score });
                Assert.Equal(Sort(from parent in seed from child in seed where EqualKey(parent.Bucket, child.Bucket, relationalNulls: true)
                    select new Pair(parent.Id, child.Id, child.Score)), await Read(predicate.OrderBy(pair => pair.Parent).ThenBy(pair => pair.Child).Select(pair => new Pair(pair.Parent, pair.Child, pair.Score)), async));
                var cross = rows.Where(parent => parent.Id <= 2).SelectMany(parent => rows.Where(child => child.Id >= 5),
                    (parent, child) => new { Parent = parent.Id, Child = (int?)child.Id, child.Score });
                Assert.Equal(new[] { new Pair(1, 5, 50), new Pair(1, 6, null), new Pair(2, 5, 50), new Pair(2, 6, null) },
                    await Read(cross.OrderBy(pair => pair.Parent).ThenBy(pair => pair.Child).Select(pair => new Pair(pair.Parent, pair.Child, pair.Score)), async));
                Assert.Empty(context.ChangeTracker.Entries());
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public async Task Correlated_pages_scalar_collections_and_identity_refresh_each_parent_scope(OceanBaseCompatibilityMode mode, bool async, bool relationalNulls)
            => await WithTable(mode, async, relationalNulls, async (context, stage) =>
            {
                var rows = context.Set<Row>();
                var seed = Seed();
                var floor = 0;
                var skip = 0;
                var take = 2;
                var cross = rows.SelectMany(parent => rows.Where(child => child.Id > parent.Id + floor).OrderBy(child => child.Id).Skip(skip).Take(take),
                    (parent, child) => new { Parent = parent.Id, Child = (int?)child.Id, child.Score }).OrderBy(pair => pair.Parent).ThenBy(pair => pair.Child)
                    .Select(pair => new Pair(pair.Parent, pair.Child, pair.Score));
                var outer = rows.SelectMany(parent => rows.Where(child => child.Id > parent.Id + floor).OrderBy(child => child.Id).Skip(skip).Take(take).DefaultIfEmpty(),
                    (parent, child) => new { Parent = parent.Id, Child = (int?)child!.Id, child.Score }).OrderBy(pair => pair.Parent).ThenBy(pair => pair.Child)
                    .Select(pair => new Pair(pair.Parent, pair.Child, pair.Score));
                var collections = rows.OrderBy(parent => parent.Id).Select(parent => new
                {
                    parent.Id,
                    First = rows.Where(child => child.Id > parent.Id + floor).OrderBy(child => child.Id).Skip(skip).Select(child => (int?)child.Id).FirstOrDefault(),
                    Children = rows.Where(child => child.Id > parent.Id + floor).OrderBy(child => child.Id).Skip(skip).Take(take).Select(child => child.Id).ToList()
                });
                foreach (var bounds in new[] { (0, 0, 2), (1, 1, 1), (4, 0, 0), (99, 0, 2), (0, 0, 2) })
                {
                    (floor, skip, take) = bounds;
                    stage($"correlated captures floor={floor},skip={skip},take={take}");
                    Assert.Equal(seed.SelectMany(parent => Children(parent).Select(child => new Pair(parent.Id, child.Id, child.Score))), await Read(cross, async));
                    Assert.Equal(seed.SelectMany(parent => Children(parent).DefaultIfEmpty().Select(child => new Pair(parent.Id, child?.Id, child?.Score))), await Read(outer, async));
                    var actual = await Read(collections, async);
                    Assert.Equal(seed.Select(parent => parent.Id), actual.Select(parent => parent.Id));
                    foreach (var parent in actual)
                    {
                        var expected = Children(seed.Single(row => row.Id == parent.Id)).Select(child => child.Id).ToArray();
                        Assert.Equal(expected, parent.Children);
                        Assert.Equal(seed.Where(child => child.Id > parent.Id + floor).OrderBy(child => child.Id).Skip(skip).Select(child => (int?)child.Id).FirstOrDefault(), parent.First);
                    }
                }
                IEnumerable<Row> Children(Row parent) => seed.Where(child => child.Id > parent.Id + floor).OrderBy(child => child.Id).Skip(skip).Take(take);

                stage("nested parent and child correlations");
                var nested = rows.Where(parent => parent.Id <= 3).SelectMany(parent => rows.Where(child => child.Id > parent.Id).OrderBy(child => child.Id).Take(2)
                    .SelectMany(child => rows.Where(leaf => leaf.Id > child.Id && leaf.Enabled == parent.Enabled).OrderBy(leaf => leaf.Id).Take(1)
                        .Select(leaf => new { Parent = parent.Id, Child = child.Id, Leaf = leaf.Id })));
                var expectedNested = seed.Where(parent => parent.Id <= 3).SelectMany(parent => seed.Where(child => child.Id > parent.Id).OrderBy(child => child.Id).Take(2)
                    .SelectMany(child => seed.Where(leaf => leaf.Id > child.Id && leaf.Enabled == parent.Enabled).OrderBy(leaf => leaf.Id).Take(1)
                        .Select(leaf => new Triple(parent.Id, child.Id, leaf.Id))));
                Assert.Equal(expectedNested, await Read(nested.OrderBy(pair => pair.Parent).ThenBy(pair => pair.Child).ThenBy(pair => pair.Leaf)
                    .Select(pair => new Triple(pair.Parent, pair.Child, pair.Leaf)), async));

                foreach (var tracking in Enum.GetValues<QueryTrackingBehavior>())
                {
                    stage("lateral entity identity=" + tracking);
                    context.ChangeTracker.Clear();
                    context.ChangeTracker.QueryTrackingBehavior = tracking;
                    var pairs = rows.SelectMany(parent => rows.Where(child => child.Id > parent.Id).OrderBy(child => child.Id).Take(2).DefaultIfEmpty(),
                        (parent, child) => new { Parent = parent, Child = child }).OrderBy(pair => pair.Parent.Id).ThenBy(pair => pair.Child!.Id);
                    var actual = await Read(pairs, async);
                    Assert.Equal(seed.SelectMany(parent => seed.Where(child => child.Id > parent.Id).OrderBy(child => child.Id).Take(2).DefaultIfEmpty()
                        .Select(child => new Pair(parent.Id, child?.Id, child?.Score))), actual.Select(pair => new Pair(pair.Parent.Id, pair.Child?.Id, pair.Child?.Score)));
                    Assert.Null(actual[^1].Child);
                    if (tracking == QueryTrackingBehavior.NoTracking) Assert.NotSame(actual[0].Parent, actual[1].Parent);
                    else Assert.Same(actual[0].Parent, actual[1].Parent);
                    var again = await Read(pairs, async);
                    if (tracking == QueryTrackingBehavior.TrackAll) Assert.Same(actual[0].Parent, again[0].Parent);
                    else Assert.NotSame(actual[0].Parent, again[0].Parent);
                    Assert.Equal(tracking == QueryTrackingBehavior.TrackAll ? 6 : 0, context.ChangeTracker.Entries().Count());
                }
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public async Task Quantifiers_and_membership_obey_empty_and_configured_NULL_semantics(OceanBaseCompatibilityMode mode, bool async, bool relationalNulls)
            => await WithTable(mode, async, relationalNulls, async (context, stage) =>
            {
                var rows = context.Set<Row>();
                var seed = Seed();
                var empty = rows.Where(row => row.Id < 0);
                stage("root Any/All and empty-source vacuity");
                Assert.True(async ? await rows.AnyAsync() : rows.Any());
                Assert.False(async ? await empty.AnyAsync() : empty.Any());
                Assert.True(async ? await empty.AllAsync(row => row.Enabled) : empty.All(row => row.Enabled));
                Assert.False(async ? await rows.AllAsync(row => row.Enabled) : rows.All(row => row.Enabled));
                var threshold = 0;
                var quantified = rows.OrderBy(parent => parent.Id).Select(parent => new Booleans(parent.Id,
                    rows.Any(child => child.Id != parent.Id && child.Score == parent.Score),
                    rows.Where(child => child.ParentId == parent.Id).All(child => child.Score == null || child.Score > threshold),
                    (bool?)rows.Where(child => child.Id != parent.Id).Select(child => child.Score).Contains(parent.Score)));
                foreach (var bound in new[] { 0, 25, 100, 0 })
                {
                    threshold = bound;
                    stage("correlated quantifier threshold=" + bound);
                    var expected = seed.Select(parent => new Booleans(parent.Id,
                        seed.Any(child => child.Id != parent.Id && EqualKey(child.Score, parent.Score, relationalNulls)),
                        seed.Where(child => child.ParentId == parent.Id).All(child => child.Score == null || child.Score > threshold),
                        Membership(parent.Score, seed.Where(child => child.Id != parent.Id).Select(child => child.Score), relationalNulls)));
                    Assert.Equal(expected, await Read(quantified, async));
                }
                int? needle = null;
                foreach (var value in new int?[] { null, 20, 999, 10 })
                {
                    needle = value;
                    var expected = Membership(needle, seed.Select(row => row.Score), relationalNulls);
                    if (expected is null)
                    {
                        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                        {
                            if (async) await rows.Select(row => row.Score).ContainsAsync(needle);
                            else rows.Select(row => row.Score).Contains(needle);
                        });
                        Assert.Equal("Nullable object must have a value.", error.Message);
                    }
                    else Assert.Equal(expected.Value,
                        async ? await rows.Select(row => row.Score).ContainsAsync(needle) : rows.Select(row => row.Score).Contains(needle));
                }

                int?[] values = [];
                List<int?> list = [];
                var positive = rows.Where(row => values.Contains(row.Bucket)).OrderBy(row => row.Id).Select(row => row.Id);
                var negative = rows.Where(row => !values.Contains(row.Bucket)).OrderBy(row => row.Id).Select(row => row.Id);
                var listed = rows.Where(row => list.Contains(row.Bucket)).OrderBy(row => row.Id).Select(row => row.Id);
                var forced = rows.Where(row => EF.Parameter(values).Contains(row.Bucket)).OrderBy(row => row.Id).Select(row => row.Id);
                foreach (var input in new int?[][] { [], [10, null, 10], [20], [null, null], [], [10] })
                {
                    stage("captured nullable membership length=" + input.Length);
                    values = input;
                    list = input.ToList();
                    var expectedPositive = seed.Where(row => Membership(row.Bucket, input, relationalNulls) == true).Select(row => row.Id).ToArray();
                    Assert.Equal(expectedPositive, await Read(positive, async));
                    Assert.Equal(expectedPositive, await Read(listed, async));
                    Assert.Throws<InvalidOperationException>(() => forced.ToQueryString());
                    Assert.Equal(seed.Where(row => Membership(row.Bucket, input, relationalNulls) == false).Select(row => row.Id), await Read(negative, async));
                }
                stage("inline null membership and explicit comparer rejection");
                Assert.Equal(seed.Where(row => Membership(row.Bucket, [null, 10, 10], relationalNulls) == true).Select(row => row.Id),
                    await Read(rows.Where(row => new int?[] { null, 10, 10 }.Contains(row.Bucket)).OrderBy(row => row.Id).Select(row => row.Id), async));
                Assert.Throws<InvalidOperationException>(() => rows.Where(row => new[] { "one" }.Contains(row.Label, StringComparer.Ordinal)).ToQueryString());
                Assert.Equal(6, async ? await rows.CountAsync() : rows.Count());
                Assert.Empty(context.ChangeTracker.Entries());
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public async Task Explicit_parameter_membership_requires_and_honors_the_configured_collection_mode(OceanBaseCompatibilityMode mode, bool async, bool relationalNulls)
        {
            foreach (var translation in new[] { ParameterTranslationMode.Constant, ParameterTranslationMode.MultipleParameters })
                await WithTable(mode, async, relationalNulls, async (context, stage) =>
                {
                    int?[] values = [];
                    List<int?> list = [];
                    var rows = context.Set<Row>();
                    var forced = rows.Where(row => EF.Parameter(values).Contains(row.Bucket)).OrderBy(row => row.Id).Select(row => row.Id);
                    var listed = rows.Where(row => EF.Parameter(list).Contains(row.Bucket)).OrderBy(row => row.Id).Select(row => row.Id);
                    var constants = rows.Where(row => EF.Constant(values).Contains(row.Bucket)).OrderBy(row => row.Id).Select(row => row.Id);
                    foreach (var input in new int?[][] { [], [10, null, 10], [20], [null, null], [], [10] })
                    {
                        stage($"explicit membership mode={translation},length={input.Length}");
                        values = input;
                        list = input.ToList();
                        var expected = Seed().Where(row => Membership(row.Bucket, input, relationalNulls) == true).Select(row => row.Id);
                        Assert.Equal(expected, await Read(forced, async));
                        Assert.Equal(expected, await Read(listed, async));
                        Assert.Equal(expected, await Read(constants, async));
                    }
                    Assert.Equal(6, async ? await rows.CountAsync() : rows.Count());
                }, translation);
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public async Task Typed_collection_roots_keep_multiplicity_NULLs_and_current_membership(OceanBaseCompatibilityMode mode, bool async, bool relationalNulls)
            => await WithTable(mode, async, relationalNulls, async (context, stage) =>
            {
                await Check<sbyte?>([sbyte.MinValue, sbyte.MaxValue, null, sbyte.MinValue]);
                await Check<byte?>([0, byte.MaxValue, null, 0]);
                await Check<short?>([short.MinValue, short.MaxValue, null, short.MinValue]);
                await Check<ushort?>([0, ushort.MaxValue, null, 0]);
                await Check<int?>([int.MinValue, int.MaxValue, null, int.MinValue]);
                await Check<uint?>([0, uint.MaxValue, null, 0]);
                await Check<long?>([long.MinValue, long.MaxValue, null, long.MinValue]);
                await Check<ulong?>([0, ulong.MaxValue, null, 0]);
                await Check<decimal?>([-123.456789m, 123.456789m, null, -123.456789m]);
                await Check<float?>([-1.25f, 2.5f, null, -1.25f]);
                await Check<double?>([-1.25, 2.5, null, -1.25]);
                await Check<bool?>([false, true, null, false]);
                await Check<string?>(["alpha", "beta", null, "alpha"]);
                await Check<Guid?>([Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"), Guid.Empty, null, Guid.Empty]);
                await Check<DateTime?>([new(2026, 1, 2, 3, 4, 5), new(2030, 6, 7, 8, 9, 10), null]);
                await Check<DateTimeOffset?>([new(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(8)), new(2030, 6, 7, 8, 9, 10, TimeSpan.Zero), null]);
                await Check<DateOnly?>([new(2026, 1, 2), new(2030, 6, 7), null]);
                await Check<TimeOnly?>([new(1, 2, 3), new(23, 59, 59), null]);
                await Check<TimeSpan?>([TimeSpan.FromHours(-25), TimeSpan.FromHours(25), null]);
                await Check<byte[]?>([[0, 1, 255], [1, 0, 128], null, [0, 1, 255]]);

                async Task Check<T>(T[] sample)
                {
                    stage("typed root=" + typeof(T));
                    T[] values = [];
                    T[] wanted = [];
                    var root = context.Set<Row>().Where(row => row.Id == 1).SelectMany(row => values.Where(value => row.Id > 0));
                    var matched = context.Set<Row>().Where(row => row.Id == 1).SelectMany(row => values.Where(value => row.Id > 0 && wanted.Contains(value)));
                    foreach (var input in new[] { sample, Array.Empty<T>(), sample.Reverse().ToArray() })
                    {
                        values = input;
                        AssertMultiset(input, await Read(root, async));
                        if (mode == OceanBaseCompatibilityMode.Oracle && typeof(T) == typeof(byte[])) continue;
                        foreach (var candidates in new[] { sample.Take(1).ToArray(), Array.Empty<T>(), sample.Skip(1).ToArray() })
                        {
                            wanted = candidates;
                            var expected = input.Where(value => (!relationalNulls || value is not null)
                                && candidates.Any(candidate => SameValue(value, candidate)));
                            AssertMultiset(expected.ToArray(), await Read(matched, async));
                        }
                    }
                }
                Assert.Empty(context.ChangeTracker.Entries());
            });

        [Theory]
        [MemberData(nameof(OracleCases))]
        public async Task Text_roots_preserve_query_values_beyond_contextual_column_lengths(OceanBaseCompatibilityMode mode, bool async, bool relationalNulls)
            => await WithTable(mode, async, relationalNulls, async (context, stage) =>
            {
                string?[] sample = ["one", "alpha", new string('x', 17), null, new string('y', 2001)];
                foreach (var property in new[] { nameof(Row.Label), "NationalLabel", "FixedLabel", "NationalFixed" })
                {
                    string?[] values = sample;
                    var root = context.Set<Row>().Where(row => row.Id == 3).SelectMany(row => values.Where(value =>
                        value == EF.Property<string?>(row, property) || EF.Property<string?>(row, property) == null));
                    foreach (var input in new[] { sample, Array.Empty<string?>(), sample.Reverse().ToArray() })
                    {
                        values = input;
                        stage("text query carrier property=" + property + ",length=" + input.Length);
                        if (property is "FixedLabel" or "NationalFixed")
                        {
                            Assert.Contains("fixed-length text", Assert.Throws<NotSupportedException>(() => root.ToQueryString()).Message);
                            continue;
                        }
                        AssertMultiset(input, await Read(root, async));
                    }
                }
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public async Task Bounded_binary_roots_support_membership_while_Oracle_BLOB_comparison_rejects(OceanBaseCompatibilityMode mode, bool async, bool relationalNulls)
            => await WithTable(mode, async, relationalNulls, async (context, stage) =>
            {
                byte[]?[] sample = [[0, 1, 255], [1, 0, 128], null, [0, 1, 255]];
                byte[]?[] values = sample;
                var stored = await Read(context.Set<Row>().Where(row => row.Id <= 2).OrderBy(row => row.Id).AsTracking(), async);
                stored[0].Payload = sample[0];
                stored[1].Payload = sample[1];
                if (async) await context.SaveChangesAsync();
                else context.SaveChanges();
                context.ChangeTracker.Clear();
                var rows = context.Set<Row>().Where(row => row.Id == 3);
                // The property supplies the bounded RAW/VARBINARY mapping to the collection root.
                var root = rows.SelectMany(row => values.Where(value => value == row.Payload || row.Payload == null));
                var matched = context.Set<Row>().Where(row => values.Contains(row.Payload)).OrderBy(row => row.Id).Select(row => row.Id);
                byte[]?[] payloads = [sample[0], sample[1], null, null, null, null];
                foreach (var input in new[] { sample, Array.Empty<byte[]?>(), sample.Reverse().ToArray() })
                {
                    values = input;
                    stage("bounded binary root length=" + values.Length);
                    AssertMultiset(input, await Read(root, async));
                    Assert.Equal(Enumerable.Range(1, 6).Where(id => (!relationalNulls || payloads[id - 1] is not null)
                        && input.Any(candidate => SameValue(payloads[id - 1], candidate))), await Read(matched, async));
                }
                if (mode == OceanBaseCompatibilityMode.Oracle)
                {
                    values = sample;
                    byte[]?[] wanted = [sample[0], sample[1]];
                    stage("default BLOB comparison rejection before execution");
                    byte[] needle = [0, 1, 255];
                    var blobs = rows.SelectMany(row => values.Where(value => row.Id > 0));
                    foreach (var query in new[] { blobs.Where(value => value == needle), blobs.Where(value => value != needle),
                        blobs.Where(value => wanted.Contains(value)), blobs.Where(value => blobs.Contains(value)) })
                        Assert.Contains("BLOB", Assert.Throws<NotSupportedException>(() => query.ToQueryString()).Message);
                    Assert.Single(await Read(blobs.Where(value => value == null), async));
                    Assert.Equal(3, (await Read(blobs.Where(value => value != null), async)).Length);
                }
                Assert.Equal(6, async ? await context.Set<Row>().CountAsync() : context.Set<Row>().Count());
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public async Task Set_operations_preserve_NULL_equality_duplicates_converters_and_operand_grouping(OceanBaseCompatibilityMode mode, bool async, bool relationalNulls)
            => await WithTable(mode, async, relationalNulls, async (context, stage) =>
            {
                var rows = context.Set<Row>();
                var seed = Seed();
                var boundary = 3;
                var left = rows.Where(row => row.Id <= boundary);
                var right = rows.Where(row => row.Id >= boundary);
                foreach (var bound in new[] { 3, 5, 0, 99, 3 })
                {
                    boundary = bound;
                    foreach (var operation in new[] { "Union", "Concat", "Intersect", "Except" })
                    {
                        stage($"set={operation},boundary={bound}");
                        var expectedLeft = seed.Where(row => row.Id <= bound).ToArray();
                        var expectedRight = seed.Where(row => row.Id >= bound).ToArray();
                        var numbers = Set(left.Select(row => row.Score), right.Select(row => row.Score), operation).OrderBy(value => value ?? int.MinValue);
                        var expectedNumbers = Set(expectedLeft.Select(row => row.Score), expectedRight.Select(row => row.Score), operation).OrderBy(value => value ?? int.MinValue).ToArray();
                        Assert.Equal(expectedNumbers, await Read(numbers, async));
                        Assert.Equal(expectedNumbers.Where(value => value != null).Skip(1).Take(2), await Read(numbers.Where(value => value != null).Skip(1).Take(2), async));
                        AssertMultiset(Set(expectedLeft.Select(row => row.Label), expectedRight.Select(row => row.Label), operation).ToArray(),
                            await Read(Set(left.Select(row => row.Label), right.Select(row => row.Label), operation), async));
                        AssertMultiset(Set(expectedLeft.Select(row => row.Rank), expectedRight.Select(row => row.Rank), operation).ToArray(),
                            await Read(Set(left.Select(row => row.Rank), right.Select(row => row.Rank), operation), async));
                        Assert.Equal(Set(expectedLeft, expectedRight, operation).OrderBy(row => row.Id),
                            await Read(Set(left, right, operation).OrderBy(row => row.Id), async));
                    }
                }
                stage("nested grouping and paged operands");
                var a = rows.Where(row => row.Id <= 3).Select(row => row.Score);
                var b = rows.Where(row => row.Id >= 3).Select(row => row.Score);
                var c = rows.Where(row => row.Id == 6).Select(row => row.Score);
                var ea = seed.Where(row => row.Id <= 3).Select(row => row.Score);
                var eb = seed.Where(row => row.Id >= 3).Select(row => row.Score);
                var ec = seed.Where(row => row.Id == 6).Select(row => row.Score);
                var queries = new[] { a.Except(b.Except(c)), a.Except(b).Except(c), a.Union(b).Intersect(c), a.Concat(b.Union(c)) };
                var expectedQueries = new[] { ea.Except(eb.Except(ec)), ea.Except(eb).Except(ec), ea.Union(eb).Intersect(ec), ea.Concat(eb.Union(ec)) };
                for (var i = 0; i < queries.Length; i++)
                    AssertMultiset(expectedQueries[i].ToArray(), await Read(queries[i], async));
                var paged = rows.OrderBy(row => row.Id).Take(3).Select(row => row.Score)
                    .Except(rows.OrderBy(row => row.Id).Skip(2).Take(2).Select(row => row.Score)).OrderBy(value => value ?? int.MinValue);
                Assert.Equal(seed.Take(3).Select(row => row.Score).Except(seed.Skip(2).Take(2).Select(row => row.Score)).OrderBy(value => value ?? int.MinValue), await Read(paged, async));
                Assert.Throws<InvalidOperationException>(() => a.Where(value => value == -99).Union(b, EqualityComparer<int?>.Default).ToQueryString());
                Assert.Equal(6, async ? await rows.CountAsync() : rows.Count());
                Assert.Empty(context.ChangeTracker.Entries());
            });

        private static bool EqualKey(int? left, int? right, bool relationalNulls)
            => (!relationalNulls || left.HasValue && right.HasValue) && left == right;

        private static bool? Membership(int? value, IEnumerable<int?> source, bool relationalNulls)
        {
            var values = source.ToArray();
            if (values.Length == 0) return false;
            if (!relationalNulls) return values.Contains(value);
            if (value.HasValue && values.Contains(value)) return true;
            return !value.HasValue || values.Contains(null) ? null : false;
        }

        private static IEnumerable<Pair> Sort(IEnumerable<Pair> pairs) => pairs.OrderBy(pair => pair.Parent).ThenBy(pair => pair.Child);
        private static IQueryable<T> Set<T>(IQueryable<T> left, IQueryable<T> right, string operation) => operation switch
        { "Union" => left.Union(right), "Concat" => left.Concat(right), "Intersect" => left.Intersect(right), _ => left.Except(right) };
        private static IEnumerable<T> Set<T>(IEnumerable<T> left, IEnumerable<T> right, string operation) => operation switch
        { "Union" => left.Union(right), "Concat" => left.Concat(right), "Intersect" => left.Intersect(right), _ => left.Except(right) };
        private static Task<T[]> Read<T>(IQueryable<T> query, bool async) => async ? query.ToArrayAsync() : Task.FromResult(query.ToArray());

        private static bool SameValue<T>(T left, T right)
            => left is byte[] a && right is byte[] b ? a.SequenceEqual(b) : EqualityComparer<T>.Default.Equals(left, right);

        private static void AssertMultiset<T>(T[] expected, T[] actual)
        {
            Assert.Equal(expected.Length, actual.Length);
            var remaining = actual.ToList();
            foreach (var value in expected)
            {
                var index = remaining.FindIndex(candidate => SameValue(value, candidate));
                Assert.True(index >= 0, "A native result value or duplicate was missing.");
                remaining.RemoveAt(index);
            }
        }

        private static Task WithTable(OceanBaseCompatibilityMode mode, bool async, bool relationalNulls, Func<QueryContext, Action<string>, Task> run,
            ParameterTranslationMode? translation = null)
            => LiveStorageTable.WithTable<QueryContext>(mode, async, false,
                (options, table) =>
                {
                    var builder = new DbContextOptionsBuilder<QueryContext>(options).UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
                        .ReplaceService<IModelCacheKeyFactory, CacheKey>();
                    var extension = builder.Options.Extensions.OfType<RelationalOptionsExtension>().Single().WithUseRelationalNulls(relationalNulls);
                    if (translation.HasValue) extension = extension.WithUseParameterizedCollectionMode(translation.Value);
                    ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(extension);
                    return new QueryContext(builder.Options, mode, table);
                },
                async (context, _, stage) =>
                {
                    stage("seed verification");
                    Assert.Equal(Seed(), await Read(context.Set<Row>().OrderBy(row => row.Id), async));
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
                row.Property(value => value.Payload).HasMaxLength(32);
                row.Property<string>("NationalLabel").HasMaxLength(4);
                row.Property<string>("FixedLabel").HasMaxLength(4).IsUnicode(false).IsFixedLength();
                row.Property<string>("NationalFixed").HasMaxLength(4).IsFixedLength();
                row.Property(value => value.Rank).HasConversion<string>().HasMaxLength(8).IsUnicode(false);
                row.HasData(Seed());
            }
        }

        private sealed class CacheKey : IModelCacheKeyFactory
        {
            public object Create(DbContext context, bool designTime)
                => (context.GetType(), ((QueryContext)context).Mode, ((QueryContext)context).Table, designTime);
        }

        private sealed record Pair(int Parent, int? Child, int? Score);
        private sealed record Triple(int Parent, int Child, int Leaf);
        private sealed record Booleans(int Id, bool Any, bool All, bool? Contains);
        private enum Rank { Low, Mid, High }
        private sealed record Row
        {
            public int Id { get; set; }
            public int? ParentId { get; set; }
            public int? Bucket { get; set; }
            public int? Score { get; set; }
            public string? Label { get; set; }
            public byte[]? Payload { get; set; }
            public bool Enabled { get; set; }
            public Rank Rank { get; set; }
        }
    }
}
