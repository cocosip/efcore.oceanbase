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
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using OceanBase.EntityFrameworkCore;

namespace PackageConsumer;

internal static class TpcSmoke
{
    public static void Run(OceanBaseCompatibilityMode mode)
    {
        foreach (var async in new[] { false, true }) RunAsync(mode, async).GetAwaiter().GetResult();
        Console.WriteLine($"{mode}: packed TPC schema/migrations, union materialization and concrete CRUD passed (offline).");
    }

    private static async Task RunAsync(OceanBaseCompatibilityMode mode, bool async)
    {
        var commands = new Commands();
        await using var context = new HierarchyContext(Options(mode, commands));
        // Native transaction semantics are checked by the source fixtures.
        context.Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
        var script = context.Database.GenerateCreateScript();
        var model = context.GetService<IDesignTimeModel>().Model;
        var operations = context.GetService<IMigrationsModelDiffer>().GetDifferences(null, model.GetRelationalModel());
        var migrated = string.Concat(context.GetService<IMigrationsSqlGenerator>().Generate(operations, model).Select(c => c.CommandText));
        foreach (var sql in new[] { script, migrated })
            if (Regex.Matches(sql, "CREATE TABLE").Count != 3 || sql.Contains("AUTO_INCREMENT", StringComparison.Ordinal)
                || sql.Contains("AS IDENTITY", StringComparison.Ordinal) || sql.Contains("FOREIGN KEY", StringComparison.Ordinal))
                throw new Exception("Packed TPC concrete table/key schema failed.");

        var query = context.Set<Asset>().OrderBy(a => a.Id);
        var sqlQuery = query.ToQueryString();
        if (Regex.Matches(sqlQuery, "UNION ALL").Count != 2) throw new Exception("Packed TPC union expansion failed.");
        var rows = async ? await query.ToListAsync() : query.ToList();
        if (rows.Count != 3 || rows[0].GetType() != typeof(Asset) || rows[1] is not Document { Pages: 3 }
            || rows[2] is not Sensor { Serial: 7, Reading: null }) throw new Exception("Packed TPC hierarchy materialization failed.");
        context.ChangeTracker.Clear();
        commands.Sql.Clear();
        var sensor = new Sensor { Id = 104, Name = "new", Serial = 8 };
        context.Add(sensor);
        if ((async ? await context.SaveChangesAsync() : context.SaveChanges()) != 1 || commands.Sql.Count != 1
            || !commands.Sql[0].Contains("SensorRows", StringComparison.Ordinal)
            || commands.Sql[0].Contains("RETURNING", StringComparison.Ordinal)) throw new Exception("Packed TPC assigned insert failed.");
        commands.Sql.Clear();
        sensor.Name = null;
        if ((async ? await context.SaveChangesAsync() : context.SaveChanges()) != 1
            || !commands.Sql.Single().StartsWith("UPDATE", StringComparison.Ordinal)) throw new Exception("Packed TPC update failed.");
        commands.Sql.Clear();
        context.Remove(sensor);
        if ((async ? await context.SaveChangesAsync() : context.SaveChanges()) != 1
            || !commands.Sql.Single().StartsWith("DELETE FROM", StringComparison.Ordinal)
            || context.ChangeTracker.Entries().Any()) throw new Exception("Packed TPC delete failed.");

        await using var invalid = new HierarchyContext(Options(mode, commands), generated: true);
        invalid.Add(new Sensor());
        commands.Sql.Clear();
        try
        {
            if (async) await invalid.SaveChangesAsync(); else invalid.SaveChanges();
            throw new Exception("Packed TPC unsafe identity model was accepted.");
        }
        catch (NotSupportedException error) when (error.Message.Contains("ValueGeneratedNever", StringComparison.Ordinal))
        {
            if (commands.Sql.Count != 0) throw new Exception("Packed TPC guard ran after command execution.");
        }
    }

    private static DbContextOptions Options(OceanBaseCompatibilityMode mode, Commands commands)
        => new DbContextOptionsBuilder().UseOceanBase("Server=offline.invalid;Database=package_smoke", mode)
            .EnableServiceProviderCaching(false).AddInterceptors(new NoNetwork(), commands).Options;
    private sealed class HierarchyContext(DbContextOptions options, bool generated = false) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model)
        {
            var root = model.Entity<Asset>();
            root.UseTpcMappingStrategy().ToTable("RootRows");
            if (generated) root.Property(a => a.Id).ValueGeneratedOnAdd();
            else root.Property(a => a.Id).ValueGeneratedNever();
            root.Property(a => a.Name).HasMaxLength(40);
            model.Entity<Equipment>();
            model.Entity<Sensor>().ToTable("SensorRows");
            model.Entity<Document>().ToTable("DocumentRows");
        }
    }
    private class Asset { public int Id { get; set; } public string? Name { get; set; } }
    private abstract class Equipment : Asset { public int Serial { get; set; } }
    private sealed class Sensor : Equipment { public int? Reading { get; set; } }
    private sealed class Document : Asset { public int Pages { get; set; } }

    private sealed class Commands : DbCommandInterceptor
    {
        public List<string> Sql { get; } = [];
        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData data, InterceptionResult<int> result)
        {
            Sql.Add(command.CommandText);
            if (command.Parameters.Cast<DbParameter>().Any(p => p.Direction != ParameterDirection.Input))
                throw new Exception("TPC assigned keys unexpectedly require readback.");
            return InterceptionResult<int>.SuppressWithResult(1);
        }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<int> result, CancellationToken token = default) => ValueTask.FromResult(NonQueryExecuting(command, data, result));
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result)
        {
            Sql.Add(command.CommandText);
            if (!command.CommandText.Contains("UNION ALL", StringComparison.Ordinal)) throw new Exception("Unexpected packed TPC readback.");
            var table = new DataTable();
            foreach (var type in new[] { typeof(int), typeof(string), typeof(int), typeof(int), typeof(int), typeof(string) })
                table.Columns.Add("C" + table.Columns.Count, type);
            table.Rows.Add(101, "root", DBNull.Value, DBNull.Value, DBNull.Value, "Asset");
            table.Rows.Add(102, "doc", 3, DBNull.Value, DBNull.Value, "Document");
            table.Rows.Add(103, "sensor", DBNull.Value, 7, DBNull.Value, "Sensor");
            return InterceptionResult<DbDataReader>.SuppressWithResult(table.CreateDataReader());
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken token = default) => ValueTask.FromResult(ReaderExecuting(command, data, result));
    }
    private sealed class NoNetwork : DbConnectionInterceptor
    {
        public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData data, InterceptionResult result)
            => InterceptionResult.Suppress();
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData data,
            InterceptionResult result, CancellationToken token = default) => ValueTask.FromResult(InterceptionResult.Suppress());
    }
}
