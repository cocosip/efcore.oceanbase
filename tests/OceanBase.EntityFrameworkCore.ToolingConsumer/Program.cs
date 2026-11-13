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
        private static void Main() => Console.WriteLine("Offline packed-provider migrations tooling fixture.");
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
        }
    }

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
