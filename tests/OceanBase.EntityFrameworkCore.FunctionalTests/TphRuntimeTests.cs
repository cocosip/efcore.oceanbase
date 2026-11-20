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
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;
using Xunit.Abstractions;

namespace OceanBase.EntityFrameworkCore.FunctionalTests
{
    public sealed class TphRuntimeTests(ITestOutputHelper output)
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
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Model_and_create_script_retain_one_table_and_converted_discriminator(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode, new Commands());
            var root = context.Model.FindEntityType(typeof(Asset))!;
            var table = context.Model.GetRelationalModel().Tables.Single(t => t.Name == "TphAssets");
            Assert.Equal(4, table.EntityTypeMappings.Count());
            Assert.Equal("TPH", root.GetMappingStrategy());
            Assert.True(root.GetIsDiscriminatorMappingComplete());
            var kind = root.FindDiscriminatorProperty()!;
            Assert.True(kind.IsShadowProperty());
            Assert.Equal(typeof(AssetKind), kind.ClrType);
            Assert.Equal(typeof(string), kind.GetTypeMapping().Converter!.ProviderClrType);
            Assert.Equal(16, kind.GetMaxLength());
            Assert.False(table.FindColumn("Kind")!.IsNullable);
            Assert.True(table.FindColumn("Serial")!.IsNullable);
            Assert.True(table.FindColumn("Pages")!.IsNullable);
            Assert.True(table.FindColumn("Unit")!.IsNullable);
            Assert.Equal("AssetId", Assert.Single(table.PrimaryKey!.Columns).Name);
            Assert.Equal(DeleteBehavior.Cascade, Assert.Single(context.Model.FindEntityType(typeof(Attachment))!.GetForeignKeys()).DeleteBehavior);
            var script = Unquote(context.Database.GenerateCreateScript());
            Assert.Single(Regex.Matches(script, @"CREATE TABLE TphAssets\b"));
            Assert.Contains("Kind", script);
            Assert.Contains("AttachmentRows", script);
            Assert.DoesNotContain("CREATE TABLE Sensor", script);
        }

        [Theory]
        [MemberData(nameof(TrackingModes))]
        public async Task Root_queries_materialize_all_concrete_types_and_nulls(
            OceanBaseCompatibilityMode mode, bool async, QueryTrackingBehavior tracking)
        {
            var commands = new Commands { Results = HierarchyRows() };
            await using var context = Create(mode, commands);
            context.ChangeTracker.QueryTrackingBehavior = tracking;
            var query = context.Set<Asset>().OrderBy(a => a.Id);
            output.WriteLine(query.ToQueryString());
            var rows = async ? await query.ToListAsync() : query.ToList();
            Assert.Equal(4, rows.Count);
            Assert.Equal("root", Assert.IsType<Asset>(rows[0]).Name);
            Assert.Equal("device-serial", Assert.IsType<Device>(rows[1]).Serial);
            var sensor = Assert.IsType<Sensor>(rows[2]);
            Assert.Equal("sensor-serial", sensor.Serial);
            Assert.Null(sensor.Unit);
            Assert.Null(sensor.Version);
            var document = Assert.IsType<Document>(rows[3]);
            Assert.Null(document.Name);
            Assert.Equal(9, document.Pages);
            Assert.Equal(tracking == QueryTrackingBehavior.TrackAll ? 4 : 0, context.ChangeTracker.Entries().Count());
            Assert.All(context.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
            var sql = Assert.Single(commands.Items);
            Assert.Contains("t.AssetId, t.Kind, t.Name, t.TenantId, t.Version, t.Serial, t.Pages, t.Unit", sql.Text);
            Assert.DoesNotContain("JOIN", sql.Text);
            Assert.DoesNotContain("UNION", sql.Text);
            Assert.Contains("WHERE t.TenantId = ? ORDER BY t.AssetId", sql.Template);
            Assert.Equal(new object?[] { 1 }, sql.Inputs);
            commands.Results = HierarchyRows(empty: true);
            Assert.Empty(async ? await query.ToListAsync() : query.ToList());
            AssertClean(context, commands);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Derived_type_predicates_and_inherited_filters_keep_current_bindings(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new Commands();
            await using var context = Create(mode, commands);
            var sensorQuery = context.Set<Asset>().OfType<Sensor>();
            output.WriteLine(sensorQuery.ToQueryString());
            commands.Results = Table([typeof(int), typeof(string), typeof(string), typeof(int), typeof(int), typeof(string), typeof(string)],
                [103, "Sensor", "sensor", 1, null, "serial", "C"]);
            var sensor = async ? await sensorQuery.SingleAsync() : sensorQuery.Single();
            Assert.Equal("serial", sensor.Serial);
            Assert.Equal("C", sensor.Unit);
            var sql = Assert.Single(commands.Items);
            Assert.Contains("Kind = " + KindLiteral(mode, "Sensor"), sql.Text);
            Assert.Contains("t.TenantId = ?", sql.Template);
            Assert.DoesNotContain("Pages", sql.Text);
            Assert.DoesNotContain("JOIN", sql.Text);
            commands.Items.Clear();
            commands.Results = Table([typeof(int)], [103]);
            var ids = context.Set<Asset>().Where(a => a is Device).OrderBy(a => a.Id).Select(a => a.Id);
            Assert.Equal(new[] { 103 }, async ? await ids.ToArrayAsync() : ids.ToArray());
            sql = Assert.Single(commands.Items);
            Assert.Contains("Kind IN (" + KindLiteral(mode, "Device") + ", " + KindLiteral(mode, "Sensor") + ")", sql.Text);
            Assert.Contains("t.TenantId = ?", sql.Template);
            Assert.Contains("ORDER BY t.AssetId", sql.Template);
            Assert.Equal(new object?[] { 1 }, sql.Inputs);
            commands.Items.Clear();
            context.TenantId = 2;
            Assert.Equal(new[] { 103 }, async ? await ids.ToArrayAsync() : ids.ToArray());
            sql = Assert.Single(commands.Items);
            Assert.Contains("t.TenantId = ?", sql.Template);
            Assert.Equal(new object?[] { 2 }, sql.Inputs);
            commands.Items.Clear();
            var unfiltered = context.Set<Asset>().IgnoreQueryFilters().Where(a => a is Device).OrderBy(a => a.Id).Select(a => a.Id);
            Assert.Equal(new[] { 103 }, async ? await unfiltered.ToArrayAsync() : unfiltered.ToArray());
            sql = Assert.Single(commands.Items);
            Assert.Empty(sql.Inputs);
            Assert.DoesNotContain("TenantId", sql.Text);
            Assert.Contains("Kind IN (", sql.Text);
            AssertClean(context, commands);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Derived_scalar_paging_retains_type_filter_and_dialect_parameter_order(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new Commands { Results = Table([typeof(int)], [103]) };
            await using var context = Create(mode, commands);
            var query = context.Set<Asset>().OfType<Device>().OrderBy(a => a.Id).Skip(1).Take(2).Select(a => a.Id);
            Assert.Equal(new[] { 103 }, async ? await query.ToArrayAsync() : query.ToArray());
            var command = Assert.Single(commands.Items);
            Assert.Contains("Kind IN (" + KindLiteral(mode, "Device") + ", " + KindLiteral(mode, "Sensor") + ")", command.Text);
            Assert.Contains("t.TenantId = ?", command.Template);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "ORDER BY t.AssetId LIMIT ? OFFSET ?"
                : "ORDER BY t.AssetId OFFSET ? ROWS FETCH NEXT ? ROWS ONLY", command.Template);
            Assert.Equal(mode == OceanBaseCompatibilityMode.MySql ? new object?[] { 1, 2, 1 } : [1, 1, 2], command.Inputs);
            AssertClean(context, commands);
        }

        [Theory]
        [MemberData(nameof(TrackingModes))]
        public async Task Repeated_leaf_in_join_obeys_tracking_and_identity_resolution(
            OceanBaseCompatibilityMode mode, bool async, QueryTrackingBehavior tracking)
        {
            var commands = new Commands();
            await using var context = Create(mode, commands);
            context.ChangeTracker.QueryTrackingBehavior = tracking;
            var query = context.Set<Sensor>().Join(context.Set<Asset>(), left => left.Id, right => right.Id,
                (left, right) => new { Left = left, Right = right });
            output.WriteLine(query.ToQueryString());
            commands.Results = Table([typeof(int), typeof(string), typeof(string), typeof(int), typeof(int), typeof(string), typeof(string),
                typeof(int), typeof(string), typeof(string), typeof(int), typeof(int), typeof(string), typeof(int), typeof(string)],
                [103, "Sensor", "sensor", 1, 7, "serial", "C", 103, "Sensor", "sensor", 1, 7, "serial", null, "C"]);
            var pair = async ? await query.SingleAsync() : query.Single();
            Assert.IsType<Sensor>(pair.Right);
            Assert.Equal(103, pair.Left.Id);
            Assert.Equal("serial", ((Sensor)pair.Right).Serial);
            Assert.Equal(tracking != QueryTrackingBehavior.NoTracking, ReferenceEquals(pair.Left, pair.Right));
            Assert.Equal(tracking == QueryTrackingBehavior.TrackAll ? 1 : 0, context.ChangeTracker.Entries().Count());
            var command = Assert.Single(commands.Items);
            Assert.Contains("INNER JOIN", command.Text);
            Assert.Contains("Kind = " + KindLiteral(mode, "Sensor"), command.Text);
            Assert.Contains("ON t.AssetId = t1.AssetId", command.Template);
            Assert.Contains("WHERE t0.TenantId = ?", command.Template);
            Assert.Contains("t.TenantId = ?", command.Template);
            Assert.Equal(new object?[] { 1, 1 }, command.Inputs);
            AssertClean(context, commands);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Incomplete_mapping_filters_root_queries_to_known_discriminators(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new Commands { Results = HierarchyRows() };
            await using var context = Create(mode, commands, incomplete: true);
            var query = context.Set<Asset>().IgnoreQueryFilters().OrderBy(a => a.Id);
            Assert.Equal(4, (async ? await query.ToListAsync() : query.ToList()).Count);
            var sql = Assert.Single(commands.Items);
            Assert.Contains("Kind IN (", sql.Text);
            var discriminators = Regex.Match(sql.Text, @"Kind IN \(([^)]+)\)").Groups[1].Value.Split(", ");
            Assert.Equal(new[] { "Root", "Device", "Sensor", "Document" }.Select(kind => KindLiteral(mode, kind)).OrderBy(value => value),
                discriminators.OrderBy(value => value));
            Assert.Empty(sql.Inputs);
            Assert.False(context.Model.FindEntityType(typeof(Asset))!.GetIsDiscriminatorMappingComplete());
            AssertClean(context, commands);
        }

        [Theory]
        [MemberData(nameof(TrackingModes))]
        public async Task Leaf_collection_include_keeps_identity_and_fixup_with_missing_children(
            OceanBaseCompatibilityMode mode, bool async, QueryTrackingBehavior tracking)
        {
            var commands = new Commands();
            await using var context = Create(mode, commands);
            context.ChangeTracker.QueryTrackingBehavior = tracking;
            var query = context.Set<Sensor>().Include(s => s.Attachments).OrderBy(s => s.Id);
            output.WriteLine(query.ToQueryString());
            commands.Results = Table([typeof(int), typeof(string), typeof(string), typeof(int), typeof(int), typeof(string), typeof(string),
                typeof(int), typeof(int), typeof(string)],
                [103, "Sensor", "sensor", 1, null, "serial", "C", 11, 103, "one"],
                [103, "Sensor", "sensor", 1, null, "serial", "C", 12, 103, null],
                [104, "Sensor", "empty", 1, 7, "other", null, null, null, null]);
            var rows = async ? await query.ToListAsync() : query.ToList();
            Assert.Equal(2, rows.Count);
            Assert.Equal(new string?[] { "one", null }, rows[0].Attachments.Select(a => a.Text));
            Assert.All(rows[0].Attachments, a => Assert.Same(rows[0], a.Sensor));
            Assert.Empty(rows[1].Attachments);
            Assert.Equal(tracking == QueryTrackingBehavior.TrackAll ? 4 : 0, context.ChangeTracker.Entries().Count());
            var sql = Assert.Single(commands.Items);
            Assert.Contains(mode == OceanBaseCompatibilityMode.Oracle ? "LEFT JOIN AttachmentRows a ON t.AssetId = a.SensorId"
                : "LEFT JOIN AttachmentRows AS a ON t.AssetId = a.SensorId", sql.Template);
            Assert.Contains("Kind = " + KindLiteral(mode, "Sensor"), sql.Text);
            Assert.Contains("t.TenantId = ?", sql.Template);
            Assert.Contains("ORDER BY t.AssetId", sql.Template);
            Assert.Equal(new object?[] { 1 }, sql.Inputs);
            AssertClean(context, commands);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Every_concrete_insert_writes_one_discriminator_and_only_its_columns(OceanBaseCompatibilityMode mode, bool async)
        {
            foreach (var row in new Asset[] { new(), new Device { Serial = "serial" }, new Sensor { Serial = "serial", Unit = "C" }, new Document { Pages = 9 } })
            {
                var commands = new Commands();
                await using var context = Create(mode, commands);
                row.Name = "new";
                row.TenantId = 1;
                context.Add(row);
                Assert.True(context.Entry(row).Property(a => a.Id).IsTemporary);
                Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
                Assert.Equal(101, row.Id);
                Assert.Equal(EntityState.Unchanged, context.Entry(row).State);
                var insert = Assert.Single(commands.Items, item => item.Text.StartsWith("INSERT", StringComparison.Ordinal));
                var (template, inputs) = row switch
                {
                    Sensor => ("INSERT INTO TphAssets (Kind, Name, Serial, TenantId, Unit, Version) VALUES (?, ?, ?, ?, ?, ?)", new object?[] { "Sensor", "new", "serial", 1, "C", DBNull.Value }),
                    Device => ("INSERT INTO TphAssets (Kind, Name, Serial, TenantId, Version) VALUES (?, ?, ?, ?, ?)", new object?[] { "Device", "new", "serial", 1, DBNull.Value }),
                    Document => ("INSERT INTO TphAssets (Kind, Name, Pages, TenantId, Version) VALUES (?, ?, ?, ?, ?)", new object?[] { "Document", "new", 9, 1, DBNull.Value }),
                    _ => ("INSERT INTO TphAssets (Kind, Name, TenantId, Version) VALUES (?, ?, ?, ?)", new object?[] { "Root", "new", 1, DBNull.Value })
                };
                Assert.Equal(template + (mode == OceanBaseCompatibilityMode.Oracle ? " RETURNING AssetId INTO ?;" : ";"), insert.Template);
                Assert.Equal(inputs, insert.Inputs);
                Assert.Equal(mode == OceanBaseCompatibilityMode.MySql ? 1 : 0, commands.Items.Count(c => c.Text.Contains("LAST_INSERT_ID()", StringComparison.Ordinal)));
                Assert.Equal(mode == OceanBaseCompatibilityMode.Oracle, insert.Text.Contains("RETURNING AssetId INTO", StringComparison.Ordinal));
                AssertClean(context, commands);
            }
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Leaf_graph_save_propagates_generated_key_before_child_and_commits_once(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new Commands();
            await using var context = Create(mode, commands);
            var sensor = new Sensor { Name = "sensor", TenantId = 1, Serial = "serial", Unit = "C" };
            var child = new Attachment { Id = 301, Text = "child", Sensor = sensor };
            context.Add(child);
            Assert.Equal(2, async ? await context.SaveChangesAsync() : context.SaveChanges());
            Assert.Equal(101, sensor.Id);
            Assert.Equal(101, child.SensorId);
            Assert.Same(child, Assert.Single(sensor.Attachments));
            var writes = commands.Items.Where(c => c.Text.StartsWith("INSERT", StringComparison.Ordinal)).ToArray();
            Assert.Equal(2, writes.Length);
            Assert.Equal("INSERT INTO TphAssets (Kind, Name, Serial, TenantId, Unit, Version) VALUES (?, ?, ?, ?, ?, ?)"
                + (mode == OceanBaseCompatibilityMode.Oracle ? " RETURNING AssetId INTO ?;" : ";"), writes[0].Template);
            Assert.Equal(new object?[] { "Sensor", "sensor", "serial", 1, "C", DBNull.Value }, writes[0].Inputs);
            Assert.Equal("INSERT INTO AttachmentRows (Id, SensorId, Text) VALUES (?, ?, ?);", writes[1].Template);
            Assert.Equal(new object?[] { 301, 101, "child" }, writes[1].Inputs);
            Assert.All(context.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
            var transaction = Assert.Single(Native(context).Transactions);
            Assert.Equal(1, transaction.Commits);
            Assert.Equal(1, transaction.Disposals);
            Assert.All(commands.Items, item => Assert.Same(transaction, item.Transaction));
            AssertClean(context, commands);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Update_delete_use_original_nullable_concurrency_and_shared_key_without_rewriting_kind(OceanBaseCompatibilityMode mode, bool async)
        {
            foreach (var original in new int?[] { null, 7 })
                foreach (var delete in new[] { false, true })
                {
                    var commands = new Commands();
                    await using var context = Create(mode, commands);
                    var sensor = new Sensor { Id = 103, Name = "old", Version = original, TenantId = 1, Serial = "serial", Unit = "C" };
                    context.Attach(sensor);
                    if (delete) context.Remove(sensor);
                    else { sensor.Name = "changed"; sensor.Unit = null; sensor.Version = 9; }
                    Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
                    var command = Assert.Single(commands.Items);
                    var predicate = " WHERE AssetId = ? AND Version " + (original is null ? "IS NULL" : "= ?") + ";";
                    Assert.Equal(delete ? "DELETE FROM TphAssets" + predicate
                        : "UPDATE TphAssets SET Name = ?, Unit = ?, Version = ?" + predicate, command.Template);
                    Assert.Equal(delete ? original is null ? new object?[] { 103 } : [103, 7]
                        : original is null ? new object?[] { "changed", DBNull.Value, 9, 103 } : ["changed", DBNull.Value, 9, 103, 7], command.Inputs);
                    Assert.DoesNotContain("Kind", command.Text);
                    Assert.DoesNotContain("Pages", command.Text);
                    Assert.Equal(delete ? EntityState.Detached : EntityState.Unchanged, context.Entry(sensor).State);
                    if (!delete) Assert.Equal(9, context.Entry(sensor).Property(s => s.Version).OriginalValue);
                    AssertClean(context, commands);
                }
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Concurrency_conflict_keeps_the_leaf_and_original_snapshot_for_retry(OceanBaseCompatibilityMode mode, bool async)
        {
            foreach (var delete in new[] { false, true })
            {
                var commands = new Commands { ZeroRows = true };
                await using var context = Create(mode, commands);
                var sensor = new Sensor { Id = 103, TenantId = 1, Name = "old", Version = 7, Serial = "serial" };
                context.Attach(sensor);
                if (delete) context.Remove(sensor);
                else sensor.Name = "changed";
                var failure = async ? await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => context.SaveChangesAsync())
                    : Assert.Throws<DbUpdateConcurrencyException>(() => context.SaveChanges());
                Assert.Same(sensor, Assert.Single(failure.Entries).Entity);
                Assert.Equal(delete ? EntityState.Deleted : EntityState.Modified, context.Entry(sensor).State);
                Assert.Equal(7, context.Entry(sensor).Property(s => s.Version).OriginalValue);
                Assert.Equal(new object?[] { 103, 7 }, Assert.Single(commands.Items).Inputs.TakeLast(2));
                AssertClean(context, commands);
                commands.ZeroRows = false;
                Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
                Assert.Equal(delete ? EntityState.Detached : EntityState.Unchanged, context.Entry(sensor).State);
            }
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Unknown_discriminator_rejects_and_releases_resources_before_a_later_query(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new Commands { Results = Table(HierarchyTypes, [101, "Unknown", "unknown", 1, null, null, null, null]) };
            await using var context = Create(mode, commands);
            var query = context.Set<Asset>();
            var failure = async ? await Assert.ThrowsAsync<InvalidOperationException>(() => query.ToListAsync())
                : Assert.Throws<InvalidOperationException>(() => query.ToList());
            Assert.Contains("Unknown", failure.Message);
            Assert.Empty(context.ChangeTracker.Entries());
            AssertClean(context, commands);
            commands.Results = HierarchyRows();
            Assert.Equal(4, (async ? await query.ToListAsync() : query.ToList()).Count);
            AssertClean(context, commands);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Later_child_failure_discards_generated_parent_values_and_disposes_uncommitted_transaction(OceanBaseCompatibilityMode mode, bool async)
        {
            var error = new InvalidOperationException("synthetic child failure");
            var commands = new Commands { FailOnWrite = 2, Failure = error };
            await using var context = Create(mode, commands);
            var sensor = new Sensor { Name = "sensor", Serial = "serial", TenantId = 1 };
            var child = new Attachment { Id = 301, Text = "child", Sensor = sensor };
            context.Add(child);
            var failure = async ? await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync())
                : Assert.Throws<DbUpdateException>(() => context.SaveChanges());
            Assert.Same(error, failure.InnerException);
            Assert.Same(child, Assert.Single(failure.Entries).Entity);
            Assert.Equal(0, sensor.Id);
            Assert.True(context.Entry(sensor).Property(s => s.Id).IsTemporary);
            Assert.All(context.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Added, entry.State));
            var transaction = Assert.Single(Native(context).Transactions);
            Assert.Equal(0, transaction.Commits);
            Assert.True(transaction.DisposedActive);
            Assert.Equal(1, transaction.Disposals);
            AssertClean(context, commands);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Cancellation_preserves_pending_leaf_and_later_save_can_succeed(OceanBaseCompatibilityMode mode, bool async)
        {
            using var source = new CancellationTokenSource();
            var error = new OperationCanceledException(source.Token);
            var commands = new Commands { FailOnWrite = 1, Failure = error, BeforeFailure = source.Cancel };
            await using var context = Create(mode, commands);
            var row = new Sensor { Id = 103, Name = "before", TenantId = 1, Version = 7 };
            context.Attach(row);
            row.Name = "after";
            var failure = async ? await Assert.ThrowsAsync<OperationCanceledException>(() => context.SaveChangesAsync(source.Token))
                : Assert.Throws<OperationCanceledException>(() => context.SaveChanges());
            Assert.Same(error, failure);
            Assert.True(source.IsCancellationRequested);
            Assert.Equal(EntityState.Modified, context.Entry(row).State);
            Assert.Equal(7, context.Entry(row).Property(s => s.Version).OriginalValue);
            if (async) Assert.Equal(source.Token, Assert.Single(commands.Items).Token);
            AssertClean(context, commands);
            commands.FailOnWrite = null;
            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            Assert.Equal(EntityState.Unchanged, context.Entry(row).State);
            AssertClean(context, commands);
        }

        private static string KindLiteral(OceanBaseCompatibilityMode mode, string kind)
            => (mode == OceanBaseCompatibilityMode.Oracle ? "N'" : "'") + kind + "'";

        private static string Unquote(string sql) => sql.Replace("`", "", StringComparison.Ordinal).Replace("\"", "", StringComparison.Ordinal);
        private static TransactionTests.NativeConnection Native(DbContext context)
            => Assert.IsType<TransactionTests.NativeConnection>(context.Database.GetDbConnection());
        private static void AssertClean(DbContext context, Commands commands)
        {
            Assert.Null(context.Database.CurrentTransaction);
            Assert.Equal(ConnectionState.Closed, Native(context).State);
            Assert.All(commands.Readers, reader => Assert.True(reader.IsClosed));
        }

        private static HierarchyContext Create(OceanBaseCompatibilityMode mode, Commands commands, bool incomplete = false)
        {
            var options = new DbContextOptionsBuilder().UseOceanBase("Server=offline;Database=app", mode)
                .ReplaceService<IRelationalConnection, TransactionTests.TestConnection>()
                .EnableServiceProviderCaching(false).AddInterceptors(commands).Options;
            return incomplete ? new IncompleteContext(options) : new HierarchyContext(options);
        }

        private class HierarchyContext(DbContextOptions options) : DbContext(options)
        {
            public int TenantId { get; set; } = 1;
            protected override void OnModelCreating(ModelBuilder model)
            {
                var root = model.Entity<Asset>();
                root.UseTphMappingStrategy().ToTable("TphAssets");
                root.Property(a => a.Id).HasColumnName("AssetId");
                root.Property(a => a.Name).HasMaxLength(40);
                root.Property(a => a.Version).IsConcurrencyToken();
                root.HasQueryFilter(a => a.TenantId == TenantId);
                root.HasDiscriminator<AssetKind>("Kind").HasValue<Asset>(AssetKind.Root).HasValue<Device>(AssetKind.Device)
                    .HasValue<Sensor>(AssetKind.Sensor).HasValue<Document>(AssetKind.Document);
                root.Property<AssetKind>("Kind").HasConversion<string>().HasMaxLength(16);
                model.Entity<Device>().Property(d => d.Serial).HasMaxLength(40);
                model.Entity<Sensor>().Property(s => s.Unit).HasMaxLength(12);
                var child = model.Entity<Attachment>();
                child.ToTable("AttachmentRows");
                child.Property(a => a.Id).ValueGeneratedNever();
                child.Property(a => a.Text).HasMaxLength(40);
                child.HasOne(a => a.Sensor).WithMany(s => s.Attachments).HasForeignKey(a => a.SensorId);
            }
        }

        private sealed class IncompleteContext(DbContextOptions options) : HierarchyContext(options)
        {
            protected override void OnModelCreating(ModelBuilder model)
            {
                base.OnModelCreating(model);
                model.Entity<Asset>().HasDiscriminator<AssetKind>("Kind").IsComplete(false);
            }
        }

        private enum AssetKind { Root, Device, Sensor, Document, Unknown }
        private class Asset { public int Id { get; set; } public string? Name { get; set; } public int TenantId { get; set; } public int? Version { get; set; } }
        private class Device : Asset { public string? Serial { get; set; } }
        private sealed class Sensor : Device { public string? Unit { get; set; } public List<Attachment> Attachments { get; set; } = []; }
        private sealed class Document : Asset { public int Pages { get; set; } }
        private sealed class Attachment { public int Id { get; set; } public int SensorId { get; set; } public Sensor Sensor { get; set; } = null!; public string? Text { get; set; } }

        private static readonly Type[] HierarchyTypes = [typeof(int), typeof(string), typeof(string), typeof(int), typeof(int), typeof(string), typeof(int), typeof(string)];
        private static DataTable HierarchyRows(bool empty = false)
            => empty ? Table(HierarchyTypes) : Table(HierarchyTypes,
                [101, "Root", "root", 1, 7, null, null, null],
                [102, "Device", "device", 1, 7, "device-serial", null, null],
                [103, "Sensor", "sensor", 1, null, "sensor-serial", null, null],
                [104, "Document", null, 1, 7, null, 9, null]);
        private static DataTable Table(Type[] types, params object?[][] rows)
        {
            var table = new DataTable();
            foreach (var type in types) table.Columns.Add("C" + table.Columns.Count, type);
            foreach (var row in rows) table.Rows.Add(row.Select(value => value ?? DBNull.Value).ToArray());
            return table;
        }

        private sealed record Command(string Text, object?[] Inputs, DbTransaction? Transaction, CancellationToken Token)
        {
            public string Template => Regex.Replace(Regex.Replace(Text.Trim(), @"[@:]\w+", "?"), @"\s+", " ");
        }

        private sealed class Commands : DbCommandInterceptor
        {
            private int _writes;
            public DataTable? Results { get; set; }
            public bool ZeroRows { get; set; }
            public int? FailOnWrite { get; set; }
            public Exception? Failure { get; init; }
            public Action? BeforeFailure { get; init; }
            public List<Command> Items { get; } = [];
            public List<DbDataReader> Readers { get; } = [];
            private void Capture(DbCommand command, CancellationToken token)
            {
                var parameters = command.Parameters.Cast<DbParameter>().ToArray();
                var position = 0;
                var used = new HashSet<DbParameter>();
                var inputs = new List<object?>();
                foreach (Match match in Regex.Matches(command.CommandText, @"[@:]\w+|\?"))
                {
                    var parameter = match.Value == "?" ? parameters[position++]
                        : parameters.Single(p => p.ParameterName.TrimStart('@', ':') == match.Value.TrimStart('@', ':'));
                    used.Add(parameter);
                    if (parameter.Direction == ParameterDirection.Input) inputs.Add(parameter.Value);
                }
                Assert.Equal(parameters.Length, used.Count);
                Items.Add(new Command(Unquote(command.CommandText), inputs.ToArray(), command.Transaction, token));
            }
            private InterceptionResult<int> Write(DbCommand command, CancellationToken token, InterceptionResult<int> result)
            {
                Capture(command, token);
                if (++_writes == FailOnWrite) { BeforeFailure?.Invoke(); throw Failure!; }
                return ZeroRows ? InterceptionResult<int>.SuppressWithResult(0) : result;
            }
            public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData data, InterceptionResult<int> result)
                => Write(command, default, result);
            public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data,
                InterceptionResult<int> result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(Write(command, cancellationToken, result));
            private InterceptionResult<DbDataReader> Read(DbCommand command, CancellationToken token, InterceptionResult<DbDataReader> result)
            {
                Capture(command, token);
                return Results is null ? result : InterceptionResult<DbDataReader>.SuppressWithResult(Results.CreateDataReader());
            }
            public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result)
                => Read(command, default, result);
            public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
                InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(Read(command, cancellationToken, result));
            public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData data, DbDataReader result)
            { Readers.Add(result); return result; }
            public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data, DbDataReader result,
                CancellationToken cancellationToken = default) => ValueTask.FromResult(ReaderExecuted(command, data, result));
        }
    }
}
