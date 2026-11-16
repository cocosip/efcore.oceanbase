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
    public sealed class SetOperationRuntimeTests
    {
        public static IEnumerable<object[]> Operators()
        {
            foreach (var pair in TransactionTests.Modes())
                foreach (var operation in new[] { "Union", "Concat", "Intersect", "Except" })
                    yield return [pair[0], pair[1], operation];
        }

        [Theory]
        [MemberData(nameof(Operators))]
        public async Task Nullable_scalar_sets_bind_each_operand_and_refresh_cached_paging(OceanBaseCompatibilityMode mode, bool async, string operation)
        {
            var commands = new Commands { Results = Table([typeof(int)], [null], [2]) };
            await using var context = Create(mode, commands);
            var boundary = 10;
            var skip = 1;
            var take = 4;
            var rows = context.Set<Row>();
            var ordered = Set(rows.Where(r => r.Id < boundary).Select(r => r.Value),
                rows.Where(r => r.Id > boundary).Select(r => r.Value), operation).OrderBy(v => v);
            foreach (var value in new[] { 10, 20 })
            {
                boundary = value;
                skip = value == 10 ? 1 : 2;
                take = value == 10 ? 4 : 5;
                var query = ordered.Skip(skip).Take(take);
                Assert.Equal(new int?[] { null, 2 }, await Run(query, async));
                var command = commands.Items[^1];
                AssertOperator(command.Sql, mode, operation);
                Assert.Contains("s.RowKey < ?", command.Sql);
                Assert.Contains("s0.RowKey > ?", command.Sql);
                Assert.Contains("ORDER BY", command.Sql);
                Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "LIMIT ? OFFSET ?" : "OFFSET ? ROWS FETCH NEXT ? ROWS ONLY", command.Sql);
                Assert.Equal(mode == OceanBaseCompatibilityMode.MySql ? new object?[] { value, value, take, skip }
                    : [value, value, skip, take], command.Inputs);
                AssertClean(context, commands);
            }
            commands.Results = Table([typeof(int)]);
            Assert.Empty(await Run(ordered.Take(3), async));
            AssertClean(context, commands);
        }

        [Theory]
        [MemberData(nameof(Operators))]
        public async Task Bounded_text_and_converted_enum_set_projections_materialize_nullable_values(OceanBaseCompatibilityMode mode, bool async, string operation)
        {
            var commands = new Commands { Results = Table([typeof(string)], [null], ["海🌊"]) };
            await using var context = Create(mode, commands);
            var rows = context.Set<Row>();
            var left = rows.Where(r => r.Id < 10);
            var right = rows.Where(r => r.Id > 3);
            var names = Set(left.Select(r => r.Name), right.Select(r => r.Name), operation);
            Assert.Equal(new string?[] { null, "海🌊" }, await Run(names, async));
            AssertOperator(commands.Items[^1].Sql, mode, operation);
            Assert.Contains("s.Name", commands.Items[^1].Sql);
            Assert.Contains("s0.Name", commands.Items[^1].Sql);
            commands.Results = Table([typeof(string)], ["Active"], [null], ["Archived"]);
            var kinds = Set(left.Select(r => r.Kind), right.Select(r => r.Kind), operation);
            Assert.Equal(new Kind?[] { Kind.Active, null, Kind.Archived }, await Run(kinds, async));
            AssertOperator(commands.Items[^1].Sql, mode, operation);
            Assert.Contains("s.Kind", commands.Items[^1].Sql);
            Assert.Contains("s0.Kind", commands.Items[^1].Sql);
            Assert.Empty(context.ChangeTracker.Entries());
            AssertClean(context, commands);
        }

        [Theory]
        [MemberData(nameof(Operators))]
        public async Task Entity_sets_keep_tracking_identity_and_concat_reader_duplicates(OceanBaseCompatibilityMode mode, bool async, string operation)
        {
            foreach (var tracking in Enum.GetValues<QueryTrackingBehavior>())
            {
                var data = Table([typeof(int), typeof(string), typeof(string), typeof(int)],
                    [7, "Active", "seven", 2], [8, null, null, null]);
                if (operation == "Concat") data.Rows.InsertAt(data.NewRow(), 1);
                if (operation == "Concat") data.Rows[1].ItemArray = [7, "Active", "seven", 2];
                var commands = new Commands { Results = data };
                await using var context = Create(mode, commands);
                context.ChangeTracker.QueryTrackingBehavior = tracking;
                var rows = context.Set<Row>();
                var floor = operation == "Except" ? 20 : 3;
                var query = Set(rows.Where(r => r.Id < 10), rows.Where(r => r.Id > floor), operation).OrderBy(r => r.Id);
                var result = await Run(query, async);
                Assert.Equal(operation == "Concat" ? new[] { 7, 7, 8 } : [7, 8], result.Select(r => r.Id));
                Assert.Equal(Kind.Active, result[0].Kind);
                Assert.Equal("seven", result[0].Name);
                Assert.Null(result[^1].Name);
                Assert.Null(result[^1].Value);
                if (operation == "Concat") Assert.Equal(tracking != QueryTrackingBehavior.NoTracking, ReferenceEquals(result[0], result[1]));
                var again = await Run(query, async);
                Assert.Equal(tracking == QueryTrackingBehavior.TrackAll, ReferenceEquals(result[0], again[0]));
                Assert.Equal(tracking == QueryTrackingBehavior.TrackAll ? 2 : 0, context.ChangeTracker.Entries().Count());
                AssertOperator(commands.Items[0].Sql, mode, operation);
                Assert.Contains("s.RowKey < 10", commands.Items[0].Sql);
                Assert.Contains("s0.RowKey > ?", commands.Items[0].Sql);
                Assert.Equal(new object?[] { floor }, commands.Items[0].Inputs);
                AssertClean(context, commands);
            }
        }

        [Theory]
        [MemberData(nameof(TransactionTests.Modes), MemberType = typeof(TransactionTests))]
        public async Task Count_over_each_set_and_nested_difference_keeps_the_server_query(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new Commands { Results = Table([typeof(int)], [3]) };
            await using var context = Create(mode, commands);
            var rows = context.Set<Row>();
            foreach (var operation in new[] { "Union", "Concat", "Intersect", "Except" })
            {
                var query = Set(rows.Where(r => r.Id < 10).Select(r => r.Value), rows.Where(r => r.Id > 3).Select(r => r.Value), operation);
                Assert.Equal(3, async ? await query.CountAsync() : query.Count());
                AssertOperator(commands.Items[^1].Sql, mode, operation);
                Assert.StartsWith("SELECT COUNT(*) FROM (", commands.Items[^1].Sql);
                commands.Results = Table([typeof(int)], [0]);
                Assert.Equal(0, async ? await query.CountAsync() : query.Count());
                commands.Results = Table([typeof(int)], [3]);
            }
            var a = rows.Where(r => r.Id == 1).Select(r => r.Value);
            var b = rows.Where(r => r.Id == 2).Select(r => r.Value);
            var c = rows.Where(r => r.Id == 3).Select(r => r.Value);
            commands.Results = Table([typeof(int)], [2]);
            Assert.Equal(new int?[] { 2 }, await Run(a.Except(b.Except(c)), async));
            var difference = mode == OceanBaseCompatibilityMode.Oracle ? "MINUS" : "EXCEPT";
            Assert.Contains(difference + " ( SELECT", commands.Items[^1].Sql);
            Assert.Equal(2, Regex.Matches(commands.Items[^1].Sql, @"\b" + difference + @"\b").Count);
            AssertClean(context, commands);
        }

        [Theory]
        [MemberData(nameof(TransactionTests.Modes), MemberType = typeof(TransactionTests))]
        public async Task Set_query_failure_and_cancellation_release_readers_and_allow_a_later_query(OceanBaseCompatibilityMode mode, bool async)
        {
            foreach (var cancel in new[] { false, true })
            {
                using var source = new CancellationTokenSource();
                Exception error = cancel ? new OperationCanceledException(source.Token) : new InvalidOperationException("synthetic set query failure");
                var commands = new Commands { Results = Table([typeof(int)], [2]), Failure = error, BeforeFailure = cancel ? source.Cancel : null };
                await using var context = Create(mode, commands);
                var rows = context.Set<Row>();
                var query = rows.Where(r => r.Id < 10).Select(r => r.Value).Except(rows.Where(r => r.Id > 3).Select(r => r.Value));
                var failure = await Record.ExceptionAsync(() => Run(query, async, source.Token));
                Assert.Same(error, failure);
                Assert.Equal(cancel, source.IsCancellationRequested);
                if (async) Assert.Equal(source.Token, Assert.Single(commands.Items).Token);
                AssertClean(context, commands);
                commands.Failure = null;
                Assert.Equal(new int?[] { 2 }, await Run(query, async));
                AssertOperator(commands.Items[^1].Sql, mode, "Except");
                AssertClean(context, commands);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Oracle_LOB_sets_reject_before_opening_or_executing(bool async)
        {
            var commands = new Commands();
            await using var context = Create(OceanBaseCompatibilityMode.Oracle, commands);
            var rows = context.Set<LobRow>().Select(r => r.Text);
            foreach (var operation in new[] { "Union", "Concat", "Intersect", "Except" })
            {
                var error = await Assert.ThrowsAsync<NotSupportedException>(() => Run(Set(rows, rows, operation), async));
                Assert.Contains("LOB", error.Message);
                Assert.Empty(commands.Items);
                Assert.Equal(0, Native(context).Closes);
                AssertClean(context, commands);
            }
        }

        [Theory]
        [MemberData(nameof(TransactionTests.Modes), MemberType = typeof(TransactionTests))]
        public async Task Custom_set_comparers_reject_without_client_side_predicate_fallback(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new Commands();
            await using var context = Create(mode, commands);
            var rows = context.Set<Row>().Select(r => r.Name);
            foreach (var query in new[] { rows.Union(rows, StringComparer.OrdinalIgnoreCase), rows.Intersect(rows, StringComparer.OrdinalIgnoreCase), rows.Except(rows, StringComparer.OrdinalIgnoreCase) })
                await Assert.ThrowsAsync<InvalidOperationException>(() => Run(query, async));
            Assert.Empty(commands.Items);
            Assert.Equal(0, Native(context).Closes);
            AssertClean(context, commands);
        }

        private static Task<List<T>> Run<T>(IQueryable<T> query, bool async, CancellationToken token = default)
            => async ? query.ToListAsync(token) : Task.FromResult(query.ToList());
        private static IQueryable<T> Set<T>(IQueryable<T> left, IQueryable<T> right, string operation) => operation switch
        { "Union" => left.Union(right), "Concat" => left.Concat(right), "Intersect" => left.Intersect(right), _ => left.Except(right) };
        private static void AssertOperator(string sql, OceanBaseCompatibilityMode mode, string operation)
        {
            var keyword = operation switch { "Concat" => "UNION ALL", "Intersect" => "INTERSECT", "Except" => mode == OceanBaseCompatibilityMode.Oracle ? "MINUS" : "EXCEPT", _ => "UNION" };
            Assert.Equal(new[] { keyword }, Regex.Matches(sql, @"\b(?:UNION ALL|UNION|INTERSECT|MINUS|EXCEPT)\b").Select(m => m.Value));
        }
        private static DataTable Table(Type[] types, params object?[][] rows)
        {
            var table = new DataTable();
            for (var i = 0; i < types.Length; i++) table.Columns.Add("C" + i, types[i]);
            foreach (var row in rows) table.Rows.Add(row.Select(v => v ?? DBNull.Value).ToArray());
            return table;
        }
        private static TransactionTests.NativeConnection Native(DbContext context) => Assert.IsType<TransactionTests.NativeConnection>(context.Database.GetDbConnection());
        private static void AssertClean(DbContext context, Commands commands)
        {
            Assert.Equal(ConnectionState.Closed, Native(context).State);
            Assert.Null(context.Database.CurrentTransaction);
            Assert.All(commands.Readers, r => Assert.True(r.IsClosed));
        }
        private static SetContext Create(OceanBaseCompatibilityMode mode, Commands commands) => new(new DbContextOptionsBuilder<SetContext>()
            .UseOceanBase("Server=offline;Database=app", mode).ReplaceService<IRelationalConnection, TransactionTests.TestConnection>()
            .AddInterceptors(commands).Options);
        private sealed class SetContext(DbContextOptions<SetContext> options) : DbContext(options)
        {
            protected override void OnModelCreating(ModelBuilder model)
            {
                model.Entity<Row>().ToTable("SetRows");
                model.Entity<Row>().Property(r => r.Id).HasColumnName("RowKey");
                model.Entity<Row>().Property(r => r.Name).HasMaxLength(40);
                model.Entity<Row>().Property(r => r.Kind).HasConversion<string>().HasMaxLength(16);
                model.Entity<LobRow>().Property(r => r.Text).HasMaxLength(8000);
            }
        }
        private enum Kind { Active, Archived }
        private sealed class Row { public int Id { get; set; } public Kind? Kind { get; set; } public string? Name { get; set; } public int? Value { get; set; } }
        private sealed class LobRow { public int Id { get; set; } public string? Text { get; set; } }
        private sealed record Command(string Sql, object?[] Inputs, CancellationToken Token);
        private sealed class Commands : DbCommandInterceptor
        {
            public DataTable Results { get; set; } = new();
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
                    var p = match.Value == "?" ? parameters[position++] : parameters.Single(p => p.ParameterName.TrimStart('@', ':') == match.Value.TrimStart('@', ':'));
                    used.Add(p);
                    return p.Value;
                }).ToArray();
                Assert.Equal(parameters.Length, used.Count);
                var sql = Regex.Replace(Regex.Replace(command.CommandText.Replace("`", "", StringComparison.Ordinal)
                    .Replace("\"", "", StringComparison.Ordinal).Trim(), @"[@:]\w+", "?"), @"\s+", " ");
                Items.Add(new Command(sql, inputs, token));
                if (Failure is not null) { BeforeFailure?.Invoke(); throw Failure; }
                var reader = Results.CreateDataReader();
                Readers.Add(reader);
                return InterceptionResult<DbDataReader>.SuppressWithResult(reader);
            }
            public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result) => Read(command, default);
            public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
                InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) => ValueTask.FromResult(Read(command, cancellationToken));
        }
    }
}
