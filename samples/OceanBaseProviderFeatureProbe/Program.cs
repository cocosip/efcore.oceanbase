using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBase.EntityFrameworkCore;
using OceanBaseSamples;
using Mode = OceanBase.EntityFrameworkCore.OceanBaseCompatibilityMode;

namespace OceanBaseProviderFeatureProbe;

internal static class Program
{
    private const string Usage = "Options: --list | [--mode mysql|oracle|both] [--only text] [--dry-run] [--sync] [--no-tls]";

    private static async Task<int> Main(string[] args)
    {
        var modeFilter = "both";
        var only = "";
        var list = false;
        var dryRun = false;
        var useAsync = true;
        var noTls = false;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--list": list = true; break;
                case "--dry-run": dryRun = true; break;
                case "--sync": useAsync = false; break;
                case "--no-tls": noTls = true; break;
                case "--mode" when index + 1 < args.Length: modeFilter = args[++index].ToLowerInvariant(); break;
                case "--only" when index + 1 < args.Length: only = args[++index]; break;
                case "--help": Console.WriteLine(Usage); return 0;
                default: Console.Error.WriteLine(Usage); return 2;
            }
        }

        var modes = Enum.GetValues<Mode>().Where(mode => modeFilter == "both" || mode.ToString().Equals(modeFilter, StringComparison.OrdinalIgnoreCase)).ToArray();
        var steps = Scenarios.Steps.Where(step => step.Name.Contains(only, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (modes.Length == 0 || steps.Length == 0)
        {
            Console.Error.WriteLine("Select a valid mode and at least one matching scenario. Use --list to see scenario names.");
            return 2;
        }
        if (list)
        {
            foreach (var step in steps) Console.WriteLine(step.Name);
            return 0;
        }

        Console.WriteLine($"OceanBase EF Core feature sample ({(dryRun ? "offline DDL" : useAsync ? "live async" : "live sync")})");
        var passed = 0;
        var failed = 0;
        foreach (var mode in modes)
        foreach (var step in steps)
        {
            var stage = "connection and schema";
            try
            {
                var root = SampleDatabase.NewPrefix();
                var tableNames = Scenarios.TableSuffixes(step.Model).Select(suffix => root + suffix).ToArray();
                var connection = dryRun ? "Server=offline.invalid;Database=offline" : SampleDatabase.Connection(mode, noTls);
                var options = new DbContextOptionsBuilder<Scenarios.Context>().UseOceanBase(connection, mode);
                if (dryRun)
                {
                    stage = "generate DDL";
                    using var context = Scenarios.Create(options.Options, root, step.Model);
                    var sql = context.Database.GenerateCreateScript();
                    foreach (var name in tableNames) Assert.True(sql.Contains(name, StringComparison.Ordinal));
                }
                else
                {
                    await SampleDatabase.WithOwnedTables(connection, mode, tableNames, async (scope, observer) =>
                    {
                        options.EnableOceanBaseSchemaManagement(scope).AddInterceptors(observer);
                        await using var context = Scenarios.Create(options.Options, root, step.Model);
                        var creator = context.GetService<IRelationalDatabaseCreator>();
                        if (useAsync) await creator.CreateTablesAsync(); else creator.CreateTables();
                        Assert.True(observer.Created.SetEquals(tableNames));
                        await step.Run(context, useAsync, value => stage = value);
                        stage = "cleanup";
                    });
                }
                Console.WriteLine($"PASS {mode} {step.Name}");
                passed++;
            }
            catch (Exception error)
            {
                var description = error is CheckFailedException ? error.Message : SampleDatabase.Describe(error);
                Console.Error.WriteLine($"FAIL {mode} {step.Name} [{stage}] {description}");
                failed++;
            }
        }
        Console.WriteLine($"{passed} passed, {failed} failed");
        return failed == 0 ? 0 : 1;
    }
}
