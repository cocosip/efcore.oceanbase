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
using OceanBase.EntityFrameworkCore;

namespace PackageConsumer;

internal static class LazyLoadingSmoke
{
    public static void Run(OceanBaseCompatibilityMode mode)
    {
        foreach (var async in new[] { false, true })
        foreach (var tracking in new[] { QueryTrackingBehavior.TrackAll, QueryTrackingBehavior.NoTracking })
            RunAsync(mode, async, tracking).GetAwaiter().GetResult();
        Console.WriteLine($"{mode}: packed optional lazy-loading proxies, filters/fixup and tracked writes passed (offline).");
    }

    private static async Task RunAsync(OceanBaseCompatibilityMode mode, bool async, QueryTrackingBehavior tracking)
    {
        var capture = new Capture();
        await using var context = new Context(new DbContextOptionsBuilder<Context>()
            .UseOceanBase("Server=offline;Database=app", mode).UseLazyLoadingProxies()
            .EnableServiceProviderCaching(false)
            .ReplaceService<IRelationalConnection, RecoverySmoke.Connection>()
            .UseQueryTrackingBehavior(tracking).AddInterceptors(capture).Options);
        var parent = async ? await context.Set<LazyParent>().SingleAsync() : context.Set<LazyParent>().Single();
        if (parent.GetType() == typeof(LazyParent) || capture.Readers.Count != 1
            || context.Entry(parent).Collection(p => p.Children).IsLoaded)
            throw new Exception("Packed provider did not materialize a lazy proxy.");
        var child = parent.Children.Single();
        if (child.Id != 11 || !ReferenceEquals(parent, child.Parent) || parent.Children.Count != 1
            || capture.Readers.Count != 2 || capture.AsyncQueries != (async ? 1 : 0)
            || context.ChangeTracker.Entries().Count() != (tracking == QueryTrackingBehavior.TrackAll ? 2 : 0))
            throw new Exception("Packed lazy graph lost loading state, fixup or tracking behavior.");
        if (tracking == QueryTrackingBehavior.TrackAll)
        {
            child.Text = "changed";
            var affected = async ? await context.SaveChangesAsync() : context.SaveChanges();
            if (affected != 1 || capture.Writes != 1 || context.Entry(child).State != EntityState.Unchanged)
                throw new Exception("Packed lazy-loaded entity could not be saved normally.");
        }
        if (capture.Readers.Any(reader => !reader.IsClosed) || context.Database.GetDbConnection().State != ConnectionState.Closed)
            throw new Exception("Packed lazy loading leaked a reader or connection.");
    }

    private sealed class Context(DbContextOptions<Context> options) : DbContext(options)
    {
        public int Tenant { get; set; } = 7;
        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<LazyParent>().ToTable("PackedLazyParents").HasQueryFilter(p => p.TenantId == Tenant);
            model.Entity<LazyParent>().Property(p => p.Id).ValueGeneratedNever();
            model.Entity<LazyChild>().ToTable("PackedLazyChildren").HasQueryFilter(c => c.TenantId == Tenant);
            model.Entity<LazyChild>().Property(c => c.Id).ValueGeneratedNever();
            model.Entity<LazyChild>().Property(c => c.Text).HasMaxLength(32);
            model.Entity<LazyChild>().Property(c => c.Revision).IsConcurrencyToken();
            model.Entity<LazyChild>().HasOne(c => c.Parent).WithMany(p => p.Children).HasForeignKey(c => c.ParentId);
        }
    }

    private sealed class Capture : DbCommandInterceptor
    {
        public List<DbDataReader> Readers { get; } = [];
        public int Writes { get; private set; }
        public int AsyncQueries { get; private set; }
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result)
        {
            if (Readers.Any(reader => !reader.IsClosed)) throw new Exception("Packed lazy query overlapped native readers.");
            var sql = command.CommandText.Replace("`", "", StringComparison.Ordinal).Replace("\"", "", StringComparison.Ordinal);
            VerifyBinding(command, sql, "TenantId", 7);
            var table = new DataTable();
            table.Columns.Add("Id", typeof(int));
            if (sql.Contains("PackedLazyChildren", StringComparison.Ordinal))
            {
                VerifyBinding(command, sql, "ParentId", 1);
                table.Columns.Add("ParentId", typeof(int)); table.Columns.Add("Revision", typeof(string));
                table.Columns.Add("TenantId", typeof(int)); table.Columns.Add("Text", typeof(string));
                table.Rows.Add(11, 1, DBNull.Value, 7, "child");
            }
            else
            {
                table.Columns.Add("TenantId", typeof(int)); table.Rows.Add(1, 7);
            }
            var reader = table.CreateDataReader();
            Readers.Add(reader);
            return InterceptionResult<DbDataReader>.SuppressWithResult(reader);
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            AsyncQueries++;
            return ValueTask.FromResult(ReaderExecuting(command, data, result));
        }
        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData data, InterceptionResult<int> result)
        {
            Writes++;
            var sql = command.CommandText.Replace("`", "", StringComparison.Ordinal).Replace("\"", "", StringComparison.Ordinal);
            if (!sql.StartsWith("UPDATE PackedLazyChildren SET Text =", StringComparison.Ordinal)
                || !sql.Contains("Revision IS NULL", StringComparison.Ordinal) || command.Parameters.Count != 2)
                throw new Exception("Packed lazy-loaded write lost values or nullable concurrency.");
            VerifyBinding(command, sql, "Text", "changed");
            VerifyBinding(command, sql, "Id", 11);
            return InterceptionResult<int>.SuppressWithResult(1);
        }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<int> result, CancellationToken token = default)
            => ValueTask.FromResult(NonQueryExecuting(command, data, result));

        private static void VerifyBinding(DbCommand command, string sql, string column, object value)
        {
            var placeholder = Regex.Match(sql, $@"\b{column} = ([?:@]\w*)").Groups[1];
            if (!placeholder.Success) throw new Exception("Packed lazy command lost predicate/assignment: " + column);
            var parameter = placeholder.Value == "?" ? command.Parameters[sql[..placeholder.Index].Count(c => c == '?')]
                : command.Parameters[placeholder.Value];
            if (!Equals(parameter.Value, value)) throw new Exception("Packed lazy command bound the wrong value to " + column);
        }
    }
}

public class LazyParent
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public virtual ICollection<LazyChild> Children { get; set; } = new List<LazyChild>();
}
public class LazyChild
{
    public int Id { get; set; }
    public int ParentId { get; set; }
    public string? Revision { get; set; }
    public int TenantId { get; set; }
    public string Text { get; set; } = "";
    public virtual LazyParent Parent { get; set; } = null!;
}
