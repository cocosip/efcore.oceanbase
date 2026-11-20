using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text;
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
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "table''s comment" : "table''''s comment", forward);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "column''s comment" : "column''''s comment", forward);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "MODIFY COLUMN" : "MODIFY (", forward);
            Assert.Contains("initial row", forward);
            Assert.Contains(Expand, forward);
            Assert.DoesNotContain("START TRANSACTION", forward);
            var reverse = migrator.GenerateScript(Expand, Migration.InitialDatabase);
            Assert.Contains("DROP TABLE", reverse);
            Assert.Contains("DELETE FROM", reverse);
            var idempotent = migrator.GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "PREPARE" : "EXECUTE IMMEDIATE", idempotent);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql
                ? Convert.ToHexString(Encoding.UTF8.GetBytes("table''s comment")) : "table''''s comment", idempotent);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "@ob_migration_apply" : "IF ob_count = 0 THEN", idempotent);
            var down = migrator.GenerateScript(Expand, Migration.InitialDatabase, MigrationsSqlGenerationOptions.Idempotent);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "@ob_migration_apply" : "IF ob_count > 0 THEN", down);
            Assert.Empty(capture.Commands);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Serialized_migrations_apply_once_and_revert_with_history(OceanBaseCompatibilityMode mode, bool async)
        {
            var capture = new Capture();
            await using var context = Create(mode, capture);
            if (async) await context.Database.MigrateAsync(); else context.Database.Migrate();
            Assert.Equal(new[] { Initial, Expand }, capture.Applied.OrderBy(id => id));
            Assert.DoesNotContain(capture.Commands, c => c.Sql.Contains("__EFMigrationsHistoryLock", StringComparison.Ordinal));
            Assert.All(capture.Commands, c => Assert.Null(c.Transaction));
            Assert.Contains(capture.Commands, c => c.Sql.StartsWith("CREATE TABLE", StringComparison.Ordinal) && c.Sql.Contains("Rows", StringComparison.Ordinal));
            var seed = Assert.Single(capture.Commands, c => c.Sql.StartsWith("INSERT", StringComparison.Ordinal)
                && c.Sql.Contains("Rows", StringComparison.Ordinal));
            var initialHistory = Assert.Single(capture.Commands, c => c.Sql.StartsWith("INSERT", StringComparison.Ordinal)
                && c.Sql.Contains(Initial, StringComparison.Ordinal));
            Assert.True(capture.Commands.IndexOf(seed) < capture.Commands.IndexOf(initialHistory));
            Assert.All(capture.Commands, c => Assert.Equal(async, c.Async));
            if (mode == OceanBaseCompatibilityMode.Oracle)
                Assert.All(capture.Commands.Where(c => !c.Sql.StartsWith("BEGIN", StringComparison.Ordinal)
                    && !c.Sql.StartsWith("DECLARE", StringComparison.Ordinal)),
                    c => Assert.False(c.Sql.TrimEnd().EndsWith(';'), c.Sql));
            var count = capture.Commands.Count;
            if (async) await context.Database.MigrateAsync(); else context.Database.Migrate();
            Assert.DoesNotContain(capture.Commands.Skip(count), c => c.Sql.Contains("ALTER TABLE", StringComparison.Ordinal));
            Assert.DoesNotContain(capture.Commands.Skip(count), c => c.Sql.Contains("CREATE TABLE", StringComparison.Ordinal)
                || c.Sql.StartsWith("INSERT", StringComparison.Ordinal));
            var migrator = context.GetService<IMigrator>();
            if (async) await migrator.MigrateAsync(Migration.InitialDatabase); else migrator.Migrate(Migration.InitialDatabase);
            Assert.Empty(capture.Applied);
            Assert.Null(context.Database.CurrentTransaction);
            Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Migration_failure_and_cancellation_do_not_record_success(OceanBaseCompatibilityMode mode, bool async)
        {
            using var source = new CancellationTokenSource();
            Exception failure = async ? new OperationCanceledException(source.Token) : new InvalidOperationException("synthetic alter failure");
            var capture = new Capture { Failure = failure };
            await using var context = Create(mode, capture);
            var actual = async ? await Assert.ThrowsAnyAsync<Exception>(() => context.Database.MigrateAsync(source.Token))
                : Assert.ThrowsAny<Exception>(() => context.Database.Migrate());
            Assert.Same(failure, actual);
            Assert.Equal(new[] { Initial }, capture.Applied);
            Assert.Null(context.Database.CurrentTransaction);
            Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Missing_opt_in_or_wrong_namespace_never_executes_migration_DDL(OceanBaseCompatibilityMode mode, bool async)
        {
            foreach (var boundary in new[] { "no-opt-in", "wrong-scope" })
            {
                var capture = new Capture { Scope = boundary == "wrong-scope" ? "other" : "offline" };
                await using var context = Create(mode, capture, optIn: boundary != "no-opt-in");
                if (async) await Assert.ThrowsAnyAsync<Exception>(() => context.Database.MigrateAsync());
                else Assert.ThrowsAny<Exception>(() => context.Database.Migrate());
                Assert.DoesNotContain(capture.Commands, c => c.Sql.Contains("CREATE TABLE", StringComparison.Ordinal) && c.Sql.Contains("Rows", StringComparison.Ordinal));
                Assert.Empty(capture.Applied);
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
        public async Task Caller_transaction_rejects_before_any_command(OceanBaseCompatibilityMode mode, bool async)
        {
            var capture = new Capture();
            await using var context = Create(mode, capture);
            await using var transaction = await context.Database.BeginTransactionAsync();
            if (async) await Assert.ThrowsAsync<NotSupportedException>(() => context.Database.MigrateAsync());
            else Assert.Throws<NotSupportedException>(() => context.Database.Migrate());
            Assert.Same(transaction, context.Database.CurrentTransaction);
            Assert.Empty(capture.Commands);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public async Task Pre_cancelled_migration_does_not_open_the_connection(OceanBaseCompatibilityMode mode)
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
        public async Task History_creation_failure_preserves_the_error_and_never_runs_migration_operations(OceanBaseCompatibilityMode mode, bool async)
        {
            var failure = new InvalidOperationException("synthetic history creation failure");
            var capture = new Capture { BootstrapFailure = failure };
            await using var context = Create(mode, capture);
            var actual = async ? await Assert.ThrowsAsync<InvalidOperationException>(() => context.Database.MigrateAsync())
                : Assert.Throws<InvalidOperationException>(() => context.Database.Migrate());
            Assert.Same(failure, actual);
            Assert.Empty(capture.Applied);
            Assert.DoesNotContain(capture.Commands, command => command.Sql.Contains("Rows", StringComparison.Ordinal));
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
            builder.HasAnnotation("ProductVersion", "8.0.0");
            var row = builder.Entity("MigrationRow");
            row.ToTable("Rows", table => table.HasComment("table's comment"));
            row.Property<int>("Id").ValueGeneratedOnAdd();
            row.HasKey("Id");
            row.Property<string>("Code").HasMaxLength(evolved ? 64 : 32).IsRequired().HasComment("column's comment");
            row.HasData(new { Id = 7, Code = "initial row" });
        }

        private sealed record Command(string Sql, bool Async, DbTransaction? Transaction);
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
            public string Scope { get; init; } = "offline";
            public Exception? Failure { get; init; }
            public Exception? BootstrapFailure { get; init; }
            private bool _history;

            private int Execute(DbCommand command, bool async)
            {
                var sql = command.CommandText;
                Commands.Add(new(sql, async, command.Transaction));
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
                Commands.Add(new(command.CommandText, async, command.Transaction));
                return command.CommandText.StartsWith("SELECT COUNT", StringComparison.Ordinal) ? _history ? 1 : 0 : Scope;
            }
            public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData data, InterceptionResult<object> result)
                => InterceptionResult<object>.SuppressWithResult(Scalar(command, false));
            public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData data,
                InterceptionResult<object> result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(InterceptionResult<object>.SuppressWithResult(Scalar(command, true)));
            private DbDataReader Reader(DbCommand command, bool async)
            {
                Commands.Add(new(command.CommandText, async, command.Transaction));
                var table = new DataTable();
                table.Columns.Add("MigrationId", typeof(string)); table.Columns.Add("ProductVersion", typeof(string));
                foreach (var id in Applied.OrderBy(id => id)) table.Rows.Add(id, "8.0.0");
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
        {
            builder.CreateTable("Rows", columns: table => new
            {
                Id = table.Column<int>(nullable: false).Annotation("OceanBase:Identity", true),
                Code = table.Column<string>(maxLength: 32, nullable: false, comment: "column's comment")
            }, constraints: table => table.PrimaryKey("PK_Rows", row => row.Id), comment: "table's comment");
            builder.InsertData("Rows", new[] { "Id", "Code" }, new object[] { 7, "initial row" });
        }
        protected override void Down(MigrationBuilder builder) => builder.DropTable("Rows");
        protected override void BuildTargetModel(ModelBuilder builder) => MigrationsTests.ConfigureModel(builder, false);
    }

    [DbContext(typeof(MigrationsTests.MigrationContext))]
    [Migration(MigrationsTests.Expand)]
    public sealed class ExpandSchemaMigration : Migration
    {
        protected override void Up(MigrationBuilder builder)
            => builder.AlterColumn<string>("Code", "Rows", maxLength: 64, nullable: false, oldClrType: typeof(string), oldMaxLength: 32,
                comment: "column's comment", oldComment: "column's comment");
        protected override void Down(MigrationBuilder builder)
            => builder.AlterColumn<string>("Code", "Rows", maxLength: 32, nullable: false, oldClrType: typeof(string), oldMaxLength: 64,
                comment: "column's comment", oldComment: "column's comment");
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
