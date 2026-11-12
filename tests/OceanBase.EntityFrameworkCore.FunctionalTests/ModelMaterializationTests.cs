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
using Xunit;

namespace OceanBase.EntityFrameworkCore.FunctionalTests
{
    public sealed class ModelMaterializationTests
    {
        public static IEnumerable<object[]> TrackingCases()
        {
            foreach (var mode in Enum.GetValues<OceanBaseCompatibilityMode>())
                foreach (var async in new[] { false, true })
                    foreach (var tracking in Enum.GetValues<QueryTrackingBehavior>())
                        yield return [mode, async, tracking];
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Relational_model_retains_keys_relationships_owned_and_shadow_metadata(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode, new ReaderCapture());
            var parent = context.Model.FindEntityType(typeof(Parent))!;
            var child = context.Model.FindEntityType(typeof(Child))!;
            var optional = context.Model.FindEntityType(typeof(OptionalLink))!;
            Assert.Equal(new[] { "TenantId", "Id" }, parent.FindPrimaryKey()!.Properties.Select(p => p.Name));
            var alternate = Assert.Single(parent.GetKeys(), key => !key.IsPrimaryKey());
            Assert.Equal(new[] { "TenantId", "Code" }, alternate.Properties.Select(p => p.Name));
            Assert.Equal("GraphParents", parent.GetTableName());
            Assert.Equal("DisplayName", parent.FindProperty(nameof(Parent.Name))!.GetColumnName(
                StoreObjectIdentifier.Table("GraphParents", null)));
            Assert.Equal("IX_Parent_Name", Assert.Single(parent.GetIndexes()).GetDatabaseName());
            var required = Assert.Single(child.GetForeignKeys());
            Assert.True(required.IsRequired);
            Assert.Same(alternate, required.PrincipalKey);
            Assert.Equal(new[] { "TenantId", "ParentCode" }, required.Properties.Select(p => p.Name));
            Assert.Equal(DeleteBehavior.Cascade, required.DeleteBehavior);
            var optionalFk = Assert.Single(optional.GetForeignKeys());
            Assert.False(optionalFk.IsRequired);
            Assert.Same(parent.FindPrimaryKey(), optionalFk.PrincipalKey);
            Assert.Equal(new[] { "TenantId", "ParentId" }, optionalFk.Properties.Select(p => p.Name));
            Assert.Equal(DeleteBehavior.ClientSetNull, optionalFk.DeleteBehavior);
            var revision = optional.FindProperty("Revision")!;
            Assert.True(revision.IsShadowProperty());
            Assert.True(revision.IsConcurrencyToken);
            Assert.Equal(typeof(int), revision.ClrType);
            Assert.Equal(ValueGenerated.Never, parent.FindProperty(nameof(Parent.Id))!.ValueGenerated);

            var profile = context.Model.FindEntityType(typeof(Profile))!;
            var address = profile.FindNavigation(nameof(Profile.Address))!.TargetEntityType;
            Assert.True(address.IsOwned());
            Assert.Equal(profile.GetTableName(), address.GetTableName());
            Assert.Same(profile, address.FindOwnership()!.PrincipalEntityType);
            var labels = profile.FindNavigation(nameof(Profile.Labels))!.TargetEntityType;
            Assert.True(labels.IsOwned());
            Assert.Equal("ProfileLabels", labels.GetTableName());
            Assert.Equal(new[] { "ProfileId", "Id" }, labels.FindPrimaryKey()!.Properties.Select(p => p.Name));
            Assert.True(labels.FindProperty("ProfileId")!.IsShadowProperty());
            Assert.Equal(new[] { "ProfileId" }, labels.FindOwnership()!.Properties.Select(p => p.Name));
            Assert.NotEmpty(context.Model.GetRelationalModel().Tables);
        }

        [Theory]
        [MemberData(nameof(TrackingCases))]
        public async Task Collection_then_reference_include_shapes_composite_graphs(
            OceanBaseCompatibilityMode mode, bool async, QueryTrackingBehavior tracking)
        {
            var capture = new ReaderCapture
            {
                Results = Table([typeof(int), typeof(int), typeof(string), typeof(string),
                        typeof(int), typeof(int), typeof(string), typeof(string), typeof(int), typeof(int), typeof(string)],
                    [1, 7, "A", "first", 1, 10, "child-one", "A", 1, 10, "detail"],
                    [1, 7, "A", "first", 1, 11, "child-two", "A", null, null, null],
                    [1, 8, "B", "empty", null, null, null, null, null, null, null],
                    [2, 7, "A", "other-tenant", 2, 10, "child-other", "A", 2, 10, "other-detail"])
            };
            await using var context = CreateContext(mode, capture);
            context.ChangeTracker.QueryTrackingBehavior = tracking;
            var query = context.Set<Parent>().Include(p => p.Children).ThenInclude(c => c.Detail)
                .OrderBy(p => p.TenantId).ThenBy(p => p.Id);
            var result = async ? await query.ToListAsync() : query.ToList();
            Assert.Equal(3, result.Count);
            Assert.Equal(new[] { "first", "empty", "other-tenant" }, result.Select(p => p.Name));
            var first = result[0];
            Assert.Equal(new[] { 10, 11 }, first.Children.Select(c => c.Id));
            Assert.All(first.Children, child => Assert.Same(first, child.Parent));
            Assert.Equal("detail", first.Children[0].Detail!.Text);
            Assert.Same(first.Children[0], first.Children[0].Detail!.Child);
            Assert.Null(first.Children[1].Detail);
            Assert.Empty(result[1].Children);
            var other = Assert.Single(result[2].Children);
            Assert.Equal(2, other.TenantId);
            Assert.Equal("other-detail", other.Detail!.Text);
            Assert.NotSame(first.Children[0], other);
            Assert.Same(result[2], other.Parent);
            Assert.Equal(tracking == QueryTrackingBehavior.TrackAll ? 8 : 0, context.ChangeTracker.Entries().Count());
            Assert.All(context.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
            Assert.Contains("LEFT JOIN", capture.Sql!);
            var sql = Unquote(capture.Sql!);
            Assert.Matches(@"^SELECT (\w+)\.TenantId, \1\.Id, \1\.Code, \1\.DisplayName, (\w+)\.TenantId, \2\.Id, \2\.Name, \2\.ParentCode, \2\.TenantId0, \2\.ChildId, \2\.Text\r?\n", sql);
            Assert.Matches(@"ON (\w+)\.TenantId = (\w+)\.TenantId AND \1\.Code = \2\.ParentCode", sql);
            Assert.Matches(@"ON (\w+)\.TenantId = (\w+)\.TenantId AND \1\.Id = \2\.ChildId", sql);
            Assert.Contains("ORDER BY", capture.Sql!);
            Assert.Equal(1, capture.Executions);
        }

        [Theory]
        [MemberData(nameof(TrackingCases))]
        public async Task Required_reference_include_obeys_tracking_and_identity_resolution(
            OceanBaseCompatibilityMode mode, bool async, QueryTrackingBehavior tracking)
        {
            var capture = new ReaderCapture
            {
                Results = Table([typeof(int), typeof(int), typeof(string), typeof(string),
                        typeof(int), typeof(int), typeof(string), typeof(string)],
                    [1, 10, "one", "A", 1, 7, "A", "parent"],
                    [1, 11, "two", "A", 1, 7, "A", "parent"])
            };
            await using var context = CreateContext(mode, capture);
            context.ChangeTracker.QueryTrackingBehavior = tracking;
            var query = context.Set<Child>().Include(c => c.Parent).OrderBy(c => c.Id);
            var result = async ? await query.ToListAsync() : query.ToList();
            Assert.Equal(2, result.Count);
            Assert.Equal(new[] { "one", "two" }, result.Select(c => c.Name));
            Assert.All(result, child =>
            {
                Assert.Equal("parent", child.Parent.Name);
                Assert.Contains(child, child.Parent.Children);
            });
            if (tracking == QueryTrackingBehavior.NoTracking)
            {
                Assert.NotSame(result[0].Parent, result[1].Parent);
                Assert.All(result, child => Assert.Single(child.Parent.Children));
            }
            else
            {
                Assert.Same(result[0].Parent, result[1].Parent);
                Assert.Equal(2, result[0].Parent.Children.Count);
            }
            Assert.Equal(tracking == QueryTrackingBehavior.TrackAll ? 3 : 0, context.ChangeTracker.Entries().Count());
            Assert.Contains("INNER JOIN", capture.Sql!);
            var sql = Unquote(capture.Sql!);
            Assert.Matches(@"^SELECT (\w+)\.TenantId, \1\.Id, \1\.Name, \1\.ParentCode, (\w+)\.TenantId, \2\.Id, \2\.Code, \2\.DisplayName\r?\n", sql);
            Assert.Matches(@"ON (\w+)\.TenantId = (\w+)\.TenantId AND \1\.ParentCode = \2\.Code", sql);
            Assert.Equal(1, capture.Executions);
        }

        [Theory]
        [MemberData(nameof(TrackingCases))]
        public async Task Optional_composite_reference_handles_null_foreign_key_and_shadow_values(
            OceanBaseCompatibilityMode mode, bool async, QueryTrackingBehavior tracking)
        {
            var capture = new ReaderCapture
            {
                Results = Table([typeof(int), typeof(int), typeof(int), typeof(int),
                        typeof(int), typeof(int), typeof(string), typeof(string)],
                    [1, 20, 7, 8, 1, 7, "A", "parent"],
                    [1, 21, null, 9, null, null, null, null])
            };
            await using var context = CreateContext(mode, capture);
            context.ChangeTracker.QueryTrackingBehavior = tracking;
            var query = context.Set<OptionalLink>().Include(l => l.Parent).OrderBy(l => l.Id);
            var result = async ? await query.ToListAsync() : query.ToList();
            Assert.Equal(2, result.Count);
            Assert.Equal(7, result[0].ParentId);
            Assert.Equal("parent", result[0].Parent!.Name);
            Assert.Null(result[1].ParentId);
            Assert.Null(result[1].Parent);
            if (tracking == QueryTrackingBehavior.TrackAll)
            {
                Assert.Equal(8, context.Entry(result[0]).Property<int>("Revision").CurrentValue);
                Assert.Equal(9, context.Entry(result[1]).Property<int>("Revision").CurrentValue);
                Assert.All(result, link => Assert.True(context.Entry(link).Reference(l => l.Parent).IsLoaded));
            }
            Assert.Equal(tracking == QueryTrackingBehavior.TrackAll ? 3 : 0, context.ChangeTracker.Entries().Count());
            Assert.Contains("LEFT JOIN", capture.Sql!);
            var sql = Unquote(capture.Sql!);
            Assert.Matches(@"^SELECT (\w+)\.TenantId, \1\.Id, \1\.ParentId, \1\.Revision, (\w+)\.TenantId, \2\.Id, \2\.Code, \2\.DisplayName\r?\n", sql);
            Assert.Matches(@"ON (\w+)\.TenantId = (\w+)\.TenantId AND \1\.ParentId = \2\.Id", sql);
            Assert.Equal(1, capture.Executions);
        }

        [Theory]
        [MemberData(nameof(TrackingCases))]
        public async Task Owned_reference_and_collection_materialize_optional_rows_and_shadow_owner_keys(
            OceanBaseCompatibilityMode mode, bool async, QueryTrackingBehavior tracking)
        {
            var capture = new ReaderCapture
            {
                Results = Table([typeof(int), typeof(string), typeof(int), typeof(int), typeof(string)],
                    [5, "Paris", 5, 1, "one"], [5, "Paris", 5, 2, "two"],
                    [6, null, null, null, null])
            };
            await using var context = CreateContext(mode, capture);
            context.ChangeTracker.QueryTrackingBehavior = tracking;
            var query = context.Set<Profile>().OrderBy(p => p.Id);
            var result = async ? await query.ToListAsync() : query.ToList();
            Assert.Equal(2, result.Count);
            Assert.Equal("Paris", result[0].Address!.City);
            Assert.Equal(new[] { 1, 2 }, result[0].Labels.Select(l => l.Id));
            Assert.Equal(new[] { "one", "two" }, result[0].Labels.Select(l => l.Value));
            Assert.Null(result[1].Address);
            Assert.Empty(result[1].Labels);
            if (tracking == QueryTrackingBehavior.TrackAll)
                Assert.All(result[0].Labels, label => Assert.Equal(5, context.Entry(label).Property<int>("ProfileId").CurrentValue));
            Assert.Equal(tracking == QueryTrackingBehavior.TrackAll ? 5 : 0, context.ChangeTracker.Entries().Count());
            Assert.Contains("LEFT JOIN", capture.Sql!);
            Assert.Matches(@"^SELECT (\w+)\.Id, \1\.Address_City, (\w+)\.ProfileId, \2\.Id, \2\.Value\r?\n", Unquote(capture.Sql!));
            Assert.Equal(1, capture.Executions);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Entity_first_single_and_default_variants_enforce_cardinality(OceanBaseCompatibilityMode mode, bool async)
        {
            foreach (var count in new[] { 0, 1, 2 })
                foreach (var single in new[] { false, true })
                    foreach (var orDefault in new[] { false, true })
                    {
                        // First relies on its server limit; Single must inspect a possible second row.
                        if (!single && count == 2) continue;
                        var rows = Enumerable.Range(1, count).Select(id => new object?[] { 1, id, "P" + id, "name" + id }).ToArray();
                        var capture = new ReaderCapture { Results = Table([typeof(int), typeof(int), typeof(string), typeof(string)], rows) };
                        await using var context = CreateContext(mode, capture);
                        var query = context.Set<Parent>().AsNoTracking().OrderBy(p => p.Id);
                        async Task<Parent?> Execute()
                            => (async, single, orDefault) switch
                            {
                                (true, true, true) => await query.SingleOrDefaultAsync(),
                                (true, true, false) => await query.SingleAsync(),
                                (true, false, true) => await query.FirstOrDefaultAsync(),
                                (true, false, false) => await query.FirstAsync(),
                                (false, true, true) => query.SingleOrDefault(),
                                (false, true, false) => query.Single(),
                                (false, false, true) => query.FirstOrDefault(),
                                (false, false, false) => query.First()
                            };
                        if (count == 0 && !orDefault || count == 2)
                            await Assert.ThrowsAsync<InvalidOperationException>(Execute);
                        else
                        {
                            var result = await Execute();
                            if (count == 0) Assert.Null(result);
                            else
                            {
                                Assert.Equal(1, result!.Id);
                                Assert.Equal("P1", result.Code);
                                Assert.Equal("name1", result.Name);
                            }
                        }
                        var limit = single ? 2 : 1;
                        Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "LIMIT " + limit
                            : "FETCH FIRST " + limit + " ROWS ONLY", capture.Sql!);
                        Assert.Empty(context.ChangeTracker.Entries());
                        Assert.Equal(1, capture.Executions);
                    }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Ordered_distinct_paging_materializes_results_and_empty_pages(
            OceanBaseCompatibilityMode mode, bool async)
        {
            foreach (var (rows, expected) in new[]
            {
                (new object?[][] { ["beta"], ["gamma"] }, new[] { "beta", "gamma" }),
                (Array.Empty<object?[]>(), Array.Empty<string>())
            })
            {
                var capture = new ReaderCapture { Results = Table([typeof(string)], rows) };
                await using var context = CreateContext(mode, capture);
                var query = context.Set<Parent>().Select(parent => parent.Name).Distinct()
                    .OrderBy(name => name).Skip(1).Take(2);
                var result = async ? await query.ToListAsync() : query.ToList();

                Assert.Equal(expected, result);
                Assert.Contains("DISTINCT", capture.Sql!, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("ORDER BY", capture.Sql!, StringComparison.OrdinalIgnoreCase);
                if (mode == OceanBaseCompatibilityMode.MySql)
                {
                    Assert.Contains("LIMIT ? OFFSET ?", capture.Sql!, StringComparison.OrdinalIgnoreCase);
                    Assert.Equal(new object?[] { 2, 1 }, capture.Values);
                }
                else
                {
                    Assert.Matches(new Regex(@"OFFSET :p ROWS FETCH NEXT :p\d+ ROWS ONLY", RegexOptions.IgnoreCase), capture.Sql!);
                    Assert.Equal(new object?[] { 1, 2 }, capture.Values);
                    Assert.DoesNotContain(" AS ", capture.Sql!, StringComparison.Ordinal);
                }
                Assert.Equal(1, capture.Executions);
            }

            var offsetCapture = new ReaderCapture { Results = Table([typeof(string)], [new object?[] { "delta" }]) };
            await using var offsetContext = CreateContext(mode, offsetCapture);
            var offsetQuery = offsetContext.Set<Parent>().Select(parent => parent.Name).OrderBy(name => name).Skip(3);
            var offsetResult = async ? await offsetQuery.ToListAsync() : offsetQuery.ToList();
            Assert.Equal(new[] { "delta" }, offsetResult);
            if (mode == OceanBaseCompatibilityMode.MySql)
                Assert.Contains("LIMIT 18446744073709551615 OFFSET ?", offsetCapture.Sql!, StringComparison.OrdinalIgnoreCase);
            else
                Assert.Contains("OFFSET :p ROWS", offsetCapture.Sql!, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(3, Convert.ToInt32(offsetCapture.Values[^1]));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Named_filters_compose_disable_selectively_and_refresh_context_parameters(OceanBaseCompatibilityMode mode, bool async)
        {
            var capture = new ReaderCapture { Results = Table([typeof(int)], [7]) };
            await using var context = CreateContext(mode, capture);
            foreach (var disabled in new[] { "none", "Active", "Tenant", "all" })
            {
                var name = "first' ? @tenant";
                IQueryable<FilteredRow> source = context.Set<FilteredRow>();
                source = disabled switch
                {
                    "none" => source,
                    "all" => source.IgnoreQueryFilters(),
                    _ => source.IgnoreQueryFilters([disabled])
                };
                var query = source.Where(row => row.Name == name).Select(row => row.Id);
                foreach (var tenant in new[] { 1, 2, 1 })
                {
                    context.TenantId = tenant;
                    name = "tenant-" + tenant + "' ? @tenant";
                    var result = async ? await query.ToListAsync() : query.ToList();
                    Assert.Equal(new[] { 7 }, result);
                    var hasTenant = disabled is "none" or "Active";
                    Assert.Equal(hasTenant ? new object?[] { tenant, name } : [name], capture.Values);
                    var sql = Unquote(capture.Sql!);
                    var alias = Regex.Match(sql, @"^SELECT (\w+)\.Id\r?\n").Groups[1].Value;
                    Assert.NotEmpty(alias);
                    string Bind(int index) => mode == OceanBaseCompatibilityMode.MySql ? "?"
                        : ":" + capture.ParameterNames[index].TrimStart(':');
                    var predicates = new List<string>();
                    if (hasTenant) predicates.Add(alias + ".TenantId = " + Bind(0));
                    if (disabled is "none" or "Tenant")
                        predicates.Add(mode == OceanBaseCompatibilityMode.MySql ? "NOT (" + alias + ".IsDeleted)"
                            : alias + ".IsDeleted = 0");
                    predicates.Add(alias + ".Name = " + Bind(hasTenant ? 1 : 0));
                    Assert.Equal(string.Join(" AND ", predicates), sql[(sql.IndexOf("WHERE ", StringComparison.Ordinal) + 6)..].Trim());
                    Assert.DoesNotContain(name, capture.Sql!);
                }
            }
            Assert.Equal(12, capture.Executions);
            Assert.Empty(context.ChangeTracker.Entries());
        }

        private static string Unquote(string sql) => sql.Replace("`", "", StringComparison.Ordinal).Replace("\"", "", StringComparison.Ordinal);

        private static GraphContext CreateContext(OceanBaseCompatibilityMode mode, ReaderCapture capture)
            => new(new DbContextOptionsBuilder<GraphContext>()
                .UseOceanBase("Server=localhost;Database=test", mode)
                .AddInterceptors(new NoNetworkConnection(), capture).Options);

        private static DataTable Table(Type[] types, params object?[][] rows)
        {
            var table = new DataTable();
            for (var i = 0; i < types.Length; i++) table.Columns.Add("c" + i, types[i]);
            foreach (var row in rows) table.Rows.Add(row.Select(value => value ?? DBNull.Value).ToArray());
            return table;
        }

        private sealed class GraphContext(DbContextOptions<GraphContext> options) : DbContext(options)
        {
            public int TenantId { get; set; } = 1;

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity<FilteredRow>(row =>
                {
                    row.Property(r => r.Name).HasMaxLength(80);
                    row.HasQueryFilter("Tenant", r => r.TenantId == TenantId);
                    row.HasQueryFilter("Active", r => !r.IsDeleted);
                });
                modelBuilder.Entity<Parent>(parent =>
                {
                    parent.ToTable("GraphParents");
                    parent.HasKey(p => new { p.TenantId, p.Id });
                    parent.Property(p => p.Id).ValueGeneratedNever();
                    parent.HasAlternateKey(p => new { p.TenantId, p.Code });
                    parent.Property(p => p.Code).HasMaxLength(32);
                    parent.Property(p => p.Name).HasColumnName("DisplayName").HasMaxLength(80);
                    parent.HasIndex(p => p.Name).HasDatabaseName("IX_Parent_Name");
                });
                modelBuilder.Entity<Child>(child =>
                {
                    child.ToTable("GraphChildren");
                    child.HasKey(c => new { c.TenantId, c.Id });
                    child.Property(c => c.Id).ValueGeneratedNever();
                    child.Property(c => c.Name).HasMaxLength(80);
                    child.HasOne(c => c.Parent).WithMany(p => p.Children)
                        .HasForeignKey(c => new { c.TenantId, c.ParentCode })
                        .HasPrincipalKey(p => new { p.TenantId, p.Code });
                });
                modelBuilder.Entity<Detail>(detail =>
                {
                    detail.ToTable("GraphDetails");
                    detail.HasKey(d => new { d.TenantId, d.ChildId });
                    detail.Property(d => d.Text).HasMaxLength(80);
                    detail.HasOne(d => d.Child).WithOne(c => c.Detail)
                        .HasForeignKey<Detail>(d => new { d.TenantId, d.ChildId });
                });
                modelBuilder.Entity<OptionalLink>(link =>
                {
                    link.HasKey(l => new { l.TenantId, l.Id });
                    link.HasOne(l => l.Parent).WithMany().HasForeignKey(l => new { l.TenantId, l.ParentId });
                    link.Property<int>("Revision").IsConcurrencyToken();
                });
                modelBuilder.Entity<Profile>(profile =>
                {
                    profile.OwnsOne(p => p.Address, address => address.Property(a => a.City).HasMaxLength(80));
                    profile.OwnsMany(p => p.Labels, labels =>
                    {
                        labels.ToTable("ProfileLabels");
                        labels.WithOwner().HasForeignKey("ProfileId");
                        labels.HasKey("ProfileId", nameof(Label.Id));
                        labels.Property(l => l.Id).ValueGeneratedNever();
                        labels.Property(l => l.Value).HasMaxLength(32);
                    });
                });
            }
        }

        private sealed class FilteredRow
        {
            public int Id { get; set; }
            public int TenantId { get; set; }
            public bool IsDeleted { get; set; }
            public string Name { get; set; } = "";
        }

        private sealed class Parent
        {
            public int TenantId { get; set; }
            public int Id { get; set; }
            public string Code { get; set; } = "";
            public string Name { get; set; } = "";
            public List<Child> Children { get; set; } = [];
        }

        private sealed class Child
        {
            public int TenantId { get; set; }
            public int Id { get; set; }
            public string ParentCode { get; set; } = "";
            public string Name { get; set; } = "";
            public Parent Parent { get; set; } = null!;
            public Detail? Detail { get; set; }
        }

        private sealed class Detail
        {
            public int TenantId { get; set; }
            public int ChildId { get; set; }
            public string Text { get; set; } = "";
            public Child Child { get; set; } = null!;
        }

        private sealed class OptionalLink
        {
            public int TenantId { get; set; }
            public int Id { get; set; }
            public int? ParentId { get; set; }
            public Parent? Parent { get; set; }
        }

        private sealed class Profile
        {
            public int Id { get; set; }
            public Address? Address { get; set; }
            public List<Label> Labels { get; set; } = [];
        }

        private sealed class Address
        {
            public string City { get; set; } = "";
        }

        private sealed class Label
        {
            public int Id { get; set; }
            public string Value { get; set; } = "";
        }

        private sealed class NoNetworkConnection : DbConnectionInterceptor
        {
            public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
                => InterceptionResult.Suppress();

            public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
                InterceptionResult result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(InterceptionResult.Suppress());
        }

        private sealed class ReaderCapture : DbCommandInterceptor
        {
            public DataTable Results { get; init; } = new();
            public string? Sql { get; private set; }
            public int Executions { get; private set; }
            public IReadOnlyList<object?> Values { get; private set; } = [];
            public IReadOnlyList<string> ParameterNames { get; private set; } = [];

            public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData,
                InterceptionResult<DbDataReader> result)
            {
                Sql = command.CommandText;
                Values = command.Parameters.Cast<DbParameter>().Select(parameter => parameter.Value).ToArray();
                ParameterNames = command.Parameters.Cast<DbParameter>().Select(parameter => parameter.ParameterName).ToArray();
                Executions++;
                return InterceptionResult<DbDataReader>.SuppressWithResult(Results.CreateDataReader());
            }

            public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
                InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(ReaderExecuting(command, eventData, result));
        }
    }
}
