using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace OceanBase.EntityFrameworkCore.LiveTests
{
    public sealed class LiveGraphQueryTests
    {
        private const string Hostile = "x' OR 1=1 -- ? :p\n/* graph */";
        private static readonly QueryTrackingBehavior[] TrackingModes = [QueryTrackingBehavior.TrackAll,
            QueryTrackingBehavior.NoTracking, QueryTrackingBehavior.NoTrackingWithIdentityResolution];

        public static IEnumerable<object[]> Cases()
        {
            foreach (var mode in new[] { OceanBaseCompatibilityMode.MySql, OceanBaseCompatibilityMode.Oracle })
                foreach (var async in new[] { false, true })
                    foreach (var nulls in new[] { false, true }) yield return [mode, async, nulls];
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public Task Include_ThenInclude_preserve_composite_alternate_keys_owned_values_and_empty_graphs(
            OceanBaseCompatibilityMode mode, bool async, bool nulls)
            => WithTables(mode, async, nulls, async (context, _, stage) =>
            {
                foreach (var tracking in TrackingModes)
                    foreach (var split in new[] { false, true })
                    {
                        stage($"Include graph {tracking} split={split}");
                        context.ChangeTracker.Clear();
                        var query = Shape(context.Set<Parent>().Include(row => row.Children).ThenInclude(row => row.Detail), tracking, split)
                            .OrderBy(row => row.TenantId).ThenBy(row => row.Id);
                        var actual = await Read(query, async);
                        CheckParents(actual, 1, false, false, details: true);
                        if (tracking == QueryTrackingBehavior.TrackAll)
                        {
                            Assert.Equal(3, context.ChangeTracker.Entries<Parent>().Count());
                            Assert.Equal(2, context.ChangeTracker.Entries<Child>().Count());
                            Assert.Single(context.ChangeTracker.Entries<Detail>());
                            Assert.Equal(2, context.ChangeTracker.Entries<Address>().Count());
                            Assert.Equal(2, context.ChangeTracker.Entries<Label>().Count());
                            foreach (var parent in actual)
                            {
                                Assert.True(context.Entry(parent).Collection(row => row.Children).IsLoaded);
                                Assert.True(context.Entry(parent).Collection(row => row.Labels).IsLoaded);
                                foreach (var label in parent.Labels)
                                {
                                    Assert.Equal(parent.TenantId, context.Entry(label).Property<int>("TenantId").CurrentValue);
                                    Assert.Equal(parent.Id, context.Entry(label).Property<int>("ParentId").CurrentValue);
                                }
                                if (parent.Address is { } address)
                                {
                                    Assert.Equal(parent.TenantId, context.Entry(address).Property<int>("TenantId").CurrentValue);
                                    Assert.Equal(parent.Id, context.Entry(address).Property<int>("ParentId").CurrentValue);
                                }
                            }
                        }
                        else Assert.Empty(context.ChangeTracker.Entries());
                    }
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public Task Required_optional_references_and_identity_resolution_follow_each_tracking_mode(
            OceanBaseCompatibilityMode mode, bool async, bool nulls)
            => WithTables(mode, async, nulls, async (context, _, stage) =>
            {
                foreach (var tracking in TrackingModes)
                    foreach (var split in new[] { false, true })
                    {
                        stage($"references and identity {tracking} split={split}");
                        context.ChangeTracker.Clear();
                        var query = Shape(context.Set<Child>().Include(row => row.Parent), tracking, split).OrderBy(row => row.Id);
                        var first = await Read(query, async);
                        Assert.Equal(new[] { 10, 11 }, first.Select(row => row.Id));
                        Assert.All(first, row => { Assert.Equal(1, row.Parent.TenantId); Assert.Equal(1, row.Parent.Id); });
                        Identity(first[0].Parent, first[1].Parent, tracking != QueryTrackingBehavior.NoTracking);
                        var second = await Read(query, async);
                        Identity(first[0], second[0], tracking == QueryTrackingBehavior.TrackAll);
                        Identity(first[0].Parent, second[0].Parent, tracking == QueryTrackingBehavior.TrackAll);
                        if (tracking != QueryTrackingBehavior.TrackAll) Assert.Empty(context.ChangeTracker.Entries());

                        context.ChangeTracker.Clear();
                        var optional = await Read(Shape(context.Set<Optional>().Include(row => row.Parent), tracking, split).OrderBy(row => row.Id), async);
                        Assert.Equal(new[] { 1, 2, 3, 4 }, optional.Select(row => row.Id));
                        Assert.Null(optional[1].Parent);
                        Assert.Null(optional[2].Parent);
                        Assert.Equal(1, optional[0].Parent!.Id);
                        Assert.Equal(1, optional[3].Parent!.Id);
                        Identity(optional[0].Parent!, optional[3].Parent!, tracking != QueryTrackingBehavior.NoTracking);
                        if (tracking != QueryTrackingBehavior.TrackAll) Assert.Empty(context.ChangeTracker.Entries());

                        stage($"optional composite reference across tenants {tracking} split={split}");
                        context.ChangeTracker.Clear();
                        var acrossTenants = context.Set<Optional>().IgnoreQueryFilters().Include(row => row.Parent);
                        var visible = await Read(Shape(acrossTenants, tracking, split).OrderBy(row => row.TenantId).ThenBy(row => row.Id), async);
                        Assert.Equal(new (int Tenant, int Id, int? ParentTenant, int? ParentId)[]
                        {
                            (1, 1, 1, 1), (1, 2, null, null), (1, 3, 1, 3), (1, 4, 1, 1), (1, 5, 1, 1),
                            (2, 1, 2, 1), (2, 2, null, null)
                        }, visible.Select(row => (row.TenantId, row.Id, row.Parent?.TenantId, row.Parent?.Id)));
                        if (tracking != QueryTrackingBehavior.TrackAll) Assert.Empty(context.ChangeTracker.Entries());
                    }
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public Task Filtered_Include_orders_pages_and_marks_empty_collections_loaded(
            OceanBaseCompatibilityMode mode, bool async, bool nulls)
            => WithTables(mode, async, nulls, async (context, _, stage) =>
            {
                foreach (var tracking in TrackingModes)
                    foreach (var split in new[] { false, true })
                    {
                        stage($"filtered Include {tracking} split={split}");
                        context.ChangeTracker.Clear();
                        var query = Shape(context.Set<Parent>().Include(row => row.Children.OrderByDescending(child => child.Id).Skip(1).Take(1))
                            .ThenInclude(child => child.Detail), tracking, split).OrderBy(row => row.Id);
                        var actual = await Read(query, async);
                        Assert.Equal(new[] { 1, 2, 4 }, actual.Select(row => row.Id));
                        foreach (var parent in actual)
                        {
                            var expected = Children().Where(child => child.TenantId == parent.TenantId && child.ParentCode == parent.Code && !child.IsDeleted)
                                .OrderByDescending(child => child.Id).Skip(1).Take(1).Select(child => child.Id);
                            Assert.Equal(expected, parent.Children.Select(child => child.Id));
                            if (tracking == QueryTrackingBehavior.TrackAll) Assert.True(context.Entry(parent).Collection(row => row.Children).IsLoaded);
                        }
                        Assert.Equal("detail-one", Assert.Single(actual[0].Children).Detail!.Text);
                        context.ChangeTracker.Clear();
                        var page = await Read(query.Skip(1).Take(2), async);
                        Assert.Equal(new[] { 2, 4 }, page.Select(row => row.Id));
                        Assert.All(page, parent => Assert.Empty(parent.Children));
                        Assert.Empty(await Read(query.Skip(10).Take(2), async));
                        if (tracking != QueryTrackingBehavior.TrackAll) Assert.Empty(context.ChangeTracker.Entries());
                    }
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public Task Composed_global_filters_disable_together_and_refresh_current_context_across_graph_sources(
            OceanBaseCompatibilityMode mode, bool async, bool nulls)
            => WithTables(mode, async, nulls, async (context, _, stage) =>
            {
                foreach (var tracking in TrackingModes)
                    foreach (var disabled in new[] { false, true })
                    {
                        IQueryable<Parent> source = context.Set<Parent>();
                        if (disabled) source = source.IgnoreQueryFilters();
                        var query = Shape(source.Include(row => row.Children).ThenInclude(row => row.Detail).Include(row => row.Tags),
                            tracking, tracking != QueryTrackingBehavior.NoTracking).OrderBy(row => row.TenantId).ThenBy(row => row.Id);
                        foreach (var tenant in new[] { 1, 2, 1 })
                        {
                            stage($"composed filters {tracking} disabled={disabled} tenant={tenant}");
                            context.ChangeTracker.Clear();
                            context.TenantId = tenant;
                            var actual = await Read(query, async);
                            CheckParents(actual, tenant, disabled, disabled, details: true);
                            foreach (var parent in actual)
                            {
                                var tags = Links().Where(link => link.TenantId == parent.TenantId && link.ParentId == parent.Id)
                                    .Join(Tags().Where(tag => (disabled || tag.TenantId == tenant) && (disabled || !tag.IsDeleted)),
                                        link => (link.TenantId, link.TagId), tag => (tag.TenantId, tag.Id), (_, tag) => tag.Id).Order();
                                Assert.Equal(tags, parent.Tags.Select(tag => tag.Id).Order());
                            }
                            if (tracking != QueryTrackingBehavior.TrackAll) Assert.Empty(context.ChangeTracker.Entries());
                        }
                    }
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public Task Many_to_many_graphs_preserve_composite_links_shared_identity_and_inverse_fixup(
            OceanBaseCompatibilityMode mode, bool async, bool nulls)
            => WithTables(mode, async, nulls, async (context, _, stage) =>
            {
                foreach (var tracking in TrackingModes)
                    foreach (var split in new[] { false, true })
                    {
                        stage($"many-to-many {tracking} split={split}");
                        context.ChangeTracker.Clear();
                        var parents = await Read(Shape(context.Set<Parent>().Include(row => row.Tags), tracking, split).OrderBy(row => row.Id), async);
                        Assert.Equal(new[] { 1, 2, 4 }, parents.Select(row => row.Id));
                        Assert.Equal(new[] { 100, 101 }, parents[0].Tags.Select(tag => tag.Id).Order());
                        Assert.Empty(parents[1].Tags);
                        var shared = Assert.Single(parents[2].Tags);
                        Identity(parents[0].Tags.Single(tag => tag.Id == 100), shared, tracking != QueryTrackingBehavior.NoTracking);
                        foreach (var parent in parents)
                            foreach (var tag in parent.Tags) Assert.Contains(tag.Parents, inverse => ReferenceEquals(inverse, parent));
                        if (tracking == QueryTrackingBehavior.TrackAll) Assert.Equal(3, context.ChangeTracker.Entries<Link>().Count());
                        else Assert.Empty(context.ChangeTracker.Entries());

                        context.ChangeTracker.Clear();
                        var links = await Read(Shape(context.Set<Link>().Include(row => row.Parent).Include(row => row.Tag), tracking, split)
                            .OrderBy(row => row.ParentId).ThenBy(row => row.TagId), async);
                        Assert.Equal(new[] { (1, 1, 100), (1, 1, 101), (1, 4, 100) }, links.Select(link => (link.TenantId, link.ParentId, link.TagId)));
                        Assert.All(links, link => { Assert.Equal(link.TenantId, link.Parent.TenantId); Assert.Equal(link.ParentId, link.Parent.Id);
                            Assert.Equal(link.TenantId, link.Tag.TenantId); Assert.Equal(link.TagId, link.Tag.Id); });
                        Identity(links[0].Tag, links[2].Tag, tracking != QueryTrackingBehavior.NoTracking);
                        if (tracking != QueryTrackingBehavior.TrackAll) Assert.Empty(context.ChangeTracker.Entries());
                    }
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public Task Pretracked_filtered_Include_fixup_differs_from_fresh_no_tracking_results(
            OceanBaseCompatibilityMode mode, bool async, bool nulls)
            => WithTables(mode, async, nulls, async (context, _, stage) =>
            {
                foreach (var split in new[] { false, true })
                {
                    stage($"pretracked filtered Include split={split}");
                    context.ChangeTracker.Clear();
                    var deleted = Assert.Single(await Read(context.Set<Child>().IgnoreQueryFilters().AsTracking().Where(row => row.TenantId == context.TenantId && row.Id == 12), async));
                    var parent = Assert.Single(await Read(Shape(context.Set<Parent>().Where(row => row.Id == 1), QueryTrackingBehavior.TrackAll, split), async));
                    Assert.Same(deleted, Assert.Single(parent.Children));
                    Assert.False(context.Entry(parent).Collection(row => row.Children).IsLoaded);
                    var query = context.Set<Parent>().Where(row => row.Id == 1).Include(row => row.Children.Where(child => child.Id == 10));
                    var tracked = Assert.Single(await Read(Shape(query, QueryTrackingBehavior.TrackAll, split), async));
                    Assert.Same(parent, tracked);
                    Assert.Equal(new[] { 10, 12 }, tracked.Children.Select(child => child.Id).Order());
                    Assert.True(context.Entry(tracked).Collection(row => row.Children).IsLoaded);
                    foreach (var tracking in new[] { QueryTrackingBehavior.NoTracking, QueryTrackingBehavior.NoTrackingWithIdentityResolution })
                    {
                        var fresh = Assert.Single(await Read(Shape(query, tracking, split), async));
                        Assert.NotSame(parent, fresh);
                        Assert.Equal(10, Assert.Single(fresh.Children).Id);
                        Assert.Equal(new[] { 10, 12 }, tracked.Children.Select(child => child.Id).Order());
                    }
                }
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public Task Captured_hostile_predicates_bind_and_refresh_without_changing_graph_command_text(
            OceanBaseCompatibilityMode mode, bool async, bool nulls)
            => WithTables(mode, async, nulls, async (context, capture, stage) =>
            {
                capture.Enabled = true;
                foreach (var tracking in TrackingModes)
                    foreach (var split in new[] { false, true })
                    {
                        var needle = Hostile;
                        var query = Shape(context.Set<Parent>().Where(row => row.Name == needle).Include(row => row.Children)
                            .ThenInclude(row => row.Detail), tracking, split).OrderBy(row => row.Id);
                        string[]? baselineSql = null;
                        foreach (var (tenant, name) in new[] { (1, Hostile), (1, "empty"), (1, "missing"), (2, Hostile), (1, Hostile) })
                        {
                            stage($"graph predicate {tracking} split={split} tenant={tenant}");
                            context.ChangeTracker.Clear();
                            capture.Items.Clear();
                            context.TenantId = tenant;
                            needle = name;
                            var actual = await Read(query, async);
                            Assert.Equal(Parents().Where(parent => parent.TenantId == tenant && !parent.IsDeleted && parent.Name == needle)
                                .Select(parent => parent.Id), actual.Select(parent => parent.Id));
                            if (split && actual.Length != 0) Assert.True(capture.Items.Count > 1);
                            else Assert.Single(capture.Items);
                            var sql = capture.Items.Select(command => command.Sql).ToArray();
                            baselineSql ??= sql;
                            Assert.Equal(split && actual.Length == 0 ? baselineSql.Take(1) : baselineSql, sql);
                            Assert.All(capture.Items, command =>
                            {
                                Assert.DoesNotContain(Hostile, command.Sql, StringComparison.Ordinal);
                                Assert.Equal(needle, Assert.Single(command.Values.OfType<string>().Distinct()));
                                Assert.Contains(tenant, command.Values);
                            });
                            foreach (var parent in actual)
                                Assert.Equal(Children().Where(child => child.TenantId == tenant && child.ParentCode == parent.Code && !child.IsDeleted)
                                    .Select(child => child.Id).Order(), parent.Children.Select(child => child.Id).Order());
                            if (tracking != QueryTrackingBehavior.TrackAll) Assert.Empty(context.ChangeTracker.Entries());
                        }
                    }
            });

        private static void CheckParents(Parent[] actual, int tenant, bool ignoreTenant, bool ignoreActive, bool details)
        {
            var parents = Parents().Where(parent => (ignoreTenant || parent.TenantId == tenant) && (ignoreActive || !parent.IsDeleted))
                .OrderBy(parent => parent.TenantId).ThenBy(parent => parent.Id).ToArray();
            Assert.Equal(parents.Select(parent => (parent.TenantId, parent.Id)), actual.Select(parent => (parent.TenantId, parent.Id)));
            foreach (var (expected, parent) in parents.Zip(actual))
            {
                Assert.Equal(expected.Code, parent.Code);
                Assert.Equal(expected.Name, parent.Name);
                Assert.Equal(expected.Address?.City, parent.Address?.City);
                Assert.Equal(expected.Address is null, parent.Address is null);
                Assert.Equal(Labels().Where(label => label.TenantId == parent.TenantId && label.ParentId == parent.Id)
                    .OrderBy(label => label.Id).Select(label => (label.Id, label.Value)), parent.Labels.OrderBy(label => label.Id).Select(label => (label.Id, label.Value)));
                var children = Children().Where(child => child.TenantId == parent.TenantId && child.ParentCode == parent.Code
                    && (ignoreTenant || child.TenantId == tenant) && (ignoreActive || !child.IsDeleted)).OrderBy(child => child.Id).ToArray();
                Assert.Equal(children.Select(child => child.Id), parent.Children.Select(child => child.Id).Order());
                foreach (var child in parent.Children)
                {
                    Assert.Same(parent, child.Parent);
                    Assert.Equal(children.Single(expectedChild => expectedChild.Id == child.Id).Name, child.Name);
                    if (details)
                    {
                        var detail = Details().SingleOrDefault(value => value.TenantId == child.TenantId && value.ChildId == child.Id);
                        Assert.Equal(detail?.Text, child.Detail?.Text);
                        Assert.Equal(detail is null, child.Detail is null);
                        if (child.Detail is { } loaded) Assert.Same(child, loaded.Child);
                    }
                }
            }
        }

        private static void Identity(object left, object right, bool same) { if (same) Assert.Same(left, right); else Assert.NotSame(left, right); }
        private static Task<T[]> Read<T>(IQueryable<T> query, bool async) => async ? query.ToArrayAsync() : Task.FromResult(query.ToArray());
        private static IQueryable<T> Shape<T>(IQueryable<T> query, QueryTrackingBehavior tracking, bool split) where T : class
        {
            query = tracking switch { QueryTrackingBehavior.TrackAll => query.AsTracking(), QueryTrackingBehavior.NoTracking => query.AsNoTracking(),
                _ => query.AsNoTrackingWithIdentityResolution() };
            return split ? query.AsSplitQuery() : query.AsSingleQuery();
        }

        private static Task WithTables(OceanBaseCompatibilityMode mode, bool async, bool nulls, Func<Context, Capture, Action<string>, Task> run)
        {
            var capture = new Capture();
            return LiveStorageTable.WithTable<Context>(mode, async, false, (options, root) =>
            {
                var builder = new DbContextOptionsBuilder<Context>(options).EnableServiceProviderCaching(false)
                    .ReplaceService<IModelCacheKeyFactory, CacheKey>().UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).AddInterceptors(capture);
                ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(
                    builder.Options.Extensions.OfType<RelationalOptionsExtension>().Single().WithUseRelationalNulls(nulls));
                return new Context(builder.Options, root);
            }, async (context, _, stage) =>
            {
                if (async) await context.Database.OpenConnectionAsync(); else context.Database.OpenConnection();
                try
                {
                    stage("verify assigned graph seed counts");
                    Assert.Equal(Parents().Length, async ? await context.Set<Parent>().IgnoreQueryFilters().CountAsync() : context.Set<Parent>().IgnoreQueryFilters().Count());
                    Assert.Equal(Children().Length, async ? await context.Set<Child>().IgnoreQueryFilters().CountAsync() : context.Set<Child>().IgnoreQueryFilters().Count());
                    await run(context, capture, stage);
                }
                finally { if (async) await context.Database.CloseConnectionAsync(); else context.Database.CloseConnection(); }
            }, ["P", "C", "D", "O", "L", "T", "J"]);
        }

        private static Parent[] Parents() => [
            new() { TenantId = 1, Id = 1, Code = "A", Name = Hostile, Address = new Address { City = "north" } },
            new() { TenantId = 1, Id = 2, Code = "B", Name = "empty" },
            new() { TenantId = 1, Id = 3, Code = "C", Name = "deleted", IsDeleted = true, Address = new Address { City = "hidden" } },
            new() { TenantId = 1, Id = 4, Code = "D", Name = "filtered", Address = new Address { City = "edge" } },
            new() { TenantId = 2, Id = 1, Code = "A", Name = Hostile, Address = new Address { City = "south" } },
            new() { TenantId = 2, Id = 2, Code = "B", Name = "other-deleted", IsDeleted = true }];
        private static Child[] Children() => [
            new() { TenantId = 1, Id = 10, ParentCode = "A", Name = "first" },
            new() { TenantId = 1, Id = 11, ParentCode = "A", Name = "second" },
            new() { TenantId = 1, Id = 12, ParentCode = "A", Name = "deleted-child", IsDeleted = true },
            new() { TenantId = 1, Id = 13, ParentCode = "C", Name = "hidden-parent" },
            new() { TenantId = 1, Id = 14, ParentCode = "D", Name = "only-deleted", IsDeleted = true },
            new() { TenantId = 2, Id = 10, ParentCode = "A", Name = "other-first" },
            new() { TenantId = 2, Id = 11, ParentCode = "B", Name = "other-hidden-parent" }];
        private static Detail[] Details() => [new() { TenantId = 1, ChildId = 10, Text = "detail-one" },
            new() { TenantId = 1, ChildId = 12, Text = "deleted-detail" }, new() { TenantId = 2, ChildId = 10, Text = "detail-two" }];
        private static Optional[] Optionals() => [new() { TenantId = 1, Id = 1, ParentId = 1 }, new() { TenantId = 1, Id = 2 },
            new() { TenantId = 1, Id = 3, ParentId = 3 }, new() { TenantId = 1, Id = 4, ParentId = 1 },
            new() { TenantId = 1, Id = 5, ParentId = 1, IsDeleted = true }, new() { TenantId = 2, Id = 1, ParentId = 1 }, new() { TenantId = 2, Id = 2 }];
        private static (int TenantId, int ParentId, int Id, string Value)[] Labels() => [(1, 1, 1, "red"), (1, 1, 2, "blue"), (1, 3, 1, "hidden"), (2, 1, 1, "green")];
        private static Tag[] Tags() => [new() { TenantId = 1, Id = 100, Name = "shared" }, new() { TenantId = 1, Id = 101, Name = "second" },
            new() { TenantId = 1, Id = 102, Name = "hidden", IsDeleted = true }, new() { TenantId = 2, Id = 100, Name = "other" }];
        private static Link[] Links() => [new() { TenantId = 1, ParentId = 1, TagId = 100 }, new() { TenantId = 1, ParentId = 1, TagId = 101 },
            new() { TenantId = 1, ParentId = 1, TagId = 102 }, new() { TenantId = 1, ParentId = 4, TagId = 100 }, new() { TenantId = 2, ParentId = 1, TagId = 100 }];

        private sealed class Context(DbContextOptions<Context> options, string root) : DbContext(options)
        {
            public string Root => root;
            public int TenantId { get; set; } = 1;
            protected override void OnModelCreating(ModelBuilder model)
            {
                var parent = model.Entity<Parent>();
                parent.ToTable(root + "P");
                parent.HasKey(row => new { row.TenantId, row.Id });
                parent.Property(row => row.Id).ValueGeneratedNever();
                parent.HasAlternateKey(row => new { row.TenantId, row.Code });
                parent.Property(row => row.Code).HasMaxLength(32);
                parent.Property(row => row.Name).HasMaxLength(80);
                parent.HasQueryFilter(row => row.TenantId == TenantId && !row.IsDeleted);
                parent.HasData(Parents().Select(row => new { row.TenantId, row.Id, row.Code, row.Name, row.IsDeleted }));
                parent.OwnsOne(row => row.Address, owned =>
                {
                    owned.WithOwner().HasForeignKey("TenantId", "ParentId");
                    owned.Property<int>("TenantId").HasColumnName("TenantId");
                    owned.Property<int>("ParentId").HasColumnName("Id");
                    owned.Property(row => row.City).HasMaxLength(50).HasColumnName("City");
                    owned.HasData(Parents().Where(row => row.Address is not null).Select(row => new { row.TenantId, ParentId = row.Id, row.Address!.City }));
                });
                parent.OwnsMany(row => row.Labels, owned =>
                {
                    owned.ToTable(root + "L");
                    owned.WithOwner().HasForeignKey("TenantId", "ParentId").Metadata.DeleteBehavior = DeleteBehavior.NoAction;
                    owned.HasKey("TenantId", "ParentId", nameof(Label.Id));
                    owned.Property(row => row.Id).ValueGeneratedNever();
                    owned.Property(row => row.Value).HasMaxLength(32);
                    owned.HasData(Labels().Select(row => new { row.TenantId, row.ParentId, row.Id, row.Value }));
                });
                var child = model.Entity<Child>();
                child.ToTable(root + "C");
                child.HasKey(row => new { row.TenantId, row.Id });
                child.Property(row => row.Id).ValueGeneratedNever();
                child.Property(row => row.ParentCode).HasMaxLength(32);
                child.Property(row => row.Name).HasMaxLength(80);
                child.HasOne(row => row.Parent).WithMany(row => row.Children).HasForeignKey(row => new { row.TenantId, row.ParentCode })
                    .HasPrincipalKey(row => new { row.TenantId, row.Code }).OnDelete(DeleteBehavior.NoAction);
                child.HasQueryFilter(row => row.TenantId == TenantId && !row.IsDeleted);
                child.HasData(Children());
                var detail = model.Entity<Detail>();
                detail.ToTable(root + "D");
                detail.HasKey(row => new { row.TenantId, row.ChildId });
                detail.Property(row => row.Text).HasMaxLength(80);
                detail.HasOne(row => row.Child).WithOne(row => row.Detail).HasForeignKey<Detail>(row => new { row.TenantId, row.ChildId }).OnDelete(DeleteBehavior.NoAction);
                detail.HasQueryFilter(row => row.TenantId == TenantId);
                detail.HasData(Details());
                var optional = model.Entity<Optional>();
                optional.ToTable(root + "O");
                optional.HasKey(row => new { row.TenantId, row.Id });
                optional.Property(row => row.Id).ValueGeneratedNever();
                optional.HasOne(row => row.Parent).WithMany().HasForeignKey(row => new { row.TenantId, row.ParentId }).OnDelete(DeleteBehavior.NoAction);
                optional.HasQueryFilter(row => row.TenantId == TenantId && !row.IsDeleted);
                optional.HasData(Optionals());
                var tag = model.Entity<Tag>();
                tag.ToTable(root + "T");
                tag.HasKey(row => new { row.TenantId, row.Id });
                tag.Property(row => row.Id).ValueGeneratedNever();
                tag.Property(row => row.Name).HasMaxLength(32);
                tag.HasQueryFilter(row => row.TenantId == TenantId && !row.IsDeleted);
                tag.HasData(Tags());
                parent.HasMany(row => row.Tags).WithMany(row => row.Parents).UsingEntity<Link>(
                    right => right.HasOne(row => row.Tag).WithMany().HasForeignKey(row => new { row.TenantId, row.TagId }).OnDelete(DeleteBehavior.NoAction),
                    left => left.HasOne(row => row.Parent).WithMany().HasForeignKey(row => new { row.TenantId, row.ParentId }).OnDelete(DeleteBehavior.NoAction),
                    link => { link.ToTable(root + "J"); link.HasKey(row => new { row.TenantId, row.ParentId, row.TagId });
                        link.HasQueryFilter(row => row.TenantId == TenantId); link.HasData(Links()); });
            }
        }
        private sealed class CacheKey : IModelCacheKeyFactory
        {
            public object Create(DbContext context, bool designTime) => (context.GetType(), ((Context)context).Root, designTime);
        }
        private sealed class Parent
        {
            public int TenantId { get; set; } public int Id { get; set; } public string Code { get; set; } = "";
            public string Name { get; set; } = ""; public bool IsDeleted { get; set; } public Address? Address { get; set; }
            public List<Child> Children { get; set; } = []; public List<Label> Labels { get; set; } = []; public List<Tag> Tags { get; set; } = [];
        }
        private sealed class Child
        {
            public int TenantId { get; set; } public int Id { get; set; } public string ParentCode { get; set; } = "";
            public string Name { get; set; } = ""; public bool IsDeleted { get; set; } public Parent Parent { get; set; } = null!; public Detail? Detail { get; set; }
        }
        private sealed class Detail { public int TenantId { get; set; } public int ChildId { get; set; } public string Text { get; set; } = ""; public Child Child { get; set; } = null!; }
        private sealed class Optional { public int TenantId { get; set; } public int Id { get; set; } public int? ParentId { get; set; } public bool IsDeleted { get; set; } public Parent? Parent { get; set; } }
        private sealed class Address { public string? City { get; set; } }
        private sealed class Label { public int Id { get; set; } public string Value { get; set; } = ""; }
        private sealed class Tag { public int TenantId { get; set; } public int Id { get; set; } public string Name { get; set; } = ""; public bool IsDeleted { get; set; } public List<Parent> Parents { get; set; } = []; }
        private sealed class Link { public int TenantId { get; set; } public int ParentId { get; set; } public int TagId { get; set; } public Parent Parent { get; set; } = null!; public Tag Tag { get; set; } = null!; }
        private sealed record Command(string Sql, object?[] Values);
        private sealed class Capture : DbCommandInterceptor
        {
            public bool Enabled { get; set; }
            public List<Command> Items { get; } = [];
            public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
            {
                if (Enabled) Items.Add(new Command(command.CommandText, command.Parameters.Cast<DbParameter>().Select(parameter => parameter.Value).ToArray()));
                return result;
            }
            public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
                InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(ReaderExecuting(command, eventData, result));
        }
    }
}
