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
                    .Select(row => new { row.Name, row.Token, row.OccurredAt, row.OffsetAt, row.Day,
                        row.Clock, row.Duration, Positive = row.Id > 0 }).ToQueryString();
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

                var temporalTypes = mode == OceanBaseCompatibilityMode.Oracle
                    ? new[] { "TIMESTAMP(7)", "TIMESTAMP(7) WITH TIME ZONE", "DATE",
                        "INTERVAL DAY(1) TO SECOND(7)", "INTERVAL DAY(9) TO SECOND(7)" }
                    : new[] { "DATETIME(6)", "DATETIME(6)", "DATE", "TIME(6)", "TIME(6)" };
                var temporalProperties = new[] { nameof(Row.OccurredAt), nameof(Row.OffsetAt), nameof(Row.Day),
                    nameof(Row.Clock), nameof(Row.Duration) };
                for (var index = 0; index < temporalProperties.Length; index++)
                {
                    if (context.Model.FindEntityType(typeof(Row))!.FindProperty(temporalProperties[index])!.GetColumnType()
                        != temporalTypes[index])
                    {
                        throw new Exception($"Wrong temporal storage type for {temporalProperties[index]}.");
                    }
                }

                Console.WriteLine($"{mode}: packed provider registration, Guid/temporal model, Boolean SQL and paging passed; EF {typeof(DbContext).Assembly.GetName().Version}.");
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
            public DateTime OccurredAt { get; set; }
            public DateTimeOffset OffsetAt { get; set; }
            public DateOnly Day { get; set; }
            public TimeOnly Clock { get; set; }
            public TimeSpan Duration { get; set; }
        }
    }
}
