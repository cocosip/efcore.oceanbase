using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests
{
    public sealed class GroupingQueryTests
    {
        public static IEnumerable<object[]> Cases()
        {
            foreach (var mode in Enum.GetValues<OceanBaseCompatibilityMode>())
                foreach (var relationalNulls in new[] { false, true })
                    foreach (var resultSelector in new[] { false, true })
                        yield return [mode, relationalNulls, resultSelector];
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void Nullable_group_key_matches_its_null_group_element(
            OceanBaseCompatibilityMode mode, bool relationalNulls, bool resultSelector)
        {
            using var context = Create(mode, relationalNulls);
            var rows = context.Set<Row>();
            var query = resultSelector
                ? rows.GroupBy(r => r.Key, (key, group) => new { Key = key, Count = group.Count(), First = group.OrderBy(r => r.Id).FirstOrDefault() })
                : rows.GroupBy(r => r.Key).Select(group => new { group.Key, Count = group.Count(), First = group.OrderBy(r => r.Id).FirstOrDefault() });
            var sql = Flat(query.ToQueryString());
            Assert.Matches(@"\w+\.Key IS NULL AND \w+\.Key IS NULL", sql);
            Assert.Contains("COUNT(*)", sql);
            Assert.Contains("GROUP BY", sql);
            Assert.Contains("ORDER BY", sql);
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void Ordinary_nullable_join_keeps_sql_equality_and_nonnullable_group_needs_no_compensation(
            OceanBaseCompatibilityMode mode, bool relationalNulls, bool resultSelector)
        {
            using var context = Create(mode, relationalNulls);
            var rows = context.Set<Row>();
            var join = Flat(rows.Join(rows, left => left.Key, right => right.Key, (left, right) => left.Id).ToQueryString());
            Assert.Contains("INNER JOIN", join);
            Assert.DoesNotContain("IS NULL", join);
            Assert.DoesNotContain("LATERAL", join);
            var query = resultSelector
                ? rows.GroupBy(r => r.Tag, (key, g) => new { Count = g.Count(), First = g.OrderBy(r => r.Id).FirstOrDefault() })
                : rows.GroupBy(r => r.Tag).Select(g => new { Count = g.Count(), First = g.OrderBy(r => r.Id).FirstOrDefault() });
            var group = Flat(query.ToQueryString());
            Assert.DoesNotContain("IS NULL", group);
            Assert.DoesNotContain("LATERAL", group);
        }

        private static string Flat(string sql) => Regex.Replace(sql.Replace("`", "", StringComparison.Ordinal)
            .Replace("\"", "", StringComparison.Ordinal), @"\s+", " ");

        [Theory]
        [MemberData(nameof(Cases))]
        public void Composite_computed_and_element_selector_keys_preserve_group_null_matching(
            OceanBaseCompatibilityMode mode, bool relationalNulls, bool resultSelector)
        {
            using var context = Create(mode, relationalNulls);
            var rows = context.Set<Row>();
            var computed = resultSelector
                ? rows.GroupBy(r => r.Key + 1, (key, group) => new { Key = key, Count = group.Count(), First = group.OrderBy(r => r.Id).FirstOrDefault() })
                : rows.GroupBy(r => r.Key + 1).Select(group => new { group.Key, Count = group.Count(), First = group.OrderBy(r => r.Id).FirstOrDefault() });
            var composite = rows.GroupBy(r => new { r.Key, r.Tag })
                .Select(g => new { g.Key, Count = g.Count(), First = g.OrderBy(r => r.Id).FirstOrDefault() });
            var element = rows.GroupBy(r => r.Key, r => r.Tag,
                (key, group) => new { Key = key, Count = group.Count(), First = group.OrderBy(v => v).FirstOrDefault() });
            foreach (var sql in new[] { computed.ToQueryString(), composite.ToQueryString(), element.ToQueryString() }.Select(Flat))
            {
                Assert.Contains("IS NULL AND", sql);
                Assert.Contains("COUNT(*)", sql);
                Assert.DoesNotContain("APPLY", sql);
            }
        }

        private static Context Create(OceanBaseCompatibilityMode mode, bool relationalNulls)
        {
            var builder = new DbContextOptionsBuilder<Context>().UseOceanBase("Server=offline;Database=app", mode);
            ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(
                builder.Options.Extensions.OfType<RelationalOptionsExtension>().Single().WithUseRelationalNulls(relationalNulls));
            return new Context(builder.Options);
        }

        private sealed class Context(DbContextOptions<Context> options) : DbContext(options)
        {
            protected override void OnModelCreating(ModelBuilder model) => model.Entity<Row>().ToTable("GroupingRows");
        }

        private sealed class Row
        {
            public int Id { get; set; }
            public int? Key { get; set; }
            public int Tag { get; set; }
        }
    }
}
