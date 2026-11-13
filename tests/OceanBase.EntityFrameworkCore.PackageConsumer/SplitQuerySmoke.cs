using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBase.EntityFrameworkCore;

namespace PackageConsumer;

internal static class SplitQuerySmoke
{
    public static void Run(OceanBaseCompatibilityMode mode)
    {
        foreach (var async in new[] { false, true })
        foreach (var configured in new[] { false, true }) RunAsync(mode, async, configured).GetAwaiter().GetResult();
        Console.WriteLine($"{mode}: packed configured/per-query split graphs use sequential buffered readers (offline).");
    }

    private static async Task RunAsync(OceanBaseCompatibilityMode mode, bool async, bool configured)
    {
        var capture = new Capture();
        await using var context = new Context(new DbContextOptionsBuilder<Context>()
            .UseOceanBase("Server=offline;Database=app", mode, options =>
            { if (configured) options.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery); })
            .ReplaceService<IRelationalConnection, RecoverySmoke.Connection>().AddInterceptors(capture).Options);
        IQueryable<Parent> query = context.Set<Parent>().Include(p => p.Children).OrderBy(p => p.Id);
        if (!configured) query = query.AsSplitQuery();
        var values = async ? await query.ToArrayAsync() : query.ToArray();
        if (values.Length != 2 || values[0].Children.Count != 1 || values[0].Children[0].Id != 11
            || !ReferenceEquals(values[0], values[0].Children[0].Parent) || values[1].Children.Count != 0
            || context.ChangeTracker.Entries().Count() != 3 || capture.Readers.Count != 2
            || capture.Readers.Any(reader => !reader.IsClosed) || context.Database.GetDbConnection().State != ConnectionState.Closed)
            throw new Exception("Packed split query failed graph shaping, disposal or tracking.");
    }

    private sealed class Context(DbContextOptions<Context> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<Parent>().ToTable("SplitParents").Property(p => p.Name).HasMaxLength(32);
            model.Entity<Child>().ToTable("SplitChildren").HasOne(c => c.Parent).WithMany(p => p.Children).HasForeignKey(c => c.ParentId);
            model.Entity<Child>().Property(c => c.Text).HasMaxLength(32);
        }
    }
    private sealed class Parent { public int Id { get; set; } public string Name { get; set; } = ""; public List<Child> Children { get; set; } = []; }
    private sealed class Child { public int Id { get; set; } public int ParentId { get; set; } public string Text { get; set; } = ""; public Parent Parent { get; set; } = null!; }
    private sealed class Capture : DbCommandInterceptor
    {
        public List<DbDataReader> Readers { get; } = [];
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result)
        {
            if (Readers.Any(reader => !reader.IsClosed)) throw new Exception("Packed split query opened overlapping native readers.");
            var table = new DataTable();
            if (command.CommandText.Contains("SplitChildren", StringComparison.Ordinal))
            {
                table.Columns.Add("Id", typeof(int)); table.Columns.Add("ParentId", typeof(int));
                table.Columns.Add("Text", typeof(string)); table.Columns.Add("OwnerId", typeof(int));
                table.Rows.Add(11, 1, "child", 1);
            }
            else
            {
                table.Columns.Add("Id", typeof(int)); table.Columns.Add("Name", typeof(string));
                table.Rows.Add(1, "first"); table.Rows.Add(2, "empty");
            }
            var reader = table.CreateDataReader();
            Readers.Add(reader);
            return InterceptionResult<DbDataReader>.SuppressWithResult(reader);
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken token = default) => ValueTask.FromResult(ReaderExecuting(command, data, result));
    }
}
