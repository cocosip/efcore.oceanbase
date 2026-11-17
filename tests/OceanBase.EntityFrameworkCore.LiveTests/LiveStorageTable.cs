using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.LiveTests;

internal static class LiveStorageTable
{
    internal static async Task WithTable<TContext>(OceanBaseCompatibilityMode mode, bool async, bool noBackslashEscapes,
        Func<DbContextOptions<TContext>, string, TContext> createContext,
        Func<TContext, Commands, Action<string>, Task> run, string[]? tableSuffixes = null) where TContext : DbContext
    {
        if (Environment.GetEnvironmentVariable("OCEANBASE_RUN_LIVE_TESTS") != "1"
            || Environment.GetEnvironmentVariable("OCEANBASE_RUN_LIVE_SCHEMA_TESTS") != "1")
            throw new InvalidOperationException("Set OCEANBASE_RUN_LIVE_TESTS=1 and OCEANBASE_RUN_LIVE_SCHEMA_TESTS=1 to create owned live test tables.");
        var key = mode == OceanBaseCompatibilityMode.MySql ? "ConnectionStrings:OceanBaseMySql" : "ConnectionStrings:OceanBaseOracle";
        var configured = new ConfigurationBuilder().AddUserSecrets<LiveConnectionTests>(optional: false).Build()[key];
        if (string.IsNullOrWhiteSpace(configured)) throw new InvalidOperationException($"Missing User Secret '{key}'.");
        // Connector's public Oracle RETURNING path requires this explicit write-profile opt-in.
        if (mode == OceanBaseCompatibilityMode.Oracle)
            configured = new OceanBaseConnectionStringBuilder(configured) { UseOraclePrepareExecute = true }.ConnectionString;
        var table = "EFT_" + Guid.NewGuid().ToString("N")[..24];
        var tables = (tableSuffixes ?? [""]).Select(suffix => table + suffix).ToArray();
        var observer = new Commands();
        var stage = "scope";
        Exception? failure = null;
        try
        {
            await using var probe = new DbContext(new DbContextOptionsBuilder().UseOceanBase(configured, mode).Options);
            var scopeSql = mode == OceanBaseCompatibilityMode.MySql
                ? "SELECT DATABASE() AS `Value`"
                : "SELECT SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA') AS \"Value\" FROM DUAL";
            var scopeQuery = probe.Database.SqlQueryRaw<string>(scopeSql);
            var scope = Assert.Single(async ? await scopeQuery.ToListAsync() : scopeQuery.ToList());
            Assert.False(string.IsNullOrWhiteSpace(scope));
            await using var context = createContext(new DbContextOptionsBuilder<TContext>()
                .UseOceanBase(configured, mode).EnableOceanBaseSchemaManagement(scope)
                .AddInterceptors(observer).Options, table);
            if (noBackslashEscapes)
            {
                if (async) await context.Database.OpenConnectionAsync();
                else context.Database.OpenConnection();
                const string sqlMode = "SET SESSION sql_mode = CONCAT(@@SESSION.sql_mode, ',NO_BACKSLASH_ESCAPES')";
                if (async) await context.Database.ExecuteSqlRawAsync(sqlMode);
                else context.Database.ExecuteSqlRaw(sqlMode);
            }
            var sqlHelper = context.GetService<ISqlGenerationHelper>();
            observer.CreateStatements = tables.ToDictionary(name => name,
                name => "CREATE TABLE " + sqlHelper.DelimitIdentifier(name), StringComparer.Ordinal);
            stage = "CreateTables/defaults/HasData";
            var creator = context.GetService<IRelationalDatabaseCreator>();
            if (async) await creator.CreateTablesAsync();
            else creator.CreateTables();
            Assert.Equal(observer.CreateStatements.Count, observer.CreatedTables.Count);

            await run(context, observer, value => stage = value);
        }
        catch (Exception error)
        {
            failure = SafeFailure(mode, table, stage, error, configured);
            throw failure;
        }
        finally
        {
            var cleanupFailures = new List<Exception>();
            // DDL creates alphabetically, then adds FKs; callers declare principals before dependents.
            foreach (var createdTable in tables.Reverse().Where(observer.CreatedTables.Contains))
            {
                try
                {
                    await using var cleanup = new DbContext(new DbContextOptionsBuilder().UseOceanBase(configured, mode).Options);
                    if (async) await cleanup.Database.OpenConnectionAsync();
                    else cleanup.Database.OpenConnection();
                    var identifier = cleanup.GetService<ISqlGenerationHelper>().DelimitIdentifier(createdTable);
                    var dropSql = "DROP TABLE " + identifier;
                    if (async) await cleanup.Database.ExecuteSqlRawAsync(dropSql);
                    else cleanup.Database.ExecuteSqlRaw(dropSql);
                    var existsSql = mode == OceanBaseCompatibilityMode.MySql
                        ? "SELECT table_name AS `Value` FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = {0}"
                        : "SELECT TABLE_NAME AS \"Value\" FROM USER_TABLES WHERE TABLE_NAME = {0}";
                    var remaining = cleanup.Database.SqlQueryRaw<string>(existsSql, createdTable);
                    Assert.Empty(async ? await remaining.ToListAsync() : remaining.ToList());
                }
                catch (Exception error)
                {
                    cleanupFailures.Add(SafeFailure(mode, createdTable, "cleanup; inspect owned table", error, configured));
                }
            }
            if (cleanupFailures.Count != 0)
            {
                if (failure is not null) cleanupFailures.Insert(0, failure);
                throw new AggregateException(cleanupFailures);
            }
        }
    }

    private static Exception SafeFailure(OceanBaseCompatibilityMode mode, string table, string stage, Exception error, string configured)
    {
        if (error is Xunit.Sdk.XunitException) return error;
        // Do not attach Connector exceptions: authentication diagnostics can contain connection details.
        var types = error.GetType().Name;
        var code = 0;
        var detail = "";
        for (var nested = error; nested is not null; nested = nested.InnerException)
        {
            if (nested is OceanBaseException native) code = native.ServerErrorCode;
            if (nested != error) types += "/" + nested.GetType().Name;
            detail = nested.Message + "\n" + nested.StackTrace;
        }
        var settings = new OceanBaseConnectionStringBuilder(configured);
        foreach (var value in new[] { configured, settings.Password, settings.UserId, settings.Server, settings.Database })
            if (!string.IsNullOrEmpty(value)) detail = detail.Replace(value, "[redacted]", StringComparison.OrdinalIgnoreCase);
        return new InvalidOperationException($"{mode}: table={table}; stage={stage}; error={types}; server-code={code}; detail={detail}");
    }

    internal sealed class Commands : DbCommandInterceptor
    {
        public Dictionary<string, string> CreateStatements { get; set; } = new(StringComparer.Ordinal);
        public List<string> CreatedTables { get; } = [];
        public int ParameterWrites { get; private set; }

        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            if (eventData.CommandSource == CommandSource.SaveChanges && command.Parameters.Count > 0) ParameterWrites++;
            return result;
        }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(NonQueryExecuting(command, eventData, result));
        public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
        {
            foreach (var (table, sql) in CreateStatements)
                if (command.CommandText.StartsWith(sql + " (", StringComparison.Ordinal) && !CreatedTables.Contains(table))
                    CreatedTables.Add(table);
            return result;
        }
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(NonQueryExecuted(command, eventData, result));
    }
}
