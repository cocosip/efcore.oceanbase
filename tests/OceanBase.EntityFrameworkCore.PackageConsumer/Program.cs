using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using OceanBase.EntityFrameworkCore;

namespace PackageConsumer
{
    internal static class Program
    {
        private static void Main()
        {
            foreach (var mode in new[] { OceanBaseCompatibilityMode.MySql, OceanBaseCompatibilityMode.Oracle })
            {
                var options = new DbContextOptionsBuilder<ConsumerContext>()
                    .UseOceanBase("Server=localhost;Database=package_smoke", mode).Options;
                using var context = new ConsumerContext(options);
                if (!context.Database.IsOceanBase()) throw new Exception("Provider registration failed.");
                var sql = context.Set<Row>().OrderBy(row => row.Id).Skip(1).Take(2).Select(row => row.Name).ToQueryString();
                var paging = mode == OceanBaseCompatibilityMode.MySql ? "LIMIT" : "FETCH NEXT";
                if (!sql.Contains(paging, StringComparison.Ordinal)) throw new Exception("Wrong query dialect.");
                Console.WriteLine(mode + ": packed provider registration, model and paging passed.");
            }
        }

        private sealed class ConsumerContext(DbContextOptions<ConsumerContext> options) : DbContext(options)
        {
            protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Row>();
        }

        private sealed class Row
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
        }
    }
}
