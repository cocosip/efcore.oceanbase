using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OceanBase.EntityFrameworkCore.Query;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests
{
    public sealed class BooleanQueryTests
    {
        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Mixed_Boolean_comparisons_normalize_each_operand_before_null_compensation(
            OceanBaseCompatibilityMode mode, bool relationalNulls)
        {
            using var context = CreateContext(mode, relationalNulls);
            var sql = context.Rows.Where(row => row.Text == row.Number)
                .Select(row => new { Equal = row.Text == row.Number, Different = row.Native != row.Inverted,
                    NullableEqual = row.OptionalText == row.OptionalNumber,
                    NullableDifferent = row.OptionalText != row.OptionalNumber }).ToQueryString();
            Assert.Contains(TrueText(mode), sql);
            Assert.Contains(" = 2 THEN 1", sql);
            Assert.Contains(" = -1 THEN 0", sql);
            Assert.Contains(" = 0 THEN 1", sql);
            Assert.Contains(" = 1 THEN 0", sql);
            Assert.Contains("ELSE NULL", sql);
            Assert.Equal(!relationalNulls, sql.Contains("IS NULL", StringComparison.Ordinal));
            Assert.DoesNotContain("__oceanbase_internal_", sql);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Logical_operators_and_predicate_roots_use_truth_values(OceanBaseCompatibilityMode mode, bool relationalNulls)
        {
            using var context = CreateContext(mode, relationalNulls);
            IQueryable[] queries =
            [
                context.Rows.Where(row => row.Text),
                context.Rows.Where(row => !row.Text),
                context.Rows.Where(row => row.Text && row.Number),
                context.Rows.Where(row => row.Text || row.Inverted),
                context.Rows.Where(row => row.Text & row.Inverted),
                context.Rows.Where(row => row.Text | row.Inverted),
                context.Rows.Where(row => (row.OptionalText & row.OptionalNumber) == true),
                context.Rows.Where(row => (row.OptionalText | row.OptionalNumber) == false),
                context.Rows.Select(row => new { A = !row.OptionalText, B = row.OptionalText & row.OptionalNumber,
                    C = row.OptionalText | row.OptionalNumber }),
                context.Rows.Select(row => row.Text ? 10 : 20),
                context.Rows.OrderBy(row => row.Text).Select(row => row.Id),
                context.Rows.Where(row => row.Number && context.Rows.Any(other => other.Text && other.Id == row.Id))
            ];
            foreach (var query in queries)
            {
                var sql = query.ToQueryString();
                Assert.Contains("CASE WHEN", sql);
                Assert.DoesNotContain("__oceanbase_internal_", sql);
            }
            Assert.Contains(TrueText(mode), context.Rows.Where(row => row.Text).ToQueryString());
            Assert.Contains(" = 0 THEN 1", context.Rows.Where(row => row.Inverted).ToQueryString());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Converted_Boolean_CASE_and_coalesce_keep_independent_arm_mappings(
            OceanBaseCompatibilityMode mode, bool relationalNulls)
        {
            using var context = CreateContext(mode, relationalNulls);
            var sql = context.Rows.Where(row => (row.OptionalText ?? row.Number) == row.Inverted)
                .Select(row => new { A = row.Native ? row.OptionalText : row.OptionalNumber,
                    B = row.OptionalText ?? row.OptionalNumber }).ToQueryString();
            Assert.Contains(TrueText(mode), sql);
            Assert.Contains(" = 2 THEN 1", sql);
            Assert.Contains(" = 0 THEN 1", sql);
            Assert.Contains("COALESCE(", sql);
            Assert.DoesNotContain("__oceanbase_internal_", sql);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Nullable_captures_refresh_in_comparisons_and_lifted_logical_operations(
            OceanBaseCompatibilityMode mode, bool relationalNulls)
        {
            using var context = CreateContext(mode, relationalNulls);
            bool? flag = true;
            var query = context.Rows.Where(row => row.OptionalText == flag)
                .Select(row => new { A = row.OptionalText != flag, B = row.OptionalText & flag, C = row.OptionalText | flag });
            foreach (var value in new bool?[] { true, false, null, false, null, true })
            {
                flag = value;
                using var command = query.CreateDbCommand();
                Assert.Contains(TrueText(mode), command.CommandText);
                Assert.DoesNotContain("__oceanbase_internal_", command.CommandText);
                if (value is null)
                    Assert.Empty(command.Parameters);
                else
                {
                    Assert.NotEmpty(command.Parameters);
                    foreach (System.Data.Common.DbParameter parameter in command.Parameters)
                        Assert.Equal(value.Value ? 1m : 0m, Convert.ToDecimal(parameter.Value));
                }
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Native_Boolean_operands_keep_simple_SQL_and_raw_converted_projection_keeps_its_mapping(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var native = context.Rows.Where(row => row.Native && !row.Second).ToQueryString();
            Assert.DoesNotContain("CASE WHEN", native);
            var raw = context.Rows.Select(row => row.Text).ToQueryString();
            Assert.DoesNotContain("CASE", raw);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "SELECT `r`.`Text`" : "SELECT \"r\".\"Text\"", raw);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Converted_Boolean_operations_reject_ambiguous_storage(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            IQueryable[] invalid =
            [
                context.Rows.Where(row => row.Ambiguous),
                context.Rows.Where(row => row.Ambiguous == row.Native),
                context.Rows.Where(row => !row.Ambiguous),
                context.Rows.Where(row => row.Ambiguous && row.Text),
                context.Rows.Select(row => row.Ambiguous ? 10 : 20)
            ];
            foreach (var query in invalid)
                Assert.Throws<NotSupportedException>(() => query.ToQueryString());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Decimal_encoding_near_one_is_not_canonical_one(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var sql = context.Rows.Where(row => row.NearOne == row.Native).ToQueryString();
            Assert.Contains(" = 1.0000000000000000000000000001 THEN 1", sql);
            Assert.Contains(" = 0.0 THEN 0", sql);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Boolean_value_renderer_rejects_malformed_internal_markers(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var factory = context.GetService<ISqlExpressionFactory>();
            var mappings = context.GetService<IRelationalTypeMappingSource>();
            var boolean = mappings.FindMapping(typeof(bool))!;
            var integer = mappings.FindMapping(typeof(int))!;
            var value = factory.Constant(true, boolean);
            const string marker = OceanBaseSqlTranslatingExpressionVisitorFactory.BooleanValueMarker;
            SqlExpression[] invalid =
            [
                factory.Function(marker, [], true, [], typeof(bool), boolean),
                factory.Function(marker, [value, value], true, [true, true], typeof(bool), boolean),
                factory.Function(marker, [value], false, [true], typeof(bool), boolean),
                factory.Function(marker, [value], true, [false], typeof(bool), boolean),
                factory.Function(marker, [value], true, [true], typeof(int), integer)
            ];
            foreach (var expression in invalid)
            {
                var generator = new OceanBaseQuerySqlGenerator(context.GetService<QuerySqlGeneratorDependencies>(), mode);
                Assert.Contains("Invalid internal Boolean", Assert.Throws<InvalidOperationException>(
                    () => generator.GetCommand(expression)).Message);
            }
        }

        private static string TrueText(OceanBaseCompatibilityMode mode)
            => mode == OceanBaseCompatibilityMode.MySql ? " = 'Y' THEN 1" : " = N'Y' THEN 1";

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
                model.Entity<Row>().Property(row => row.Text).HasConversion(new BoolToStringConverter("N", "Y"));
                model.Entity<Row>().Property(row => row.OptionalText).HasConversion(new BoolToStringConverter("N", "Y"));
                model.Entity<Row>().Property(row => row.Number).HasConversion(value => value ? 2 : -1, value => value == 2);
                model.Entity<Row>().Property(row => row.OptionalNumber).HasConversion(new ValueConverter<bool, int>(value => value ? 2 : -1, value => value == 2));
                model.Entity<Row>().Property(row => row.Inverted).HasConversion(value => value ? 0 : 1, value => value == 0);
                model.Entity<Row>().Property(row => row.Ambiguous).HasConversion(value => "same", value => value == "same");
                model.Entity<Row>().Property(row => row.NearOne).HasConversion(
                    value => value ? 1.0000000000000000000000000001m : 0m, value => value != 0m).HasPrecision(38, 28);
            }
        }

        private sealed class Row
        {
            public int Id { get; set; }
            public bool Native { get; set; }
            public bool Second { get; set; }
            public bool Text { get; set; }
            public bool Number { get; set; }
            public bool Inverted { get; set; }
            public bool? OptionalText { get; set; }
            public bool? OptionalNumber { get; set; }
            public bool Ambiguous { get; set; }
            public bool NearOne { get; set; }
        }
    }
}
