using System;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBase.EntityFrameworkCore.Query;
using OceanBase.EntityFrameworkCore;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests
{
    public sealed class StringSearchQueryTests
    {
        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Search_methods_translate_as_total_native_boolean_expressions(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var sql = context.Rows.Where(row => row.Optional!.Contains("a%_ '\\")
                && row.Required.StartsWith("abc")
                && row.Optional!.EndsWith("long repeated needle long repeated needle")).ToQueryString();
            Assert.Contains("INSTR(", sql);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "RIGHT(" : "SUBSTR(", sql);
            Assert.Contains("CASE", sql);
            Assert.Contains("ELSE", sql);
            Assert.DoesNotContain("LIKE", sql);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Nullable_column_needles_and_negation_keep_total_case_guards(OceanBaseCompatibilityMode mode, bool relationalNulls)
        {
            using var context = CreateContext(mode, relationalNulls);
            var queries = new IQueryable[]
            {
                context.Rows.Where(row => row.Optional!.Contains(row.Required)),
                context.Rows.Where(row => !row.Required.StartsWith(row.Optional!)),
                context.Rows.Where(row => row.Optional!.EndsWith(row.Optional!)),
                context.Rows.Select(row => new { row.Id, Match = row.Optional!.EndsWith(row.Required) })
                    .Where(row => row.Match == false)
            };
            foreach (var query in queries)
            {
                var sql = query.ToQueryString();
                Assert.Contains("CASE", sql);
                Assert.Contains("ELSE", sql);
                Assert.Contains("IS NOT NULL", sql);
                Assert.DoesNotContain("__oceanbase_internal", sql);
            }
            var suffix = queries[2].ToQueryString();
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "RIGHT(" : "SUBSTR(", suffix);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "CHAR_LENGTH(" : "LENGTH(", suffix);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Empty_null_and_changing_captured_needles_preserve_search_policy(OceanBaseCompatibilityMode mode, bool relationalNulls)
        {
            using var context = CreateContext(mode, relationalNulls);
            var empty = context.Rows.Where(row => row.Optional!.Contains("") && row.Required.StartsWith("")
                && row.Optional!.EndsWith("")).ToQueryString();
            Assert.Contains("IS NOT NULL", empty);
            Assert.DoesNotContain("INSTR(", empty);
            Assert.DoesNotContain("RIGHT(", empty);
            Assert.DoesNotContain("SUBSTR(", empty);

            string? needle = "a%_ '\\";
            var query = context.Rows.Where(row => row.Optional!.EndsWith(needle!)
                && !row.Required.Contains(needle!) && row.Required.StartsWith(needle!));
            foreach (var value in new string?[] { needle, "", null, "long long repeated needle", "", null, "a%_ '\\" })
            {
                needle = value;
                var sql = query.ToQueryString();
                Assert.DoesNotContain("__oceanbase_internal", sql);
                Assert.DoesNotContain("LIKE", sql);
                if (mode == OceanBaseCompatibilityMode.Oracle)
                {
                    Assert.Equal(value is { Length: > 0 }, sql.Contains("INSTR(", StringComparison.Ordinal));
                    Assert.Equal(value is { Length: > 0 }, sql.Contains("SUBSTR(", StringComparison.Ordinal));
                }
                else if (value is not null)
                    Assert.Contains("CHAR_LENGTH(", sql);
            }
            var nullLiteral = context.Rows.Where(row => row.Required.Contains((string)null!)
                || row.Optional!.StartsWith((string)null!) || row.Optional!.EndsWith((string)null!)).ToQueryString();
            Assert.DoesNotContain("INSTR(", nullLiteral);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Known_null_needles_are_false_and_negated_true_in_predicates_and_projections(
            OceanBaseCompatibilityMode mode, bool relationalNulls)
        {
            using var context = CreateContext(mode, relationalNulls);
            IQueryable[] falseQueries =
            [
                context.Rows.Where(row => row.Optional!.Contains((string)null!)),
                context.Rows.Where(row => row.Optional!.StartsWith((string)null!)),
                context.Rows.Where(row => row.Optional!.EndsWith((string)null!))
            ];
            IQueryable[] trueQueries =
            [
                context.Rows.Where(row => !row.Optional!.Contains((string)null!)),
                context.Rows.Where(row => !row.Optional!.StartsWith((string)null!)),
                context.Rows.Where(row => !row.Optional!.EndsWith((string)null!))
            ];
            foreach (var query in falseQueries)
            {
                var sql = query.ToQueryString();
                Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "WHERE 0" : "WHERE 0 = 1", sql);
                Assert.DoesNotContain("INSTR(", sql);
            }
            foreach (var query in trueQueries)
            {
                var sql = query.ToQueryString();
                Assert.DoesNotContain("WHERE", sql);
                Assert.DoesNotContain("INSTR(", sql);
            }
            var falseProjection = context.Rows.Select(row => new
            {
                Contains = row.Optional!.Contains((string)null!),
                Starts = row.Optional!.StartsWith((string)null!),
                Ends = row.Optional!.EndsWith((string)null!)
            }).Distinct().ToQueryString();
            var trueProjection = context.Rows.Select(row => new
            {
                Contains = !row.Optional!.Contains((string)null!),
                Starts = !row.Optional!.StartsWith((string)null!),
                Ends = !row.Optional!.EndsWith((string)null!)
            }).Distinct().ToQueryString();
            Assert.Contains("SELECT DISTINCT 0", falseProjection);
            Assert.Contains("SELECT DISTINCT 1", trueProjection);
            foreach (var name in new[] { "Contains", "Starts", "Ends" })
            {
                Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? $"0 AS `{name}`" : $"0 \"{name}\"", falseProjection);
                Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? $"1 AS `{name}`" : $"1 \"{name}\"", trueProjection);
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Nullable_suffix_guards_source_and_needle_before_equality(OceanBaseCompatibilityMode mode, bool relationalNulls)
        {
            using var context = CreateContext(mode, relationalNulls);
            var sql = context.Rows.Where(row => row.Optional!.EndsWith(row.Needle!)).ToQueryString();
            var quote = mode == OceanBaseCompatibilityMode.MySql ? '`' : '"';
            Assert.Contains($"{quote}Optional{quote} IS NOT NULL", sql);
            Assert.Contains($"{quote}Needle{quote} IS NOT NULL", sql);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "RIGHT(" : "SUBSTR(", sql);
            Assert.Contains("CASE", sql);
            Assert.Contains("ELSE", sql);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Composed_search_and_converter_keep_native_mapping_and_mode_isolation(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var sql = context.Rows.Where(row => (row.Id > 0 ? row.Optional : row.Required)!.ToUpper()
                .Contains(row.Required + "x") && (row.Optional ?? row.Required).StartsWith("abc")
                && row.Converted.EndsWith("tail")).ToQueryString();
            Assert.Contains("CASE", sql);
            Assert.Contains("UPPER(", sql);
            Assert.Contains("COALESCE(", sql);
            Assert.Contains("INSTR(", sql);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "RIGHT(" : "SUBSTR(", sql);
            Assert.Contains("tail!", sql);
            var convertedEmpty = context.Rows.Where(row => row.Converted.Contains("")
                && row.Converted.StartsWith("") && row.Converted.EndsWith("")).ToQueryString();
            Assert.Contains("INSTR(", convertedEmpty);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "RIGHT(" : "SUBSTR(", convertedEmpty);
            Assert.Contains("!", convertedEmpty);
            using var opposite = CreateContext(mode == OceanBaseCompatibilityMode.MySql
                ? OceanBaseCompatibilityMode.Oracle : OceanBaseCompatibilityMode.MySql);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "SUBSTR(" : "RIGHT(",
                opposite.Rows.Where(row => row.Required.EndsWith("tail")).ToQueryString());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Unsupported_overloads_and_binary_converter_remain_client_only(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            IQueryable[] unsupported =
            [
                context.Rows.Where(row => row.Required.Contains('x')),
                context.Rows.Where(row => row.Required.StartsWith("x", StringComparison.Ordinal)),
                context.Rows.Where(row => row.Required.EndsWith('x')),
                context.Rows.Where(row => row.BinaryConverted.Contains("x"))
            ];
            foreach (var query in unsupported)
                Assert.Contains("could not be translated", Assert.Throws<InvalidOperationException>(() => query.ToQueryString()).Message);
            Assert.Contains("Required", context.Rows.Select(row => row.Required.Contains('x')).ToQueryString());
            Assert.Contains("Required", context.Rows.Select(row => row.Required.EndsWith("x", StringComparison.Ordinal)).ToQueryString());
            var nativeSql = context.Rows.Where(row => NativeInstr(row.Required, row.Optional!) > 0).ToQueryString();
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "`INSTR`(" : "\"INSTR\"(", nativeSql);
            Assert.DoesNotContain("__oceanbase_internal", nativeSql);
        }

        private static int NativeInstr(string source, string needle) => throw new NotSupportedException();

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Oracle_computed_null_needle_keeps_total_search_policy(bool relationalNulls)
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.Oracle, relationalNulls);
            IQueryable[] queries =
            [
                context.Rows.Where(row => row.Required.Contains(row.Id > 0 ? (string)null! : row.Needle!)),
                context.Rows.Where(row => !row.Required.StartsWith((row.Id > 0 ? (string)null! : row.Needle!) ?? (string)null!)),
                context.Rows.Select(row => row.Required.EndsWith(row.Id > 0 ? (string)null! : row.Needle!)).Distinct()
            ];
            foreach (var query in queries)
            {
                var sql = query.ToQueryString();
                Assert.Contains("CASE", sql);
                Assert.Contains(" IS NOT NULL", sql);
                Assert.DoesNotContain("__oceanbase_internal", sql);
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Unrelated_unmapped_instance_method_returns_no_translation(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var factory = context.GetService<ISqlExpressionFactory>();
            var translator = new OceanBaseStringTranslator(factory, context.GetService<IRelationalTypeMappingSource>(), mode);
            var result = translator.Translate(factory.Constant(new object()),
                typeof(object).GetMethod(nameof(object.ToString), Type.EmptyTypes)!, [],
                context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>());
            Assert.Null(result);
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
                modelBuilder.Entity<Row>().Property(row => row.Converted)
                    .HasConversion(value => value + "!", value => value.Substring(0, value.Length - 1));
                modelBuilder.Entity<Row>().Property(row => row.BinaryConverted)
                    .HasConversion(value => Encoding.UTF8.GetBytes(value), value => Encoding.UTF8.GetString(value));
                modelBuilder.HasDbFunction(typeof(StringSearchQueryTests).GetMethod(nameof(NativeInstr),
                    BindingFlags.NonPublic | BindingFlags.Static)!).HasName("INSTR");
            }
        }

        private sealed class Row
        {
            public int Id { get; set; }
            public string Required { get; set; } = "";
            public string? Optional { get; set; }
            public string? Needle { get; set; }
            public string Converted { get; set; } = "";
            public string BinaryConverted { get; set; } = "";
        }
    }
}
