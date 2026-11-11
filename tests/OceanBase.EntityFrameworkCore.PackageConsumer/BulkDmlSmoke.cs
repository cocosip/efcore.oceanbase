using System;
using System.Collections.Generic;
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
    internal static class BulkDmlSmoke
    {
        public static void Run(OceanBaseCompatibilityMode mode)
        {
            foreach (var async in new[] { false, true })
            {
                var capture = new Capture();
                using var context = new TestContext(new DbContextOptionsBuilder<TestContext>()
                    .UseOceanBase("Server=offline;Database=offline", mode).AddInterceptors(capture).Options);
                var name = "bound";
                var query = context.Set<Row>().Where(row => row.Id > 1).TagWith("bulk package");
                var count = async ? query.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Count, row => row.Count + 1)
                    .SetProperty(row => row.Previous, row => row.Count).SetProperty(row => row.Name, name)).GetAwaiter().GetResult()
                    : query.ExecuteUpdate(setters => setters.SetProperty(row => row.Count, row => row.Count + 1)
                        .SetProperty(row => row.Previous, row => row.Count).SetProperty(row => row.Name, name));
                var expected = mode == OceanBaseCompatibilityMode.MySql
                    ? @"SET \w+\.Previous = \w+\.Count, \w+\.Count = \w+\.Count \+ 1, \w+\.Name ="
                    : @"SET Count = \w+\.Count \+ 1, Previous = \w+\.Count, Name =";
                if (count != 4 || capture.Commands.Count != 1 || !Regex.IsMatch(capture.Commands[0], expected)
                    || !capture.Commands[0].StartsWith("-- bulk package", StringComparison.Ordinal) || !capture.HasBoundName)
                    throw new Exception("Packed bulk update lost tags, original-row assignment order or binding.");
                var opens = capture.Opens;
                var rejected = false;
                try
                {
                    name = "too long for the column";
                    if (async) query.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Name, name)).GetAwaiter().GetResult();
                    else query.ExecuteUpdate(setters => setters.SetProperty(row => row.Name, name));
                }
                catch (ArgumentException) { rejected = true; }
                if (!rejected || capture.Opens != opens || capture.Commands.Count != 1)
                    throw new Exception("Packed bulk setter write bounds did not reject before opening.");
                count = async ? query.ExecuteDeleteAsync().GetAwaiter().GetResult() : query.ExecuteDelete();
                if (count != 4 || capture.Commands.Count != 2 || !capture.Commands[1].Contains("DELETE FROM BulkRows", StringComparison.Ordinal))
                    throw new Exception("Packed bulk delete lost affected rows or its target.");
                count = async ? context.Set<Row>().ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Active, row => row.Count > 0)).GetAwaiter().GetResult()
                    : context.Set<Row>().ExecuteUpdate(setters => setters.SetProperty(row => row.Active, row => row.Count > 0));
                var prefix = mode == OceanBaseCompatibilityMode.Oracle ? "N" : "";
                if (count != 4 || capture.Commands.Count != 3 || !capture.Commands[2].Contains($"THEN {prefix}'Y'", StringComparison.Ordinal)
                    || !capture.Commands[2].Contains($"THEN {prefix}'N'", StringComparison.Ordinal))
                    throw new Exception("Packed computed Boolean setter lost its target encoding.");
                if (mode == OceanBaseCompatibilityMode.Oracle)
                {
                    var joined = context.Set<Row>().Join(context.Set<Row>(), row => row.Id, other => other.Id,
                        (row, other) => new { Target = row, Source = other });
                    count = async ? joined.ExecuteUpdateAsync(setters => setters.SetProperty(pair => pair.Target.Name, pair => pair.Source.Name)).GetAwaiter().GetResult()
                        : joined.ExecuteUpdate(setters => setters.SetProperty(pair => pair.Target.Name, pair => pair.Source.Name));
                    var sql = capture.Commands.Last();
                    if (count != 4 || !sql.StartsWith("UPDATE BulkRows __ob_bulk_target SET Name = (SELECT", StringComparison.Ordinal)
                        || !sql.Contains("WHERE EXISTS (SELECT 1", StringComparison.Ordinal)
                        || Regex.Matches(sql, @" = __ob_bulk_target.Id").Count != 2)
                        throw new Exception("Packed Oracle joined bulk update lost its source or key correlation.");
                }
            }
            Console.WriteLine($"{mode}: packed bulk update/delete, tags, assignment order and write bounds passed (offline).");
        }

        private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
        {
            protected override void OnModelCreating(ModelBuilder model)
            {
                model.Entity<Row>().ToTable("BulkRows");
                model.Entity<Row>().Property(row => row.Name).HasMaxLength(8);
                model.Entity<Row>().Property(row => row.Active).HasConversion(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.BoolToStringConverter("N", "Y"));
            }
        }
        private sealed class Row
        {
            public int Id { get; set; }
            public int Count { get; set; }
            public int Previous { get; set; }
            public string Name { get; set; } = "";
            public bool Active { get; set; }
        }

        private sealed class Capture : DbConnectionInterceptor, IDbCommandInterceptor
        {
            public List<string> Commands { get; } = [];
            public bool HasBoundName { get; private set; }
            public int Opens { get; private set; }
            public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
            { Opens++; return InterceptionResult.Suppress(); }
            public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
                InterceptionResult result, CancellationToken cancellationToken = default) => ValueTask.FromResult(ConnectionOpening(connection, eventData, result));
            public InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
            {
                if (command.Transaction is not null) throw new Exception("Packed bulk operations created an implicit transaction.");
                Commands.Add(Regex.Replace(command.CommandText.Replace("`", "", StringComparison.Ordinal).Replace("\"", "", StringComparison.Ordinal), @"\s+", " ").Trim());
                HasBoundName |= command.Parameters.Cast<DbParameter>().Any(parameter => Equals(parameter.Value, "bound"));
                return InterceptionResult<int>.SuppressWithResult(4);
            }
            public ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
                InterceptionResult<int> result, CancellationToken cancellationToken = default) => ValueTask.FromResult(NonQueryExecuting(command, eventData, result));
        }
    }
}
