using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests
{
    public sealed class DefaultIfEmptyQueryTests
    {
        [Theory]
        [MemberData(nameof(Modes))]
        public void Explicit_scalar_defaults_depend_on_row_presence_in_composed_queries(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var rows = context.Set<Row>();
            int? fallback = 42;
            var defaults = rows.Where(row => row.Id > 20).Select(row => row.Value).DefaultIfEmpty(fallback);
            var composed = Flat(defaults.Where(value => value == fallback).ToQueryString());
            Assert.Contains("LEFT JOIN", composed);
            Assert.Contains("CASE", composed);
            Assert.Contains("IS NULL", composed);
            Assert.DoesNotContain("COALESCE", composed);

            var terminal = Flat(rows.Where(row => rows.OrderBy(child => child.Id).Select(child => child.Value)
                .FirstOrDefault(fallback) == row.Value).ToQueryString());
            Assert.Contains("CASE", terminal);
            Assert.Contains("LEFT JOIN", terminal);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public void Reference_defaults_project_presence_inside_the_original_source(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var sql = Flat(context.Set<Row>().Select(r => new { r.Id, r.Value }).DefaultIfEmpty().ToQueryString());
            Assert.Matches(@"LEFT JOIN \( SELECT .*1", sql);
            Assert.DoesNotContain("COALESCE", sql);
            var dto = Flat(context.Set<Row>().Select(r => new Box { Required = r.Id, Optional = r.Value }).DefaultIfEmpty().ToQueryString());
            Assert.Matches(@"LEFT JOIN \( SELECT .*1", dto);
            Assert.DoesNotContain("COALESCE", dto);
            var nested = context.Set<Row>().Select(r => new
            {
                r.Id,
                Children = context.Set<Row>().Where(child => child.Id > r.Id).Select(child => child.Id).ToList()
            }).DefaultIfEmpty().ToQueryString();
            Assert.Contains("DefaultRows", nested);
            Assert.Equal(2, Regex.Matches(nested, "DefaultRows").Count);
            Assert.Matches(@" [<>] ", Flat(nested));
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public void Reference_null_and_member_composition_use_presence_and_current_values(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var floor = 3;
            var query = context.Set<Row>().Select(r => new { r.Id, r.Value }).DefaultIfEmpty();
            var sql = Flat(query.Where(r => r != null && r.Id > floor).OrderBy(r => r!.Value)
                .Select(r => r!.Id).ToQueryString());
            Assert.Contains("IS NOT NULL", sql);
            Assert.Contains("ORDER BY", sql);
            Assert.DoesNotContain("COALESCE", sql);
            var missing = Flat(query.Where(r => r == null).ToQueryString());
            Assert.Contains("IS NULL", missing);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public void Boxing_and_nullable_required_reads_do_not_coalesce_present_NULL(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var rows = context.Set<Row>();
            var boxed = Flat(rows.Select(r => (object)r.Id).DefaultIfEmpty().ToQueryString());
            Assert.Matches(@"LEFT JOIN \( SELECT .*1", boxed);
            Assert.DoesNotContain("COALESCE", boxed);
            var required = Flat(rows.Select(r => r.Value!.Value).DefaultIfEmpty().ToQueryString());
            Assert.Matches(@"LEFT JOIN \( SELECT .*1", required);
            Assert.DoesNotContain("COALESCE", required);
        }

        public static IEnumerable<object[]> Modes()
        {
            foreach (var mode in Enum.GetValues<OceanBaseCompatibilityMode>()) yield return [mode];
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public void Standalone_scalars_keep_CLR_defaults_and_nullable_values(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var rows = context.Set<Row>().Where(r => r.Id > 20);
            var integral = Flat(rows.Select(r => r.Id).DefaultIfEmpty().ToQueryString());
            Assert.Contains("COALESCE(", integral);
            Assert.Contains(".Id, 0)", integral);
            Assert.Contains("LEFT JOIN (", integral);
            Assert.Contains(" ON ", integral);
            var boolean = Flat(rows.Select(r => r.Active).DefaultIfEmpty().ToQueryString());
            Assert.Contains("COALESCE(", boolean);
            Assert.Contains(".Active, 0)", boolean);
            foreach (var sql in new[] { rows.Select(r => r.Value).DefaultIfEmpty().ToQueryString(),
                rows.Select(r => r.Name).DefaultIfEmpty().ToQueryString() })
            {
                Assert.DoesNotContain("COALESCE", sql);
                Assert.Contains("LEFT JOIN", sql);
            }
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public void Converted_default_uses_the_original_mapping(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var sql = Flat(context.Set<Row>().Select(r => r.Offset).DefaultIfEmpty().ToQueryString());
            Assert.Contains("COALESCE(", sql);
            Assert.Contains(".Offset, 100)", sql);
            Assert.DoesNotContain(".Offset, 0)", sql);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public void Entity_default_remains_a_nullable_entity_projection(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var sql = Flat(context.Set<Row>().Where(r => r.Id > 20).DefaultIfEmpty().ToQueryString());
            Assert.Contains("LEFT JOIN (", sql);
            Assert.DoesNotContain("COALESCE", sql);
            Assert.Contains(".Id", sql);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public void Correlated_scalar_and_anonymous_defaults_keep_compensation_inside_the_right_source(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var rows = context.Set<Row>();
            var scalar = rows.SelectMany(parent => rows.Where(child => child.Id > parent.Id)
                .OrderBy(child => child.Id).Take(1).Select(child => child.Id).DefaultIfEmpty(),
                (parent, child) => new { Parent = parent.Id, Child = child });
            var sql = Flat(scalar.ToQueryString());
            Assert.Contains("JOIN LATERAL (", sql);
            Assert.Contains("LEFT JOIN (", sql);
            Assert.Contains("COALESCE(", sql);
            Assert.Contains(", 0)", sql);
            Assert.Contains("d0.Id > d.Id", sql);
            Assert.Contains("ORDER BY d0.Id", sql);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "LIMIT 1" : "FETCH FIRST 1 ROWS ONLY", sql);
            var anonymous = Flat(rows.SelectMany(parent => rows.Where(child => child.Id > parent.Id)
                .OrderBy(child => child.Id).Take(1).Select(child => new { child.Id, child.Value }).DefaultIfEmpty(),
                (parent, child) => new { Parent = parent.Id, Child = child!.Id, child.Value }).ToQueryString());
            Assert.DoesNotContain("COALESCE", anonymous);
            Assert.Contains(".Value", anonymous);
            Assert.DoesNotContain("APPLY", anonymous);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public void Post_default_predicates_compare_the_compensated_value(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var sql = Flat(context.Set<Row>().Select(r => r.Offset).DefaultIfEmpty().Where(value => value == 0).ToQueryString());
            Assert.Contains("WHERE COALESCE(", sql);
            Assert.Contains(".Offset, 100) = 100", sql);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public void Scalar_lateral_sources_create_the_default_row_before_computing_its_value(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var rows = context.Set<Row>();
            var sql = Flat(rows.SelectMany(parent => rows.Where(child => child.Id > parent.Id).OrderBy(child => child.Id)
                .Take(1).Select(child => child.Id).DefaultIfEmpty()).ToQueryString());
            Assert.Contains("JOIN LATERAL ( SELECT COALESCE(", sql);
            Assert.Contains("LEFT JOIN (", sql);
            Assert.DoesNotContain("LEFT JOIN LATERAL", sql);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public void Explicit_entity_default_is_rejected(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            Assert.Throws<InvalidOperationException>(() => context.Set<Row>().DefaultIfEmpty(new Row { Id = 42 }).ToQueryString());
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public void Explicit_SingleOrDefault_does_not_reduce_a_multirow_subquery_to_one_row(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var rows = context.Set<Row>();
            Assert.Throws<InvalidOperationException>(() => rows.Select(parent => rows.Where(child => child.Id > parent.Id)
                .Select(child => child.Value).SingleOrDefault(42)).ToQueryString());
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public void Correlated_nullable_scalars_and_members_preserve_NULL(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var rows = context.Set<Row>();
            var scalar = Flat(rows.SelectMany(parent => rows.Where(child => child.Id > parent.Id).Take(1)
                .Select(child => child.Value).DefaultIfEmpty()).ToQueryString());
            Assert.DoesNotContain("COALESCE", scalar);
            var anonymous = Flat(rows.SelectMany(parent => rows.Where(child => child.Id > parent.Id).Take(1)
                .Select(child => new { child.Id, child.Value }).DefaultIfEmpty()).ToQueryString());
            Assert.DoesNotContain("COALESCE", anonymous);
            Assert.DoesNotContain(".Value, 0)", anonymous);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public void User_coalesce_is_preserved_and_nullable_boolean_keeps_NULL(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var rows = context.Set<Row>();
            var explicitCoalesce = Flat(rows.Select(r => (int?)(r.Value ?? 7)).DefaultIfEmpty().ToQueryString());
            Assert.Contains(".Value, 7)", explicitCoalesce);
            Assert.Single(Regex.Matches(explicitCoalesce, "COALESCE"));
            Assert.Contains("LEFT JOIN ( SELECT COALESCE(", explicitCoalesce);
            var boolean = Flat(rows.Select(r => (bool?)r.Active).DefaultIfEmpty().ToQueryString());
            Assert.DoesNotContain("COALESCE", boolean);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public void Member_initializers_and_mixed_entity_projections_keep_each_projection_contract(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var sql = Flat(context.Set<Row>().Select(r => new Box { Required = r.Id, Optional = r.Value, Entity = r })
                .DefaultIfEmpty().ToQueryString());
            Assert.DoesNotContain("COALESCE", sql);
            Assert.DoesNotContain(".Value, 0)", sql);
            Assert.Contains(".Active", sql);
            Assert.Contains(".Offset", sql);
            var nullPredicate = Flat(context.Set<Row>().Select(r => r.Value).DefaultIfEmpty().Where(value => value == null).ToQueryString());
            Assert.Contains(".Value IS NULL", nullPredicate);
            Assert.DoesNotContain("COALESCE", nullPredicate);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public void Constructing_a_mapped_CLR_type_is_still_a_projection_not_an_entity_shaper(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var rows = context.Set<Row>();
            var sql = Flat(rows.SelectMany(parent => rows.Where(child => child.Id > parent.Id).OrderBy(child => child.Id).Take(1)
                .Select(child => new Row { Id = child.Id }).DefaultIfEmpty()).ToQueryString());
            Assert.Contains("JOIN LATERAL (", sql);
            Assert.Contains("LEFT JOIN (", sql);
            Assert.DoesNotContain("LEFT JOIN LATERAL", sql);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public void Required_numeric_and_reference_coalesce_are_evaluated_inside_the_source(OceanBaseCompatibilityMode mode)
        {
            using var context = Create(mode);
            var rows = context.Set<Row>();
            var numeric = Flat(rows.Select(r => r.Value ?? 7).DefaultIfEmpty().ToQueryString());
            Assert.Contains("LEFT JOIN ( SELECT COALESCE(d.Value, 7)", numeric);
            Assert.Matches(@"^SELECT \w+\.\w+, \w+\.Present FROM", numeric);
            Assert.Contains("1 Present", numeric);
            var text = Flat(rows.Select(r => r.Name ?? "fallback").DefaultIfEmpty().ToQueryString());
            Assert.Matches(@"LEFT JOIN \( SELECT COALESCE\(d.Name, N?'fallback'\)", text);
            Assert.DoesNotContain("SELECT COALESCE", text[..text.IndexOf("FROM", StringComparison.Ordinal)]);
        }

        private static string Flat(string sql) => Regex.Replace(sql.Replace("`", "", StringComparison.Ordinal)
            .Replace("\"", "", StringComparison.Ordinal).Replace(" AS ", " ", StringComparison.Ordinal), @"\s+", " ").Trim();
        private static Context Create(OceanBaseCompatibilityMode mode) => new(new DbContextOptionsBuilder<Context>()
            .UseOceanBase("Server=offline;Database=app", mode).Options);
        private sealed class Context(DbContextOptions<Context> options) : DbContext(options)
        {
            protected override void OnModelCreating(ModelBuilder model)
            {
                model.Entity<Row>().ToTable("DefaultRows");
                model.Entity<Row>().Property(r => r.Offset).HasConversion(value => value + 100, value => value - 100);
                model.Entity<Row>().Property(r => r.Name).HasMaxLength(40);
            }
        }
        private sealed class Row
        {
            public int Id { get; set; }
            public int Offset { get; set; }
            public int? Value { get; set; }
            public bool Active { get; set; }
            public string? Name { get; set; }
        }
        private sealed class Box { public int Required { get; set; } public int? Optional { get; set; } public Row? Entity { get; set; } }
    }
}
