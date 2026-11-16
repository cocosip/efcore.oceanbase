using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace OceanBase.EntityFrameworkCore.FunctionalTests
{
    public sealed class DefaultIfEmptyRuntimeTests
    {
        [Theory]
        [MemberData(nameof(TransactionTests.Modes), MemberType = typeof(TransactionTests))]
        public async Task Nested_collections_preserve_outer_defaults_and_parent_groups(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new Commands();
            await using var context = Create(mode, commands);
            var rows = context.Set<Row>();
            var query = rows.OrderBy(r => r.Id).Select(r => new
            {
                r.Id,
                Children = rows.Where(child => child.Id > r.Id).OrderBy(child => child.Id)
                    .Select(child => child.Id).ToList()
            }).DefaultIfEmpty();
            commands.Results = Table([typeof(int), typeof(int), typeof(int)], new object?[] { null, null, null });
            Assert.Null(Assert.Single(await Run(query, async)));
            AssertClean(context, commands);
            commands.Results = Table([typeof(int), typeof(int), typeof(int)], [0, 1, 1], [0, 2, 1], [1, 2, 1], [2, null, 1]);
            var result = await Run(query, async);
            Assert.Equal(new[] { 0, 1, 2 }, result.Select(r => r!.Id));
            Assert.Equal(new[] { 1, 2 }, result[0]!.Children);
            Assert.Equal(new[] { 2 }, result[1]!.Children);
            Assert.Empty(result[2]!.Children);
            Assert.Matches(@"^SELECT (\w+)\.Id, (\w+)\.Id, \1\.c FROM", commands.Items[^1].Sql);
            Assert.Matches(@"LEFT JOIN DefaultRows (\w+) ON (\w+)\.Id < \1\.Id ORDER BY \2\.Id, \2\.Id, \1\.Id$", commands.Items[^1].Sql);
            Assert.Empty(context.ChangeTracker.Entries());
            AssertClean(context, commands);
        }

        [Theory]
        [MemberData(nameof(TransactionTests.Modes), MemberType = typeof(TransactionTests))]
        public async Task Nested_nullable_and_converted_collections_keep_present_NULL_elements(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new Commands();
            await using var context = Create(mode, commands);
            var rows = context.Set<Row>();
            var query = rows.OrderBy(r => r.Id).Select(r => new
            {
                r.Id,
                Children = rows.Where(child => child.Id > r.Id).OrderBy(child => child.Id)
                    .Select(child => new { child.Value, child.Offset }).ToList()
            }).DefaultIfEmpty();
            commands.Results = Table([typeof(int), typeof(int), typeof(int), typeof(int), typeof(int)],
                new object?[] { null, null, null, null, null });
            Assert.Null(Assert.Single(await Run(query, async)));
            AssertClean(context, commands);
            commands.Results = Table([typeof(int), typeof(int), typeof(int), typeof(int), typeof(int)],
                [0, null, 107, 1, 1], [0, 4, 109, 2, 1], [1, 4, 109, 2, 1], [2, null, null, null, 1]);
            var result = await Run(query, async);
            Assert.Equal(new[] { 0, 1, 2 }, result.Select(r => r!.Id));
            Assert.Equal(new int?[] { null, 4 }, result[0]!.Children.Select(c => c.Value));
            Assert.Equal(new[] { 7, 9 }, result[0]!.Children.Select(c => c.Offset));
            Assert.Equal(4, Assert.Single(result[1]!.Children).Value);
            Assert.Equal(9, result[1]!.Children[0].Offset);
            Assert.Empty(result[2]!.Children);
            Assert.Matches(@"^SELECT (\w+)\.Id, (\w+)\.Value, \2\.Offset, \2\.Id, \1\.c FROM", commands.Items[^1].Sql);
            Assert.Empty(context.ChangeTracker.Entries());
            AssertClean(context, commands);
        }

        [Theory]
        [MemberData(nameof(ModelMaterializationTests.TrackingCases), MemberType = typeof(ModelMaterializationTests))]
        public async Task Nested_entity_collections_keep_default_and_identity_scopes_separate(OceanBaseCompatibilityMode mode, bool async, QueryTrackingBehavior tracking)
        {
            var commands = new Commands();
            await using var context = Create(mode, commands);
            context.ChangeTracker.QueryTrackingBehavior = tracking;
            var rows = context.Set<Row>();
            var query = rows.OrderBy(r => r.Id).Select(r => new
            {
                r.Id,
                Children = rows.Where(child => child.Id > r.Id).OrderBy(child => child.Id).ToList()
            }).DefaultIfEmpty();
            commands.Results = Table([typeof(int), typeof(int), typeof(int), typeof(string), typeof(int), typeof(int), typeof(int)],
                new object?[] { null, null, null, null, null, null, null });
            Assert.Null(Assert.Single(await Run(query, async)));
            Assert.Empty(context.ChangeTracker.Entries());
            AssertClean(context, commands);
            commands.Results = Table([typeof(int), typeof(int), typeof(int), typeof(string), typeof(int), typeof(int), typeof(int)],
                [0, 1, 1, "one", 107, null, 1], [0, 2, 0, "two", 109, 4, 1],
                [1, 2, 0, "two", 109, 4, 1], [2, null, null, null, null, null, 1]);
            var result = await Run(query, async);
            Assert.Equal(new[] { 0, 1, 2 }, result.Select(r => r!.Id));
            Assert.Equal(new[] { 1, 2 }, result[0]!.Children.Select(c => c.Id));
            Assert.Equal(new[] { "one", "two" }, result[0]!.Children.Select(c => c.Name));
            Assert.Equal(new[] { 7, 9 }, result[0]!.Children.Select(c => c.Offset));
            Assert.True(result[0]!.Children[0].Active);
            Assert.False(result[0]!.Children[1].Active);
            Assert.Null(result[0]!.Children[0].Value);
            Assert.Equal(4, result[0]!.Children[1].Value);
            Assert.Equal(2, Assert.Single(result[1]!.Children).Id);
            Assert.Empty(result[2]!.Children);
            Assert.Equal(tracking != QueryTrackingBehavior.NoTracking,
                ReferenceEquals(result[0]!.Children[1], result[1]!.Children[0]));
            Assert.Equal(tracking == QueryTrackingBehavior.TrackAll ? 2 : 0, context.ChangeTracker.Entries().Count());
            Assert.Matches(@"^SELECT (\w+)\.Id, (\w+)\.Id, \2\.Active, \2\.Name, \2\.Offset, \2\.Value, \1\.c FROM", commands.Items[^1].Sql);
            AssertClean(context, commands);
        }

        [Theory]
        [MemberData(nameof(TransactionTests.Modes), MemberType = typeof(TransactionTests))]
        public async Task Nested_collection_paging_keeps_current_parameters(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new Commands();
            await using var context = Create(mode, commands);
            var rows = context.Set<Row>();
            var floor = 0;
            var take = 2;
            var query = rows.Where(r => r.Id >= floor).OrderBy(r => r.Id).Take(2).Select(r => new
            {
                r.Id,
                Children = rows.Where(child => child.Id > r.Id + floor).OrderBy(child => child.Id).Take(take)
                    .Select(child => child.Id).ToList()
            }).DefaultIfEmpty();
            commands.Results = Table([typeof(int), typeof(int), typeof(int)], [0, 1, 1], [0, 2, 1], [1, 2, 1], [1, 3, 1]);
            var result = await Run(query, async);
            Assert.Equal(new[] { 0, 1 }, result.Select(r => r!.Id));
            Assert.Equal(new[] { 1, 2 }, result[0]!.Children);
            Assert.Equal(new[] { 2, 3 }, result[1]!.Children);
            Assert.Equal(new object?[] { 0, 2, 0, 2 }, commands.Items[^1].Inputs);
            AssertClean(context, commands);
            floor = 1;
            take = 1;
            commands.Results = Table([typeof(int), typeof(int), typeof(int)], [1, 3, 1], [2, 4, 1]);
            result = await Run(query, async);
            Assert.Equal(new[] { 1, 2 }, result.Select(r => r!.Id));
            Assert.Equal(new[] { 3 }, result[0]!.Children);
            Assert.Equal(new[] { 4 }, result[1]!.Children);
            var command = commands.Items[^1];
            Assert.Equal(new object?[] { 1, 2, 1, 1 }, command.Inputs);
            Assert.Contains("LEFT JOIN LATERAL (", command.Sql);
            Assert.Matches(@"WHERE \w+\.Id >= \? ORDER BY \w+\.Id", command.Sql);
            Assert.Matches(@"WHERE \w+\.Id > \(\w+\.Id \+ \?\) ORDER BY \w+\.Id", command.Sql);
            Assert.Equal(2, Regex.Matches(command.Sql, mode == OceanBaseCompatibilityMode.MySql ? @"LIMIT \?" : @"FETCH FIRST \? ROWS ONLY").Count);
            AssertClean(context, commands);
        }

        [Theory]
        [MemberData(nameof(TransactionTests.Modes), MemberType = typeof(TransactionTests))]
        public async Task Nested_collection_failures_dispose_readers_and_allow_recovery(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new Commands();
            await using var context = Create(mode, commands);
            var rows = context.Set<Row>();
            var query = rows.OrderBy(r => r.Id).Select(r => new
            {
                r.Id,
                Children = rows.Where(child => child.Id > r.Id).OrderBy(child => child.Id)
                    .Select(child => child.Offset).ToList()
            }).DefaultIfEmpty();
            commands.Results = Table([typeof(int), typeof(int), typeof(int), typeof(int)],
                new object?[] { null, null, null, null });
            foreach (var cancel in new[] { false, true })
            {
                using var source = new CancellationTokenSource();
                commands.Failure = cancel ? new OperationCanceledException(source.Token) : new InvalidOperationException("collection query failed");
                commands.BeforeFailure = cancel ? source.Cancel : null;
                var error = await Record.ExceptionAsync(() => Run(query, async, source.Token));
                Assert.Same(commands.Failure, error);
                if (async) Assert.Equal(source.Token, commands.Items[^1].Token);
                AssertClean(context, commands);
                commands.Failure = null;
                commands.BeforeFailure = null;
                Assert.Null(Assert.Single(await Run(query, async)));
                AssertClean(context, commands);
            }
            commands.Results = Table([typeof(int), typeof(string), typeof(int), typeof(int)], [0, "107", 1, 1], [0, "invalid", 2, 1]);
            await Assert.ThrowsAsync<FormatException>(() => Run(query, async));
            AssertClean(context, commands);
            commands.Results = Table([typeof(int), typeof(int), typeof(int), typeof(int)], [0, 107, 1, 1], [1, null, null, 1]);
            var recovered = await Run(query, async);
            Assert.Equal(new[] { 0, 1 }, recovered.Select(r => r!.Id));
            Assert.Equal(new[] { 7 }, recovered[0]!.Children);
            Assert.Empty(recovered[1]!.Children);
            Assert.Matches(@"^SELECT (\w+)\.Id, (\w+)\.Offset, \2\.Id, \1\.c FROM", commands.Items[^1].Sql);
            AssertClean(context, commands);
        }

        [Theory]
        [MemberData(nameof(TransactionTests.Modes), MemberType = typeof(TransactionTests))]
        public async Task Nested_collection_user_projection_after_default_still_constructs_an_object(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new Commands();
            await using var context = Create(mode, commands);
            var rows = context.Set<Row>();
            var query = rows.Select(r => new
            {
                r.Id,
                Children = rows.Where(child => child.Id > r.Id).OrderBy(child => child.Id)
                    .Select(child => child.Id).ToList()
            }).DefaultIfEmpty().Select(row => new
            {
                ParentId = row == null ? (int?)null : row.Id,
                Children = row == null ? null : row.Children
            });
            commands.Results = Table([typeof(int), typeof(int), typeof(int), typeof(int)], [null, 1, null, null]);
            var missing = Assert.Single(await Run(query, async));
            Assert.NotNull(missing);
            Assert.Null(missing.ParentId);
            Assert.Null(missing.Children);
            AssertClean(context, commands);
            commands.Results = Table([typeof(int), typeof(int), typeof(int), typeof(int)],
                [0, 0, 0, 1], [0, 0, 0, 2], [1, 0, 1, 2], [2, 0, 2, null]);
            var result = await Run(query, async);
            Assert.Equal(new int?[] { 0, 1, 2 }, result.Select(r => r.ParentId));
            Assert.Equal(new[] { 1, 2 }, result[0].Children);
            Assert.Equal(new[] { 2 }, result[1].Children);
            Assert.Empty(result[2].Children!);
            Assert.Matches(mode == OceanBaseCompatibilityMode.MySql
                ? @"^SELECT CASE WHEN (\w+)\.c IS NULL THEN NULL ELSE \1\.Id END, \1\.c IS NULL, \1\.Id, \w+\.Id FROM"
                : @"^SELECT CASE WHEN (\w+)\.c IS NULL THEN NULL ELSE \1\.Id END, CASE WHEN \1\.c IS NULL THEN 1 WHEN \1\.c IS NOT NULL THEN 0 END, \1\.Id, \w+\.Id FROM",
                commands.Items[^1].Sql);
            AssertClean(context, commands);
        }

        [Theory]
        [MemberData(nameof(TransactionTests.Modes), MemberType = typeof(TransactionTests))]
        public async Task Reference_defaults_distinguish_missing_rows_from_present_all_NULL_members(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new Commands { Results = Table([typeof(int), typeof(string), typeof(int)],
                [null, null, null], [null, null, 1], [7, "present", 1]) };
            await using var context = Create(mode, commands);
            var rows = context.Set<Row>();
            var anonymous = await Run(rows.Select(r => new { r.Value, r.Name }).DefaultIfEmpty(), async);
            Assert.Null(anonymous[0]);
            Assert.NotNull(anonymous[1]);
            Assert.Null(anonymous[1]!.Value);
            Assert.Null(anonymous[1]!.Name);
            Assert.Equal(7, anonymous[2]!.Value);
            Assert.Equal("present", anonymous[2]!.Name);
            var dto = await Run(rows.Select(r => new ReferenceRow { Value = r.Value, Name = r.Name }).DefaultIfEmpty(), async);
            Assert.Null(dto[0]);
            Assert.NotNull(dto[1]);
            Assert.Null(dto[1]!.Value);
            Assert.Equal(7, dto[2]!.Value);
            Assert.Empty(context.ChangeTracker.Entries());
            AssertClean(context, commands);
        }

        [Theory]
        [MemberData(nameof(TransactionTests.Modes), MemberType = typeof(TransactionTests))]
        public async Task Required_reference_members_and_constructed_mapped_types_skip_missing_row_reads(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new Commands { Results = Table([typeof(int), typeof(int)], [null, null], [0, 1], [7, 1]) };
            await using var context = Create(mode, commands);
            var rows = context.Set<Row>();
            var anonymous = await Run(rows.Select(r => new { r.Id }).DefaultIfEmpty(), async);
            Assert.Null(anonymous[0]);
            Assert.Equal(new[] { 0, 7 }, anonymous.Skip(1).Select(r => r!.Id));
            var mapped = await Run(rows.Select(r => new Row { Id = r.Id }).DefaultIfEmpty(), async);
            Assert.Null(mapped[0]);
            Assert.Equal(new[] { 0, 7 }, mapped.Skip(1).Select(r => r!.Id));
            Assert.Empty(context.ChangeTracker.Entries());
            AssertClean(context, commands);
        }

        [Theory]
        [MemberData(nameof(TransactionTests.Modes), MemberType = typeof(TransactionTests))]
        public async Task Boxed_defaults_keep_null_zero_and_converted_values_distinct(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new Commands { Results = Table([typeof(int), typeof(int)], [null, null], [0, 1], [7, 1]) };
            await using var context = Create(mode, commands);
            var rows = context.Set<Row>();
            Assert.Equal(new object?[] { null, 0, 7 }, await Run(rows.Select(r => (object)r.Id).DefaultIfEmpty(), async));
            commands.Results = Table([typeof(int), typeof(int)], [null, null], [null, 1], [0, 1]);
            Assert.Equal(new object?[] { null, null, 0 }, await Run(rows.Select(r => (object?)r.Value).DefaultIfEmpty(), async));
            commands.Results = Table([typeof(int), typeof(int)], [null, null], [100, 1], [107, 1]);
            Assert.Equal(new object?[] { null, 0, 7 }, await Run(rows.Select(r => (object)r.Offset).DefaultIfEmpty(), async));
            AssertClean(context, commands);
        }

        [Theory]
        [MemberData(nameof(TransactionTests.Modes), MemberType = typeof(TransactionTests))]
        public async Task Required_nullable_reads_throw_for_present_NULL_but_default_for_missing_rows(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new Commands { Results = Table([typeof(int), typeof(int)], [null, null], [7, 1]) };
            await using var context = Create(mode, commands);
            var query = context.Set<Row>().Select(r => r.Value!.Value).DefaultIfEmpty();
            Assert.Equal(new[] { 0, 7 }, await Run(query, async));
            commands.Results = Table([typeof(int), typeof(int)], [null, 1]);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Run(query, async));
            AssertClean(context, commands);
            commands.Results = Table([typeof(int), typeof(int)], [9, 1]);
            Assert.Equal(new[] { 9 }, await Run(query, async));
            AssertClean(context, commands);
            var computed = context.Set<Row>().Select(r => r.Value!.Value + 1).DefaultIfEmpty();
            commands.Results = Table([typeof(int), typeof(int)], [null, null], [8, 1]);
            Assert.Equal(new[] { 0, 8 }, await Run(computed, async));
            commands.Results = Table([typeof(int), typeof(int)], [null, 1]);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Run(computed, async));
            AssertClean(context, commands);
            commands.Results = Table([typeof(int), typeof(int)], [null, null], [7, 1]);
            Assert.Equal(new[] { 0, 7 }, await Run(context.Set<Row>().Select(r => r.Value ?? 7).DefaultIfEmpty(), async));
            AssertClean(context, commands);
        }

        private sealed class ReferenceRow
        {
            public int? Value { get; set; }
            public string? Name { get; set; }
        }

        private sealed record ConstructorRow(int Required, int? Optional);

        [Theory]
        [MemberData(nameof(TransactionTests.Modes), MemberType = typeof(TransactionTests))]
        public async Task Constructor_and_nested_defaults_keep_the_whole_reference_null(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new Commands { Results = Table([typeof(int), typeof(int), typeof(int)], [null, null, null], [7, null, 1]) };
            await using var context = Create(mode, commands);
            var rows = context.Set<Row>();
            var constructed = await Run(rows.Select(r => new ConstructorRow(r.Id, r.Value)).DefaultIfEmpty(), async);
            Assert.Null(constructed[0]);
            Assert.Equal(new ConstructorRow(7, null), constructed[1]);
            commands.Results = Table([typeof(int), typeof(int), typeof(int)], [1, null, 1], [0, null, 1]);
            var nested = await Run(rows.Select(r => new { r.Value }).DefaultIfEmpty().DefaultIfEmpty(), async);
            Assert.Null(nested[0]);
            Assert.NotNull(nested[1]);
            Assert.Null(nested[1]!.Value);
            AssertClean(context, commands);
        }

        [Theory]
        [MemberData(nameof(TransactionTests.Modes), MemberType = typeof(TransactionTests))]
        public async Task Correlated_reference_defaults_preserve_parent_and_skip_required_member_reads(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new Commands { Results = Table([typeof(int), typeof(int), typeof(int), typeof(int)],
                [1, 1, null, null], [2, 0, 3, null]) };
            await using var context = Create(mode, commands);
            var rows = context.Set<Row>();
            var query = rows.SelectMany(parent => rows.Where(child => child.Id > parent.Id).OrderBy(child => child.Id).Take(1)
                .Select(child => new { child.Id, child.Value }).DefaultIfEmpty(),
                (parent, child) => new { Parent = parent.Id, Child = child });
            var result = await Run(query, async);
            Assert.Equal(new[] { 1, 2 }, result.Select(r => r.Parent));
            Assert.Null(result[0].Child);
            Assert.Equal(3, result[1].Child!.Id);
            Assert.Null(result[1].Child!.Value);
            Assert.Contains("JOIN LATERAL (", commands.Items[^1].Sql);
            Assert.Matches(@"1 (?:Present|c\d*)\b", commands.Items[^1].Sql);
            AssertClean(context, commands);
        }

        [Theory]
        [MemberData(nameof(TransactionTests.Modes), MemberType = typeof(TransactionTests))]
        public async Task Mixed_entity_projection_keeps_tracking_and_the_outer_default_separate(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new Commands { Results = Table([typeof(int), typeof(int), typeof(int), typeof(string), typeof(int), typeof(int)],
                [null, null, null, null, null, null], [null, 1, 1, "one", 107, 1]) };
            await using var context = Create(mode, commands);
            var query = context.Set<Row>().Select(r => new { r.Value, Entity = r }).DefaultIfEmpty();
            var result = await Run(query, async);
            Assert.Null(result[0]);
            Assert.Null(result[1]!.Value);
            Assert.Equal(1, result[1]!.Entity.Id);
            Assert.Equal(7, result[1]!.Entity.Offset);
            Assert.Same(result[1]!.Entity, Assert.Single(context.ChangeTracker.Entries()).Entity);
            AssertClean(context, commands);
            context.ChangeTracker.Clear();
            commands.Results = Table([typeof(int)], new object?[] { null });
            var explicitProjection = await Run(query.Select(row => new { Value = row == null ? null : row.Value }), async);
            Assert.NotNull(Assert.Single(explicitProjection));
            Assert.Null(explicitProjection[0].Value);
            Assert.Empty(context.ChangeTracker.Entries());
            AssertClean(context, commands);
        }

        [Theory]
        [MemberData(nameof(TransactionTests.Modes), MemberType = typeof(TransactionTests))]
        public async Task Reference_and_required_scalar_composition_keep_current_parameter_bindings(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new Commands { Results = Table([typeof(int)], [7]) };
            await using var context = Create(mode, commands);
            var rows = context.Set<Row>();
            var floor = 0;
            var reference = rows.Select(r => new { r.Id, r.Value }).DefaultIfEmpty()
                .Where(r => r != null && r.Id > floor).OrderBy(r => r!.Value).Select(r => r!.Id);
            var required = rows.Select(r => r.Value!.Value).DefaultIfEmpty().Where(value => value > floor);
            foreach (var current in new[] { 0, 3 })
            {
                floor = current;
                commands.Results = Table([typeof(int)], [7]);
                Assert.Equal(new[] { 7 }, await Run(reference, async));
                Assert.Equal(new object?[] { current }, commands.Items[^1].Inputs);
                Assert.Contains("IS NOT NULL", commands.Items[^1].Sql);
                commands.Results = Table([typeof(int), typeof(int)], [7, 1]);
                Assert.Equal(new[] { 7 }, await Run(required, async));
                Assert.Equal(new object?[] { current }, commands.Items[^1].Inputs);
                Assert.Contains("CASE", commands.Items[^1].Sql);
                Assert.DoesNotContain("COALESCE", commands.Items[^1].Sql);
                AssertClean(context, commands);
            }
        }

        [Theory]
        [MemberData(nameof(TransactionTests.Modes), MemberType = typeof(TransactionTests))]
        public async Task Standalone_scalar_defaults_and_present_NULLs_keep_their_result_types(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new Commands();
            await using var context = Create(mode, commands);
            var rows = context.Set<Row>().Where(r => r.Id > 20);
            commands.Results = Table([typeof(int)], [0]);
            Assert.Equal(new[] { 0 }, await Run(rows.Select(r => r.Id).DefaultIfEmpty(), async));
            Assert.Contains("COALESCE(", commands.Items[^1].Sql);
            commands.Results = Table([typeof(int)], new object?[] { null });
            Assert.Equal(new int?[] { null }, await Run(rows.Select(r => r.Value).DefaultIfEmpty(), async));
            Assert.DoesNotContain("COALESCE", commands.Items[^1].Sql);
            commands.Results = Table([typeof(string)], new object?[] { null });
            Assert.Equal(new string?[] { null }, await Run(rows.Select(r => r.Name).DefaultIfEmpty(), async));
            Assert.DoesNotContain("COALESCE", commands.Items[^1].Sql);
            commands.Results = Table([typeof(int)], [0]);
            Assert.Equal(new[] { false }, await Run(rows.Select(r => r.Active).DefaultIfEmpty(), async));
            commands.Results = Table([typeof(int)], new object?[] { null });
            Assert.Equal(new bool?[] { null }, await Run(rows.Select(r => (bool?)r.Active).DefaultIfEmpty(), async));
            Assert.DoesNotContain("COALESCE", commands.Items[^1].Sql);
            commands.Results = Table([typeof(int)], [8], [null]);
            Assert.Equal(new int?[] { 8, null }, await Run(rows.Select(r => r.Value).DefaultIfEmpty(), async));
            AssertClean(context, commands);
        }

        [Theory]
        [MemberData(nameof(TransactionTests.Modes), MemberType = typeof(TransactionTests))]
        public async Task Converted_defaults_and_post_default_predicates_keep_current_model_values(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new Commands { Results = Table([typeof(int)], [100], [95]) };
            await using var context = Create(mode, commands);
            var threshold = 0;
            var query = context.Set<Row>().Select(r => r.Offset).DefaultIfEmpty().Where(value => value >= threshold);
            foreach (var value in new[] { 0, -5, 7 })
            {
                threshold = value;
                Assert.Equal(new[] { 0, -5 }, await Run(query, async));
                var command = commands.Items[^1];
                Assert.Contains("COALESCE(d.Offset, 100)", command.Sql);
                Assert.Contains("WHERE COALESCE(d.Offset, 100) >= ?", command.Sql);
                Assert.Equal(new object?[] { value + 100 }, command.Inputs);
                AssertClean(context, commands);
            }
            commands.Results = Table([typeof(int)], [null], [100]);
            var nullable = context.Set<Row>().Select(r => (int?)r.Offset).DefaultIfEmpty();
            Assert.Equal(new int?[] { null, 0 }, await Run(nullable, async));
            Assert.DoesNotContain("COALESCE", commands.Items[^1].Sql);
            AssertClean(context, commands);
        }

        [Theory]
        [MemberData(nameof(TransactionTests.Modes), MemberType = typeof(TransactionTests))]
        public async Task Correlated_scalar_defaults_preserve_parents_paging_and_nullable_values(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new Commands();
            await using var context = Create(mode, commands);
            var rows = context.Set<Row>();
            var floor = 0;
            var take = 1;
            var integral = rows.SelectMany(parent => rows.Where(child => child.Id > parent.Id + floor)
                .OrderBy(child => child.Id).Take(take).Select(child => child.Id).DefaultIfEmpty(),
                (parent, child) => new { Parent = parent.Id, Child = child });
            foreach (var value in new[] { 0, 3 })
            {
                floor = value;
                commands.Results = Table([typeof(int), typeof(int)], [1, 2], [4, 0]);
                var result = await Run(integral, async);
                Assert.Equal(new[] { 1, 4 }, result.Select(r => r.Parent));
                Assert.Equal(new[] { 2, 0 }, result.Select(r => r.Child));
                var command = commands.Items[^1];
                Assert.Contains("JOIN LATERAL ( SELECT COALESCE(", command.Sql);
                Assert.Contains("LEFT JOIN (", command.Sql);
                Assert.DoesNotContain("LEFT JOIN LATERAL", command.Sql);
                Assert.Contains("d0.Id > (d.Id + ?)", command.Sql);
                Assert.Contains("ORDER BY d0.Id", command.Sql);
                Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "LIMIT ?" : "FETCH FIRST ? ROWS ONLY", command.Sql);
                Assert.Equal(new object?[] { floor, take }, command.Inputs);
                AssertClean(context, commands);
            }
            commands.Results = Table([typeof(int), typeof(int)], [1, 7], [3, null], [4, null]);
            var nullable = rows.SelectMany(parent => rows.Where(child => child.Id > parent.Id).OrderBy(child => child.Id).Take(1)
                .Select(child => child.Value).DefaultIfEmpty(), (parent, child) => new { Parent = parent.Id, Child = child });
            var optional = await Run(nullable, async);
            Assert.Equal(new[] { 1, 3, 4 }, optional.Select(r => r.Parent));
            Assert.Equal(new int?[] { 7, null, null }, optional.Select(r => r.Child));
            Assert.DoesNotContain("COALESCE", commands.Items[^1].Sql);
            Assert.Contains("LEFT JOIN (", commands.Items[^1].Sql);
            AssertClean(context, commands);
        }

        [Theory]
        [MemberData(nameof(TransactionTests.Modes), MemberType = typeof(TransactionTests))]
        public async Task Anonymous_member_defaults_keep_required_and_nullable_members_distinct(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new Commands { Results = Table([typeof(int), typeof(int), typeof(int)], [1, 2, null]) };
            await using var context = Create(mode, commands);
            var rows = context.Set<Row>();
            var query = rows.SelectMany(parent => rows.Where(child => child.Id > parent.Id).OrderBy(child => child.Id).Take(1)
                .Select(child => new { child.Id, child.Value }).DefaultIfEmpty(),
                (parent, child) => new { Parent = parent.Id, Child = child!.Id, child.Value });
            var result = await Run(query, async);
            Assert.Equal(new[] { 2 }, result.Select(r => r.Child));
            Assert.All(result, r => Assert.Null(r.Value));
            var sql = commands.Items[^1].Sql;
            Assert.DoesNotContain("COALESCE", sql);
            Assert.DoesNotContain(".Value, 0)", sql);
            Assert.Contains("LEFT JOIN (", sql);
            AssertClean(context, commands);
            commands.Results = Table([typeof(int), typeof(int), typeof(int)], [4, null, null]);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Run(query, async));
            AssertClean(context, commands);
        }

        [Theory]
        [MemberData(nameof(ModelMaterializationTests.TrackingCases), MemberType = typeof(ModelMaterializationTests))]
        public async Task Missing_entities_do_not_create_tracked_phantoms(OceanBaseCompatibilityMode mode, bool async, QueryTrackingBehavior tracking)
        {
            var commands = new Commands { Results = Table([typeof(int), typeof(int), typeof(string), typeof(int), typeof(int)],
                new object?[] { null, null, null, null, null }) };
            await using var context = Create(mode, commands);
            context.ChangeTracker.QueryTrackingBehavior = tracking;
            var query = context.Set<Row>().Where(r => r.Id > 20).DefaultIfEmpty();
            Assert.Null(Assert.Single(await Run(query, async)));
            Assert.Empty(context.ChangeTracker.Entries());
            commands.Results = Table([typeof(int), typeof(int), typeof(string), typeof(int), typeof(int)], [21, 1, "one", 107, null]);
            var first = Assert.IsType<Row>(Assert.Single(await Run(query, async)));
            Assert.Equal(21, first.Id);
            Assert.True(first.Active);
            Assert.Equal("one", first.Name);
            Assert.Equal(7, first.Offset);
            Assert.Null(first.Value);
            var second = Assert.Single(await Run(query, async));
            Assert.Equal(tracking == QueryTrackingBehavior.TrackAll, ReferenceEquals(first, second));
            Assert.Equal(tracking == QueryTrackingBehavior.TrackAll ? 1 : 0, context.ChangeTracker.Entries().Count());
            Assert.DoesNotContain("COALESCE", commands.Items[^1].Sql);
            AssertClean(context, commands);
        }

        [Theory]
        [MemberData(nameof(TransactionTests.Modes), MemberType = typeof(TransactionTests))]
        public async Task Nullable_default_composition_keeps_user_coalesce_and_aggregate_contracts(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new Commands { Results = Table([typeof(int)], [7], [null]) };
            await using var context = Create(mode, commands);
            var rows = context.Set<Row>();
            var explicitCoalesce = rows.Select(r => (int?)(r.Value ?? 7)).DefaultIfEmpty();
            Assert.Equal(new int?[] { 7, null }, await Run(explicitCoalesce, async));
            Assert.Single(Regex.Matches(commands.Items[^1].Sql, "COALESCE"));
            Assert.Contains(".Value, 7)", commands.Items[^1].Sql);
            commands.Results = Table([typeof(double)], new object?[] { null });
            var nullable = rows.Where(r => r.Id < 0).Select(r => r.Value).DefaultIfEmpty();
            Assert.Null(async ? await nullable.AverageAsync() : nullable.Average());
            Assert.Contains("AVG(", commands.Items[^1].Sql);
            Assert.DoesNotContain("COALESCE", commands.Items[^1].Sql);
            commands.Results = Table([typeof(int)], [0]);
            Assert.Equal(0, async ? await nullable.SumAsync() : nullable.Sum());
            Assert.Contains("SUM(", commands.Items[^1].Sql);
            commands.Results = Table([typeof(int)], [1]);
            Assert.Equal(1, async ? await nullable.CountAsync() : nullable.Count());
            Assert.Contains("COUNT(*)", commands.Items[^1].Sql);
            AssertClean(context, commands);
        }

        [Theory]
        [MemberData(nameof(TransactionTests.Modes), MemberType = typeof(TransactionTests))]
        public async Task Rejected_default_overloads_and_predicates_never_open_the_connection(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new Commands();
            await using var context = Create(mode, commands);
            var rows = context.Set<Row>();
            await Assert.ThrowsAsync<InvalidOperationException>(() => Run(rows.Select(r => r.Id).DefaultIfEmpty(42), async));
            await Assert.ThrowsAsync<InvalidOperationException>(() => Run(rows.Select(r => r.Id).DefaultIfEmpty().Where(value => Local(value)), async));
            Assert.Equal(0, commands.Openings.Count);
            Assert.Empty(commands.Items);
            AssertClean(context, commands);
        }

        [Theory]
        [MemberData(nameof(TransactionTests.Modes), MemberType = typeof(TransactionTests))]
        public async Task Command_cancellation_and_bad_reader_values_release_resources_and_allow_recovery(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new Commands { Results = Table([typeof(int)], new object?[] { null }) };
            await using var context = Create(mode, commands);
            var query = context.Set<Row>().Select(r => r.Value).DefaultIfEmpty();
            foreach (var cancel in new[] { false, true })
            {
                using var source = new CancellationTokenSource();
                commands.Failure = cancel ? new OperationCanceledException(source.Token) : new InvalidOperationException("query failed");
                commands.BeforeFailure = cancel ? source.Cancel : null;
                var error = await Record.ExceptionAsync(() => Run(query, async, source.Token));
                Assert.Same(commands.Failure, error);
                if (async) Assert.Equal(source.Token, commands.Items[^1].Token);
                AssertClean(context, commands);
                commands.Failure = null;
                Assert.Null(Assert.Single(await Run(query, async)));
                AssertClean(context, commands);
            }
            commands.Results = Table([typeof(string)], ["invalid"]);
            await Assert.ThrowsAsync<FormatException>(() => Run(query, async));
            AssertClean(context, commands);
            commands.Results = Table([typeof(int)], [9]);
            Assert.Equal(9, Assert.Single(await Run(query, async)));
            AssertClean(context, commands);
        }

        private static bool Local(int value) => value > 0;
        private static Task<List<T>> Run<T>(IQueryable<T> query, bool async, CancellationToken token = default)
            => async ? query.ToListAsync(token) : Task.FromResult(query.ToList());
        private static void AssertClean(DbContext context, Commands commands)
        {
            Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
            Assert.All(commands.Readers, reader => Assert.True(reader.IsClosed));
        }
        private static DataTable Table(Type[] types, params object?[][] rows)
        {
            var table = new DataTable();
            for (var i = 0; i < types.Length; i++) table.Columns.Add("C" + i, types[i]);
            foreach (var row in rows) table.Rows.Add(row.Select(v => v ?? DBNull.Value).ToArray());
            return table;
        }
        private static Context Create(OceanBaseCompatibilityMode mode, Commands commands) => new(new DbContextOptionsBuilder<Context>()
            .UseOceanBase("Server=offline;Database=app", mode).ReplaceService<IRelationalConnection, TransactionTests.TestConnection>()
            .AddInterceptors(commands, commands.Openings).Options);
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
            public bool Active { get; set; }
            public string? Name { get; set; }
            public int Offset { get; set; }
            public int? Value { get; set; }
        }
        private sealed record Command(string Sql, object?[] Inputs, CancellationToken Token);
        private sealed class Openings : DbConnectionInterceptor
        {
            public int Count { get; private set; }
            public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData e, InterceptionResult result)
            { Count++; return result; }
            public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData e,
                InterceptionResult result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(ConnectionOpening(connection, e, result));
        }
        private sealed class Commands : DbCommandInterceptor
        {
            public Openings Openings { get; } = new();
            public DataTable Results { get; set; } = new();
            public Exception? Failure { get; set; }
            public Action? BeforeFailure { get; set; }
            public List<Command> Items { get; } = [];
            public List<DbDataReader> Readers { get; } = [];
            private InterceptionResult<DbDataReader> Read(DbCommand command, CancellationToken token)
            {
                var parameters = command.Parameters.Cast<DbParameter>().ToArray();
                var position = 0;
                var used = new HashSet<DbParameter>();
                var inputs = Regex.Matches(command.CommandText, @"[@:]\w+|\?").Select(match =>
                {
                    var parameter = match.Value == "?" ? parameters[position++] : parameters.Single(p => p.ParameterName.TrimStart('@', ':') == match.Value.TrimStart('@', ':'));
                    used.Add(parameter);
                    return parameter.Value;
                }).ToArray();
                Assert.Equal(parameters.Length, used.Count);
                var sql = Regex.Replace(Regex.Replace(command.CommandText.Replace("`", "", StringComparison.Ordinal)
                    .Replace("\"", "", StringComparison.Ordinal).Trim(), @"[@:]\w+", "?"), @"\s+", " ")
                    .Replace(" AS ", " ", StringComparison.Ordinal);
                Items.Add(new Command(sql, inputs, token));
                if (Failure is not null) { BeforeFailure?.Invoke(); throw Failure; }
                var reader = Results.CreateDataReader();
                Readers.Add(reader);
                return InterceptionResult<DbDataReader>.SuppressWithResult(reader);
            }
            public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData e, InterceptionResult<DbDataReader> result)
                => Read(command, default);
            public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData e,
                InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(Read(command, cancellationToken));
        }
    }
}
