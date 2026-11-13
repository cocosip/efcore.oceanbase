using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PackageConsumer;
using OceanBase.EntityFrameworkCore;
using OceanBaseConnector;
using Mode = OceanBase.EntityFrameworkCore.OceanBaseCompatibilityMode;

namespace ToolingConsumer;

internal static class CompiledModelSmoke
{
    internal static void Run()
    {
        foreach (var mode in Enum.GetValues<Mode>())
        foreach (var async in new[] { false, true }) RunAsync(mode, async).GetAwaiter().GetResult();
    }

    private static async Task RunAsync(Mode mode, bool async)
    {
        var modelType = typeof(CompiledModelSmoke).Assembly.GetType($"ToolingConsumer.Compiled.{mode}.Compiled{mode}ContextModel", true)!;
        var model = (IModel)modelType.GetProperty("Instance")!.GetValue(null)!;
        var capture = new Capture(mode);
        var options = new DbContextOptionsBuilder().UseOceanBase("Server=offline;Database=app", mode).UseModel(model)
            .ReplaceService<IRelationalConnection, RecoverySmoke.Connection>().AddInterceptors(capture).Options;
        CompiledGraphContext.ForbidModelBuilding = true;
        try
        {
            await using CompiledGraphContext context = mode == Mode.MySql ? new CompiledMySqlContext(options) : new CompiledOracleContext(options);
            var query = context.Set<CompiledRoot>().Include(r => r.Children).AsSplitQuery().OrderBy(r => r.Id);
            var values = async ? await query.ToArrayAsync() : query.ToArray();
            if (values.Length != 2 || values[0].Children.Count != 1 || values[1].Children.Count != 0
                || !ReferenceEquals(values[0], values[0].Children[0].Parent) || context.ChangeTracker.Entries().Count() != 3)
                throw new Exception("Compiled graph lost shaping/tracking/fixup.");
            var root = values[0];
            if (root.Code != new CompiledCode("code") || root.Day != new DateOnly(2024, 2, 29)
                || root.Clock != new TimeOnly(12, 34, 56) || root.Duration != TimeSpan.FromHours(-25)
                || !root.Active || root.Amount != 123.45m || root.Reading != 1.25f || root.Revision is not null
                || root.Token != Capture.Token || !root.Buffer.SequenceEqual(new byte[] { 1, 2, 3 })
                || root.Zoned != Capture.Zoned || capture.Readers.Count != 2 || capture.Readers.Any(r => !r.IsClosed))
                throw new Exception("Compiled scalar conversion or sequential reader cleanup failed.");
            root.Name = "changed";
            root.Code = new CompiledCode("new-code");
            root.Clock = new TimeOnly(10, 20, 30);
            var changed = async ? await context.SaveChangesAsync() : context.SaveChanges();
            if (changed != 1 || capture.Writes.Count != 1 || !capture.Writes[0].Sql.Contains("Revision IS NULL", StringComparison.Ordinal)
                || !capture.Writes[0].Values.Contains("new-code") || !capture.Writes[0].Values.Contains("changed")
                || !capture.Writes[0].Values.Contains(mode == Mode.MySql ? new TimeSpan(10, 20, 30)
                    : new OceanBaseIntervalDayToSecond(false, 0, 10, 20, 30)))
                throw new Exception("Compiled update lost converted parameters or original nullable concurrency.");
            root.Code = new CompiledCode(new string('x', 13));
            Exception? error = null;
            try { if (async) await context.SaveChangesAsync(); else context.SaveChanges(); }
            catch (DbUpdateException failure) when (failure.InnerException is ArgumentException) { error = failure; }
            if (error is null || capture.Writes.Count != 1) throw new Exception("Compiled mapping lost pre-write length validation.");
            if (context.Database.GetDbConnection().State != ConnectionState.Closed) throw new Exception("Compiled graph leaked its connection.");
            var opposite = new DbContextOptionsBuilder().UseOceanBase("Server=offline", mode == Mode.MySql ? Mode.Oracle : Mode.MySql).UseModel(model).Options;
            using var wrong = new DbContext(opposite);
            try { _ = wrong.Model; throw new Exception("Wrong-mode compiled model was accepted."); }
            catch (InvalidOperationException failure) when (failure.Message.Contains("compatibility mode", StringComparison.OrdinalIgnoreCase)) { }
            Console.WriteLine($"{mode}/async={async}: packed optimized model graph/conversions/update/guards passed (offline).");
        }
        finally { CompiledGraphContext.ForbidModelBuilding = false; }
    }

    private sealed class Capture(Mode mode) : DbCommandInterceptor
    {
        internal static readonly Guid Token = new("7b7d83e5-8d26-4c52-93fb-597b83664b20");
        internal static readonly DateTimeOffset Zoned = new(2024, 2, 29, 12, 34, 56, TimeSpan.FromHours(8));
        internal List<DbDataReader> Readers { get; } = [];
        internal List<(string Sql, object?[] Values)> Writes { get; } = [];
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            if (Readers.Any(reader => !reader.IsClosed)) throw new Exception("Compiled split query overlapped native readers.");
            var sql = Unquote(command.CommandText);
            var table = new DataTable();
            if (sql.Contains("CompiledGraphChildren", StringComparison.Ordinal))
            {
                Columns(table, typeof(int), typeof(int), typeof(string), typeof(int));
                table.Rows.Add(11, 1, "child", 1);
            }
            else
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(sql,
                    @"SELECT (\w+)\.Id, \1\.Active, \1\.Amount, \1\.Buffer, \1\.Clock, \1\.Code, \1\.Day, \1\.Duration, \1\.Name, \1\.Reading, \1\.Revision, \1\.Token, \1\.Zoned"))
                    throw new Exception("Unexpected compiled root projection: " + sql);
                Columns(table, typeof(int), typeof(object), typeof(decimal), typeof(byte[]), typeof(object), typeof(string),
                    typeof(DateTime), typeof(object), typeof(string), typeof(float), typeof(int), typeof(object), typeof(object));
                object clock = mode == Mode.MySql ? new TimeSpan(12, 34, 56) : new OceanBaseIntervalDayToSecond(false, 0, 12, 34, 56);
                object duration = mode == Mode.MySql ? TimeSpan.FromHours(-25) : new OceanBaseIntervalDayToSecond(true, 1, 1, 0, 0);
                for (var id = 1; id <= 2; id++)
                    table.Rows.Add(id, mode == Mode.MySql ? true : 1m, 123.45m, new byte[] { 1, 2, 3 }, clock, "code", new DateTime(2024, 2, 29),
                        duration, "root", 1.25f, DBNull.Value, mode == Mode.MySql ? Token.ToString() : Token.ToByteArray(), mode == Mode.MySql ? Zoned.UtcDateTime : Zoned);
            }
            var reader = table.CreateDataReader(); Readers.Add(reader);
            return InterceptionResult<DbDataReader>.SuppressWithResult(reader);
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken token = default) => ValueTask.FromResult(ReaderExecuting(command, eventData, result));
        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            Writes.Add((Unquote(command.CommandText), command.Parameters.Cast<DbParameter>().Select(p => p.Value).ToArray()));
            return InterceptionResult<int>.SuppressWithResult(1);
        }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken token = default) => ValueTask.FromResult(NonQueryExecuting(command, eventData, result));
        private static string Unquote(string sql) => sql.Replace("`", "", StringComparison.Ordinal).Replace("\"", "", StringComparison.Ordinal);
        private static void Columns(DataTable table, params Type[] types)
        { for (var i = 0; i < types.Length; i++) table.Columns.Add("c" + i, types[i]); }
    }
}

public abstract class CompiledGraphContext(DbContextOptions options) : DbContext(options)
{
    internal static bool ForbidModelBuilding { get; set; }
    protected override void OnModelCreating(ModelBuilder model)
    {
        if (ForbidModelBuilding) throw new Exception("UseModel unexpectedly rebuilt the model.");
        var root = model.Entity<CompiledRoot>();
        root.ToTable("CompiledGraphRoots");
        root.Property(r => r.Id).ValueGeneratedNever();
        root.Property(r => r.Name).HasMaxLength(32);
        root.Property(r => r.Code).HasConversion(new ValueConverter<CompiledCode, string>(
            value => value.Value, value => new CompiledCode(value), new ConverterMappingHints(size: 12)));
        root.Property(r => r.Clock).HasConversion(new ValueConverter<TimeOnly, TimeOnly>(v => v, v => v));
        root.Property(r => r.Day).HasConversion(new ValueConverter<DateOnly, DateOnly>(v => v, v => v));
        root.Property(r => r.Duration).HasConversion(new ValueConverter<TimeSpan, TimeSpan>(v => v, v => v));
        root.Property(r => r.Amount).HasPrecision(12, 2);
        root.Property(r => r.Buffer).HasMaxLength(4);
        root.Property(r => r.Revision).IsConcurrencyToken();
        model.Entity<CompiledChild>().ToTable("CompiledGraphChildren").HasOne(c => c.Parent).WithMany(p => p.Children).HasForeignKey(c => c.ParentId);
        model.Entity<CompiledChild>().Property(c => c.Id).ValueGeneratedNever();
        model.Entity<CompiledChild>().Property(c => c.Text).HasMaxLength(32);
    }
}
public sealed class CompiledMySqlContext(DbContextOptions options) : CompiledGraphContext(options);
public sealed class CompiledOracleContext(DbContextOptions options) : CompiledGraphContext(options);
public sealed class CompiledMySqlFactory : IDesignTimeDbContextFactory<CompiledMySqlContext>
{
    public CompiledMySqlContext CreateDbContext(string[] args) => new(ToolingOptions.Create<CompiledMySqlContext>(Mode.MySql));
}
public sealed class CompiledOracleFactory : IDesignTimeDbContextFactory<CompiledOracleContext>
{
    public CompiledOracleContext CreateDbContext(string[] args) => new(ToolingOptions.Create<CompiledOracleContext>(Mode.Oracle));
}
public sealed class CompiledRoot
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public CompiledCode Code { get; set; } = new("");
    public DateOnly Day { get; set; }
    public TimeOnly Clock { get; set; }
    public TimeSpan Duration { get; set; }
    public DateTimeOffset Zoned { get; set; }
    public Guid Token { get; set; }
    public bool Active { get; set; }
    public decimal Amount { get; set; }
    public float Reading { get; set; }
    public byte[] Buffer { get; set; } = [];
    public int? Revision { get; set; }
    public List<CompiledChild> Children { get; set; } = [];
}
public sealed record CompiledCode(string Value);
public sealed class CompiledChild
{
    public int Id { get; set; }
    public int ParentId { get; set; }
    public string Text { get; set; } = "";
    public CompiledRoot Parent { get; set; } = null!;
}
