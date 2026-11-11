using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests
{
    public sealed class BitwiseQueryTests
    {
        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Signed_operators_are_translated_in_predicates_and_projections(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var column = Column(mode, "Signed");
            var expected = mode == OceanBaseCompatibilityMode.MySql
                ? new[] { $"CAST(({column}) & (-3) AS SIGNED)", $"CAST(({column}) | (-3) AS SIGNED)",
                    $"CAST(({column}) ^ (-3) AS SIGNED)", $"CAST(~({column}) AS SIGNED)" }
                : new[] { $"BITAND({column}, -3)", $"(({column}) + (-3) - BITAND({column}, -3))",
                    $"(({column}) + (-3) - 2 * BITAND({column}, -3))", $"(-1 - ({column}))" };
            IQueryable[] queries =
            [
                context.Rows.Where(row => (row.Signed & -3) == -3).Select(row => row.Signed & -3),
                context.Rows.Where(row => (row.Signed | -3) == -3).Select(row => row.Signed | -3),
                context.Rows.Where(row => (row.Signed ^ -3) == -3).Select(row => row.Signed ^ -3),
                context.Rows.Where(row => ~row.Signed == -3).Select(row => ~row.Signed)
            ];
            for (var i = 0; i < queries.Length; i++)
            {
                var sql = queries[i].ToQueryString();
                Assert.Contains(expected[i], sql);
                Assert.Contains("WHERE", sql);
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Small_integers_are_promoted_before_complement(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            IQueryable<int>[] queries =
            [
                context.Rows.Select(row => ~row.SByte), context.Rows.Select(row => ~row.Byte),
                context.Rows.Select(row => ~row.Int16), context.Rows.Select(row => ~row.UInt16)
            ];
            foreach (var query in queries)
            {
                var sql = query.ToQueryString();
                Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? " AS SIGNED)" : "(-1 - (", sql);
                Assert.DoesNotContain(" & 255", sql);
                Assert.DoesNotContain(" & 65535", sql);
                if (mode == OceanBaseCompatibilityMode.MySql)
                {
                    Assert.DoesNotContain(" AS INT)", sql);
                    Assert.Contains("CAST(", sql);
                }
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Every_integer_family_supports_binary_operators(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            IQueryable[] queries =
            [
                context.Rows.Where(row => ((row.SByte & -3) | 2) == (row.SByte ^ 7)),
                context.Rows.Where(row => ((row.Byte & 3) | 2) == (row.Byte ^ 7)),
                context.Rows.Where(row => ((row.Int16 & -3) | 2) == (row.Int16 ^ 7)),
                context.Rows.Where(row => ((row.UInt16 & 3) | 2) == (row.UInt16 ^ 7)),
                context.Rows.Where(row => ((row.Signed & -3) | 2) == (row.Signed ^ 7)),
                context.Rows.Where(row => ((row.Unsigned & 3U) | 2U) == (row.Unsigned ^ 7U)),
                context.Rows.Where(row => ((row.Wide & long.MinValue) | -2L) == (row.Wide ^ long.MaxValue)),
                context.Rows.Where(row => ((row.UnsignedWide & ulong.MaxValue) | 9223372036854775808UL)
                    == (row.UnsignedWide ^ 7UL))
            ];
            foreach (var query in queries)
            {
                var sql = query.ToQueryString();
                Assert.Contains("WHERE", sql);
                Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? " ^ " : "BITAND(", sql);
                if (mode == OceanBaseCompatibilityMode.Oracle)
                {
                    Assert.DoesNotContain(" & ", sql);
                    Assert.DoesNotContain(" | ", sql);
                    Assert.DoesNotContain(" ^ ", sql);
                }
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Small_binary_operands_retain_promoted_result_width_and_signed_constants(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var sql = context.Rows.Where(row => (row.Byte ^ -1) < 0).Select(row => new
                { A = row.Byte | 256, B = row.Byte ^ -1, C = row.Byte & row.Signed, D = row.UInt16 ^ int.MinValue })
                .ToQueryString();
            if (mode == OceanBaseCompatibilityMode.MySql)
            {
                Assert.Contains($"CAST((CAST({Column(mode, "Byte")} AS SIGNED)) | (256) AS SIGNED)", sql);
                Assert.Contains(" ^ (-1) AS SIGNED)", sql);
            }
            else
            {
                Assert.Contains($"CAST({Column(mode, "Byte")} AS NUMBER(10))", sql);
                Assert.Contains(" + (256) - BITAND(", sql);
                Assert.Contains(" + (-1) - 2 * BITAND(", sql);
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Unsigned_complement_has_CLR_width_and_UInt64_and_uses_safe_Oracle_halves(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var sql = context.Rows.Select(row => new { A = ~row.Unsigned, B = ~row.UnsignedWide,
                C = ~row.ByteFlags, D = ~row.ShortFlags, E = ~row.Wide,
                F = row.UnsignedWide & 9223372036854775808UL }).ToQueryString();
            var wide = Column(mode, "UnsignedWide");
            if (mode == OceanBaseCompatibilityMode.MySql)
            {
                Assert.Contains($"(~({Column(mode, "Unsigned")}) & 4294967295)", sql);
                Assert.Contains($"(~({Column(mode, "ByteFlags")}) & 255)", sql);
                Assert.Contains($"(~({Column(mode, "ShortFlags")}) & 65535)", sql);
                Assert.Contains($"(~({wide}))", sql);
                Assert.Contains($"({wide}) & (9223372036854775808)", sql);
            }
            else
            {
                Assert.Contains($"(4294967295 - ({Column(mode, "Unsigned")}))", sql);
                Assert.True(sql.Contains($"(255 - ({Column(mode, "ByteFlags")}))", StringComparison.Ordinal), sql);
                Assert.Contains($"(65535 - ({Column(mode, "ShortFlags")}))", sql);
                Assert.Contains($"(18446744073709551615 - ({wide}))", sql);
                Assert.Contains($"BITAND(TRUNC(({wide}) / 4294967296), TRUNC((9223372036854775808) / 4294967296))", sql);
                Assert.Contains($"BITAND(MOD({wide}, 4294967296), MOD(9223372036854775808, 4294967296))", sql);
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Nullable_operators_retain_lifted_null_semantics(OceanBaseCompatibilityMode mode, bool relationalNulls)
        {
            using var context = CreateContext(mode, relationalNulls);
            var mask = -3;
            var sql = context.Rows.Where(row => (row.Optional ^ mask) != row.Optional)
                .Select(row => new { A = row.Optional & mask, B = row.Optional | mask,
                    C = row.Optional ^ mask, D = ~row.Optional, E = ~row.OptionalUnsigned,
                    F = row.OptionalUnsignedWide & 9223372036854775808UL }).ToQueryString();
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "@mask" : ":mask", sql);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? " ^ " : "BITAND(", sql);
            Assert.Equal(!relationalNulls, sql.Contains("IS NULL", StringComparison.Ordinal));
            var nullTest = context.Rows.Where(row => (row.Optional & mask) == null).ToQueryString();
            Assert.Contains("IS NULL", nullTest);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Nested_operands_are_grouped_and_integral_converters_and_enums_keep_numeric_storage(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var sql = context.Rows.Where(row => ((row.Converted | -3) ^ (row.Signed + 2)) == -1)
                .Select(row => new { A = ~(row.Signed + 2), B = row.Flags & Flags.High,
                    C = row.Flags | Flags.Low, D = row.Flags ^ Flags.High, E = ~row.Flags }).ToQueryString();
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? $"~({Column(mode, "Signed")} + 2)"
                : $"-1 - ({Column(mode, "Signed")} + 2)", sql);
            Assert.Contains(Column(mode, "Converted"), sql);
            Assert.Contains("-2147483648", sql);
            Assert.Contains("WHERE", sql);
            var promotedEnum = context.Rows.Select(row => ~(int)row.ByteFlags).ToQueryString();
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? " AS SIGNED)" : "(-1 - (", promotedEnum);
            Assert.DoesNotContain("255 -", promotedEnum);
            Assert.DoesNotContain(" & 255", promotedEnum);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Non_integer_storage_is_rejected_before_execution(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            IQueryable[] queries =
            [
                context.Rows.Where(row => (row.TextBacked & 3) == 1),
                context.Rows.Where(row => (row.FloatBacked | 3) == 1),
                context.Rows.Where(row => (row.DecimalBacked ^ 3) == 1),
                context.Rows.Where(row => ~row.TextFlags == Flags.Low),
                context.Rows.Where(row => ~row.TextBacked == 1)
            ];
            foreach (var query in queries)
            {
                var error = Record.Exception(() => query.ToQueryString());
                Assert.True(error is NotSupportedException, $"{query.Expression}\n{error}\n{(error is null ? query.ToQueryString() : "")}");
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Boolean_logic_and_opposite_mode_contexts_keep_their_paths(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var sql = context.Rows.Where(row => (row.Enabled & (row.Signed > 0)) | !row.Enabled).ToQueryString();
            Assert.Contains(" AND ", sql);
            Assert.Contains(" OR ", sql);
            Assert.DoesNotContain("BITAND(", sql);
            Assert.DoesNotContain(" AS SIGNED)", sql);
            using var opposite = CreateContext(mode == OceanBaseCompatibilityMode.MySql
                ? OceanBaseCompatibilityMode.Oracle : OceanBaseCompatibilityMode.MySql);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "BITAND(" : " AS SIGNED)",
                opposite.Rows.Where(row => (row.Signed & -3) == -3).ToQueryString());
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? " AS SIGNED)" : "BITAND(",
                context.Rows.Where(row => (row.Signed & -3) == -3).ToQueryString());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Explicit_numeric_casts_outside_bitwise_keep_their_existing_path(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var sql = context.Rows.Select(row => new { A = (long)row.TextBacked,
                B = (long)row.FloatBacked, C = (long)row.DecimalBacked }).ToQueryString();
            Assert.Contains("CAST(", sql);
            Assert.Contains(Column(mode, "TextBacked"), sql);
        }

        private static string Column(OceanBaseCompatibilityMode mode, string name)
            => mode == OceanBaseCompatibilityMode.MySql ? $"`r`.`{name}`" : $"\"r\".\"{name}\"";

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
            protected override void OnModelCreating(ModelBuilder builder)
            {
                builder.Entity<Row>().Property(row => row.Converted).HasConversion<long>();
                builder.Entity<Row>().Property(row => row.TextBacked).HasConversion<string>();
                builder.Entity<Row>().Property(row => row.FloatBacked).HasConversion<double>();
                builder.Entity<Row>().Property(row => row.DecimalBacked).HasConversion<decimal>();
                builder.Entity<Row>().Property(row => row.TextFlags).HasConversion<string>();
            }
        }

        [Flags]
        private enum Flags { Low = 1, High = int.MinValue }
        [Flags]
        private enum ByteFlags : byte { Low = 1 }
        [Flags]
        private enum ShortFlags : ushort { Low = 1 }

        private sealed class Row
        {
            public int Id { get; set; }
            public sbyte SByte { get; set; }
            public byte Byte { get; set; }
            public short Int16 { get; set; }
            public ushort UInt16 { get; set; }
            public int Signed { get; set; }
            public uint Unsigned { get; set; }
            public long Wide { get; set; }
            public ulong UnsignedWide { get; set; }
            public int? Optional { get; set; }
            public uint? OptionalUnsigned { get; set; }
            public ulong? OptionalUnsignedWide { get; set; }
            public int Converted { get; set; }
            public Flags Flags { get; set; }
            public ByteFlags ByteFlags { get; set; }
            public ShortFlags ShortFlags { get; set; }
            public int TextBacked { get; set; }
            public int FloatBacked { get; set; }
            public int DecimalBacked { get; set; }
            public Flags TextFlags { get; set; }
            public bool Enabled { get; set; }
        }
    }
}
