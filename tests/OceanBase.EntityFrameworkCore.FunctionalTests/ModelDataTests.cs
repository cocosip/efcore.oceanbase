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

namespace OceanBase.EntityFrameworkCore.FunctionalTests;

public sealed class ModelDataTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var mode in Enum.GetValues<OceanBaseCompatibilityMode>())
        foreach (var async in new[] { false, true }) yield return [mode, async];
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql)]
    [InlineData(OceanBaseCompatibilityMode.Oracle)]
    public void Create_script_appends_ordered_converted_and_owned_model_data_without_excluded_tables(OceanBaseCompatibilityMode mode)
    {
        var capture = new Commands();
        using var context = Create(mode, capture);
        var sql = Unquote(context.Database.GenerateCreateScript());
        Assert.True(sql.IndexOf("INSERT", StringComparison.Ordinal) > sql.LastIndexOf("ALTER TABLE", StringComparison.Ordinal));
        Assert.True(sql.IndexOf("INSERT INTO ZParents", StringComparison.Ordinal) < sql.IndexOf("INSERT INTO AChildren", StringComparison.Ordinal));
        Assert.Contains("City", sql);
        Assert.Contains("'can''t'", sql);
        Assert.Contains("'Ready'", sql);
        Assert.Contains("'city'", sql);
        var insert = Regex.Match(sql, @"INSERT INTO ZParents \(([^)]+)\)\s+VALUES \(([^)]+)\)");
        Assert.True(insert.Success, sql);
        var columns = insert.Groups[1].Value.Split(", ");
        var values = insert.Groups[2].Value.Split(", ");
        Assert.Equal("7", values[Array.IndexOf(columns, "Id")]);
        Assert.Equal("1", values[Array.IndexOf(columns, "Enabled")]);
        Assert.Equal(mode == OceanBaseCompatibilityMode.MySql ? "'Ready'" : "N'Ready'", values[Array.IndexOf(columns, "State")]);
        Assert.DoesNotContain("ExternalRows", sql);
        Assert.Equal(2, sql.Split("INSERT INTO", StringSplitOptions.None).Length - 1);
        Assert.Empty(capture.Items);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task EnsureCreated_inserts_model_data_before_one_callback_in_separate_transactions(OceanBaseCompatibilityMode mode, bool async)
    {
        foreach (var existing in new[] { false, true })
        {
            var capture = new Commands { ExistingTables = existing };
            var callbacks = 0;
            void Callback(DbContext db, bool created)
            {
                callbacks++;
                Assert.Equal(!existing, created);
                Assert.Equal(existing ? 0 : 1, capture.Transactions.Count(t => t.Committed));
                Assert.NotNull(db.Database.CurrentTransaction);
                capture.Events.Add("callback");
            }
            await using var context = Create(mode, capture, Callback);
            context.Database.SetCommandTimeout(24);
            using var source = new CancellationTokenSource();
            Assert.Equal(!existing, async ? await context.Database.EnsureCreatedAsync(source.Token) : context.Database.EnsureCreated());
            Assert.Equal(1, callbacks);
            Assert.Equal(existing ? 1 : 2, capture.Transactions.Count);
            Assert.All(capture.Transactions, tx => { Assert.True(tx.Committed); Assert.True(tx.Disposed); });
            var inserts = capture.Items.Where(item => item.Sql.StartsWith("INSERT", StringComparison.Ordinal)).ToArray();
            Assert.Equal(existing ? 0 : 2, inserts.Length);
            if (!existing)
            {
                Assert.StartsWith("INSERT INTO ZParents", Unquote(inserts[0].Sql));
                Assert.StartsWith("INSERT INTO AChildren", Unquote(inserts[1].Sql));
                Assert.All(inserts, item => Assert.Same(capture.Transactions[0], item.Transaction));
                Assert.True(capture.Events.IndexOf("commit") < capture.Events.IndexOf("callback"));
            }
            Assert.All(capture.Items.Where(item => item.Sql.StartsWith("CREATE", StringComparison.Ordinal)
                || item.Sql.StartsWith("ALTER", StringComparison.Ordinal)), item => Assert.Null(item.Transaction));
            Assert.All(capture.Items, item => { Assert.Equal(async, item.Async); Assert.Equal(24, item.Timeout); if (async) Assert.Equal(source.Token, item.Token); });
            AssertReleased(context);
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Explicit_CreateTables_applies_model_data_without_EnsureCreated_callbacks(OceanBaseCompatibilityMode mode, bool async)
    {
        var capture = new Commands();
        await using var context = Create(mode, capture, (_, _) => throw new Exception("CreateTables must not invoke callback."));
        context.Database.OpenConnection();
        try
        {
            var creator = context.GetService<IRelationalDatabaseCreator>();
            if (async) await creator.CreateTablesAsync(); else creator.CreateTables();
            Assert.Equal(2, capture.Items.Count(item => item.Sql.StartsWith("INSERT", StringComparison.Ordinal)));
            Assert.True(Assert.Single(capture.Transactions).Committed);
            Assert.Equal(ConnectionState.Open, context.Database.GetDbConnection().State);
        }
        finally { context.Database.CloseConnection(); }
        AssertReleased(context);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Failed_model_data_rolls_back_without_replaying_or_invoking_callbacks(OceanBaseCompatibilityMode mode, bool async)
    {
        foreach (var scenario in new[] { "insert", "cancel", "scope", "rollback", "dispose", "commit" })
        {
            using var source = new CancellationTokenSource();
            var failure = scenario == "cancel" ? (Exception)new OperationCanceledException(source.Token)
                : new InvalidOperationException("Synthetic model data failure.");
            var capture = new Commands
            {
                InsertFailure = scenario is "scope" or "commit" ? null : failure,
                SeedScope = scenario == "scope" ? "other" : "owned",
                BeforeInsertFailure = scenario == "cancel" ? source.Cancel : null,
                RollbackFailure = scenario == "rollback" ? new InvalidOperationException("Rollback failure.") : null,
                DisposeFailure = scenario == "dispose" ? new InvalidOperationException("Dispose failure.") : null,
                CommitFailure = scenario == "commit" ? failure : null
            };
            await using var context = Create(mode, capture, (_, _) => throw new Exception("Callback must not run after failed model data."));
            var error = await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                if (async) await context.Database.EnsureCreatedAsync(source.Token); else context.Database.EnsureCreated();
            });
            if (scenario != "scope") Assert.Same(failure, error);
            else Assert.IsType<InvalidOperationException>(error);
            var transaction = Assert.Single(capture.Transactions);
            Assert.False(transaction.Committed);
            Assert.True(transaction.Disposed);
            Assert.Equal(1, transaction.Rollbacks);
            Assert.Equal(CancellationToken.None, transaction.RollbackToken);
            Assert.Equal(scenario == "scope" ? 0 : 2, capture.Inserts);
            if (scenario == "rollback") Assert.Same(capture.RollbackFailure, error.Data["OceanBase.ModelDataRollbackError"]);
            if (scenario == "dispose") Assert.Same(capture.DisposeFailure, error.Data["OceanBase.ModelDataCleanupError"]);
            AssertReleased(context);
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Cross_scope_seeded_models_reject_before_open_or_DDL(OceanBaseCompatibilityMode mode, bool async)
    {
        var capture = new Commands();
        await using var context = Create(mode, capture, crossScope: true);
        if (async) await Assert.ThrowsAsync<NotSupportedException>(() => context.Database.EnsureCreatedAsync());
        else Assert.Throws<NotSupportedException>(() => context.Database.EnsureCreated());
        Assert.Empty(capture.Items);
        Assert.Empty(capture.Transactions);
        AssertReleased(context);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Failed_model_data_with_a_caller_opened_connection_preserves_the_lease(OceanBaseCompatibilityMode mode, bool async)
    {
        var failure = new InvalidOperationException("Insert failed.");
        var capture = new Commands
        {
            InsertFailure = failure,
            RollbackFailure = new InvalidOperationException("Rollback failed."),
            DisposeFailure = new InvalidOperationException("Dispose failed.")
        };
        await using var context = Create(mode, capture);
        if (async) await context.Database.OpenConnectionAsync(); else context.Database.OpenConnection();
        try
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                if (async) await context.Database.EnsureCreatedAsync(); else context.Database.EnsureCreated();
            });
            Assert.Same(failure, error);
            Assert.Null(context.Database.CurrentTransaction);
            Assert.Equal(ConnectionState.Open, context.Database.GetDbConnection().State);
            Assert.Equal(1, Assert.Single(capture.Transactions).Rollbacks);
        }
        finally
        {
            if (async) await context.Database.CloseConnectionAsync(); else context.Database.CloseConnection();
        }
        AssertReleased(context);
    }

    private static Context Create(OceanBaseCompatibilityMode mode, Commands capture, Action<DbContext, bool>? callback = null, bool crossScope = false)
    {
        var builder = new DbContextOptionsBuilder<Context>().UseOceanBase("Server=offline;Database=owned", mode)
            .EnableOceanBaseSchemaManagement("owned").EnableServiceProviderCaching(false)
            .ReplaceService<IRelationalConnection, TransactionTests.TestConnection>().AddInterceptors(capture);
        if (callback is not null) builder.UseSeeding(callback).UseAsyncSeeding((db, created, _) => { callback(db, created); return Task.CompletedTask; });
        return new Context(builder.Options, crossScope);
    }

    private static void AssertReleased(DbContext context)
    {
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }
    private static string Unquote(string sql) => sql.Replace("`", "", StringComparison.Ordinal).Replace("\"", "", StringComparison.Ordinal);

    private sealed class Context(DbContextOptions<Context> options, bool crossScope) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model)
        {
            var parent = model.Entity<Parent>();
            parent.ToTable("ZParents", crossScope ? "other" : null);
            parent.Property(p => p.Name).HasMaxLength(32);
            parent.Property(p => p.State).HasConversion<string>().HasMaxLength(16);
            parent.HasData(new { Id = 7, Name = "can't", Enabled = true, State = State.Ready });
            parent.OwnsOne(p => p.Address, owned =>
            {
                owned.Property(a => a.City).HasMaxLength(32).HasColumnName("City");
                owned.HasData(new { ParentId = 7, City = "city" });
            });
            var child = model.Entity<Child>();
            child.ToTable("AChildren");
            child.Property(c => c.Note).HasMaxLength(32);
            child.HasOne<Parent>().WithMany().HasForeignKey(c => c.ParentId);
            child.HasData(new { Id = 11, ParentId = 7, Note = "child" });
            model.Entity<External>().ToTable("ExternalRows", table => table.ExcludeFromMigrations());
            model.Entity<External>().HasData(new { Id = 1 });
        }
    }
    private enum State { Ready }
    private sealed class Parent { public int Id { get; set; } public string Name { get; set; } = ""; public bool Enabled { get; set; } public State State { get; set; } public Address Address { get; set; } = new(); }
    private sealed class Address { public string City { get; set; } = ""; }
    private sealed class Child { public int Id { get; set; } public int ParentId { get; set; } public string Note { get; set; } = ""; }
    private sealed class External { public int Id { get; set; } }
    private sealed record Command(string Sql, bool Async, CancellationToken Token, DbTransaction? Transaction, int Timeout);

    private sealed class Commands : DbCommandInterceptor, IDbTransactionInterceptor
    {
        public bool ExistingTables { get; init; }
        public string SeedScope { get; init; } = "owned";
        public int Inserts { get; private set; }
        public Exception? InsertFailure { get; init; }
        public Action? BeforeInsertFailure { get; init; }
        public Exception? RollbackFailure { get; init; }
        public Exception? DisposeFailure { get; init; }
        public Exception? CommitFailure { get; init; }
        public List<Command> Items { get; } = [];
        public List<SeedTransaction> Transactions { get; } = [];
        public List<string> Events { get; } = [];
        public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData data, InterceptionResult<object> result)
            => Scalar(command, false, default);
        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<object> result, CancellationToken token = default) => ValueTask.FromResult(Scalar(command, true, token));
        private InterceptionResult<object> Scalar(DbCommand command, bool async, CancellationToken token)
        {
            Record(command, async, token);
            return InterceptionResult<object>.SuppressWithResult(command.CommandText.StartsWith("SELECT COUNT", StringComparison.Ordinal)
                ? ExistingTables ? 1 : 0 : command.Transaction is null ? "owned" : SeedScope);
        }
        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData data, InterceptionResult<int> result)
            => Write(command, false, default);
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<int> result, CancellationToken token = default) => ValueTask.FromResult(Write(command, true, token));
        private InterceptionResult<int> Write(DbCommand command, bool async, CancellationToken token)
        {
            Record(command, async, token);
            if (command.CommandText.StartsWith("INSERT", StringComparison.Ordinal) && ++Inserts == 2 && InsertFailure is { } failure)
            { BeforeInsertFailure?.Invoke(); throw failure; }
            return InterceptionResult<int>.SuppressWithResult(1);
        }
        private void Record(DbCommand command, bool async, CancellationToken token)
        {
            Items.Add(new(command.CommandText, async, token, command.Transaction, command.CommandTimeout));
            Events.Add(command.CommandText.StartsWith("INSERT", StringComparison.Ordinal) ? "insert" : "command");
        }
        public InterceptionResult<DbTransaction> TransactionStarting(DbConnection connection, TransactionStartingEventData data, InterceptionResult<DbTransaction> result)
        {
            var transaction = new SeedTransaction(connection, this);
            Transactions.Add(transaction);
            return InterceptionResult<DbTransaction>.SuppressWithResult(transaction);
        }
        public ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection, TransactionStartingEventData data,
            InterceptionResult<DbTransaction> result, CancellationToken token = default) => ValueTask.FromResult(TransactionStarting(connection, data, result));
    }

    private sealed class SeedTransaction(DbConnection connection, Commands capture) : DbTransaction
    {
        public bool Committed { get; private set; }
        public bool Disposed { get; private set; }
        public int Rollbacks { get; private set; }
        public CancellationToken RollbackToken { get; private set; }
        protected override DbConnection DbConnection => connection;
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        public override void Commit()
        {
            if (capture.CommitFailure is { } failure) throw failure;
            Committed = true;
            capture.Events.Add("commit");
        }
        public override void Rollback()
        {
            Rollbacks++;
            capture.Events.Add("rollback");
            if (capture.RollbackFailure is { } failure) throw failure;
        }
        public override Task RollbackAsync(CancellationToken token = default)
        { RollbackToken = token; token.ThrowIfCancellationRequested(); Rollback(); return Task.CompletedTask; }
        protected override void Dispose(bool disposing)
        {
            Disposed |= disposing;
            base.Dispose(disposing);
            if (disposing && capture.DisposeFailure is { } failure) throw failure;
        }
    }
}
