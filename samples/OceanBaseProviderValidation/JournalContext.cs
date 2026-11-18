using System;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using OceanBase.EntityFrameworkCore;
using OceanBaseSamples;
using Mode = OceanBase.EntityFrameworkCore.OceanBaseCompatibilityMode;

namespace OceanBaseProviderValidation;

public sealed class JournalContext : DbContext
{
    private readonly MigrationProfile _profile;

    internal JournalContext(DbContextOptions<JournalContext> options, MigrationProfile profile) : base(options)
        => _profile = profile;

    public DbSet<Journal> Entries => Set<Journal>();
    public DbSet<JournalLine> Lines => Set<JournalLine>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        var journal = model.Entity<Journal>().ToTable(_profile.Prefix + "J");
        journal.Property(entry => entry.Id).ValueGeneratedNever();
        journal.Property(entry => entry.Title).HasMaxLength(60);
        if (_profile.Revision == 1) journal.Ignore(entry => entry.Notes);
        else journal.Property(entry => entry.Notes).HasMaxLength(80);

        var line = model.Entity<JournalLine>().ToTable(_profile.Prefix + "L");
        line.HasKey(entry => new { entry.JournalId, entry.Number });
        line.Property(entry => entry.Code).HasMaxLength(24);
        line.HasIndex(entry => entry.Code).IsUnique();
        line.HasOne(entry => entry.Journal).WithMany().HasForeignKey(entry => entry.JournalId);
    }
}

public sealed class Journal
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string? Notes { get; set; }
}

public sealed class JournalLine
{
    public int JournalId { get; set; }
    public int Number { get; set; }
    public string Code { get; set; } = "";
    public Journal Journal { get; set; } = null!;
}

public sealed class JournalFactory : IDesignTimeDbContextFactory<JournalContext>
{
    public JournalContext CreateDbContext(string[] args)
    {
        var profile = MigrationProfile.Parse(args);
        return profile.Create("Server=offline.invalid;Database=offline");
    }
}

internal sealed record MigrationProfile(Mode Mode, string Prefix, int Revision, bool NoTls)
{
    internal static MigrationProfile Parse(string[] args)
    {
        Mode? mode = null;
        string? prefix = null;
        var revision = 2;
        var noTls = false;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--mode" when index + 1 < args.Length:
                    var name = args[++index];
                    mode = name.Equals("mysql", StringComparison.OrdinalIgnoreCase) ? Mode.MySql
                        : name.Equals("oracle", StringComparison.OrdinalIgnoreCase) ? Mode.Oracle : null;
                    break;
                case "--prefix" when index + 1 < args.Length: prefix = args[++index]; break;
                case "--revision" when index + 1 < args.Length && int.TryParse(args[index + 1], out revision): index++; break;
                case "--no-tls": noTls = true; break;
                default: throw new SampleFailure("Invalid migration sample option. Run Run-Migrations.ps1 -Help.");
            }
        }
        if (mode is null || prefix is null || !Regex.IsMatch(prefix, "\\AEFT_[0-9A-F]{20}\\z") || revision is not (1 or 2))
            throw new SampleFailure("Provide MySql or Oracle, an EFT_ sample prefix and model revision 1 or 2. Run Run-Migrations.ps1 -Help.");
        return new(mode.Value, prefix, revision, noTls);
    }

    internal JournalContext Create(string connection, string? scope = null, OwnedTables? observer = null)
    {
        var builder = new DbContextOptionsBuilder<JournalContext>().EnableServiceProviderCaching(false)
            .UseOceanBase(connection, Mode, options => options.MigrationsHistoryTable(Prefix + "H"));
        if (scope is not null) builder.EnableOceanBaseSchemaManagement(scope);
        if (observer is not null) builder.AddInterceptors(observer);
        return new(builder.Options, this);
    }
}
