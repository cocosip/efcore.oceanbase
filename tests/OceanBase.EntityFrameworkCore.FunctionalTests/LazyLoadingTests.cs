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

public sealed class LazyLoadingTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var mode in Enum.GetValues<OceanBaseCompatibilityMode>())
        foreach (var async in new[] { false, true })
        foreach (var tracking in Enum.GetValues<QueryTrackingBehavior>()) yield return [mode, async, tracking];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Proxy_collection_loads_on_access_with_current_filters_and_inverse_fixup(
        OceanBaseCompatibilityMode mode, bool async, QueryTrackingBehavior tracking)
    {
        var capture = new Commands();
        await using var context = Create(mode, capture);
        context.ChangeTracker.QueryTrackingBehavior = tracking;
        var query = context.Set<Parent>().Where(p => p.Id == 1);
        var parents = async ? await query.ToListAsync() : query.ToList();
        var parent = Assert.Single(parents);
        Assert.NotEqual(typeof(Parent), parent.GetType());
        Assert.False(context.Entry(parent).Collection(p => p.Children).IsLoaded);
        Assert.Single(capture.Items);
        Assert.True(capture.Readers[0].IsClosed);
        Assert.Equal(11, Assert.Single(parent.Children).Id);
        Assert.Same(parent, parent.Children.Single().Parent);
        Assert.True(context.Entry(parent).Collection(p => p.Children).IsLoaded);
        Assert.Equal(2, capture.Items.Count);
        Assert.Equal(new[] { async, false }, capture.Items.Select(item => item.Async));
        AssertLazySql(capture.Items[1], "LlChildren", "ParentId", 1, 7);
        _ = parent.Children.Count;
        Assert.Equal(2, capture.Items.Count);
        Assert.Equal(tracking == QueryTrackingBehavior.TrackAll ? 2 : 0, context.ChangeTracker.Entries().Count());
        AssertReleased(context, capture);

        context.ChangeTracker.LazyLoadingEnabled = false;
        context.Tenant = 8;
        context.ChangeTracker.Clear();
        var next = async ? await query.SingleAsync() : query.Single();
        context.ChangeTracker.LazyLoadingEnabled = true;
        Assert.Single(next.Children);
        AssertLazySql(capture.Items[^1], "LlChildren", "ParentId", 1, 8);
        AssertReleased(context, capture);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Proxy_reference_preserves_optional_null_and_empty_loaded_state(
        OceanBaseCompatibilityMode mode, bool async, QueryTrackingBehavior tracking)
    {
        var capture = new Commands();
        await using var context = Create(mode, capture);
        context.ChangeTracker.QueryTrackingBehavior = tracking;
        var query = context.Set<Child>();
        var child = async ? await query.SingleAsync() : query.Single();
        Assert.False(context.Entry(child).Reference(c => c.Parent).IsLoaded);
        Assert.Equal("parent", child.Parent!.Name);
        Assert.Contains(child, child.Parent.Children);
        AssertLazySql(capture.Items[1], "LlParents", "Id", 1, 7);
        var count = capture.Items.Count;
        _ = child.Parent;
        Assert.Equal(count, capture.Items.Count);
        AssertReleased(context, capture);

        context.ChangeTracker.LazyLoadingEnabled = false;
        context.ChangeTracker.Clear();
        capture.NullParent = true;
        var orphan = async ? await query.SingleAsync() : query.Single();
        context.ChangeTracker.LazyLoadingEnabled = true;
        count = capture.Items.Count;
        Assert.Null(orphan.Parent);
        Assert.Null(orphan.Parent);
        Assert.Equal(count, capture.Items.Count);
        Assert.True(context.Entry(orphan).Reference(c => c.Parent).IsLoaded);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task Empty_collection_and_disabled_loading_do_not_repeat_commands(OceanBaseCompatibilityMode mode, bool async)
    {
        var capture = new Commands { EmptyChildren = true };
        await using var context = Create(mode, capture);
        var parent = async ? await context.Set<Parent>().SingleAsync() : context.Set<Parent>().Single();
        context.ChangeTracker.LazyLoadingEnabled = false;
        Assert.Empty(parent.Children);
        Assert.Single(capture.Items);
        Assert.False(context.Entry(parent).Collection(p => p.Children).IsLoaded);
        context.ChangeTracker.LazyLoadingEnabled = true;
        Assert.Empty(parent.Children);
        Assert.Empty(parent.Children);
        Assert.True(context.Entry(parent).Collection(p => p.Children).IsLoaded);
        Assert.Equal(2, capture.Items.Count);
        AssertReleased(context, capture);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task Injected_loader_uses_provider_queries_without_proxies(OceanBaseCompatibilityMode mode, bool async)
    {
        var capture = new Commands { Injected = true };
        await using var context = new InjectedContext(new DbContextOptionsBuilder<InjectedContext>()
            .UseOceanBase("Server=offline;Database=app", mode)
            .EnableServiceProviderCaching(false)
            .ReplaceService<IRelationalConnection, TransactionTests.TestConnection>().AddInterceptors(capture).Options);
        var parent = async ? await context.Set<InjectedParent>().SingleAsync() : context.Set<InjectedParent>().Single();
        Assert.Equal(typeof(InjectedParent), parent.GetType());
        Assert.Single(capture.Items);
        Assert.Equal(11, Assert.Single(parent.Children).Id);
        Assert.Same(parent, parent.Children.Single().Parent);
        AssertLazySql(capture.Items[1], "LlChildren", "ParentId", 1, null);
        _ = parent.Children.Count;
        Assert.Equal(2, capture.Items.Count);
        AssertReleased(context, capture);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task Injected_reference_loads_by_key_and_marks_missing_target_loaded(OceanBaseCompatibilityMode mode, bool async)
    {
        var capture = new Commands { Injected = true };
        await using var context = new InjectedContext(new DbContextOptionsBuilder<InjectedContext>()
            .UseOceanBase("Server=offline;Database=app", mode).EnableServiceProviderCaching(false)
            .ReplaceService<IRelationalConnection, TransactionTests.TestConnection>().AddInterceptors(capture).Options);
        var child = async ? await context.Set<InjectedChild>().SingleAsync() : context.Set<InjectedChild>().Single();
        Assert.Single(capture.Items);
        Assert.Equal(1, child.Parent!.Id);
        Assert.True(context.Entry(child).Reference(c => c.Parent).IsLoaded);
        AssertLazySql(capture.Items[1], "LlParents", "Id", 1, null);
        _ = child.Parent;
        Assert.Equal(2, capture.Items.Count);
        AssertReleased(context, capture);

        context.ChangeTracker.Clear();
        capture.EmptyParents = true;
        var missing = async ? await context.Set<InjectedChild>().SingleAsync() : context.Set<InjectedChild>().Single();
        Assert.Equal(1, missing.ParentId);
        Assert.Null(missing.Parent);
        Assert.Null(missing.Parent);
        Assert.True(context.Entry(missing).Reference(c => c.Parent).IsLoaded);
        Assert.Equal(4, capture.Items.Count);
        AssertReleased(context, capture);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task Proxy_reference_with_nonnull_key_and_no_target_does_not_query_repeatedly(OceanBaseCompatibilityMode mode, bool async)
    {
        var capture = new Commands { EmptyParents = true };
        await using var context = Create(mode, capture);
        var child = async ? await context.Set<Child>().SingleAsync() : context.Set<Child>().Single();
        Assert.Equal(1, child.ParentId);
        Assert.Null(child.Parent);
        Assert.Null(child.Parent);
        Assert.True(context.Entry(child).Reference(c => c.Parent).IsLoaded);
        Assert.Equal(2, capture.Items.Count);
        AssertLazySql(capture.Items[1], "LlParents", "Id", 1, 7);
        AssertReleased(context, capture);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task Eager_loaded_proxy_collections_do_not_query_again(OceanBaseCompatibilityMode mode, bool async)
    {
        var capture = new Commands();
        await using var context = Create(mode, capture);
        var query = context.Set<Parent>().Include(p => p.Children);
        var parent = async ? await query.SingleAsync() : query.Single();
        Assert.True(context.Entry(parent).Collection(p => p.Children).IsLoaded);
        Assert.Equal(11, Assert.Single(parent.Children).Id);
        Assert.Same(parent, parent.Children.Single().Parent);
        Assert.Single(capture.Items);
        AssertReleased(context, capture);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public void Detaching_or_disposing_does_not_run_another_lazy_query(OceanBaseCompatibilityMode mode, bool detach)
    {
        var capture = new Commands();
        var context = Create(mode, capture);
        var parent = context.Set<Parent>().Single();
        if (detach)
        {
            context.Entry(parent).State = EntityState.Detached;
            Assert.Empty(parent.Children);
            Assert.Empty(context.ChangeTracker.Entries());
            context.Dispose();
        }
        else
        {
            context.Dispose();
            Assert.Throws<InvalidOperationException>(() => parent.Children.Count);
        }
        Assert.Single(capture.Items);
        Assert.All(capture.Readers, reader => Assert.True(reader.IsClosed));
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task Failed_lazy_query_releases_its_connection_and_remains_retryable(OceanBaseCompatibilityMode mode, bool async)
    {
        var capture = new Commands();
        await using var context = Create(mode, capture);
        var parent = async ? await context.Set<Parent>().SingleAsync() : context.Set<Parent>().Single();
        var failure = new InvalidOperationException("Synthetic lazy query failure.");
        capture.NextFailure = failure;
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => parent.Children.Count));
        Assert.False(context.Entry(parent).Collection(p => p.Children).IsLoaded);
        AssertReleased(context, capture);
        Assert.Equal(11, Assert.Single(parent.Children).Id);
        Assert.True(context.Entry(parent).Collection(p => p.Children).IsLoaded);
        Assert.Equal(3, capture.Items.Count);
        AssertReleased(context, capture);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql)]
    [InlineData(OceanBaseCompatibilityMode.Oracle)]
    public async Task Injected_async_lazy_load_carries_cancellation_and_can_be_retried(OceanBaseCompatibilityMode mode)
    {
        var capture = new Commands { Injected = true };
        await using var context = new InjectedContext(new DbContextOptionsBuilder<InjectedContext>()
            .UseOceanBase("Server=offline;Database=app", mode)
            .EnableServiceProviderCaching(false)
            .ReplaceService<IRelationalConnection, TransactionTests.TestConnection>().AddInterceptors(capture).Options);
        var parent = await context.Set<InjectedParent>().SingleAsync();
        using var source = new CancellationTokenSource();
        capture.BeforeAsync = source.Cancel;
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => parent.LoadChildrenAsync(source.Token));
        Assert.Equal(source.Token, error.CancellationToken);
        Assert.False(context.Entry(parent).Collection(p => p.Children).IsLoaded);
        AssertReleased(context, capture);
        capture.BeforeAsync = null;
        await parent.LoadChildrenAsync(CancellationToken.None);
        Assert.Single(parent.Children);
        Assert.True(context.Entry(parent).Collection(p => p.Children).IsLoaded);
        AssertLazySql(capture.Items[^1], "LlChildren", "ParentId", 1, null);
        Assert.True(capture.Items[^1].Async);
        AssertReleased(context, capture);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task Lazy_queries_preserve_a_caller_opened_connection(OceanBaseCompatibilityMode mode, bool async)
    {
        var capture = new Commands();
        await using var context = Create(mode, capture);
        if (async) await context.Database.OpenConnectionAsync(); else context.Database.OpenConnection();
        try
        {
            var parent = async ? await context.Set<Parent>().SingleAsync() : context.Set<Parent>().Single();
            Assert.Single(parent.Children);
            Assert.Equal(ConnectionState.Open, context.Database.GetDbConnection().State);
            Assert.All(capture.Readers, reader => Assert.True(reader.IsClosed));
        }
        finally
        {
            if (async) await context.Database.CloseConnectionAsync(); else context.Database.CloseConnection();
        }
        AssertReleased(context, capture);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task Lazy_loaded_entities_use_normal_tracked_writes_and_original_null_concurrency(OceanBaseCompatibilityMode mode, bool async)
    {
        var capture = new Commands();
        await using var context = Create(mode, capture);
        var parent = async ? await context.Set<Parent>().SingleAsync() : context.Set<Parent>().Single();
        var child = Assert.Single(parent.Children);
        child.Name = "changed";
        var affected = async ? await context.SaveChangesAsync() : context.SaveChanges();
        Assert.Equal(1, affected);
        Assert.Equal(EntityState.Unchanged, context.Entry(child).State);
        Assert.Null(context.Entry(child).Property(c => c.Revision).OriginalValue);
        var write = Assert.Single(capture.Items, item => item.Sql.StartsWith("UPDATE", StringComparison.Ordinal));
        var sql = write.Sql.Replace("`", "", StringComparison.Ordinal).Replace("\"", "", StringComparison.Ordinal);
        Assert.Contains("UPDATE LlChildren SET Name =", sql);
        Assert.Matches(@"WHERE Id = [?:@]\w* AND Revision IS NULL", sql);
        Assert.Equal("changed", BoundValue(write, Regex.Match(sql, @"Name = ([?:@]\w*)").Groups[1]));
        Assert.Equal(11, BoundValue(write, Regex.Match(sql, @"WHERE Id = ([?:@]\w*)").Groups[1]));
        Assert.Equal(2, write.Inputs.Length);
        Assert.Equal(async, write.Async);
        AssertReleased(context, capture);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql)]
    [InlineData(OceanBaseCompatibilityMode.Oracle)]
    public void Invalid_proxy_entity_shapes_fail_during_model_initialization(OceanBaseCompatibilityMode mode)
    {
        using var context = new InvalidProxyContext(new DbContextOptionsBuilder<InvalidProxyContext>()
            .UseOceanBase("Server=offline;Database=app", mode).UseLazyLoadingProxies()
            .EnableServiceProviderCaching(false).Options);
        var error = Assert.Throws<InvalidOperationException>(() => context.Model);
        Assert.Contains("sealed", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static Context Create(OceanBaseCompatibilityMode mode, Commands capture)
        => new(new DbContextOptionsBuilder<Context>().UseOceanBase("Server=offline;Database=app", mode)
            .UseLazyLoadingProxies().ReplaceService<IRelationalConnection, TransactionTests.TestConnection>()
            .EnableServiceProviderCaching(false)
            .ConfigureWarnings(w => w.Throw(CoreEventId.LazyLoadOnDisposedContextWarning))
            .AddInterceptors(capture).Options);

    private static void AssertReleased(DbContext context, Commands capture)
    {
        Assert.All(capture.Readers, reader => Assert.True(reader.IsClosed));
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    private static void AssertLazySql(Command command, string table, string key, int id, int? tenant)
    {
        var sql = command.Sql.Replace("`", "", StringComparison.Ordinal).Replace("\"", "", StringComparison.Ordinal);
        Assert.Contains("FROM " + table, sql);
        Assert.Equal(id, BoundValue(command, Regex.Match(sql, $@"\b\w+\.{key} = ([?:@]\w*)").Groups[1]));
        if (tenant is not null)
        {
            Assert.Equal(tenant.Value, BoundValue(command, Regex.Match(sql, @"\b\w+\.TenantId = ([?:@]\w*)").Groups[1]));
        }
        var projection = table == "LlChildren" && tenant is not null
            ? @"SELECT (\w+)\.Id, \1\.Name, \1\.ParentId, \1\.Revision, \1\.TenantId\r?\n"
            : table == "LlParents" && tenant is not null ? @"SELECT (\w+)\.Id, \1\.Name, \1\.TenantId\r?\n"
            : table == "LlChildren" ? @"SELECT (\w+)\.Id, \1\.ParentId\r?\n" : @"SELECT (\w+)\.Id\r?\n";
        Assert.Matches(projection, sql);
    }

    private static object? BoundValue(Command command, Group placeholder)
    {
        Assert.True(placeholder.Success, command.Sql);
        var sql = command.Sql.Replace("`", "", StringComparison.Ordinal).Replace("\"", "", StringComparison.Ordinal);
        var index = placeholder.Value == "?" ? sql[..placeholder.Index].Count(c => c == '?')
            : Array.IndexOf(command.Names, placeholder.Value);
        Assert.InRange(index, 0, command.Inputs.Length - 1);
        return command.Inputs[index];
    }

    private sealed class Context(DbContextOptions<Context> options) : DbContext(options)
    {
        public int Tenant { get; set; } = 7;
        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<Parent>().ToTable("LlParents").HasQueryFilter(p => p.TenantId == Tenant);
            model.Entity<Parent>().Property(p => p.Name).HasMaxLength(32);
            model.Entity<Parent>().Property(p => p.Id).ValueGeneratedNever();
            model.Entity<Child>().ToTable("LlChildren").HasQueryFilter(c => c.TenantId == Tenant);
            model.Entity<Child>().Property(c => c.Name).HasMaxLength(32);
            model.Entity<Child>().Property(c => c.Id).ValueGeneratedNever();
            model.Entity<Child>().Property(c => c.Revision).IsConcurrencyToken();
            model.Entity<Child>().HasOne(c => c.Parent).WithMany(p => p.Children).HasForeignKey(c => c.ParentId);
        }
    }

    public class Parent
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int TenantId { get; set; }
        public virtual ICollection<Child> Children { get; set; } = new List<Child>();
    }

    public class Child
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int? ParentId { get; set; }
        public string? Revision { get; set; }
        public int TenantId { get; set; }
        public virtual Parent? Parent { get; set; }
    }

    private sealed class InjectedContext(DbContextOptions<InjectedContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<InjectedParent>().ToTable("LlParents");
            model.Entity<InjectedChild>().ToTable("LlChildren").HasOne(c => c.Parent)
                .WithMany(p => p.Children).HasForeignKey(c => c.ParentId);
        }
    }

    public sealed class InjectedParent
    {
        private readonly ILazyLoader _lazyLoader;
        private ICollection<InjectedChild>? _children;
        private InjectedParent(ILazyLoader lazyLoader) => _lazyLoader = lazyLoader;
        public int Id { get; set; }
        public ICollection<InjectedChild> Children { get => _lazyLoader.Load(this, ref _children)!; set => _children = value; }
        public Task LoadChildrenAsync(CancellationToken token) => _lazyLoader.LoadAsync(this, token, nameof(Children));
    }

    public sealed class InjectedChild
    {
        private readonly ILazyLoader _lazyLoader;
        private InjectedParent? _parent;
        private InjectedChild(ILazyLoader lazyLoader) => _lazyLoader = lazyLoader;
        public int Id { get; set; }
        public int ParentId { get; set; }
        public InjectedParent? Parent { get => _lazyLoader.Load(this, ref _parent); set => _parent = value; }
    }

    private sealed class InvalidProxyContext(DbContextOptions<InvalidProxyContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model) => model.Entity<InjectedChild>();
    }

    private sealed record Command(string Sql, object?[] Inputs, bool Async, string[] Names);
    private sealed class Commands : DbCommandInterceptor
    {
        public bool EmptyChildren { get; set; }
        public bool EmptyParents { get; set; }
        public bool NullParent { get; set; }
        public bool Injected { get; init; }
        public Exception? NextFailure { get; set; }
        public Action? BeforeAsync { get; set; }
        public List<Command> Items { get; } = [];
        public List<DbDataReader> Readers { get; } = [];
        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData data,
            InterceptionResult<int> result)
        {
            Items.Add(Snapshot(command, false));
            return InterceptionResult<int>.SuppressWithResult(1);
        }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<int> result, CancellationToken token = default)
        {
            Items.Add(Snapshot(command, true));
            return ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(1));
        }
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result) => Execute(command, data, false);
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken token = default)
        {
            BeforeAsync?.Invoke();
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Execute(command, data, true));
        }

        private InterceptionResult<DbDataReader> Execute(DbCommand command, CommandEventData data, bool async)
        {
            if (Readers.Any(reader => !reader.IsClosed)) throw new InvalidOperationException("A native reader is still active.");
            Items.Add(Snapshot(command, async));
            if (NextFailure is { } failure) { NextFailure = null; throw failure; }
            var child = command.CommandText.Contains("LlChildren", StringComparison.Ordinal);
            var include = command.CommandText.Contains("LEFT JOIN", StringComparison.Ordinal);
            var table = new DataTable();
            var types = include ? new[] { typeof(int), typeof(string), typeof(int), typeof(int), typeof(string), typeof(int), typeof(string), typeof(int) }
                : Injected ? child ? new[] { typeof(int), typeof(int) } : [typeof(int)]
                : child ? [typeof(int), typeof(string), typeof(int), typeof(string), typeof(int)]
                : new[] { typeof(int), typeof(string), typeof(int) };
            for (var index = 0; index < types.Length; index++) table.Columns.Add("c" + index, types[index]);
            var tenant = (data.Context as Context)?.Tenant ?? 7;
            if (include) table.Rows.Add(1, "parent", tenant, 11, "child", 1, DBNull.Value, tenant);
            else if (Injected) table.Rows.Add(child ? new object[] { 11, 1 } : [1]);
            else if (child && !EmptyChildren) table.Rows.Add(11, "child", NullParent ? DBNull.Value : 1, DBNull.Value, tenant);
            else if (!child) table.Rows.Add(1, "parent", tenant);
            if (!child && EmptyParents) table.Rows.Clear();
            var reader = table.CreateDataReader();
            Readers.Add(reader);
            return InterceptionResult<DbDataReader>.SuppressWithResult(reader);
        }

        private static Command Snapshot(DbCommand command, bool async) => new(command.CommandText,
            command.Parameters.Cast<DbParameter>().Select(p => p.Value).ToArray(), async,
            command.Parameters.Cast<DbParameter>().Select(p => p.ParameterName).ToArray());
    }
}
