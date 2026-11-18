using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OceanBaseSamples;

namespace OceanBaseProviderValidation;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var stage = "configuration";
        OwnedTables? commands = null;
        try
        {
            var profile = MigrationProfile.Parse(args);
            Require(profile.Revision == 2, "Execute the sample with model revision 2.");
            var connection = SampleDatabase.Connection(profile.Mode, profile.NoTls);
            var history = profile.Prefix + "H";
            var journal = profile.Prefix + "J";
            var line = profile.Prefix + "L";
            var tables = new[] { history, history + "Lock", journal, line };
            Console.WriteLine($"OceanBase EF Core migration sample ({profile.Mode})");

            stage = "connect and validate object scope";
            await SampleDatabase.WithOwnedTables(connection, profile.Mode, tables, async (scope, observer) =>
            {
                commands = observer;
                await using var context = profile.Create(connection, scope, observer);
                var migrations = context.Database.GetMigrations().ToArray();
                Require(migrations.Length == 2 && migrations[0].EndsWith("_InitialJournal", StringComparison.Ordinal)
                    && migrations[1].EndsWith("_AddJournalNotes", StringComparison.Ordinal), "Generate both migrations with Run-Migrations.ps1.");

                stage = "apply initial migration and insert a row";
                var first = migrations[0];
                Require((await context.Database.GetPendingMigrationsAsync()).SequenceEqual(migrations), "Expected two pending migrations.");
                await context.Database.MigrateAsync(first);
                await using (var initial = (profile with { Revision = 1 }).Create(connection, scope, observer))
                {
                    initial.Lines.Add(new JournalLine
                    {
                        Journal = new Journal { Id = 1, Title = "kept across schema changes" },
                        Number = 1,
                        Code = "first-line"
                    });
                    await initial.SaveChangesAsync();
                }
                Passed(profile, stage);

                stage = "apply upgrade and verify existing data and new column";
                await context.Database.MigrateAsync();
                var row = await context.Entries.SingleAsync();
                Require(row.Title == "kept across schema changes" && row.Notes is null, "Upgrade must preserve the original row.");
                Require(await context.Lines.AnyAsync(entry => entry.Code == "first-line" && entry.Journal.Title == row.Title),
                    "Upgrade must preserve the related row.");
                row.Notes = "stored after upgrade";
                await context.SaveChangesAsync();
                context.ChangeTracker.Clear();
                Require(await context.Entries.AnyAsync(entry => entry.Notes == "stored after upgrade"), "New column must round-trip.");
                Passed(profile, stage);

                stage = "roll back upgrade, preserve row and reapply twice";
                await context.Database.MigrateAsync(first);
                Require((await context.Database.GetAppliedMigrationsAsync()).SequenceEqual([first]), "Rollback must remove the second history row.");
                await using (var restored = (profile with { Revision = 1 }).Create(connection, scope, observer))
                    Require(await restored.Lines.AnyAsync(entry => entry.Number == 1 && entry.Journal.Id == 1),
                        "Rollback must preserve both rows and their relationship.");
                await context.Database.MigrateAsync();
                await context.Database.MigrateAsync();
                Require((await context.Database.GetAppliedMigrationsAsync()).SequenceEqual(migrations), "Reapply must record exactly two migrations.");
                Require(await context.Entries.AnyAsync(entry => entry.Id == 1 && entry.Notes == null), "Reapply must recreate the nullable column.");
                Passed(profile, stage);

                stage = "roll back to zero";
                await context.Database.MigrateAsync("0");
                Require(!(await context.Database.GetAppliedMigrationsAsync()).Any(), "Expected an empty migration history.");
                Require(!await SampleDatabase.Exists(context, profile.Mode, journal)
                    && !await SampleDatabase.Exists(context, profile.Mode, line), "Rollback to zero must drop both application tables.");
                Require(observer.Created.SetEquals(tables), "Expected to observe the creation of every sample table.");
                Passed(profile, stage);
                stage = "cleanup";
            });
            Passed(profile, stage);
            return 0;
        }
        catch (Exception error)
        {
            var command = commands?.FailedCommand is { } commandName ? $" [{commandName}]" : "";
            Console.Error.WriteLine($"FAIL [{stage}]{command} {SampleDatabase.Describe(error)}");
            return 1;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new SampleFailure(message);
    }

    private static void Passed(MigrationProfile profile, string stage) => Console.WriteLine($"PASS {profile.Mode} {stage}");
}
