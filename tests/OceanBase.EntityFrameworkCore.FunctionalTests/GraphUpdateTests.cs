using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
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
    public sealed class GraphUpdateTests
    {
        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Graph_save_orders_dependencies_propagates_keys_and_deletes_in_reverse(OceanBaseCompatibilityMode mode, bool async)
        {
            var capture = new Commands();
            await using var context = CreateContext(mode, capture);
            var parent = new Parent { Name = "parent" };
            var child = new Child { Name = "child", Parent = parent };
            var leaf = new Leaf { Id = 301, Value = "leaf", Child = child };
            context.AddRange(leaf, child, parent);
            Assert.True(context.Entry(parent).Property(p => p.Id).IsTemporary);
            Assert.True(context.Entry(child).Property(c => c.Id).IsTemporary);
            Assert.Equal(3, async ? await context.SaveChangesAsync() : context.SaveChanges());
            Assert.Equal(101, parent.Id);
            Assert.Equal(201, child.Id);
            Assert.Equal(101, child.ParentId);
            Assert.Equal(201, leaf.ChildId);
            Assert.Same(child, Assert.Single(parent.Children));
            Assert.Same(leaf, Assert.Single(child.Leaves));
            Assert.All(context.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
            Assert.False(context.Entry(parent).Property(p => p.Id).IsTemporary);
            Assert.False(context.Entry(child).Property(c => c.Id).IsTemporary);
            Assert.All(capture.Items, command =>
            {
                Assert.Same(context.Database.GetDbConnection(), command.Connection);
                Assert.Null(command.Transaction);
            });
            var writes = capture.Items.Where(command => command.Sql.StartsWith("INSERT", StringComparison.Ordinal)).ToArray();
            Assert.Equal(3, writes.Length);
            var suffix = mode == OceanBaseCompatibilityMode.Oracle ? " RETURNING Id INTO ?" : "";
            Assert.Equal("INSERT INTO Parents (Name) VALUES (?)" + suffix + ";", writes[0].Template);
            Assert.Equal(new object?[] { "parent" }, writes[0].InputsInSqlOrder());
            Assert.Equal("INSERT INTO Children (Name, ParentId) VALUES (?, ?)" + suffix + ";", writes[1].Template);
            Assert.Equal(new object?[] { "child", 101 }, writes[1].InputsInSqlOrder());
            Assert.Equal("INSERT INTO Leaves (Id, ChildId, Value) VALUES (?, ?, ?);", writes[2].Template);
            Assert.Equal(new object?[] { 301, 201, "leaf" }, writes[2].InputsInSqlOrder());
            if (mode == OceanBaseCompatibilityMode.MySql)
            {
                Assert.Equal(new[] { "INSERT", "SELECT", "INSERT", "SELECT", "INSERT" },
                    capture.Items.Select(command => command.Sql.Split(' ')[0]));
                Assert.Equal("SELECT Id FROM Parents WHERE Id = LAST_INSERT_ID()", capture.Items[1].Template);
                Assert.Equal("SELECT Id FROM Children WHERE Id = LAST_INSERT_ID()", capture.Items[3].Template);
            }
            else Assert.Equal(3, capture.Items.Count);

            capture.Items.Clear();
            context.Remove(parent);
            Assert.Equal(3, async ? await context.SaveChangesAsync() : context.SaveChanges());
            Assert.Equal(new[] { "DELETE FROM Leaves WHERE Id = ?;", "DELETE FROM Children WHERE Id = ?;", "DELETE FROM Parents WHERE Id = ?;" },
                capture.Items.Select(command => command.Template));
            Assert.Equal(new object?[] { 301, 201, 101 }, capture.Items.SelectMany(command => command.InputsInSqlOrder()));
            Assert.Empty(context.ChangeTracker.Entries());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Composite_updates_and_deletes_use_original_nullable_tokens(OceanBaseCompatibilityMode mode, bool async)
        {
            foreach (int? original in new int?[] { null, 7 })
                foreach (var delete in new[] { false, true })
                {
                    var capture = new Commands();
                    await using var context = CreateContext(mode, capture);
                    var row = new Versioned { TenantId = 2, Id = 17, Name = "before", Version = original };
                    context.Attach(row);
                    if (delete) context.Remove(row);
                    else
                    {
                        row.Name = "after";
                        row.Version = 9;
                    }
                    Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
                    var command = Assert.Single(capture.Items);
                    var predicate = "WHERE Id = ? AND TenantId = ? AND Version " + (original is null ? "IS NULL" : "= ?") + ";";
                    Assert.Equal(delete ? "DELETE FROM VersionedRows " + predicate
                        : "UPDATE VersionedRows SET Name = ?, Version = ? " + predicate, command.Template);
                    var expected = new List<object?>();
                    if (!delete) expected.AddRange(["after", 9]);
                    expected.AddRange([17, 2]);
                    if (original is not null) expected.Add(original);
                    Assert.Equal(expected, command.InputsInSqlOrder());
                    Assert.Equal(delete ? EntityState.Detached : EntityState.Unchanged, context.Entry(row).State);
                    if (!delete) Assert.Equal(9, context.Entry(row).Property(r => r.Version).OriginalValue);
                }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Current_null_write_still_binds_while_original_nonnull_token_is_a_predicate(OceanBaseCompatibilityMode mode, bool async)
        {
            var capture = new Commands();
            await using var context = CreateContext(mode, capture);
            var row = new Versioned { TenantId = 2, Id = 17, Name = "same", Version = 7 };
            context.Attach(row);
            row.Version = null;
            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            var command = Assert.Single(capture.Items);
            Assert.Equal("UPDATE VersionedRows SET Version = ? WHERE Id = ? AND TenantId = ? AND Version = ?;", command.Template);
            Assert.Equal(new object?[] { DBNull.Value, 17, 2, 7 }, command.InputsInSqlOrder());
            Assert.Null(context.Entry(row).Property(r => r.Version).OriginalValue);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Zero_row_composite_update_and_delete_report_the_conflicting_entry(OceanBaseCompatibilityMode mode, bool async)
        {
            foreach (var delete in new[] { false, true })
            {
                var capture = new Commands { AffectedRows = 0 };
                await using var context = CreateContext(mode, capture);
                var row = new Versioned { TenantId = 2, Id = 17, Name = "before", Version = null };
                context.Attach(row);
                if (delete) context.Remove(row);
                else row.Name = "after";
                var failure = async ? await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => context.SaveChangesAsync())
                    : Assert.Throws<DbUpdateConcurrencyException>(() => context.SaveChanges());
                Assert.Same(row, Assert.Single(failure.Entries).Entity);
                Assert.Null(failure.InnerException);
                Assert.Equal(delete ? EntityState.Deleted : EntityState.Modified, context.Entry(row).State);
                Assert.Single(capture.Items);
            }
        }

        private static WriteContext CreateContext(OceanBaseCompatibilityMode mode, Commands commands)
        {
            var context = new WriteContext(new DbContextOptionsBuilder<WriteContext>()
                .UseOceanBase("Server=localhost;Database=test", mode)
                .AddInterceptors(new NoNetwork(), commands).Options);
            // This fixture checks commands/dependency propagation; atomicity has a separate TXN gate.
            context.Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
            return context;
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Connector_errors_keep_the_original_exception_and_failing_entry(OceanBaseCompatibilityMode mode, bool async)
        {
            foreach (var failOnRead in mode == OceanBaseCompatibilityMode.MySql ? new[] { false, true } : new[] { false })
            {
                var code = mode == OceanBaseCompatibilityMode.MySql ? 1062 : 1;
                var error = (OceanBaseException)Activator.CreateInstance(typeof(OceanBaseException),
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null,
                    new object?[] { "Synthetic server error", code, null, false, null, null }, CultureInfo.InvariantCulture)!;
                var capture = new Commands { Failure = error, FailOnRead = failOnRead };
                await using var context = CreateContext(mode, capture);
                var parent = new Parent { Name = "parent" };
                context.Add(parent);
                var failure = async ? await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync())
                    : Assert.Throws<DbUpdateException>(() => context.SaveChanges());
                Assert.Same(error, failure.InnerException);
                Assert.Equal(code, ((OceanBaseException)failure.InnerException!).ServerErrorCode);
                Assert.Same(parent, Assert.Single(failure.Entries).Entity);
                Assert.Equal(EntityState.Added, context.Entry(parent).State);
                Assert.True(context.Entry(parent).Property(p => p.Id).IsTemporary);
                Assert.Equal(failOnRead ? 2 : 1, capture.Items.Count);
                if (failOnRead) Assert.Same(capture.Items[0].Connection, capture.Items[1].Connection);
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task In_flight_cancellation_keeps_the_original_exception_and_token(OceanBaseCompatibilityMode mode, bool async)
        {
            foreach (var failOnRead in mode == OceanBaseCompatibilityMode.MySql ? new[] { false, true } : new[] { false })
            {
                using var cancellation = new CancellationTokenSource();
                var error = new OperationCanceledException("Synthetic command cancellation", cancellation.Token);
                var capture = new Commands { Failure = error, FailOnRead = failOnRead, Cancellation = cancellation };
                await using var context = CreateContext(mode, capture);
                var parent = new Parent { Name = "parent" };
                context.Add(parent);
                var failure = async ? await Assert.ThrowsAsync<OperationCanceledException>(() => context.SaveChangesAsync(cancellation.Token))
                    : Assert.Throws<OperationCanceledException>(() => context.SaveChanges());
                Assert.Same(error, failure);
                Assert.Equal(cancellation.Token, failure.CancellationToken);
                Assert.True(cancellation.IsCancellationRequested);
                Assert.Equal(EntityState.Added, context.Entry(parent).State);
                Assert.True(context.Entry(parent).Property(p => p.Id).IsTemporary);
                Assert.Equal(failOnRead ? 2 : 1, capture.Items.Count);
                if (async)
                {
                    Assert.Equal(capture.Items.Count, capture.Tokens.Count);
                    Assert.All(capture.Tokens, token => Assert.Equal(cancellation.Token, token));
                }
                else Assert.Empty(capture.Tokens);
            }
        }

        private sealed class WriteContext(DbContextOptions<WriteContext> options) : DbContext(options)
        {
            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity<Parent>(parent =>
                {
                    parent.ToTable("Parents");
                    parent.Property(p => p.Name).HasMaxLength(80);
                });
                modelBuilder.Entity<Child>(child =>
                {
                    child.ToTable("Children");
                    child.Property(c => c.Name).HasMaxLength(80);
                    child.HasOne(c => c.Parent).WithMany(p => p.Children).HasForeignKey(c => c.ParentId);
                });
                modelBuilder.Entity<Leaf>(leaf =>
                {
                    leaf.ToTable("Leaves");
                    leaf.Property(l => l.Id).ValueGeneratedNever();
                    leaf.Property(l => l.Value).HasMaxLength(80);
                    leaf.HasOne(l => l.Child).WithMany(c => c.Leaves).HasForeignKey(l => l.ChildId);
                });
                modelBuilder.Entity<Versioned>(row =>
                {
                    row.ToTable("VersionedRows");
                    row.HasKey(r => new { r.TenantId, r.Id });
                    row.Property(r => r.Id).ValueGeneratedNever();
                    row.Property(r => r.Name).HasMaxLength(80);
                    row.Property(r => r.Version).IsConcurrencyToken();
                });
            }
        }

        private sealed class Parent
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public List<Child> Children { get; set; } = [];
        }

        private sealed class Child
        {
            public int Id { get; set; }
            public int ParentId { get; set; }
            public string Name { get; set; } = "";
            public Parent Parent { get; set; } = null!;
            public List<Leaf> Leaves { get; set; } = [];
        }

        private sealed class Leaf
        {
            public int Id { get; set; }
            public int ChildId { get; set; }
            public string Value { get; set; } = "";
            public Child Child { get; set; } = null!;
        }

        private sealed class Versioned
        {
            public int TenantId { get; set; }
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public int? Version { get; set; }
        }

        private sealed record Parameter(string Name, object? Value, ParameterDirection Direction);

        private sealed record Command(string Sql, Parameter[] Parameters, DbConnection? Connection, DbTransaction? Transaction)
        {
            private static readonly Regex Binds = new(@"[:@]\w+|\?", RegexOptions.CultureInvariant);
            public string Template => Regex.Replace(Binds.Replace(Sql.Replace("`", "", StringComparison.Ordinal)
                .Replace("\"", "", StringComparison.Ordinal).Trim(), "?"), @"\s+", " ");

            public object?[] InputsInSqlOrder()
            {
                var index = 0;
                var result = new List<object?>();
                var used = new HashSet<string>(StringComparer.Ordinal);
                foreach (Match match in Binds.Matches(Sql))
                {
                    var parameter = match.Value == "?" ? Parameters[index++]
                        : Parameters.Single(p => p.Name.TrimStart(':', '@') == match.Value.TrimStart(':', '@'));
                    used.Add(parameter.Name);
                    if (parameter.Direction == ParameterDirection.Input) result.Add(parameter.Value);
                }
                Assert.Equal(Parameters.Length, used.Count);
                return result.ToArray();
            }
        }

        private sealed class Commands : DbCommandInterceptor
        {
            public List<Command> Items { get; } = [];
            public List<CancellationToken> Tokens { get; } = [];
            public int AffectedRows { get; init; } = 1;
            public Exception? Failure { get; init; }
            public bool FailOnRead { get; init; }
            public CancellationTokenSource? Cancellation { get; init; }

            private void Capture(DbCommand command, bool read = false)
            {
                var captured = new Command(command.CommandText, command.Parameters.Cast<DbParameter>()
                    .Select(p => new Parameter(p.ParameterName, p.Value, p.Direction)).ToArray(), command.Connection, command.Transaction);
                Items.Add(captured);
                captured.InputsInSqlOrder();
                if (Failure is not null && FailOnRead == read)
                {
                    Cancellation?.Cancel();
                    throw Failure;
                }
            }

            public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
            {
                Capture(command);
                var id = command.CommandText.Contains("Parents", StringComparison.Ordinal) ? 101 : 201;
                foreach (DbParameter parameter in command.Parameters)
                    if (parameter.Direction == ParameterDirection.Output) parameter.Value = new OceanBaseDecimal(id);
                return InterceptionResult<int>.SuppressWithResult(AffectedRows);
            }

            public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
                InterceptionResult<int> result, CancellationToken cancellationToken = default)
            {
                Tokens.Add(cancellationToken);
                return ValueTask.FromResult(NonQueryExecuting(command, eventData, result));
            }

            public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData,
                InterceptionResult<DbDataReader> result)
            {
                Capture(command, read: true);
                var table = new DataTable();
                table.Columns.Add("Id", typeof(int));
                table.Rows.Add(command.CommandText.Contains("Parents", StringComparison.Ordinal) ? 101 : 201);
                return InterceptionResult<DbDataReader>.SuppressWithResult(table.CreateDataReader());
            }

            public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
                InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
            {
                Tokens.Add(cancellationToken);
                return ValueTask.FromResult(ReaderExecuting(command, eventData, result));
            }
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
