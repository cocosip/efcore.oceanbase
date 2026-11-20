using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace OceanBase.EntityFrameworkCore.FunctionalTests
{
    public sealed class GroupingRuntimeTests
    {
        [Theory]
        [MemberData(nameof(TransactionTests.Modes), MemberType = typeof(TransactionTests))]
        public async Task Composite_nullable_converted_keys_keep_aggregate_and_element_projections_aligned(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new Commands { Results = Table([typeof(int), typeof(int), typeof(int), typeof(int), typeof(int)],
                [null, 107, 2, null, 1], [null, 107, 2, 12, 2], [1, 108, 1, 30, 3]) };
            await using var context = Create(mode, commands);
            var query = context.Set<Row>().GroupBy(r => new { r.Key, r.Tag }).OrderBy(g => g.Key.Key).ThenBy(g => g.Key.Tag)
                .Select(g => new { g.Key, Count = g.Count(), Values = g.OrderBy(r => r.Id).Select(r => r.Value).ToList() });
            var result = await Run(query, async);
            Assert.Equal(new int?[] { null, 1 }, result.Select(r => r.Key.Key));
            Assert.Equal(new[] { 7, 8 }, result.Select(r => r.Key.Tag));
            Assert.Equal(new[] { 2, 1 }, result.Select(r => r.Count));
            Assert.Equal(new int?[] { null, 12 }, result[0].Values);
            Assert.Equal(new int?[] { 30 }, result[1].Values);
            Assert.Matches(@"^SELECT (\w+)\.Key, \1\.Tag, \1\.c, (\w+)\.Value, \2\.Id FROM", commands.Items[0].Sql);
            Assert.Contains("GROUP BY", commands.Items[0].Sql);
            Assert.Contains("IS NULL AND", commands.Items[0].Sql);
            AssertClean(context, commands);
        }

        public static IEnumerable<object[]> FirstCases()
        {
            foreach (var mode in Enum.GetValues<OceanBaseCompatibilityMode>())
                foreach (var async in new[] { false, true })
                    foreach (var tracking in Enum.GetValues<QueryTrackingBehavior>())
                        foreach (var relationalNulls in new[] { false, true })
                            foreach (var resultSelector in new[] { false, true })
                                yield return [mode, async, tracking, relationalNulls, resultSelector];
        }

        [Theory]
        [MemberData(nameof(FirstCases))]
        public async Task Ordered_first_in_group_keeps_the_selected_entity_and_aggregate(OceanBaseCompatibilityMode mode, bool async,
            QueryTrackingBehavior tracking, bool relationalNulls, bool resultSelector)
        {
            var commands = new Commands { Results = Table([typeof(int), typeof(int), typeof(int), typeof(int), typeof(int), typeof(int)],
                [null, 2, 1, null, 107, 12], [1, 1, 3, 1, 108, null]) };
            await using var context = Create(mode, commands, relationalNulls);
            context.ChangeTracker.QueryTrackingBehavior = tracking;
            var rows = context.Set<Row>();
            var query = resultSelector
                ? rows.GroupBy(r => r.Key, (key, group) => new { Key = key, Count = group.Count(), First = group.OrderBy(r => r.Id).FirstOrDefault() }).OrderBy(g => g.Key)
                : rows.GroupBy(r => r.Key).Select(g => new { g.Key, Count = g.Count(), First = g.OrderBy(r => r.Id).FirstOrDefault() }).OrderBy(g => g.Key);
            var result = await Run(query, async);
            Assert.Equal(new int?[] { null, 1 }, result.Select(r => r.Key));
            Assert.Equal(new[] { 2, 1 }, result.Select(r => r.Count));
            Assert.Equal(new int?[] { 1, 3 }, result.Select(r => r.First?.Id));
            Assert.Equal(new int?[] { 7, 8 }, result.Select(r => r.First?.Tag));
            Assert.Equal(12, result[0].First!.Value);
            Assert.Null(result[1].First!.Value);
            Assert.Null(result[0].First!.Key);
            var again = await Run(query, async);
            Assert.Equal(tracking == QueryTrackingBehavior.TrackAll, ReferenceEquals(result[0].First, again[0].First));
            Assert.Equal(tracking == QueryTrackingBehavior.TrackAll ? 2 : 0, context.ChangeTracker.Entries().Count());
            var sql = commands.Items[0].Sql;
            Assert.Matches(@"^SELECT (\w+)\.Key, \1\.c, (\w+)\.Id, \2\.Key, \2\.Tag, \2\.Value FROM", sql);
            Assert.Contains("IS NULL AND", sql);
            Assert.Contains("COUNT(*)", sql);
            Assert.Contains("ORDER BY", sql);
            AssertClean(context, commands);
        }

        [Theory]
        [MemberData(nameof(TransactionTests.Modes), MemberType = typeof(TransactionTests))]
        public async Task Group_pages_use_current_converted_inputs_and_keep_ordered_elements(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new Commands { Results = Table([typeof(int), typeof(int), typeof(int), typeof(int), typeof(int)],
                [1, 107, 2, null, 3], [1, 107, 2, 12, 4]) };
            await using var context = Create(mode, commands);
            var tag = 7;
            var minimum = 1;
            var skip = 2;
            var take = 3;
            var query = () => context.Set<Row>().Where(r => r.Tag == tag).GroupBy(r => new { r.Key, r.Tag })
                .Where(g => g.Count() > minimum).OrderBy(g => g.Key.Key).ThenBy(g => g.Key.Tag).Skip(skip).Take(take)
                .Select(g => new { g.Key, Count = g.Count(), Values = g.OrderBy(r => r.Id).Select(r => r.Value).ToList() });
            foreach (var iteration in new[] { 0, 1 })
            {
                commands.Results = Table([typeof(int), typeof(int), typeof(int), typeof(int), typeof(int)],
                    [1, tag + 100, 2, null, 3], [1, tag + 100, 2, 12, 4]);
                var result = await Run(query(), async);
                Assert.Equal(new int?[] { null, 12 }, Assert.Single(result).Values);
                Assert.Equal(2, result[0].Count);
                Assert.Equal(tag, result[0].Key.Tag);
                var command = commands.Items[^1];
                Assert.Contains("HAVING COUNT(*) > ?", command.Sql);
                Assert.Contains("ORDER BY", command.Sql);
                Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "LIMIT ? OFFSET ?" : "OFFSET ? ROWS FETCH NEXT ? ROWS ONLY", command.Sql);
                // The filtered source is repeated for grouped keys and their elements.
                Assert.Equal(mode == OceanBaseCompatibilityMode.MySql
                    ? new object?[] { tag + 100, minimum, take, skip, tag + 100 }
                    : [tag + 100, minimum, skip, take, tag + 100], command.Inputs);
                tag = 8; minimum = 0; skip = 4; take = 5;
            }
            AssertClean(context, commands);
        }

        [Theory]
        [MemberData(nameof(TransactionTests.Modes), MemberType = typeof(TransactionTests))]
        public async Task Group_results_release_resources_after_empty_failure_cancellation_and_bad_values(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new Commands { Results = Table([typeof(int), typeof(int)]) };
            await using var context = Create(mode, commands);
            var query = context.Set<Row>().GroupBy(r => r.Key).Select(g => new { g.Key, Count = g.Count() });
            Assert.Empty(await Run(query, async));
            foreach (var cancel in new[] { false, true })
            {
                using var cancellation = new CancellationTokenSource();
                cancellation.Cancel();
                Exception failure = cancel ? new OperationCanceledException(cancellation.Token) : new InvalidOperationException("group command failure");
                commands.Failure = failure;
                Assert.Same(failure, await Assert.ThrowsAnyAsync<Exception>(() => Run(query, async)));
                AssertClean(context, commands);
                commands.Failure = null;
                commands.Results = Table([typeof(int), typeof(int)], [null, 2]);
                var result = Assert.Single(await Run(query, async));
                Assert.Null(result.Key);
                Assert.Equal(2, result.Count);
            }
            commands.Results = Table([typeof(int), typeof(int)], [1, null]);
            await Assert.ThrowsAnyAsync<Exception>(() => Run(query, async));
            AssertClean(context, commands);
            commands.Results = Table([typeof(int), typeof(int)], [1, 3]);
            Assert.Equal(3, Assert.Single(await Run(query, async)).Count);
            AssertClean(context, commands);
        }

        [Theory]
        [MemberData(nameof(TransactionTests.Modes), MemberType = typeof(TransactionTests))]
        public async Task Unsupported_group_predicate_rejects_before_open(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new Commands();
            var opens = new OpenCounter();
            await using var context = Create(mode, commands, openCounter: opens);
            var query = context.Set<Row>().GroupBy(r => r.Key).Where(g => Unsupported(g.Key)).Select(g => g.Count());
            await Assert.ThrowsAsync<InvalidOperationException>(() => Run(query, async));
            Assert.Empty(commands.Items);
            Assert.Equal(0, opens.Count);
        }

        private static bool Unsupported(int? value) => value == 1;

        private static void AssertClean(Context context, Commands commands)
        {
            Assert.All(commands.Readers, reader => Assert.True(reader.IsClosed));
            Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        }

        private static Task<List<T>> Run<T>(IQueryable<T> query, bool async)
            => async ? query.ToListAsync() : Task.FromResult(query.ToList());
        private static DataTable Table(Type[] types, params object?[][] rows)
        {
            var table = new DataTable();
            for (var i = 0; i < types.Length; i++) table.Columns.Add("C" + i, types[i]);
            foreach (var row in rows) table.Rows.Add(row.Select(v => v ?? DBNull.Value).ToArray());
            return table;
        }
        private static Context Create(OceanBaseCompatibilityMode mode, Commands commands, bool relationalNulls = false, OpenCounter? openCounter = null)
        {
            var builder = new DbContextOptionsBuilder<Context>().UseOceanBase("Server=offline;Database=app", mode)
                .ReplaceService<IRelationalConnection, TransactionTests.TestConnection>().AddInterceptors(commands);
            if (openCounter is not null) builder.AddInterceptors(openCounter);
            ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(
                builder.Options.Extensions.OfType<RelationalOptionsExtension>().Single().WithUseRelationalNulls(relationalNulls));
            return new Context(builder.Options);
        }
        private sealed class Context(DbContextOptions<Context> options) : DbContext(options)
        {
            protected override void OnModelCreating(ModelBuilder model)
            {
                model.Entity<Row>().ToTable("GroupingRows");
                model.Entity<Row>().Property(r => r.Tag).HasConversion(value => value + 100, value => value - 100);
            }
        }
        private sealed class Row
        {
            public int Id { get; set; }
            public int? Key { get; set; }
            public int Tag { get; set; }
            public int? Value { get; set; }
        }
        private sealed record Command(string Sql, object?[] Inputs);
        private sealed class OpenCounter : DbConnectionInterceptor
        {
            public int Count { get; private set; }
            public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
            { Count++; return result; }
            public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
                InterceptionResult result, CancellationToken cancellationToken = default)
            { Count++; return ValueTask.FromResult(result); }
        }
        private sealed class Commands : DbCommandInterceptor
        {
            public DataTable Results { get; set; } = new();
            public List<Command> Items { get; } = [];
            public List<DbDataReader> Readers { get; } = [];
            public Exception? Failure { get; set; }
            private InterceptionResult<DbDataReader> Read(DbCommand command)
            {
                var parameters = command.Parameters.Cast<DbParameter>().ToArray();
                var position = 0;
                var inputs = Regex.Matches(command.CommandText, @"[@:]\w+|\?").Select(match => match.Value == "?"
                    ? parameters[position++].Value
                    : parameters.Single(p => p.ParameterName.TrimStart('@', ':') == match.Value.TrimStart('@', ':')).Value).ToArray();
                var sql = Regex.Replace(Regex.Replace(command.CommandText.Replace("`", "", StringComparison.Ordinal)
                    .Replace("\"", "", StringComparison.Ordinal).Trim(), @"[@:]\w+", "?"), @"\s+", " ")
                    .Replace(" AS ", " ", StringComparison.Ordinal);
                Items.Add(new Command(sql, inputs));
                if (Failure is not null) throw Failure;
                var reader = Results.CreateDataReader();
                Readers.Add(reader);
                return InterceptionResult<DbDataReader>.SuppressWithResult(reader);
            }
            public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData e, InterceptionResult<DbDataReader> result)
                => Read(command);
            public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData e,
                InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(Read(command));
        }
    }
}
