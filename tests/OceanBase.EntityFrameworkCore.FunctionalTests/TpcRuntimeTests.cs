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

public sealed class TpcRuntimeTests(ITestOutputHelper output)
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
    public async Task Union_materializes_concrete_root_leaf_and_sibling_with_nulls_and_empty_results(
        OceanBaseCompatibilityMode mode, bool async, QueryTrackingBehavior tracking)
    {
        var capture = new Commands();
        await using var context = Create(mode, capture);
        context.ChangeTracker.QueryTrackingBehavior = tracking;
        var query = context.Set<Asset>().OrderBy(a => a.Id);
        capture.Results = Table([typeof(int), typeof(string), typeof(int), typeof(int), typeof(int), typeof(int), typeof(int), typeof(string)],
            [101, "root", 1, null, null, null, null, "Asset"],
            [102, null, 1, 7, 3, null, null, "Document"],
            [103, "sensor", 1, null, null, 42, null, "Sensor"]);
        var rows = async ? await query.ToListAsync() : query.ToList();
        Assert.Equal(3, rows.Count);
        Assert.Equal("root", Assert.IsType<Asset>(rows[0]).Name);
        var document = Assert.IsType<Document>(rows[1]);
        Assert.Null(document.Name);
        Assert.Equal(3, document.Pages);
        Assert.Equal(7, document.Version);
        var sensor = Assert.IsType<Sensor>(rows[2]);
        Assert.Equal(42, sensor.Serial);
        Assert.Null(sensor.Reading);
        Assert.Null(sensor.Version);
        Assert.Equal(new[] { 101, 102, 103 }, rows.Select(r => r.Id));
        Assert.Equal(tracking == QueryTrackingBehavior.TrackAll ? 3 : 0, context.ChangeTracker.Entries().Count());
        Assert.All(context.ChangeTracker.Entries(), e => Assert.Equal(EntityState.Unchanged, e.State));
        var command = Assert.Single(capture.Items);
        Assert.Equal(2, Regex.Matches(command.Sql, "UNION ALL").Count);
        Assert.Contains("u.AssetId, u.Name, u.TenantId, u.Version, u.Pages, u.Serial, u.Reading, u.Discriminator", Unquote(command.Sql));
        Assert.Equal(new object?[] { 1 }, command.Inputs);
        capture.Results = Table([typeof(int), typeof(string), typeof(int), typeof(int), typeof(int), typeof(int), typeof(int), typeof(string)]);
        Assert.Empty(async ? await query.ToListAsync() : query.ToList());
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task OfType_abstract_type_and_type_predicates_keep_current_filters(OceanBaseCompatibilityMode mode, bool async)
    {
        var capture = new Commands();
        await using var context = Create(mode, capture);
        capture.Results = Table([typeof(int), typeof(string), typeof(int), typeof(int), typeof(int), typeof(int), typeof(string)],
            [101, "sensor", 1, null, 7, null, "Sensor"]);
        var sensors = context.Set<Asset>().OfType<Sensor>();
        var sensor = async ? await sensors.SingleAsync() : sensors.Single();
        Assert.Equal(7, sensor.Serial);
        Assert.Null(sensor.Reading);
        var sql = Assert.Single(capture.Items).Sql;
        Assert.DoesNotContain("UNION", sql);
        Assert.DoesNotContain("RootRows", sql);
        Assert.Contains("SensorRows", sql);
        Assert.Contains("AssetId", sql);

        capture.Items.Clear();
        context.ChangeTracker.Clear();
        capture.Results = Table([typeof(int), typeof(string), typeof(int), typeof(int), typeof(int), typeof(int)],
            [101, "sensor", 1, null, 7, null]);
        var equipment = context.Set<Equipment>();
        Assert.IsType<Sensor>(async ? await equipment.SingleAsync() : equipment.Single());
        Assert.DoesNotContain("Discriminator", Assert.Single(capture.Items).Sql);

        capture.Items.Clear();
        capture.Results = Table([typeof(int)], [101]);
        var ids = context.Set<Asset>().Where(a => a is Sensor).Select(a => a.Id);
        Assert.Equal(new[] { 101 }, async ? await ids.ToArrayAsync() : ids.ToArray());
        Assert.Contains("SensorRows", Assert.Single(capture.Items).Sql);
        Assert.Equal(new object?[] { 1 }, capture.Items[0].Inputs);
        Assert.DoesNotContain("DocumentRows", capture.Items[0].Sql);
        capture.Items.Clear();
        context.TenantId = 2;
        capture.Results = Table([typeof(int)], [202]);
        Assert.Equal(new[] { 202 }, async ? await ids.ToArrayAsync() : ids.ToArray());
        Assert.Equal(new object?[] { 2 }, Assert.Single(capture.Items).Inputs);
        capture.Items.Clear();
        var unfiltered = context.Set<Asset>().IgnoreQueryFilters().Where(a => a is Sensor).Select(a => a.Id);
        Assert.Equal(new[] { 202 }, async ? await unfiltered.ToArrayAsync() : unfiltered.ToArray());
        Assert.Empty(Assert.Single(capture.Items).Inputs);
        Assert.DoesNotContain("WHERE", capture.Items[0].Sql);
    }

    [Theory]
    [MemberData(nameof(TrackingModes))]
    public async Task Concrete_collection_include_keeps_optional_rows_and_fixup(OceanBaseCompatibilityMode mode, bool async,
        QueryTrackingBehavior tracking)
    {
        var capture = new Commands();
        await using var context = Create(mode, capture);
        context.ChangeTracker.QueryTrackingBehavior = tracking;
        var query = context.Set<Sensor>().Include(s => s.Attachments).OrderBy(s => s.Id);
        output.WriteLine(query.ToQueryString());
        capture.Results = Table([typeof(int), typeof(string), typeof(int), typeof(int), typeof(int), typeof(int), typeof(int), typeof(int), typeof(string)],
            [101, "sensor", 1, null, 7, 2, 11, 101, "one"],
            [101, "sensor", 1, null, 7, 2, 12, 101, "two"],
            [102, null, 1, null, 8, null, null, null, null]);
        var rows = async ? await query.ToListAsync() : query.ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "one", "two" }, rows[0].Attachments.Select(a => a.Text));
        Assert.All(rows[0].Attachments, a => Assert.Same(rows[0], a.Sensor));
        Assert.Empty(rows[1].Attachments);
        Assert.Null(rows[1].Reading);
        Assert.Equal(tracking == QueryTrackingBehavior.TrackAll ? 4 : 0, context.ChangeTracker.Entries().Count());
        var sql = Assert.Single(capture.Items).Sql;
        Assert.Contains("LEFT JOIN", sql);
        Assert.Contains("Attachments", sql);
        Assert.DoesNotContain("UNION", sql);
        Assert.DoesNotContain("RootRows", sql);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Assigned_keys_write_one_concrete_table_per_entity_and_commit_together(OceanBaseCompatibilityMode mode, bool async)
    {
        var capture = new Commands();
        await using var context = Create(mode, capture);
        Asset[] assets = [new Asset { Id = 101, Name = "root", TenantId = 1 },
            new Document { Id = 102, Name = "doc", TenantId = 1, Pages = 3, Version = 7 },
            new Sensor { Id = 103, Name = "sensor", TenantId = 1, Serial = 42, Reading = 2 }];
        context.AddRange(assets);
        Assert.All(assets, a => Assert.False(context.Entry(a).Property(r => r.Id).IsTemporary));
        Assert.Equal(3, async ? await context.SaveChangesAsync() : context.SaveChanges());
        Assert.Equal(new[] { "DocumentRows", "RootRows", "SensorRows" }, capture.Items.Select(c => c.Table).Order());
        Assert.All(capture.Items, c => Assert.StartsWith("INSERT INTO", c.Sql));
        foreach (var asset in assets) Assert.Contains(capture.Items, c => c.Inputs.Contains(asset.Id) && c.Inputs.Contains(asset.Name));
        Assert.DoesNotContain(capture.Items, c => c.Sql.Contains("LAST_INSERT_ID") || c.Sql.Contains("RETURNING"));
        AssertAutomaticCommit(Native(context), capture);
        capture.Items.Clear();
        foreach (var asset in assets) asset.Name = "changed";
        ((Sensor)assets[2]).Reading = null;
        Assert.Equal(3, async ? await context.SaveChangesAsync() : context.SaveChanges());
        Assert.All(capture.Items, c => Assert.StartsWith("UPDATE", c.Sql));
        Assert.Contains(capture.Items, c => c.Table == "SensorRows" && c.Inputs.Contains(DBNull.Value) && c.Sql.Contains("IS NULL"));
        Assert.Contains(capture.Items, c => c.Table == "DocumentRows" && c.Inputs.Contains(7));
        AssertAutomaticCommit(Native(context), capture);
        capture.Items.Clear();
        context.RemoveRange(assets);
        Assert.Equal(3, async ? await context.SaveChangesAsync() : context.SaveChanges());
        Assert.Equal(new[] { "DocumentRows", "RootRows", "SensorRows" }, capture.Items.Select(c => c.Table).Order());
        Assert.All(capture.Items, c => Assert.StartsWith("DELETE FROM", c.Sql));
        Assert.Empty(context.ChangeTracker.Entries());
        AssertAutomaticCommit(Native(context), capture);
        Assert.Equal(3, Native(context).Transactions.Count);
        Assert.Equal(ConnectionState.Closed, Native(context).State);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Concurrency_failure_retains_original_nullable_token_and_disposes_uncommitted_transaction(OceanBaseCompatibilityMode mode, bool async)
    {
        foreach (int? version in new int?[] { null, 7 })
        {
            var capture = new Commands { ZeroRowsFor = "SensorRows" };
            await using var context = Create(mode, capture);
            var sensor = new Sensor { Id = 103, Name = "old", TenantId = 1, Version = version, Serial = 42 };
            context.Attach(sensor);
            sensor.Name = "changed";
            sensor.Version = 8;
            context.Add(new Document { Id = 102, Name = "other", TenantId = 1 });
            var error = async ? await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => context.SaveChangesAsync())
                : Assert.Throws<DbUpdateConcurrencyException>(() => context.SaveChanges());
            Assert.Same(sensor, Assert.Single(error.Entries).Entity);
            Assert.Equal(EntityState.Modified, context.Entry(sensor).State);
            Assert.Equal(version, context.Entry(sensor).Property(a => a.Version).OriginalValue);
            var update = Assert.Single(capture.Items, c => c.Table == "SensorRows");
            Assert.StartsWith("UPDATE", update.Sql);
            if (version is null) Assert.Contains("Version IS NULL", Unquote(update.Sql));
            else Assert.Contains(7, update.Inputs);
            Assert.Contains(8, update.Inputs);
            Assert.Equal(version is null ? new object?[] { "changed", 8, 103 }
                : new object?[] { "changed", 8, 103, 7 }, update.Inputs);
            AssertUncommitted(context, capture);
        }
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Second_concrete_insert_failure_keeps_pending_entries_without_commit(OceanBaseCompatibilityMode mode, bool async)
    {
        var capture = new Commands();
        await using var context = Create(mode, capture);
        var failure = new InvalidOperationException("Synthetic second concrete insert failure.");
        Native(context).WriteFailure = failure;
        context.AddRange(new Asset { Id = 101 }, new Document { Id = 102 });
        var error = async ? await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync())
            : Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        Assert.Same(failure, error.InnerException);
        Assert.Equal(2, capture.Items.Count);
        Assert.All(context.ChangeTracker.Entries(), e => Assert.Equal(EntityState.Added, e.State));
        AssertUncommitted(context, capture);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Client_Guid_keys_are_unique_across_siblings_and_never_read_back(OceanBaseCompatibilityMode mode, bool async)
    {
        var capture = new Commands();
        await using var context = new GuidContext(Options(mode, capture));
        var first = new GuidAsset();
        var second = new GuidLeaf();
        context.AddRange(first, second);
        Assert.NotEqual(Guid.Empty, first.Id);
        Assert.NotEqual(Guid.Empty, second.Id);
        Assert.NotEqual(first.Id, second.Id);
        Assert.False(context.Entry(first).Property(a => a.Id).IsTemporary);
        Assert.Equal(2, async ? await context.SaveChangesAsync() : context.SaveChanges());
        Assert.Equal(2, capture.Items.Count);
        Assert.DoesNotContain(capture.Items, c => c.Sql.Contains("SELECT") || c.Sql.Contains("RETURNING"));
        Assert.All(capture.Items, c => Assert.Single(c.Inputs));
        AssertAutomaticCommit(Native(context), capture);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Computed_and_integer_converted_Guid_keys_reject_before_writes(OceanBaseCompatibilityMode mode, bool async)
    {
        foreach (var generation in new[] { "computed-never", "numeric-guid" })
        {
            var capture = new Commands();
            await using var context = new GuidContext(Options(mode, capture), generation);
            context.Add(new GuidLeaf());
            var error = async ? await Assert.ThrowsAsync<NotSupportedException>(() => context.SaveChangesAsync())
                : Assert.Throws<NotSupportedException>(() => context.SaveChanges());
            Assert.Contains("ValueGeneratedNever", error.Message);
            Assert.Empty(capture.Items);
            Assert.Empty(Native(context).Commands);
            Assert.Empty(Native(context).Transactions);
            Assert.Equal(ConnectionState.Closed, Native(context).State);
        }
    }

    private static void AssertAutomaticCommit(TransactionTests.NativeConnection native, Commands capture)
    {
        var transaction = native.Transactions.Last();
        Assert.Equal(1, transaction.Commits);
        Assert.Equal(1, transaction.Disposals);
        Assert.All(capture.Items, c => Assert.Same(transaction, c.Transaction));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Assigned_composite_keys_are_bound_in_leaf_insert_update_and_delete(OceanBaseCompatibilityMode mode, bool async)
    {
        var capture = new Commands();
        await using var context = new CompositeContext(Options(mode, capture));
        var leaf = new CompositeLeaf { Id = 101, TenantId = 2, Value = 3 };
        context.Add(leaf);
        Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
        Assert.Equal(new object?[] { 101, 2, 3 }, Assert.Single(capture.Items).Inputs);
        Assert.Equal("CompositeLeaves", capture.Items[0].Table);
        Assert.DoesNotContain("RETURNING", capture.Items[0].Sql);
        capture.Items.Clear();
        leaf.Value = 4;
        Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
        Assert.Equal(new object?[] { 4, 101, 2 }, Assert.Single(capture.Items).Inputs);
        Assert.Contains("TenantId", capture.Items[0].Sql);
        capture.Items.Clear();
        context.Remove(leaf);
        Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
        Assert.Equal(new object?[] { 101, 2 }, Assert.Single(capture.Items).Inputs);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Empty(Native(context).Transactions);
    }
    private static void AssertUncommitted(DbContext context, Commands capture)
    {
        var transaction = Assert.Single(Native(context).Transactions);
        Assert.Equal(0, transaction.Commits);
        Assert.Equal(1, transaction.Disposals);
        Assert.True(transaction.DisposedActive);
        Assert.All(capture.Items, c => Assert.Same(transaction, c.Transaction));
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Equal(ConnectionState.Closed, Native(context).State);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Independent_identity_generation_rejects_before_existing_schema_writes(OceanBaseCompatibilityMode mode, bool async)
    {
        var capture = new Commands();
        await using var context = Create(mode, capture, generated: true);
        context.Add(new Ordinary { Id = 1 });
        context.Add(new Sensor { Name = "unsafe", TenantId = 1, Serial = 7 });
        var error = async ? await Assert.ThrowsAsync<NotSupportedException>(() => context.SaveChangesAsync())
            : Assert.Throws<NotSupportedException>(() => context.SaveChanges());
        Assert.Contains("ValueGeneratedNever", error.Message);
        Assert.Contains("Guid", error.Message);
        Assert.Empty(capture.Items);
        Assert.Empty(Native(context).Commands);
        Assert.Empty(Native(context).Transactions);
        Assert.Equal(ConnectionState.Closed, Native(context).State);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql)]
    [InlineData(OceanBaseCompatibilityMode.Oracle)]
    public void Hierarchy_query_shapes_keep_unions_typed_columns_and_concrete_targets(OceanBaseCompatibilityMode mode)
    {
        using var context = Create(mode, new Commands());
        var all = context.Set<Asset>().OrderBy(a => a.Id).ToQueryString();
        var sensor = context.Set<Asset>().OfType<Sensor>().ToQueryString();
        var middle = context.Set<Equipment>().ToQueryString();
        output.WriteLine(all);
        output.WriteLine(sensor);
        output.WriteLine(middle);
        Assert.Equal(2, Regex.Matches(all, "UNION ALL").Count);
        Assert.Contains("RootRows", all);
        Assert.Contains("SensorRows", all);
        Assert.Contains("DocumentRows", all);
        Assert.DoesNotContain("EquipmentRows", all);
        Assert.DoesNotContain("UNION", sensor);
        Assert.DoesNotContain("RootRows", sensor);
        Assert.Contains("SensorRows", sensor);
        Assert.DoesNotContain("DocumentRows", middle);
    }

    private static HierarchyContext Create(OceanBaseCompatibilityMode mode, Commands capture, bool generated = false)
        => new(Options(mode, capture), generated);
    private static DbContextOptions Options(OceanBaseCompatibilityMode mode, Commands capture)
        => new DbContextOptionsBuilder().UseOceanBase("Server=offline;Database=app", mode)
            .ReplaceService<IRelationalConnection, TransactionTests.TestConnection>()
            .EnableServiceProviderCaching(false).AddInterceptors(capture).Options;
    private static TransactionTests.NativeConnection Native(DbContext context)
        => Assert.IsType<TransactionTests.NativeConnection>(context.Database.GetDbConnection());

    private sealed class HierarchyContext(DbContextOptions options, bool generated) : DbContext(options)
    {
        public int TenantId { get; set; } = 1;
        protected override void OnModelCreating(ModelBuilder model)
        {
            var root = model.Entity<Asset>();
            root.UseTpcMappingStrategy().ToTable("RootRows");
            root.Property(a => a.Id).HasColumnName("AssetId");
            if (generated) root.Property(a => a.Id).ValueGeneratedOnAdd();
            else root.Property(a => a.Id).ValueGeneratedNever();
            root.Property(a => a.Name).HasMaxLength(40);
            root.Property(a => a.Version).IsConcurrencyToken();
            root.HasQueryFilter(a => a.TenantId == TenantId);
            model.Entity<Equipment>();
            model.Entity<Sensor>().ToTable("SensorRows");
            model.Entity<Document>().ToTable("DocumentRows");
            model.Entity<Attachment>().ToTable("Attachments").Property(a => a.Text).HasMaxLength(40);
            model.Entity<Attachment>().HasOne(a => a.Sensor).WithMany(s => s.Attachments).HasForeignKey(a => a.SensorId);
            model.Entity<Ordinary>().ToTable("AAAOrdinary").Property(a => a.Id).ValueGeneratedNever();
        }
    }
    private class Asset { public int Id { get; set; } public string? Name { get; set; } public int TenantId { get; set; } public int? Version { get; set; } }
    private abstract class Equipment : Asset { public int Serial { get; set; } }
    private sealed class Sensor : Equipment { public int? Reading { get; set; } public List<Attachment> Attachments { get; set; } = []; }
    private sealed class Document : Asset { public int Pages { get; set; } }
    private sealed class Attachment { public int Id { get; set; } public int SensorId { get; set; } public Sensor Sensor { get; set; } = null!; public string? Text { get; set; } }
    private sealed class Ordinary { public int Id { get; set; } }

    private sealed class GuidContext(DbContextOptions options, string generation = "client") : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<GuidAsset>().UseTpcMappingStrategy().ToTable("GuidAssets");
            if (generation == "computed-never") model.Entity<GuidAsset>().Property(a => a.Id).HasComputedColumnSql("42").ValueGeneratedNever();
            if (generation == "numeric-guid") model.Entity<GuidAsset>().Property(a => a.Id).HasConversion(v => (long)v.GetHashCode(), v => Guid.Empty);
            model.Entity<GuidLeaf>().ToTable("GuidLeaves");
        }
    }
    private class GuidAsset { public Guid Id { get; set; } }
    private sealed class GuidLeaf : GuidAsset;
    private sealed class CompositeContext(DbContextOptions options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<CompositeRoot>().UseTpcMappingStrategy().ToTable("CompositeRoots");
            model.Entity<CompositeRoot>().HasKey(a => new { a.Id, a.TenantId });
            model.Entity<CompositeRoot>().Property(a => a.Id).ValueGeneratedNever();
            model.Entity<CompositeLeaf>().ToTable("CompositeLeaves");
        }
    }
    private class CompositeRoot { public int Id { get; set; } public int TenantId { get; set; } }
    private sealed class CompositeLeaf : CompositeRoot { public int Value { get; set; } }

    private static DataTable Table(Type[] types, params object?[][] rows)
    {
        var table = new DataTable();
        foreach (var type in types) table.Columns.Add("C" + table.Columns.Count, type);
        foreach (var row in rows) table.Rows.Add(row.Select(v => v ?? DBNull.Value).ToArray());
        return table;
    }

    private static string Unquote(string sql) => sql.Replace("`", "").Replace("\"", "");
    private sealed record Command(string Sql, object?[] Inputs, DbTransaction? Transaction)
    {
        public string Table => Regex.Match(Unquote(Sql), @"(?:INSERT INTO|UPDATE|DELETE FROM) (\w+)").Groups[1].Value;
    }
    private sealed class Commands : DbCommandInterceptor
    {
        public DataTable? Results { get; set; }
        public string? ZeroRowsFor { get; init; }
        public List<Command> Items { get; } = [];
        private void Capture(DbCommand command)
        {
            var parameters = command.Parameters.Cast<DbParameter>().ToArray();
            var position = 0;
            var used = new HashSet<string>(StringComparer.Ordinal);
            var inputs = new List<object?>();
            foreach (Match bind in Regex.Matches(command.CommandText, @"[:@]\w+|\?"))
            {
                var parameter = bind.Value == "?" ? parameters[position++]
                    : parameters.Single(p => p.ParameterName.TrimStart(':', '@') == bind.Value.TrimStart(':', '@'));
                used.Add(parameter.ParameterName);
                if (parameter.Direction == ParameterDirection.Input) inputs.Add(parameter.Value);
            }
            Assert.Equal(parameters.Length, used.Count);
            Items.Add(new Command(command.CommandText, inputs.ToArray(), command.Transaction));
        }
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
