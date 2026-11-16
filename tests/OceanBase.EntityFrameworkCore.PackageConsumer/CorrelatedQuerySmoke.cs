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
    internal static class CorrelatedQuerySmoke
    {
        public static void Run(OceanBaseCompatibilityMode mode)
        {
            var commands = new Commands();
            using var context = new Context(new DbContextOptionsBuilder<Context>()
                .UseOceanBase("Server=offline;Database=offline", mode).AddInterceptors(new NoNetwork(), commands).Options);
            var rows = context.Set<Row>();
            var floor = 1;
            var skip = 1;
            var take = 2;
            foreach (var outer in new[] { false, true })
            {
                commands.Outer = outer;
                var query = rows.SelectMany(parent => outer
                    ? rows.Where(child => child.Id > parent.Id + floor).OrderBy(child => child.Id).Skip(skip).Take(take).DefaultIfEmpty()
                    : rows.Where(child => child.Id > parent.Id + floor).OrderBy(child => child.Id).Skip(skip).Take(take),
                    (parent, child) => new { Parent = parent.Id, Child = (int?)child!.Id });
                foreach (var value in new[] { 1, 3 })
                {
                    floor = value;
                    foreach (var async in new[] { false, true })
                    {
                        var result = async ? query.ToListAsync().GetAwaiter().GetResult() : query.ToList();
                        if (!result.Select(r => r.Parent).SequenceEqual(outer ? new[] { 1, 4 } : [1])
                            || !result.Select(r => r.Child).SequenceEqual(outer ? new int?[] { 2, null } : [2]))
                            throw new Exception("Packed lateral projection lost supplied nullable results.");
                        if (!commands.Sql.Contains(outer ? "LEFT JOIN LATERAL (" : "JOIN LATERAL (", StringComparison.Ordinal)
                            || commands.Sql.Contains("APPLY", StringComparison.Ordinal)
                            || !commands.Sql.Contains("ON 1 = 1", StringComparison.Ordinal)
                            || !commands.Sql.Contains("c0.Id > (c.Id + ?)", StringComparison.Ordinal)
                            || !Regex.IsMatch(commands.Sql, @"^SELECT c\.Id Parent, (\w+)\.Id Child FROM CorrelationRows c")
                            || !commands.Inputs.SequenceEqual(mode == OceanBaseCompatibilityMode.MySql ? new object?[] { floor, take, skip } : [floor, skip, take])
                            || commands.Readers.Any(reader => !reader.IsClosed))
                            throw new Exception("Packed lateral SQL, bindings or cleanup changed: " + commands.Sql);
                    }
                }
            }
            var lifted = rows.SelectMany(parent => rows.Select(child => parent.Value + child.Value)).ToQueryString();
            if (lifted.Contains("LATERAL", StringComparison.Ordinal) || lifted.Contains("APPLY", StringComparison.Ordinal)
                || !lifted.Contains("CROSS JOIN", StringComparison.Ordinal))
                throw new Exception("Packed lifted projection lost ordinary base-table join syntax.");
            Console.WriteLine($"{mode}: packed lateral queries, NULL extension, bindings and cleanup passed (offline).");
        }

        private sealed class Context(DbContextOptions<Context> options) : DbContext(options)
        {
            protected override void OnModelCreating(ModelBuilder model) => model.Entity<Row>().ToTable("CorrelationRows");
        }
        private sealed class Row { public int Id { get; set; } public int? Value { get; set; } }
        private sealed class Commands : DbCommandInterceptor
        {
            public bool Outer { get; set; }
            public string Sql { get; private set; } = "";
            public object?[] Inputs { get; private set; } = [];
            public List<DbDataReader> Readers { get; } = [];
            public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
            {
                Sql = Regex.Replace(Regex.Replace(command.CommandText.Replace("`", "", StringComparison.Ordinal)
                    .Replace("\"", "", StringComparison.Ordinal).Replace(" AS ", " ", StringComparison.Ordinal), @"[@:]\w+", "?"), @"\s+", " ").Trim();
                var parameters = command.Parameters.Cast<DbParameter>().ToArray();
                var position = 0;
                Inputs = Regex.Matches(command.CommandText, @"[@:]\w+|\?")
                    .Select(m => m.Value == "?" ? parameters[position++] : parameters.Single(p => p.ParameterName.TrimStart('@', ':') == m.Value.TrimStart('@', ':')))
                    .Select(p => p.Value).ToArray();
                var table = new DataTable();
                table.Columns.Add("Parent", typeof(int));
                table.Columns.Add("Child", typeof(int));
                table.Rows.Add(1, 2);
                if (Outer) table.Rows.Add(4, DBNull.Value);
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
                InterceptionResult result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(InterceptionResult.Suppress());
        }
    }
}
