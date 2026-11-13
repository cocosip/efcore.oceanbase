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

public sealed class ManyToManyRuntimeTests(ITestOutputHelper output)
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
    public void Skip_navigation_queries_compile_with_join_filters_and_quantifiers(OceanBaseCompatibilityMode mode)
    {
        using var context = Create(mode, new Commands());
        var include = context.Set<Student>().Include(s => s.Courses).OrderBy(s => s.Id);
        var filtered = context.Set<Student>().Include(s => s.Courses.Where(c => c.Id > 10).OrderBy(c => c.Id).Take(1)).OrderBy(s => s.Id);
        var predicate = context.Set<Student>().Where(s => s.Courses.Any(c => c.Name == "math")).Select(s => s.Id);
        output.WriteLine(include.ToQueryString());
        output.WriteLine(filtered.ToQueryString());
        output.WriteLine(predicate.ToQueryString());
        Assert.Contains("Memberships", include.ToQueryString());
        Assert.Contains("LEFT JOIN", include.ToQueryString());
        Assert.Contains("ROW_NUMBER() OVER", filtered.ToQueryString());
        Assert.Contains("EXISTS", predicate.ToQueryString());
        Assert.DoesNotContain("APPLY", filtered.ToQueryString());
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Navigation_predicates_refresh_filters_and_bind_values(OceanBaseCompatibilityMode mode, bool async)
    {
        var capture = new Commands();
        await using var context = Create(mode, capture);
        var name = "math";
        var students = context.Set<Student>().Where(s => s.Courses.Any(c => c.Name == name));
        var query = students.Select(s => s.Id);
        capture.Results = Table([typeof(int)], [1], [2]);
        Assert.Equal(new[] { 1, 2 }, async ? await query.ToArrayAsync() : query.ToArray());
        var first = Assert.Single(capture.Items);
        Assert.Equal(new object?[] { 1, "math" }, first.Inputs);
        Assert.Contains("EXISTS", first.Sql);
        Assert.Contains("INNER JOIN", first.Sql);
        Assert.Contains("WHERE c.TenantId = ?", Template(first.Sql));
        Assert.Matches(@"WHERE s.Id = m.StudentId AND c0.Name =", Unquote(first.Sql));
        context.TenantId = 2;
        name = "a'_%";
        capture.Items.Clear();
        capture.Results = Table([typeof(int)]);
        Assert.Empty(async ? await query.ToArrayAsync() : query.ToArray());
        var next = Assert.Single(capture.Items);
        Assert.Equal(first.Sql, next.Sql);
        Assert.Equal(new object?[] { 2, name }, next.Inputs);
        Assert.DoesNotContain(name, next.Sql);
        capture.Items.Clear();
        var unfiltered = students.IgnoreQueryFilters().Select(s => s.Id);
        _ = async ? await unfiltered.ToArrayAsync() : unfiltered.ToArray();
        Assert.Equal(new object?[] { name }, Assert.Single(capture.Items).Inputs);
        Assert.DoesNotContain("TenantId", capture.Items[0].Sql);
    }

    [Theory]
    [MemberData(nameof(TrackingModes))]
    public async Task Include_materializes_shared_related_identities_and_empty_collections(
        OceanBaseCompatibilityMode mode, bool async, QueryTrackingBehavior tracking)
    {
        var capture = new Commands();
        await using var context = Create(mode, capture);
        context.ChangeTracker.QueryTrackingBehavior = tracking;
        var query = context.Set<Student>().Include(s => s.Courses).OrderBy(s => s.Id);
        output.WriteLine(query.ToQueryString());
        capture.Results = IncludeTable(tracking,
            [1, "one", 10, 1, 10, "math", 1], [1, "one", 20, 1, 20, "science", 1],
            [2, "two", 10, 2, 10, "math", 1], [3, "empty", null, null, null, null, null]);
        var rows = async ? await query.ToListAsync() : query.ToList();
        Assert.Equal(new[] { 1, 2, 3 }, rows.Select(s => s.Id));
        Assert.Equal(new[] { 10, 20 }, rows[0].Courses.Select(c => c.Id));
        Assert.Empty(rows[2].Courses);
        Assert.All(rows.Take(2), s => Assert.All(s.Courses, c => Assert.Contains(s, c.Students)));
        var first = rows[0].Courses[0];
        var second = Assert.Single(rows[1].Courses);
        Assert.Equal(tracking != QueryTrackingBehavior.NoTracking, ReferenceEquals(first, second));
        Assert.Equal(tracking == QueryTrackingBehavior.NoTracking ? 1 : 2, first.Students.Count);
        Assert.Equal(tracking == QueryTrackingBehavior.TrackAll ? 8 : 0, context.ChangeTracker.Entries().Count());
        if (tracking == QueryTrackingBehavior.TrackAll)
        {
            Assert.Equal(3, context.ChangeTracker.Entries().Count(e => e.Metadata.Name == "Membership"));
            Assert.All(rows, s => Assert.True(context.Entry(s).Collection(s => s.Courses).IsLoaded));
        }
        AssertIncludeProjection(Assert.Single(capture.Items).Sql, "s0", tracking);
        capture.Items.Clear();
        context.ChangeTracker.Clear();
        capture.Results = IncludeTable(tracking);
        Assert.Empty(async ? await query.ToListAsync() : query.ToList());
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Single(capture.Items);
    }

    [Theory]
    [MemberData(nameof(TrackingModes))]
    public async Task Filtered_include_limits_each_parent_and_preserves_inverse_fixup(
        OceanBaseCompatibilityMode mode, bool async, QueryTrackingBehavior tracking)
    {
        var capture = new Commands();
        await using var context = Create(mode, capture);
        context.ChangeTracker.QueryTrackingBehavior = tracking;
        var minimum = 10;
        var query = context.Set<Student>().Include(s => s.Courses.Where(c => c.Id > minimum)
            .OrderBy(c => c.Id).Take(1)).OrderBy(s => s.Id);
        output.WriteLine(query.ToQueryString());
        capture.Results = IncludeTable(tracking,
            [1, "one", 20, 1, 20, "science", 1], [2, "empty", null, null, null, null, null]);
        var rows = async ? await query.ToListAsync() : query.ToList();
        Assert.Equal(20, Assert.Single(rows[0].Courses).Id);
        Assert.Same(rows[0], Assert.Single(rows[0].Courses[0].Students));
        Assert.Empty(rows[1].Courses);
        var command = Assert.Single(capture.Items);
        AssertIncludeProjection(command.Sql, "s1", tracking);
        var sql = Unquote(command.Sql);
        Assert.Contains("ROW_NUMBER() OVER(PARTITION BY m.StudentId ORDER BY c0.Id)", sql);
        Assert.Contains("WHERE s0.row <= 1", sql);
        Assert.Contains("WHERE c0.Id > ?", Template(command.Sql));
        Assert.Contains("WHERE c.TenantId = ?", Template(command.Sql));
        Assert.Equal(new object?[] { 1, minimum }, command.Inputs);
        Assert.Equal(tracking == QueryTrackingBehavior.TrackAll ? 4 : 0, context.ChangeTracker.Entries().Count());
    }

    private static DataTable IncludeTable(QueryTrackingBehavior tracking, params object?[][] rows)
        => tracking == QueryTrackingBehavior.NoTracking
            ? Table([typeof(int), typeof(string), typeof(int), typeof(string), typeof(int), typeof(int), typeof(int)],
                rows.Select(r => new[] { r[0], r[1], r[4], r[5], r[6], r[2], r[3] }).ToArray())
            : Table([typeof(int), typeof(string), typeof(int), typeof(int), typeof(int), typeof(string), typeof(int)], rows);

    private static void AssertIncludeProjection(string command, string related, QueryTrackingBehavior tracking)
    {
        var sql = Unquote(command);
        Assert.StartsWith(tracking == QueryTrackingBehavior.NoTracking
            ? $"SELECT s.Id, s.Name, {related}.Id, {related}.Name, {related}.TenantId, {related}.CourseId, {related}.StudentId"
            : $"SELECT s.Id, s.Name, {related}.CourseId, {related}.StudentId, {related}.Id, {related}.Name, {related}.TenantId", sql);
        Assert.Contains($"ON s.Id = {related}.StudentId", sql);
        Assert.Contains("ON m.CourseId = c0.Id", sql);
    }

    [Theory]
    [MemberData(nameof(TrackingModes))]
    public async Task Explicit_join_include_materializes_payload_and_preserves_related_entities(OceanBaseCompatibilityMode mode, bool async,
        QueryTrackingBehavior tracking)
    {
        var capture = new Commands();
        await using var context = Create(mode, capture);
        context.ChangeTracker.QueryTrackingBehavior = tracking;
        var query = context.Set<Article>().Include(a => a.Tags).OrderBy(a => a.Id);
        output.WriteLine(query.ToQueryString());
        capture.Results = tracking == QueryTrackingBehavior.NoTracking
            ? Table([typeof(int), typeof(string), typeof(int), typeof(string), typeof(int), typeof(int)],
                [1, "article", 10, "tag", 1, 10], [2, "empty", null, null, null, null])
            : Table([typeof(int), typeof(string), typeof(int), typeof(int), typeof(string), typeof(int), typeof(int), typeof(string)],
                [1, "article", 1, 10, null, 7, 10, "tag"], [2, "empty", null, null, null, null, null, null]);
        var rows = async ? await query.ToListAsync() : query.ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal("tag", Assert.Single(rows[0].Tags).Name);
        Assert.Same(rows[0], Assert.Single(rows[0].Tags[0].Articles));
        Assert.Empty(rows[1].Tags);
        if (tracking == QueryTrackingBehavior.TrackAll)
        {
            var join = Assert.Single(context.ChangeTracker.Entries<ArticleTag>()).Entity;
            Assert.Equal(1, join.ArticleId);
            Assert.Equal(10, join.TagId);
            Assert.Null(join.Note);
            Assert.Equal(7, context.Entry(join).Property(l => l.Version).OriginalValue);
        }
        else Assert.Empty(context.ChangeTracker.Entries());
        var sql = Unquote(Assert.Single(capture.Items).Sql);
        Assert.StartsWith(tracking == QueryTrackingBehavior.NoTracking
            ? "SELECT a.Id, a.Title, s.Id, s.Name, s.ArticleId, s.TagId"
            : "SELECT a.Id, a.Title, s.ArticleId, s.TagId, s.Note, s.Version, s.Id, s.Name", sql);
        Assert.Contains("ON a.Id = s.ArticleId", sql);
        Assert.Contains("ON a0.TagId = t.Id", sql);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task New_principals_propagate_generated_keys_to_shared_join_rows(OceanBaseCompatibilityMode mode, bool async)
    {
        var capture = new Commands { DistinctGeneratedKeys = true };
        await using var context = Create(mode, capture);
        var student = new Student { Name = "student" };
        var course = new Course { Name = "math", TenantId = 1 };
        student.Courses.Add(course);
        context.Add(student);
        Assert.True(context.Entry(student).Property(s => s.Id).IsTemporary);
        Assert.True(context.Entry(course).Property(c => c.Id).IsTemporary);
        Assert.Equal(3, async ? await context.SaveChangesAsync() : context.SaveChanges());
        Assert.Equal(201, student.Id);
        Assert.Equal(101, course.Id);
        Assert.Same(student, Assert.Single(course.Students));
        var join = Assert.Single(context.ChangeTracker.Entries(), e => e.Metadata.Name == "Membership");
        Assert.Equal(101, join.Property("CourseId").CurrentValue);
        Assert.Equal(201, join.Property("StudentId").CurrentValue);
        Assert.All(context.ChangeTracker.Entries(), e => Assert.Equal(EntityState.Unchanged, e.State));
        var inserts = capture.Items.Where(c => c.Sql.StartsWith("INSERT", StringComparison.Ordinal)).ToArray();
        Assert.Equal(3, inserts.Length);
        Assert.Equal("Memberships", inserts[2].Table);
        Assert.Equal(new object?[] { 101, 201 }, inserts[2].Inputs);
        Assert.DoesNotContain("RETURNING", inserts[2].Sql);
        Assert.Equal(mode == OceanBaseCompatibilityMode.MySql ? 2 : 0, capture.Items.Count(c => c.Sql.Contains("LAST_INSERT_ID")));
        var native = Native(context);
        var transaction = Assert.Single(native.Transactions);
        Assert.Equal(1, transaction.Commits);
        Assert.Equal(1, transaction.Disposals);
        Assert.All(capture.Items, c => Assert.Same(transaction, c.Transaction));
        Assert.Equal(ConnectionState.Closed, native.State);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Relationship_changes_write_only_join_rows_and_delete_links_before_a_principal(OceanBaseCompatibilityMode mode, bool async)
    {
        var capture = new Commands();
        await using var context = Create(mode, capture);
        var student = new Student { Id = 1, Name = "one" };
        var course = new Course { Id = 10, Name = "math", TenantId = 1 };
        context.AttachRange(student, course);
        student.Courses.Add(course);
        Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
        var insert = Assert.Single(capture.Items);
        Assert.Equal("INSERT INTO Memberships (CourseId, StudentId) VALUES (?, ?);", Template(insert.Sql));
        Assert.Equal(new object?[] { 10, 1 }, insert.Inputs);
        Assert.Same(student, Assert.Single(course.Students));
        capture.Items.Clear();
        student.Courses.Remove(course);
        Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
        var delete = Assert.Single(capture.Items);
        Assert.Equal("DELETE FROM Memberships WHERE CourseId = ? AND StudentId = ?;", Template(delete.Sql));
        Assert.Equal(new object?[] { 10, 1 }, delete.Inputs);
        Assert.Empty(course.Students);
        Assert.Equal(2, context.ChangeTracker.Entries().Count());
        capture.Items.Clear();
        student.Courses.Add(course);
        _ = async ? await context.SaveChangesAsync() : context.SaveChanges();
        capture.Items.Clear();
        context.Remove(student);
        Assert.Equal(2, async ? await context.SaveChangesAsync() : context.SaveChanges());
        Assert.Equal(new[] { "Memberships", "Students" }, capture.Items.Select(c => c.Table));
        Assert.Equal(new object?[] { 10, 1, 1 }, capture.Items.SelectMany(c => c.Inputs));
        Assert.Equal(EntityState.Unchanged, context.Entry(course).State);
        Assert.Single(context.ChangeTracker.Entries());
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Explicit_join_payload_uses_original_nullable_concurrency_and_surfaces_stale_links(OceanBaseCompatibilityMode mode, bool async)
    {
        foreach (int? original in new int?[] { null, 7 })
        foreach (var delete in new[] { false, true })
        foreach (var stale in new[] { false, true })
        {
            var capture = new Commands { ZeroRows = stale };
            await using var context = Create(mode, capture);
            var link = new ArticleTag { ArticleId = 1, TagId = 10, Note = "old", Version = original };
            context.Attach(link);
            if (delete) context.Remove(link);
            else { link.Note = null; link.Version = 9; }
            if (stale)
            {
                var error = async ? await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => context.SaveChangesAsync())
                    : Assert.Throws<DbUpdateConcurrencyException>(() => context.SaveChanges());
                Assert.Same(link, Assert.Single(error.Entries).Entity);
                Assert.Equal(delete ? EntityState.Deleted : EntityState.Modified, context.Entry(link).State);
                Assert.Equal(original, context.Entry(link).Property(l => l.Version).OriginalValue);
            }
            else
            {
                Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
                Assert.Equal(delete ? EntityState.Detached : EntityState.Unchanged, context.Entry(link).State);
            }
            var command = Assert.Single(capture.Items);
            var expected = delete ? new List<object?>() : new List<object?> { DBNull.Value, 9 };
            expected.AddRange([1, 10]);
            if (original.HasValue) expected.Add(original.Value);
            Assert.Equal(expected, command.Inputs);
            Assert.Equal((delete ? "DELETE FROM ArticleTags" : "UPDATE ArticleTags SET Note = ?, Version = ?")
                + " WHERE ArticleId = ? AND TagId = ? AND Version " + (original is null ? "IS NULL;" : "= ?;"), Template(command.Sql));
        }
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Explicit_join_creation_and_skip_removal_preserve_payload_and_principals(OceanBaseCompatibilityMode mode, bool async)
    {
        var capture = new Commands();
        await using var context = Create(mode, capture);
        var article = new Article { Id = 1, Title = "one" };
        var tag = new Tag { Id = 10, Name = "tag" };
        context.AttachRange(article, tag);
        article.Tags.Add(tag);
        context.ChangeTracker.DetectChanges();
        var link = Assert.Single(context.ChangeTracker.Entries<ArticleTag>()).Entity;
        link.Note = "payload";
        link.Version = 7;
        Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
        Assert.Equal(new object?[] { 1, 10, "payload", 7 }, Assert.Single(capture.Items).Inputs);
        Assert.Equal("INSERT INTO ArticleTags (ArticleId, TagId, Note, Version) VALUES (?, ?, ?, ?);", Template(capture.Items[0].Sql));
        capture.Items.Clear();
        article.Tags.Remove(tag);
        Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
        Assert.Equal("DELETE FROM ArticleTags WHERE ArticleId = ? AND TagId = ? AND Version = ?;", Template(Assert.Single(capture.Items).Sql));
        Assert.Equal(new object?[] { 1, 10, 7 }, capture.Items[0].Inputs);
        Assert.Empty(tag.Articles);
        Assert.All(context.ChangeTracker.Entries(), e => Assert.Equal(EntityState.Unchanged, e.State));
        Assert.Equal(2, context.ChangeTracker.Entries().Count());
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Link_failure_preserves_pending_changes_and_recovers_caller_or_owned_transaction(OceanBaseCompatibilityMode mode, bool async)
    {
        foreach (var callerOwned in new[] { false, true })
        {
            var capture = new Commands();
            await using var context = Create(mode, capture);
            var native = Native(context);
            using var caller = callerOwned ? (async ? await context.Database.BeginTransactionAsync() : context.Database.BeginTransaction()) : null;
            var student = new Student { Id = 1, Name = "one" };
            var courses = new[] { new Course { Id = 10, Name = "math" }, new Course { Id = 20, Name = "science" } };
            context.Attach(student);
            context.AttachRange(courses);
            student.Courses.AddRange(courses);
            var failure = new InvalidOperationException("Synthetic second join insert failure.");
            native.WriteFailure = failure;
            var actual = async ? await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync())
                : Assert.Throws<DbUpdateException>(() => context.SaveChanges());
            Assert.Same(failure, actual.InnerException);
            Assert.Equal("Membership", Assert.Single(actual.Entries).Metadata.Name);
            var joins = context.ChangeTracker.Entries().Where(e => e.Metadata.Name == "Membership").ToArray();
            Assert.Equal(2, joins.Length);
            Assert.All(joins, e => Assert.Equal(EntityState.Added, e.State));
            Assert.All(capture.Items, c => Assert.Equal("Memberships", c.Table));
            var transaction = Assert.Single(native.Transactions);
            Assert.Equal(0, transaction.Commits);
            if (callerOwned)
            {
                Assert.Same(caller, context.Database.CurrentTransaction);
                Assert.Equal(0, transaction.Disposals);
                Assert.Contains(native.Commands, c => c.Sql.StartsWith("ROLLBACK TO", StringComparison.Ordinal));
                Assert.All(native.Commands, c => Assert.Same(transaction, c.Transaction));
                native.WriteFailure = null;
                capture.Items.Clear();
                Assert.Equal(2, async ? await context.SaveChangesAsync() : context.SaveChanges());
                Assert.All(joins, e => Assert.Equal(EntityState.Unchanged, e.State));
                Assert.Equal(0, transaction.Commits);
                if (async) await caller!.CommitAsync(); else caller!.Commit();
            }
            else
            {
                Assert.Null(context.Database.CurrentTransaction);
                Assert.Equal(1, transaction.Disposals);
                Assert.True(transaction.DisposedActive);
                Assert.Equal(ConnectionState.Closed, native.State);
            }
        }
    }

    private static string Template(string sql) => Regex.Replace(Regex.Replace(Unquote(sql), @"[@:]\w+", "?"), @"\s+", " ").Trim();
    private static DataTable Table(Type[] types, params object?[][] rows)
    {
        var table = new DataTable();
        foreach (var type in types) table.Columns.Add("C" + table.Columns.Count, type);
        foreach (var row in rows) table.Rows.Add(row.Select(v => v ?? DBNull.Value).ToArray());
        return table;
    }

    private static Context Create(OceanBaseCompatibilityMode mode, Commands capture)
    {
        return new Context(new DbContextOptionsBuilder().UseOceanBase("Server=offline;Database=links", mode)
            .ReplaceService<IRelationalConnection, TransactionTests.TestConnection>()
            .EnableServiceProviderCaching(false).AddInterceptors(capture).Options);
    }
    private static TransactionTests.NativeConnection Native(DbContext context)
        => Assert.IsType<TransactionTests.NativeConnection>(context.Database.GetDbConnection());

    private sealed class Context(DbContextOptions options) : DbContext(options)
    {
        public int TenantId { get; set; } = 1;
        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<Student>().ToTable("Students").Property(s => s.Name).HasMaxLength(40);
            model.Entity<Course>().ToTable("Courses").Property(c => c.Name).HasMaxLength(40);
            model.Entity<Course>().HasQueryFilter(c => c.TenantId == TenantId);
            model.Entity<Student>().HasMany(s => s.Courses).WithMany(c => c.Students)
                .UsingEntity<Dictionary<string, object>>("Membership",
                    r => r.HasOne<Course>().WithMany().HasForeignKey("CourseId"),
                    l => l.HasOne<Student>().WithMany().HasForeignKey("StudentId"),
                    j => { j.ToTable("Memberships"); j.HasKey("CourseId", "StudentId"); });
            model.Entity<Article>().ToTable("Articles").Property(a => a.Title).HasMaxLength(40);
            model.Entity<Tag>().ToTable("Tags").Property(t => t.Name).HasMaxLength(40);
            model.Entity<Article>().HasMany(a => a.Tags).WithMany(t => t.Articles).UsingEntity<ArticleTag>(
                r => r.HasOne<Tag>().WithMany().HasForeignKey(l => l.TagId),
                l => l.HasOne<Article>().WithMany().HasForeignKey(l => l.ArticleId),
                j => { j.ToTable("ArticleTags"); j.HasKey(l => new { l.ArticleId, l.TagId });
                    j.Property(l => l.Note).HasMaxLength(60); j.Property(l => l.Version).IsConcurrencyToken(); });
        }
    }
    private sealed class Student { public int Id { get; set; } public string Name { get; set; } = ""; public List<Course> Courses { get; set; } = []; }
    private sealed class Course { public int Id { get; set; } public string Name { get; set; } = ""; public int TenantId { get; set; } public List<Student> Students { get; set; } = []; }
    private sealed class Article { public int Id { get; set; } public string Title { get; set; } = ""; public List<Tag> Tags { get; set; } = []; }
    private sealed class Tag { public int Id { get; set; } public string Name { get; set; } = ""; public List<Article> Articles { get; set; } = []; }
    private sealed class ArticleTag { public int ArticleId { get; set; } public int TagId { get; set; } public string? Note { get; set; } public int? Version { get; set; } }

    private sealed record Command(string Sql, object?[] Inputs, DbTransaction? Transaction)
    {
        public string Table => Regex.Match(Unquote(Sql), @"(?:INSERT INTO|UPDATE|DELETE FROM) (\w+)").Groups[1].Value;
    }
    private static string Unquote(string sql) => sql.Replace("`", "").Replace("\"", "");
    private sealed class Commands : DbCommandInterceptor
    {
        public DataTable? Results { get; set; }
        public bool ZeroRows { get; set; }
        public bool DistinctGeneratedKeys { get; init; }
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
        { Capture(command); return ZeroRows ? InterceptionResult<int>.SuppressWithResult(0) : result; }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<int> result, CancellationToken token = default) => ValueTask.FromResult(NonQueryExecuting(command, data, result));
        public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData data, int result)
        {
            if (DistinctGeneratedKeys && command.CommandText.Contains("RETURNING", StringComparison.Ordinal))
                foreach (DbParameter parameter in command.Parameters)
                    if (parameter.Direction == ParameterDirection.Output)
                        parameter.Value = command.CommandText.Contains("Students", StringComparison.Ordinal) ? 201 : 101;
            return result;
        }
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData data, int result,
            CancellationToken token = default) => ValueTask.FromResult(NonQueryExecuted(command, data, result));
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result)
        {
            Capture(command);
            if (DistinctGeneratedKeys && command.CommandText.Contains("LAST_INSERT_ID", StringComparison.Ordinal))
                return InterceptionResult<DbDataReader>.SuppressWithResult(Table([typeof(int)],
                    [command.CommandText.Contains("Students", StringComparison.Ordinal) ? 201 : 101]).CreateDataReader());
            return Results is null ? result : InterceptionResult<DbDataReader>.SuppressWithResult(Results.CreateDataReader());
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken token = default) => ValueTask.FromResult(ReaderExecuting(command, data, result));
    }
}
