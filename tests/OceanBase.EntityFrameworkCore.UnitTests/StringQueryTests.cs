using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests
{
    public sealed class StringQueryTests
    {
        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Fixed_string_concat_and_addition_use_native_syntax(OceanBaseCompatibilityMode mode, bool relationalNulls)
        {
            using var context = CreateContext(mode, relationalNulls);
            var suffix = "海'_%\\";
            IQueryable[] queries =
            [
                context.Rows.Select(row => row.Left + row.Right),
                context.Rows.Select(row => string.Concat(row.Left, suffix)),
                context.Rows.Select(row => string.Concat(row.Left, "-", row.Right)),
                context.Rows.Select(row => string.Concat(row.Left, "-", row.Right, suffix)),
                context.Rows.Where(row => (row.Left + (row.Right + suffix)) == "target"),
                context.Rows.OrderBy(row => row.Left + suffix).Select(row => row.Id)
            ];
            foreach (var query in queries)
            {
                var sql = query.ToQueryString();
                Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "CONCAT(" : " || ", sql);
                Assert.DoesNotContain(" + ", sql);
                if (mode == OceanBaseCompatibilityMode.MySql)
                {
                    Assert.Contains("COALESCE(", sql);
                }
            }

            Assert.Contains(" + 2", context.Rows.Select(row => row.Id + 2).ToQueryString());
            using var opposite = CreateContext(mode == OceanBaseCompatibilityMode.MySql
                ? OceanBaseCompatibilityMode.Oracle : OceanBaseCompatibilityMode.MySql, relationalNulls);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? " || " : "CONCAT(",
                opposite.Rows.Select(row => row.Left + row.Right).ToQueryString());
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "CONCAT(" : " || ",
                context.Rows.Select(row => row.Left + row.Right).ToQueryString());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Oracle_concat_null_tests_examine_the_result_instead_of_operand_OR(bool relationalNulls)
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.Oracle, relationalNulls);
            IQueryable[] queries =
            [
                context.Rows.Where(row => row.Left + row.Right == null),
                context.Rows.Where(row => row.Left + row.Right != null),
                context.Rows.Where(row => string.Concat(row.Left, row.Right, row.Left) == null),
                context.Rows.Where(row => (row.Left + "") == null),
                context.Rows.Where(row => (row.Left + row.Right ?? "") == null),
                context.Rows.Select(row => row.Left + row.Right == null),
                context.Rows.Select(row => !(row.Left + row.Right != null))
            ];
            foreach (var query in queries)
            {
                var sql = query.ToQueryString();
                Assert.Contains(" || ", sql);
                Assert.Contains(" IS ", sql);
                Assert.DoesNotContain(" OR ", sql);
                Assert.DoesNotContain("COALESCE(", sql);
            }

            var fallback = context.Rows.Where(row => (row.Left + row.Right ?? "fallback") == null).ToQueryString();
            Assert.Contains("WHERE 0 = 1", fallback);
            var projection = context.Rows.Select(row => (row.Left + row.Right) ?? "fallback").ToQueryString();
            Assert.Contains("COALESCE(", projection);
            Assert.Contains("'fallback'", projection);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Oracle_captured_empty_and_null_strings_are_native_NULL_without_stale_cache(bool relationalNulls)
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.Oracle, relationalNulls);
            string? value = "present";
            var query = context.Rows.Where(row => (row.Left + row.Right ?? value) == null);
            Assert.Contains("WHERE 0 = 1", query.ToQueryString());
            foreach (var empty in new string?[] { "", null, "", null })
            {
                value = empty;
                var sql = query.ToQueryString();
                Assert.Contains(" || ", sql);
                Assert.Contains(" IS NULL", sql);
                Assert.DoesNotContain("WHERE 0 = 1", sql);
                Assert.DoesNotContain("COALESCE(", sql);
            }
            value = "again";
            Assert.Contains("WHERE 0 = 1", query.ToQueryString());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Oracle_concat_with_required_or_one_nullable_operand_preserves_native_null_tests(bool relationalNulls)
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.Oracle, relationalNulls);
            var nullable = context.Rows.Where(row => row.Left + (string?)null == null).ToQueryString();
            Assert.Contains("(\"r\".\"Left\" || NULL) IS NULL", nullable);
            Assert.DoesNotContain(" OR ", nullable);
            var required = context.Rows.Where(row => row.Required + row.Left == null).ToQueryString();
            Assert.DoesNotContain("WHERE 0 = 1", required);
            Assert.Contains(" || ", required);
            Assert.Contains("IS NULL", required);
            var nested = context.Rows.Select(row => (row.Left ?? "") + (row.Right ?? "") ?? "fallback").ToQueryString();
            Assert.Contains("COALESCE((", nested);
            Assert.Contains(" || ", nested);
            Assert.Contains("N'fallback'", nested);
            Assert.DoesNotContain("N''", nested);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Oracle_concat_comparisons_with_empty_literals_and_parameters_use_native_null_compensation(bool relationalNulls)
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.Oracle, relationalNulls);
            var literal = context.Rows.Where(row => row.Left + row.Right == "").ToQueryString();
            Assert.Contains(" || ", literal);
            Assert.Contains("IS NULL", literal);
            string? target = "target";
            var query = context.Rows.Where(row => row.Left + row.Right != target);
            foreach (var value in new string?[] { "target", "", null, "again" })
            {
                target = value;
                var sql = query.ToQueryString();
                Assert.Contains(" || ", sql);
                if (string.IsNullOrEmpty(value))
                {
                    Assert.Contains("IS NOT NULL", sql);
                    Assert.DoesNotContain("<>", sql);
                }
                else
                {
                    Assert.Contains("<>", sql);
                    Assert.Equal(!relationalNulls, sql.Contains("IS NULL", StringComparison.Ordinal));
                }
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Oracle_string_equality_normalizes_provider_empty_values_including_converters(bool relationalNulls)
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.Oracle, relationalNulls);
            Assert.Contains("\"Left\" IS NULL", context.Rows.Where(row => row.Left == "").ToQueryString());
            Assert.Contains("N'!'", context.Rows.Where(row => row.Converted == "").ToQueryString());
            Assert.Contains("\"EmptyConverted\" IS NULL", context.Rows.Where(row => row.EmptyConverted == "empty").ToQueryString());
            Assert.Contains("\"NullOutput\" IS NULL", context.Rows.Where(row => row.NullOutput == "null-output").ToQueryString());
            Assert.Contains("\"State\" IS NULL", context.Rows.Where(row => row.State == State.Empty).ToQueryString());
            Assert.Contains("\"Token\" IS NULL", context.Rows.Where(row => row.Token == Guid.Empty).ToQueryString());
            string? value = "present";
            var equality = context.Rows.Where(row => row.Left == value);
            var converted = context.Rows.Where(row => row.Converted == value);
            var emptyConverted = context.Rows.Where(row => row.EmptyConverted == value);
            var nullOutput = context.Rows.Where(row => row.NullOutput == value);
            foreach (var candidate in new string?[] { "present", "", null, "empty", "null-output", "again" })
            {
                value = candidate;
                var sql = equality.ToQueryString();
                Assert.Equal(string.IsNullOrEmpty(value), sql.Contains("\"Left\" IS NULL", StringComparison.Ordinal));
                Assert.Equal(value is null, converted.ToQueryString().Contains("\"Converted\" IS NULL", StringComparison.Ordinal));
                Assert.Equal(value is null or "" or "empty",
                    emptyConverted.ToQueryString().Contains("\"EmptyConverted\" IS NULL", StringComparison.Ordinal));
                Assert.Equal(value is null or "" or "null-output",
                    nullOutput.ToQueryString().Contains("\"NullOutput\" IS NULL", StringComparison.Ordinal));
            }
            var state = State.Present;
            var stateQuery = context.Rows.Where(row => row.State == state);
            var token = Guid.NewGuid();
            var tokenQuery = context.Rows.Where(row => row.Token == token);
            Assert.DoesNotContain("\"State\" IS NULL", stateQuery.ToQueryString());
            Assert.DoesNotContain("\"Token\" IS NULL", tokenQuery.ToQueryString());
            state = State.Empty;
            token = Guid.Empty;
            Assert.Contains("\"State\" IS NULL", stateQuery.ToQueryString());
            Assert.Contains("\"Token\" IS NULL", tokenQuery.ToQueryString());
        }

        [Fact]
        public void Oracle_rejects_null_converting_provider_string_query_values_precisely()
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.Oracle);
            var error = Assert.Throws<InvalidOperationException>(() => context.Rows
                .Where(row => row.NullConverted == "").ToQueryString());
            Assert.Contains("null-converting string value converters", error.Message);
            var value = "";
            error = Assert.Throws<InvalidOperationException>(() => context.Rows
                .Where(row => row.NullConverted == value).ToQueryString());
            Assert.Contains("null-converting string value converters", error.Message);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Equality_and_concat_comparisons_keep_relational_null_contract(OceanBaseCompatibilityMode mode, bool relationalNulls)
        {
            using var context = CreateContext(mode, relationalNulls);
            foreach (var query in new[]
            {
                context.Rows.Where(row => row.Left == row.Right),
                context.Rows.Where(row => row.Left != row.Right),
                context.Rows.Where(row => row.Left!.Equals(row.Right)),
                context.Rows.Where(row => string.Equals(row.Left, row.Right))
            })
            {
                var sql = query.ToQueryString();
                Assert.Contains("WHERE", sql);
                Assert.Equal(!relationalNulls, sql.Contains("IS NULL", StringComparison.Ordinal));
            }

            var comparison = context.Rows.Where(row => !(row.Left + row.Right == row.Right)).ToQueryString();
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "CONCAT(" : " || ", comparison);
            Assert.Equal(!relationalNulls, comparison.Contains("IS NULL", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Unsupported_predicates_fail_while_top_level_client_projection_is_allowed(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            IQueryable[] unsupported =
            [
                context.Rows.Where(row => string.Equals(row.Left, row.Right, StringComparison.Ordinal)),
                context.Rows.Where(row => row.Left!.Equals(row.Right, StringComparison.OrdinalIgnoreCase)),
                context.Rows.Where(row => Custom(row.Left) == "x"),
                context.Rows.Where(row => string.Concat(new[] { row.Left, row.Right }) == "x"),
                context.Rows.Where(row => string.Concat((IEnumerable<string?>)new[] { row.Left, row.Right }) == "x"),
                context.Rows.Where(row => string.Concat((object?)row.Left, row.Id) == "x")
            ];
            foreach (var query in unsupported)
            {
                Assert.Contains("could not be translated", Assert.Throws<InvalidOperationException>(() => query.ToQueryString()).Message);
            }
            Assert.Contains("Left", context.Rows.Select(row => Custom(row.Left)).ToQueryString());
        }

        private static string Custom(string? value) => value + "!";

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
                modelBuilder.Entity<Row>().Property(row => row.Converted).HasConversion(value => value + "!", value => value);
                modelBuilder.Entity<Row>().Property(row => row.EmptyConverted)
                    .HasConversion(value => value == "empty" ? "" : value, value => value);
                modelBuilder.Entity<Row>().Property(row => row.NullOutput)
                    .HasConversion(value => value == "null-output" ? null : value, value => value);
                modelBuilder.Entity<Row>().Property(row => row.State)
                    .HasConversion(value => value == State.Empty ? "" : "present", value => State.Present);
                modelBuilder.Entity<Row>().Property(row => row.Token)
                    .HasConversion(value => value == Guid.Empty ? "" : value.ToString(), value => Guid.Parse(value!));
                modelBuilder.Entity<Row>().Property(row => row.NullConverted).HasConversion(
                    new ValueConverter<string?, string?>(value => value ?? "sentinel", value => value, convertsNulls: true));
            }
        }

        private sealed class Row
        {
            public int Id { get; set; }
            public string? Left { get; set; }
            public string? Right { get; set; }
            public string Required { get; set; } = string.Empty;
            public string? Converted { get; set; }
            public string? EmptyConverted { get; set; }
            public State? State { get; set; }
            public Guid? Token { get; set; }
            public string? NullConverted { get; set; }
            public string? NullOutput { get; set; }
        }

        private enum State { Empty, Present }
    }
}
