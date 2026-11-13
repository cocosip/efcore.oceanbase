using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.FunctionalTests;

public sealed class SplitQueryRuntimeTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var mode in Enum.GetValues<OceanBaseCompatibilityMode>())
            foreach (var async in new[] { false, true })
                foreach (var tracking in Enum.GetValues<QueryTrackingBehavior>()) yield return [mode, async, tracking];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Sibling_and_nested_split_collections_buffer_before_opening_the_next_reader(
        OceanBaseCompatibilityMode mode, bool async, QueryTrackingBehavior tracking)
    {
        var capture = new Commands();
        await using var context = Create(mode, capture);
        context.ChangeTracker.QueryTrackingBehavior = tracking;
        var query = context.Set<Parent>().Include(p => p.Children).ThenInclude(c => c.Notes).Include(p => p.Tags)
            .AsSplitQuery().OrderBy(p => p.Id);
        var result = async ? await query.ToArrayAsync() : query.ToArray();
        Assert.Equal(new[] { 1, 2 }, result.Select(p => p.Id));
        Assert.Equal(new[] { 11, 12 }, result[0].Children.Select(c => c.Id));
        Assert.All(result[0].Children, child => Assert.Same(result[0], child.Parent));
        var note = Assert.Single(result[0].Children[0].Notes);
        Assert.Equal(111, note.Id);
        Assert.Same(result[0].Children[0], note.Child);
        Assert.Empty(result[0].Children[1].Notes);
        Assert.Equal(21, Assert.Single(result[0].Tags).Id);
        Assert.Same(result[0], result[0].Tags[0].Parent);
        Assert.Empty(result[1].Children);
        Assert.Empty(result[1].Tags);
        Assert.Equal(tracking == QueryTrackingBehavior.TrackAll ? 6 : 0, context.ChangeTracker.Entries().Count());
        Assert.Equal(4, capture.Items.Count);
        Assert.All(capture.Readers, reader => Assert.True(reader.Disposed));
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        Assert.All(capture.Items, command => Assert.Contains(7, command.Inputs));
        var sql = capture.Items.Select(command => Unquote(command.Sql)).ToArray();
        Assert.Matches(@"SELECT (\w+)\.Id, \1\.Name, \1\.TenantId\r?\n", sql[0]);
        Assert.Matches(@"SELECT (\w+)\.Id, \1\.Name, \1\.ParentId, (\w+)\.Id\r?\n", sql[1]);
        Assert.Matches(@"SELECT (\w+)\.Id, \1\.ChildId, \1\.Text, (\w+)\.Id, (\w+)\.Id\r?\n", sql[2]);
        Assert.Matches(@"SELECT (\w+)\.Id, \1\.ParentId, \1\.Text, (\w+)\.Id\r?\n", sql[3]);
        Assert.All(sql.Skip(1), command => Assert.Contains("INNER JOIN", command));
        Assert.All(sql, command => Assert.Contains("ORDER BY", command));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Configured_split_queries_keep_current_filters_and_deterministic_paging_in_each_command(
        OceanBaseCompatibilityMode mode, bool async, QueryTrackingBehavior tracking)
    {
        var capture = new Commands();
        await using var context = Create(mode, capture, split: true);
        context.ChangeTracker.QueryTrackingBehavior = tracking;
        for (var tenant = 7; tenant <= 8; tenant++)
        {
            context.Tenant = tenant;
            context.ChangeTracker.Clear();
            capture.Items.Clear();
            var query = context.Set<Parent>().Include(p => p.Children.Where(c => c.Id > 10).OrderBy(c => c.Id).Take(2))
                .ThenInclude(c => c.Notes).Include(p => p.Tags).OrderBy(p => p.Id).Take(2);
            var values = async ? await query.ToArrayAsync() : query.ToArray();
            Assert.Equal(2, values.Length);
            Assert.All(values, parent => Assert.Equal(tenant, parent.TenantId));
            Assert.Equal(2, values[0].Children.Count);
            Assert.Single(values[0].Children[0].Notes);
            Assert.Equal(4, capture.Items.Count);
            foreach (var command in capture.Items)
            {
                Assert.Contains(tenant, command.Inputs);
                Assert.Contains(2, command.Inputs);
                var sql = Unquote(command.Sql);
                Assert.Matches(@"TenantId = [:@?]", sql);
                var parent = Regex.Match(sql, @"FROM SqParents (?:AS )?(\w+)").Groups[1].Value;
                Assert.NotEmpty(parent);
                Assert.Matches($@"ORDER BY {parent}\.Id\s+{(mode == OceanBaseCompatibilityMode.MySql ? "LIMIT" : "FETCH")}", sql);
            }
            foreach (var command in capture.Items.Skip(1).Take(2))
            {
                var sql = Unquote(command.Sql);
                var child = Regex.Match(sql, @"FROM SqChildren (?:AS )?(\w+)").Groups[1].Value;
                Assert.NotEmpty(child);
                Assert.Contains($"ROW_NUMBER() OVER(PARTITION BY {child}.ParentId ORDER BY {child}.Id)", sql);
                Assert.Contains($"WHERE {child}.Id > 10", sql);
                Assert.Matches(@"\) (?:AS )?(\w+)\s+WHERE \1\.row <= 2\s*\)", sql);
                var join = Regex.Match(sql, @"\) (?:AS )?(?<child>\w+) ON (?<parent>\w+)\.Id = \k<child>\.ParentId");
                Assert.True(join.Success, sql);
                var parentKey = join.Groups["parent"].Value;
                var childKey = join.Groups["child"].Value;
                Assert.EndsWith($"ORDER BY {parentKey}.Id, {childKey}.ParentId, {childKey}.Id", sql.TrimEnd());
                if (sql.Contains("SqNotes", StringComparison.Ordinal))
                    Assert.Matches($@"INNER JOIN SqNotes (?:AS )?(\w+) ON {childKey}\.Id = \1\.ChildId", sql);
            }
            var tagsSql = Unquote(capture.Items[3].Sql);
            var tags = Regex.Match(tagsSql, @"\) (?:AS )?(?<parent>\w+)\s+INNER JOIN SqTags (?:AS )?(?<tag>\w+) ON \k<parent>\.Id = \k<tag>\.ParentId");
            Assert.True(tags.Success, tagsSql);
            Assert.EndsWith($"ORDER BY {tags.Groups["parent"].Value}.Id", tagsSql.TrimEnd());
            Assert.Equal(tracking == QueryTrackingBehavior.TrackAll ? 6 : 0, context.ChangeTracker.Entries().Count());
        }
        Assert.All(capture.Readers, reader => Assert.True(reader.Disposed));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Single_query_override_preserves_one_command_under_a_split_default(
        OceanBaseCompatibilityMode mode, bool async, QueryTrackingBehavior tracking)
    {
        var capture = new Commands { SingleCollection = true };
        await using var context = Create(mode, capture, split: true);
        context.ChangeTracker.QueryTrackingBehavior = tracking;
        var query = context.Set<Parent>().Include(p => p.Children).AsSingleQuery().OrderBy(p => p.Id);
        var values = async ? await query.ToArrayAsync() : query.ToArray();
        Assert.Equal(2, values.Length);
        Assert.Equal(new[] { 11, 12 }, values[0].Children.Select(child => child.Id));
        Assert.Empty(values[1].Children);
        Assert.Equal(tracking == QueryTrackingBehavior.TrackAll ? 4 : 0, context.ChangeTracker.Entries().Count());
        Assert.Single(capture.Items);
        Assert.True(Assert.Single(capture.Readers).Disposed);
        Assert.Contains("LEFT JOIN", capture.Items[0].Sql);
        Assert.Matches(@"SELECT (\w+)\.Id, \1\.Name, \1\.TenantId, (\w+)\.Id, \2\.Name, \2\.ParentId", Unquote(capture.Items[0].Sql));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Empty_roots_and_empty_collections_dispose_readers_without_creating_phantom_children(
        OceanBaseCompatibilityMode mode, bool async, QueryTrackingBehavior tracking)
    {
        foreach (var emptyRoots in new[] { false, true })
        {
            var capture = new Commands { EmptyRoots = emptyRoots, EmptyCollections = true };
            await using var context = Create(mode, capture, split: true);
            context.ChangeTracker.QueryTrackingBehavior = tracking;
            var query = context.Set<Parent>().Include(p => p.Children).Include(p => p.Tags).OrderBy(p => p.Id);
            var values = async ? await query.ToArrayAsync() : query.ToArray();
            Assert.Equal(emptyRoots ? 0 : 2, values.Length);
            Assert.All(values, parent => { Assert.Empty(parent.Children); Assert.Empty(parent.Tags); });
            Assert.Equal(emptyRoots ? 1 : 3, capture.Items.Count);
            Assert.Equal(!emptyRoots && tracking == QueryTrackingBehavior.TrackAll ? 2 : 0, context.ChangeTracker.Entries().Count());
            Assert.All(capture.Readers, reader => Assert.True(reader.Disposed));
            Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Later_buffered_command_retries_without_duplicate_relationships(
        OceanBaseCompatibilityMode mode, bool async, QueryTrackingBehavior tracking)
    {
        var error = ServerError(mode, unknown: false);
        var capture = new Commands { ReadFailure = error };
        await using var context = Create(mode, capture, split: true, retry: true);
        context.ChangeTracker.QueryTrackingBehavior = tracking;
        var query = context.Set<Parent>().Include(p => p.Children).ThenInclude(c => c.Notes).Include(p => p.Tags).OrderBy(p => p.Id);
        var values = async ? await query.ToArrayAsync() : query.ToArray();
        Assert.Equal(2, values.Length);
        Assert.Equal(new[] { 11, 12 }, values[0].Children.Select(child => child.Id));
        Assert.Single(values[0].Children[0].Notes);
        Assert.Single(values[0].Tags);
        Assert.Equal(5, capture.Items.Count);
        Assert.Equal(capture.Items[1].Sql, capture.Items[2].Sql);
        Assert.Equal(capture.Items[1].Inputs, capture.Items[2].Inputs);
        Assert.Equal(tracking == QueryTrackingBehavior.TrackAll ? 6 : 0, context.ChangeTracker.Entries().Count());
        Assert.All(capture.Readers, reader => Assert.True(reader.Disposed));
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Unknown_or_cancelled_later_reader_preserves_error_and_caller_lease_without_replay(
        OceanBaseCompatibilityMode mode, bool async, QueryTrackingBehavior tracking)
    {
        foreach (var cancel in new[] { false, true })
        foreach (var outerLease in new[] { false, true })
        {
            using var source = new CancellationTokenSource();
            Exception error = cancel ? new OperationCanceledException(source.Token) : ServerError(mode, unknown: true);
            var capture = new Commands { ReadFailure = error, BeforeFailure = cancel ? source.Cancel : null };
            await using var context = Create(mode, capture, split: true, retry: true);
            context.ChangeTracker.QueryTrackingBehavior = tracking;
            if (outerLease) { if (async) await context.Database.OpenConnectionAsync(); else context.Database.OpenConnection(); }
            var query = context.Set<Parent>().Include(p => p.Children).OrderBy(p => p.Id);
            var actual = async ? await Record.ExceptionAsync(() => query.ToArrayAsync(source.Token)) : Record.Exception(() => query.ToArray());
            Assert.Same(error, actual);
            Assert.Equal(2, capture.Items.Count);
            Assert.All(capture.Readers, reader => Assert.True(reader.Disposed));
            Assert.Equal(outerLease ? ConnectionState.Open : ConnectionState.Closed, context.Database.GetDbConnection().State);
            context.ChangeTracker.Clear();
            var values = async ? await query.ToArrayAsync() : query.ToArray();
            Assert.Equal(2, values[0].Children.Count);
            Assert.Equal(4, capture.Items.Count);
            if (outerLease) { if (async) await context.Database.CloseConnectionAsync(); else context.Database.CloseConnection(); }
            Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        }
    }

    private static OceanBaseException ServerError(OceanBaseCompatibilityMode mode, bool unknown)
        => (OceanBaseException)Activator.CreateInstance(typeof(OceanBaseException), BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null, args: ["Synthetic split read error.", mode == OceanBaseCompatibilityMode.MySql ? 1213 : 60, null, unknown, null, null], culture: null)!;

    private static Context Create(OceanBaseCompatibilityMode mode, Commands capture, bool split = false, bool retry = false)
        => new(new DbContextOptionsBuilder<Context>().UseOceanBase("Server=offline;Database=app", mode, options =>
            { if (split) options.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery); if (retry) options.EnableRetryOnFailure(2, TimeSpan.Zero); })
            .ReplaceService<IRelationalConnection, TransactionTests.TestConnection>()
            .EnableServiceProviderCaching(false).AddInterceptors(capture).Options);
    private static string Unquote(string sql) => sql.Replace("`", "", StringComparison.Ordinal).Replace("\"", "", StringComparison.Ordinal);
    private sealed class Context(DbContextOptions<Context> options) : DbContext(options)
    {
        public int Tenant { get; set; } = 7;
        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<Parent>().ToTable("SqParents").HasQueryFilter(p => p.TenantId == Tenant);
            model.Entity<Parent>().Property(p => p.Name).HasMaxLength(32);
            model.Entity<Child>().ToTable("SqChildren").HasOne(c => c.Parent).WithMany(p => p.Children).HasForeignKey(c => c.ParentId);
            model.Entity<Child>().Property(c => c.Name).HasMaxLength(32);
            model.Entity<Note>().ToTable("SqNotes").HasOne(n => n.Child).WithMany(c => c.Notes).HasForeignKey(n => n.ChildId);
            model.Entity<Note>().Property(n => n.Text).HasMaxLength(32);
            model.Entity<Tag>().ToTable("SqTags").HasOne(t => t.Parent).WithMany(p => p.Tags).HasForeignKey(t => t.ParentId);
            model.Entity<Tag>().Property(t => t.Text).HasMaxLength(32);
        }
    }
    private sealed class Parent { public int Id { get; set; } public string Name { get; set; } = ""; public int TenantId { get; set; } public List<Child> Children { get; set; } = []; public List<Tag> Tags { get; set; } = []; }
    private sealed class Child { public int Id { get; set; } public string Name { get; set; } = ""; public int ParentId { get; set; } public Parent Parent { get; set; } = null!; public List<Note> Notes { get; set; } = []; }
    private sealed class Note { public int Id { get; set; } public int ChildId { get; set; } public string Text { get; set; } = ""; public Child Child { get; set; } = null!; }
    private sealed class Tag { public int Id { get; set; } public int ParentId { get; set; } public string Text { get; set; } = ""; public Parent Parent { get; set; } = null!; }
    private sealed record Command(string Sql, object?[] Inputs);
    private sealed class Commands : DbCommandInterceptor
    {
        public bool SingleCollection { get; init; }
        public bool EmptyRoots { get; init; }
        public bool EmptyCollections { get; init; }
        public Exception? ReadFailure { get; init; }
        public Action? BeforeFailure { get; init; }
        private bool _failed;
        public List<Command> Items { get; } = [];
        public List<Reader> Readers { get; } = [];
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result)
        {
            if (Readers.Any(reader => !reader.Disposed)) throw new InvalidOperationException("A native reader is still active: " + command.CommandText);
            Items.Add(new Command(command.CommandText, command.Parameters.Cast<DbParameter>().Select(p => p.Value).ToArray()));
            var sql = Unquote(command.CommandText);
            var table = new DataTable();
            var tenant = ((Context)data.Context!).Tenant;
            if (SingleCollection)
            {
                Columns(table, typeof(int), typeof(string), typeof(int), typeof(int), typeof(string), typeof(int));
                table.Rows.Add(1, "first", tenant, 11, "child", 1); table.Rows.Add(1, "first", tenant, 12, "child2", 1);
                table.Rows.Add(2, "empty", tenant, null, null, null);
            }
            else if (sql.Contains("SqNotes", StringComparison.Ordinal))
            {
                Columns(table, typeof(int), typeof(int), typeof(string), typeof(int), typeof(int));
                table.Rows.Add(111, 11, "note", 1, 11);
            }
            else if (sql.Contains("SqChildren", StringComparison.Ordinal))
            {
                Columns(table, typeof(int), typeof(string), typeof(int), typeof(int));
                table.Rows.Add(11, "child", 1, 1); table.Rows.Add(12, "child2", 1, 1);
            }
            else if (sql.Contains("SqTags", StringComparison.Ordinal))
            {
                Columns(table, typeof(int), typeof(int), typeof(string), typeof(int));
                table.Rows.Add(21, 1, "tag", 1);
            }
            else
            {
                Columns(table, typeof(int), typeof(string), typeof(int));
                table.Rows.Add(1, "first", tenant); table.Rows.Add(2, "empty", tenant);
            }
            var related = sql.Contains("SqChildren", StringComparison.Ordinal) || sql.Contains("SqTags", StringComparison.Ordinal);
            if (EmptyRoots || EmptyCollections && related) table.Rows.Clear();
            var failure = !_failed && sql.Contains("SqChildren", StringComparison.Ordinal) ? ReadFailure : null;
            if (failure is not null) _failed = true;
            var reader = new Reader(table.CreateDataReader(), failure, BeforeFailure);
            Readers.Add(reader);
            return InterceptionResult<DbDataReader>.SuppressWithResult(reader);
        }
        private static void Columns(DataTable table, params Type[] types)
        { for (var index = 0; index < types.Length; index++) table.Columns.Add("c" + index, types[index]); }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken token = default) => ValueTask.FromResult(ReaderExecuting(command, data, result));
    }
    private sealed class Reader(DbDataReader inner, Exception? failure, Action? beforeFailure) : DbDataReader
    {
        private int _reads;
        public bool Disposed { get; private set; }
        public override bool Read() { if (++_reads == 2 && failure is not null) { beforeFailure?.Invoke(); throw failure; } return inner.Read(); }
        public override Task<bool> ReadAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult(Read()); }
        protected override void Dispose(bool disposing) { if (disposing) { Disposed = true; inner.Dispose(); } base.Dispose(disposing); }
        public override ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        public override void Close() => inner.Close();
        public override int Depth => inner.Depth;
        public override int FieldCount => inner.FieldCount;
        public override bool HasRows => inner.HasRows;
        public override bool IsClosed => inner.IsClosed;
        public override int RecordsAffected => inner.RecordsAffected;
        public override object this[int ordinal] => inner[ordinal];
        public override object this[string name] => inner[name];
        public override bool NextResult() => inner.NextResult();
        public override string GetName(int ordinal) => inner.GetName(ordinal);
        public override string GetDataTypeName(int ordinal) => inner.GetDataTypeName(ordinal);
        public override Type GetFieldType(int ordinal) => inner.GetFieldType(ordinal);
        public override object GetValue(int ordinal) => inner.GetValue(ordinal);
        public override int GetValues(object[] values) => inner.GetValues(values);
        public override int GetOrdinal(string name) => inner.GetOrdinal(name);
        public override bool GetBoolean(int ordinal) => inner.GetBoolean(ordinal);
        public override byte GetByte(int ordinal) => inner.GetByte(ordinal);
        public override long GetBytes(int ordinal, long offset, byte[]? buffer, int bufferOffset, int length) => inner.GetBytes(ordinal, offset, buffer, bufferOffset, length);
        public override char GetChar(int ordinal) => inner.GetChar(ordinal);
        public override long GetChars(int ordinal, long offset, char[]? buffer, int bufferOffset, int length) => inner.GetChars(ordinal, offset, buffer, bufferOffset, length);
        public override Guid GetGuid(int ordinal) => inner.GetGuid(ordinal);
        public override short GetInt16(int ordinal) => inner.GetInt16(ordinal);
        public override int GetInt32(int ordinal) => inner.GetInt32(ordinal);
        public override long GetInt64(int ordinal) => inner.GetInt64(ordinal);
        public override float GetFloat(int ordinal) => inner.GetFloat(ordinal);
        public override double GetDouble(int ordinal) => inner.GetDouble(ordinal);
        public override string GetString(int ordinal) => inner.GetString(ordinal);
        public override decimal GetDecimal(int ordinal) => inner.GetDecimal(ordinal);
        public override DateTime GetDateTime(int ordinal) => inner.GetDateTime(ordinal);
        public override bool IsDBNull(int ordinal) => inner.IsDBNull(ordinal);
        public override IEnumerator GetEnumerator() => ((IEnumerable)inner).GetEnumerator();
    }
}
