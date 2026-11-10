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
                using var context = new ConsumerContext(options, mode);
                if (!context.Database.IsOceanBase()) throw new Exception("Provider registration failed.");
                var sql = context.Set<Row>().Where(row => row.Enabled).OrderBy(row => row.Id).Skip(1).Take(2)
                    .Select(row => new { row.Name, row.Token, row.ExplicitToken, row.OccurredAt, row.OffsetAt, row.Day,
                        row.Clock, row.Duration, row.Payload, row.LargePayload, row.Count, Positive = row.Id > 0 }).ToQueryString();
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

                var rowType = context.Model.FindEntityType(typeof(Row))!;
                var explicitGuid = rowType.FindProperty(nameof(Row.ExplicitToken))!.GetRelationalTypeMapping();
                if (explicitGuid.StoreType != (mode == OceanBaseCompatibilityMode.Oracle ? "VARCHAR2(36)" : "BINARY(16)"))
                {
                    throw new Exception("Explicit Guid storage was not preserved by its mapping.");
                }

                var token = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
                var guidParameterSql = context.Set<Row>().Where(row => row.ExplicitToken == token).ToQueryString();
                if (!guidParameterSql.Contains(mode == OceanBaseCompatibilityMode.Oracle
                        ? "00112233-4455-6677-8899-aabbccddeeff" : "33221100554477668899AABBCCDDEEFF", StringComparison.Ordinal))
                {
                    throw new Exception("Explicit Guid query operand used the wrong representation.");
                }

                if (rowType.FindProperty(nameof(Row.Payload))!.GetColumnType()
                        != (mode == OceanBaseCompatibilityMode.Oracle ? "RAW(8)" : "VARBINARY(8)")
                    || rowType.FindProperty(nameof(Row.LargePayload))!.GetColumnType()
                        != (mode == OceanBaseCompatibilityMode.Oracle ? "BLOB" : "LONGBLOB"))
                {
                    throw new Exception("Wrong bounded/unbounded binary storage type.");
                }

                if (rowType.FindProperty(nameof(Row.Count))!.GetRelationalTypeMapping().StoreType
                        != (mode == OceanBaseCompatibilityMode.Oracle ? "NUMBER(3)" : "MEDIUMINT")
                    || rowType.FindProperty(nameof(Row.Enabled))!.GetRelationalTypeMapping().StoreType
                        != (mode == OceanBaseCompatibilityMode.Oracle ? "NUMBER(3)" : "BOOLEAN"))
                {
                    throw new Exception("Explicit integer/Boolean store types were not preserved by their mappings.");
                }

                Console.WriteLine($"{mode}: packed provider registration, scalar/temporal/binary model, Boolean SQL and paging passed; EF {typeof(DbContext).Assembly.GetName().Version}.");
            }
        }

        private sealed class ConsumerContext(DbContextOptions<ConsumerContext> options, OceanBaseCompatibilityMode mode) : DbContext(options)
        {
            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity<Row>().Property(row => row.Payload).HasMaxLength(8);
                modelBuilder.Entity<Row>().Property(row => row.Count).HasColumnType(mode == OceanBaseCompatibilityMode.Oracle ? "NUMBER(3)" : "MEDIUMINT");
                modelBuilder.Entity<Row>().Property(row => row.Enabled).HasColumnType(mode == OceanBaseCompatibilityMode.Oracle ? "NUMBER(3)" : "BOOLEAN");
                modelBuilder.Entity<Row>().Property(row => row.ExplicitToken).HasColumnType(mode == OceanBaseCompatibilityMode.Oracle ? "VARCHAR2(36)" : "BINARY(16)");
            }
        }

        private sealed class Row
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public bool Enabled { get; set; }
            public Guid Token { get; set; }
            public Guid ExplicitToken { get; set; }
            public DateTime OccurredAt { get; set; }
            public DateTimeOffset OffsetAt { get; set; }
            public DateOnly Day { get; set; }
            public TimeOnly Clock { get; set; }
            public TimeSpan Duration { get; set; }
            public byte[] Payload { get; set; } = [];
            public byte[]? LargePayload { get; set; }
            public long Count { get; set; }
        }
    }
}
