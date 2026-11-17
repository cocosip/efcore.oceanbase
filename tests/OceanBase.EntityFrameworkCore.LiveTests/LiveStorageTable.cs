using System;
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
        Func<TContext, Commands, Action<string>, Task> run) where TContext : DbContext
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
            observer.CreateSql = "CREATE TABLE " + context.GetService<ISqlGenerationHelper>().DelimitIdentifier(table);
            stage = "CreateTables/defaults/HasData";
            var creator = context.GetService<IRelationalDatabaseCreator>();
            if (async) await creator.CreateTablesAsync();
            else creator.CreateTables();
            Assert.True(observer.OwnsTable);

            await run(context, observer, value => stage = value);
        }
        catch (Exception error)
        {
            failure = SafeFailure(mode, table, stage, error, configured);
            throw failure;
        }
        finally
        {
            if (observer.OwnsTable)
            {
                try
                {
                    await using var cleanup = new DbContext(new DbContextOptionsBuilder().UseOceanBase(configured, mode).Options);
                    var identifier = cleanup.GetService<ISqlGenerationHelper>().DelimitIdentifier(table);
                    var dropSql = "DROP TABLE " + identifier;
                    if (async) await cleanup.Database.ExecuteSqlRawAsync(dropSql);
                    else cleanup.Database.ExecuteSqlRaw(dropSql);
                    var existsSql = mode == OceanBaseCompatibilityMode.MySql
                        ? "SELECT table_name AS `Value` FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = {0}"
                        : "SELECT TABLE_NAME AS \"Value\" FROM USER_TABLES WHERE TABLE_NAME = {0}";
                    var remaining = cleanup.Database.SqlQueryRaw<string>(existsSql, table);
                    Assert.Empty(async ? await remaining.ToListAsync() : remaining.ToList());
                }
                catch (Exception error)
                {
                    var cleanupFailure = SafeFailure(mode, table, "cleanup; inspect owned table", error, configured);
                    if (failure is null) throw cleanupFailure;
                    throw new AggregateException(failure, cleanupFailure);
                }
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
        public string CreateSql { get; set; } = "";
        public bool OwnsTable { get; private set; }
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
            if (command.CommandText.StartsWith(CreateSql + " (", StringComparison.Ordinal)) OwnsTable = true;
            return result;
        }
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(NonQueryExecuted(command, eventData, result));
    }
}
