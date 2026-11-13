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
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace OceanBase.EntityFrameworkCore.FunctionalTests
{
    public sealed class MigrationsTests
    {
        public const string Initial = "20261113090000_Initial";
        public const string Expand = "20261113090100_Expand";

        public static IEnumerable<object[]> Modes()
        {
            foreach (var mode in Enum.GetValues<OceanBaseCompatibilityMode>())
                foreach (var async in new[] { false, true }) yield return new object[] { mode, async };
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Forward_reverse_and_idempotent_scripts_use_the_real_EF_migration_pipeline(OceanBaseCompatibilityMode mode)
        {
            var capture = new Capture();
            using var context = Create(mode, capture);
            var migrator = context.GetService<IMigrator>();
            Assert.Equal(new[] { Initial, Expand }, context.Database.GetMigrations());
            var forward = migrator.GenerateScript();
            Assert.Contains("__EFMigrationsHistory", forward);
            Assert.Contains("CREATE TABLE", forward);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "MODIFY COLUMN" : "MODIFY (", forward);
            Assert.Contains(Expand, forward);
            Assert.DoesNotContain("START TRANSACTION", forward);
            var reverse = migrator.GenerateScript(Expand, Migration.InitialDatabase);
            Assert.Contains("DROP TABLE", reverse);
            Assert.Contains("DELETE FROM", reverse);
            var idempotent = migrator.GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "PREPARE" : "EXECUTE IMMEDIATE", idempotent);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "@ob_migration_apply" : "IF ob_count = 0 THEN", idempotent);
            var down = migrator.GenerateScript(Expand, Migration.InitialDatabase, MigrationsSqlGenerationOptions.Idempotent);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "@ob_migration_apply" : "IF ob_count > 0 THEN", down);
            Assert.Empty(capture.Commands);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Migrate_applies_once_and_reverts_with_owned_lock_and_history(OceanBaseCompatibilityMode mode, bool async)
        {
            var capture = new Capture();
            await using var context = Create(mode, capture);
            if (async) await context.Database.MigrateAsync(); else context.Database.Migrate();
            Assert.Equal(new[] { Initial, Expand }, capture.Applied.OrderBy(id => id));
            Assert.False(capture.LockHeld);
            Assert.Contains(capture.Commands, c => c.Sql.StartsWith("CREATE TABLE", StringComparison.Ordinal) && c.Sql.Contains("Rows", StringComparison.Ordinal));
            Assert.All(capture.Commands, c => Assert.Equal(async, c.Async));
            if (mode == OceanBaseCompatibilityMode.Oracle)
                Assert.All(capture.Commands.Where(c => !c.Sql.StartsWith("BEGIN", StringComparison.Ordinal)),
                    c => Assert.False(c.Sql.TrimEnd().EndsWith(';'), c.Sql));
            var count = capture.Commands.Count;
            if (async) await context.Database.MigrateAsync(); else context.Database.Migrate();
            Assert.DoesNotContain(capture.Commands.Skip(count), c => c.Sql.Contains("ALTER TABLE", StringComparison.Ordinal));
            var migrator = context.GetService<IMigrator>();
            if (async) await migrator.MigrateAsync(Migration.InitialDatabase); else migrator.Migrate(Migration.InitialDatabase);
            Assert.Empty(capture.Applied);
            Assert.False(capture.LockHeld);
            Assert.Null(context.Database.CurrentTransaction);
            Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Migration_failure_and_cancellation_do_not_record_success_or_leave_a_lock(OceanBaseCompatibilityMode mode, bool async)
        {
            using var source = new CancellationTokenSource();
            Exception failure = async ? new OperationCanceledException(source.Token) : new InvalidOperationException("synthetic alter failure");
            var capture = new Capture { Failure = failure };
            await using var context = Create(mode, capture);
            var actual = async ? await Assert.ThrowsAnyAsync<Exception>(() => context.Database.MigrateAsync(source.Token))
                : Assert.ThrowsAny<Exception>(() => context.Database.Migrate());
            Assert.Same(failure, actual);
            Assert.Equal(new[] { Initial }, capture.Applied);
            Assert.False(capture.LockHeld);
            Assert.Null(context.Database.CurrentTransaction);
            Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Missing_opt_in_wrong_namespace_or_busy_lock_never_executes_migration_DDL(OceanBaseCompatibilityMode mode, bool async)
        {
            foreach (var boundary in new[] { "no-opt-in", "wrong-scope", "busy" })
            {
                var capture = new Capture { Scope = boundary == "wrong-scope" ? "other" : "offline", LockHeld = boundary == "busy" };
                await using var context = Create(mode, capture, optIn: boundary != "no-opt-in");
                if (async) await Assert.ThrowsAnyAsync<Exception>(() => context.Database.MigrateAsync());
                else Assert.ThrowsAny<Exception>(() => context.Database.Migrate());
                Assert.DoesNotContain(capture.Commands, c => c.Sql.Contains("CREATE TABLE", StringComparison.Ordinal) && c.Sql.Contains("Rows", StringComparison.Ordinal));
                Assert.Empty(capture.Applied);
                Assert.Equal(boundary == "busy", capture.LockHeld);
            }
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Caller_open_connection_survives_and_ambient_transactions_reject_before_DDL(OceanBaseCompatibilityMode mode, bool async)
        {
            var capture = new Capture();
            await using var context = Create(mode, capture);
            if (async) await context.Database.OpenConnectionAsync(); else context.Database.OpenConnection();
            if (async) await context.Database.MigrateAsync(); else context.Database.Migrate();
            Assert.Equal(ConnectionState.Open, context.Database.GetDbConnection().State);
            capture.Commands.Clear();
            using (var transaction = new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
            {
                if (async) await Assert.ThrowsAsync<NotSupportedException>(() => context.Database.MigrateAsync());
                else Assert.Throws<NotSupportedException>(() => context.Database.Migrate());
            }
            Assert.Empty(capture.Commands);
            if (async) await context.Database.CloseConnectionAsync(); else context.Database.CloseConnection();
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Caller_transaction_and_wrong_seeder_reject_before_any_command(OceanBaseCompatibilityMode mode, bool async)
        {
            var capture = new Capture();
            await using (var context = Create(mode, capture))
            {
                await using var transaction = await context.Database.BeginTransactionAsync();
                if (async) await Assert.ThrowsAsync<NotSupportedException>(() => context.Database.MigrateAsync());
                else Assert.Throws<NotSupportedException>(() => context.Database.Migrate());
                Assert.Same(transaction, context.Database.CurrentTransaction);
                Assert.Empty(capture.Commands);
            }
            await using (var context = Create(mode, capture, configure: builder =>
            {
                if (async) builder.UseSeeding((_, _) => { });
                else builder.UseAsyncSeeding((_, _, _) => Task.CompletedTask);
            }))
            {
                if (async) await Assert.ThrowsAsync<InvalidOperationException>(() => context.Database.MigrateAsync());
                else Assert.Throws<InvalidOperationException>(() => context.Database.Migrate());
                Assert.Empty(capture.Commands);
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public async Task Pre_cancelled_migration_does_not_open_or_claim_a_lock(OceanBaseCompatibilityMode mode)
        {
            var capture = new Capture();
            await using var context = Create(mode, capture);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.Database.MigrateAsync(cancellation.Token));
            Assert.Empty(capture.Commands);
            Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Bootstrap_failure_releases_lock_and_release_failure_preserves_original_error(OceanBaseCompatibilityMode mode, bool async)
        {
            foreach (var bootstrap in new[] { true, false })
            {
                var error = new InvalidOperationException("synthetic primary failure");
                var cleanup = new InvalidOperationException("synthetic unlock failure");
                var capture = new Capture
                {
                    BootstrapFailure = bootstrap ? error : null,
                    Failure = bootstrap ? null : error,
                    ReleaseFailure = bootstrap ? null : cleanup
                };
                await using var context = Create(mode, capture);
                var actual = async ? await Assert.ThrowsAnyAsync<Exception>(() => context.Database.MigrateAsync())
                    : Assert.ThrowsAny<Exception>(() => context.Database.Migrate());
                Assert.Same(error, actual);
                Assert.Equal(!bootstrap, capture.LockHeld);
                if (!bootstrap) Assert.Same(cleanup, error.Data["OceanBase.MigrationCleanupError"]);
                Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
            }
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Uncertain_lock_claim_is_cleaned_up_using_only_its_owner_token(OceanBaseCompatibilityMode mode, bool async)
        {
            var error = new InvalidOperationException("claim committed but response lost");
            var capture = new Capture { ClaimFailure = error };
            await using var context = Create(mode, capture);
            var actual = async ? await Assert.ThrowsAsync<InvalidOperationException>(() => context.Database.MigrateAsync())
                : Assert.Throws<InvalidOperationException>(() => context.Database.Migrate());
            Assert.Same(error, actual);
            Assert.False(capture.LockHeld);
            Assert.Empty(capture.Applied);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Automatic_retries_reject_before_nontransactional_migration_work(OceanBaseCompatibilityMode mode, bool async)
        {
            var capture = new Capture();
            await using var context = Create(mode, capture, configure: builder => builder
                .ReplaceService<IExecutionStrategyFactory, RetryingFactory>());
            if (async) await Assert.ThrowsAsync<NotSupportedException>(() => context.Database.MigrateAsync());
            else Assert.Throws<NotSupportedException>(() => context.Database.Migrate());
            Assert.Empty(capture.Commands);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Matching_seeders_run_under_the_lock_for_changed_and_unchanged_migrations(OceanBaseCompatibilityMode mode, bool async)
        {
            var capture = new Capture();
            var changes = new List<bool>();
            var fail = false;
            var error = new InvalidOperationException("synthetic seeder failure");
            void Seed(DbContext _, bool changed)
            {
                Assert.True(capture.LockHeld);
                changes.Add(changed);
                if (fail) throw error;
            }
            await using var context = Create(mode, capture, configure: builder =>
            {
                if (async) builder.UseAsyncSeeding((db, changed, _) => { Seed(db, changed); return Task.CompletedTask; });
                else builder.UseSeeding(Seed);
            });
            for (var i = 0; i < 2; i++)
                if (async) await context.Database.MigrateAsync(); else context.Database.Migrate();
            Assert.Equal(new[] { true, false }, changes);
            fail = true;
            var actual = async ? await Assert.ThrowsAsync<InvalidOperationException>(() => context.Database.MigrateAsync())
                : Assert.Throws<InvalidOperationException>(() => context.Database.Migrate());
            Assert.Same(error, actual);
            Assert.Equal(new[] { Initial, Expand }, capture.Applied.OrderBy(id => id));
            Assert.False(capture.LockHeld);
            Assert.Null(context.Database.CurrentTransaction);
            Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        }

        [Fact]
        public void Oracle_scripted_raw_blocks_keep_their_terminators_and_reject_oversized_dynamic_commands()
        {
            using var context = new RawMigrationContext(new DbContextOptionsBuilder<RawMigrationContext>()
                .UseOceanBase("Server=localhost;Database=offline", OceanBaseCompatibilityMode.Oracle)
                .EnableServiceProviderCaching(false).Options);
            var migrator = context.GetService<IMigrator>();
            foreach (var options in new[] { MigrationsSqlGenerationOptions.Default, MigrationsSqlGenerationOptions.Idempotent })
            {
                var script = migrator.GenerateScript(toMigration: "20261113100000_Raw", options: options);
                Assert.Contains("-- comment' || CHR(10) || 'BEGIN NULL; END;';", script);
                Assert.Contains("CREATE OR REPLACE TRIGGER", script);
                Assert.Contains("BEGIN NULL; END;';", script);
                Assert.Contains("END;\n/", script);
                Assert.StartsWith("SET DEFINE OFF", script);
                Assert.Contains("WHENEVER SQLERROR EXIT SQL.SQLCODE ROLLBACK", script);
                Assert.EndsWith("COMMIT;\n", script);
                Assert.Throws<NotSupportedException>(() => migrator.GenerateScript(options: options));
            }
        }

        private static MigrationContext Create(OceanBaseCompatibilityMode mode, Capture capture, bool optIn = true,
            Action<DbContextOptionsBuilder<MigrationContext>>? configure = null)
        {
            var options = new DbContextOptionsBuilder<MigrationContext>().UseOceanBase("Server=localhost;Database=offline", mode)
                .EnableServiceProviderCaching(false)
                .ReplaceService<IRelationalConnection, TransactionTests.TestConnection>().AddInterceptors(capture);
            if (optIn) options.EnableOceanBaseSchemaManagement("offline");
            configure?.Invoke(options);
            return new MigrationContext(options.Options);
        }

        public sealed class MigrationContext(DbContextOptions<MigrationContext> options) : DbContext(options)
        {
            protected override void OnModelCreating(ModelBuilder modelBuilder) => ConfigureModel(modelBuilder, evolved: true);
        }

        internal static void ConfigureModel(ModelBuilder builder, bool evolved)
        {
            builder.HasAnnotation("ProductVersion", "10.0.0");
            var row = builder.Entity("MigrationRow");
            row.ToTable("Rows");
            row.Property<int>("Id").ValueGeneratedOnAdd();
            row.HasKey("Id");
            row.Property<string>("Code").HasMaxLength(evolved ? 64 : 32).IsRequired();
        }

        private sealed record Command(string Sql, bool Async);
        public sealed class RetryingFactory(ExecutionStrategyDependencies dependencies) : IExecutionStrategyFactory
        {
            public IExecutionStrategy Create() => new RetryingStrategy(dependencies);
        }
        private sealed class RetryingStrategy(ExecutionStrategyDependencies dependencies)
            : ExecutionStrategy(dependencies, 1, TimeSpan.Zero)
        {
            protected override bool ShouldRetryOn(Exception exception) => true;
        }
        private sealed class Capture : DbCommandInterceptor
        {
            public List<Command> Commands { get; } = new();
            public HashSet<string> Applied { get; } = new();
            public bool LockHeld { get; set; }
            public string Scope { get; init; } = "offline";
            public Exception? Failure { get; init; }
            public Exception? BootstrapFailure { get; init; }
            public Exception? ReleaseFailure { get; init; }
            public Exception? ClaimFailure { get; init; }
            private bool _history;
            private string? _owner;

            private int Execute(DbCommand command, bool async)
            {
                var sql = command.CommandText;
                Commands.Add(new(sql, async));
                if (sql.Contains("__EFMigrationsHistoryLock", StringComparison.Ordinal))
                {
                    if (sql.StartsWith("INSERT", StringComparison.Ordinal))
                    {
                        if (LockHeld) return 0;
                        LockHeld = true;
                        _owner = Regex.Match(sql, @"[a-f0-9]{8}-[a-f0-9-]{27}").Value;
                        Assert.NotEmpty(_owner);
                        if (ClaimFailure is { } claim) throw claim;
                        return 1;
                    }
                    if (sql.StartsWith("DELETE", StringComparison.Ordinal))
                    {
                        if (_owner is null || !sql.Contains(_owner, StringComparison.Ordinal)) return 0;
                        if (ReleaseFailure is { } cleanup) throw cleanup;
                        LockHeld = false; return 1;
                    }
                    return 0;
                }
                if (sql.Contains("CREATE TABLE", StringComparison.Ordinal) && sql.Contains("__EFMigrationsHistory", StringComparison.Ordinal))
                {
                    if (BootstrapFailure is { } bootstrap) throw bootstrap;
                    _history = true;
                }
                if (sql.StartsWith("ALTER TABLE", StringComparison.Ordinal) && Failure is { } failure) throw failure;
                var id = Regex.Match(sql, @"20261113\d{6}_(Initial|Expand)").Value;
                if (sql.StartsWith("INSERT", StringComparison.Ordinal) && id.Length > 0) Applied.Add(id);
                if (sql.StartsWith("DELETE", StringComparison.Ordinal) && id.Length > 0) Applied.Remove(id);
                return 1;
            }

            public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData data, InterceptionResult<int> result)
                => InterceptionResult<int>.SuppressWithResult(Execute(command, false));
            public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data,
                InterceptionResult<int> result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(Execute(command, true)));
            private object Scalar(DbCommand command, bool async)
            {
                Commands.Add(new(command.CommandText, async));
                return command.CommandText.StartsWith("SELECT COUNT", StringComparison.Ordinal) ? _history ? 1 : 0 : Scope;
            }
            public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData data, InterceptionResult<object> result)
                => InterceptionResult<object>.SuppressWithResult(Scalar(command, false));
            public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData data,
                InterceptionResult<object> result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(InterceptionResult<object>.SuppressWithResult(Scalar(command, true)));
            private DbDataReader Reader(DbCommand command, bool async)
            {
                Commands.Add(new(command.CommandText, async));
                var table = new DataTable();
                table.Columns.Add("MigrationId", typeof(string)); table.Columns.Add("ProductVersion", typeof(string));
                foreach (var id in Applied.OrderBy(id => id)) table.Rows.Add(id, "10.0.0");
                return table.CreateDataReader();
            }
            public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result)
                => InterceptionResult<DbDataReader>.SuppressWithResult(Reader(command, false));
            public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
                InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(InterceptionResult<DbDataReader>.SuppressWithResult(Reader(command, true)));
        }
    }

    [DbContext(typeof(MigrationsTests.MigrationContext))]
    [Migration(MigrationsTests.Initial)]
    public sealed class InitialSchemaMigration : Migration
    {
        protected override void Up(MigrationBuilder builder)
            => builder.CreateTable("Rows", columns: table => new
            {
                Id = table.Column<int>(nullable: false).Annotation("OceanBase:Identity", true),
                Code = table.Column<string>(maxLength: 32, nullable: false)
            }, constraints: table => table.PrimaryKey("PK_Rows", row => row.Id));
        protected override void Down(MigrationBuilder builder) => builder.DropTable("Rows");
        protected override void BuildTargetModel(ModelBuilder builder) => MigrationsTests.ConfigureModel(builder, false);
    }

    [DbContext(typeof(MigrationsTests.MigrationContext))]
    [Migration(MigrationsTests.Expand)]
    public sealed class ExpandSchemaMigration : Migration
    {
        protected override void Up(MigrationBuilder builder)
            => builder.AlterColumn<string>("Code", "Rows", maxLength: 64, nullable: false, oldClrType: typeof(string), oldMaxLength: 32);
        protected override void Down(MigrationBuilder builder)
            => builder.AlterColumn<string>("Code", "Rows", maxLength: 32, nullable: false, oldClrType: typeof(string), oldMaxLength: 64);
        protected override void BuildTargetModel(ModelBuilder builder) => MigrationsTests.ConfigureModel(builder, true);
    }

    [DbContext(typeof(MigrationsTests.MigrationContext))]
    public sealed class MigrationModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder builder) => MigrationsTests.ConfigureModel(builder, true);
    }

    public sealed class RawMigrationContext(DbContextOptions<RawMigrationContext> options) : DbContext(options);

    [DbContext(typeof(RawMigrationContext))]
    [Migration("20261113100000_Raw")]
    public sealed class RawBlockMigration : Migration
    {
        protected override void Up(MigrationBuilder builder)
        {
            builder.Sql("-- comment\nBEGIN NULL; END;");
            builder.Sql("CREATE OR REPLACE TRIGGER \"TR_Rows\" BEFORE INSERT ON \"Rows\" BEGIN NULL; END;");
        }
    }

    [DbContext(typeof(RawMigrationContext))]
    [Migration("20261113100100_Large")]
    public sealed class LargeBlockMigration : Migration
    {
        protected override void Up(MigrationBuilder builder) => builder.Sql("BEGIN /*" + new string('界', 11000) + "*/ NULL; END;");
    }
}
