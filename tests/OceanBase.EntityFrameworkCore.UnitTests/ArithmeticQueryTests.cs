using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests
{
    public sealed class ArithmeticQueryTests
    {
        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void All_integer_families_use_truncating_division_after_integral_promotion(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            IQueryable[] queries =
            [
                context.Rows.Select(row => row.SByte / 2),
                context.Rows.Select(row => row.Byte / 2),
                context.Rows.Select(row => row.Int16 / 2),
                context.Rows.Select(row => row.UInt16 / 2),
                context.Rows.Select(row => row.Int32 / 2),
                context.Rows.Select(row => row.UInt32 / 2U),
                context.Rows.Select(row => row.Int64 / 2L),
                context.Rows.Select(row => row.UInt64 / 2UL),
                context.Rows.Select(row => row.Converted / 2)
            ];
            foreach (var query in queries)
            {
                var sql = query.ToQueryString();
                Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? " DIV 2" : "TRUNC(", sql);
                if (mode == OceanBaseCompatibilityMode.Oracle) Assert.Contains(" / 2)", sql);
            }
            using var opposite = CreateContext(mode == OceanBaseCompatibilityMode.MySql
                ? OceanBaseCompatibilityMode.Oracle : OceanBaseCompatibilityMode.MySql);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "TRUNC(" : " DIV 2",
                opposite.Rows.Select(row => row.Int32 / 2).ToQueryString());
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? " DIV 2" : "TRUNC(",
                context.Rows.Select(row => row.Int32 / 2).ToQueryString());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Nullable_signed_division_and_remainder_keep_parameters_and_null_semantics(
            OceanBaseCompatibilityMode mode, bool relationalNulls)
        {
            using var context = CreateContext(mode, relationalNulls);
            var divisor = -3;
            var sql = context.Rows.Select(row => new { Q = -row.Int32 / divisor, L = row.Int64 / -3L,
                N = row.Optional / divisor, U = row.OptionalUnsigned / 2U }).ToQueryString();
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "@divisor" : ":divisor", sql);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? " DIV " : "TRUNC(", sql);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "-`r`.`Int32` DIV @divisor"
                : "TRUNC(-\"r\".\"Int32\" / :divisor)", sql);
            var remainder = context.Rows.Select(row => new { R = -row.Int32 % divisor,
                L = row.Int64 % -3L, N = row.Optional % divisor, U = row.UInt64 % 2UL,
                D = row.Decimal % 2m, F = row.Single % 2f, W = row.Double % 2d }).ToQueryString();
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? " % " : "MOD(", remainder);
            if (mode == OceanBaseCompatibilityMode.Oracle) Assert.DoesNotContain(" % ", remainder);
            var comparison = context.Rows.Where(row => row.Optional / divisor != row.Optional).ToQueryString();
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? " DIV " : "TRUNC(", comparison);
            Assert.Equal(!relationalNulls, comparison.Contains("IS NULL", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Remainder_uses_native_operator_or_function(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var sql = context.Rows.Select(row => row.Int32 % 3).ToQueryString();
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "`r`.`Int32` % 3"
                : "MOD(\"r\".\"Int32\", 3)", sql);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Fractional_division_and_explicit_casts_are_not_integer_division(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            IQueryable[] queries =
            [
                context.Rows.Select(row => row.Decimal / 2m),
                context.Rows.Select(row => row.Single / 2f),
                context.Rows.Select(row => row.Double / 2d),
                context.Rows.Select(row => (decimal)row.Int32 / 2m),
                context.Rows.Select(row => (float)row.Int32 / 2f),
                context.Rows.Select(row => (double)row.Int32 / 2d)
            ];
            foreach (var query in queries)
            {
                var sql = query.ToQueryString();
                Assert.Contains(" / ", sql);
                Assert.DoesNotContain(" DIV ", sql);
                Assert.DoesNotContain("TRUNC(", sql);
            }
            var arithmetic = context.Rows.Select(row => new { A = row.Int32 + 2,
                S = row.Int32 - 2, M = row.Int32 * 2, N = -row.Int32 }).ToQueryString();
            Assert.Contains(" + 2", arithmetic);
            Assert.Contains(" - 2", arithmetic);
            Assert.Contains(" * 2", arithmetic);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "-`r`.`Int32`" : "-\"r\".\"Int32\"", arithmetic);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Nested_arithmetic_keeps_operand_grouping_and_predicate_comparisons(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var sql = context.Rows.Select(row => new { Sum = row.Int32 / (row.Int32 + 2),
                Divide = row.Int32 / (row.Int32 / 2), Remainder = row.Int32 % (row.Int32 % 3),
                Product = (row.Int32 / 2) * 3, Divisor = row.Int32 / (row.Int32 * 2) }).ToQueryString();
            if (mode == OceanBaseCompatibilityMode.MySql)
            {
                Assert.Contains("`r`.`Int32` DIV (`r`.`Int32` + 2)", sql);
                Assert.Contains("`r`.`Int32` DIV (`r`.`Int32` DIV 2)", sql);
                Assert.Contains("`r`.`Int32` % (`r`.`Int32` % 3)", sql);
                Assert.Contains("(`r`.`Int32` DIV 2) * 3", sql);
                Assert.Contains("`r`.`Int32` DIV (`r`.`Int32` * 2)", sql);
            }
            else
            {
                Assert.Contains("TRUNC(\"r\".\"Int32\" / (\"r\".\"Int32\" + 2))", sql);
                Assert.Contains("TRUNC(\"r\".\"Int32\" / (TRUNC(\"r\".\"Int32\" / 2)))", sql);
                Assert.Contains("MOD(\"r\".\"Int32\", MOD(\"r\".\"Int32\", 3))", sql);
                Assert.Contains("(TRUNC(\"r\".\"Int32\" / 2)) * 3", sql);
                Assert.Contains("TRUNC(\"r\".\"Int32\" / (\"r\".\"Int32\" * 2))", sql);
            }
            var predicate = context.Rows.Where(row => row.Int32 / 2 == 1 && row.Int32 % 2 == 1).ToQueryString();
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "(`r`.`Int32` DIV 2) = 1"
                : "(TRUNC(\"r\".\"Int32\" / 2)) = 1", predicate);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "(`r`.`Int32` % 2) = 1"
                : "(MOD(\"r\".\"Int32\", 2)) = 1", predicate);
        }

        private static TestContext CreateContext(OceanBaseCompatibilityMode mode, bool relationalNulls = false)
        {
            var builder = new DbContextOptionsBuilder<TestContext>().UseOceanBase("Server=localhost;Database=test", mode);
            ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(
                builder.Options.Extensions.OfType<RelationalOptionsExtension>().Single().WithUseRelationalNulls(relationalNulls));
            return new TestContext(builder.Options);
        }

        private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
        {
            public DbSet<Row> Rows => Set<Row>();
            protected override void OnModelCreating(ModelBuilder modelBuilder)
                => modelBuilder.Entity<Row>().Property(row => row.Converted).HasConversion<long>();
        }

        private sealed class Row
        {
            public int Id { get; set; }
            public sbyte SByte { get; set; }
            public byte Byte { get; set; }
            public short Int16 { get; set; }
            public ushort UInt16 { get; set; }
            public int Int32 { get; set; }
            public uint UInt32 { get; set; }
            public long Int64 { get; set; }
            public ulong UInt64 { get; set; }
            public int? Optional { get; set; }
            public uint? OptionalUnsigned { get; set; }
            public int Converted { get; set; }
            public decimal Decimal { get; set; }
            public float Single { get; set; }
            public double Double { get; set; }
        }
    }
}
