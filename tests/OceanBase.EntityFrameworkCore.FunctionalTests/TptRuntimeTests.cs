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

public sealed class TptRuntimeTests(ITestOutputHelper output)
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
    public async Task Hierarchy_queries_materialize_leaf_intermediate_and_sibling_types(
        OceanBaseCompatibilityMode mode, bool async, QueryTrackingBehavior tracking)
    {
        var capture = new Commands();
        await using var context = Create(mode, capture);
        context.ChangeTracker.QueryTrackingBehavior = tracking;
        var query = context.Set<Asset>().OrderBy(a => a.Id);
        output.WriteLine(query.ToQueryString());
        capture.Results = Table([typeof(int), typeof(string), typeof(int), typeof(int), typeof(string), typeof(int), typeof(string), typeof(string)],
            [101, "sensor", 1, 7, "serial", null, null, "Sensor"],
            [102, null, 1, null, null, 3, null, "Document"],
            [103, "device", 1, null, "other", null, null, "Device"]);
        var rows = async ? await query.ToListAsync() : query.ToList();
        Assert.Equal(3, rows.Count);
        var sensor = Assert.IsType<Sensor>(rows[0]);
        Assert.Equal(101, sensor.Id);
        Assert.Equal("serial", sensor.Serial);
        Assert.Null(sensor.Unit);
        var document = Assert.IsType<Document>(rows[1]);
        Assert.Null(document.Name);
        Assert.Equal(3, document.Pages);
        Assert.IsType<Device>(rows[2]);
        Assert.Equal(tracking == QueryTrackingBehavior.TrackAll ? 3 : 0, context.ChangeTracker.Entries().Count());
        Assert.All(context.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
        Assert.Contains("LEFT JOIN", capture.Items.Single().Sql);
        Assert.Contains("CASE", capture.Items.Single().Sql);
        Assert.Equal(1, Assert.Single(capture.Items).Inputs.Single());
        Assert.Contains("d0.Pages, s.Unit, CASE", Unquote(capture.Items[0].Sql));

        capture.Results = Table([typeof(int), typeof(string), typeof(int), typeof(int), typeof(string), typeof(int), typeof(string), typeof(string)]);
        Assert.Empty(async ? await query.ToListAsync() : query.ToList());
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Derived_OfType_type_tests_and_inherited_filters_keep_current_values(OceanBaseCompatibilityMode mode, bool async)
    {
        var capture = new Commands();
        await using var context = Create(mode, capture);
        var sensors = context.Set<Asset>().OfType<Sensor>();
        capture.Results = Table([typeof(int), typeof(string), typeof(int), typeof(int), typeof(string), typeof(string)],
            [101, "sensor", 1, null, "serial", "C"]);
        var sensor = async ? await sensors.SingleAsync() : sensors.Single();
        Assert.Equal("C", sensor.Unit);
        Assert.Equal("serial", sensor.Serial);
        Assert.Contains("LEFT JOIN", Assert.Single(capture.Items).Sql);
        Assert.Contains("IS NOT NULL", capture.Items[0].Sql);
        Assert.DoesNotContain("DocumentRows", capture.Items[0].Sql);

        capture.Items.Clear();
        var ids = context.Set<Asset>().Where(a => a is Sensor).Select(a => a.Id);
        capture.Results = Table([typeof(int)], [101]);
        Assert.Equal(new[] { 101 }, async ? await ids.ToArrayAsync() : ids.ToArray());
        Assert.Contains("SensorRows", Assert.Single(capture.Items).Sql);
        Assert.Contains("IS NOT NULL", capture.Items[0].Sql);
        Assert.Equal(new object?[] { 1 }, capture.Items[0].Inputs);
        capture.Items.Clear();
        context.TenantId = 2;
        capture.Results = Table([typeof(int)], [202]);
        Assert.Equal(new[] { 202 }, async ? await ids.ToArrayAsync() : ids.ToArray());
        Assert.Equal(new object?[] { 2 }, Assert.Single(capture.Items).Inputs);
        capture.Items.Clear();
        var unfiltered = context.Set<Asset>().IgnoreQueryFilters().Where(a => a is Sensor).Select(a => a.Id);
        Assert.Equal(new[] { 202 }, async ? await unfiltered.ToArrayAsync() : unfiltered.ToArray());
        Assert.Empty(Assert.Single(capture.Items).Inputs);
        Assert.DoesNotContain("TenantId", capture.Items[0].Sql);
    }

    [Theory]
    [MemberData(nameof(TrackingModes))]
    public async Task Derived_collection_include_preserves_hierarchy_and_fixup(OceanBaseCompatibilityMode mode, bool async,
        QueryTrackingBehavior tracking)
    {
        var capture = new Commands();
        await using var context = Create(mode, capture);
        context.ChangeTracker.QueryTrackingBehavior = tracking;
        var query = context.Set<Sensor>().Include(s => s.Attachments).OrderBy(s => s.Id);
        output.WriteLine(query.ToQueryString());
        capture.Results = Table([typeof(int), typeof(string), typeof(int), typeof(int), typeof(string), typeof(string),
            typeof(int), typeof(int), typeof(string)],
            [101, "sensor", 1, null, "serial", "C", 11, 101, "one"],
            [101, "sensor", 1, null, "serial", "C", 12, 101, "two"],
            [102, "empty", 1, null, "empty-serial", null, null, null, null]);
        var rows = async ? await query.ToListAsync() : query.ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "one", "two" }, rows[0].Attachments.Select(a => a.Text));
        Assert.All(rows[0].Attachments, a => Assert.Same(rows[0], a.Asset));
        Assert.Empty(rows[1].Attachments);
        Assert.Equal(tracking == QueryTrackingBehavior.TrackAll ? 4 : 0, context.ChangeTracker.Entries().Count());
        var sql = Assert.Single(capture.Items).Sql;
        Assert.Contains("LEFT JOIN", sql);
        Assert.Contains("AttachmentRows", sql);
        Assert.Contains("INNER JOIN", sql);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Multi_table_leaf_save_propagates_one_key_and_uses_an_automatic_transaction(OceanBaseCompatibilityMode mode, bool async)
    {
        var capture = new Commands();
        await using var context = Create(mode, capture);
        var native = Assert.IsType<TransactionTests.NativeConnection>(context.Database.GetDbConnection());
        var sensor = new Sensor { Name = "new", TenantId = 1, Serial = "serial", Unit = "C", Version = 7 };
        context.Add(sensor);
        Assert.True(context.Entry(sensor).Property(s => s.Id).IsTemporary);
        Assert.Equal(3, async ? await context.SaveChangesAsync() : context.SaveChanges());
        Assert.Equal(101, sensor.Id);
        Assert.Equal(EntityState.Unchanged, context.Entry(sensor).State);
        var inserts = capture.Items.Where(c => c.Sql.StartsWith("INSERT", StringComparison.Ordinal)).ToArray();
        Assert.Equal(new[] { "AssetRows", "DeviceRows", "SensorRows" }, inserts.Select(c => c.Table));
        Assert.DoesNotContain(101, inserts[0].Inputs);
        Assert.Contains(101, inserts[1].Inputs);
        Assert.Contains(101, inserts[2].Inputs);
        Assert.Equal(mode == OceanBaseCompatibilityMode.MySql ? 1 : 0, capture.Items.Count(c => c.Sql.Contains("LAST_INSERT_ID")));
        Assert.Equal(mode == OceanBaseCompatibilityMode.Oracle ? 1 : 0, inserts.Count(c => c.Sql.Contains("RETURNING")));
        AssertAutomaticCommit(native, capture);

        capture.Items.Clear();
        sensor.Name = "changed";
        sensor.Unit = null;
        Assert.Equal(2, async ? await context.SaveChangesAsync() : context.SaveChanges());
        Assert.Equal(new[] { "AssetRows", "SensorRows" }, capture.Items.Select(c => c.Table).OrderBy(n => n));
        Assert.Contains(capture.Items, c => c.Inputs.Contains("changed") && c.Inputs.Contains(7));
        Assert.Contains(capture.Items, c => c.Inputs.Contains(DBNull.Value));
        AssertAutomaticCommit(native, capture);

        capture.Items.Clear();
        context.Remove(sensor);
        Assert.Equal(3, async ? await context.SaveChangesAsync() : context.SaveChanges());
        Assert.Equal(new[] { "SensorRows", "DeviceRows", "AssetRows" }, capture.Items.Select(c => c.Table));
        Assert.Empty(context.ChangeTracker.Entries());
        AssertAutomaticCommit(native, capture);
        Assert.Equal(3, native.Transactions.Count);
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Equal(ConnectionState.Closed, native.State);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Derived_insert_failure_keeps_the_entry_and_disposes_the_uncommitted_transaction(OceanBaseCompatibilityMode mode, bool async)
    {
        var capture = new Commands();
        await using var context = Create(mode, capture);
        var native = Assert.IsType<TransactionTests.NativeConnection>(context.Database.GetDbConnection());
        var error = new InvalidOperationException("Synthetic derived-table failure.");
        native.WriteFailure = error;
        var sensor = new Sensor { Name = "new", Serial = "serial", Unit = "C", TenantId = 1 };
        context.Add(sensor);
        var failure = async ? await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync())
            : Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        Assert.Same(error, failure.InnerException);
        Assert.Same(sensor, Assert.Single(failure.Entries).Entity);
        Assert.Equal(EntityState.Added, context.Entry(sensor).State);
        Assert.True(context.Entry(sensor).Property(s => s.Id).IsTemporary);
        var transaction = Assert.Single(native.Transactions);
        Assert.Equal(0, transaction.Commits);
        Assert.Equal(1, transaction.Disposals);
        Assert.True(transaction.DisposedActive);
        Assert.DoesNotContain(capture.Items, c => c.Table == "SensorRows");
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Equal(ConnectionState.Closed, native.State);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Inherited_concurrency_failure_keeps_original_values_and_does_not_commit(OceanBaseCompatibilityMode mode, bool async)
    {
        var capture = new Commands { ZeroRowsFor = "AssetRows" };
        await using var context = Create(mode, capture);
        var native = Assert.IsType<TransactionTests.NativeConnection>(context.Database.GetDbConnection());
        var sensor = new Sensor { Id = 101, TenantId = 1, Version = 7, Name = "old", Serial = "serial", Unit = "C" };
        context.Attach(sensor);
        sensor.Name = "changed";
        sensor.Unit = "F";
        var error = async ? await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => context.SaveChangesAsync())
            : Assert.Throws<DbUpdateConcurrencyException>(() => context.SaveChanges());
        Assert.Same(sensor, Assert.Single(error.Entries).Entity);
        Assert.Equal(EntityState.Modified, context.Entry(sensor).State);
        Assert.Equal(7, context.Entry(sensor).Property(s => s.Version).OriginalValue);
        Assert.Contains(capture.Items, c => c.Table == "AssetRows" && c.Inputs.Contains(7) && c.Sql.Contains("WHERE"));
        var transaction = Assert.Single(native.Transactions);
        Assert.Equal(0, transaction.Commits);
        Assert.Equal(1, transaction.Disposals);
        Assert.True(transaction.DisposedActive);
        Assert.All(capture.Items, c => Assert.Same(transaction, c.Transaction));
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Equal(ConnectionState.Closed, native.State);
    }

    private static string Unquote(string sql) => sql.Replace("`", "").Replace("\"", "");

    private static void AssertAutomaticCommit(TransactionTests.NativeConnection native, Commands capture)
    {
        var transaction = native.Transactions.Last();
        Assert.Equal(1, transaction.Commits);
        Assert.Equal(1, transaction.Disposals);
        Assert.All(capture.Items, command => Assert.Same(transaction, command.Transaction));
    }

    private static HierarchyContext Create(OceanBaseCompatibilityMode mode, Commands capture)
        => new(new DbContextOptionsBuilder<HierarchyContext>().UseOceanBase("Server=offline;Database=app", mode)
            .ReplaceService<IRelationalConnection, TransactionTests.TestConnection>()
            .EnableServiceProviderCaching(false).AddInterceptors(capture).Options);

    private sealed class HierarchyContext(DbContextOptions<HierarchyContext> options) : DbContext(options)
    {
        public int TenantId { get; set; } = 1;
        protected override void OnModelCreating(ModelBuilder model)
        {
            var root = model.Entity<Asset>();
            root.UseTptMappingStrategy().ToTable("AssetRows");
            root.Property(a => a.Name).HasMaxLength(40);
            root.Property(a => a.Version).IsConcurrencyToken();
            root.HasQueryFilter(a => a.TenantId == TenantId);
            model.Entity<Device>().ToTable("DeviceRows").Property(d => d.Serial).HasMaxLength(40);
            model.Entity<Sensor>().ToTable("SensorRows").Property(s => s.Unit).HasMaxLength(12);
            model.Entity<Document>().ToTable("DocumentRows");
            model.Entity<Attachment>().ToTable("AttachmentRows").Property(a => a.Text).HasMaxLength(40);
            model.Entity<Attachment>().HasOne(a => a.Asset).WithMany(a => a.Attachments).HasForeignKey(a => a.AssetId);
        }
    }

    private abstract class Asset { public int Id { get; set; } public string? Name { get; set; } public int TenantId { get; set; } public int? Version { get; set; } public List<Attachment> Attachments { get; set; } = []; }
    private class Device : Asset { public string? Serial { get; set; } }
    private sealed class Sensor : Device { public string? Unit { get; set; } }
    private sealed class Document : Asset { public int Pages { get; set; } }
    private sealed class Attachment { public int Id { get; set; } public int AssetId { get; set; } public Asset Asset { get; set; } = null!; public string? Text { get; set; } }

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
        public List<Command> Items { get; } = [];
        private void Capture(DbCommand command) => Items.Add(new Command(command.CommandText,
            command.Parameters.Cast<DbParameter>().Where(p => p.Direction == ParameterDirection.Input).Select(p => p.Value).ToArray(), command.Transaction));
        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData data, InterceptionResult<int> result)
        { Capture(command); return Items.Last().Table == ZeroRowsFor ? InterceptionResult<int>.SuppressWithResult(0) : result; }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<int> result, CancellationToken token = default) => ValueTask.FromResult(NonQueryExecuting(command, data, result));
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result)
        { Capture(command); return Results is null ? result : InterceptionResult<DbDataReader>.SuppressWithResult(Results.CreateDataReader()); }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken token = default) => ValueTask.FromResult(ReaderExecuting(command, data, result));
    }
}
