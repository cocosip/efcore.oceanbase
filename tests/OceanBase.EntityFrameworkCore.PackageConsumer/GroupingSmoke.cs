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
using OceanBase.EntityFrameworkCore;

namespace PackageConsumer
{
    internal static class GroupingSmoke
    {
        public static void Run(OceanBaseCompatibilityMode mode)
        {
            foreach (var relationalNulls in new[] { false, true })
            foreach (var async in new[] { false, true })
            foreach (var resultSelector in new[] { false, true })
            {
                var commands = new Commands();
                var builder = new DbContextOptionsBuilder<Context>().UseOceanBase("Server=offline;Database=app", mode)
                    .AddInterceptors(new NoNetwork(), commands);
                ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(
                    builder.Options.Extensions.OfType<RelationalOptionsExtension>().Single().WithUseRelationalNulls(relationalNulls));
                using var context = new Context(builder.Options);
                var rows = context.Set<Row>();
                var query = resultSelector
                    ? rows.GroupBy(r => r.Key, (key, group) => new { Key = key, Count = group.Count(), First = group.OrderBy(r => r.Id).FirstOrDefault() })
                    : rows.GroupBy(r => r.Key).Select(group => new { group.Key, Count = group.Count(), First = group.OrderBy(r => r.Id).FirstOrDefault() });
                var result = async ? query.OrderBy(r => r.Key).ToListAsync().GetAwaiter().GetResult() : query.OrderBy(r => r.Key).ToList();
                if (result.Count != 2 || result[0].Key is not null || result[0].Count != 2
                    || result[0].First is not { Id: 1, Key: null, Tag: 7, Value: 12 }
                    || result[1].Key != 1 || result[1].First is not { Id: 3, Key: 1, Tag: 8, Value: null })
                    throw new Exception("Packed grouped projection lost supplied results or conversion.");
                if (!Regex.IsMatch(commands.Sql, @"\w+\.Key IS NULL AND \w+\.Key IS NULL")
                    || !commands.Sql.Contains("LEFT JOIN LATERAL", StringComparison.Ordinal)
                    || !Regex.IsMatch(commands.Sql, @"^SELECT (\w+)\.Key, \1\.c, (\w+)\.Id, \2\.Key, \2\.Tag, \2\.Value FROM")
                    || commands.Readers.Any(reader => !reader.IsClosed))
                    throw new Exception("Packed grouped SQL/projection or cleanup changed: " + commands.Sql);
            }
            Console.WriteLine($"{mode}: packed grouped first/aggregate, NULL correlation, conversion and cleanup passed (offline).");
        }

        private sealed class Context(DbContextOptions<Context> options) : DbContext(options)
        {
            protected override void OnModelCreating(ModelBuilder model)
            {
                model.Entity<Row>().ToTable("GroupingRows");
                model.Entity<Row>().Property(r => r.Tag).HasConversion(value => value + 100, value => value - 100);
            }
        }
        private sealed class Row
        {
            public int Id { get; set; }
            public int? Key { get; set; }
            public int Tag { get; set; }
            public int? Value { get; set; }
        }
        private sealed class Commands : DbCommandInterceptor
        {
            public string Sql { get; private set; } = "";
            public List<DbDataReader> Readers { get; } = [];
            public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
            {
                Sql = Regex.Replace(command.CommandText.Replace("`", "", StringComparison.Ordinal)
                    .Replace("\"", "", StringComparison.Ordinal).Replace(" AS ", " ", StringComparison.Ordinal), @"\s+", " ").Trim();
                var table = new DataTable();
                for (var i = 0; i < 6; i++) table.Columns.Add("C" + i, typeof(int));
                table.Rows.Add(DBNull.Value, 2, 1, DBNull.Value, 107, 12);
                table.Rows.Add(1, 1, 3, 1, 108, DBNull.Value);
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
