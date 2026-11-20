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
    internal static class RelationshipWriteSmoke
    {
        public static void Run(OceanBaseCompatibilityMode mode)
        {
            foreach (var async in new[] { false, true })
            {
                var commands = new Commands();
                using var context = new RelationshipContext(new DbContextOptionsBuilder<RelationshipContext>()
                    .UseOceanBase("Server=offline;Database=offline", mode)
                    .AddInterceptors(new NoNetwork(), commands).Options);
                context.Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
                var owner = new Owner { Id = 11 };
                var old = new Exclusive { Id = 41, Owner = owner, Version = 7 };
                owner.Exclusive = old;
                context.Attach(owner);
                var replacement = new Exclusive { Id = 42 };
                owner.Exclusive = replacement;
                var count = async ? context.SaveChangesAsync(false).GetAwaiter().GetResult() : context.SaveChanges(false);
                if (count != 2 || commands.Items.Count != 2 || context.Entry(old).State != EntityState.Deleted
                    || context.Entry(replacement).State != EntityState.Added || replacement.OwnerId != 11)
                    throw new Exception("Packed relationship replacement lost ordering or pending acceptance.");
                commands.AssertAt(0, "DELETE FROM PackedExclusive WHERE Id = ? AND Version = ?;", [41, 7]);
                commands.AssertAt(1, "INSERT INTO PackedExclusive (Id, OwnerId, Version) VALUES (?, ?, ?);", [42, 11, DBNull.Value]);
                context.ChangeTracker.AcceptAllChanges();
                if (context.Entry(old).State != EntityState.Detached || context.Entry(replacement).State != EntityState.Unchanged)
                    throw new Exception("Packed replacement acceptance changed.");
                var optional = new Optional { Id = 21, Owner = owner };
                context.Attach(optional);
                optional.Owner = null;
                if ((async ? context.SaveChangesAsync().GetAwaiter().GetResult() : context.SaveChanges()) != 1)
                    throw new Exception("Packed optional relationship severing changed.");
                commands.AssertAt(2, "UPDATE PackedOptional SET OwnerId = ? WHERE Id = ? AND Version IS NULL;", [DBNull.Value, 21]);
                if (context.Entry(optional).Property<int?>("OwnerId").OriginalValue is not null || owner.OptionalItems.Count != 0
                    || context.ChangeTracker.HasChanges())
                    throw new Exception("Packed shadow FK or relationship fixup was not accepted.");
            }
            Console.WriteLine($"{mode}: packed relationship replacement, explicit acceptance and optional severing passed (offline; automatic transactions disabled).");
        }

        private sealed class RelationshipContext(DbContextOptions<RelationshipContext> options) : DbContext(options)
        {
            protected override void OnModelCreating(ModelBuilder model)
            {
                model.Entity<Owner>().ToTable("PackedOwners").Property(o => o.Id).ValueGeneratedNever();
                var exclusive = model.Entity<Exclusive>();
                exclusive.ToTable("PackedExclusive");
                exclusive.Property(i => i.Id).ValueGeneratedNever();
                exclusive.Property(i => i.Version).IsConcurrencyToken();
                exclusive.HasOne(i => i.Owner).WithOne(o => o.Exclusive).HasForeignKey<Exclusive>(i => i.OwnerId);
                var optional = model.Entity<Optional>();
                optional.ToTable("PackedOptional");
                optional.Property(i => i.Id).ValueGeneratedNever();
                optional.Property(i => i.Version).IsConcurrencyToken();
                optional.HasOne(i => i.Owner).WithMany(o => o.OptionalItems).HasForeignKey("OwnerId");
            }
        }
        private sealed class Owner { public int Id { get; set; } public Exclusive? Exclusive { get; set; } public List<Optional> OptionalItems { get; set; } = []; }
        private sealed class Exclusive { public int Id { get; set; } public int OwnerId { get; set; } public Owner Owner { get; set; } = null!; public int? Version { get; set; } }
        private sealed class Optional { public int Id { get; set; } public Owner? Owner { get; set; } public int? Version { get; set; } }
        private sealed class Commands : DbCommandInterceptor
        {
            public List<(string Sql, object?[] Inputs)> Items { get; } = [];
            public void AssertAt(int index, string sql, object?[] inputs)
            {
                if (Items[index].Sql != sql || !Items[index].Inputs.SequenceEqual(inputs))
                    throw new Exception("Packed relationship DML or input bindings changed: " + Items[index].Sql);
            }
            public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData data, InterceptionResult<int> result)
            {
                var parameters = command.Parameters.Cast<DbParameter>().ToArray();
                var position = 0;
                var inputs = Regex.Matches(command.CommandText, @"[@:]\w+|\?").Select(match =>
                    (match.Value == "?" ? parameters[position++] : parameters.Single(p => p.ParameterName.TrimStart('@', ':') == match.Value.TrimStart('@', ':'))).Value).ToArray();
                var sql = Regex.Replace(Regex.Replace(command.CommandText.Replace("`", "", StringComparison.Ordinal)
                    .Replace("\"", "", StringComparison.Ordinal).Trim(), @"[@:]\w+", "?"), @"\s+", " ");
                Items.Add((sql, inputs));
                return InterceptionResult<int>.SuppressWithResult(1);
            }
            public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data,
                InterceptionResult<int> result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(NonQueryExecuting(command, data, result));
        }
        private sealed class NoNetwork : DbConnectionInterceptor
        {
            public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData data, InterceptionResult result) => InterceptionResult.Suppress();
            public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData data,
                InterceptionResult result, CancellationToken cancellationToken = default) => ValueTask.FromResult(InterceptionResult.Suppress());
        }
    }
}
