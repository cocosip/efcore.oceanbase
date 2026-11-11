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
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.FunctionalTests
{
    public sealed class GeneratedValuesTests
    {
        public static IEnumerable<object[]> Modes()
        {
            foreach (var mode in Enum.GetValues<OceanBaseCompatibilityMode>())
                foreach (var async in new[] { false, true }) yield return [mode, async];
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Nullable_and_converted_multi_outputs_refresh_insert_and_update_snapshots(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new Capture();
            if (mode == OceanBaseCompatibilityMode.Oracle)
            {
                commands.Values["Id"] = new OceanBaseDecimal(101m);
                commands.Values["RequiredCount"] = new OceanBaseDecimal(11m);
                commands.Values["Version"] = new OceanBaseDecimal(2m);
            }
            await using var context = Create(mode, commands);
            var row = new Row { TenantId = 7, Input = "first" };
            context.Add(row);
            using var source = new CancellationTokenSource();
            Assert.Equal(1, async ? await context.SaveChangesAsync(source.Token) : context.SaveChanges());
            Assert.Equal(101, row.Id);
            Assert.Equal("server default", row.DefaultText);
            Assert.Equal(11, row.RequiredCount);
            Assert.Null(row.NullableCount);
            Assert.Equal(Status.Ready, row.Version);
            Assert.Equal(EntityState.Unchanged, context.Entry(row).State);
            Assert.Equal(Status.Ready, context.Entry(row).Property(r => r.Version).OriginalValue);
            Assert.False(context.Entry(row).Property(r => r.Id).IsTemporary);
            AssertPlan(commands, mode, async, source.Token, inserted: true);

            commands.Items.Clear();
            commands.Values["RequiredCount"] = 12;
            commands.Values["Version"] = 3;
            commands.Values["DefaultText"] = "must not refresh";
            row.Input = "second";
            Assert.Equal(1, async ? await context.SaveChangesAsync(source.Token) : context.SaveChanges());
            Assert.Equal(12, row.RequiredCount);
            Assert.Equal(Status.Done, row.Version);
            Assert.Equal("server default", row.DefaultText);
            Assert.Equal(Status.Done, context.Entry(row).Property(r => r.Version).OriginalValue);
            AssertPlan(commands, mode, async, source.Token, inserted: false);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Explicit_default_value_is_written_and_not_read_back(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new Capture();
            await using var context = Create(mode, commands);
            var row = new Row { TenantId = 7, Input = "first", DefaultText = "client override" };
            context.Add(row);
            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            Assert.Equal("client override", row.DefaultText);
            Assert.Contains("DefaultText", commands.Items[0].Sql, StringComparison.Ordinal);
            Assert.Contains("client override", commands.Items[0].Inputs);
            Assert.DoesNotContain("DefaultText", commands.ReadColumns);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Nullable_converted_output_uses_NULL_original_predicate_until_refreshed(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new Capture();
            commands.Values["Version"] = null;
            await using var context = Create(mode, commands);
            var row = new Row { TenantId = 7, Input = "first" };
            context.Add(row);
            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            Assert.Null(row.Version);
            Assert.Null(context.Entry(row).Property(r => r.Version).OriginalValue);
            commands.Items.Clear();
            commands.Values["Version"] = 3;
            row.Input = "second";
            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            Assert.Equal(Status.Done, row.Version);
            Assert.Equal(Status.Done, context.Entry(row).Property(r => r.Version).OriginalValue);
            Assert.Contains("Version IS NULL", commands.Items[0].Sql, StringComparison.Ordinal);
            Assert.Equal(new object?[] { "second", 101, 7 }, commands.Items[0].Inputs);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Required_generated_NULL_fails_instead_of_accepting_a_CLR_default(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new Capture();
            commands.Values["RequiredCount"] = null;
            await using var context = Create(mode, commands);
            var row = new Row { TenantId = 7, Input = "first" };
            context.Add(row);
            var error = async ? await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync())
                : Assert.Throws<DbUpdateException>(() => context.SaveChanges());
            Assert.NotNull(error.InnerException);
            Assert.Same(row, Assert.Single(error.Entries).Entity);
            Assert.Equal(EntityState.Added, context.Entry(row).State);
            Assert.True(context.Entry(row).Property(r => r.Id).IsTemporary);
            Assert.Equal(0, row.Id);
            Assert.Equal(0, row.RequiredCount);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Later_output_conversion_failure_discards_partial_generated_values(OceanBaseCompatibilityMode mode, bool async)
        {
            foreach (var update in new[] { false, true })
            {
                var commands = new Capture();
                await using var context = Create(mode, commands);
                var row = new Row { TenantId = 7, Input = "first" };
                context.Add(row);
                if (update)
                {
                    if (async) await context.SaveChangesAsync();
                    else context.SaveChanges();
                    row.Input = "changed";
                }
                commands.Items.Clear();
                commands.Values["RequiredCount"] = 99;
                commands.Values["Version"] = 1.5m;
                var error = async ? await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync())
                    : Assert.Throws<DbUpdateException>(() => context.SaveChanges());
                Assert.IsType<InvalidCastException>(error.InnerException);
                Assert.Same(row, Assert.Single(error.Entries).Entity);
                Assert.Equal(update ? EntityState.Modified : EntityState.Added, context.Entry(row).State);
                Assert.Equal(update ? 101 : 0, row.Id);
                Assert.Equal(update ? 11 : 0, row.RequiredCount);
                Assert.Equal(update ? Status.Ready : null, row.Version);
                Assert.Equal(update ? Status.Ready : null, context.Entry(row).Property(r => r.Version).OriginalValue);
                Assert.Equal(!update, context.Entry(row).Property(r => r.Id).IsTemporary);
                Assert.Equal(update ? "changed" : "first", row.Input);
                Assert.Equal(mode == OceanBaseCompatibilityMode.MySql ? 2 : 1, commands.Items.Count);
            }
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Zero_rows_does_not_apply_outputs_or_start_a_MySql_readback(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new Capture { AffectedRows = 0 };
            await using var context = Create(mode, commands);
            var row = new Row { TenantId = 7, Input = "first" };
            context.Add(row);
            var error = async ? await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => context.SaveChangesAsync())
                : Assert.Throws<DbUpdateConcurrencyException>(() => context.SaveChanges());
            Assert.Same(row, Assert.Single(error.Entries).Entity);
            Assert.Single(commands.Items);
            Assert.Equal(EntityState.Added, context.Entry(row).State);
            Assert.True(context.Entry(row).Property(r => r.Id).IsTemporary);
            Assert.Equal(0, row.Id);
            Assert.Null(row.DefaultText);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Missing_MySql_generated_row_retains_the_added_entry(bool async)
        {
            var commands = new Capture { MissingRow = true };
            await using var context = Create(OceanBaseCompatibilityMode.MySql, commands);
            var row = new Row { TenantId = 7, Input = "first" };
            context.Add(row);
            var error = async ? await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => context.SaveChangesAsync())
                : Assert.Throws<DbUpdateConcurrencyException>(() => context.SaveChanges());
            Assert.Same(row, Assert.Single(error.Entries).Entity);
            Assert.Equal(2, commands.Items.Count);
            Assert.Equal(EntityState.Added, context.Entry(row).State);
            Assert.True(context.Entry(row).Property(r => r.Id).IsTemporary);
            Assert.Equal(0, row.Id);
        }

        private static void AssertPlan(Capture capture, OceanBaseCompatibilityMode mode, bool async, CancellationToken token, bool inserted)
        {
            Assert.Equal(mode == OceanBaseCompatibilityMode.MySql ? 2 : 1, capture.Items.Count);
            var write = capture.Items[0];
            Assert.StartsWith(inserted ? "INSERT INTO" : "UPDATE", write.Sql);
            Assert.Equal(inserted ? new[] { "DefaultText", "Id", "NullableCount", "RequiredCount", "Version" }
                : new[] { "NullableCount", "RequiredCount", "Version" }, capture.ReadColumns.OrderBy(c => c, StringComparer.Ordinal));
            if (mode == OceanBaseCompatibilityMode.MySql)
            {
                var read = capture.Items[1];
                Assert.StartsWith("SELECT", read.Sql);
                Assert.Contains(inserted ? "Id = LAST_INSERT_ID()" : "Id = ?", read.Sql, StringComparison.Ordinal);
                Assert.Contains("TenantId = ?", read.Sql, StringComparison.Ordinal);
                Assert.Equal(inserted ? new object?[] { 7 } : new object?[] { 101, 7 }, read.Inputs);
            }
            else
            {
                Assert.Contains(" RETURNING ", write.Sql, StringComparison.Ordinal);
                Assert.Equal(capture.ReadColumns.Length, write.Outputs);
            }
            Assert.Equal(inserted ? new object?[] { 7, "first" } : new object?[] { "second", 101, 7, 2 }, write.Inputs);
            Assert.All(capture.Items, item =>
            {
                Assert.Equal(async, item.Async);
                if (async) Assert.Equal(token, item.Token);
                Assert.Same(write.Connection, item.Connection);
                Assert.Null(item.Transaction);
            });
        }

        private static TestContext Create(OceanBaseCompatibilityMode mode, Capture commands)
        {
            var context = new TestContext(new DbContextOptionsBuilder<TestContext>().UseOceanBase("Server=offline;Database=offline", mode)
                .AddInterceptors(commands).Options);
            context.Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
            return context;
        }

        private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
        {
            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                var row = modelBuilder.Entity<Row>();
                row.ToTable("GeneratedRows");
                row.HasKey(r => new { r.TenantId, r.Id });
                row.Property(r => r.Id).ValueGeneratedOnAdd();
                row.Property(r => r.Input).HasMaxLength(40);
                row.Property(r => r.DefaultText).HasMaxLength(40).HasDefaultValue("server default");
                row.Property(r => r.RequiredCount).HasComputedColumnSql("11");
                row.Property(r => r.NullableCount).HasComputedColumnSql("NULL");
                row.Property(r => r.Version).HasConversion<int>().HasComputedColumnSql("2").IsConcurrencyToken();
            }
        }

        private enum Status { Ready = 2, Done = 3 }
        private sealed class Row
        {
            public int TenantId { get; set; }
            public int Id { get; set; }
            public string Input { get; set; } = "";
            public string? DefaultText { get; set; }
            public int RequiredCount { get; set; }
            public int? NullableCount { get; set; }
            public Status? Version { get; set; }
        }

        private sealed record Command(string Sql, object?[] Inputs, int Outputs, DbConnection? Connection, DbTransaction? Transaction,
            bool Async, CancellationToken Token);

        private sealed class Capture : DbConnectionInterceptor, IDbCommandInterceptor
        {
            public Dictionary<string, object?> Values { get; } = new(StringComparer.Ordinal)
            {
                ["Id"] = 101, ["DefaultText"] = "server default", ["RequiredCount"] = 11, ["NullableCount"] = null, ["Version"] = 2
            };
            public List<Command> Items { get; } = [];
            public string[] ReadColumns { get; private set; } = [];
            public int AffectedRows { get; init; } = 1;
            public bool MissingRow { get; init; }
            public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
                => InterceptionResult.Suppress();
            public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
                InterceptionResult result, CancellationToken cancellationToken = default) => ValueTask.FromResult(InterceptionResult.Suppress());
            private string Record(DbCommand command, bool async, CancellationToken token)
            {
                var sql = Regex.Replace(command.CommandText.Replace("`", "", StringComparison.Ordinal).Replace("\"", "", StringComparison.Ordinal), @"\s+", " ").Trim();
                var parameters = command.Parameters.Cast<DbParameter>().ToArray();
                var inputs = new List<object?>();
                var position = 0;
                foreach (Match bind in Regex.Matches(sql, @"[:@]\w+|\?", RegexOptions.CultureInvariant))
                {
                    var parameter = bind.Value == "?" ? parameters[position++]
                        : parameters.Single(p => p.ParameterName.TrimStart(':', '@') == bind.Value.TrimStart(':', '@'));
                    if (parameter.Direction == ParameterDirection.Input) inputs.Add(parameter.Value);
                }
                Items.Add(new Command(sql, inputs.ToArray(),
                    parameters.Count(p => p.Direction == ParameterDirection.Output), command.Connection, command.Transaction, async, token));
                return sql;
            }
            private InterceptionResult<int> Write(DbCommand command, bool async, CancellationToken token)
            {
                var sql = Record(command, async, token);
                var start = sql.IndexOf(" RETURNING ", StringComparison.Ordinal);
                if (start >= 0)
                {
                    var into = sql.IndexOf(" INTO ", start, StringComparison.Ordinal);
                    ReadColumns = sql[(start + 11)..into].Split(", ");
                    var outputs = command.Parameters.Cast<DbParameter>().Where(p => p.Direction == ParameterDirection.Output).ToArray();
                    var binds = Regex.Matches(sql[(into + 6)..], @"[:@](\w+)", RegexOptions.CultureInvariant)
                        .Select(m => m.Groups[1].Value).ToArray();
                    Assert.Equal(ReadColumns.Length, binds.Length);
                    Assert.Equal(binds.Length, binds.Distinct(StringComparer.Ordinal).Count());
                    Assert.Equal(outputs.Select(p => p.ParameterName.TrimStart(':', '@')).Order(StringComparer.Ordinal), binds.Order(StringComparer.Ordinal));
                    for (var i = 0; i < binds.Length; i++)
                        outputs.Single(p => p.ParameterName.TrimStart(':', '@') == binds[i]).Value = Values[ReadColumns[i]] ?? DBNull.Value;
                }
                return InterceptionResult<int>.SuppressWithResult(AffectedRows);
            }
            private InterceptionResult<DbDataReader> Read(DbCommand command, bool async, CancellationToken token)
            {
                var sql = Record(command, async, token);
                ReadColumns = sql[7..sql.IndexOf(" FROM ", StringComparison.Ordinal)].Split(", ");
                var table = new DataTable();
                foreach (var column in ReadColumns) table.Columns.Add(column, typeof(object));
                if (!MissingRow) table.Rows.Add(ReadColumns.Select(column => Values[column] ?? DBNull.Value).ToArray());
                return InterceptionResult<DbDataReader>.SuppressWithResult(table.CreateDataReader());
            }
            public InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
                => Write(command, false, default);
            public ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
                InterceptionResult<int> result, CancellationToken cancellationToken = default) => ValueTask.FromResult(Write(command, true, cancellationToken));
            public InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
                => Read(command, false, default);
            public ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
                InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) => ValueTask.FromResult(Read(command, true, cancellationToken));
        }
    }
}
