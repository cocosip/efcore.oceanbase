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
using OceanBase.EntityFrameworkCore;

namespace PackageConsumer
{
    internal static class TphRuntimeSmoke
    {
        public static void Run(OceanBaseCompatibilityMode mode)
        {
            foreach (var async in new[] { false, true })
            {
                var commands = new Commands();
                using var context = new HierarchyContext(new DbContextOptionsBuilder<HierarchyContext>()
                    .UseOceanBase("Server=offline;Database=offline", mode)
                    .AddInterceptors(new NoNetwork(), commands).Options);
                context.Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
                var query = context.Set<Record>().AsNoTracking().OrderBy(row => row.Id);
                var rows = async ? query.ToListAsync().GetAwaiter().GetResult() : query.ToList();
                if (rows.Count != 2 || rows[0].GetType() != typeof(Record) || rows[0].Name != "root"
                    || rows[1] is not Report { Id: 2, Name: null, Pages: 9 } || context.ChangeTracker.Entries().Any())
                    throw new Exception("Packed TPH discriminator materialization changed.");
                var derivedSql = context.Set<Record>().OfType<Report>().ToQueryString();
                if (!derivedSql.Contains(mode == OceanBaseCompatibilityMode.MySql ? "`Kind` = 'Report'" : "\"Kind\" = N'Report'", StringComparison.Ordinal))
                    throw new Exception("Packed TPH derived query lost its converted discriminator predicate.");

                var report = new Report { Id = 3, Name = "new", Pages = 12 };
                context.Add(report);
                Save(context, async);
                commands.AssertLast("INSERT INTO PackedTphRows (Id, Kind, Name, Pages) VALUES (?, ?, ?, ?);", [3, "Report", "new", 12]);
                report.Name = "changed";
                Save(context, async);
                commands.AssertLast("UPDATE PackedTphRows SET Name = ? WHERE Id = ?;", ["changed", 3]);
                context.Remove(report);
                Save(context, async);
                commands.AssertLast("DELETE FROM PackedTphRows WHERE Id = ?;", [3]);
                if (context.ChangeTracker.Entries().Any() || commands.Readers.Any(reader => !reader.IsClosed))
                    throw new Exception("Packed TPH leaves accepted entries or readers behind.");
            }
            Console.WriteLine($"{mode}: packed TPH discriminator, derived query and single-table CRUD passed (offline).");
        }

        private static void Save(DbContext context, bool async)
        {
            if ((async ? context.SaveChangesAsync().GetAwaiter().GetResult() : context.SaveChanges()) != 1)
                throw new Exception("Packed TPH SaveChanges returned an unexpected command count.");
        }

        private sealed class HierarchyContext(DbContextOptions<HierarchyContext> options) : DbContext(options)
        {
            protected override void OnModelCreating(ModelBuilder model)
            {
                var root = model.Entity<Record>();
                root.UseTphMappingStrategy().ToTable("PackedTphRows");
                root.Property(row => row.Id).ValueGeneratedNever();
                root.HasDiscriminator<Kind>("Kind").HasValue<Record>(Kind.Root).HasValue<Report>(Kind.Report);
                root.Property<Kind>("Kind").HasConversion<string>().HasMaxLength(16);
                root.Property(row => row.Name).HasMaxLength(40);
            }
        }

        private enum Kind { Root, Report }
        private class Record { public int Id { get; set; } public string? Name { get; set; } }
        private sealed class Report : Record { public int Pages { get; set; } }

        private sealed class Commands : DbCommandInterceptor
        {
            private string _sql = "";
            private object?[] _inputs = [];
            public List<DbDataReader> Readers { get; } = [];
            public void AssertLast(string template, object?[] inputs)
            {
                if (_sql != template || !_inputs.SequenceEqual(inputs))
                    throw new Exception("Packed TPH single-table DML or discriminator bindings changed: " + _sql
                        + " [" + string.Join(", ", _inputs) + "]");
            }
            public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
            {
                _sql = Regex.Replace(Regex.Replace(command.CommandText.Replace("`", "", StringComparison.Ordinal)
                    .Replace("\"", "", StringComparison.Ordinal).Trim(), @"[@:]\w+", "?"), @"\s+", " ");
                var parameters = command.Parameters.Cast<DbParameter>().ToArray();
                var position = 0;
                _inputs = Regex.Matches(command.CommandText, @"[@:]\w+|\?")
                    .Select(match => match.Value == "?" ? parameters[position++]
                        : parameters.Single(p => p.ParameterName.TrimStart('@', ':') == match.Value.TrimStart('@', ':')))
                    .Where(p => p.Direction == ParameterDirection.Input).Select(p => p.Value).ToArray();
                return InterceptionResult<int>.SuppressWithResult(1);
            }
            public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
                InterceptionResult<int> result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(NonQueryExecuting(command, eventData, result));
            public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData,
                InterceptionResult<DbDataReader> result)
            {
                var table = new DataTable();
                table.Columns.Add("Id", typeof(int));
                table.Columns.Add("Kind", typeof(string));
                table.Columns.Add("Name", typeof(string));
                table.Columns.Add("Pages", typeof(int));
                table.Rows.Add(1, "Root", "root", DBNull.Value);
                table.Rows.Add(2, "Report", DBNull.Value, 9);
                var reader = table.CreateDataReader();
                Readers.Add(reader);
                return InterceptionResult<DbDataReader>.SuppressWithResult(reader);
            }
            public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
                InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(ReaderExecuting(command, eventData, result));
        }

        private sealed class NoNetwork : DbConnectionInterceptor
        {
            public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
                => InterceptionResult.Suppress();
            public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
                InterceptionResult result, CancellationToken cancellationToken = default) => ValueTask.FromResult(InterceptionResult.Suppress());
        }
    }
}
