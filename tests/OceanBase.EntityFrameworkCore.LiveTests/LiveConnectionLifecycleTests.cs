using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.LiveTests
{
    public sealed class LiveConnectionLifecycleTests
    {
        public static IEnumerable<object[]> Cases()
        {
            foreach (var mode in Enum.GetValues<OceanBaseCompatibilityMode>())
                foreach (var async in new[] { false, true }) yield return [mode, async];
        }

        public static IEnumerable<object[]> OwnershipCases()
        {
            foreach (var pair in Cases())
                foreach (var ownership in new[] { "provider", "supplied-closed", "caller-open" })
                    foreach (var fail in new[] { false, true }) yield return [pair[0], pair[1], ownership, fail];
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public Task Direct_connector_control_opens_and_executes(OceanBaseCompatibilityMode mode, bool async)
            => Run(mode, async (configured, stage) =>
            {
                stage("direct Connector open");
                using var native = new OceanBaseConnection(configured);
                if (async) await native.OpenAsync(); else native.Open();
                stage("direct Connector SELECT");
                await NativeOne(native, mode, async);
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public Task Direct_reader_control_requires_response_consumption_to_retain_the_session(OceanBaseCompatibilityMode mode, bool async)
            => Run(mode, async (configured, stage) =>
            {
                foreach (var drain in new[] { false, true })
                {
                    stage("direct partial-reader disposal; drain=" + drain);
                    using var native = new OceanBaseConnection(configured);
                    if (async) await native.OpenAsync(); else native.Open();
                    using (var command = native.CreateCommand())
                    {
                        command.CommandText = SelectOne(mode) + (mode == OceanBaseCompatibilityMode.MySql ? " UNION ALL SELECT 2" : " UNION ALL SELECT 2 FROM DUAL");
                        var reader = async ? await command.ExecuteReaderAsync() : command.ExecuteReader();
                        try
                        {
                            Assert.True(async ? await reader.ReadAsync() : reader.Read());
                            if (drain)
                            {
                                do { while (async ? await reader.ReadAsync() : reader.Read()) { } }
                                while (async ? await reader.NextResultAsync() : reader.NextResult());
                            }
                        }
                        finally { if (async) await reader.DisposeAsync(); else reader.Dispose(); }
                    }
                    Assert.Equal(drain ? ConnectionState.Open : ConnectionState.Broken, native.State);
                    if (!drain)
                    {
                        if (async) { await native.CloseAsync(); await native.OpenAsync(); }
                        else { native.Close(); native.Open(); }
                    }
                    await NativeOne(native, mode, async);
                }
            });

        [Theory]
        [MemberData(nameof(OwnershipCases))]
        public Task Real_open_query_error_and_disposal_preserve_connection_ownership(
            OceanBaseCompatibilityMode mode, bool async, string ownership, bool fail)
            => Run(mode, async (configured, stage) =>
            {
                using var supplied = ownership == "provider" ? null : new OceanBaseConnection(configured);
                if (ownership == "caller-open")
                {
                    stage("caller native open");
                    if (async) await supplied!.OpenAsync(); else supplied!.Open();
                }
                var capture = new Commands();
                var builder = new DbContextOptionsBuilder();
                if (supplied is null) builder.UseOceanBase(configured, mode);
                else builder.UseOceanBase(supplied, mode);
                var context = new DbContext(builder.AddInterceptors(capture).Options);
                var native = context.Database.GetDbConnection();
                try
                {
                    stage("native query and server error");
                    await One(context, mode, async);
                    Assert.Equal(ownership == "caller-open" ? ConnectionState.Open : ConnectionState.Closed, native.State);
                    if (fail)
                    {
                        var missing = "EFT_MISSING_" + Guid.NewGuid().ToString("N")[..16];
                        var missingSql = "SELECT Id FROM " + missing;
                        var error = await Assert.ThrowsAsync<OceanBaseException>(async () =>
                        {
                            var query = context.Database.SqlQueryRaw<int>(missingSql);
                            if (async) await query.ToArrayAsync(); else query.ToArray();
                        });
                        Assert.NotEqual(0, error.ServerErrorCode);
                        Assert.False(error.IsExecutionOutcomeUnknown);
                        Assert.Same(error, capture.Error);
                        Assert.Equal(ownership == "caller-open" ? ConnectionState.Open : ConnectionState.Closed, native.State);
                        await One(context, mode, async);
                    }
                    stage("nested EF leases");
                    await Open(context, async);
                    await Open(context, async);
                    await One(context, mode, async);
                    await Close(context, async);
                    Assert.Equal(ConnectionState.Open, native.State);
                    await One(context, mode, async);
                    await Close(context, async);
                    Assert.Equal(ownership == "caller-open" ? ConnectionState.Open : ConnectionState.Closed, native.State);
                }
                finally
                {
                    stage("context disposal");
                    if (async) await context.DisposeAsync(); else context.Dispose();
                }
                stage("post-disposal native ownership");
                if (supplied is null)
                {
                    Assert.Throws<ObjectDisposedException>(() => native.ConnectionString = configured);
                }
                else
                {
                    Assert.Equal(ownership == "caller-open" ? ConnectionState.Open : ConnectionState.Closed, supplied.State);
                    if (supplied.State != ConnectionState.Open)
                    {
                        if (async) await supplied.OpenAsync(); else supplied.Open();
                    }
                    await NativeOne(supplied, mode, async);
                }
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public Task Partial_reader_disposal_releases_automatic_lease_and_preserves_explicit_lease(
            OceanBaseCompatibilityMode mode, bool async)
            => Run(mode, async (configured, stage) =>
            {
                await using var context = new DbContext(new DbContextOptionsBuilder().UseOceanBase(configured, mode).Options);
                foreach (var explicitOpen in new[] { false, true })
                {
                    stage("partial enumeration; explicit=" + explicitOpen);
                    if (explicitOpen) await Open(context, async);
                    var sql = mode == OceanBaseCompatibilityMode.MySql
                        ? "SELECT 1 AS `Value` UNION ALL SELECT 2 UNION ALL SELECT 3"
                        : "SELECT 1 AS \"Value\" FROM DUAL UNION ALL SELECT 2 FROM DUAL UNION ALL SELECT 3 FROM DUAL";
                    var query = context.Database.SqlQueryRaw<int>(sql).OrderBy(value => value);
                    if (async)
                    {
                        await using var reader = query.AsAsyncEnumerable().GetAsyncEnumerator();
                        Assert.True(await reader.MoveNextAsync());
                        Assert.Equal(1, reader.Current);
                        Assert.Equal(ConnectionState.Open, context.Database.GetDbConnection().State);
                    }
                    else
                    {
                        using var reader = query.AsEnumerable().GetEnumerator();
                        Assert.True(reader.MoveNext());
                        Assert.Equal(1, reader.Current);
                        Assert.Equal(ConnectionState.Open, context.Database.GetDbConnection().State);
                    }
                    stage("after partial disposal; explicit=" + explicitOpen + "; state=" + context.Database.GetDbConnection().State);
                    Assert.Equal(explicitOpen ? ConnectionState.Open : ConnectionState.Closed, context.Database.GetDbConnection().State);
                    await One(context, mode, async);
                    if (explicitOpen) await Close(context, async);
                    Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
                }
            });

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public Task Pre_cancelled_open_and_query_and_post_yield_cancellation_release_native_resources(
            OceanBaseCompatibilityMode mode)
            => Run(mode, async (configured, stage) =>
            {
                var capture = new Commands();
                await using var context = new DbContext(new DbContextOptionsBuilder().UseOceanBase(configured, mode).AddInterceptors(capture).Options);
                using var cancelled = new CancellationTokenSource();
                cancelled.Cancel();
                stage("pre-cancelled open/query");
                var openError = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.Database.OpenConnectionAsync(cancelled.Token));
                Assert.Equal(cancelled.Token, openError.CancellationToken);
                var queryError = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.Database.SqlQueryRaw<int>(SelectOne(mode)).ToArrayAsync(cancelled.Token));
                Assert.Equal(cancelled.Token, queryError.CancellationToken);
                Assert.Equal(0, capture.Readers);
                Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
                await One(context, mode, true);
                stage("post-yield cancellation");
                using var caller = new CancellationTokenSource();
                var sql = SelectOne(mode) + (mode == OceanBaseCompatibilityMode.MySql ? " UNION ALL SELECT 2" : " UNION ALL SELECT 2 FROM DUAL");
                await using (var reader = context.Database.SqlQueryRaw<int>(sql).AsAsyncEnumerable().GetAsyncEnumerator(caller.Token))
                {
                    Assert.True(await reader.MoveNextAsync());
                    caller.Cancel();
                    var error = await Assert.ThrowsAsync<OceanBaseException>(() => reader.MoveNextAsync().AsTask());
                    Assert.True(error.IsExecutionOutcomeUnknown);
                    stage("post-yield cancellation cause token");
                    Assert.Equal(caller.Token, Assert.IsType<OperationCanceledException>(error.InnerException).CancellationToken);
                }
                stage("after cancelled reader disposal; state=" + context.Database.GetDbConnection().State);
                Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
                await One(context, mode, true);
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public Task Detected_mode_conflict_rejects_before_command_without_disposing_caller_connection(
            OceanBaseCompatibilityMode mode, bool async)
            => Run(mode, async (configured, stage) =>
            {
                using var supplied = new OceanBaseConnection(configured);
                stage("native detected mode");
                if (async) await supplied.OpenAsync(); else supplied.Open();
                var expected = mode == OceanBaseCompatibilityMode.MySql
                    ? OceanBaseConnector.OceanBaseCompatibilityMode.MySql : OceanBaseConnector.OceanBaseCompatibilityMode.Oracle;
                Assert.Equal(expected, supplied.CompatibilityMode);
                var opposite = mode == OceanBaseCompatibilityMode.MySql ? OceanBaseCompatibilityMode.Oracle : OceanBaseCompatibilityMode.MySql;
                var capture = new Commands();
                stage("reject actual-mode mismatch");
                await using (var context = new DbContext(new DbContextOptionsBuilder().UseOceanBase(supplied, opposite).AddInterceptors(capture).Options))
                {
                    var error = await Assert.ThrowsAsync<InvalidOperationException>(() => One(context, mode, async));
                    Assert.Contains("connection mode does not match", error.Message, StringComparison.OrdinalIgnoreCase);
                    Assert.Equal(0, capture.Readers);
                }
                Assert.Equal(ConnectionState.Open, supplied.State);
                await NativeOne(supplied, mode, async);
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public Task Caller_opened_connection_can_be_closed_and_reopened_without_losing_credentials(
            OceanBaseCompatibilityMode mode, bool async)
            => Run(mode, async (configured, stage) =>
            {
                var builder = new OceanBaseConnectionStringBuilder(configured) { ExpectedMode = null, PersistSecurityInfo = false };
                using var supplied = new OceanBaseConnection(builder.ConnectionString);
                stage("caller opens before EF");
                if (async) await supplied.OpenAsync(); else supplied.Open();
                await using var context = new DbContext(new DbContextOptionsBuilder().UseOceanBase(supplied, mode).Options);
                await One(context, mode, async);
                stage("caller closes after EF use");
                if (async) await supplied.CloseAsync(); else supplied.Close();
                stage("EF reopens caller connection");
                await One(context, mode, async);
                Assert.Equal(ConnectionState.Closed, supplied.State);
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public Task Callback_failure_after_real_open_closes_the_connection_and_restores_EF_accounting(
            OceanBaseCompatibilityMode mode, bool async)
            => Run(mode, async (configured, stage) =>
            {
                var failure = new InvalidOperationException("test opened callback failure");
                var interceptor = new OpenedFailure { Failure = failure };
                await using var context = new DbContext(new DbContextOptionsBuilder().UseOceanBase(configured, mode).AddInterceptors(interceptor).Options);
                stage("opened callback failure");
                Assert.Same(failure, await Record.ExceptionAsync(() => Open(context, async)));
                Assert.Equal(ConnectionState.Open, interceptor.ObservedState);
                Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
                interceptor.Failure = null;
                stage("reuse after failed open callback");
                await Open(context, async);
                await One(context, mode, async);
                await Close(context, async);
                Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
                await One(context, mode, async);
                Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public Task Native_command_timeout_has_an_explicit_close_reopen_recovery_path(OceanBaseCompatibilityMode mode, bool async)
            => Interrupted(mode, async, false);

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public Task Native_in_flight_cancellation_preserves_cause_and_has_an_explicit_recovery_path(OceanBaseCompatibilityMode mode)
            => Interrupted(mode, true, true);

        private static Task Interrupted(OceanBaseCompatibilityMode mode, bool async, bool cancel)
            => Run(mode, async (configured, stage) =>
            {
                var capture = new Commands();
                await using var context = new DbContext(new DbContextOptionsBuilder().UseOceanBase(configured, mode).AddInterceptors(capture).Options);
                stage("open before interruption");
                await Open(context, async);
                await One(context, mode, async);
                context.Database.SetCommandTimeout(cancel ? 10 : 1);
                using var caller = new CancellationTokenSource();
                capture.Starting = () => { if (cancel) caller.CancelAfter(TimeSpan.FromMilliseconds(250)); };
                var sql = mode == OceanBaseCompatibilityMode.MySql ? "SELECT SLEEP(5)" : "BEGIN DBMS_LOCK.SLEEP(5); END;";
                stage(cancel ? "in-flight caller cancellation" : "native command timeout");
                var error = await Assert.ThrowsAsync<OceanBaseException>(async () =>
                {
                    if (async) await context.Database.ExecuteSqlRawAsync(sql, caller.Token);
                    else context.Database.ExecuteSqlRaw(sql);
                });
                Assert.Same(error, capture.Error);
                Assert.True(error.IsExecutionOutcomeUnknown);
                if (cancel)
                {
                    Assert.True(caller.IsCancellationRequested);
                    Assert.Equal(caller.Token, Assert.IsType<OperationCanceledException>(error.InnerException).CancellationToken);
                }
                else Assert.IsType<TimeoutException>(error.InnerException);
                stage("close and reopen after broken session");
                capture.Starting = null;
                context.Database.SetCommandTimeout(30);
                await Close(context, async);
                Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
                await Open(context, async);
                await One(context, mode, async);
                await Close(context, async);
                Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
                await One(context, mode, async);
            });

        private static string SelectOne(OceanBaseCompatibilityMode mode)
            => mode == OceanBaseCompatibilityMode.MySql ? "SELECT 1 AS `Value`" : "SELECT 1 AS \"Value\" FROM DUAL";

        private static async Task One(DbContext context, OceanBaseCompatibilityMode mode, bool async)
        {
            var query = context.Database.SqlQueryRaw<int>(SelectOne(mode));
            Assert.Equal(1, Assert.Single(async ? await query.ToArrayAsync() : query.ToArray()));
        }

        private static async Task NativeOne(OceanBaseConnection connection, OceanBaseCompatibilityMode mode, bool async)
        {
            using var command = connection.CreateCommand();
            command.CommandText = SelectOne(mode);
            var value = async ? await command.ExecuteScalarAsync() : command.ExecuteScalar();
            Assert.Equal(1m, value is OceanBaseDecimal number ? number.ToDecimal() : Convert.ToDecimal(value));
        }

        private static Task Open(DbContext context, bool async)
        {
            if (async) return context.Database.OpenConnectionAsync();
            context.Database.OpenConnection();
            return Task.CompletedTask;
        }

        private static Task Close(DbContext context, bool async)
        {
            if (async) return context.Database.CloseConnectionAsync();
            context.Database.CloseConnection();
            return Task.CompletedTask;
        }

        private static async Task Run(OceanBaseCompatibilityMode mode, Func<string, Action<string>, Task> run)
        {
            if (Environment.GetEnvironmentVariable("OCEANBASE_RUN_LIVE_TESTS") != "1")
                throw new InvalidOperationException("Set OCEANBASE_RUN_LIVE_TESTS=1 to run native connection lifecycle tests.");
            var key = mode == OceanBaseCompatibilityMode.MySql ? "ConnectionStrings:OceanBaseMySql" : "ConnectionStrings:OceanBaseOracle";
            var configured = new ConfigurationBuilder().AddUserSecrets<LiveConnectionTests>(optional: false).Build()[key];
            if (string.IsNullOrWhiteSpace(configured)) throw new InvalidOperationException($"Missing User Secret '{key}'.");
            configured = LiveTestConfiguration.ConnectionString(configured);
            var stage = "configuration";
            try { await run(configured, value => stage = value); }
            catch (Exception error)
            {
                // Even assertion failures may wrap native exceptions containing credentials.
                var types = new List<string>();
                var codes = new List<int>();
                for (var nested = error; nested is not null; nested = nested.InnerException)
                {
                    var category = nested.Message.Contains("Connection closed while reading", StringComparison.OrdinalIgnoreCase) ? "packet-closure"
                        : nested.Message.Contains("does not advertise TLS", StringComparison.OrdinalIgnoreCase) ? "server-no-tls"
                        : nested.Message.Contains("sequence", StringComparison.OrdinalIgnoreCase) ? "packet-sequence"
                        : nested.Message.Contains("authentication", StringComparison.OrdinalIgnoreCase) ? "authentication"
                        : nested.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ? "timeout" : "unclassified";
                    var frame = new System.Diagnostics.StackTrace(nested).GetFrames()?.FirstOrDefault()?.GetMethod();
                    types.Add(nested.GetType().Name + ":" + category + ":" + frame?.DeclaringType?.Name + "." + frame?.Name);
                    if (nested is OceanBaseException native) codes.Add(native.ServerErrorCode);
                }
                throw new InvalidOperationException($"{mode}: connection lifecycle stage={stage}; types={string.Join('/', types)}; server-codes={string.Join(',', codes)}. No test objects created.");
            }
        }

        private sealed class Commands : DbCommandInterceptor
        {
            public int Readers { get; private set; }
            public Exception? Error { get; private set; }
            public Action? Starting { get; set; }
            public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
            { Readers++; return result; }
            public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
                InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(ReaderExecuting(command, eventData, result));
            public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
            { Starting?.Invoke(); return result; }
            public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
                InterceptionResult<int> result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(NonQueryExecuting(command, eventData, result));
            public override void CommandFailed(DbCommand command, CommandErrorEventData eventData) => Error = eventData.Exception;
            public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
            { CommandFailed(command, eventData); return Task.CompletedTask; }
        }

        private sealed class OpenedFailure : DbConnectionInterceptor
        {
            public Exception? Failure { get; set; }
            public ConnectionState ObservedState { get; private set; }
            public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
            {
                ObservedState = connection.State;
                if (Failure is not null) throw Failure;
            }
            public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
            { ConnectionOpened(connection, eventData); return Task.CompletedTask; }
        }
    }
}
