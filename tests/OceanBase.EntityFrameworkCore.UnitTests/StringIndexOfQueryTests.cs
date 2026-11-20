using System;
using System.Linq;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests
{
    public sealed class StringIndexOfQueryTests
    {
        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Six_signatures_translate_ranges_composition_and_nullable_results(OceanBaseCompatibilityMode mode, bool relationalNulls)
        {
            using var context = CreateContext(mode, relationalNulls);
            string? needle = "x";
            var character = 'x';
            IQueryable[] queries =
            [
                context.Rows.Where(row => row.Text!.IndexOf("x") >= 0),
                context.Rows.Where(row => row.Text!.IndexOf(needle!, row.Start) >= 0),
                context.Rows.Where(row => row.Text!.IndexOf(row.Needle!, row.Start, row.Count) >= 0),
                context.Rows.Where(row => row.Text!.IndexOf('x') >= 0),
                context.Rows.Where(row => row.Text!.IndexOf(character, row.Start) >= 0),
                context.Rows.Where(row => row.Text!.IndexOf(row.Character!.Value, row.Start, row.Count) >= 0),
                context.Rows.Select(row => (int?)row.Text!.IndexOf(needle!, row.Start, row.Count)).Where(index => index == null),
                context.Rows.Where(row => row.Text!.Substring(1).ToUpper().IndexOf(needle!) == 0)
            ];
            foreach (var query in queries)
            {
                var sql = query.ToQueryString();
                Assert.Contains("INSTR(", sql);
                Assert.Contains("CASE", sql);
                Assert.DoesNotContain("__oceanbase_internal", sql);
            }
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(queries[0].ToQueryString(), "INSTR\\("));
            Assert.Contains(" IS NULL", queries[6].ToQueryString());
            needle = "";
            var empty = queries[1].ToQueryString();
            Assert.Contains("Start", empty);
            if (mode == OceanBaseCompatibilityMode.Oracle) Assert.DoesNotContain("INSTR(", empty);
            needle = null;
            Assert.Contains("NULL", queries[1].ToQueryString());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Unsupported_comparisons_and_nontext_carriers_reject(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            Assert.Throws<InvalidOperationException>(() => context.Rows.Where(row => row.Text!.IndexOf("x", StringComparison.Ordinal) >= 0).ToQueryString());
            Assert.Throws<InvalidOperationException>(() => context.Rows.Where(row => row.Binary.IndexOf("x") >= 0).ToQueryString());
            Assert.Throws<InvalidOperationException>(() => context.Rows.Where(row => row.Text!.IndexOf(row.NumericCharacter) >= 0).ToQueryString());
            Assert.Contains("Text", context.Rows.Select(row => row.Text!.IndexOf("x", StringComparison.Ordinal)).ToQueryString());
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
                model.Entity<Row>().Property(row => row.Text).IsUnicode(false).HasMaxLength(32);
                model.Entity<Row>().Property(row => row.Needle).IsUnicode(false).HasMaxLength(16);
                model.Entity<Row>().Property(row => row.Character).HasConversion<string>().IsUnicode(false).HasMaxLength(1);
                model.Entity<Row>().Property(row => row.NumericCharacter).HasConversion<int>();
                model.Entity<Row>().Property(row => row.Binary).HasConversion(value => Encoding.UTF8.GetBytes(value), value => Encoding.UTF8.GetString(value));
            }
        }

        private sealed class Row
        {
            public int Id { get; set; }
            public string? Text { get; set; }
            public string? Needle { get; set; }
            public char? Character { get; set; }
            public char NumericCharacter { get; set; }
            public string Binary { get; set; } = "";
            public int Start { get; set; }
            public int Count { get; set; }
        }
    }
}
