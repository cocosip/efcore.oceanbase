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
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Xunit;

namespace OceanBase.EntityFrameworkCore.FunctionalTests
{
    public sealed class BulkDmlTests
    {
        public static IEnumerable<object[]> Modes()
        {
            foreach (var mode in Enum.GetValues<OceanBaseCompatibilityMode>())
                foreach (var async in new[] { false, true }) yield return [mode, async];
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Direct_updates_bind_current_setters_and_filters_without_tracking_or_an_implicit_transaction(OceanBaseCompatibilityMode mode, bool async)
        {
            var capture = new Capture { AffectedRows = 7 };
            await using var context = Create(mode, capture);
            var tracked = new Row { TenantId = 3, Id = 1, Name = "tracked", Count = 10 };
            context.Attach(tracked);
            using var source = new CancellationTokenSource();
            var name = "x';--";
            var minimum = 4;
            var query = context.Set<Row>().Where(row => row.Count >= minimum).TagWith("bulk update");
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var affected = async ? await query.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Name, name)
                    .SetProperty(row => row.Count, row => row.Count + 2).SetProperty(row => row.Optional, (int?)null)
                    .SetProperty(row => row.Active, true).SetProperty(row => row.Status, Status.Ready), source.Token)
                    : query.ExecuteUpdate(setters => setters.SetProperty(row => row.Name, name)
                        .SetProperty(row => row.Count, row => row.Count + 2).SetProperty(row => row.Optional, (int?)null)
                        .SetProperty(row => row.Active, true).SetProperty(row => row.Status, Status.Ready));
                Assert.Equal(7, affected);
                var command = capture.Commands.Last();
                Assert.StartsWith("-- bulk update", command.Sql);
                Assert.Contains("UPDATE BulkRows", command.Sql);
                Assert.Contains(" SET ", command.Sql);
                Assert.Contains("Optional = NULL", command.Sql);
                Assert.Contains("Y", command.Values);
                Assert.Contains("Ready", command.Values);
                Assert.Contains(name, command.Values);
                Assert.Contains(minimum, command.Values);
                Assert.Contains(context.TenantId, command.Values);
                Assert.DoesNotContain(name, command.Sql);
                Assert.Null(command.Transaction);
                Assert.Equal(async, command.Async);
                Assert.Equal(async ? source.Token : default, command.Token);
                Assert.Equal(new object[] { name, "Y", "Ready", context.TenantId, minimum }, command.Values);
                name = "new";
                minimum = 8;
                context.TenantId = 5;
            }
            Assert.Equal(2, capture.Commands.Count);
            Assert.Equal("tracked", tracked.Name);
            Assert.Equal(10, tracked.Count);
            Assert.Equal(EntityState.Unchanged, context.Entry(tracked).State);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Delete_counts_and_named_filter_controls_do_not_apply_tracked_concurrency(OceanBaseCompatibilityMode mode, bool async)
        {
            var capture = new Capture { AffectedRows = 0 };
            await using var context = Create(mode, capture);
            var tracked = new Row { TenantId = 3, Id = 4, Count = 6 };
            context.Attach(tracked);
            var query = context.Set<Row>().Where(row => row.Id == 4);
            Assert.Equal(0, async ? await query.ExecuteDeleteAsync() : query.ExecuteDelete());
            var filtered = Assert.Single(capture.Commands);
            Assert.StartsWith("DELETE FROM BulkRows", filtered.Sql);
            Assert.Contains("TenantId", filtered.Sql);
            Assert.Contains("Id = 4", filtered.Sql);
            Assert.DoesNotContain("Count", filtered.Sql);
            query = context.Set<Row>().IgnoreQueryFilters(["Tenant"]).TagWith("bulk delete");
            Assert.Equal(0, async ? await query.ExecuteDeleteAsync() : query.ExecuteDelete());
            Assert.DoesNotContain("WHERE", capture.Commands.Last().Sql);
            Assert.StartsWith("-- bulk delete DELETE FROM", capture.Commands.Last().Sql);
            Assert.Equal(EntityState.Unchanged, context.Entry(tracked).State);
            Assert.All(capture.Commands, command => Assert.Null(command.Transaction));
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Direct_setter_bounds_reject_before_open_but_query_operands_keep_their_transport(OceanBaseCompatibilityMode mode, bool async)
        {
            var capture = new Capture();
            await using var context = Create(mode, capture);
            var value = "too long for eight characters";
            var query = context.Set<Row>().Where(row => row.Name == value);
            if (async) await Assert.ThrowsAsync<ArgumentException>(() => query.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Name, value)));
            else Assert.Throws<ArgumentException>(() => query.ExecuteUpdate(setters => setters.SetProperty(row => row.Name, value)));
            Assert.Equal(0, capture.Opens);
            Assert.Empty(capture.Commands);
            Assert.Equal(1, async ? await query.ExecuteDeleteAsync() : query.ExecuteDelete());
            Assert.Contains(value, Assert.Single(capture.Commands).Values);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Cached_setters_revalidate_each_current_value_before_open(OceanBaseCompatibilityMode mode, bool async)
        {
            var capture = new Capture();
            await using var context = Create(mode, capture);
            var value = "valid";
            async Task<int> Update() => async
                ? await context.Set<Row>().ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Name, value))
                : context.Set<Row>().ExecuteUpdate(setters => setters.SetProperty(row => row.Name, value));
            Assert.Equal(1, await Update());
            var opens = capture.Opens;
            value = "this exceeds the column";
            await Assert.ThrowsAsync<ArgumentException>(Update);
            Assert.Equal(opens, capture.Opens);
            Assert.Single(capture.Commands);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Multiple_setters_preserve_original_row_values(OceanBaseCompatibilityMode mode, bool async)
        {
            var capture = new Capture();
            await using var context = Create(mode, capture);
            Assert.Equal(1, async ? await context.Set<Row>().ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.Count, row => row.Count + 1).SetProperty(row => row.Previous, row => row.Count))
                : context.Set<Row>().ExecuteUpdate(setters => setters
                    .SetProperty(row => row.Count, row => row.Count + 1).SetProperty(row => row.Previous, row => row.Count)));
            var sql = Assert.Single(capture.Commands).Sql;
            if (mode == OceanBaseCompatibilityMode.MySql)
            {
                Assert.Matches(@"SET \w+\.Previous = \w+\.Count, \w+\.Count = \w+\.Count \+ 1", sql);
            }
            else Assert.Matches(@"SET Count = \w+\.Count \+ 1, Previous = \w+\.Count", sql);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Assignment_cycles_use_MySql_snapshots_or_native_Oracle_simultaneous_assignment(OceanBaseCompatibilityMode mode, bool async)
        {
            var capture = new Capture();
            await using var context = Create(mode, capture);
            async Task<int> Update() => async ? await context.Set<Row>().ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.Count, row => row.Previous).SetProperty(row => row.Previous, row => row.Count))
                : context.Set<Row>().ExecuteUpdate(setters => setters
                    .SetProperty(row => row.Count, row => row.Previous).SetProperty(row => row.Previous, row => row.Count));
            Assert.Equal(1, await Update());
            var sql = Assert.Single(capture.Commands).Sql;
            if (mode == OceanBaseCompatibilityMode.MySql)
            {
                Assert.Contains("SELECT /*+ NO_MERGE */", sql);
                Assert.Contains(".Previous AS __ob_value_0", sql);
                Assert.Contains(".Count AS __ob_value_1", sql);
                Assert.Contains("SET __ob_bulk_target.Count = __ob_bulk_snapshot.__ob_value_0, __ob_bulk_target.Previous = __ob_bulk_snapshot.__ob_value_1", sql);
            }
            else Assert.Matches(@"SET Count = \w+\.Previous, Previous = \w+\.Count", sql);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Joined_updates_use_mode_specific_target_and_source_sql(OceanBaseCompatibilityMode mode, bool async)
        {
            var capture = new Capture();
            await using var context = Create(mode, capture);
            var code = "group";
            var query = context.Set<Row>().Join(context.Set<Group>(), row => row.GroupId, group => group.Id,
                (row, group) => new { Row = row, Group = group }).Where(pair => pair.Group.Code == code);
            async Task<int> Update() => async ? await query.ExecuteUpdateAsync(setters => setters.SetProperty(pair => pair.Row.Name, pair => pair.Group.Code))
                : query.ExecuteUpdate(setters => setters.SetProperty(pair => pair.Row.Name, pair => pair.Group.Code));
            Assert.Equal(1, await Update());
            var command = Assert.Single(capture.Commands);
            if (mode == OceanBaseCompatibilityMode.MySql)
            {
                Assert.Matches(@"UPDATE BulkRows AS (\w+) INNER JOIN BulkGroups AS (\w+) ON \1\.GroupId = \2\.Id SET \1\.Name = \2\.Code WHERE", command.Sql);
                Assert.DoesNotContain(" FROM ", command.Sql);
            }
            else
            {
                Assert.StartsWith("UPDATE BulkRows __ob_bulk_target SET Name = (SELECT", command.Sql);
                Assert.Contains("INNER JOIN BulkGroups", command.Sql);
                Assert.Contains("WHERE EXISTS (SELECT 1", command.Sql);
            }
            Assert.Contains(code, command.Values);
            Assert.Contains(context.TenantId, command.Values);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task MySql_joined_assignment_dependencies_use_projected_snapshot_values(bool async)
        {
            var capture = new Capture();
            await using var context = Create(OceanBaseCompatibilityMode.MySql, capture);
            var query = context.Set<Row>().Join(context.Set<Group>(), row => row.GroupId, group => group.Id,
                (row, group) => new { Row = row, Group = group });
            async Task<int> Update() => async ? await query.ExecuteUpdateAsync(setters => setters
                .SetProperty(pair => pair.Row.Count, pair => pair.Row.Count + 1).SetProperty(pair => pair.Row.Previous, pair => pair.Row.Count))
                : query.ExecuteUpdate(setters => setters
                    .SetProperty(pair => pair.Row.Count, pair => pair.Row.Count + 1).SetProperty(pair => pair.Row.Previous, pair => pair.Row.Count));
            Assert.Equal(1, await Update());
            var command = Assert.Single(capture.Commands);
            Assert.Contains("SELECT /*+ NO_MERGE */", command.Sql);
            Assert.Contains("INNER JOIN BulkGroups", command.Sql);
            Assert.Contains(".Count + 1 AS __ob_value_0", command.Sql);
            Assert.Contains(".Count AS __ob_value_1", command.Sql);
            Assert.Contains("__ob_bulk_target.TenantId = __ob_bulk_snapshot.__ob_key_0 AND __ob_bulk_target.Id = __ob_bulk_snapshot.__ob_key_1", command.Sql);
            Assert.Equal(new object[] { context.TenantId }, command.Values);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task MySql_snapshot_sql_preserves_all_keys_current_binds_tags_and_write_validation(bool async)
        {
            var capture = new Capture();
            await using var context = Create(OceanBaseCompatibilityMode.MySql, capture);
            context.Database.SetCommandTimeout(19);
            var floor = 2;
            var delta = 3;
            var name = "x'--";
            var query = context.Set<Row>().Where(row => row.Id > floor).TagWith("snapshot");
            async Task<int> Update() => async ? await query.ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.Count, row => row.Previous + delta).SetProperty(row => row.Previous, row => row.Count)
                .SetProperty(row => row.Name, name))
                : query.ExecuteUpdate(setters => setters.SetProperty(row => row.Count, row => row.Previous + delta)
                    .SetProperty(row => row.Previous, row => row.Count).SetProperty(row => row.Name, name));
            for (var attempt = 0; attempt < 2; attempt++)
            {
                Assert.Equal(1, await Update());
                var command = capture.Commands.Last();
                Assert.Equal("-- snapshot UPDATE BulkRows AS __ob_bulk_target INNER JOIN ( SELECT /*+ NO_MERGE */ b.TenantId AS __ob_key_0, b.Id AS __ob_key_1, b.Previous + ? AS __ob_value_0, b.Count AS __ob_value_1, ? AS __ob_value_2 FROM BulkRows AS b WHERE b.TenantId = ? AND b.Id > ? ) AS __ob_bulk_snapshot ON __ob_bulk_target.TenantId = __ob_bulk_snapshot.__ob_key_0 AND __ob_bulk_target.Id = __ob_bulk_snapshot.__ob_key_1 SET __ob_bulk_target.Count = __ob_bulk_snapshot.__ob_value_0, __ob_bulk_target.Previous = __ob_bulk_snapshot.__ob_value_1, __ob_bulk_target.Name = __ob_bulk_snapshot.__ob_value_2", command.Sql);
                Assert.Equal(new object[] { delta, name, context.TenantId, floor }, command.Values);
                Assert.DoesNotContain(name, command.Sql);
                Assert.Null(command.Transaction);
                Assert.Equal(19, command.Timeout);
                floor = 6;
                delta = 8;
                name = "next";
                context.TenantId = 9;
            }
            name = "longer than eight";
            var opens = capture.Opens;
            await Assert.ThrowsAsync<ArgumentException>(Update);
            Assert.Equal(opens, capture.Opens);
            Assert.Equal(2, capture.Commands.Count);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public async Task MySql_nullable_Boolean_swaps_encode_each_snapshot_value_with_or_without_a_left_join(bool async, bool leftJoin)
        {
            var capture = new Capture();
            await using var context = Create(OceanBaseCompatibilityMode.MySql, capture);
            var query = context.Set<Row>();
            int count;
            if (leftJoin)
            {
                var joined = query.GroupJoin(context.Set<Group>(), row => row.GroupId, group => group.Id,
                        (row, groups) => new { Row = row, Groups = groups })
                    .SelectMany(pair => pair.Groups.DefaultIfEmpty(), (pair, group) => new { pair.Row, Group = group });
                count = async ? await joined.ExecuteUpdateAsync(setters => setters.SetProperty(pair => pair.Row.OptionalFlag, pair => pair.Row.OptionalSourceFlag)
                    .SetProperty(pair => pair.Row.OptionalSourceFlag, pair => pair.Row.OptionalFlag))
                    : joined.ExecuteUpdate(setters => setters.SetProperty(pair => pair.Row.OptionalFlag, pair => pair.Row.OptionalSourceFlag)
                        .SetProperty(pair => pair.Row.OptionalSourceFlag, pair => pair.Row.OptionalFlag));
            }
            else count = async ? await query.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.OptionalFlag, row => row.OptionalSourceFlag)
                .SetProperty(row => row.OptionalSourceFlag, row => row.OptionalFlag))
                : query.ExecuteUpdate(setters => setters.SetProperty(row => row.OptionalFlag, row => row.OptionalSourceFlag)
                    .SetProperty(row => row.OptionalSourceFlag, row => row.OptionalFlag));
            Assert.Equal(1, count);
            var command = Assert.Single(capture.Commands);
            Assert.Contains("CASE WHEN b.OptionalSourceFlag = 'T' THEN 'Y' WHEN b.OptionalSourceFlag = 'F' THEN 'N' END AS __ob_value_0", command.Sql);
            Assert.Contains("CASE WHEN b.OptionalFlag = 'Y' THEN 'T' WHEN b.OptionalFlag = 'N' THEN 'F' END AS __ob_value_1", command.Sql);
            Assert.Contains("SET __ob_bulk_target.OptionalFlag = __ob_bulk_snapshot.__ob_value_0, __ob_bulk_target.OptionalSourceFlag = __ob_bulk_snapshot.__ob_value_1", command.Sql);
            Assert.Equal(leftJoin, command.Sql.Contains("LEFT JOIN BulkGroups", StringComparison.Ordinal));
            Assert.DoesNotContain("ELSE 'N'", command.Sql);
            Assert.DoesNotContain("ELSE 'F'", command.Sql);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task MySql_snapshot_key_updates_and_paging_keep_original_key_projections(bool async)
        {
            var capture = new Capture();
            await using var context = Create(OceanBaseCompatibilityMode.MySql, capture);
            var query = context.Set<Row>().OrderBy(row => row.Id).Skip(1).Take(2);
            Assert.Equal(1, async ? await query.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Id, row => row.Previous)
                .SetProperty(row => row.Previous, row => row.Id))
                : query.ExecuteUpdate(setters => setters.SetProperty(row => row.Id, row => row.Previous)
                    .SetProperty(row => row.Previous, row => row.Id)));
            var command = Assert.Single(capture.Commands);
            Assert.Contains("SELECT /*+ NO_MERGE */", command.Sql);
            Assert.Single(Regex.Matches(command.Sql, "LIMIT "));
            Assert.Single(Regex.Matches(command.Sql, "OFFSET "));
            Assert.Contains("ORDER BY", command.Sql);
            Assert.Contains("AS __ob_key_0", command.Sql);
            Assert.Contains("AS __ob_key_1", command.Sql);
            Assert.Contains("__ob_bulk_target.Id = __ob_bulk_snapshot.__ob_key_1 SET __ob_bulk_target.Id = __ob_bulk_snapshot.__ob_value_0", command.Sql);
            Assert.Contains(1, command.Values);
            Assert.Contains(2, command.Values);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task MySql_keyless_snapshot_rejects_before_open_instead_of_losing_row_identity(bool async)
        {
            var capture = new Capture();
            await using var context = Create(OceanBaseCompatibilityMode.MySql, capture);
            async Task<int> Update() => async ? await context.Set<KeylessRow>().ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.Count, row => row.Previous).SetProperty(row => row.Previous, row => row.Count))
                : context.Set<KeylessRow>().ExecuteUpdate(setters => setters.SetProperty(row => row.Count, row => row.Previous)
                    .SetProperty(row => row.Previous, row => row.Count));
            Assert.Contains("primary key", (await Assert.ThrowsAsync<NotSupportedException>(Update)).Message);
            Assert.Equal(0, capture.Opens);
            Assert.Empty(capture.Commands);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public async Task MySql_snapshot_failures_preserve_identity_tokens_and_tracker_state(bool async, bool cancellation)
        {
            var capture = new Capture { AffectedRows = 0 };
            await using var context = Create(OceanBaseCompatibilityMode.MySql, capture);
            var tracked = new Row { TenantId = 3, Id = 1, Count = 7, Previous = 9 };
            context.Attach(tracked);
            using var source = new CancellationTokenSource();
            Exception failure = cancellation ? new OperationCanceledException(source.Token) : new InvalidOperationException("snapshot command failure");
            capture.Failure = failure;
            if (cancellation) capture.BeforeFailure = source.Cancel;
            async Task<int> Update(CancellationToken token) => async ? await context.Set<Row>().ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.Count, row => row.Previous).SetProperty(row => row.Previous, row => row.Count), token)
                : context.Set<Row>().ExecuteUpdate(setters => setters.SetProperty(row => row.Count, row => row.Previous)
                    .SetProperty(row => row.Previous, row => row.Count));
            Assert.Same(failure, await Record.ExceptionAsync(() => Update(source.Token)));
            var command = Assert.Single(capture.Commands);
            Assert.Contains("SELECT /*+ NO_MERGE */", command.Sql);
            Assert.Equal(async ? source.Token : default, command.Token);
            Assert.Null(command.Transaction);
            Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
            capture.Failure = null;
            capture.BeforeFailure = null;
            Assert.Equal(0, await Update(default));
            Assert.Equal(2, capture.Commands.Count);
            Assert.Equal(7, tracked.Count);
            Assert.Equal(9, tracked.Previous);
            Assert.Equal(EntityState.Unchanged, context.Entry(tracked).State);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Literal_and_captured_setters_share_decimal_write_bounds_and_preserve_enum_conversion(OceanBaseCompatibilityMode mode, bool async)
        {
            var capture = new Capture();
            await using var context = Create(mode, capture);
            var value = 1.23m;
            async Task<int> Captured() => async ? await context.Set<Row>().ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Price, value))
                : context.Set<Row>().ExecuteUpdate(setters => setters.SetProperty(row => row.Price, value));
            async Task<int> Literal() => async ? await context.Set<Row>().ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Price, row => 1.23m))
                : context.Set<Row>().ExecuteUpdate(setters => setters.SetProperty(row => row.Price, row => 1.23m));
            await Assert.ThrowsAsync<ArgumentException>(Captured);
            await Assert.ThrowsAsync<ArgumentException>(Literal);
            Assert.Equal(0, capture.Opens);
            Assert.Empty(capture.Commands);
            Assert.Equal(1, async ? await context.Set<Row>().ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Status, row => Status.Done))
                : context.Set<Row>().ExecuteUpdate(setters => setters.SetProperty(row => row.Status, row => Status.Done)));
            Assert.Contains(mode == OceanBaseCompatibilityMode.Oracle ? "Status = N'Done'" : "Status = 'Done'", Assert.Single(capture.Commands).Sql);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Computed_and_cross_column_Boolean_setters_encode_the_target_values(OceanBaseCompatibilityMode mode, bool async)
        {
            var capture = new Capture();
            await using var context = Create(mode, capture);
            Assert.Equal(1, async ? await context.Set<Row>().ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Active, row => row.Count > 0))
                : context.Set<Row>().ExecuteUpdate(setters => setters.SetProperty(row => row.Active, row => row.Count > 0)));
            Assert.Equal(1, async ? await context.Set<Row>().ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Active, row => row.SourceFlag))
                : context.Set<Row>().ExecuteUpdate(setters => setters.SetProperty(row => row.Active, row => row.SourceFlag)));
            var prefix = mode == OceanBaseCompatibilityMode.Oracle ? "N" : "";
            Assert.All(capture.Commands, command =>
            {
                Assert.Contains("Active = CASE", command.Sql);
                Assert.Contains($"THEN {prefix}'Y'", command.Sql);
                Assert.Contains($"THEN {prefix}'N'", command.Sql);
            });
            Assert.Contains($"SourceFlag = {prefix}'T'", capture.Commands.Last().Sql);
            Assert.Contains($"SourceFlag = {prefix}'F'", capture.Commands.Last().Sql);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Arbitrary_cross_converter_server_setters_reject_before_open(OceanBaseCompatibilityMode mode, bool async)
        {
            var capture = new Capture();
            await using var context = Create(mode, capture);
            async Task<int> Update() => async ? await context.Set<Row>().ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Name, row => row.EncodedName))
                : context.Set<Row>().ExecuteUpdate(setters => setters.SetProperty(row => row.Name, row => row.EncodedName));
            var error = await Assert.ThrowsAsync<NotSupportedException>(Update);
            Assert.Contains("converter", error.Message);
            Assert.Equal(0, capture.Opens);
            Assert.Empty(capture.Commands);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Nullable_cross_encoding_setters_preserve_SQL_null_instead_of_false(OceanBaseCompatibilityMode mode, bool async)
        {
            var capture = new Capture();
            await using var context = Create(mode, capture);
            Assert.Equal(1, async ? await context.Set<Row>().ExecuteUpdateAsync(setters => setters.SetProperty(row => row.OptionalFlag, row => row.OptionalSourceFlag))
                : context.Set<Row>().ExecuteUpdate(setters => setters.SetProperty(row => row.OptionalFlag, row => row.OptionalSourceFlag)));
            var sql = Assert.Single(capture.Commands).Sql;
            var prefix = mode == OceanBaseCompatibilityMode.Oracle ? "N" : "";
            Assert.Contains("OptionalFlag = CASE", sql);
            Assert.Contains($"OptionalSourceFlag = {prefix}'T' THEN {prefix}'Y'", sql);
            Assert.Contains($"OptionalSourceFlag = {prefix}'F' THEN {prefix}'N'", sql);
            Assert.DoesNotContain($"ELSE {prefix}'N'", sql);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public async Task Cancellation_keeps_the_original_error_and_token_without_implicit_transaction(OceanBaseCompatibilityMode mode)
        {
            var capture = new Capture();
            await using var context = Create(mode, capture);
            using var pre = new CancellationTokenSource();
            pre.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.Set<Row>().ExecuteDeleteAsync(pre.Token));
            Assert.Empty(capture.Commands);
            using var source = new CancellationTokenSource();
            var failure = new OperationCanceledException(source.Token);
            capture.Failure = failure;
            capture.BeforeFailure = source.Cancel;
            var actual = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.Set<Row>()
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Count, 2), source.Token));
            Assert.Same(failure, actual);
            var command = Assert.Single(capture.Commands);
            Assert.Equal(source.Token, command.Token);
            Assert.Null(command.Transaction);
        }

        private static TestContext Create(OceanBaseCompatibilityMode mode, Capture capture)
            => new(new DbContextOptionsBuilder<TestContext>().UseOceanBase("Server=offline;Database=offline", mode).AddInterceptors(capture).Options);

        private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
        {
            public int TenantId { get; set; } = 3;
            protected override void OnModelCreating(ModelBuilder model)
            {
                var row = model.Entity<Row>();
                row.ToTable("BulkRows");
                row.HasKey(value => new { value.TenantId, value.Id });
                row.HasQueryFilter("Tenant", value => value.TenantId == TenantId);
                row.Property(value => value.Name).HasMaxLength(8);
                row.Property(value => value.Count).IsConcurrencyToken();
                row.Property(value => value.Active).HasConversion(new BoolToStringConverter("N", "Y"));
                row.Property(value => value.SourceFlag).HasConversion(new BoolToStringConverter("F", "T"));
                row.Property(value => value.OptionalFlag).HasConversion(new BoolToStringConverter("N", "Y"));
                row.Property(value => value.OptionalSourceFlag).HasConversion(new BoolToStringConverter("F", "T"));
                row.Property(value => value.EncodedName).HasConversion(value => "prefix:" + value, value => value.Substring(7));
                row.Property(value => value.Status).HasConversion<string>();
                row.Property(value => value.Price).HasPrecision(4, 1);
                model.Entity<Group>().ToTable("BulkGroups");
                model.Entity<KeylessRow>().ToTable("BulkKeylessRows").HasNoKey();
            }
        }

        private enum Status { Ready, Done }
        private sealed class Row
        {
            public int TenantId { get; set; }
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public int Count { get; set; }
            public int Previous { get; set; }
            public int? Optional { get; set; }
            public bool Active { get; set; }
            public bool SourceFlag { get; set; }
            public bool? OptionalFlag { get; set; }
            public bool? OptionalSourceFlag { get; set; }
            public string EncodedName { get; set; } = "";
            public Status Status { get; set; }
            public int GroupId { get; set; }
            public decimal Price { get; set; }
        }
        private sealed class Group { public int Id { get; set; } public string Code { get; set; } = ""; }
        private sealed class KeylessRow { public int Count { get; set; } public int Previous { get; set; } }
        private sealed record Command(string Sql, object[] Values, bool Async, CancellationToken Token, DbTransaction? Transaction, int Timeout);

        private sealed class Capture : DbConnectionInterceptor, IDbCommandInterceptor
        {
            public List<Command> Commands { get; } = [];
            public int Opens { get; private set; }
            public int AffectedRows { get; init; } = 1;
            public Exception? Failure { get; set; }
            public Action? BeforeFailure { get; set; }
            public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
            { Opens++; return InterceptionResult.Suppress(); }
            public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
                InterceptionResult result, CancellationToken cancellationToken = default)
            { cancellationToken.ThrowIfCancellationRequested(); return ValueTask.FromResult(ConnectionOpening(connection, eventData, result)); }
            public InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
                => Execute(command, false, default);
            public ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
                InterceptionResult<int> result, CancellationToken cancellationToken = default) => ValueTask.FromResult(Execute(command, true, cancellationToken));
            private InterceptionResult<int> Execute(DbCommand command, bool async, CancellationToken token)
            {
                var sql = Regex.Replace(command.CommandText.Replace("`", "", StringComparison.Ordinal).Replace("\"", "", StringComparison.Ordinal), @"\s+", " ").Trim();
                var parameters = command.Parameters.Cast<DbParameter>().ToArray();
                var position = 0;
                var values = Regex.Matches(sql, @"[:@]\w+|\?", RegexOptions.CultureInvariant).Select(bind =>
                    bind.Value == "?" ? parameters[position++].Value!
                        : parameters.Single(parameter => parameter.ParameterName.TrimStart(':', '@') == bind.Value.TrimStart(':', '@')).Value!).ToArray();
                Commands.Add(new Command(sql, values, async, token, command.Transaction, command.CommandTimeout));
                if (Failure is { } failure) { BeforeFailure?.Invoke(); throw failure; }
                return InterceptionResult<int>.SuppressWithResult(AffectedRows);
            }
        }
    }
}
