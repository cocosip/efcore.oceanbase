using System;
using System.Linq;
using System.Linq.Expressions;
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
    public sealed class BooleanXorQueryTests
    {
        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Nullable_XOR_keeps_three_valued_projection_and_surrounding_compensation(
            OceanBaseCompatibilityMode mode, bool relationalNulls)
        {
            using var context = CreateContext(mode, relationalNulls);
            var projection = context.Rows.Select(row => row.Optional ^ row.Other).ToQueryString();
            Assert.Contains(" <> ", projection);
            Assert.Contains("CASE WHEN", projection);
            Assert.Contains("THEN 1", projection);
            Assert.Contains("THEN 0", projection);
            Assert.Contains("ELSE NULL", projection);
            Assert.DoesNotContain(" ^ ", projection);
            Assert.DoesNotContain("IS NULL", projection);
            Assert.DoesNotContain("IS NOT NULL", projection);
            var comparison = context.Rows.Where(row => (row.Optional ^ row.Other) != row.Optional).ToQueryString();
            Assert.Contains(" <> ", comparison);
            Assert.Equal(!relationalNulls, comparison.Contains("IS NULL", StringComparison.Ordinal));
            var nullTest = context.Rows.Where(row => (row.Optional ^ row.Other) == null).ToQueryString();
            Assert.Contains("IS NULL", nullTest);
            Assert.DoesNotContain(" ^ ", nullTest);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void XOR_normalizes_each_converted_operand_and_has_native_Boolean_result(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var sql = context.Rows.Where(row => row.Text ^ row.Number)
                .Select(row => new { A = row.Text ^ row.Number, B = row.Number ^ row.Native,
                    C = row.Inverted ^ row.Text, D = row.OptionalText ^ row.Other }).ToQueryString();
            Assert.Contains("WHERE", sql);
            Assert.DoesNotContain(" ^ ", sql);
            Assert.Contains(mode == OceanBaseCompatibilityMode.Oracle ? " = N'Y' THEN 1" : " = 'Y' THEN 1", sql);
            Assert.Contains(mode == OceanBaseCompatibilityMode.Oracle ? " = N'N' THEN 0" : " = 'N' THEN 0", sql);
            Assert.Contains(" = 2 THEN 1", sql);
            Assert.Contains(" = -1 THEN 0", sql);
            Assert.Contains(" = 0 THEN 1", sql);
            Assert.Contains(" = 1 THEN 0", sql);
            Assert.DoesNotContain("THEN 'Y'", sql);
            Assert.DoesNotContain("THEN N'Y'", sql);
            Assert.DoesNotContain("THEN 2", sql);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void XOR_translates_in_nested_conditions_NOT_CASE_order_and_subquery(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            IQueryable[] queries =
            [
                context.Rows.Where(row => !(row.Native ^ (row.Id > 0))),
                context.Rows.Where(row => (row.Text ^ row.Number) && row.Native),
                context.Rows.Where(row => (row.Text ^ row.Number) || !row.Native),
                context.Rows.Where(row => ((row.Native ^ row.Text) ^ row.Number) == row.Native),
                context.Rows.Where(row => (row.Optional ^ row.Other) == true),
                context.Rows.Select(row => (row.Native ^ row.Text) ? 10 : 20),
                context.Rows.OrderBy(row => row.Native ^ row.Text).Select(row => row.Id),
                context.Rows.Where(row => row.Native ^ context.Rows.Any(other => other.Id > row.Id))
            ];
            foreach (var query in queries)
            {
                var sql = query.ToQueryString();
                Assert.Contains(" <> ", sql);
                Assert.Contains("CASE WHEN", sql);
                Assert.DoesNotContain(" ^ ", sql);
            }
            var integer = context.Rows.Where(row => (row.Id ^ 3) != 0).ToQueryString();
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? " ^ " : "BITAND(", integer);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Nullable_capture_transitions_preserve_XOR_NULL_and_refresh_binds(
            OceanBaseCompatibilityMode mode, bool relationalNulls)
        {
            using var context = CreateContext(mode, relationalNulls);
            bool? flag = true;
            var query = context.Rows.Where(row => (row.OptionalText ^ flag) == true)
                .Select(row => row.OptionalText ^ flag);
            foreach (var value in new bool?[] { true, false, null, true, null, false })
            {
                flag = value;
                using var command = query.CreateDbCommand();
                Assert.DoesNotContain(" ^ ", command.CommandText);
                Assert.Contains(" <> ", command.CommandText);
                if (value is null)
                {
                    Assert.Empty(command.Parameters);
                    Assert.Contains("(NULL) = 1 THEN 1", command.CommandText);
                }
                else
                {
                    Assert.NotEmpty(command.Parameters);
                    foreach (System.Data.Common.DbParameter parameter in command.Parameters)
                        Assert.Equal(value.Value ? 1m : 0m, Convert.ToDecimal(parameter.Value));
                    Assert.DoesNotContain("(NULL) = 1", command.CommandText);
                }
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void XOR_rejects_ambiguous_or_unsupported_Boolean_storage(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            foreach (var query in new[] { context.Rows.Where(row => row.Ambiguous ^ row.Native),
                context.Rows.Where(row => row.Binary ^ row.Native),
                context.Rows.Where(row => row.NullFalse ^ row.Native) })
                Assert.Throws<NotSupportedException>(() => query.ToQueryString());
            if (mode == OceanBaseCompatibilityMode.Oracle)
                Assert.Throws<NotSupportedException>(() => context.Rows.Where(row => row.Empty ^ row.Native).ToQueryString());
            else
                Assert.Contains(" = '' THEN 0", context.Rows.Where(row => row.Empty ^ row.Native).ToQueryString());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void XOR_rejects_null_converting_Boolean_carrier(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var native = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(bool))!;
#pragma warning disable EF1001 // Exercise EF's null-converting mapping boundary without a model convention changing it.
            var converter = new ValueConverter<bool, bool>(value => value, value => value, convertsNulls: true);
#pragma warning restore EF1001
            var mapped = (RelationalTypeMapping)new BoolTypeMapping("BOOLEAN").WithComposedConverter(converter);
            var left = new ColumnExpression("Flag", "r", typeof(bool), mapped, true);
            var right = new SqlConstantExpression(true, typeof(bool), native);
            var expression = new SqlBinaryExpression(ExpressionType.ExclusiveOr, left, right, typeof(bool), native);
            var generator = new OceanBaseQuerySqlGenerator(context.GetService<QuerySqlGeneratorDependencies>(), mode);
            Assert.Throws<NotSupportedException>(() => generator.GetCommand(expression));
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
                model.Entity<Row>().Property(row => row.Text).HasConversion(value => value ? "Y" : "N", value => value == "Y");
                model.Entity<Row>().Property(row => row.OptionalText).HasConversion(new BoolToStringConverter("N", "Y"));
                model.Entity<Row>().Property(row => row.Number).HasConversion(value => value ? 2 : -1, value => value == 2);
                model.Entity<Row>().Property(row => row.Inverted).HasConversion(value => value ? 0 : 1, value => value == 0);
                model.Entity<Row>().Property(row => row.Ambiguous).HasConversion(value => "same", value => value == "same");
                model.Entity<Row>().Property(row => row.Empty).HasConversion(value => value ? "Y" : "", value => value == "Y");
                model.Entity<Row>().Property(row => row.NullFalse).HasConversion(value => value ? "Y" : null!, value => value == "Y");
                model.Entity<Row>().Property(row => row.Binary).HasConversion(value => new byte[] { value ? (byte)1 : (byte)0 }, value => value[0] == 1);
            }
        }

        private sealed class Row
        {
            public int Id { get; set; }
            public bool Native { get; set; }
            public bool? Optional { get; set; }
            public bool? Other { get; set; }
            public bool Text { get; set; }
            public bool? OptionalText { get; set; }
            public bool Number { get; set; }
            public bool Inverted { get; set; }
            public bool Ambiguous { get; set; }
            public bool Empty { get; set; }
            public bool Binary { get; set; }
            public bool NullFalse { get; set; }
        }
    }
}
