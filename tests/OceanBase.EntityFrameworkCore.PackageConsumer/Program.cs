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
                var sql = context.Set<Row>().Where(row => row.Enabled).OrderBy(row => row.Id).Skip(1).Take(2)
                    .Select(row => new { row.Name, row.Token, Positive = row.Id > 0 }).ToQueryString();
                var paging = mode == OceanBaseCompatibilityMode.MySql ? "LIMIT" : "FETCH NEXT";
                if (!sql.Contains(paging, StringComparison.Ordinal)) throw new Exception("Wrong query dialect.");
                if (mode == OceanBaseCompatibilityMode.Oracle && !sql.Contains("CASE", StringComparison.Ordinal))
                {
                    throw new Exception("Oracle Boolean projection was not converted to a scalar value.");
                }

                var guidType = context.Model.FindEntityType(typeof(Row))!.FindProperty(nameof(Row.Token))!.GetColumnType();
                if (guidType != (mode == OceanBaseCompatibilityMode.Oracle ? "RAW(16)" : "CHAR(36)"))
                {
                    throw new Exception("Wrong Guid storage type.");
                }

                Console.WriteLine($"{mode}: packed provider registration, Guid model, Boolean SQL and paging passed; EF {typeof(DbContext).Assembly.GetName().Version}.");
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
            public bool Enabled { get; set; }
            public Guid Token { get; set; }
        }
    }
}
