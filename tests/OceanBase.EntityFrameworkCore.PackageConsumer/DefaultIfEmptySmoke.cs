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
    internal static class DefaultIfEmptySmoke
    {
        internal static void Run(OceanBaseCompatibilityMode mode)
        {
            var commands = new Commands();
            using var context = new Context(new DbContextOptionsBuilder<Context>()
                .UseOceanBase("Server=offline;Database=offline", mode).AddInterceptors(new NoNetwork(), commands).Options);
            var rows = context.Set<Row>();
            foreach (var async in new[] { false, true })
            {
                commands.Rows = null;
                commands.Values = [null, 8];
                var nullable = Read(rows.Select(r => r.Value).DefaultIfEmpty(), async);
                if (!nullable.SequenceEqual(new int?[] { null, 8 }) || commands.Sql.Contains("COALESCE", StringComparison.Ordinal))
                    throw new Exception("Packed nullable default compensation changed.");
                commands.Values = [100, 105];
                var converted = Read(rows.Select(r => r.Offset).DefaultIfEmpty(), async);
                if (!converted.SequenceEqual(new[] { 0, 5 }) || !commands.Sql.Contains(".Offset, 100)", StringComparison.Ordinal))
                    throw new Exception("Packed converter-owned default changed.");
                commands.Values = [0, 2];
                var scalar = rows.SelectMany(parent => rows.Where(child => child.Id > parent.Id).OrderBy(child => child.Id).Take(1)
                    .Select(child => child.Id).DefaultIfEmpty());
                if (!Read(scalar, async).SequenceEqual(new[] { 0, 2 })
                    || !commands.Sql.Contains("LEFT JOIN (", StringComparison.Ordinal)
                    || !commands.Sql.Contains("JOIN LATERAL (", StringComparison.Ordinal)
                    || commands.Sql.Contains("LEFT JOIN LATERAL", StringComparison.Ordinal)
                    || !commands.Sql.Contains("COALESCE(", StringComparison.Ordinal)
                    || commands.Readers.Any(reader => !reader.IsClosed))
                    throw new Exception("Packed lateral scalar default rows or cleanup changed.");
                commands.Rows = [[null, null, null], [7, null, 1]];
                var reference = Read(rows.Select(r => new { r.Id, r.Value }).DefaultIfEmpty(), async);
                if (reference[0] is not null || reference[1]?.Id != 7 || reference[1]!.Value is not null)
                    throw new Exception("Packed reference defaults or nullable members changed.");
                commands.Rows = [[null, null], [100, 1], [107, 1]];
                if (!Read(rows.Select(r => (object)r.Offset).DefaultIfEmpty(), async).SequenceEqual(new object?[] { null, 0, 7 }))
                    throw new Exception("Packed boxed converter-owned values changed.");
                commands.Rows = [[null, null], [7, 1]];
                var required = rows.Select(r => r.Value!.Value).DefaultIfEmpty();
                if (!Read(required, async).SequenceEqual(new[] { 0, 7 }))
                    throw new Exception("Packed required scalar row presence changed.");
                commands.Rows = [[null, 1]];
                var rejected = false;
                try { Read(required, async); }
                catch (InvalidOperationException) { rejected = true; }
                if (!rejected || commands.Readers.Any(reader => !reader.IsClosed))
                    throw new Exception("Packed present-NULL required read or cleanup changed.");
            }
            Console.WriteLine($"{mode}: packed DefaultIfEmpty NULL/default/converter/lateral profile passed (offline).");
        }

        private static List<T> Read<T>(IQueryable<T> query, bool async) => async ? query.ToListAsync().GetAwaiter().GetResult() : query.ToList();
        private sealed class Context(DbContextOptions<Context> options) : DbContext(options)
        {
            protected override void OnModelCreating(ModelBuilder model)
            {
                model.Entity<Row>().ToTable("DefaultRows");
                model.Entity<Row>().Property(r => r.Offset).HasConversion(value => value + 100, value => value - 100);
            }
        }
        private sealed class Row { public int Id { get; set; } public int? Value { get; set; } public int Offset { get; set; } }
        private sealed class Commands : DbCommandInterceptor
        {
            public object?[] Values { get; set; } = [];
            public object?[][]? Rows { get; set; }
            public string Sql { get; private set; } = "";
            public List<DbDataReader> Readers { get; } = [];
            public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData e, InterceptionResult<DbDataReader> result)
            {
                Sql = Regex.Replace(command.CommandText.Replace("`", "", StringComparison.Ordinal).Replace("\"", "", StringComparison.Ordinal), @"\s+", " ").Trim();
                var table = new DataTable();
                var rows = Rows ?? Values.Select(value => new[] { value }).ToArray();
                for (var i = 0; i < (Rows is null ? 1 : rows[0].Length); i++) table.Columns.Add("C" + i, typeof(int));
                foreach (var row in rows) table.Rows.Add(row.Select(value => value ?? DBNull.Value).ToArray());
                var reader = table.CreateDataReader();
                Readers.Add(reader);
                return InterceptionResult<DbDataReader>.SuppressWithResult(reader);
            }
            public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData e,
                InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(ReaderExecuting(command, e, result));
        }
        private sealed class NoNetwork : DbConnectionInterceptor
        {
            public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData e, InterceptionResult result)
                => InterceptionResult.Suppress();
            public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData e,
                InterceptionResult result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(InterceptionResult.Suppress());
        }
    }
}
