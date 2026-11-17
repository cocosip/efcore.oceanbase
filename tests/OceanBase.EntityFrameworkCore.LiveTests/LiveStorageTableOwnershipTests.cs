using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.LiveTests
{
    public sealed class LiveStorageTableOwnershipTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Only_successfully_completed_exact_expected_CREATE_commands_confer_ownership(bool async)
        {
            var observer = new LiveStorageTable.Commands
            {
                CreateStatements = new() { ["EFT_testP"] = "CREATE TABLE \"EFT_testP\"", ["EFT_testC"] = "CREATE TABLE \"EFT_testC\"" }
            };
            using var parent = new OceanBaseCommand { CommandText = "CREATE TABLE \"EFT_testP\" (\"Id\" NUMBER)" };
            Assert.Empty(observer.CreatedTables);
            using var unrelated = new OceanBaseCommand { CommandText = "CREATE TABLE \"EFT_other\" (\"Id\" NUMBER)" };
            await Complete(unrelated);
            using var prefix = new OceanBaseCommand { CommandText = "CREATE TABLE \"EFT_testP_more\" (\"Id\" NUMBER)" };
            await Complete(prefix);
            Assert.Empty(observer.CreatedTables);
            await Complete(parent);
            using var child = new OceanBaseCommand { CommandText = "CREATE TABLE \"EFT_testC\" (\"Id\" NUMBER)" };
            await Complete(child);
            await Complete(parent);
            Assert.Equal(new[] { "EFT_testP", "EFT_testC" }, observer.CreatedTables);

            async Task Complete(DbCommand command)
            {
                if (async) await observer.NonQueryExecutedAsync(command, null!, 0);
                else observer.NonQueryExecuted(command, null!, 0);
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Partial_CREATE_failure_cleans_confirmed_tables_in_dependency_order(OceanBaseCompatibilityMode mode, bool async)
        {
            var failure = new FailThirdCreate();
            DbContextOptions<Context>? options = null;
            string? root = null;
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => LiveStorageTable.WithTable<Context>(mode, async, false,
                (input, table) =>
                {
                    root = table;
                    options = new DbContextOptionsBuilder<Context>(input).EnableServiceProviderCaching(false)
                        .ReplaceService<IModelCacheKeyFactory, CacheKey>().AddInterceptors(failure).Options;
                    return new Context(options, table);
                }, (_, _, _) => throw new InvalidOperationException("The incomplete schema must not reach the test body."), ["A", "B", "C"]));
            Assert.Contains("Synthetic third CREATE failure", error.Message);
            Assert.Equal(new[] { root + "A", root + "B" }, failure.Created);
            await using var check = new Context(options!, root!);
            if (async) await check.Database.OpenConnectionAsync(); else check.Database.OpenConnection();
            foreach (var name in new[] { root + "A", root + "B", root + "C" })
            {
                var sql = mode == OceanBaseCompatibilityMode.MySql
                    ? "SELECT table_name AS `Value` FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = {0}"
                    : "SELECT TABLE_NAME AS \"Value\" FROM USER_TABLES WHERE TABLE_NAME = {0}";
                var query = check.Database.SqlQueryRaw<string>(sql, name);
                Assert.Empty(async ? await query.ToArrayAsync() : query.ToArray());
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public Task Cleanup_uses_declared_dependencies_when_CREATE_order_is_reversed(OceanBaseCompatibilityMode mode, bool async)
            => LiveStorageTable.WithTable<Context>(mode, async, false,
                (input, table) => new Context(new DbContextOptionsBuilder<Context>(input).EnableServiceProviderCaching(false)
                    .ReplaceService<IModelCacheKeyFactory, CacheKey>().Options, table, reverseNames: true),
                (context, commands, _) =>
                {
                    Assert.Equal(new[] { context.Root + "A", context.Root + "B", context.Root + "C" }, commands.CreatedTables);
                    return Task.CompletedTask;
                }, ["C", "B", "A"]);

        private sealed class Context(DbContextOptions<Context> options, string root, bool reverseNames = false) : DbContext(options)
        {
            public string Root => root;
            public bool ReverseNames => reverseNames;
            protected override void OnModelCreating(ModelBuilder model)
            {
                model.Entity<Parent>().ToTable(root + (reverseNames ? "C" : "A")).Property(row => row.Id).ValueGeneratedNever();
                model.Entity<Child>().ToTable(root + "B").Property(row => row.Id).ValueGeneratedNever();
                model.Entity<Child>().HasOne<Parent>().WithMany().HasForeignKey(row => row.ParentId).OnDelete(DeleteBehavior.NoAction);
                model.Entity<Last>().ToTable(root + (reverseNames ? "A" : "C")).Property(row => row.Id).ValueGeneratedNever();
                model.Entity<Last>().HasOne<Child>().WithMany().HasForeignKey(row => row.ChildId).OnDelete(DeleteBehavior.NoAction);
            }
        }
        private sealed class Parent { public int Id { get; set; } }
        private sealed class Child { public int Id { get; set; } public int ParentId { get; set; } }
        private sealed class Last { public int Id { get; set; } public int ChildId { get; set; } }
        private sealed class CacheKey : IModelCacheKeyFactory
        {
            public object Create(DbContext context, bool designTime) => (context.GetType(), ((Context)context).Root, ((Context)context).ReverseNames, designTime);
        }
        private sealed class FailThirdCreate : DbCommandInterceptor
        {
            public List<string> Created { get; } = [];
            public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData data, InterceptionResult<int> result)
            {
                if (command.CommandText.StartsWith("CREATE TABLE ", StringComparison.Ordinal) && Created.Count == 2)
                    throw new InvalidOperationException("Synthetic third CREATE failure");
                return result;
            }
            public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data,
                InterceptionResult<int> result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(NonQueryExecuting(command, data, result));
            public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData data, int result)
            {
                if (command.CommandText.StartsWith("CREATE TABLE ", StringComparison.Ordinal))
                    Created.Add(command.CommandText.Split(' ')[2].Trim('`', '"'));
                return result;
            }
            public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData data, int result,
                CancellationToken cancellationToken = default) => ValueTask.FromResult(NonQueryExecuted(command, data, result));
        }
    }
}
