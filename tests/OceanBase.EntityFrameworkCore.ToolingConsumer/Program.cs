using System;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OceanBase.EntityFrameworkCore;

namespace ToolingConsumer
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length > 0) ReverseEngineeringSmoke.Run(args);
                else Console.WriteLine("Offline packed-provider migrations tooling fixture.");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine($"Tooling fixture failed ({error.GetType().Name}): {error.Message}");
                return 1;
            }
        }
    }

    public abstract class ToolingContext(DbContextOptions options, bool expanded) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity("ToolingRow", row =>
            {
                row.ToTable("ToolingRows");
                row.Property<int>("Id").ValueGeneratedOnAdd();
                row.HasKey("Id");
                row.Property<string>("Name").HasMaxLength(expanded ? 64 : 32).IsRequired();
                row.Property<bool>("Active").HasConversion(new BoolToStringConverter("N", "Y")).HasDefaultValue(true);
                row.HasIndex("Name");
                if (expanded) row.Property<int?>("Revision");
                row.HasData(new { Id = 1, Name = "R&D's\\path", Active = true });
            });
            builder.Entity<ToolingAsset>().UseTptMappingStrategy().ToTable("ToolingAssets");
            builder.Entity<ToolingAsset>().Property(a => a.Name).HasMaxLength(40);
            builder.Entity<ToolingDevice>().ToTable("ToolingDevices");
            builder.Entity<ToolingSensor>().ToTable("ToolingSensors");
            if (expanded) builder.Entity<ToolingSensor>().Property<int?>("Calibration");
            var tpc = builder.Entity<ToolingConcreteRoot>();
            tpc.UseTpcMappingStrategy().ToTable("ToolingConcreteRoots");
            tpc.Property(a => a.Id).ValueGeneratedNever();
            tpc.Property(a => a.Name).HasMaxLength(40);
            builder.Entity<ToolingConcreteMiddle>();
            builder.Entity<ToolingConcreteLeaf>().ToTable("ToolingConcreteLeaves");
            builder.Entity<ToolingConcreteSibling>().ToTable("ToolingConcreteSiblings");
            if (expanded) tpc.Property<int?>("TpcRevision");
            var split = builder.Entity<ToolingSplit>();
            split.ToTable("ToolingSplitRows");
            split.Property(r => r.Name).HasMaxLength(40);
            split.Property(r => r.Text).HasMaxLength(40);
            if (expanded) split.Property<int?>("FragmentRevision");
            split.SplitToTable("ToolingSplitDetails", table =>
            {
                table.Property(r => r.Id).HasColumnName("DetailId");
                table.Property(r => r.Text);
                if (expanded) table.Property("FragmentRevision");
            });
            builder.Entity<ToolingOwner>().ToTable("ToolingSharedRows").Property(o => o.Name).HasMaxLength(40);
            builder.Entity<ToolingDetail>().ToTable("ToolingSharedRows").Property(d => d.Text).HasMaxLength(40);
            builder.Entity<ToolingOwner>().Property(o => o.Version).IsConcurrencyToken();
            builder.Entity<ToolingDetail>().Property(d => d.Version).IsConcurrencyToken();
            builder.Entity<ToolingOwner>().HasOne(o => o.Detail).WithOne().HasForeignKey<ToolingDetail>(d => d.Id);
        }
    }

    public abstract class ToolingAsset { public int Id { get; set; } public string? Name { get; set; } }
    public class ToolingDevice : ToolingAsset { public int Serial { get; set; } }
    public sealed class ToolingSensor : ToolingDevice { public int? Reading { get; set; } }
    public class ToolingConcreteRoot { public int Id { get; set; } public string? Name { get; set; } }
    public abstract class ToolingConcreteMiddle : ToolingConcreteRoot { public int Serial { get; set; } }
    public sealed class ToolingConcreteLeaf : ToolingConcreteMiddle { public int? Reading { get; set; } }
    public sealed class ToolingConcreteSibling : ToolingConcreteRoot { public int Pages { get; set; } }
    public sealed class ToolingSplit { public int Id { get; set; } public string? Name { get; set; } public string? Text { get; set; } }
    public sealed class ToolingOwner { public int Id { get; set; } public string? Name { get; set; } public int? Version { get; set; } public ToolingDetail? Detail { get; set; } }
    public sealed class ToolingDetail { public int Id { get; set; } public string? Text { get; set; } public int? Version { get; set; } }

    public sealed class MySqlContext(DbContextOptions<MySqlContext> options, bool expanded) : ToolingContext(options, expanded);
    public sealed class OracleContext(DbContextOptions<OracleContext> options, bool expanded) : ToolingContext(options, expanded);

    public sealed class MySqlFactory : IDesignTimeDbContextFactory<MySqlContext>
    {
        public MySqlContext CreateDbContext(string[] args)
            => new(ToolingOptions.Create<MySqlContext>(OceanBaseCompatibilityMode.MySql), args.Contains("--expanded"));
    }

    public sealed class OracleFactory : IDesignTimeDbContextFactory<OracleContext>
    {
        public OracleContext CreateDbContext(string[] args)
            => new(ToolingOptions.Create<OracleContext>(OceanBaseCompatibilityMode.Oracle), args.Contains("--expanded"));
    }

    internal static class ToolingOptions
    {
        internal static DbContextOptions<T> Create<T>(OceanBaseCompatibilityMode mode) where T : DbContext
            => new DbContextOptionsBuilder<T>()
                .UseOceanBase("Server=offline.invalid;Database=tooling", mode, options => options
                    .MigrationsAssembly(typeof(T).Assembly.GetName().Name)
                    .MigrationsHistoryTable("__ToolingHistory"))
                .EnableOceanBaseSchemaManagement("tooling")
                .AddInterceptors(new NoNetwork()).Options;

        private sealed class NoNetwork : DbConnectionInterceptor
        {
            public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData,
                InterceptionResult result) => throw new InvalidOperationException("Offline tooling cannot open a database.");
            public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
                InterceptionResult result, CancellationToken cancellationToken = default)
                => throw new InvalidOperationException("Offline tooling cannot open a database.");
        }
    }
}
