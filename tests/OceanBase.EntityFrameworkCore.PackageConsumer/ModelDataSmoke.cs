using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBase.EntityFrameworkCore;

namespace PackageConsumer;

internal static class ModelDataSmoke
{
    public static void Run(OceanBaseCompatibilityMode mode)
    {
        foreach (var async in new[] { false, true }) VerifyAsync(mode, async).GetAwaiter().GetResult();
        Console.WriteLine($"{mode}: packed HasData create scripts and transactional schema bootstrap passed (offline).");
    }
    private static async Task VerifyAsync(OceanBaseCompatibilityMode mode, bool async)
    {
        var capture = new Capture();
        using var context = new Context(new DbContextOptionsBuilder<Context>().UseOceanBase("Server=offline;Database=owned", mode)
            .EnableOceanBaseSchemaManagement("owned").EnableServiceProviderCaching(false)
            .ReplaceService<IRelationalConnection, RecoverySmoke.Connection>().AddInterceptors(capture).Options);
        var script = context.Database.GenerateCreateScript();
        if (script.IndexOf("INSERT", StringComparison.Ordinal) < script.LastIndexOf("CREATE", StringComparison.Ordinal)
            || !script.Contains("'can''t'", StringComparison.Ordinal) || capture.Sql.Count != 0)
            throw new Exception("Packed HasData script lost ordering, escaping or offline generation.");
        var created = async ? await context.Database.EnsureCreatedAsync() : context.Database.EnsureCreated();
        if (!created || capture.Sql.Count(sql => sql.StartsWith("INSERT", StringComparison.Ordinal)) != 2
            || capture.Commits != 1 || capture.Transactions != 1 || context.Database.CurrentTransaction is not null
            || context.Database.GetDbConnection().State != ConnectionState.Closed)
            throw new Exception("Packed HasData initialization did not own and commit its data transaction.");
        if (async ? await context.Database.EnsureCreatedAsync() : context.Database.EnsureCreated())
            throw new Exception("Packed HasData repeated schema creation.");
        if (capture.Sql.Count(sql => sql.StartsWith("INSERT", StringComparison.Ordinal)) != 2 || capture.Transactions != 1)
            throw new Exception("Packed HasData repeated inserts in a populated schema.");
    }
    private sealed class Context(DbContextOptions<Context> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<Principal>().ToTable("SeedZPrincipals").Property(p => p.Name).HasMaxLength(32);
            model.Entity<Principal>().HasData(new Principal { Id = 7, Name = "can't" });
            model.Entity<Dependent>().ToTable("SeedADependents").HasOne<Principal>().WithMany().HasForeignKey(p => p.PrincipalId);
            model.Entity<Dependent>().HasData(new Dependent { Id = 11, PrincipalId = 7 });
        }
    }
    private sealed class Principal { public int Id { get; set; } public string Name { get; set; } = ""; }
    private sealed class Dependent { public int Id { get; set; } public int PrincipalId { get; set; } }
    private sealed class Capture : DbCommandInterceptor, IDbTransactionInterceptor
    {
        public List<string> Sql { get; } = [];
        public int Commits { get; set; }
        public int Transactions { get; private set; }
        private bool _populated;
        public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData data, InterceptionResult<object> result)
        {
            Sql.Add(command.CommandText);
            return InterceptionResult<object>.SuppressWithResult(command.CommandText.StartsWith("SELECT COUNT", StringComparison.Ordinal) ? _populated ? 1 : 0 : "owned");
        }
        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<object> result, CancellationToken token = default) => ValueTask.FromResult(ScalarExecuting(command, data, result));
        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData data, InterceptionResult<int> result)
        {
            var sql = command.CommandText.Replace("`", "", StringComparison.Ordinal).Replace("\"", "", StringComparison.Ordinal);
            if (sql.StartsWith("INSERT", StringComparison.Ordinal))
            {
                var index = Sql.Count(item => item.StartsWith("INSERT", StringComparison.Ordinal));
                if (command.Transaction is null || !sql.StartsWith(index == 0 ? "INSERT INTO SeedZPrincipals" : "INSERT INTO SeedADependents", StringComparison.Ordinal))
                    throw new Exception("Packed model data lost its transaction or dependency order.");
            }
            else if (command.Transaction is not null) throw new Exception("Packed schema DDL entered a data transaction.");
            Sql.Add(sql);
            _populated = true;
            return InterceptionResult<int>.SuppressWithResult(1);
        }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<int> result, CancellationToken token = default) => ValueTask.FromResult(NonQueryExecuting(command, data, result));
        public InterceptionResult<DbTransaction> TransactionStarting(DbConnection connection, TransactionStartingEventData data, InterceptionResult<DbTransaction> result)
        { Transactions++; return InterceptionResult<DbTransaction>.SuppressWithResult(new Transaction(connection, this)); }
        public ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection, TransactionStartingEventData data,
            InterceptionResult<DbTransaction> result, CancellationToken token = default) => ValueTask.FromResult(TransactionStarting(connection, data, result));
    }
    private sealed class Transaction(DbConnection connection, Capture capture) : DbTransaction
    {
        protected override DbConnection DbConnection => connection;
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        public override void Commit() => capture.Commits++;
        public override void Rollback() => throw new Exception("Unexpected bootstrap rollback.");
    }
}
