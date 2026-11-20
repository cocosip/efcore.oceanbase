using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.FunctionalTests
{
    public sealed class OracleUserManagementTests
    {
        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public async Task EnsureCreated_creates_user_grants_minimum_privileges_and_creates_tables_with_redacted_logs(bool async, bool sensitive)
        {
            var capture = new Capture();
            var messages = new List<string>();
            var passwordCalls = 0;
            const string password = "synthetic ' ; -- @p0 secret";
            await using var context = Create(capture, () => { passwordCalls++; return password; }, builder =>
            {
                builder.LogTo(messages.Add, LogLevel.Information);
                if (sensitive) builder.EnableSensitiveDataLogging();
            });
            context.Database.SetCommandTimeout(27);
            Assert.True(async ? await context.Database.EnsureCreatedAsync() : context.Database.EnsureCreated());
            Assert.Equal(1, passwordCalls);
            var statements = capture.Commands.Select(c => c.Sql).ToArray();
            Assert.StartsWith("SELECT USERNAME FROM SYS.DBA_USERS WHERE USERNAME = :", statements[0]);
            Assert.Equal(new object?[] { "owned" }, capture.Commands[0].Values);
            Assert.Equal("CREATE USER \"owned\" IDENTIFIED BY <redacted>", statements[1]);
            Assert.Equal("GRANT CREATE SESSION, CREATE TABLE TO \"owned\"", statements[2]);
            Assert.Contains(statements, statement => statement.StartsWith("CREATE TABLE", StringComparison.Ordinal));
            Assert.Contains("IDENTIFIED BY <redacted>", string.Join("\n", messages));
            Assert.DoesNotContain(password, string.Join("\n", messages));
            Assert.All(capture.Commands, command => { Assert.Equal(27, command.Timeout); Assert.Equal(async, command.Async); });
            var management = Assert.Single(capture.Disposals);
            Assert.Same(management, capture.Commands[1].Connection);
            Assert.NotSame(management, context.Database.GetDbConnection());
            using var targetCommand = context.Database.GetDbConnection().CreateCommand();
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task Credential_reaches_only_the_executing_command_and_is_cleared_after_execution(bool async, bool fail)
        {
            const string password = "synthetic-private-password";
            const string redacted = "CREATE USER \"owned\" IDENTIFIED BY <redacted>";
            var failure = fail ? new InvalidOperationException("synthetic execution failure") : null;
            var capture = new Capture { ExecuteCredentialCommand = true, ExecutionFailure = failure };
            var messages = new List<string>();
            await using var context = Create(capture, () => password, builder =>
            {
                builder.EnableSensitiveDataLogging().LogTo(messages.Add, LogLevel.Information);
            });
            var creator = context.GetService<IRelationalDatabaseCreator>();
            if (failure is not null)
            {
                if (async) Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => creator.CreateAsync()));
                else Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => creator.Create()));
                Assert.Single(capture.Commands);
            }
            else if (async) await creator.CreateAsync();
            else creator.Create();

            var native = Assert.IsType<CredentialCommand>(capture.NativeCommand);
            Assert.Equal("CREATE USER \"owned\" IDENTIFIED BY \"" + password + "\"", native.ExecutedSql);
            Assert.Equal(redacted, native.CommandText);
            Assert.True(native.WasDisposed);
            Assert.Equal(redacted, capture.Commands[0].Sql);
            Assert.Contains(redacted, string.Join("\n", messages));
            Assert.DoesNotContain(password, string.Join("\n", messages));
        }

        [Theory]
        [InlineData(false, "/* tag */\n-- request\n{0} /* end */", true, false)]
        [InlineData(true, "/* tag */\n-- request\n{0} /* end */", true, false)]
        [InlineData(false, "{0} DEFAULT TABLESPACE users", true, false)]
        [InlineData(true, "{0} DEFAULT TABLESPACE users", true, false)]
        [InlineData(false, "/* {0} */ ALTER SESSION SET CURRENT_SCHEMA = owned", false, false)]
        [InlineData(true, "-- {0}\nALTER SESSION SET CURRENT_SCHEMA = owned", false, false)]
        [InlineData(false, "SELECT '{0}' FROM DUAL", false, false)]
        [InlineData(true, "{0} /* {0} */", false, false)]
        [InlineData(false, "/* tag */ {0}", false, true)]
        [InlineData(true, "/* tag */ {0}", false, true)]
        public async Task Interception_preserves_sql_edits_and_limits_credential_substitution_to_the_original_statement(
            bool async, string sqlFormat, bool substitute, bool suppress)
        {
            const string password = "synthetic-private-password";
            const string redacted = "CREATE USER \"owned\" IDENTIFIED BY <redacted>";
            const string executable = "CREATE USER \"owned\" IDENTIFIED BY \"" + password + "\"";
            var modifiedSql = string.Format(sqlFormat, redacted);
            var capture = new Capture
            {
                ExecuteCredentialCommand = true,
                EditCredentialCommand = command =>
                {
                    command.CommandText = modifiedSql;
                    return suppress ? InterceptionResult<int>.SuppressWithResult(0) : default;
                }
            };
            var messages = new List<string>();
            await using var context = Create(capture, () => password, builder =>
                builder.EnableSensitiveDataLogging().LogTo(messages.Add, LogLevel.Information));
            var creator = context.GetService<IRelationalDatabaseCreator>();

            if (async) await creator.CreateAsync();
            else creator.Create();

            var native = Assert.IsType<CredentialCommand>(capture.NativeCommand);
            var expectedSql = substitute ? string.Format(sqlFormat, executable) : modifiedSql;
            Assert.Equal(suppress ? null : expectedSql, native.ExecutedSql);
            Assert.Equal(suppress ? redacted : modifiedSql, native.CommandText);
            Assert.True(native.WasDisposed);
            Assert.DoesNotContain(password, string.Join("\n", messages));
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public async Task EnsureDeleted_uses_authoritative_catalog_and_never_requests_a_password(bool async, bool exists)
        {
            var capture = new Capture { Exists = exists };
            await using var context = Create(capture, () => throw new InvalidOperationException("Must not request credentials."));
            Assert.Equal(exists, async ? await context.Database.EnsureDeletedAsync() : context.Database.EnsureDeleted());
            Assert.Equal(exists ? 2 : 1, capture.Commands.Count);
            if (exists) Assert.Equal("DROP USER \"owned\" CASCADE", capture.Commands[1].Sql);
            Assert.Single(capture.Disposals);
            Assert.False(async ? await context.Database.EnsureDeletedAsync() : context.Database.EnsureDeleted());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Existing_populated_user_skips_password_grants_and_table_creation(bool async)
        {
            var capture = new Capture { Exists = true, HasTables = true };
            await using var context = Create(capture, () => throw new InvalidOperationException("Must not change an existing password."));
            Assert.False(async ? await context.Database.EnsureCreatedAsync() : context.Database.EnsureCreated());
            Assert.DoesNotContain(capture.Commands, c => c.Sql.StartsWith("CREATE", StringComparison.Ordinal) || c.Sql.StartsWith("GRANT", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData(false, "")]
        [InlineData(true, "")]
        [InlineData(false, "a\"b")]
        [InlineData(true, "a\"b")]
        [InlineData(false, "a\nb")]
        [InlineData(true, "a\0b")]
        public async Task Unestablished_password_encodings_reject_before_ddl_and_dispose_management(bool async, string password)
        {
            var capture = new Capture();
            await using var context = Create(capture, () => password);
            if (async) await Assert.ThrowsAsync<ArgumentException>(() => context.Database.EnsureCreatedAsync());
            else Assert.Throws<ArgumentException>(() => context.Database.EnsureCreated());
            Assert.Single(capture.Commands);
            Assert.Single(capture.Disposals);
        }

        [Theory]
        [InlineData(false, "SELECT")]
        [InlineData(true, "SELECT")]
        [InlineData(false, "CREATE USER")]
        [InlineData(true, "CREATE USER")]
        [InlineData(false, "GRANT")]
        [InlineData(true, "GRANT")]
        [InlineData(false, "DROP USER")]
        [InlineData(true, "DROP USER")]
        public async Task Failures_preserve_identity_and_partial_ddl_without_automatic_rollback(bool async, string prefix)
        {
            var failure = new InvalidOperationException("synthetic operation failure");
            var capture = new Capture { Exists = prefix == "DROP USER", FailPrefix = prefix, Failure = failure };
            await using var context = Create(capture, () => "synthetic");
            if (async) Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => prefix == "DROP USER"
                ? context.Database.EnsureDeletedAsync() : context.Database.EnsureCreatedAsync()));
            else Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => prefix == "DROP USER"
                ? context.Database.EnsureDeleted() : context.Database.EnsureCreated()));
            Assert.Equal(prefix == "GRANT" || prefix == "DROP USER", capture.Exists);
            Assert.Single(capture.Disposals);
            Assert.DoesNotContain(capture.Commands, c => c.Sql.StartsWith("DROP", StringComparison.Ordinal) && prefix != "DROP USER");
        }

        [Theory]
        [InlineData(false, "login")]
        [InlineData(true, "login")]
        [InlineData(false, "schema")]
        [InlineData(true, "schema")]
        [InlineData(false, "mode")]
        [InlineData(true, "mode")]
        [InlineData(false, "factory")]
        [InlineData(true, "factory")]
        [InlineData(false, "model")]
        [InlineData(true, "model")]
        public async Task Invalid_target_or_table_plans_reject_before_administration_factory(bool async, string profile)
        {
            var capture = new Capture();
            var calls = 0;
            await using var context = Create(capture, () => "synthetic", builder =>
            {
                if (profile == "login") builder.UseOceanBase("Server=offline;User Id=other@tenant", OceanBaseCompatibilityMode.Oracle);
                if (profile == "schema") builder.UseOceanBase("Server=offline;User Id=owned@tenant;Database=other", OceanBaseCompatibilityMode.Oracle);
                if (profile == "mode") builder.UseOceanBase("Server=offline;User Id=owned;Database=owned", OceanBaseCompatibilityMode.MySql);
                builder.UseOceanBaseManagementConnection(() => { calls++; return new OceanBaseConnection("Server=offline;User Id=administrator@tenant"); });
                if (profile == "factory") builder.UseOceanBaseManagementConnection(() => { calls++; return null!; });
            }, invalidModel: profile == "model");
            var expected = profile is "model" or "mode" ? typeof(NotSupportedException) : typeof(InvalidOperationException);
            if (async) await Assert.ThrowsAsync(expected, () => context.Database.EnsureCreatedAsync());
            else Assert.Throws(expected, () => context.Database.EnsureCreated());
            Assert.Equal(profile == "factory" ? 1 : 0, calls);
            Assert.Empty(capture.Commands);
            Assert.Empty(capture.Opens);
        }

        [Fact]
        public async Task Precancelled_operations_do_not_invoke_either_factory()
        {
            var capture = new Capture();
            await using var context = Create(capture, () => throw new InvalidOperationException("Must not request password."),
                builder => builder.UseOceanBaseManagementConnection(() => throw new InvalidOperationException("Must not request admin.")));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.Database.EnsureCreatedAsync(cancellation.Token));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.Database.EnsureDeletedAsync(cancellation.Token));
            Assert.Empty(capture.Opens);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Oracle_deletion_never_treats_mysql_error_codes_as_proof_of_absence(bool async)
        {
            var failure = (OceanBaseException)Activator.CreateInstance(typeof(OceanBaseException),
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null,
                ["synthetic namespace error", 1008, "42000", false, null, null], null)!;
            var capture = new Capture { Exists = true, FailPrefix = "DROP USER", Failure = failure };
            await using var context = Create(capture, () => throw new InvalidOperationException("Must not request password."));
            if (async) Assert.Same(failure, await Assert.ThrowsAsync<OceanBaseException>(() => context.Database.EnsureDeletedAsync()));
            else Assert.Same(failure, Assert.Throws<OceanBaseException>(() => context.Database.EnsureDeleted()));
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public async Task Credential_ddl_failure_logs_redact_password_and_preserve_primary_error_under_disposal_failure(bool async, bool sensitive)
        {
            var messages = new List<string>();
            var primary = new InvalidOperationException("synthetic credential DDL failure");
            var cleanup = new InvalidOperationException("synthetic cleanup failure");
            var capture = new Capture { FailPrefix = "CREATE USER", Failure = primary, CleanupFailure = cleanup };
            await using var context = Create(capture, () => "synthetic-private-password", builder =>
            {
                builder.LogTo(messages.Add, LogLevel.Information);
                if (sensitive) builder.EnableSensitiveDataLogging();
            });
            if (async) Assert.Same(primary, await Assert.ThrowsAsync<InvalidOperationException>(() => context.Database.EnsureCreatedAsync()));
            else Assert.Same(primary, Assert.Throws<InvalidOperationException>(() => context.Database.EnsureCreated()));
            Assert.Same(cleanup, primary.Data["OceanBase.DatabaseManagementCleanupError"]);
            Assert.Contains("Failed executing DbCommand", string.Join("\n", messages));
            Assert.Contains("IDENTIFIED BY <redacted>", string.Join("\n", messages));
            Assert.DoesNotContain("synthetic-private-password", string.Join("\n", messages));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Quoted_user_names_are_bound_for_catalog_and_delimited_for_ddl(bool async)
        {
            const string scope = "own\"ed";
            var capture = new Capture { Namespace = scope };
            await using var context = Create(capture, () => "synthetic", scope: scope);
            var creator = context.GetService<IRelationalDatabaseCreator>();
            if (async) await creator.CreateAsync(); else creator.Create();
            Assert.Equal("CREATE USER \"own\"\"ed\" IDENTIFIED BY <redacted>", capture.Commands[0].Sql);
            Assert.Equal("GRANT CREATE SESSION, CREATE TABLE TO \"own\"\"ed\"", capture.Commands[1].Sql);
            Assert.True(async ? await creator.EnsureDeletedAsync() : creator.EnsureDeleted());
            Assert.Equal(new object?[] { scope }, capture.Commands[2].Values);
            Assert.Equal("DROP USER \"own\"\"ed\" CASCADE", capture.Commands[3].Sql);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task A_catalog_mismatch_is_an_error_and_does_not_authorize_ddl(bool async)
        {
            var capture = new Capture { Exists = true, Namespace = "OWNED" };
            await using var context = Create(capture, () => throw new InvalidOperationException("Must not request password."));
            if (async) await Assert.ThrowsAsync<InvalidOperationException>(() => context.Database.EnsureDeletedAsync());
            else Assert.Throws<InvalidOperationException>(() => context.Database.EnsureDeleted());
            Assert.Single(capture.Commands);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Explicit_management_factory_is_required_for_oracle_namespaces(bool async)
        {
            var capture = new Capture();
            await using var context = Create(capture, () => "synthetic", managementFactory: false);
            if (async) await Assert.ThrowsAsync<NotSupportedException>(() => context.Database.EnsureDeletedAsync());
            else Assert.Throws<NotSupportedException>(() => context.Database.EnsureDeleted());
            Assert.Empty(capture.Opens);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task The_owned_user_cannot_be_the_administration_login(bool async)
        {
            var capture = new Capture();
            using var candidate = new OceanBaseConnection("Server=offline;User Id=owned@tenant");
            await using var context = Create(capture, () => "synthetic", builder => builder.UseOceanBaseManagementConnection(() => candidate));
            if (async) await Assert.ThrowsAsync<InvalidOperationException>(() => context.Database.EnsureDeletedAsync());
            else Assert.Throws<InvalidOperationException>(() => context.Database.EnsureDeleted());
            Assert.Empty(capture.Opens);
            Assert.Throws<ObjectDisposedException>(() => candidate.CreateCommand());
        }

        [Fact]
        public async Task Inflight_cancellation_preserves_token_and_disposes_management_without_followup_grants()
        {
            using var cancellation = new CancellationTokenSource();
            var failure = new OperationCanceledException(cancellation.Token);
            var capture = new Capture { FailPrefix = "CREATE USER", Failure = failure, BeforeFailure = cancellation.Cancel };
            await using var context = Create(capture, () => "synthetic");
            var actual = await Assert.ThrowsAsync<OperationCanceledException>(() => context.Database.EnsureCreatedAsync(cancellation.Token));
            Assert.Same(failure, actual);
            Assert.Equal(cancellation.Token, actual.CancellationToken);
            Assert.Equal(cancellation.Token, capture.LastToken);
            Assert.Single(capture.Disposals);
            Assert.DoesNotContain(capture.Commands, c => c.Sql.StartsWith("GRANT", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Existing_empty_users_create_tables_and_supplied_targets_survive_context_disposal(bool async)
        {
            var capture = new Capture { Exists = true };
            using var target = new OceanBaseConnection("Server=offline;User Id=owned@tenant;Database=owned");
            await using (var context = Create(capture, () => throw new InvalidOperationException("Must not change password."),
                builder => builder.UseOceanBase(target, OceanBaseCompatibilityMode.Oracle)))
            {
                Assert.True(async ? await context.Database.EnsureCreatedAsync() : context.Database.EnsureCreated());
                Assert.Contains(capture.Commands, c => c.Sql.StartsWith("CREATE TABLE", StringComparison.Ordinal));
                Assert.DoesNotContain(capture.Commands, c => c.Sql.StartsWith("CREATE USER", StringComparison.Ordinal));
            }
            using var stillUsable = target.CreateCommand();
            Assert.DoesNotContain(target, capture.Disposals);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Caller_transactions_reject_before_requesting_administration(bool async)
        {
            var capture = new Capture();
            var calls = 0;
            await using var context = Create(capture, () => "synthetic", builder => builder.UseOceanBaseManagementConnection(() =>
            { calls++; return new OceanBaseConnection("Server=offline;User Id=administrator@tenant"); }));
            using var native = new BorrowedTransaction(context.Database.GetDbConnection());
            using var transaction = context.Database.UseTransaction(native);
            var opens = capture.Opens.Count;
            if (async) await Assert.ThrowsAsync<NotSupportedException>(() => context.Database.EnsureCreatedAsync());
            else Assert.Throws<NotSupportedException>(() => context.Database.EnsureCreated());
            Assert.Equal(0, calls);
            Assert.Empty(capture.Commands);
            Assert.Equal(opens, capture.Opens.Count);
            Assert.Same(transaction, context.Database.CurrentTransaction);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Target_schema_is_rechecked_after_user_creation_before_any_table_ddl(bool async)
        {
            var capture = new Capture { CurrentScope = "other" };
            await using var context = Create(capture, () => "synthetic");
            if (async) await Assert.ThrowsAsync<InvalidOperationException>(() => context.Database.EnsureCreatedAsync());
            else Assert.Throws<InvalidOperationException>(() => context.Database.EnsureCreated());
            Assert.True(capture.Exists);
            Assert.Contains(capture.Commands, c => c.Sql.StartsWith("GRANT", StringComparison.Ordinal));
            Assert.DoesNotContain(capture.Commands, c => c.Sql.StartsWith("CREATE TABLE", StringComparison.Ordinal));
            Assert.DoesNotContain(capture.Commands, c => c.Sql.StartsWith("DROP", StringComparison.Ordinal));
        }

        private sealed class BorrowedTransaction(DbConnection connection) : DbTransaction
        {
            protected override DbConnection DbConnection => connection;
            public override System.Data.IsolationLevel IsolationLevel => System.Data.IsolationLevel.ReadCommitted;
            public override void Commit() => throw new InvalidOperationException("Must not commit caller transaction.");
            public override void Rollback() => throw new InvalidOperationException("Must not roll back caller transaction.");
        }

        private static TestContext Create(Capture capture, Func<string> password,
            Action<DbContextOptionsBuilder<TestContext>>? configure = null, bool invalidModel = false,
            string scope = "owned", bool managementFactory = true)
        {
            var target = new OceanBaseConnectionStringBuilder { Server = "offline", UserId = scope + "@tenant" };
            var builder = new DbContextOptionsBuilder<TestContext>()
                .UseOceanBase(target.ConnectionString, OceanBaseCompatibilityMode.Oracle)
                .EnableOceanBaseUserManagement(scope, password)
                .AddInterceptors(capture).ReplaceService<IModelCacheKeyFactory, ShapeKey>()
                .EnableServiceProviderCaching(false);
            if (managementFactory) builder.UseOceanBaseManagementConnection(() => new OceanBaseConnection("Server=offline;User Id=administrator@tenant"));
            configure?.Invoke(builder);
            return new TestContext(builder.Options, invalidModel);
        }

        private sealed class TestContext(DbContextOptions<TestContext> options, bool invalidModel) : DbContext(options)
        {
            public bool InvalidModel => invalidModel;
            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity<Row>().ToTable("Rows", invalidModel ? "outside" : null);
            }
        }
        private sealed class Row { public int Id { get; set; } }
        private sealed class ShapeKey : IModelCacheKeyFactory
        {
            public object Create(DbContext context, bool designTime) => (((TestContext)context).InvalidModel, designTime);
        }
        private sealed record Command(string Sql, DbConnection Connection, bool Async, int Timeout, object?[] Values);

        private sealed class CredentialCommand(DbCommand command, Exception? failure) : DbCommand
        {
            public string? ExecutedSql { get; private set; }
            public bool WasDisposed { get; private set; }
            [AllowNull] public override string CommandText { get => command.CommandText; set => command.CommandText = value; }
            public override int CommandTimeout { get => command.CommandTimeout; set => command.CommandTimeout = value; }
            public override CommandType CommandType { get => command.CommandType; set => command.CommandType = value; }
            public override bool DesignTimeVisible { get => command.DesignTimeVisible; set => command.DesignTimeVisible = value; }
            public override UpdateRowSource UpdatedRowSource { get => command.UpdatedRowSource; set => command.UpdatedRowSource = value; }
            protected override DbConnection? DbConnection { get => command.Connection; set => command.Connection = value; }
            protected override DbTransaction? DbTransaction { get => command.Transaction; set => command.Transaction = value; }
            protected override DbParameterCollection DbParameterCollection => command.Parameters;
            protected override DbParameter CreateDbParameter() => command.CreateParameter();
            public override void Cancel() => throw new NotSupportedException();
            public override void Prepare() => throw new NotSupportedException();
            public override object ExecuteScalar() => throw new NotSupportedException();
            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();

            public override int ExecuteNonQuery()
            {
                ExecutedSql = CommandText;
                if (failure is not null) throw failure;
                return 0;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    command.Dispose();
                    WasDisposed = true;
                }
                base.Dispose(disposing);
            }
        }

        private sealed class Capture : DbConnectionInterceptor, IDbCommandInterceptor
        {
            public List<DbConnection> Opens { get; } = [];
            public List<DbConnection> Disposals { get; } = [];
            public List<Command> Commands { get; } = [];
            public bool Exists { get; set; }
            public bool HasTables { get; init; }
            public string Namespace { get; init; } = "owned";
            public string CurrentScope { get; init; } = "owned";
            public string? FailPrefix { get; init; }
            public Exception? Failure { get; init; }
            public Exception? CleanupFailure { get; set; }
            public bool ExecuteCredentialCommand { get; init; }
            public Func<DbCommand, InterceptionResult<int>>? EditCredentialCommand { get; init; }
            public Exception? ExecutionFailure { get; init; }
            public DbCommand? NativeCommand { get; private set; }
            public Action? BeforeFailure { get; init; }
            public CancellationToken LastToken { get; private set; }
            public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
            { Opens.Add(connection); return InterceptionResult.Suppress(); }
            public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
                InterceptionResult result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(ConnectionOpening(connection, eventData, result));
            public override void ConnectionDisposed(DbConnection connection, ConnectionEndEventData eventData)
            {
                Disposals.Add(connection);
                if (CleanupFailure is { } failure) { CleanupFailure = null; throw failure; }
            }
            public override Task ConnectionDisposedAsync(DbConnection connection, ConnectionEndEventData eventData)
            { ConnectionDisposed(connection, eventData); return Task.CompletedTask; }
            public InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
                => InterceptionResult<object>.SuppressWithResult(Scalar(command, eventData, false));
            public ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData,
                InterceptionResult<object> result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(InterceptionResult<object>.SuppressWithResult(Scalar(command, eventData, true, cancellationToken)));
            private object Scalar(DbCommand command, CommandEventData eventData, bool async, CancellationToken token = default)
            {
                Record(command, eventData, async, token);
                if (command.CommandText.StartsWith("SELECT USERNAME", StringComparison.Ordinal)) return Exists ? Namespace : DBNull.Value;
                if (command.CommandText.StartsWith("SELECT COUNT", StringComparison.Ordinal)) return HasTables ? 1 : 0;
                return CurrentScope;
            }
            public InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
                => Execute(command, eventData, false);
            public ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
                InterceptionResult<int> result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(Execute(command, eventData, true, cancellationToken));
            public DbCommand CommandInitialized(CommandEndEventData eventData, DbCommand command)
            {
                if (!ExecuteCredentialCommand || !command.CommandText.StartsWith("CREATE USER", StringComparison.Ordinal))
                    return command;
                return NativeCommand = new CredentialCommand(command, ExecutionFailure);
            }
            private InterceptionResult<int> Execute(DbCommand command, CommandEventData eventData, bool async, CancellationToken token = default)
            {
                Record(command, eventData, async, token);
                if (ExecuteCredentialCommand && command.CommandText.StartsWith("CREATE USER", StringComparison.Ordinal))
                    return EditCredentialCommand?.Invoke(command) ?? default;
                if (command.CommandText.StartsWith("CREATE USER", StringComparison.Ordinal)) Exists = true;
                if (command.CommandText.StartsWith("DROP USER", StringComparison.Ordinal)) Exists = false;
                return InterceptionResult<int>.SuppressWithResult(0);
            }
            private void Record(DbCommand command, CommandEventData eventData, bool async, CancellationToken token = default)
            {
                LastToken = token;
                Commands.Add(new Command(command.CommandText, command.Connection!, async, command.CommandTimeout,
                    command.Parameters.Cast<DbParameter>().Select(p => p.Value).ToArray()));
                if (FailPrefix is not null && command.CommandText.StartsWith(FailPrefix, StringComparison.Ordinal))
                { BeforeFailure?.Invoke(); throw Failure!; }
            }
        }
    }
}
