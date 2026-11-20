using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OceanBase.EntityFrameworkCore;

namespace PackageConsumer
{
    internal static class RawQuerySmoke
    {
        public static void Run(OceanBaseCompatibilityMode mode)
        {
            var commands = new Commands();
            using var context = new RawContext(new DbContextOptionsBuilder<RawContext>()
                .UseOceanBase("Server=offline;Database=offline", mode).AddInterceptors(new NoNetwork(), commands).Options);
            foreach (var async in new[] { false, true })
            {
                var input = "'; DROP TABLE RawRows; -- 海🌊";
                var query = context.Set<Row>().FromSqlInterpolated($"SELECT RowKey, State, DisplayName FROM RawRows WHERE DisplayName = {input}")
                    .AsNoTrackingWithIdentityResolution();
                List<Row> Run() => async ? query.ToListAsync().GetAwaiter().GetResult() : query.ToList();
                var result = Run();
                if (!result.Select(r => r.Id).SequenceEqual(new[] { 7, 7, 8 })
                    || !ReferenceEquals(result[0], result[1]) || result[0].Kind != Kind.Active
                    || result[0].Name != "海🌊" || result[^1].Kind is not null || result[^1].Name is not null
                    || !commands.Inputs.SequenceEqual(new object?[] { input }) || commands.Sql.Contains(input, StringComparison.Ordinal)
                    || !commands.Sql.Contains(mode == OceanBaseCompatibilityMode.MySql ? " = ?" : " = :p0", StringComparison.Ordinal)
                    || context.ChangeTracker.Entries().Any())
                    throw new Exception("Packed raw query lost column-name mapping, converted/null results, identity or parameter isolation.");
                commands.MissingColumn = true;
                try { Run(); }
                catch (InvalidOperationException error) when (error.Message.Contains("State", StringComparison.Ordinal))
                {
                    commands.MissingColumn = false;
                    if (Run().Count != 3 || commands.Readers.Any(reader => !reader.IsClosed))
                        throw new Exception("Packed raw query did not recover after missing-column reader failure.");
                    continue;
                }
                throw new Exception("Packed raw query accepted a missing mapped result column.");
            }
            Console.WriteLine($"{mode}: packed raw query column mapping, identity, bindings and schema-error recovery passed (offline).");
        }

        private sealed class RawContext(DbContextOptions<RawContext> options) : DbContext(options)
        {
            protected override void OnModelCreating(ModelBuilder model)
            {
                var row = model.Entity<Row>().ToTable("RawRows");
                row.Property(r => r.Id).HasColumnName("RowKey");
                row.Property(r => r.Kind).HasColumnName("State").HasConversion<string>().HasMaxLength(16);
                row.Property(r => r.Name).HasColumnName("DisplayName").HasMaxLength(40);
            }
        }
        private enum Kind { Active, Archived }
        private sealed class Row { public int Id { get; set; } public Kind? Kind { get; set; } public string? Name { get; set; } }
        private sealed class Commands : DbCommandInterceptor
        {
            public bool MissingColumn { get; set; }
            public string Sql { get; private set; } = "";
            public object?[] Inputs { get; private set; } = [];
            public List<DbDataReader> Readers { get; } = [];
            public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData e, InterceptionResult<DbDataReader> result)
            {
                Sql = command.CommandText;
                Inputs = command.Parameters.Cast<DbParameter>().Select(p => p.Value).ToArray();
                var table = new DataTable();
                table.Columns.Add("displayname", typeof(string));
                table.Columns.Add("state", typeof(string));
                table.Columns.Add("rowkey", typeof(int));
                table.Columns.Add("extra", typeof(int));
                table.Rows.Add("海🌊", "Active", 7, 0);
                table.Rows.Add("海🌊", "Active", 7, 0);
                table.Rows.Add(DBNull.Value, DBNull.Value, 8, 0);
                if (MissingColumn) table.Columns.Remove("state");
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
                InterceptionResult result, CancellationToken cancellationToken = default) => ValueTask.FromResult(InterceptionResult.Suppress());
        }
    }
}
