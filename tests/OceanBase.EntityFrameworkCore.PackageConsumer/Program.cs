using System;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBase.EntityFrameworkCore;
using OceanBaseCommand = OceanBaseConnector.OceanBaseCommand;
using OceanBaseParameter = OceanBaseConnector.OceanBaseParameter;

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
                        row.Clock, row.Duration, row.Payload, row.LargePayload, row.Count, row.Amplitude, row.Wide,
                        Positive = row.Id > 0 }).ToQueryString();
                var paging = mode == OceanBaseCompatibilityMode.MySql ? "LIMIT" : "FETCH NEXT";
                if (!sql.Contains(paging, StringComparison.Ordinal)) throw new Exception("Wrong query dialect.");
                if (mode == OceanBaseCompatibilityMode.Oracle && !sql.Contains("CASE", StringComparison.Ordinal))
                {
                    throw new Exception("Oracle Boolean projection was not converted to a scalar value.");
                }

                var suffix = "包'_%\\";
                var concatSql = context.Set<Row>().Select(row => string.Concat(row.Name, suffix)).ToQueryString();
                if (!concatSql.Contains(mode == OceanBaseCompatibilityMode.MySql ? "CONCAT(" : " || ", StringComparison.Ordinal)
                    || concatSql.Contains(" + ", StringComparison.Ordinal))
                {
                    throw new Exception("Packed provider used the wrong string concatenation syntax.");
                }

                if (mode == OceanBaseCompatibilityMode.Oracle)
                {
                    string? fallback = "present";
                    var nullQuery = context.Set<Row>().Where(row => (row.Name + suffix ?? fallback) == null);
                    if (!nullQuery.ToQueryString().Contains("WHERE 0 = 1", StringComparison.Ordinal))
                    {
                        throw new Exception("A nonempty Oracle coalesce fallback was treated as nullable.");
                    }
                    foreach (var value in new string?[] { "", null, "again", "" })
                    {
                        fallback = value;
                        var nullSql = nullQuery.ToQueryString();
                        if (string.IsNullOrEmpty(value)
                            ? !nullSql.Contains(" || ", StringComparison.Ordinal)
                                || !nullSql.Contains("IS NULL", StringComparison.Ordinal)
                                || nullSql.Contains("WHERE 0 = 1", StringComparison.Ordinal)
                            : !nullSql.Contains("WHERE 0 = 1", StringComparison.Ordinal))
                        {
                            throw new Exception("Oracle empty/null parameter processing reused stale command SQL.");
                        }
                    }
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

                if (rowType.FindProperty(nameof(Row.Amplitude))!.GetRelationalTypeMapping().StoreType
                        != (mode == OceanBaseCompatibilityMode.Oracle ? "BINARY_FLOAT" : "FLOAT(24)")
                    || rowType.FindProperty(nameof(Row.Wide))!.GetRelationalTypeMapping().StoreType
                        != (mode == OceanBaseCompatibilityMode.Oracle ? "BINARY_DOUBLE" : "DOUBLE PRECISION"))
                {
                    throw new Exception("Explicit floating-point store types were not preserved by their mappings.");
                }

                var floatingSql = context.Set<Row>().Select(row => row.Amplitude + 0.1d).ToQueryString();
                if (!floatingSql.Contains(mode == OceanBaseCompatibilityMode.Oracle ? "0.1D" : "0.1E0", StringComparison.Ordinal))
                {
                    throw new Exception("Floating-point literal used the wrong representation.");
                }

                var temporalBoundary = new DateTime(2026, 11, 10, 12, 0, 0).AddTicks(1234560);
                var durationBoundary = TimeSpan.FromDays(mode == OceanBaseCompatibilityMode.Oracle ? 100 : 1)
                    .Add(TimeSpan.FromTicks(1234560));
                var temporalSql = context.Set<Row>().Where(row => row.NarrowStamp < temporalBoundary
                    && row.NarrowDuration < durationBoundary).Select(row => row.Id).ToQueryString();
                if (!temporalSql.Contains(".123456", StringComparison.Ordinal))
                {
                    throw new Exception("Temporal query comparands lost fractional precision.");
                }

                var amountMapping = rowType.FindProperty(nameof(Row.Amount))!.GetRelationalTypeMapping();
                using var command = new OceanBaseCommand();
                var amountParameter = (OceanBaseParameter)amountMapping.CreateParameter(command, "boundary", decimal.MaxValue);
                if (amountMapping.Precision != 5 || amountMapping.Scale != 2
                    || amountParameter.Precision != 0 || amountParameter.DecimalScale is not null
                    || !Equals(amountParameter.Value, decimal.MaxValue))
                {
                    throw new Exception("Decimal query transport inherited physical column precision.");
                }

                var captionMapping = rowType.FindProperty(nameof(Row.Name))!.GetRelationalTypeMapping();
                var captionParameter = (OceanBaseParameter)captionMapping.CreateParameter(command, "caption", "海洋🌊数据库");
                if (captionParameter.Size != "海洋🌊数据库".Length || captionMapping.Size != 4
                    || captionParameter.OceanBaseDbType != (mode == OceanBaseCompatibilityMode.Oracle
                        ? OceanBaseConnector.OceanBaseDbType.NVarchar2 : (OceanBaseConnector.OceanBaseDbType?)null))
                {
                    throw new Exception("National text query binding lost its type or transport length.");
                }

                var source = context.GetService<IRelationalTypeMappingSource>();
                var integerMapping = source.FindMapping(typeof(int))!;
                foreach (var fraction in new object[] { 1.5f, 1.5d })
                {
                    var fractionalReadRejected = false;
                    var read = integerMapping.CustomizeDataReaderExpression(Expression.Constant(fraction, typeof(object)));
                    try { Expression.Lambda<Func<int>>(read).Compile()(); }
                    catch (InvalidCastException) { fractionalReadRejected = true; }
                    if (!fractionalReadRejected) throw new Exception("A floating database fraction was rounded to an integer.");
                }

                var temporalFacetRejected = false;
                try { source.FindMapping(typeof(DateTime), null, size: 3); }
                catch (ArgumentException) { temporalFacetRejected = true; }
                if (!temporalFacetRejected) throw new Exception("A temporal length facet was silently discarded.");

                var temporalCloneRejected = false;
                try { source.FindMapping(typeof(DateTime))!.WithStoreTypeAndSize("VARCHAR(4)", null); }
                catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
                {
                    temporalCloneRejected = true;
                }
                if (!temporalCloneRejected) throw new Exception("An incompatible temporal clone was silently accepted.");

                var temporalTransportRejected = false;
                try { source.FindMapping(typeof(DateTime))!.Clone(new RelationalTypeMappingInfo(typeof(DateTime), dbType: DbType.String)); }
                catch (ArgumentException) { temporalTransportRejected = true; }
                if (!temporalTransportRejected) throw new Exception("A temporal clone changed its parameter transport.");

                if (mode == OceanBaseCompatibilityMode.Oracle)
                {
                    foreach (var declaration in new[] { "DECIMAL", "NUMERIC" })
                    {
                        foreach (var type in new[] { typeof(decimal), typeof(int), typeof(bool) })
                        {
                            var aliasMapping = source.FindMapping(type, declaration)!;
                            if (aliasMapping.Precision != 38 || aliasMapping.Scale != 0)
                            {
                                throw new Exception("Oracle fixed-point alias defaults were not preserved.");
                            }
                        }

                        var alias = source.FindMapping(typeof(decimal), declaration)!;
                        var input = (OceanBaseParameter)alias.CreateParameter(command, "aliasInput", 1.25m);
                        var output = (OceanBaseParameter)alias.CreateParameter(command, "aliasOutput", null,
                            direction: ParameterDirection.Output);
                        if (!Equals(input.Value, 1.25m) || input.Precision != 0 || input.DecimalScale is not null
                            || output.Precision != 38 || output.DecimalScale != 0)
                        {
                            throw new Exception("Oracle fixed-point aliases changed query inputs or lost output facets.");
                        }
                    }

                    var number = source.FindMapping(typeof(decimal), "NUMBER")!;
                    if (number.Precision is not null || number.Scale is not null)
                    {
                        throw new Exception("Unqualified Oracle NUMBER lost value inference.");
                    }
                }

                foreach (var declaration in mode == OceanBaseCompatibilityMode.Oracle
                    ? new[] { "NVARCHAR2(4) trailing", "NCLOB(4)", "VARCHAR2" }
                    : new[] { "VARCHAR(4) trailing", "TEXT(4)", "VARCHAR" })
                {
                    var rejected = false;
                    try { source.FindMapping(typeof(string), declaration); }
                    catch (ArgumentException) { rejected = true; }
                    if (!rejected) throw new Exception("An invalid text declaration was silently accepted.");
                }

                var invalidUnicodeRejected = false;
                try { captionMapping.CreateParameter(command, "caption", "\ud800"); }
                catch (ArgumentException) { invalidUnicodeRejected = true; }
                if (!invalidUnicodeRejected) throw new Exception("Invalid UTF-16 was silently accepted.");

                Console.WriteLine($"{mode}: packed provider registration, scalar/text/temporal/binary/decimal model, string/null/Boolean SQL and paging passed; EF {typeof(DbContext).Assembly.GetName().Version}.");
            }
        }

        private sealed class ConsumerContext(DbContextOptions<ConsumerContext> options, OceanBaseCompatibilityMode mode) : DbContext(options)
        {
            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity<Row>().Property(row => row.Name).HasMaxLength(4);
                modelBuilder.Entity<Row>().Property(row => row.Payload).HasMaxLength(8);
                modelBuilder.Entity<Row>().Property(row => row.Count).HasColumnType(mode == OceanBaseCompatibilityMode.Oracle ? "NUMBER(3)" : "MEDIUMINT");
                modelBuilder.Entity<Row>().Property(row => row.Enabled).HasColumnType(mode == OceanBaseCompatibilityMode.Oracle ? "NUMBER(3)" : "BOOLEAN");
                modelBuilder.Entity<Row>().Property(row => row.ExplicitToken).HasColumnType(mode == OceanBaseCompatibilityMode.Oracle ? "VARCHAR2(36)" : "BINARY(16)");
                modelBuilder.Entity<Row>().Property(row => row.Amplitude).HasColumnType(mode == OceanBaseCompatibilityMode.Oracle ? "BINARY_FLOAT" : "FLOAT(24)");
                modelBuilder.Entity<Row>().Property(row => row.Wide).HasColumnType(mode == OceanBaseCompatibilityMode.Oracle ? "BINARY_DOUBLE" : "DOUBLE PRECISION");
                modelBuilder.Entity<Row>().Property(row => row.NarrowStamp).HasPrecision(3);
                modelBuilder.Entity<Row>().Property(row => row.Amount).HasPrecision(5, 2);
                modelBuilder.Entity<Row>().Property(row => row.NarrowDuration)
                    .HasColumnType(mode == OceanBaseCompatibilityMode.Oracle ? "INTERVAL DAY(1) TO SECOND(3)" : "TIME(3)");
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
            public double Amplitude { get; set; }
            public float Wide { get; set; }
            public DateTime NarrowStamp { get; set; }
            public decimal Amount { get; set; }
            public TimeSpan NarrowDuration { get; set; }
        }
    }
}
