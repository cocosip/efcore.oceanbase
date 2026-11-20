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
using OceanBase.EntityFrameworkCore;
using OceanBaseConnector;
using Mode = OceanBase.EntityFrameworkCore.OceanBaseCompatibilityMode;

namespace OceanBaseSamples;

internal static class SampleDatabase
{
    internal static string Connection(Mode mode, bool noTls)
    {
        var key = $"ConnectionStrings:OceanBase{mode}";
        var configured = new ConfigurationBuilder().AddUserSecrets<OwnedTables>(optional: true).Build()[key];
        if (string.IsNullOrWhiteSpace(configured)) throw new SampleFailure($"Missing User Secret '{key}'.");
        var connection = new OceanBaseConnectionStringBuilder(configured);
        if (noTls) connection.TlsMode = OceanBaseTlsMode.Disabled;
        if (mode == Mode.Oracle) connection.UseOraclePrepareExecute = true;
        return connection.ConnectionString;
    }

    internal static string NewPrefix() => "EFT_" + Guid.NewGuid().ToString("N")[..20].ToUpperInvariant();

    internal static string Describe(Exception error)
    {
        if (error is SampleFailure) return error.Message;
        if (error is AggregateException multiple) return string.Join(" | ", multiple.InnerExceptions.Select(Describe));
        var details = new List<string>();
        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            details.Add(current switch
            {
                OceanBaseException native => $"{native.GetType().Name} (server code {native.ServerErrorCode})",
                _ when current.Message.Contains("does not advertise TLS", StringComparison.OrdinalIgnoreCase) => "server does not advertise TLS",
                _ => current.GetType().Name
            });
        }
        return string.Join(" / ", details);
    }

    internal static async Task WithOwnedTables(string connection, Mode mode, string[] names,
        Func<string, OwnedTables, Task> run)
    {
        await using var catalog = new DbContext(new DbContextOptionsBuilder().UseOceanBase(connection, mode).Options);
        var helper = catalog.GetService<ISqlGenerationHelper>();
        var observer = new OwnedTables(names, helper);
        var scopeSql = mode == Mode.MySql ? "SELECT DATABASE() AS `Value`"
            : "SELECT SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA') AS \"Value\" FROM DUAL";
        var scope = (await catalog.Database.SqlQueryRaw<string>(scopeSql).ToListAsync()).Single();
        if (string.IsNullOrWhiteSpace(scope)) throw new SampleFailure("Select a database or schema before running the sample.");
        foreach (var name in names)
            if (await Exists(catalog, mode, name)) throw new SampleFailure($"Sample object {name} already exists. Use a fresh prefix.");

        Exception? primaryFailure = null;
        try
        {
            await run(scope, observer);
        }
        catch (Exception error)
        {
            primaryFailure = error;
            throw;
        }
        finally
        {
            var cleanupFailures = new List<Exception>();
            foreach (var name in Enumerable.Reverse(names).Where(observer.Created.Contains))
            {
                try
                {
                    var drop = "DROP TABLE " + helper.DelimitIdentifier(name);
                    if (await Exists(catalog, mode, name))
                        await catalog.Database.ExecuteSqlRawAsync(drop);
                    if (await Exists(catalog, mode, name)) throw new SampleFailure("Table still exists after cleanup.");
                }
                catch (Exception error)
                {
                    cleanupFailures.Add(new SampleFailure($"Cleanup failed for {name}: {Describe(error)}"));
                }
            }
            if (cleanupFailures.Count != 0)
            {
                if (primaryFailure is not null) cleanupFailures.Insert(0, primaryFailure);
                throw new AggregateException(cleanupFailures);
            }
        }
    }

    internal static async Task<bool> Exists(DbContext context, Mode mode, string name)
    {
        var sql = mode == Mode.MySql
            ? "SELECT table_name AS `Value` FROM information_schema.tables WHERE table_schema = DATABASE() AND LOWER(table_name) = LOWER({0})"
            : "SELECT TABLE_NAME AS \"Value\" FROM ALL_TABLES WHERE OWNER = SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA') AND TABLE_NAME = {0}";
        return (await context.Database.SqlQueryRaw<string>(sql, name).ToListAsync()).Count != 0;
    }
}

internal sealed class SampleFailure(string message) : Exception(message);

internal sealed class OwnedTables(string[] names, ISqlGenerationHelper helper) : DbCommandInterceptor
{
    private readonly Dictionary<string, string> _identifiers = names.ToDictionary(name => name, helper.DelimitIdentifier);
    internal HashSet<string> Created { get; } = new(StringComparer.Ordinal);
    internal string? FailedCommand { get; private set; }

    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        var sql = command.CommandText.TrimStart();
        foreach (var (name, identifier) in _identifiers)
            if (Creates(sql, identifier))
                Created.Add(name);
        return result;
    }

    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData)
    {
        var sql = command.CommandText.TrimStart();
        foreach (var (name, identifier) in _identifiers)
        {
            if (Creates(sql, identifier)) { FailedCommand = "create " + name; return; }
            if (sql.StartsWith("ALTER TABLE " + identifier + " ", StringComparison.Ordinal)) { FailedCommand = "alter " + name; return; }
        }
        FailedCommand = eventData.CommandSource.ToString();
    }

    public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        CommandFailed(command, eventData);
        return Task.CompletedTask;
    }

    private static bool Creates(string sql, string identifier)
        => sql.StartsWith("CREATE TABLE " + identifier + " (", StringComparison.Ordinal)
            || sql.StartsWith("CREATE TABLE IF NOT EXISTS " + identifier + " (", StringComparison.Ordinal)
            || (sql.StartsWith("DECLARE ob_count NUMBER;", StringComparison.Ordinal)
                && sql.Contains("EXECUTE IMMEDIATE 'CREATE TABLE " + identifier + " (", StringComparison.Ordinal));

    public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
        int result, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(NonQueryExecuted(command, eventData, result));
}
