using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests
{
    public sealed class JsonValueQueryTests
    {
        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Json_scalar_path_translates_in_predicates_projections_and_null_checks(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var path = "$.name";
            var sql = context.Rows.Where(row => EF.Functions.OceanBaseJsonValue(row.Document, path) == "Ada")
                .Select(row => EF.Functions.OceanBaseJsonValue(row.Document, "$.items[0].name")).ToQueryString();
            Assert.Contains("JSON_VALUE(", sql);
            Assert.Contains("'$.name'", sql);
            Assert.Contains("'$.items[0].name'", sql);
            Assert.DoesNotContain("@__path", sql);
            Assert.DoesNotContain(":__path", sql);
            var missing = context.Rows.Where(row => EF.Functions.OceanBaseJsonValue(row.Document, "$.missing") == null)
                .ToQueryString();
            Assert.Contains("JSON_VALUE(", missing);
            Assert.Contains("IS NULL", missing);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Json_paths_reject_dynamic_columns_and_empty_paths(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            Assert.Throws<InvalidOperationException>(() => context.Rows
                .Where(row => EF.Functions.OceanBaseJsonValue(row.Document, row.Path) == "Ada").ToQueryString());
            Assert.Throws<ArgumentException>(() => context.Rows
                .Where(row => EF.Functions.OceanBaseJsonValue(row.Document, "") == "Ada").ToQueryString());
        }

        [Fact]
        public void Json_value_has_no_client_evaluation()
            => Assert.Throws<InvalidOperationException>(() => EF.Functions.OceanBaseJsonValue("{}", "$.name"));

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Json_paths_quote_SQL_metacharacters_as_a_single_literal(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var path = "$.\"key' OR 1=1 --\"";
            var query = context.Rows.Where(row => EF.Functions.OceanBaseJsonValue(row.Document, path) == "safe");
            Assert.Contains("key'' OR 1=1 --", query.ToQueryString());
            path = "$.name";
            var refreshed = query.ToQueryString();
            Assert.Contains("'$.name'", refreshed);
            Assert.DoesNotContain("key''", refreshed);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Json_value_constant_documents_execute_on_the_server(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            Assert.Contains("JSON_VALUE(", context.Rows.Select(row =>
                EF.Functions.OceanBaseJsonValue("{\"name\":\"Ada\"}", "$.name")).ToQueryString());
        }

        private static Context CreateContext(OceanBaseCompatibilityMode mode)
            => new(new DbContextOptionsBuilder<Context>().UseOceanBase("Server=localhost;Database=test", mode)
                .EnableServiceProviderCaching(false).Options);

        private sealed class Context(DbContextOptions<Context> options) : DbContext(options)
        {
            public DbSet<Row> Rows => Set<Row>();
            protected override void OnModelCreating(ModelBuilder modelBuilder)
                => modelBuilder.Entity<Row>().Property(row => row.Document).HasColumnType("json");
        }

        private sealed class Row
        {
            public int Id { get; set; }
            public string? Document { get; set; }
            public string Path { get; set; } = "";
        }
    }
}
