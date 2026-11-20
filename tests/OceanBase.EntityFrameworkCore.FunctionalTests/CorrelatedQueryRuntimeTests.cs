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
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace OceanBase.EntityFrameworkCore.FunctionalTests
{
    public sealed class CorrelatedQueryRuntimeTests
    {
        public static IEnumerable<object[]> Shapes()
        {
            foreach (var mode in Enum.GetValues<OceanBaseCompatibilityMode>())
                foreach (var async in new[] { false, true })
                    foreach (var outer in new[] { false, true })
                        yield return [mode, async, outer];
        }

        [Theory]
        [MemberData(nameof(Shapes))]
        public async Task Correlated_pages_preserve_projection_NULL_extension_and_current_bindings(OceanBaseCompatibilityMode mode, bool async, bool outer)
        {
            var commands = new Commands { Results = Table([typeof(int), typeof(int), typeof(int)], [1, 2, 30]) };
            if (outer) commands.Results.Rows.Add(4, DBNull.Value, DBNull.Value);
            await using var context = Create(mode, commands);
            var rows = context.Set<Row>();
            var floor = 1;
            var skip = 0;
            var take = 2;
            var query = rows.SelectMany(parent => outer
                ? rows.Where(child => child.Id > parent.Id + floor).OrderBy(child => child.Id).Skip(skip).Take(take).DefaultIfEmpty()
                : rows.Where(child => child.Id > parent.Id + floor).OrderBy(child => child.Id).Skip(skip).Take(take),
                (parent, child) => new { Parent = parent.Id, Child = (int?)child!.Id, Sum = parent.Value + child.Value })
                .OrderBy(pair => pair.Parent).ThenBy(pair => pair.Child);
            foreach (var value in new[] { 1, 3 })
            {
                floor = value;
                skip = value == 1 ? 0 : 1;
                take = value == 1 ? 2 : 3;
                var result = await Run(query, async);
                Assert.Equal(outer ? new[] { 1, 4 } : [1], result.Select(r => r.Parent));
                Assert.Equal(outer ? new int?[] { 2, null } : [2], result.Select(r => r.Child));
                Assert.Equal(outer ? new int?[] { 30, null } : [30], result.Select(r => r.Sum));
                var command = commands.Items[^1];
                AssertLateral(command.Sql, outer);
                Assert.Matches(@"^SELECT c\.RowKey Parent, (\w+)\.RowKey Child, c\.Value \+ \1\.Value Sum FROM CorrelationRows c", command.Sql);
                Assert.Contains("c0.RowKey > (c.RowKey + ?)", command.Sql);
                Assert.Contains("ORDER BY c0.RowKey", command.Sql);
                Assert.Contains("ORDER BY c.RowKey,", command.Sql);
                Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "LIMIT ? OFFSET ?" : "OFFSET ? ROWS FETCH NEXT ? ROWS ONLY", command.Sql);
                Assert.Equal(mode == OceanBaseCompatibilityMode.MySql ? new object?[] { floor, take, skip } : [floor, skip, take], command.Inputs);
                AssertClean(context, commands);
            }
            commands.Results.Rows.Clear();
            Assert.Empty(await Run(query, async));
            AssertClean(context, commands);
        }

        [Theory]
        [MemberData(nameof(TransactionTests.Modes), MemberType = typeof(TransactionTests))]
        public async Task Correlated_collection_projection_groups_children_and_preserves_empty_parent(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new Commands { Results = Table([typeof(int), typeof(int)], [1, 2], [1, 3], [4, null]) };
            await using var context = Create(mode, commands);
            var rows = context.Set<Row>();
            var query = rows.OrderBy(parent => parent.Id).Select(parent => new
            {
                parent.Id,
                Children = rows.Where(child => child.Id > parent.Id).OrderBy(child => child.Id).Take(2).Select(child => child.Id).ToList()
            });
            var result = await Run(query, async);
            Assert.Equal(new[] { 1, 4 }, result.Select(r => r.Id));
            Assert.Equal(new[] { 2, 3 }, result[0].Children);
            Assert.Empty(result[1].Children);
            var sql = commands.Items[^1].Sql;
            AssertLateral(sql, true);
            Assert.Matches(@"^SELECT c\.RowKey, (\w+)\.RowKey FROM CorrelationRows c", sql);
            Assert.Contains("c0.RowKey > c.RowKey", sql);
            Assert.Contains("ORDER BY c.RowKey,", sql);
            AssertClean(context, commands);
        }

        [Theory]
        [MemberData(nameof(ModelMaterializationTests.TrackingCases), MemberType = typeof(ModelMaterializationTests))]
        public async Task Lateral_entity_pairs_use_the_requested_identity_scope(OceanBaseCompatibilityMode mode, bool async, QueryTrackingBehavior tracking)
        {
            var commands = new Commands { Results = Table([typeof(int), typeof(string), typeof(int), typeof(int), typeof(string), typeof(int)],
                [1, "one", 10, 2, "two", 20], [1, "one", 10, 3, "three", null], [4, "four", null, null, null, null]) };
            await using var context = Create(mode, commands);
            context.ChangeTracker.QueryTrackingBehavior = tracking;
            var rows = context.Set<Row>();
            var query = rows.SelectMany(parent => rows.Where(child => child.Id > parent.Id).OrderBy(child => child.Id).Take(2).DefaultIfEmpty(),
                (parent, child) => new { Parent = parent, Child = child }).OrderBy(pair => pair.Parent.Id).ThenBy(pair => pair.Child!.Id);
            var result = await Run(query, async);
            Assert.Equal(new[] { 1, 1, 4 }, result.Select(r => r.Parent.Id));
            Assert.Equal(new int?[] { 2, 3, null }, result.Select(r => r.Child?.Id));
            Assert.Equal("two", result[0].Child!.Name);
            Assert.Null(result[1].Child!.Value);
            Assert.Equal(tracking != QueryTrackingBehavior.NoTracking, ReferenceEquals(result[0].Parent, result[1].Parent));
            var again = await Run(query, async);
            Assert.Equal(tracking == QueryTrackingBehavior.TrackAll, ReferenceEquals(result[0].Parent, again[0].Parent));
            Assert.Equal(tracking == QueryTrackingBehavior.TrackAll ? 4 : 0, context.ChangeTracker.Entries().Count());
            var sql = commands.Items[0].Sql;
            AssertLateral(sql, true);
            Assert.Matches(@"^SELECT c\.RowKey, c\.Name, c\.Value, (\w+)\.RowKey, \1\.Name, \1\.Value FROM CorrelationRows c", sql);
            Assert.Contains("c0.RowKey > c.RowKey", sql);
            AssertClean(context, commands);
        }

        [Theory]
        [MemberData(nameof(TransactionTests.Modes), MemberType = typeof(TransactionTests))]
        public async Task Lifted_scalar_projection_over_a_table_keeps_ordinary_join_syntax(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new Commands { Results = Table([typeof(int)], [30], [null]) };
            await using var context = Create(mode, commands);
            var rows = context.Set<Row>();
            var query = rows.SelectMany(parent => rows.Select(child => parent.Value + child.Value));
            Assert.Equal(new int?[] { 30, null }, await Run(query, async));
            Assert.Equal("SELECT c.Value + c0.Value FROM CorrelationRows c CROSS JOIN CorrelationRows c0", commands.Items[^1].Sql);
            AssertClean(context, commands);
        }

        [Theory]
        [MemberData(nameof(TransactionTests.Modes), MemberType = typeof(TransactionTests))]
        public async Task Lateral_command_failure_and_cancellation_release_the_lease_and_allow_recovery(OceanBaseCompatibilityMode mode, bool async)
        {
            foreach (var cancel in new[] { false, true })
            {
                using var source = new CancellationTokenSource();
                Exception failure = cancel ? new OperationCanceledException(source.Token) : new InvalidOperationException("query failed");
                var commands = new Commands { Results = Table([typeof(int)], [2]), Failure = failure,
                    BeforeFailure = cancel ? source.Cancel : null };
                await using var context = Create(mode, commands);
                var rows = context.Set<Row>();
                var query = rows.SelectMany(parent => rows.Where(child => child.Id > parent.Id).Take(2), (parent, child) => child.Id);
                var error = await Record.ExceptionAsync(() => Run(query, async, source.Token));
                Assert.Same(failure, error);
                Assert.Equal(cancel, source.IsCancellationRequested);
                if (async) Assert.Equal(source.Token, Assert.Single(commands.Items).Token);
                AssertClean(context, commands);
                commands.Failure = null;
                Assert.Equal(new[] { 2 }, await Run(query, async));
                AssertLateral(commands.Items[^1].Sql, false);
                AssertClean(context, commands);
            }
        }

        [Theory]
        [MemberData(nameof(TransactionTests.Modes), MemberType = typeof(TransactionTests))]
        public async Task Unsupported_correlated_predicates_reject_before_connection_opening(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new Commands();
            await using var context = Create(mode, commands);
            var rows = context.Set<Row>();
            var query = rows.SelectMany(parent => rows.Where(child => Local(child.Value, parent.Value)).Take(2), (parent, child) => child.Id);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Run(query, async));
            Assert.Equal(0, commands.Openings.Count);
            Assert.Empty(commands.Items);
            AssertClean(context, commands);
        }

        private static bool Local(int? left, int? right) => left == right;
        private static Task<List<T>> Run<T>(IQueryable<T> query, bool async, CancellationToken token = default)
            => async ? query.ToListAsync(token) : Task.FromResult(query.ToList());
        private static void AssertLateral(string sql, bool outer)
        {
            Assert.Contains(outer ? "LEFT JOIN LATERAL (" : "JOIN LATERAL (", sql);
            Assert.Contains("ON 1 = 1", sql);
            Assert.DoesNotContain("APPLY", sql);
        }
        private static void AssertClean(DbContext context, Commands commands)
        {
            Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
            Assert.Null(context.Database.CurrentTransaction);
            Assert.All(commands.Readers, reader => Assert.True(reader.IsClosed));
        }
        private static DataTable Table(Type[] types, params object?[][] rows)
        {
            var table = new DataTable();
            for (var i = 0; i < types.Length; i++) table.Columns.Add("C" + i, types[i]);
            foreach (var row in rows) table.Rows.Add(row.Select(v => v ?? DBNull.Value).ToArray());
            return table;
        }
        private static Context Create(OceanBaseCompatibilityMode mode, Commands commands) => new(new DbContextOptionsBuilder<Context>()
            .UseOceanBase("Server=offline;Database=app", mode).ReplaceService<IRelationalConnection, TransactionTests.TestConnection>()
            .AddInterceptors(commands, commands.Openings).Options);
        private sealed class Context(DbContextOptions<Context> options) : DbContext(options)
        {
            protected override void OnModelCreating(ModelBuilder model)
            {
                model.Entity<Row>().ToTable("CorrelationRows");
                model.Entity<Row>().Property(r => r.Id).HasColumnName("RowKey");
                model.Entity<Row>().Property(r => r.Name).HasMaxLength(40);
            }
        }
        private sealed class Row { public int Id { get; set; } public string? Name { get; set; } public int? Value { get; set; } }
        private sealed record Command(string Sql, object?[] Inputs, CancellationToken Token);
        private sealed class Openings : DbConnectionInterceptor
        {
            public int Count { get; private set; }
            public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
            { Count++; return result; }
            public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
                InterceptionResult result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(ConnectionOpening(connection, eventData, result));
        }
        private sealed class Commands : DbCommandInterceptor
        {
            public Openings Openings { get; } = new();
            public DataTable Results { get; init; } = new();
            public Exception? Failure { get; set; }
            public Action? BeforeFailure { get; init; }
            public List<Command> Items { get; } = [];
            public List<DbDataReader> Readers { get; } = [];
            private InterceptionResult<DbDataReader> Read(DbCommand command, CancellationToken token)
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
                    .Replace("\"", "", StringComparison.Ordinal).Trim(), @"[@:]\w+", "?"), @"\s+", " ")
                    .Replace(" AS ", " ", StringComparison.Ordinal);
                Items.Add(new Command(sql, inputs, token));
                if (Failure is not null) { BeforeFailure?.Invoke(); throw Failure; }
                var reader = Results.CreateDataReader();
                Readers.Add(reader);
                return InterceptionResult<DbDataReader>.SuppressWithResult(reader);
            }
            public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
                => Read(command, default);
            public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
                InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(Read(command, cancellationToken));
        }
    }
}
