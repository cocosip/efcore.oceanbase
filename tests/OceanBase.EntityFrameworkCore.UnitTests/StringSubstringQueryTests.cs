using System;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests
{
    public sealed class StringSubstringQueryTests
    {
        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Both_overloads_translate_in_predicates_and_composed_projections(OceanBaseCompatibilityMode mode, bool relationalNulls)
        {
            using var context = CreateContext(mode, relationalNulls);
            var function = mode == OceanBaseCompatibilityMode.MySql ? "SUBSTRING(" : "SUBSTR(";
            IQueryable[] queries =
            [
                context.Rows.Where(row => row.Required.Substring(0) == "x"),
                context.Rows.Where(row => row.Optional!.Substring(row.Start, row.Length) != "x"),
                context.Rows.Where(row => row.Required.Substring(0, 0) == null),
                context.Rows.Where(row => row.Required.Substring(1) != null),
                context.Rows.Where(row => !(row.Required.Substring(1, 2) != null)),
                context.Rows.Select(row => (row.Id > 0 ? row.Optional : row.Required)!.Substring(row.Start, row.Length))
                    .Where(value => value == null),
                context.Rows.Select(row => (row.Optional ?? row.Required).Substring(row.Start).ToUpper() + "x"),
                context.Rows.Where(row => row.Required.Substring(row.Start).Contains("x")),
                context.Rows.Where(row => (row.Optional!.Substring(row.Start, row.Length) ?? "x") == "x")
            ];
            foreach (var query in queries)
                Assert.Contains(function, query.ToQueryString());
            Assert.Contains(" IS NULL", queries[2].ToQueryString());
            Assert.Contains(" IS NOT NULL", queries[3].ToQueryString());
            Assert.Contains("CASE", queries[5].ToQueryString());
            Assert.Contains("COALESCE(", queries[8].ToQueryString());
            Assert.Contains("INSTR(", queries[7].ToQueryString());
            Assert.Contains("UPPER(", queries[6].ToQueryString());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Origin_adjustment_uses_long_for_constants_and_dynamic_start(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var function = mode == OceanBaseCompatibilityMode.MySql ? "SUBSTRING(" : "SUBSTR(";
            foreach (var start in new[] { 0, -1, -2, int.MinValue, int.MaxValue })
            {
                var sql = context.Rows.Where(row => row.Required.Substring(start) == "x").ToQueryString();
                Assert.Contains(function, sql);
                Assert.Contains($"'{start}'", sql);
                Assert.Contains(" + 1", sql);
            }
            Assert.Contains("2147483648", context.Rows.Where(row => row.Required.Substring(int.MaxValue) == "x").ToQueryString());
            Assert.Contains("-2147483647", context.Rows.Where(row => row.Required.Substring(int.MinValue) == "x").ToQueryString());
            Assert.Contains(", 1)", context.Rows.Where(row => row.Required.Substring(0) == "x").ToQueryString());
            Assert.Contains(", 0)", context.Rows.Where(row => row.Required.Substring(-1) == "x").ToQueryString());
            Assert.Contains(", -1)", context.Rows.Where(row => row.Required.Substring(-2) == "x").ToQueryString());
            foreach (var length in new[] { -1, 0, int.MaxValue })
                Assert.Contains($"'{length}'",
                    context.Rows.Where(row => row.Required.Substring(0, length) == "x").ToQueryString());
            var columnSql = context.Rows.Where(row => row.Required.Substring(row.Start, row.Length) == "x").ToQueryString();
            Assert.Contains("Start", columnSql);
            Assert.Contains(" + 1", columnSql);
            Assert.Contains("Length", columnSql);

            var factory = context.GetService<ISqlExpressionFactory>();
            var sourceMapping = (RelationalTypeMapping)context.Model.FindEntityType(typeof(Row))!.FindProperty(nameof(Row.Required))!.GetTypeMapping();
            var intMapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(int))!;
            var translator = context.GetService<IMethodCallTranslatorProvider>();
            var logger = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>();
            var method = typeof(string).GetMethod(nameof(string.Substring), [typeof(int), typeof(int)])!;
            var functionExpression = Assert.IsType<SqlFunctionExpression>(translator.Translate(context.Model,
                factory.Constant("abc", sourceMapping), method,
                [factory.Function("START_VALUE", [], nullable: true, argumentsPropagateNullability: [], typeof(int), intMapping),
                    factory.Function("LENGTH_VALUE", [], nullable: true, argumentsPropagateNullability: [], typeof(int), intMapping)], logger));
            Assert.Equal(3, functionExpression.Arguments!.Count);
            var adjustment = Assert.IsType<SqlBinaryExpression>(functionExpression.Arguments[1]);
            Assert.Equal(typeof(long), adjustment.Type);
            Assert.Equal(typeof(long), adjustment.TypeMapping!.ClrType);
            Assert.Equal(typeof(int), adjustment.Left.Type);
            Assert.Same(intMapping, adjustment.Left.TypeMapping);
            Assert.Equal(1L, Assert.IsType<SqlConstantExpression>(adjustment.Right).Value);
            Assert.Equal(typeof(long), adjustment.Right.TypeMapping!.ClrType);
            Assert.Same(intMapping, functionExpression.Arguments[2].TypeMapping);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Result_keeps_source_mapping_and_converters(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var factory = context.GetService<ISqlExpressionFactory>();
            var translator = context.GetService<IMethodCallTranslatorProvider>();
            var logger = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>();
            foreach (var name in new[] { nameof(Row.Required), nameof(Row.Fixed), nameof(Row.National), nameof(Row.Converted) })
            {
                var mapping = (RelationalTypeMapping)context.Model.FindEntityType(typeof(Row))!.FindProperty(name)!.GetTypeMapping();
                foreach (var arity in new[] { 1, 2 })
                {
                    var method = typeof(string).GetMethod(nameof(string.Substring), arity == 1 ? [typeof(int)] : [typeof(int), typeof(int)])!;
                    var arguments = arity == 1 ? new[] { factory.Constant(0) } : new[] { factory.Constant(0), factory.Constant(0) };
                    var result = Assert.IsType<SqlFunctionExpression>(translator.Translate(context.Model,
                        factory.Constant("value", mapping), method, arguments, logger));
                    Assert.Same(mapping, result.TypeMapping);
                    Assert.Same(mapping, result.Arguments![0].TypeMapping);
                    Assert.True(result.IsNullable);
                    Assert.All(result.ArgumentsPropagateNullability!, Assert.False);
                    Assert.True(result.TypeMapping!.Size is null or > 0);
                }
            }
            var converted = context.Rows.Where(row => row.Converted.Substring(0, 1) == "x").ToQueryString();
            Assert.Contains("x!", converted);
            foreach (var family in mode == OceanBaseCompatibilityMode.MySql
                ? new[] { "tinytext", "text", "mediumtext", "longtext" }
                : new[] { "clob", "nclob", " clob ", " nclob " })
            {
                var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(string), storeTypeName: family)!;
                var result = Assert.IsType<SqlFunctionExpression>(translator.Translate(context.Model,
                    factory.Constant("value", mapping), typeof(string).GetMethod(nameof(string.Substring), [typeof(int)])!,
                    [factory.Constant(0)], logger));
                Assert.Same(mapping, result.TypeMapping);
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Binary_converter_and_other_signatures_stay_client_only(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            Assert.Contains("could not be translated", Assert.Throws<InvalidOperationException>(() =>
                context.Rows.Where(row => row.BinaryConverted.Substring(1) == "x").ToQueryString()).Message);
            Assert.Contains("BinaryConverted", context.Rows.Select(row => row.BinaryConverted.Substring(1)).ToQueryString());
            Assert.Contains("could not be translated", Assert.Throws<InvalidOperationException>(() =>
                context.Rows.Where(row => row.Required.Remove(1) == "x").ToQueryString()).Message);
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
            {
                modelBuilder.Entity<Row>().Property(row => row.Required).HasMaxLength(12);
                modelBuilder.Entity<Row>().Property(row => row.Fixed).IsFixedLength().HasMaxLength(8);
                modelBuilder.Entity<Row>().Property(row => row.National).IsUnicode().HasMaxLength(8);
                modelBuilder.Entity<Row>().Property(row => row.Converted)
                    .HasConversion(value => value + "!", value => value.Substring(0, value.Length - 1)).HasMaxLength(16);
                modelBuilder.Entity<Row>().Property(row => row.BinaryConverted)
                    .HasConversion(value => Encoding.UTF8.GetBytes(value), value => Encoding.UTF8.GetString(value));
            }
        }

        private sealed class Row
        {
            public int Id { get; set; }
            public string Required { get; set; } = "";
            public string? Optional { get; set; }
            public string Fixed { get; set; } = "";
            public string National { get; set; } = "";
            public string Converted { get; set; } = "";
            public string BinaryConverted { get; set; } = "";
            public int Start { get; set; }
            public int Length { get; set; }
        }
    }
}
