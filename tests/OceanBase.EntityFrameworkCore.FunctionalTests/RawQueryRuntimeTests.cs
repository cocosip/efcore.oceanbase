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
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.FunctionalTests
{
    public sealed class RawQueryRuntimeTests
    {
        private const string Raw = "SELECT Maybe, RowKey, State, DisplayName FROM RawRows";

        public static IEnumerable<object[]> TrackingCases()
        {
            foreach (var pair in TransactionTests.Modes())
                foreach (var tracking in Enum.GetValues<QueryTrackingBehavior>())
                    yield return [pair[0], pair[1], tracking];
        }

        [Theory]
        [MemberData(nameof(TrackingCases))]
        public async Task Raw_entity_columns_use_mapped_names_and_preserve_tracking_identity(
            OceanBaseCompatibilityMode mode, bool async, QueryTrackingBehavior tracking)
        {
            var commands = new Commands { Results = EntityTable(duplicates: true) };
            await using var context = Create(mode, commands);
            context.ChangeTracker.QueryTrackingBehavior = tracking;
            var query = context.Set<Row>().FromSqlRaw(Raw);
            var result = await Run(query, async);
            Assert.Equal(new[] { 7, 7, 8 }, result.Select(r => r.Id));
            Assert.Equal(Kind.Active, result[0].Kind);
            Assert.Equal("海🌊", result[0].Name);
            Assert.Equal(2, result[0].Value);
            Assert.Null(result[^1].Kind);
            Assert.Null(result[^1].Name);
            Assert.Null(result[^1].Value);
            Assert.Equal(tracking != QueryTrackingBehavior.NoTracking, ReferenceEquals(result[0], result[1]));
            var again = await Run(query, async);
            Assert.Equal(tracking == QueryTrackingBehavior.TrackAll, ReferenceEquals(result[0], again[0]));
            Assert.Equal(tracking == QueryTrackingBehavior.TrackAll ? 2 : 0, context.ChangeTracker.Entries().Count());
            Assert.All(commands.Items, item => { Assert.Equal(Raw, item.Sql); Assert.Empty(item.Inputs); });
            AssertClean(context, commands);
        }

        [Theory]
        [MemberData(nameof(TransactionTests.Modes), MemberType = typeof(TransactionTests))]
        public async Task Composed_raw_queries_keep_hostile_and_null_inputs_predicates_order_and_paging(
            OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new Commands { Results = ScalarTable() };
            await using var context = Create(mode, commands);
            foreach (var raw in new[] { false, true })
                foreach (var input in new string?[] { "'; DROP TABLE RawRows; -- @p0 {0} 海🌊", null, "again" })
                {
                    var floor = input is null ? 2 : 3;
                    var source = raw
                        ? context.Set<Row>().FromSqlRaw(Raw + " WHERE DisplayName = {0}", input!)
                        : context.Set<Row>().FromSqlInterpolated($"SELECT Maybe, RowKey, State, DisplayName FROM RawRows WHERE DisplayName = {input}");
                    var query = source.Where(r => r.Id > floor).OrderBy(r => r.Id).Skip(1).Take(4).Select(r => r.Value);
                    Assert.Equal(new int?[] { null, 2 }, await Run(query, async));
                    var command = commands.Items[^1];
                    var projection = Regex.Match(command.Sql, @"^SELECT (\w+)\.Maybe FROM \(");
                    Assert.True(projection.Success, command.Sql);
                    var alias = projection.Groups[1].Value;
                    Assert.Contains("FROM ( SELECT Maybe, RowKey, State, DisplayName FROM RawRows WHERE DisplayName = ? )", command.Sql);
                    Assert.Contains(alias + ".RowKey > ?", command.Sql);
                    Assert.Contains("ORDER BY " + alias + ".RowKey", command.Sql);
                    Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "LIMIT ? OFFSET ?" : "OFFSET ? ROWS FETCH NEXT ? ROWS ONLY", command.Sql);
                    Assert.Equal(mode == OceanBaseCompatibilityMode.MySql
                        ? new object?[] { input ?? (object)DBNull.Value, floor, 4, 1 }
                        : [input ?? (object)DBNull.Value, floor, 1, 4], command.Inputs);
                    if (input is not null) Assert.DoesNotContain(input, command.Sql);
                    Assert.Empty(context.ChangeTracker.Entries());
                    AssertClean(context, commands);
                }
        }

        [Theory]
        [MemberData(nameof(TransactionTests.Modes), MemberType = typeof(TransactionTests))]
        public async Task Missing_mapped_columns_fail_for_nonempty_and_empty_readers_then_allow_recovery(
            OceanBaseCompatibilityMode mode, bool async)
        {
            foreach (var empty in new[] { false, true })
            {
                var missing = EntityTable();
                missing.Columns.Remove("state");
                if (empty) missing.Rows.Clear();
                var commands = new Commands { Results = missing };
                await using var context = Create(mode, commands);
                var query = context.Set<Row>().FromSqlRaw(Raw).AsNoTracking();
                var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Run(query, async));
                Assert.Contains("State", error.Message);
                Assert.Empty(context.ChangeTracker.Entries());
                AssertClean(context, commands);
                commands.Results = EntityTable();
                Assert.Equal(new[] { 7, 8 }, (await Run(query, async)).Select(r => r.Id));
                AssertClean(context, commands);
            }
        }

        [Theory]
        [MemberData(nameof(TransactionTests.Modes), MemberType = typeof(TransactionTests))]
        public async Task Invalid_reader_values_and_required_nulls_keep_property_diagnostics_and_release_resources(
            OceanBaseCompatibilityMode mode, bool async)
        {
            foreach (var nullKey in new[] { false, true })
            {
                var table = EntityTable();
                if (nullKey) table.Rows[0]["rowkey"] = DBNull.Value;
                else table.Rows[0]["state"] = "UnknownKind";
                var commands = new Commands { Results = table };
                await using var context = Create(mode, commands);
                var query = context.Set<Row>().FromSqlRaw(Raw).AsNoTracking();
                var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Run(query, async));
                Assert.Contains(nullKey ? "Id" : "Kind", error.Message);
                Assert.NotNull(error.InnerException);
                Assert.Empty(context.ChangeTracker.Entries());
                AssertClean(context, commands);
                commands.Results = EntityTable();
                Assert.Equal(2, (await Run(query, async)).Count);
                AssertClean(context, commands);
            }
        }

        [Theory]
        [MemberData(nameof(TransactionTests.Modes), MemberType = typeof(TransactionTests))]
        public async Task Noncomposable_composition_rejects_before_open_and_a_later_raw_query_succeeds(
            OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new Commands { Results = EntityTable() };
            await using var context = Create(mode, commands);
            var query = context.Set<Row>().FromSqlRaw("DELETE FROM RawRows").Where(r => r.Id > 0);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Run(query, async));
            Assert.Empty(commands.Items);
            Assert.Equal(0, commands.Opens);
            AssertClean(context, commands);
            Assert.Equal(2, (await Run(context.Set<Row>().FromSqlRaw(Raw).AsNoTracking(), async)).Count);
            AssertClean(context, commands);
        }

        [Theory]
        [MemberData(nameof(TransactionTests.Modes), MemberType = typeof(TransactionTests))]
        public async Task Repeated_caller_parameter_preserves_the_documented_mode_boundary_and_recovers(
            OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new Commands { Results = EntityTable() };
            await using var context = Create(mode, commands);
            var parameter = new OceanBaseParameter { ParameterName = "named", DbType = DbType.Int32, Value = 3 };
            var sql = mode == OceanBaseCompatibilityMode.MySql
                ? Raw + " WHERE RowKey > @named OR Maybe = @named"
                : Raw + " WHERE RowKey > :named OR Maybe = :named";
            var query = context.Set<Row>().FromSqlRaw(sql, parameter).AsNoTracking();
            if (mode == OceanBaseCompatibilityMode.MySql)
            {
                var error = await Assert.ThrowsAsync<NotSupportedException>(() => Run(query, async));
                Assert.Contains("clone contract", error.Message);
                Assert.Empty(commands.Items);
                Assert.Empty(commands.Readers);
                Assert.Equal(1, commands.Opens);
            }
            else
            {
                Assert.Equal(2, (await Run(query, async)).Count);
                Assert.Equal(new object?[] { 3, 3 }, Assert.Single(commands.Items).Inputs);
            }
            Assert.Equal("named", parameter.ParameterName);
            Assert.Equal(3, parameter.Value);
            AssertClean(context, commands);
            Assert.Equal(2, (await Run(context.Set<Row>().FromSqlRaw(Raw).AsNoTracking(), async)).Count);
            AssertClean(context, commands);
        }

        [Theory]
        [MemberData(nameof(TransactionTests.Modes), MemberType = typeof(TransactionTests))]
        public async Task Raw_command_failure_and_cancellation_preserve_identity_and_permit_later_enumeration(
            OceanBaseCompatibilityMode mode, bool async)
        {
            foreach (var cancel in new[] { false, true })
            {
                using var source = new CancellationTokenSource();
                Exception error = cancel ? new OperationCanceledException(source.Token) : new InvalidOperationException("raw command failure");
                var commands = new Commands { Results = EntityTable(), Failure = error, BeforeFailure = cancel ? source.Cancel : null };
                await using var context = Create(mode, commands);
                var query = context.Set<Row>().FromSqlRaw(Raw).AsNoTracking();
                Assert.Same(error, await Record.ExceptionAsync(() => Run(query, async, source.Token)));
                if (async) Assert.Equal(source.Token, Assert.Single(commands.Items).Token);
                Assert.Equal(cancel, source.IsCancellationRequested);
                AssertClean(context, commands);
                commands.Failure = null;
                Assert.Equal(2, (await Run(query, async)).Count);
                AssertClean(context, commands);
            }
        }

        private static Task<List<T>> Run<T>(IQueryable<T> query, bool async, CancellationToken token = default)
            => async ? query.ToListAsync(token) : Task.FromResult(query.ToList());
        private static DataTable EntityTable(bool duplicates = false)
        {
            var table = new DataTable();
            table.Columns.Add("maybe", typeof(int));
            table.Columns.Add("rowkey", typeof(int));
            table.Columns.Add("state", typeof(string));
            table.Columns.Add("displayname", typeof(string));
            table.Columns.Add("Extra", typeof(string));
            table.Rows.Add(2, 7, "Active", "海🌊", "ignored");
            if (duplicates) table.Rows.Add(2, 7, "Active", "海🌊", "ignored");
            table.Rows.Add(DBNull.Value, 8, DBNull.Value, DBNull.Value, "ignored");
            return table;
        }
        private static DataTable ScalarTable()
        {
            var table = new DataTable();
            table.Columns.Add("Maybe", typeof(int));
            table.Rows.Add(DBNull.Value);
            table.Rows.Add(2);
            return table;
        }
        private static TransactionTests.NativeConnection Native(DbContext context) => Assert.IsType<TransactionTests.NativeConnection>(context.Database.GetDbConnection());
        private static void AssertClean(DbContext context, Commands commands)
        {
            Assert.Equal(ConnectionState.Closed, Native(context).State);
            Assert.Null(context.Database.CurrentTransaction);
            Assert.All(commands.Readers, reader => Assert.True(reader.IsClosed));
        }
        private static RawContext Create(OceanBaseCompatibilityMode mode, Commands commands) => new(new DbContextOptionsBuilder<RawContext>()
            .UseOceanBase("Server=offline;Database=app", mode).ReplaceService<IRelationalConnection, TransactionTests.TestConnection>()
            .EnableServiceProviderCaching(false).EnableDetailedErrors().AddInterceptors(new Connections(commands), commands).Options);
        private sealed class RawContext(DbContextOptions<RawContext> options) : DbContext(options)
        {
            protected override void OnModelCreating(ModelBuilder model)
            {
                var row = model.Entity<Row>().ToTable("RawRows");
                row.Property(r => r.Id).HasColumnName("RowKey");
                row.Property(r => r.Kind).HasColumnName("State").HasConversion<string>().HasMaxLength(16);
                row.Property(r => r.Name).HasColumnName("DisplayName").HasMaxLength(40);
                row.Property(r => r.Value).HasColumnName("Maybe");
            }
        }
        private enum Kind { Active, Archived }
        private sealed class Row { public int Id { get; set; } public Kind? Kind { get; set; } public string? Name { get; set; } public int? Value { get; set; } }
        private sealed record Command(string Sql, object?[] Inputs, CancellationToken Token);
        private sealed class Commands : DbCommandInterceptor
        {
            public DataTable Results { get; set; } = new();
            public Exception? Failure { get; set; }
            public Action? BeforeFailure { get; init; }
            public int Opens { get; set; }
            public List<Command> Items { get; } = [];
            public List<DbDataReader> Readers { get; } = [];
            private InterceptionResult<DbDataReader> Read(DbCommand command, CancellationToken token)
            {
                var parameters = command.Parameters.Cast<DbParameter>().ToArray();
                var position = 0;
                var inputs = Regex.Matches(command.CommandText, @"[@:]\w+|\?")
                    .Select(m => m.Value == "?" ? parameters[position++] : parameters.Single(p => p.ParameterName.TrimStart('@', ':') == m.Value.TrimStart('@', ':')))
                    .Select(p => p.Value).ToArray();
                var sql = Regex.Replace(Regex.Replace(command.CommandText.Replace("`", "", StringComparison.Ordinal)
                    .Replace("\"", "", StringComparison.Ordinal).Trim(), @"[@:]\w+", "?"), @"\s+", " ");
                Items.Add(new Command(sql, inputs, token));
                if (Failure is not null) { BeforeFailure?.Invoke(); throw Failure; }
                var reader = Results.CreateDataReader();
                Readers.Add(reader);
                return InterceptionResult<DbDataReader>.SuppressWithResult(reader);
            }
            public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData e, InterceptionResult<DbDataReader> result) => Read(command, default);
            public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData e,
                InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) => ValueTask.FromResult(Read(command, cancellationToken));
        }

        private sealed class Connections(Commands commands) : DbConnectionInterceptor
        {
            public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData e, InterceptionResult result)
            { commands.Opens++; return result; }
            public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData e,
                InterceptionResult result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(ConnectionOpening(connection, e, result));
        }
    }
}
