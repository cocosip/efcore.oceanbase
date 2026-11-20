using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests
{
    public sealed class CorrelatedQueryTests
    {
        public static IEnumerable<object[]> Shapes()
        {
            foreach (var mode in Enum.GetValues<OceanBaseCompatibilityMode>())
                foreach (var outer in new[] { false, true })
                    foreach (var smallPage in new[] { false, true })
                        yield return [mode, outer, smallPage];
        }

        [Theory]
        [MemberData(nameof(Shapes))]
        public void Correlated_sources_use_lateral_joins_and_keep_outer_references(OceanBaseCompatibilityMode mode, bool outer, bool smallPage)
        {
            using var context = Create(mode);
            var rows = context.Set<Row>();
            var query = rows.SelectMany(parent => outer
                ? rows.Where(child => child.Id > parent.Id).OrderBy(child => child.Id).Take(smallPage ? 2 : 100)
                    .Select(child => new { Parent = parent.Id, Child = (int?)child.Id, Sum = parent.Value + child.Value }).DefaultIfEmpty()
                : rows.Where(child => child.Id > parent.Id).OrderBy(child => child.Id).Take(smallPage ? 2 : 100)
                    .Select(child => new { Parent = parent.Id, Child = (int?)child.Id, Sum = parent.Value + child.Value }));
            var sql = Flat(query.ToQueryString());
            Assert.True(sql.Contains("JOIN LATERAL (", StringComparison.Ordinal), sql);
            if (outer) Assert.Contains("LEFT JOIN (", sql);
            Assert.DoesNotContain("APPLY", sql);
            Assert.Contains("c0.RowKey > c.RowKey", sql);
            Assert.Contains("c.Value + c0.Value", sql);
            Assert.Contains("ORDER BY c0.RowKey", sql);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "LIMIT " : "FETCH FIRST ", sql);
            Assert.Matches(@"\) \w+ ON 1 = 1", sql);
            Assert.StartsWith("SELECT", sql);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Lifted_inner_projection_and_scalar_default_source_keep_their_join_contract(OceanBaseCompatibilityMode mode, bool outer)
        {
            using var context = Create(mode);
            var rows = context.Set<Row>();
            var query = rows.SelectMany(parent => outer
                ? rows.Select(child => parent.Value + child.Value).DefaultIfEmpty()
                : rows.Select(child => parent.Value + child.Value));
            var sql = Flat(query.ToQueryString());
            Assert.Contains(outer ? "LEFT JOIN (" : "CROSS JOIN CorrelationRows c0", sql);
            Assert.Contains("c.Value + c0.Value", sql);
            Assert.Equal(outer, sql.Contains("LATERAL", StringComparison.Ordinal));
            Assert.DoesNotContain("APPLY", sql);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Nested_lateral_sources_keep_distinct_correlation_scopes(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var rows = context.Set<Row>();
            var query = rows.SelectMany(parent => rows.Where(child => child.Id > parent.Id).Take(2)
                .SelectMany(child => rows.Where(leaf => leaf.Id > child.Id && leaf.Value == parent.Value).Take(3)
                    .Select(leaf => new { Parent = parent.Id, Child = child.Id, Leaf = leaf.Id })));
            var sql = Flat(query.ToQueryString());
            Assert.Equal(2, Regex.Matches(sql, "JOIN LATERAL").Count);
            Assert.Equal(2, Regex.Matches(sql, "ON 1 = 1").Count);
            Assert.DoesNotContain("APPLY", sql);
            Assert.Contains("c0.RowKey > c.RowKey", sql);
            Assert.Contains("c1.RowKey > c2.RowKey", sql);
            Assert.Contains("c1.Value = c.Value", sql);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Decorrelated_equality_and_scalar_subqueries_do_not_gain_lateral_joins(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var rows = context.Set<Row>();
            var join = rows.SelectMany(parent => rows.Where(child => child.Value == parent.Id), (parent, child) => child.Id);
            var scalar = rows.Select(parent => rows.Where(child => child.Value == parent.Id).Select(child => child.Id).FirstOrDefault());
            Assert.Contains("INNER JOIN", join.ToQueryString());
            Assert.DoesNotContain("LATERAL", join.ToQueryString());
            Assert.DoesNotContain("LATERAL", scalar.ToQueryString());
        }

        private static string Flat(string sql) => Regex.Replace(sql.Replace("`", "", StringComparison.Ordinal)
            .Replace("\"", "", StringComparison.Ordinal).Replace(" AS ", " ", StringComparison.Ordinal), @"\s+", " ").Trim();
        private static Context Create(OceanBaseCompatibilityMode mode) => new(new DbContextOptionsBuilder<Context>()
            .UseOceanBase("Server=offline;Database=app", mode).Options);
        private sealed class Context(DbContextOptions<Context> options) : DbContext(options)
        {
            protected override void OnModelCreating(ModelBuilder model)
            {
                model.Entity<Row>().ToTable("CorrelationRows");
                model.Entity<Row>().Property(r => r.Id).HasColumnName("RowKey");
            }
        }
        private sealed class Row { public int Id { get; set; } public int? Value { get; set; } }
    }
}
