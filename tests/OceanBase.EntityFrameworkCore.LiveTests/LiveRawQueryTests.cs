using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.LiveTests
{
    public sealed class LiveRawQueryTests
    {
        private const string KeyColumn = "Row`Key";
        private const string Hostile = "raw' -- @p0 {0} 海🌊";

        public static IEnumerable<object[]> Cases() => LiveConnectionLifecycleTests.Cases();

        public static IEnumerable<object[]> TrackingCases()
        {
            foreach (var pair in Cases())
                foreach (var tracking in Enum.GetValues<QueryTrackingBehavior>()) yield return [pair[0], pair[1], tracking];
        }

        [Theory]
        [MemberData(nameof(TrackingCases))]
        public Task Native_raw_metadata_converters_and_tracking_keep_identity(
            OceanBaseCompatibilityMode mode, bool async, QueryTrackingBehavior tracking)
            => WithTable(mode, async, async (context, capture, stage) =>
            {
                context.ChangeTracker.QueryTrackingBehavior = tracking;
                stage("raw reordered mapped columns, duplicate keys and nullable converter");
                var sql = Select(context) + " UNION ALL " + Select(context) + " WHERE " + Quote(context, KeyColumn) + " = 1";
                var query = context.Set<Row>().FromSqlRaw(sql).OrderBy(row => row.Id);
                var first = await Read(query, async);
                Assert.Equal(new[] { 1, 1, 2, 3 }, first.Select(row => row.Id));
                Assert.Equal(Hostile, first[0].Name);
                Assert.Equal(Kind.Active, first[0].Kind);
                Assert.Equal(7, first[0].Value);
                Assert.Null(first[2].Name);
                Assert.Null(first[2].Kind);
                Assert.Null(first[2].Value);
                Assert.Equal(tracking != QueryTrackingBehavior.NoTracking, ReferenceEquals(first[0], first[1]));
                var second = await Read(query, async);
                Assert.Equal(tracking == QueryTrackingBehavior.TrackAll, ReferenceEquals(first[0], second[0]));
                Assert.Equal(tracking == QueryTrackingBehavior.TrackAll ? 3 : 0, context.ChangeTracker.Entries().Count());
                Assert.Equal(capture.Items[^2].Sql, capture.Items[^1].Sql);
                Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public Task Raw_and_interpolated_queries_refresh_parameters_without_changing_SQL_or_exposing_values(
            OceanBaseCompatibilityMode mode, bool async)
            => WithTable(mode, async, async (context, capture, stage) =>
            {
                stage("raw/interpolated entity predicates and scalar SQL");
                foreach (var interpolated in new[] { false, true })
                {
                    string? previousSql = null;
                    foreach (var input in new string?[] { Hostile, null, "again", Hostile })
                    {
                        var sql = Select(context) + " WHERE (" + Quote(context, "DisplayName") + " = {0} OR ("
                            + Quote(context, "DisplayName") + " IS NULL AND {1} IS NULL))";
                        var source = interpolated
                            ? context.Set<Row>().FromSqlInterpolated(FormattableStringFactory.Create(sql, input!, input!))
                            : context.Set<Row>().FromSqlRaw(sql, input!, input!);
                        var query = source.Where(row => row.Id > 0).OrderBy(row => row.Id).Skip(0).Take(1).Select(row => row.Id);
                        Assert.Equal(new[] { input == Hostile ? 1 : input is null ? 2 : 3 }, await Read(query, async));
                        var actual = capture.Items[^1];
                        Assert.DoesNotContain(Hostile, actual.Sql);
                        Assert.Equal(2, actual.Values.Count(value => Equals(value, input ?? (object)DBNull.Value)));
                        if (previousSql is not null) Assert.Equal(previousSql, actual.Sql);
                        previousSql = actual.Sql;
                    }
                    var scalarSql = "SELECT " + Quote(context, "Maybe") + " AS " + Quote(context, "Value") + " FROM "
                        + Quote(context, context.Table) + " WHERE " + Quote(context, KeyColumn) + " = {0}";
                    foreach (var (id, expected) in new[] { (1, (int?)7), (2, (int?)null), (3, (int?)9) })
                    {
                        var scalar = interpolated
                            ? context.Database.SqlQuery<int?>(FormattableStringFactory.Create(scalarSql, id))
                            : context.Database.SqlQueryRaw<int?>(scalarSql, id);
                        Assert.Equal(expected, Assert.Single(await Read(scalar, async)));
                    }
                }
                stage("default command log parameter redaction");
                Assert.NotEmpty(capture.Logs);
                Assert.Contains(capture.Logs, log => log.Contains("Executed DbCommand", StringComparison.Ordinal));
                Assert.All(capture.Logs, log => Assert.DoesNotContain(Hostile, log));
                Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public Task Raw_and_interpolated_updates_bind_values_and_report_matched_rows(
            OceanBaseCompatibilityMode mode, bool async)
            => WithTable(mode, async, async (context, capture, stage) =>
            {
                stage("ExecuteSql raw/interpolated UPDATE and no-match count");
                foreach (var interpolated in new[] { false, true })
                {
                    string? previousSql = null;
                    foreach (var value in new string?[] { Hostile, null, "again" })
                    {
                        var sql = "UPDATE " + Quote(context, context.Table) + " SET " + Quote(context, "DisplayName")
                            + " = {0} WHERE " + Quote(context, KeyColumn) + " = {1}";
                        var formattable = FormattableStringFactory.Create(sql, value!, 3);
                        var affected = (async, interpolated) switch
                        {
                            (true, true) => await context.Database.ExecuteSqlInterpolatedAsync(formattable),
                            (true, false) => await context.Database.ExecuteSqlRawAsync(sql, value!, 3),
                            (false, true) => context.Database.ExecuteSqlInterpolated(formattable),
                            _ => context.Database.ExecuteSqlRaw(sql, value!, 3)
                        };
                        Assert.Equal(1, affected);
                        var command = capture.Items[^1];
                        Assert.DoesNotContain(Hostile, command.Sql);
                        Assert.Equal(new object[] { value ?? (object)DBNull.Value, 3 }, command.Values);
                        if (previousSql is not null) Assert.Equal(previousSql, command.Sql);
                        previousSql = command.Sql;
                        Assert.Equal(value, Assert.Single(await Read(context.Set<Row>().Where(row => row.Id == 3).Select(row => row.Name), async)));
                    }
                }
                var missingSql = "DELETE FROM " + Quote(context, context.Table) + " WHERE " + Quote(context, KeyColumn) + " = {0}";
                Assert.Equal(0, async ? await context.Database.ExecuteSqlRawAsync(missingSql, -1) : context.Database.ExecuteSqlRaw(missingSql, -1));
                Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public Task Native_raw_shape_and_server_errors_release_resources_and_allow_recovery(
            OceanBaseCompatibilityMode mode, bool async)
            => WithTable(mode, async, async (context, capture, stage) =>
            {
                foreach (var empty in new[] { false, true })
                {
                    stage("missing converter column; empty=" + empty);
                    var missing = Select(context, includeKind: false) + (empty ? " WHERE 1 = 0" : "");
                    var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Read(context.Set<Row>().FromSqlRaw(missing), async));
                    Assert.Contains("State", error.Message, StringComparison.OrdinalIgnoreCase);
                    await Recover();
                }
                stage("invalid converter value");
                var badKind = Select(context).Replace(Quote(context, "State"), "'UnknownKind' AS " + Quote(context, "State"), StringComparison.Ordinal);
                var conversion = await Assert.ThrowsAsync<InvalidOperationException>(() => Read(context.Set<Row>().FromSqlRaw(badKind), async));
                Assert.Contains("Kind", conversion.Message, StringComparison.Ordinal);
                Assert.NotNull(conversion.InnerException);
                await Recover();
                stage("required key NULL with real metadata");
                var nullType = mode == OceanBaseCompatibilityMode.MySql ? "SIGNED" : "NUMBER(10)";
                var nullKey = Select(context).Replace(Quote(context, KeyColumn), "CAST(NULL AS " + nullType + ") AS " + Quote(context, KeyColumn), StringComparison.Ordinal);
                var required = await Assert.ThrowsAsync<InvalidOperationException>(() => Read(context.Set<Row>().FromSqlRaw(nullKey), async));
                Assert.Contains("Id", required.Message, StringComparison.Ordinal);
                Assert.NotNull(required.InnerException);
                await Recover();
                stage("native SELECT error and standard interceptor identity");
                var invalid = "SELECT " + Quote(context, "missing_column") + " FROM " + Quote(context, context.Table)
                    + " WHERE " + Quote(context, "DisplayName") + " = {0}";
                var native = await Assert.ThrowsAsync<OceanBaseException>(() => Read(context.Database.SqlQueryRaw<int>(invalid, Hostile), async));
                Assert.NotEqual(0, native.ServerErrorCode);
                Assert.False(native.IsExecutionOutcomeUnknown);
                Assert.Same(native, capture.Error);
                Assert.Equal(Hostile, Assert.Single(capture.Items[^1].Values));
                Assert.Contains(capture.Logs, log => log.Contains("Failed executing DbCommand", StringComparison.Ordinal));
                Assert.All(capture.Logs, log => Assert.DoesNotContain(Hostile, log));
                await Recover();
                stage("noncomposable input rejects before command");
                var count = capture.Items.Count;
                var noncomposable = "DELETE FROM " + Quote(context, context.Table);
                await Assert.ThrowsAsync<InvalidOperationException>(() => Read(context.Set<Row>().FromSqlRaw(noncomposable).Where(row => row.Id > 0), async));
                Assert.Equal(count, capture.Items.Count);
                await Recover();

                async Task Recover()
                {
                    Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
                    Assert.Empty(context.ChangeTracker.Entries());
                    Assert.Equal(new[] { 1, 2, 3 }, await Read(context.Set<Row>().OrderBy(row => row.Id).Select(row => row.Id), async));
                    Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
                }
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public Task Repeated_caller_parameter_preserves_native_ownership_and_refreshes_each_position(OceanBaseCompatibilityMode mode, bool async)
            => WithTable(mode, async, async (context, capture, stage) =>
            {
                stage("repeated caller-owned native parameter");
                var parameter = new OceanBaseParameter { ParameterName = "floor", DbType = DbType.Int32, Value = 2 };
                var name = mode == OceanBaseCompatibilityMode.MySql ? "@floor" : ":floor";
                var sql = Select(context) + " WHERE " + Quote(context, KeyColumn) + " > " + name + " OR " + Quote(context, "Maybe") + " = " + name;
                var query = context.Set<Row>().FromSqlRaw(sql, parameter).OrderBy(row => row.Id);
                parameter.Value = 7;
                Assert.Equal(new[] { 1 }, (await Read(query, async)).Select(row => row.Id));
                parameter.Value = 2;
                Assert.Equal(new[] { 3 }, (await Read(query, async)).Select(row => row.Id));
                Assert.All(capture.Items[^1].Values, value => Assert.Equal(2, value));
                if (mode == OceanBaseCompatibilityMode.MySql)
                    Assert.Equal(2, capture.Items[^1].Values.Length);
                else
                {
                    Assert.Single(capture.Items[^1].Values);
                    Assert.Equal(2, capture.Items[^1].Sql.Split(name, StringSplitOptions.None).Length - 1);
                }
                Assert.Equal("floor", parameter.ParameterName);
                Assert.Equal(2, parameter.Value);
                Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
                Assert.Equal(new[] { 1, 2, 3 }, await Read(context.Set<Row>().OrderBy(row => row.Id).Select(row => row.Id), async));
            });

        private static string Quote(Context context, string name) => context.GetService<ISqlGenerationHelper>().DelimitIdentifier(name);
        private static string Select(Context context, bool includeKind = true)
            => "SELECT " + Quote(context, "Maybe") + ", " + Quote(context, KeyColumn) + ", "
                + (includeKind ? Quote(context, "State") + ", " : "") + Quote(context, "DisplayName") + " FROM " + Quote(context, context.Table);
        private static Task<T[]> Read<T>(IQueryable<T> query, bool async) => async ? query.ToArrayAsync() : Task.FromResult(query.ToArray());

        private static Task WithTable(OceanBaseCompatibilityMode mode, bool async, Func<Context, Capture, Action<string>, Task> run)
        {
            var capture = new Capture();
            return LiveStorageTable.WithTable<Context>(mode, async, false,
                (options, table) => new Context(new DbContextOptionsBuilder<Context>(options)
                    .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).EnableDetailedErrors().EnableServiceProviderCaching(false)
                    .ReplaceService<IModelCacheKeyFactory, CacheKey>().AddInterceptors(capture)
                    .LogTo(capture.Logs.Add, [DbLoggerCategory.Database.Command.Name], LogLevel.Information).Options, mode, table),
                (context, _, stage) =>
                {
                    capture.Items.Clear();
                    capture.Logs.Clear();
                    return run(context, capture, stage);
                });
        }

        private sealed class Context(DbContextOptions<Context> options, OceanBaseCompatibilityMode mode, string table) : DbContext(options)
        {
            public string Table => table;
            public OceanBaseCompatibilityMode Mode => mode;
            protected override void OnModelCreating(ModelBuilder model)
            {
                var row = model.Entity<Row>();
                row.ToTable(table);
                row.Property(value => value.Id).HasColumnName(KeyColumn).ValueGeneratedNever();
                row.Property(value => value.Value).HasColumnName("Maybe");
                row.Property(value => value.Name).HasColumnName("DisplayName").HasMaxLength(128);
                row.Property(value => value.Kind).HasColumnName("State").HasConversion<string>().HasMaxLength(16).IsUnicode(false);
                row.HasData(new Row { Id = 1, Value = 7, Name = Hostile, Kind = Kind.Active },
                    new Row { Id = 2 }, new Row { Id = 3, Value = 9, Name = "again", Kind = Kind.Inactive });
            }
        }

        private sealed class CacheKey : IModelCacheKeyFactory
        {
            public object Create(DbContext context, bool designTime)
                => (context.GetType(), ((Context)context).Mode, ((Context)context).Table, designTime);
        }

        private sealed class Capture : DbCommandInterceptor
        {
            public List<(string Sql, object[] Values)> Items { get; } = [];
            public List<string> Logs { get; } = [];
            public Exception? Error { get; private set; }
            private void Add(DbCommand command) => Items.Add((command.CommandText, command.Parameters.Cast<DbParameter>().Select(value => value.Value!).ToArray()));
            public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
            { Add(command); return result; }
            public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
                InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(ReaderExecuting(command, eventData, result));
            public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
            { Add(command); return result; }
            public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
                InterceptionResult<int> result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(NonQueryExecuting(command, eventData, result));
            public override void CommandFailed(DbCommand command, CommandErrorEventData eventData) => Error = eventData.Exception;
            public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
            { CommandFailed(command, eventData); return Task.CompletedTask; }
        }

        private enum Kind { Active, Inactive }
        private sealed class Row
        {
            public int Id { get; set; }
            public int? Value { get; set; }
            public string? Name { get; set; }
            public Kind? Kind { get; set; }
        }
    }
}
