using System;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OceanBase.EntityFrameworkCore;
using Xunit;

namespace OceanBase.EntityFrameworkCore.LiveTests
{
    public sealed class ReadOnlyQueryProfileTests
    {
        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Offline_profile_uses_typed_inline_rows_and_the_requested_query_dialect(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var seed = ReadOnlyQueryProfile.Seed(mode);
            Assert.Equal(4, Regex.Matches(seed, @"\bSELECT\b").Count);
            Assert.Equal(3, Regex.Matches(seed, "UNION ALL").Count);
            Assert.Equal(mode == OceanBaseCompatibilityMode.Oracle ? 4 : 0, Regex.Matches(seed, "FROM DUAL").Count);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "CAST(NULL AS SIGNED)" : "CAST(NULL AS NUMBER(10))", seed);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "CAST(NULL AS CHAR(16))" : "CAST(NULL AS VARCHAR2(16))", seed);
            Assert.DoesNotContain(";", seed);
            var rows = ReadOnlyQueryProfile.Rows(context, mode);
            var sql = rows.OrderBy(r => r.Value ?? 99).ThenBy(r => r.Id).Skip(1).Take(2).Select(r => r.Id).ToQueryString();
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "LIMIT" : "FETCH NEXT", sql);
            Assert.Contains("OFFSET", sql);
            Assert.Contains("COALESCE", sql);
            Assert.Contains("ORDER BY", sql);
            var groups = rows.GroupBy(r => r.Value).Where(g => g.Count() > 1).Select(g => g.Key).ToQueryString();
            Assert.Contains("GROUP BY", groups);
            Assert.Contains("HAVING COUNT(*) > 1", groups);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Offline_profile_translates_all_set_queries_and_nested_difference(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var rows = ReadOnlyQueryProfile.Rows(context, mode);
            var left = rows.Where(r => r.Id <= 3).Select(r => r.Value);
            var right = rows.Where(r => r.Id >= 3).Select(r => r.Value);
            foreach (var operation in new[] { "Union", "Concat", "Intersect", "Except" })
            {
                var sql = ReadOnlyQueryProfile.Set(left, right, operation).OrderBy(v => v ?? -1).ToQueryString();
                var keyword = operation switch { "Union" => "UNION", "Concat" => "UNION ALL", "Intersect" => "INTERSECT",
                    _ => mode == OceanBaseCompatibilityMode.MySql ? "EXCEPT" : "MINUS" };
                // Each of the two raw source operands already contains three UNION ALL tokens.
                var operators = Regex.Matches(sql, @"\b(?:UNION ALL|UNION|INTERSECT|MINUS|EXCEPT)\b")
                    .Select(match => match.Value).ToArray();
                Assert.Equal(Enumerable.Repeat("UNION ALL", 3).Append(keyword).Concat(Enumerable.Repeat("UNION ALL", 3)), operators);
                if (mode == OceanBaseCompatibilityMode.Oracle) Assert.DoesNotContain("EXCEPT", sql);
            }
            var difference = mode == OceanBaseCompatibilityMode.MySql ? "EXCEPT" : "MINUS";
            var nested = Regex.Replace(left.Except(right.Except(rows.Where(r => r.Id == 4).Select(r => r.Value))).ToQueryString(), @"\s+", " ");
            Assert.Contains(difference + " ( SELECT", nested);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Offline_profile_translates_native_lateral_pair_queries(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var rows = ReadOnlyQueryProfile.Rows(context, mode);
            foreach (var outer in new[] { false, true })
            {
                var sql = ReadOnlyQueryProfile.LateralPairs(rows, outer).ToQueryString();
                Assert.Contains(outer ? "LEFT JOIN LATERAL (" : "JOIN LATERAL (", sql);
                Assert.Contains("ON 1 = 1", sql);
                Assert.DoesNotContain("APPLY", sql);
                Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "LIMIT 1" : "FETCH FIRST 1 ROWS ONLY", sql);
                Assert.Contains("ORDER BY", sql);
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Offline_profile_preserves_default_rows_and_nullable_scalar_values(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var rows = ReadOnlyQueryProfile.Rows(context, mode);
            foreach (var nullable in new[] { false, true })
            {
                var sql = ReadOnlyQueryProfile.DefaultPairs(rows, nullable).ToQueryString();
                Assert.Contains("JOIN LATERAL (", sql);
                Assert.Contains("LEFT JOIN (", sql);
                Assert.DoesNotContain("LEFT JOIN LATERAL", sql);
                Assert.Equal(!nullable, sql.Contains("COALESCE", StringComparison.Ordinal));
                Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "LIMIT 1" : "FETCH FIRST 1 ROWS ONLY", sql);
            }
            var empty = rows.Where(r => r.Id < 0).Select(r => r.Value).DefaultIfEmpty().ToQueryString();
            Assert.Contains("LEFT JOIN (", empty);
            Assert.DoesNotContain("COALESCE", empty);
        }

        private static ReadOnlyQueryProfile.Context Create(OceanBaseCompatibilityMode mode)
            => new(new DbContextOptionsBuilder<ReadOnlyQueryProfile.Context>().UseOceanBase("Server=offline;Database=offline", mode).Options);
    }
}
