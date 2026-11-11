using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBase.EntityFrameworkCore.Storage;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests
{
    public sealed class ConnectionLifecycleTests
    {
        public static IEnumerable<object[]> PhysicalFailureCases()
        {
            foreach (var mode in Enum.GetValues<OceanBaseCompatibilityMode>())
                foreach (var async in new[] { false, true })
                {
                    foreach (var phase in new[] { "native", "callback", "cancelled", "ambient" })
                        yield return [mode, async, phase, "closed"];
                    yield return [mode, async, "ambient", "caller"];
                    yield return [mode, async, "ambient", "outer"];
                }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, true)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, true)]
        public async Task Failed_provider_open_restores_outer_lease_and_can_be_retried(OceanBaseCompatibilityMode mode, bool async, bool outerLease)
        {
            await using var context = new DbContext(new DbContextOptionsBuilder().UseOceanBase("Server=localhost;Database=test", mode)
                .AddInterceptors(new SuppressNetwork())
                .ConfigureWarnings(warnings => warnings.Throw(RelationalEventId.AmbientTransactionWarning)).Options);
            var connection = context.GetService<IRelationalConnection>();
            if (outerLease)
            {
                if (async) await connection.OpenAsync(CancellationToken.None);
                else connection.Open();
            }
            using (var transaction = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
            {
                if (async) await Assert.ThrowsAsync<InvalidOperationException>(() => connection.OpenAsync(CancellationToken.None));
                else Assert.Throws<InvalidOperationException>(() => connection.Open());
                Assert.Equal(outerLease ? 1 : 0, OpenCount(connection));
                Assert.Equal(outerLease, OpenedInternally(connection));
            }
            if (async) await connection.OpenAsync(CancellationToken.None);
            else connection.Open();
            Assert.Equal(outerLease ? 2 : 1, OpenCount(connection));
            if (async) await connection.CloseAsync();
            else connection.Close();
            Assert.Equal(outerLease ? 1 : 0, OpenCount(connection));
            if (outerLease)
            {
                if (async) await connection.CloseAsync();
                else connection.Close();
            }
            Assert.Equal(0, OpenCount(connection));
            Assert.False(OpenedInternally(connection));
        }

        [Theory]
        [MemberData(nameof(PhysicalFailureCases))]
        public async Task Failed_open_snapshot_restores_physical_state_and_exact_accounting(
            OceanBaseCompatibilityMode mode, bool async, string phase, string initial)
        {
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            Exception failure = phase == "cancelled" ? new OperationCanceledException(cancelled.Token)
                : new InvalidOperationException("test opening failure");
            var callback = new OpenedFailure { Failure = phase is "callback" or "cancelled" ? failure : null };
            await using var context = new DbContext(new DbContextOptionsBuilder()
                .UseOceanBase("Server=localhost;Database=test", mode).AddInterceptors(callback)
                .ConfigureWarnings(warnings => warnings.Throw(RelationalEventId.AmbientTransactionWarning)).Options);
            var native = new PhysicalConnection { Failure = phase == "native" ? failure : null };
            await using var connection = new PhysicalRelationalConnection(context.GetService<RelationalConnectionDependencies>(), native);
            if (initial == "caller") native.Open();
            else if (initial == "outer")
            {
                if (async) await connection.OpenAsync(CancellationToken.None);
                else connection.Open();
            }
            var entry = new OceanBaseConnectionOpenState(connection);
            using (var ambient = phase == "ambient" ? new TransactionScope(TransactionScopeAsyncFlowOption.Enabled) : null)
            {
                async Task Open()
                {
                    if (async) await connection.OpenAsync(CancellationToken.None);
                    else connection.Open();
                }
                var actual = await Assert.ThrowsAnyAsync<Exception>(Open);
                if (phase == "ambient") Assert.Contains("AmbientTransactionWarning", actual.Message);
                else Assert.Same(failure, actual);
                Assert.Equal(phase == "native" ? ConnectionState.Closed : ConnectionState.Open, native.State);
                if (async) await entry.RestoreAsync(actual);
                else entry.Restore(actual);
            }
            Assert.Equal(initial == "closed" ? ConnectionState.Closed : ConnectionState.Open, native.State);
            Assert.Equal(initial == "outer" ? 1 : 0, OpenCount(connection));
            Assert.Equal(initial == "outer", OpenedInternally(connection));
            var closedHere = initial == "closed" && phase != "native";
            Assert.Equal(closedHere ? 1 : 0, native.Closes);
            Assert.Equal(closedHere && async ? 1 : 0, native.AsyncCloses);

            native.Failure = null;
            callback.Failure = null;
            if (async) await connection.OpenAsync(CancellationToken.None);
            else connection.Open();
            Assert.Equal(initial == "outer" ? 2 : 1, OpenCount(connection));
            if (async) await connection.CloseAsync();
            else connection.Close();
            Assert.Equal(initial == "outer" ? 1 : 0, OpenCount(connection));
            Assert.Equal(initial == "closed" ? ConnectionState.Closed : ConnectionState.Open, native.State);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Cleanup_failure_retains_opening_cancellation_and_still_restores_accounting(bool async)
        {
            await using var context = new DbContext(new DbContextOptionsBuilder()
                .UseOceanBase("Server=localhost;Database=test", OceanBaseCompatibilityMode.MySql).Options);
            var failure = new InvalidOperationException("test close failure");
            var native = new PhysicalConnection { CloseFailure = failure };
            await using var connection = new PhysicalRelationalConnection(context.GetService<RelationalConnectionDependencies>(), native);
            var entry = new OceanBaseConnectionOpenState(connection);
            if (async) await connection.OpenAsync(CancellationToken.None);
            else connection.Open();
            Assert.Equal(1, OpenCount(connection));
            Assert.True(OpenedInternally(connection));
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            var openingError = new OperationCanceledException(cancelled.Token);
            if (async) await entry.RestoreAsync(openingError);
            else entry.Restore(openingError);
            Assert.Same(failure, openingError.Data["OceanBase.OpenCleanupError"]);
            Assert.Equal(cancelled.Token, openingError.CancellationToken);
            Assert.Equal(0, OpenCount(connection));
            Assert.False(OpenedInternally(connection));
            Assert.Equal(ConnectionState.Open, native.State);
            native.CloseFailure = null;
            native.Close();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Failed_open_cleanup_targets_the_captured_connection_not_a_callback_replacement(bool async)
        {
            await using var context = new DbContext(new DbContextOptionsBuilder()
                .UseOceanBase("Server=localhost;Database=test", OceanBaseCompatibilityMode.MySql).Options);
            var original = new PhysicalConnection();
            var replacement = new PhysicalConnection();
            await using var connection = new PhysicalRelationalConnection(context.GetService<RelationalConnectionDependencies>(), original);
            connection.SetDbConnection(original, contextOwnsConnection: false);
            var entry = new OceanBaseConnectionOpenState(connection);
            original.Open();
            replacement.Open();
            connection.SetDbConnection(replacement, contextOwnsConnection: false);
            var error = new InvalidOperationException("test callback replacement");
            if (async) await entry.RestoreAsync(error);
            else entry.Restore(error);
            Assert.Equal(ConnectionState.Closed, original.State);
            Assert.Equal(1, original.Closes);
            Assert.Equal(ConnectionState.Open, replacement.State);
            Assert.Equal(0, replacement.Closes);
            Assert.Equal(0, OpenCount(connection));
            Assert.False(OpenedInternally(connection));
            replacement.Close();
        }

        // Inspect EF's accounting because a suppressed native open intentionally leaves State == Closed.
        private static int OpenCount(IRelationalConnection connection)
            => (int)typeof(RelationalConnection).GetField("_openedCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(connection)!;

        private static bool OpenedInternally(IRelationalConnection connection)
            => (bool)typeof(RelationalConnection).GetField("_openedInternally", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(connection)!;

        private sealed class SuppressNetwork : DbConnectionInterceptor
        {
            public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
                => InterceptionResult.Suppress();

            public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
                InterceptionResult result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(InterceptionResult.Suppress());
        }

        private sealed class OpenedFailure : DbConnectionInterceptor
        {
            public Exception? Failure { get; set; }

            public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
            {
                if (Failure is not null) throw Failure;
            }

            public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData,
                CancellationToken cancellationToken = default)
            {
                ConnectionOpened(connection, eventData);
                return Task.CompletedTask;
            }
        }

        private sealed class PhysicalRelationalConnection(RelationalConnectionDependencies dependencies, PhysicalConnection native)
            : RelationalConnection(dependencies)
        {
            protected override DbConnection CreateDbConnection() => native;
        }

        private sealed class PhysicalConnection : DbConnection
        {
            private ConnectionState _state;
            public Exception? Failure { get; set; }
            public Exception? CloseFailure { get; set; }
            public int Closes { get; private set; }
            public int AsyncCloses { get; private set; }
            [AllowNull]
            public override string ConnectionString { get; set; } = "";
            public override string Database => "test";
            public override string DataSource => "offline";
            public override string ServerVersion => "test";
            public override ConnectionState State => _state;

            public override void Open()
            {
                if (Failure is not null) throw Failure;
                _state = ConnectionState.Open;
            }

            public override Task OpenAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Open();
                return Task.CompletedTask;
            }

            public override void Close()
            {
                Closes++;
                if (CloseFailure is not null) throw CloseFailure;
                _state = ConnectionState.Closed;
            }

            public override Task CloseAsync()
            {
                AsyncCloses++;
                Close();
                return Task.CompletedTask;
            }

            public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
            protected override DbTransaction BeginDbTransaction(System.Data.IsolationLevel isolationLevel) => throw new NotSupportedException();
            protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
        }
    }
}
