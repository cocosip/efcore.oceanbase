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
using Xunit.Abstractions;

namespace OceanBase.EntityFrameworkCore.FunctionalTests;

public sealed class SplittingRuntimeTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Modes()
    {
        foreach (var mode in Enum.GetValues<OceanBaseCompatibilityMode>())
            foreach (var async in new[] { false, true }) yield return [mode, async];
    }

    public static IEnumerable<object[]> TrackingModes()
    {
        foreach (var mode in Modes())
            foreach (var tracking in Enum.GetValues<QueryTrackingBehavior>()) yield return [mode[0], mode[1], tracking];
    }

    [Theory]
    [MemberData(nameof(TrackingModes))]
    public async Task Entity_fragments_materialize_one_entity_and_related_rows(OceanBaseCompatibilityMode mode, bool async,
        QueryTrackingBehavior tracking)
    {
        var commands = new Commands();
        await using var context = Create(mode, commands);
        context.ChangeTracker.QueryTrackingBehavior = tracking;
        var query = context.Set<Product>().Include(p => p.Labels).OrderBy(p => p.Id);
        output.WriteLine(query.ToQueryString());
        commands.Results = Table([typeof(int), typeof(int), typeof(string), typeof(int), typeof(string), typeof(int),
            typeof(int), typeof(int), typeof(string)],
            [101, 3, "one", 1, null, 7, 11, 101, "label"],
            [102, 4, "two", 1, "text", null, null, null, null]);
        var rows = async ? await query.ToListAsync() : query.ToList();
        Assert.Equal(2, rows.Count);
        Assert.Null(rows[0].Text);
        Assert.Equal(3, rows[0].Count);
        Assert.Equal("text", rows[1].Text);
        Assert.Same(rows[0], Assert.Single(rows[0].Labels).Product);
        Assert.Empty(rows[1].Labels);
        Assert.Equal(tracking == QueryTrackingBehavior.TrackAll ? 3 : 0, context.ChangeTracker.Entries().Count());
        var sql = Assert.Single(commands.Items).Sql;
        Assert.Equal(2, Regex.Matches(sql, "INNER JOIN").Count);
        Assert.Contains("LEFT JOIN", sql);
        Assert.Contains("DetailId", sql);
        Assert.Contains("MetricId", sql);
        Assert.Equal(new object?[] { 1 }, commands.Items[0].Inputs);
        commands.Results.Clear();
        Assert.Empty(async ? await query.ToListAsync() : query.ToList());
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Fragment_predicates_and_filters_refresh_parameters(OceanBaseCompatibilityMode mode, bool async)
    {
        var commands = new Commands { Results = Table([typeof(int)], [101]) };
        await using var context = Create(mode, commands);
        var text = "first";
        var query = context.Set<Product>().Where(p => p.Text == text && p.Count > 1).Select(p => p.Id);
        Assert.Equal(new[] { 101 }, async ? await query.ToArrayAsync() : query.ToArray());
        Assert.Equal(new object?[] { 1, "first" }, commands.Items.Single().Inputs);
        commands.Items.Clear();
        text = "second";
        context.Tenant = 2;
        Assert.Equal(new[] { 101 }, async ? await query.ToArrayAsync() : query.ToArray());
        Assert.Equal(new object?[] { 2, "second" }, commands.Items.Single().Inputs);
        Assert.Contains("Details", commands.Items[0].Sql);
        Assert.Contains("Metrics", commands.Items[0].Sql);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Fragment_CRUD_propagates_one_key_updates_only_owning_tables_and_deletes_dependents_first(
        OceanBaseCompatibilityMode mode, bool async)
    {
        var commands = new Commands();
        await using var context = Create(mode, commands);
        var native = Native(context);
        var row = new Product { Name = "new", Text = "detail", Count = 3, Tenant = 1, Version = 7 };
        context.Add(row);
        Assert.Equal(3, await Save(context, async));
        Assert.Equal(101, row.Id);
        Assert.Equal(EntityState.Unchanged, context.Entry(row).State);
        var inserts = commands.Items.Where(c => c.Sql.StartsWith("INSERT", StringComparison.Ordinal)).ToArray();
        Assert.Equal("Products", inserts[0].Table);
        Assert.Equal(new[] { "Details", "Metrics" }, inserts.Skip(1).Select(c => c.Table).OrderBy(t => t));
        Assert.DoesNotContain(101, inserts[0].Inputs);
        Assert.All(inserts.Skip(1), c => Assert.Contains(101, c.Inputs));
        Assert.Equal(mode == OceanBaseCompatibilityMode.MySql ? 1 : 0, commands.Items.Count(c => c.Sql.Contains("LAST_INSERT_ID")));
        Assert.Equal(mode == OceanBaseCompatibilityMode.Oracle ? 1 : 0, inserts.Count(c => c.Sql.Contains("RETURNING")));
        AssertCommit(native, commands);

        commands.Items.Clear();
        row.Text = null;
        Assert.Equal(1, await Save(context, async));
        Assert.Equal("Details", Assert.Single(commands.Items).Table);
        Assert.Contains(DBNull.Value, commands.Items[0].Inputs);
        Assert.Contains(101, commands.Items[0].Inputs);

        commands.Items.Clear();
        row.Name = "changed";
        row.Count = 4;
        Assert.Equal(2, await Save(context, async));
        Assert.Equal(new[] { "Metrics", "Products" }, commands.Items.Select(c => c.Table).OrderBy(t => t));
        Assert.Contains(commands.Items, c => c.Table == "Products" && c.Inputs.Contains(7));
        AssertCommit(native, commands);

        commands.Items.Clear();
        context.Remove(row);
        Assert.Equal(3, await Save(context, async));
        Assert.Equal("Products", commands.Items.Last().Table);
        Assert.Equal(new[] { "Details", "Metrics" }, commands.Items.Take(2).Select(c => c.Table).OrderBy(t => t));
        Assert.Empty(context.ChangeTracker.Entries());
        AssertCommit(native, commands);
        Assert.Equal(ConnectionState.Closed, native.State);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Failed_fragment_insert_retains_temporary_key_and_uncommitted_transaction(OceanBaseCompatibilityMode mode, bool async)
    {
        var commands = new Commands();
        await using var context = Create(mode, commands);
        var native = Native(context);
        var failure = new InvalidOperationException("Synthetic fragment insert failure.");
        native.WriteFailure = failure;
        var row = new Product { Name = "new", Text = "detail", Count = 3, Tenant = 1 };
        context.Add(row);
        var error = async ? await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync())
            : Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        Assert.Same(failure, error.InnerException);
        Assert.Same(row, Assert.Single(error.Entries).Entity);
        Assert.Equal(EntityState.Added, context.Entry(row).State);
        Assert.True(context.Entry(row).Property(p => p.Id).IsTemporary);
        AssertFailure(native, commands);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Fragment_concurrency_failure_does_not_accept_other_table_changes(OceanBaseCompatibilityMode mode, bool async)
    {
        var commands = new Commands { ZeroRowsFor = "Products" };
        await using var context = Create(mode, commands);
        var row = new Product { Id = 101, Name = "old", Text = "detail", Count = 3, Tenant = 1, Version = 7 };
        context.Attach(row);
        row.Name = "changed";
        row.Text = "changed";
        var error = async ? await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => context.SaveChangesAsync())
            : Assert.Throws<DbUpdateConcurrencyException>(() => context.SaveChanges());
        Assert.Same(row, Assert.Single(error.Entries).Entity);
        Assert.Equal(EntityState.Modified, context.Entry(row).State);
        Assert.Equal(7, context.Entry(row).Property(p => p.Version).OriginalValue);
        Assert.Contains(commands.Items, c => c.Table == "Products" && c.Inputs.Contains(7));
        AssertFailure(Native(context), commands);
    }

    [Theory]
    [MemberData(nameof(TrackingModes))]
    public async Task Shared_rows_materialize_optional_all_null_dependents_as_absent(OceanBaseCompatibilityMode mode, bool async,
        QueryTrackingBehavior tracking)
    {
        var commands = new Commands();
        await using var context = Create(mode, commands);
        context.ChangeTracker.QueryTrackingBehavior = tracking;
        var query = context.Set<Owner>().Include(o => o.Profile).OrderBy(o => o.Id);
        output.WriteLine(query.ToQueryString());
        commands.Results = Table([typeof(int), typeof(string), typeof(int), typeof(int), typeof(string), typeof(int)],
            [101, "present", 7, 101, "text", 7], [102, "absent", 7, null, null, null]);
        var rows = async ? await query.ToListAsync() : query.ToList();
        Assert.Equal(2, rows.Count);
        Assert.NotNull(rows[0].Profile);
        Assert.Equal("text", rows[0].Profile!.Text);
        Assert.Same(rows[0], rows[0].Profile!.Owner);
        Assert.Null(rows[1].Profile);
        Assert.Equal(tracking == QueryTrackingBehavior.TrackAll ? 3 : 0, context.ChangeTracker.Entries().Count());
        Assert.Single(commands.Items);
        commands.Results.Clear();
        Assert.Empty(async ? await query.ToListAsync() : query.ToList());
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Shared_row_CRUD_merges_columns_and_preserves_application_concurrency(OceanBaseCompatibilityMode mode, bool async)
    {
        var commands = new Commands();
        await using var context = Create(mode, commands);
        var owner = new Owner { Name = "new", Version = 7, Profile = new Profile { Text = "detail", Version = 7 } };
        context.Add(owner);
        await Save(context, async);
        Assert.Equal(101, owner.Id);
        Assert.Equal(owner.Id, owner.Profile.Id);
        Assert.Same(owner, owner.Profile.Owner);
        var insert = Assert.Single(commands.Items, c => c.Table == "SharedRows");
        Assert.Contains("Name", insert.Sql);
        Assert.Contains("Text", insert.Sql);
        Assert.Single(Regex.Matches(insert.Sql, "Version"));

        commands.Items.Clear();
        owner.Name = "changed";
        owner.Profile.Text = "changed-detail";
        owner.Version = 8;
        owner.Profile.Version = 8;
        await Save(context, async);
        var update = Assert.Single(commands.Items);
        Assert.Equal("SharedRows", update.Table);
        Assert.Contains("changed", update.Inputs);
        Assert.Contains("changed-detail", update.Inputs);
        Assert.Contains(7, update.Inputs);
        Assert.Contains(8, update.Inputs);
        Assert.Equal(8, owner.Version);
        Assert.Equal(8, context.Entry(owner.Profile).Property(p => p.Version).OriginalValue);

        commands.Items.Clear();
        context.Remove(owner.Profile);
        await Save(context, async);
        Assert.StartsWith("UPDATE", Assert.Single(commands.Items).Sql);
        Assert.Contains(DBNull.Value, commands.Items[0].Inputs);
        Assert.Contains(8, commands.Items[0].Inputs);
        Assert.Equal(EntityState.Unchanged, context.Entry(owner).State);

        commands.Items.Clear();
        owner.Profile = new Profile { Text = "restored", Version = 8 };
        await Save(context, async);
        Assert.StartsWith("UPDATE", Assert.Single(commands.Items).Sql);
        Assert.Contains("restored", commands.Items[0].Inputs);
        Assert.DoesNotContain("LAST_INSERT_ID", commands.Items[0].Sql);
        Assert.Equal(101, owner.Profile.Id);

        commands.Items.Clear();
        context.Remove(owner);
        await Save(context, async);
        Assert.StartsWith("DELETE", Assert.Single(commands.Items).Sql);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(ConnectionState.Closed, Native(context).State);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Shared_concurrency_failure_reports_both_entries_without_accepting_changes(OceanBaseCompatibilityMode mode, bool async)
    {
        var commands = new Commands { ZeroRowsFor = "SharedRows" };
        await using var context = Create(mode, commands);
        var owner = new Owner { Id = 101, Name = "old", Version = 7,
            Profile = new Profile { Id = 101, Text = "old", Version = 7 } };
        context.Attach(owner);
        owner.Name = "changed";
        owner.Profile.Text = "changed";
        var error = async ? await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => context.SaveChangesAsync())
            : Assert.Throws<DbUpdateConcurrencyException>(() => context.SaveChanges());
        Assert.Equal(2, error.Entries.Count);
        Assert.Contains(error.Entries, e => ReferenceEquals(e.Entity, owner));
        Assert.Contains(error.Entries, e => ReferenceEquals(e.Entity, owner.Profile));
        Assert.All(error.Entries, e => Assert.Equal(EntityState.Modified, e.State));
        Assert.Contains(7, Assert.Single(commands.Items).Inputs);
        Assert.Equal(7, owner.Version);
        Assert.Equal(7, owner.Profile.Version);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Shared_generated_concurrency_is_read_once_and_propagated_to_both_entries(OceanBaseCompatibilityMode mode, bool async)
    {
        var commands = new Commands { Results = Table([typeof(int), typeof(int)], [101, 101]) };
        await using var context = Create(mode, commands, generated: true);
        var owner = new Owner { Name = "new", Profile = new Profile { Text = "detail" } };
        context.Add(owner);
        await Save(context, async);
        Assert.Equal(101, owner.Id);
        Assert.Equal(101, owner.Version);
        Assert.Equal(101, owner.Profile.Version);
        commands.Items.Clear();
        commands.Results = Table([typeof(int)], [202]);
        commands.OutputValue = 202;
        owner.Profile.Text = "changed";
        await Save(context, async);
        Assert.Equal(202, owner.Version);
        Assert.Equal(202, owner.Profile.Version);
        Assert.Equal(202, context.Entry(owner).Property(o => o.Version).OriginalValue);
        Assert.Equal(202, context.Entry(owner.Profile).Property(p => p.Version).OriginalValue);
        var write = Assert.Single(commands.Items, c => c.Sql.StartsWith("UPDATE", StringComparison.Ordinal));
        Assert.Contains(101, write.Inputs);
        Assert.Single(Regex.Matches(write.Sql, "Version" + (mode == OceanBaseCompatibilityMode.MySql ? "` =" : "\" =")));
        Assert.Equal(mode == OceanBaseCompatibilityMode.MySql ? 2 : 1, commands.Items.Count);
    }

    private static Task<int> Save(DbContext context, bool async)
        => async ? context.SaveChangesAsync() : Task.FromResult(context.SaveChanges());
    private static TransactionTests.NativeConnection Native(DbContext context)
        => Assert.IsType<TransactionTests.NativeConnection>(context.Database.GetDbConnection());
    private static void AssertCommit(TransactionTests.NativeConnection native, Commands commands)
    {
        var transaction = native.Transactions.Last();
        Assert.Equal(1, transaction.Commits);
        Assert.Equal(1, transaction.Disposals);
        Assert.All(commands.Items, c => Assert.Same(transaction, c.Transaction));
    }
    private static void AssertFailure(TransactionTests.NativeConnection native, Commands commands)
    {
        var transaction = Assert.Single(native.Transactions);
        Assert.Equal(0, transaction.Commits);
        Assert.Equal(1, transaction.Disposals);
        Assert.True(transaction.DisposedActive);
        Assert.All(commands.Items, c => Assert.Same(transaction, c.Transaction));
        Assert.Equal(ConnectionState.Closed, native.State);
    }
    private static SplitContext Create(OceanBaseCompatibilityMode mode, Commands commands, bool generated = false)
        => new(new DbContextOptionsBuilder<SplitContext>().UseOceanBase("Server=offline;Database=app", mode)
            .ReplaceService<IRelationalConnection, TransactionTests.TestConnection>()
            .EnableServiceProviderCaching(false).AddInterceptors(commands).Options, generated);

    private sealed class SplitContext(DbContextOptions<SplitContext> options, bool generated) : DbContext(options)
    {
        public int Tenant { get; set; } = 1;
        protected override void OnModelCreating(ModelBuilder model)
        {
            var row = model.Entity<Product>();
            row.ToTable("Products");
            row.Property(p => p.Name).HasMaxLength(40);
            row.Property(p => p.Version).IsConcurrencyToken();
            row.Property(p => p.Text).HasMaxLength(40);
            row.HasQueryFilter(p => p.Tenant == Tenant);
            row.SplitToTable("Details", t => { t.Property(p => p.Id).HasColumnName("DetailId"); t.Property(p => p.Text); });
            row.SplitToTable("Metrics", t => { t.Property(p => p.Id).HasColumnName("MetricId"); t.Property(p => p.Count); });
            model.Entity<Label>().ToTable("Labels").Property(l => l.Text).HasMaxLength(40);
            model.Entity<Label>().HasOne(l => l.Product).WithMany(p => p.Labels).HasForeignKey(l => l.ProductId);
            model.Entity<Owner>().ToTable("SharedRows").Property(o => o.Name).HasMaxLength(40);
            model.Entity<Owner>().Property(o => o.Version).IsConcurrencyToken();
            model.Entity<Profile>().ToTable("SharedRows").Property(p => p.Text).HasMaxLength(40);
            model.Entity<Profile>().Property(p => p.Version).IsConcurrencyToken();
            if (generated)
            {
                model.Entity<Owner>().Property(o => o.Version).HasComputedColumnSql("1 + 1");
                model.Entity<Profile>().Property(p => p.Version).HasComputedColumnSql("1 + 1");
            }
            model.Entity<Owner>().HasOne(o => o.Profile).WithOne(p => p.Owner).HasForeignKey<Profile>(p => p.Id);
        }
    }
    private sealed class Product
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public int Tenant { get; set; }
        public int? Version { get; set; }
        public string? Text { get; set; }
        public int Count { get; set; }
        public List<Label> Labels { get; set; } = [];
    }
    private sealed class Label { public int Id { get; set; } public int ProductId { get; set; } public Product Product { get; set; } = null!; public string? Text { get; set; } }
    private sealed class Owner { public int Id { get; set; } public string? Name { get; set; } public int? Version { get; set; } public Profile? Profile { get; set; } }
    private sealed class Profile { public int Id { get; set; } public string? Text { get; set; } public int? Version { get; set; } public Owner Owner { get; set; } = null!; }

    private static DataTable Table(Type[] types, params object?[][] rows)
    {
        var table = new DataTable();
        foreach (var type in types) table.Columns.Add("C" + table.Columns.Count, type);
        foreach (var row in rows) table.Rows.Add(row.Select(v => v ?? DBNull.Value).ToArray());
        return table;
    }
    private sealed record Command(string Sql, object?[] Inputs, DbTransaction? Transaction)
    {
        public string Table => Regex.Match(Sql.Replace("`", "").Replace("\"", ""), @"(?:INSERT INTO|UPDATE|DELETE FROM) (\w+)").Groups[1].Value;
    }
    private sealed class Commands : DbCommandInterceptor
    {
        public DataTable? Results { get; set; }
        public string? ZeroRowsFor { get; init; }
        public int? OutputValue { get; set; }
        public List<Command> Items { get; } = [];
        private void Capture(DbCommand command) => Items.Add(new Command(command.CommandText,
            command.Parameters.Cast<DbParameter>().Where(p => p.Direction == ParameterDirection.Input).Select(p => p.Value).ToArray(), command.Transaction));
        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData data, InterceptionResult<int> result)
        { Capture(command); return Items.Last().Table == ZeroRowsFor ? InterceptionResult<int>.SuppressWithResult(0) : result; }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<int> result, CancellationToken token = default) => ValueTask.FromResult(NonQueryExecuting(command, data, result));
        public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData data, int result)
        {
            if (OutputValue is { } value)
                foreach (DbParameter parameter in command.Parameters)
                    if (parameter.Direction == ParameterDirection.Output) parameter.Value = value;
            return result;
        }
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData data, int result,
            CancellationToken token = default) => ValueTask.FromResult(NonQueryExecuted(command, data, result));
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result)
        { Capture(command); return Results is null ? result : InterceptionResult<DbDataReader>.SuppressWithResult(Results.CreateDataReader()); }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken token = default) => ValueTask.FromResult(ReaderExecuting(command, data, result));
    }
}
