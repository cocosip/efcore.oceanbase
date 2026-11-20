using System;
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
    internal static class SetOperationSmoke
    {
        public static void Run(OceanBaseCompatibilityMode mode)
        {
            var commands = new Commands();
            var network = new NoNetwork();
            using var context = new SetContext(new DbContextOptionsBuilder<SetContext>()
                .UseOceanBase("Server=offline;Database=offline", mode).AddInterceptors(network, commands).Options);
            var rows = context.Set<Row>();
            var boundary = 10;
            foreach (var operation in new[] { "Union", "Concat", "Intersect", "Except" })
            {
                var query = Set(rows.Where(r => r.Id < boundary).Select(r => r.Value),
                    rows.Where(r => r.Id > boundary).Select(r => r.Value), operation).OrderBy(v => v);
                foreach (var value in new[] { 10, 20 })
                {
                    boundary = value;
                    foreach (var async in new[] { false, true })
                    {
                        var result = async ? query.ToListAsync().GetAwaiter().GetResult() : query.ToList();
                        if (!result.SequenceEqual(new int?[] { null, 2 }) || !commands.Inputs.SequenceEqual(new object?[] { value, value }))
                            throw new Exception("Packed set projection lost nullable results or current operand bindings.");
                        var keyword = operation switch { "Concat" => "UNION ALL", "Intersect" => "INTERSECT",
                            "Except" => mode == OceanBaseCompatibilityMode.Oracle ? "MINUS" : "EXCEPT", _ => "UNION" };
                        if (!Regex.Matches(commands.Sql, @"\b(?:UNION ALL|UNION|INTERSECT|MINUS|EXCEPT)\b").Select(m => m.Value).SequenceEqual(new[] { keyword })
                            || !commands.Sql.Contains("s.Id < ?", StringComparison.Ordinal)
                            || !commands.Sql.Contains("s0.Id > ?", StringComparison.Ordinal)
                            || !commands.Sql.Contains("ORDER BY", StringComparison.Ordinal)
                            || !commands.LastReader!.IsClosed)
                            throw new Exception("Packed set dialect, operand SQL or reader cleanup changed: " + commands.Sql);
                    }
                }
            }
            var a = rows.Where(r => r.Id == 1).Select(r => r.Value);
            var b = rows.Where(r => r.Id == 2).Select(r => r.Value);
            var c = rows.Where(r => r.Id == 3).Select(r => r.Value);
            var nested = Regex.Replace(a.Except(b.Except(c)).ToQueryString(), @"\s+", " ");
            if (!nested.Contains((mode == OceanBaseCompatibilityMode.Oracle ? "MINUS" : "EXCEPT") + " ( SELECT", StringComparison.Ordinal))
                throw new Exception("Packed difference lost right operand grouping.");
            if (mode == OceanBaseCompatibilityMode.Oracle)
            {
                var lob = rows.Select(r => r.Lob);
                foreach (var operation in new[] { "Union", "Concat", "Intersect", "Except" })
                {
                    var opens = network.Opens;
                    var calls = commands.Calls;
                    try { Set(lob, lob, operation).ToList(); }
                    catch (NotSupportedException error) when (error.Message.Contains("LOB", StringComparison.Ordinal))
                    {
                        if (network.Opens != opens || commands.Calls != calls)
                            throw new Exception("Packed LOB rejection happened after ADO opening/execution.");
                        continue;
                    }
                    throw new Exception("Packed OceanBase Oracle set query accepted a LOB projection.");
                }
            }
            Console.WriteLine($"{mode}: packed set operators, grouping, nullable results and bindings passed (offline).");
        }

        private static IQueryable<T> Set<T>(IQueryable<T> left, IQueryable<T> right, string operation) => operation switch
        { "Union" => left.Union(right), "Concat" => left.Concat(right), "Intersect" => left.Intersect(right), _ => left.Except(right) };
        private sealed class SetContext(DbContextOptions<SetContext> options) : DbContext(options)
        {
            protected override void OnModelCreating(ModelBuilder model)
            {
                model.Entity<Row>().ToTable("SetRows");
                model.Entity<Row>().Property(r => r.Lob).HasMaxLength(8000);
            }
        }
        private sealed class Row { public int Id { get; set; } public int? Value { get; set; } public string? Lob { get; set; } }

        private sealed class Commands : DbCommandInterceptor
        {
            public string Sql { get; private set; } = "";
            public object?[] Inputs { get; private set; } = [];
            public DbDataReader? LastReader { get; private set; }
            public int Calls { get; private set; }
            public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
            {
                Calls++;
                Sql = Regex.Replace(Regex.Replace(command.CommandText.Replace("`", "", StringComparison.Ordinal)
                    .Replace("\"", "", StringComparison.Ordinal), @"[@:]\w+", "?"), @"\s+", " ").Trim();
                var parameters = command.Parameters.Cast<DbParameter>().ToArray();
                var position = 0;
                Inputs = Regex.Matches(command.CommandText, @"[@:]\w+|\?")
                    .Select(m => m.Value == "?" ? parameters[position++] : parameters.Single(p => p.ParameterName.TrimStart('@', ':') == m.Value.TrimStart('@', ':')))
                    .Select(p => p.Value).ToArray();
                var table = new DataTable();
                table.Columns.Add("Value", typeof(int));
                table.Rows.Add(DBNull.Value);
                table.Rows.Add(2);
                LastReader = table.CreateDataReader();
                return InterceptionResult<DbDataReader>.SuppressWithResult(LastReader);
            }
            public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
                InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(ReaderExecuting(command, eventData, result));
        }
        private sealed class NoNetwork : DbConnectionInterceptor
        {
            public int Opens { get; private set; }
            public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
            { Opens++; return InterceptionResult.Suppress(); }
            public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
                InterceptionResult result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(ConnectionOpening(connection, eventData, result));
        }
    }
}
