using System;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBase.EntityFrameworkCore.Query;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests
{
    public sealed class ShiftQueryTests
    {
        [Fact]
        public void MySql_signed_Int32_left_shift_cast_encloses_the_completed_unsigned_rotation()
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.MySql);
            var sql = context.Rows.Select(row => row.Signed << row.Count).ToQueryString();
            var native = "((`r`.`Signed`) << ((`r`.`Count`) & 31))";
            Assert.StartsWith($"SELECT (CAST((({native} & 4294967295) ^ 2147483648) AS SIGNED) - 2147483648)", sql);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Signed_shift_SQL_preserves_width_and_sign(OceanBaseCompatibilityMode mode, bool left)
        {
            using var context = CreateContext(mode);
            var count = -1;
            var sql = left
                ? context.Rows.Where(row => (row.Signed << count) < 0).Select(row => new
                    { I = row.Signed << count, L = row.Wide << count }).ToQueryString()
                : context.Rows.Where(row => (row.Signed >> count) < 0).Select(row => new
                    { I = row.Signed >> count, L = row.Wide >> count }).ToQueryString();
            Assert.Contains("WHERE", sql);
            Assert.DoesNotContain("__oceanbase_internal_", sql);
            if (mode == OceanBaseCompatibilityMode.MySql)
            {
                Assert.Contains(left ? " << " : " >> ", sql);
                Assert.Contains(" & 31)", sql);
                Assert.Contains(" & 63)", sql);
                Assert.Contains(" AS SIGNED)", sql);
                if (left)
                {
                    Assert.Contains(" & 4294967295)", sql);
                    Assert.Contains(" ^ 2147483648)", sql);
                    Assert.Contains(" - 2147483648)", sql);
                }
                else
                {
                    Assert.Contains("CASE WHEN", sql);
                    Assert.Contains("THEN CAST(~", sql);
                    Assert.Contains("ELSE CAST(", sql);
                }
            }
            else
            {
                Assert.Contains("BITAND(:count, 31)", sql);
                Assert.Contains("BITAND(:count, 63)", sql);
                Assert.Contains("POWER(2, ", sql);
                if (left)
                {
                    Assert.Contains("MOD(MOD(", sql);
                    Assert.Contains("32 - BITAND", sql);
                    Assert.Contains("64 - BITAND", sql);
                    Assert.Contains(" + 2147483648, 4294967296) - 2147483648)", sql);
                    Assert.Contains(" + 9223372036854775808, 18446744073709551616) - 9223372036854775808)", sql);
                }
                else
                {
                    Assert.Contains("FLOOR(", sql);
                    Assert.DoesNotContain("TRUNC(", sql);
                }
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void All_integer_families_translate_both_directions_in_predicates(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            IQueryable[] queries =
            [
                context.Rows.Where(row => (row.SByte << row.Count) == (row.SByte >> row.Count)),
                context.Rows.Where(row => (row.Byte << row.Count) == (row.Byte >> row.Count)),
                context.Rows.Where(row => (row.Int16 << row.Count) == (row.Int16 >> row.Count)),
                context.Rows.Where(row => (row.UInt16 << row.Count) == (row.UInt16 >> row.Count)),
                context.Rows.Where(row => (row.Signed << row.Count) == (row.Signed >> row.Count)),
                context.Rows.Where(row => (row.Unsigned << row.Count) == (row.Unsigned >> row.Count)),
                context.Rows.Where(row => (row.Wide << row.Count) == (row.Wide >> row.Count)),
                context.Rows.Where(row => (row.UnsignedWide << row.Count) == (row.UnsignedWide >> row.Count)),
                context.Rows.Where(row => (row.Converted << row.Count) == (row.Converted >> row.Count))
            ];
            foreach (var query in queries)
            {
                var sql = query.ToQueryString();
                Assert.Contains("WHERE", sql);
                Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? " << " : "MOD(MOD(", sql);
                Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? " >> " : "FLOOR(", sql);
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, -1)]
        [InlineData(OceanBaseCompatibilityMode.MySql, 0)]
        [InlineData(OceanBaseCompatibilityMode.MySql, 31)]
        [InlineData(OceanBaseCompatibilityMode.MySql, 32)]
        [InlineData(OceanBaseCompatibilityMode.MySql, 63)]
        [InlineData(OceanBaseCompatibilityMode.MySql, 64)]
        [InlineData(OceanBaseCompatibilityMode.MySql, 65)]
        [InlineData(OceanBaseCompatibilityMode.MySql, int.MinValue)]
        [InlineData(OceanBaseCompatibilityMode.MySql, int.MaxValue)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, -1)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, 0)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, 31)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, 32)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, 63)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, 64)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, 65)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, int.MinValue)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, int.MaxValue)]
        public void Literal_count_boundaries_are_masked_for_each_promoted_width(OceanBaseCompatibilityMode mode, int count)
        {
            using var context = CreateContext(mode);
            var row = Expression.Parameter(typeof(Row), "row");
            var narrow = Expression.LeftShift(Expression.Property(row, nameof(Row.Unsigned)), Expression.Constant(count));
            var wide = Expression.RightShift(Expression.Property(row, nameof(Row.UnsignedWide)), Expression.Constant(count));
            var predicate = Expression.Lambda<Func<Row, bool>>(Expression.AndAlso(
                Expression.Equal(narrow, Expression.Constant(0U)), Expression.Equal(wide, Expression.Constant(0UL))), row);
            var sql = context.Rows.Where(predicate).ToQueryString();
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? $"({count}) & 31" : $"BITAND({count}, 31)", sql);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? $"({count}) & 63" : $"BITAND({count}, 63)", sql);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Nullable_value_and_count_keep_compensation_and_lifted_results(OceanBaseCompatibilityMode mode, bool relationalNulls)
        {
            using var context = CreateContext(mode, relationalNulls);
            var sql = context.Rows.Where(row => (row.Optional << row.OptionalCount) != row.Optional)
                .Select(row => new { A = row.Optional << row.OptionalCount, B = row.Optional >> row.OptionalCount,
                    C = row.OptionalWide << row.OptionalCount, D = row.OptionalUnsigned >> row.OptionalCount }).ToQueryString();
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? " << " : "MOD(MOD(", sql);
            Assert.Equal(!relationalNulls, sql.Contains("IS NULL", StringComparison.Ordinal));
            var nullTest = context.Rows.Where(row => (row.Optional >> row.OptionalCount) == null).ToQueryString();
            Assert.Contains("IS NULL", nullTest);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Nested_shifts_and_other_operators_keep_operand_grouping(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var sql = context.Rows.Where(row => ((row.Signed << (row.Count + 2)) >> (row.Count & 7)) < 0)
                .Select(row => new { A = (row.Unsigned | 1U) << (row.Count + 2),
                    B = ~(row.Wide >> row.Count), C = row.Wide << (row.Count << 1),
                    D = row.Converted << row.ConvertedCount }).ToQueryString();
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? " << " : "POWER(2, ", sql);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "`r`.`Count` + 2" : "\"r\".\"Count\" + 2", sql);
            Assert.DoesNotContain("__oceanbase_internal_", sql);
            Assert.Contains("WHERE", sql);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Non_integer_value_or_count_storage_is_rejected_before_execution(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            IQueryable[] queries =
            [
                context.Rows.Where(row => (row.Text << row.Count) == 0),
                context.Rows.Where(row => (row.Signed >> row.Text) == 0),
                context.Rows.Where(row => (row.Floating << row.Count) == 0),
                context.Rows.Where(row => (row.Signed >> row.Decimal) == 0)
            ];
            foreach (var query in queries)
                Assert.Throws<NotSupportedException>(() => query.ToQueryString());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Shift_renderer_rejects_malformed_internal_markers(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var factory = context.GetService<ISqlExpressionFactory>();
            var mappings = context.GetService<IRelationalTypeMappingSource>();
            var integer = mappings.FindMapping(typeof(int))!;
            var wide = mappings.FindMapping(typeof(long))!;
            var value = new ColumnExpression("Signed", "r", typeof(int), integer, false);
            var count = factory.Constant(1, integer);
            foreach (var marker in new[] { OceanBaseSqlTranslatingExpressionVisitorFactory.LeftShiftMarker,
                OceanBaseSqlTranslatingExpressionVisitorFactory.RightShiftMarker })
            {
                SqlExpression[] invalid =
                [
                    factory.Function(marker, [value], true, [true], typeof(int), integer),
                    factory.Function(marker, [value, count], false, [true, true], typeof(int), integer),
                    factory.Function(marker, [value, count], true, [true, false], typeof(int), integer),
                    factory.Function(marker, [value, factory.Constant(1L, wide)], true, [true, true], typeof(int), integer),
                    factory.Function(marker, [value, count], true, [true, true], typeof(long), wide)
                ];
                foreach (var expression in invalid)
                {
                    var generator = new OceanBaseQuerySqlGenerator(context.GetService<QuerySqlGeneratorDependencies>(), mode);
                    Assert.Contains("Invalid internal integer shift", Assert.Throws<InvalidOperationException>(
                        () => generator.GetCommand(expression)).Message);
                }
            }
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
            protected override void OnModelCreating(ModelBuilder model)
            {
                model.Entity<Row>().Property(row => row.Converted).HasConversion<long>();
                model.Entity<Row>().Property(row => row.ConvertedCount).HasConversion<long>();
                model.Entity<Row>().Property(row => row.Text).HasConversion<string>();
                model.Entity<Row>().Property(row => row.Floating).HasConversion<double>();
                model.Entity<Row>().Property(row => row.Decimal).HasConversion<decimal>();
            }
        }

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
            public int Count { get; set; }
            public int? Optional { get; set; }
            public int? OptionalCount { get; set; }
            public long? OptionalWide { get; set; }
            public uint? OptionalUnsigned { get; set; }
            public int Converted { get; set; }
            public int ConvertedCount { get; set; }
            public int Text { get; set; }
            public int Floating { get; set; }
            public int Decimal { get; set; }
        }
    }
}
