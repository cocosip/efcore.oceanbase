using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
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
            await VerifyOffsetArithmetic(context, mode, async, token);
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

        private static async Task VerifyOffsetArithmetic(Context context, OceanBaseCompatibilityMode mode, bool async, CancellationToken token)
        {
            var oracle = mode == OceanBaseCompatibilityMode.Oracle;
            var anchor = new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.FromHours(6));
            var sameInstant = anchor.ToOffset(TimeSpan.FromMinutes(-30));
            var distant = new DateTimeOffset(1900, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var fractional = sameInstant.AddTicks(oracle ? -19 : -10);
            var maximum = oracle ? DateTimeOffset.MaxValue : DateTimeOffset.MaxValue.AddTicks(-9);
            DateTimeOffset?[] values = [sameInstant, distant, fractional, null, DateTimeOffset.MinValue,
                maximum.ToOffset(oracle ? TimeSpan.FromMinutes(-30) : TimeSpan.Zero),
                oracle ? new DateTimeOffset(DateTime.MaxValue, TimeSpan.FromHours(6)) : null,
                anchor, new DateTimeOffset(2024, 1, 31, 12, 0, 0, TimeSpan.FromMinutes(345)).AddTicks(oracle ? 1234567 : 1234560),
                oracle ? new DateTimeOffset(1, 2, 1, 0, 0, 0, TimeSpan.FromHours(6)) : null,
                oracle ? new DateTimeOffset(9998, 12, 31, 23, 45, 0, TimeSpan.FromMinutes(-30)) : null];
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(DateTimeOffset))!;
            var quote = oracle ? "\"" : "`";
            var tail = oracle ? " FROM DUAL" : "";
            var nullValue = oracle ? "CAST(NULL AS TIMESTAMP(7) WITH TIME ZONE)" : "CAST(NULL AS DATETIME(6))";
            var seed = string.Join(" UNION ALL ", values.Select((value, index) =>
                $"SELECT {index} AS {quote}Id{quote}, {(value.HasValue ? mapping.GenerateSqlLiteral(value.Value) : nullValue)} AS {quote}Zoned{quote}{tail}"));
            var rows = context.Set<ZonedRow>().FromSqlRaw(seed);
            var differences = rows.OrderBy(row => row.Id).Select(row => (TimeSpan?)row.Zoned!.Value.Subtract(anchor));
            Assert.Equal(values.Select(value => value?.Subtract(anchor)), await List(differences, async, token));
            Assert.Equal(values.Select(value => value.HasValue ? (TimeSpan?)anchor.Subtract(value.Value) : null),
                await List(rows.OrderBy(row => row.Id).Select(row => (TimeSpan?)anchor.Subtract(row.Zoned!.Value)), async, token));
            var fractionalTicks = oracle ? -19L : -10L;
            Assert.Equal(new[] { 2 }, await List(rows.Where(row => row.Zoned!.Value.Subtract(anchor).Ticks == fractionalTicks)
                .Select(row => row.Id), async, token));

            anchor = distant;
            Assert.Equal(values.Select(value => value?.Subtract(anchor)), await List(differences, async, token));
            Assert.Equal(values.Select((value, index) => (value, index)).Where(pair => pair.value > anchor).Select(pair => pair.index),
                await List(rows.Where(row => row.Zoned!.Value.Subtract(anchor).Ticks > 0)
                .OrderBy(row => row.Id).Select(row => row.Id), async, token));

            var adjustment = 0L;
            var adjusted = rows.OrderBy(row => row.Id).Select(row => (DateTimeOffset?)row.Zoned!.Value.AddTicks(adjustment));
            var duration = TimeSpan.Zero;
            var added = rows.OrderBy(row => row.Id).Select(row => (DateTimeOffset?)row.Zoned!.Value.Add(duration));
            var subtracted = rows.OrderBy(row => row.Id).Select(row => (DateTimeOffset?)row.Zoned!.Value.Subtract(duration));
            var amount = 0.0;
            var numeric = rows.Where(row => row.Id <= 3).OrderBy(row => row.Id).Select(row => new DateTimeOffset?[]
            {
                row.Zoned!.Value.AddDays(amount), row.Zoned.Value.AddHours(amount), row.Zoned.Value.AddMinutes(amount),
                row.Zoned.Value.AddSeconds(amount), row.Zoned.Value.AddMilliseconds(amount), row.Zoned.Value.AddMicroseconds(amount)
            });
            var step = oracle ? 1L : 10L;
            foreach (var ticks in new[] { -20L, -step, 0L, step, 20L, 1L, 9_007_199_254_740_990L, long.MinValue, long.MaxValue })
            {
                adjustment = ticks;
                duration = TimeSpan.FromTicks(ticks);
                await VerifyValues(adjusted, ticks);
                await VerifyValues(added, ticks);
                await VerifyValues(subtracted, -(decimal)ticks);
            }

            foreach (var increment in new[] { 1.125, -1.125, 1.0, -0.01, 0.0, 1_000_000.0000000001 })
            {
                amount = increment;
                var actual = await List(numeric, async, token);
                Assert.Equal(4, actual.Count);
                foreach (var (value, row) in values.Take(4).Zip(actual))
                {
                    Assert.Equal(6, row.Length);
                    if (!value.HasValue) { Assert.All(row, item => Assert.Null(item)); continue; }
                    var stamp = oracle ? value.Value : value.Value.ToUniversalTime();
                    var expected = new[] { stamp.AddDays(increment), stamp.AddHours(increment), stamp.AddMinutes(increment),
                        stamp.AddSeconds(increment), stamp.AddMilliseconds(increment), stamp.AddMicroseconds(increment) };
                    foreach (var (wanted, found) in expected.Zip(row))
                        if (!oracle && wanted.UtcTicks % 10 != 0) Assert.Null(found);
                        else Assert.True(found?.EqualsExact(wanted));
                }
            }

            var numericEndpoints = rows.OrderBy(row => row.Id).Select(row => (DateTimeOffset?)row.Zoned!.Value.AddMicroseconds(amount));
            foreach (var ticks in new[] { -step, 0L, step })
            {
                amount = ticks / 10.0;
                await VerifyValues(numericEndpoints, ticks);
            }

            amount = 4_000_000;
            Assert.All(await List(rows.Select(row => (DateTimeOffset?)row.Zoned!.Value.AddDays(amount)), async, token),
                item => Assert.Null(item));

            var units = 0;
            var calendar = rows.OrderBy(row => row.Id).Select(row => new
            {
                Months = (DateTimeOffset?)row.Zoned!.Value.AddMonths(units),
                Years = (DateTimeOffset?)row.Zoned.Value.AddYears(units)
            });
            foreach (var count in new[] { -1, 0, 1, 4, int.MinValue, int.MaxValue })
            {
                units = count;
                var actual = await List(calendar, async, token);
                Assert.Equal(values.Length, actual.Count);
                foreach (var (value, row) in values.Zip(actual))
                {
                    AssertZoned(CalendarExpected(value, count, years: false), row.Months);
                    AssertZoned(CalendarExpected(value, count, years: true), row.Years);
                }
            }

            async Task VerifyValues(IQueryable<DateTimeOffset?> query, decimal ticks)
            {
                var expected = values.Select(value => Expected(value, ticks));
                var actual = await List(query, async, token);
                Assert.Equal(values.Length, actual.Count);
                foreach (var (wanted, found) in expected.Zip(actual))
                    AssertZoned(wanted, found);
            }

            Assert.Null(Assert.Single(await List(rows.Where(row => row.Id == 0)
                .Select(row => (DateTimeOffset?)row.Zoned!.Value.AddTicks((row.Id == 0 ? (long?)null : row.Id)!.Value)), async, token)));

            DateTimeOffset? Expected(DateTimeOffset? value, decimal ticks)
            {
                if (!value.HasValue || ticks % step != 0) return null;
                var stamp = oracle ? value.Value : value.Value.ToUniversalTime();
                if ((decimal)stamp.UtcTicks + ticks < 0 || (decimal)stamp.UtcTicks + ticks > maximum.UtcTicks
                    || (decimal)stamp.Ticks + ticks < 0 || (decimal)stamp.Ticks + ticks > maximum.Ticks)
                    return null;
                return stamp.AddTicks((long)ticks);
            }

            DateTimeOffset? CalendarExpected(DateTimeOffset? value, int count, bool years)
            {
                if (!value.HasValue) return null;
                var stamp = oracle ? value.Value : value.Value.ToUniversalTime();
                try { return years ? stamp.AddYears(count) : stamp.AddMonths(count); }
                catch (ArgumentOutOfRangeException) { return null; }
            }

            static void AssertZoned(DateTimeOffset? expected, DateTimeOffset? actual)
            {
                if (expected.HasValue) Assert.True(actual?.EqualsExact(expected.Value));
                else Assert.Null(actual);
            }
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
                model.Entity<ZonedRow>().HasNoKey();
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

        private sealed class ZonedRow
        {
            public int Id { get; set; }
            public DateTimeOffset? Zoned { get; set; }
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
