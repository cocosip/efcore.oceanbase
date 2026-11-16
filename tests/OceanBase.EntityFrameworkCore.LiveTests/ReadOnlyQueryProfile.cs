using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OceanBase.EntityFrameworkCore;
using Xunit;

namespace OceanBase.EntityFrameworkCore.LiveTests
{
    internal static class ReadOnlyQueryProfile
    {
        internal static string Seed(OceanBaseCompatibilityMode mode)
        {
            var quote = mode == OceanBaseCompatibilityMode.MySql ? "`" : "\"";
            var tail = mode == OceanBaseCompatibilityMode.MySql ? "" : " FROM DUAL";
            var integer = mode == OceanBaseCompatibilityMode.MySql ? "SIGNED" : "NUMBER(10)";
            var text = mode == OceanBaseCompatibilityMode.MySql ? "CHAR(16)" : "VARCHAR2(16)";
            var values = new[] { (1, "10", "'one'", 1, "1.25"), (2, "20", "'two'", 0, "2.50"),
                (3, $"CAST(NULL AS {integer})", $"CAST(NULL AS {text})", 1, "3.75"), (4, "10", "'four'", 0, "5.00") };
            return string.Join(" UNION ALL ", values.Select(row =>
                $"SELECT {row.Item1} AS {quote}Id{quote}, {row.Item2} AS {quote}Value{quote}, {row.Item3} AS {quote}Name{quote}, "
                + $"{row.Item4} AS {quote}Active{quote}, {row.Item5} AS {quote}Amount{quote}{tail}"));
        }

        internal static IQueryable<Row> Rows(Context context, OceanBaseCompatibilityMode mode)
            => context.Set<Row>().FromSqlRaw(Seed(mode)).AsNoTracking();

        internal static IQueryable<Pair> LateralPairs(IQueryable<Row> rows, bool outer)
            => rows.SelectMany(parent => outer
                ? rows.Where(child => child.Id > parent.Id).OrderBy(child => child.Id).Take(1).DefaultIfEmpty()
                : rows.Where(child => child.Id > parent.Id).OrderBy(child => child.Id).Take(1),
                (parent, child) => new { Parent = parent.Id, Child = (int?)child!.Id })
                .OrderBy(pair => pair.Parent).Select(pair => new Pair(pair.Parent, pair.Child));

        internal sealed record Pair(int Parent, int? Child);

        internal sealed class DefaultReference
        {
            public int Id { get; set; }
            public int? Value { get; set; }
        }

        internal static IQueryable<DefaultReference?> ReferenceDefaults(IQueryable<Row> rows, bool empty)
            => (empty ? rows.Where(r => r.Id < 0) : rows.Where(r => r.Id == 3))
                .Select(r => new DefaultReference { Id = r.Id, Value = r.Value }).DefaultIfEmpty();

        internal static IQueryable<Pair> DefaultPairs(IQueryable<Row> rows, bool nullable)
            => nullable
                ? rows.SelectMany(parent => rows.Where(child => child.Id > parent.Id).OrderBy(child => child.Id).Take(1)
                    .Select(child => child.Value).DefaultIfEmpty(), (parent, child) => new { Parent = parent.Id, Child = child })
                    .OrderBy(pair => pair.Parent).Select(pair => new Pair(pair.Parent, pair.Child))
                : rows.SelectMany(parent => rows.Where(child => child.Id > parent.Id).OrderBy(child => child.Id).Take(1)
                    .Select(child => child.Id).DefaultIfEmpty(), (parent, child) => new { Parent = parent.Id, Child = (int?)child })
                    .OrderBy(pair => pair.Parent).Select(pair => new Pair(pair.Parent, pair.Child));

        internal static IQueryable<int?> Set(IQueryable<int?> left, IQueryable<int?> right, string operation)
            => operation switch { "Union" => left.Union(right), "Concat" => left.Concat(right),
                "Intersect" => left.Intersect(right), _ => left.Except(right) };

        internal static async Task Verify(Context context, OceanBaseCompatibilityMode mode, bool async, CancellationToken token)
        {
            var rows = Rows(context, mode);
            var materialized = await List(rows.OrderBy(r => r.Id), async, token);
            Assert.Equal(new[] { 1, 2, 3, 4 }, materialized.Select(r => r.Id));
            Assert.Equal(new int?[] { 10, 20, null, 10 }, materialized.Select(r => r.Value));
            Assert.Equal(new string?[] { "one", "two", null, "four" }, materialized.Select(r => r.Name));
            Assert.Equal(new[] { true, false, true, false }, materialized.Select(r => r.Active));
            Assert.Equal(new[] { 1.25m, 2.50m, 3.75m, 5.00m }, materialized.Select(r => r.Amount));
            Assert.Empty(context.ChangeTracker.Entries());

            var floor = 1;
            var filtered = rows.Where(r => r.Id > floor).OrderBy(r => r.Id).Select(r => r.Id);
            foreach (var value in new[] { 1, 2, 0 })
            {
                floor = value;
                Assert.Equal(Enumerable.Range(value + 1, 4 - value), await List(filtered, async, token));
            }
            string? name = "one";
            var named = rows.Where(r => r.Name == name).Select(r => r.Id);
            foreach (var value in new string?[] { "one'; -- @p0", "two", null, "one" })
            {
                name = value;
                Assert.Equal(value switch { "two" => new[] { 2 }, null => [3], "one" => [1], _ => [] }, await List(named, async, token));
            }

            var booleans = await List(rows.OrderBy(r => r.Id).Select(r => new { Not = !r.Active, HasValue = r.Value.HasValue }), async, token);
            Assert.Equal(new[] { false, true, false, true }, booleans.Select(r => r.Not));
            Assert.Equal(new[] { true, true, false, true }, booleans.Select(r => r.HasValue));
            Assert.Equal(new[] { 1, 3 }, await List(rows.Where(r => r.Active).OrderBy(r => r.Id).Select(r => r.Id), async, token));
            Assert.Equal(new[] { 4, 2 }, await List(rows.OrderBy(r => r.Value ?? 99).ThenBy(r => r.Id).Skip(1).Take(2).Select(r => r.Id), async, token));

            Assert.Equal(4, async ? await rows.CountAsync(token) : rows.Count());
            Assert.Equal(40, async ? await rows.SumAsync(r => r.Value, token) : rows.Sum(r => r.Value));
            Assert.Equal(3.125m, async ? await rows.AverageAsync(r => r.Amount, token) : rows.Average(r => r.Amount));
            Assert.True(async ? await rows.AnyAsync(r => r.Value == 20, token) : rows.Any(r => r.Value == 20));
            Assert.True(async ? await rows.AllAsync(r => r.Id > 0, token) : rows.All(r => r.Id > 0));
            Assert.False(async ? await rows.AllAsync(r => r.Active, token) : rows.All(r => r.Active));
            var groups = await List(rows.GroupBy(r => r.Value).Select(g => new { g.Key, Count = g.Count(), Sum = g.Sum(r => r.Amount) }), async, token);
            Assert.Equal(3, groups.Count);
            Assert.Equal(2, Assert.Single(groups, g => g.Key == 10).Count);
            Assert.Equal(6.25m, Assert.Single(groups, g => g.Key == 10).Sum);
            Assert.Equal(2.50m, Assert.Single(groups, g => g.Key == 20).Sum);
            Assert.Equal(3.75m, Assert.Single(groups, g => g.Key is null).Sum);
            Assert.Equal(new int?[] { 10 }, await List(rows.GroupBy(r => r.Value).Where(g => g.Count() > 1).Select(g => g.Key), async, token));

            var left = rows.Where(r => r.Id <= 3).Select(r => r.Value);
            var right = rows.Where(r => r.Id >= 3).Select(r => r.Value);
            foreach (var operation in new[] { "Union", "Concat", "Intersect", "Except" })
            {
                int?[] expected = operation switch { "Union" => [null, 10, 20], "Concat" => [null, null, 10, 10, 20],
                    "Intersect" => [null, 10], _ => [20] };
                Assert.Equal(expected, await List(Set(left, right, operation).OrderBy(v => v ?? -1), async, token));
            }
            var nested = left.Except(right.Except(rows.Where(r => r.Id == 4).Select(r => r.Value))).OrderBy(v => v ?? -1);
            Assert.Equal(new int?[] { 10, 20 }, await List(nested, async, token));

            Assert.Equal(new[] { new Pair(1, 2), new Pair(2, 3), new Pair(3, 4) },
                await List(LateralPairs(rows, false), async, token));
            Assert.Equal(new[] { new Pair(1, 2), new Pair(2, 3), new Pair(3, 4), new Pair(4, null) },
                await List(LateralPairs(rows, true), async, token));
            Assert.Equal(new[] { new Pair(1, 2), new Pair(2, 3), new Pair(3, 4), new Pair(4, 0) },
                await List(DefaultPairs(rows, false), async, token));
            Assert.Equal(new[] { new Pair(1, 20), new Pair(2, null), new Pair(3, 10), new Pair(4, null) },
                await List(DefaultPairs(rows, true), async, token));
            Assert.Equal(new[] { 0 }, await List(rows.Where(r => r.Id < 0).Select(r => r.Id).DefaultIfEmpty(), async, token));
            Assert.Equal(new int?[] { null }, await List(rows.Where(r => r.Id < 0).Select(r => r.Value).DefaultIfEmpty(), async, token));
            Assert.Equal(new int?[] { null }, await List(rows.Where(r => r.Id == 3).Select(r => r.Value).DefaultIfEmpty(), async, token));
            Assert.Null(Assert.Single(await List(ReferenceDefaults(rows, true), async, token)));
            var present = Assert.Single(await List(ReferenceDefaults(rows, false), async, token));
            Assert.NotNull(present);
            Assert.Equal(3, present.Id);
            Assert.Null(present.Value);
            Assert.Null(Assert.Single(await List(rows.Where(r => r.Id < 0).Select(r => (object)r.Id).DefaultIfEmpty(), async, token)));
            Assert.Equal(new[] { 0 }, await List(rows.Where(r => r.Id < 0).Select(r => r.Value!.Value).DefaultIfEmpty(), async, token));
            await Assert.ThrowsAsync<InvalidOperationException>(() => List(rows.Where(r => r.Id == 3).Select(r => r.Value!.Value).DefaultIfEmpty(), async, token));
        }

        private static Task<List<T>> List<T>(IQueryable<T> query, bool async, CancellationToken token)
            => async ? query.ToListAsync(token) : Task.FromResult(query.ToList());

        internal sealed class Context(DbContextOptions<Context> options) : DbContext(options)
        {
            protected override void OnModelCreating(ModelBuilder model)
            {
                var row = model.Entity<Row>().HasNoKey();
                row.Property(r => r.Name).HasMaxLength(16).IsUnicode(false);
                row.Property(r => r.Amount).HasPrecision(10, 2);
            }
        }
        internal sealed class Row
        {
            public int Id { get; set; }
            public int? Value { get; set; }
            public string? Name { get; set; }
            public bool Active { get; set; }
            public decimal Amount { get; set; }
        }

        internal sealed class Commands : DbCommandInterceptor
        {
            public int Readers { get; private set; }
            public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData e, InterceptionResult<DbDataReader> result)
            {
                if (!command.CommandText.TrimStart().StartsWith("SELECT ", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The read-only fixture only admits its generated SELECT reader commands.");
                Readers++;
                return result;
            }
            public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData e,
                InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(ReaderExecuting(command, e, result));
            public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData e, InterceptionResult<int> result)
                => throw new InvalidOperationException("The read-only fixture cannot execute nonquery commands.");
            public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData e,
                InterceptionResult<int> result, CancellationToken cancellationToken = default)
                => throw new InvalidOperationException("The read-only fixture cannot execute nonquery commands.");
        }
    }
}
