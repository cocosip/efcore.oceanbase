using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBase.EntityFrameworkCore.Query;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests
{
    public sealed class SetOperationQueryTests
    {
        public static IEnumerable<object[]> Operators()
        {
            foreach (var mode in Enum.GetValues<OceanBaseCompatibilityMode>())
                foreach (var operation in new[] { "Union", "Concat", "Intersect", "Except" })
                    yield return [mode, operation];
        }

        [Theory]
        [MemberData(nameof(Operators))]
        public void Set_operator_uses_the_tenant_dialect_and_preserves_distinctness(OceanBaseCompatibilityMode mode, string operation)
        {
            using var context = Create(mode);
            var rows = context.Set<Row>();
            var left = rows.Where(r => r.Id < 10).Select(r => r.Value);
            var right = rows.Where(r => r.Id > 3).Select(r => r.Value);
            var sql = Set(left, right, operation).ToQueryString();
            Assert.Equal(new[] { Keyword(mode, operation) }, Tokens(sql));
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "`s`.`RowKey` < 10" : "\"s\".\"RowKey\" < 10", sql);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "`s0`.`RowKey` > 3" : "\"s0\".\"RowKey\" > 3", sql);
            if (mode == OceanBaseCompatibilityMode.Oracle) Assert.DoesNotContain("EXCEPT", sql);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Nested_difference_and_mixed_operators_keep_explicit_operand_grouping(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var a = context.Set<Row>().Where(r => r.Id == 1).Select(r => r.Value);
            var b = context.Set<Row>().Where(r => r.Id == 2).Select(r => r.Value);
            var c = context.Set<Row>().Where(r => r.Id == 3).Select(r => r.Value);
            var difference = Keyword(mode, "Except");
            var queries = new[] { a.Except(b.Except(c)), a.Except(b).Except(c), a.Union(b).Intersect(c), a.Concat(b.Union(c)) };
            const string first = "SELECT s.Value FROM SetRows s WHERE s.RowKey = 1";
            const string second = "SELECT s0.Value FROM SetRows s0 WHERE s0.RowKey = 2";
            const string third = "SELECT s1.Value FROM SetRows s1 WHERE s1.RowKey = 3";
            var expected = new[] { $"{first} {difference} ( {second} {difference} {third} )",
                $"( {first} {difference} {second} ) {difference} {third}",
                $"( {first} UNION {second} ) INTERSECT {third}", $"{first} UNION ALL ( {second} UNION {third} )" };
            for (var i = 0; i < queries.Length; i++)
            {
                var sql = queries[i].ToQueryString();
                var flat = Regex.Replace(sql.Replace("`", "", StringComparison.Ordinal).Replace("\"", "", StringComparison.Ordinal), @"\s+", " ").Trim()
                    .Replace(" AS ", " ", StringComparison.Ordinal);
                Assert.Equal(expected[i], flat);
                if (mode == OceanBaseCompatibilityMode.Oracle) Assert.DoesNotContain("EXCEPT", sql);
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Operand_paging_is_wrapped_and_Boolean_set_projections_keep_numeric_SQL(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var rows = context.Set<Row>();
            var sql = rows.OrderBy(r => r.Id).Take(2).Select(r => r.Value)
                .Except(rows.OrderBy(r => r.Id).Skip(1).Take(3).Select(r => r.Value)).ToQueryString();
            Assert.Equal(new[] { Keyword(mode, "Except") }, Tokens(sql));
            Assert.Equal(2, Regex.Matches(sql, "ORDER BY").Count);
            if (mode == OceanBaseCompatibilityMode.MySql)
                Assert.Equal(2, Regex.Matches(sql, "LIMIT").Count);
            else
            {
                Assert.Single(Regex.Matches(sql, "FETCH FIRST"));
                Assert.Single(Regex.Matches(sql, "FETCH NEXT"));
            }
            Assert.Matches(@"FROM\s*\(\s*SELECT", sql);
            var booleans = rows.Select(r => r.Id > 0).Except(rows.Select(r => r.Id > 1)).ToQueryString();
            Assert.Equal(new[] { Keyword(mode, "Except") }, Tokens(booleans));
            if (mode == OceanBaseCompatibilityMode.Oracle) Assert.Equal(2, Regex.Matches(booleans, "CASE").Count);
        }

        [Theory]
        [InlineData("Except")]
        [InlineData("Intersect")]
        public void Oracle_non_distinct_difference_and_intersection_trees_reject_unsupported_ALL(string operation)
        {
            using var context = Create(OceanBaseCompatibilityMode.Oracle);
            var scalar = context.GetService<ISqlExpressionFactory>().Constant(1, context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(int)));
            var source = new SelectExpression(null, [], null, [], null,
                [new ProjectionExpression(scalar, "Value")], false, [], null, null, new SqlAliasManager());
            SetOperationBase set = operation == "Except"
                ? new ExceptExpression("s", source, source, false)
                : new IntersectExpression("s", source, source, false);
            var generator = new OceanBaseQuerySqlGenerator(context.GetService<QuerySqlGeneratorDependencies>(), OceanBaseCompatibilityMode.Oracle);
            Assert.Contains("ALL", Assert.Throws<NotSupportedException>(() => generator.GetCommand(set)).Message);
        }

        [Theory]
        [MemberData(nameof(Operators))]
        public void Composed_set_queries_retain_projection_order_filter_and_paging(OceanBaseCompatibilityMode mode, string operation)
        {
            using var context = Create(mode);
            var rows = context.Set<Row>();
            var set = Set(rows.Where(r => r.Id < 10).Select(r => r.Value), rows.Where(r => r.Id > 3).Select(r => r.Value), operation);
            var sql = set.Where(v => v != null).OrderBy(v => v).Skip(2).Take(4).ToQueryString();
            Assert.Equal(new[] { Keyword(mode, operation) }, Tokens(sql));
            Assert.Contains("IS NOT NULL", sql);
            Assert.Contains("ORDER BY", sql);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "LIMIT" : "FETCH NEXT", sql);
            Assert.Contains("OFFSET", sql);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? ") AS `" : ") \"", sql);
        }

        [Theory]
        [InlineData("Union", "Text")]
        [InlineData("Concat", "Text")]
        [InlineData("Intersect", "Text")]
        [InlineData("Except", "Text")]
        [InlineData("Union", "NationalText")]
        [InlineData("Concat", "NationalText")]
        [InlineData("Intersect", "NationalText")]
        [InlineData("Except", "NationalText")]
        [InlineData("Union", "Payload")]
        [InlineData("Concat", "Payload")]
        [InlineData("Intersect", "Payload")]
        [InlineData("Except", "Payload")]
        public void Oracle_LOB_set_projections_reject_before_generating_unsupported_SQL(string operation, string property)
        {
            using var context = Create(OceanBaseCompatibilityMode.Oracle);
            var rows = context.Set<LobRow>();
            var left = rows.Where(r => r.Id < 10);
            var right = rows.Where(r => r.Id > 3);
            if (property == "Payload")
                AssertLobSets(left.Select(r => r.Payload), right.Select(r => r.Payload), context.Set<Row>().Select(r => r.BoundedPayload), operation);
            else
                AssertLobSets(left.Select(r => EF.Property<string>(r, property)), right.Select(r => EF.Property<string>(r, property)), context.Set<Row>().Select(r => r.Name), operation);
        }

        [Theory]
        [MemberData(nameof(Operators))]
        public void MySql_LOB_and_bounded_Oracle_text_keep_existing_set_translation(OceanBaseCompatibilityMode mode, string operation)
        {
            using var context = Create(mode);
            var rows = context.Set<Row>();
            var text = mode == OceanBaseCompatibilityMode.MySql
                ? context.Set<LobRow>().Select(r => r.NationalText)
                : rows.Select(r => r.Name);
            Assert.Equal(new[] { Keyword(mode, operation) }, Tokens(Set(text, text, operation).ToQueryString()));
        }

        private static string[] Tokens(string sql) => Regex.Matches(sql, @"\b(?:UNION ALL|UNION|INTERSECT|EXCEPT|MINUS)\b").Select(m => m.Value).ToArray();
        private static void AssertLobSets<T>(IQueryable<T> left, IQueryable<T> right, IQueryable<T> bounded, string operation)
        {
            foreach (var query in new[] { Set(left, right, operation), Set(left, bounded, operation), Set(bounded, right, operation) })
                Assert.Contains("LOB", Assert.Throws<NotSupportedException>(() => query.ToQueryString()).Message);
        }
        private static string Keyword(OceanBaseCompatibilityMode mode, string operation) => operation switch
        { "Concat" => "UNION ALL", "Except" => mode == OceanBaseCompatibilityMode.Oracle ? "MINUS" : "EXCEPT", "Intersect" => "INTERSECT", _ => "UNION" };
        private static IQueryable<T> Set<T>(IQueryable<T> left, IQueryable<T> right, string operation) => operation switch
        { "Union" => left.Union(right), "Concat" => left.Concat(right), "Intersect" => left.Intersect(right), _ => left.Except(right) };
        private static SetContext Create(OceanBaseCompatibilityMode mode) => new(new DbContextOptionsBuilder<SetContext>()
            .UseOceanBase("Server=offline;Database=app", mode).Options, mode);
        private sealed class SetContext(DbContextOptions<SetContext> options, OceanBaseCompatibilityMode mode) : DbContext(options)
        {
            protected override void OnModelCreating(ModelBuilder model)
            {
                model.Entity<Row>().ToTable("SetRows");
                model.Entity<Row>().Property(r => r.Id).HasColumnName("RowKey");
                model.Entity<Row>().Property(r => r.Name).HasMaxLength(40);
                model.Entity<Row>().Property(r => r.BoundedPayload).HasMaxLength(16);
                model.Entity<LobRow>().Property(r => r.Text).IsUnicode(false);
                if (mode == OceanBaseCompatibilityMode.Oracle)
                {
                    model.Entity<LobRow>().Property(r => r.Text).HasColumnType("CLOB");
                    model.Entity<LobRow>().Property(r => r.NationalText).HasColumnType("NCLOB");
                }
            }
        }
        private sealed class Row { public int Id { get; set; } public int? Value { get; set; } public string? Name { get; set; } public byte[]? BoundedPayload { get; set; } }
        private sealed class LobRow { public int Id { get; set; } public string? Text { get; set; } public string? NationalText { get; set; } public byte[]? Payload { get; set; } }
    }
}
