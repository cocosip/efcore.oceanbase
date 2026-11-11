using System;
using System.Collections.Generic;
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
    public sealed class OracleBulkDmlTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Joined_setters_preserve_the_full_source_predicate_and_every_target_key(bool async)
        {
            var capture = new Capture();
            await using var context = Create(capture);
            context.Database.SetCommandTimeout(23);
            using var source = new CancellationTokenSource();
            var code = "dept";
            var query = context.Set<Row>().Join(context.Set<Group>(), row => row.GroupId, group => group.Id,
                (row, group) => new { Row = row, Group = group }).Where(pair => pair.Group.Code == code).TagWith("oracle joined");
            for (var attempt = 0; attempt < 2; attempt++)
            {
                Assert.Equal(4, async ? await query.ExecuteUpdateAsync(setters => setters.SetProperty(pair => pair.Row.Name, pair => pair.Group.Code), source.Token)
                    : query.ExecuteUpdate(setters => setters.SetProperty(pair => pair.Row.Name, pair => pair.Group.Code)));
                var command = capture.Commands.Last();
                const string sourceSql = "FROM OracleRows o INNER JOIN OracleGroups o0 ON o.GroupId = o0.Id WHERE (o.TenantId = ? AND o0.Code = ?) AND o.TenantId = __ob_bulk_target.TenantId AND o.Id = __ob_bulk_target.Id";
                Assert.Equal("-- oracle joined UPDATE OracleRows __ob_bulk_target SET Name = (SELECT o0.Code " + sourceSql
                    + ") WHERE EXISTS (SELECT 1 " + sourceSql + ")", command.Template);
                Assert.Equal(new object[] { context.TenantId, code, context.TenantId, code }, command.Values);
                Assert.Equal(23, command.Timeout);
                Assert.Equal(async, command.Async);
                Assert.Equal(async ? source.Token : default, command.Token);
                Assert.Null(command.Transaction);
                Assert.DoesNotContain(code, command.Template);
                code = "other";
                context.TenantId = 7;
            }
        }

        [Theory]
        [InlineData(false, 0)]
        [InlineData(true, 0)]
        [InlineData(false, 3)]
        [InlineData(true, 3)]
        public async Task Pushed_down_order_skip_take_stay_inside_the_source_query(bool async, int take)
        {
            var capture = new Capture();
            await using var context = Create(capture);
            var skip = 2;
            var query = context.Set<Row>().Where(row => row.Count > 10).OrderBy(row => row.Id).Skip(skip).Take(take);
            Assert.Equal(4, async ? await query.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Name, "paged"))
                : query.ExecuteUpdate(setters => setters.SetProperty(row => row.Name, "paged")));
            var command = Assert.Single(capture.Commands);
            Assert.StartsWith("UPDATE OracleRows __ob_bulk_target SET Name = (SELECT", command.Template);
            Assert.Equal(2, Regex.Matches(command.Template, "OFFSET ").Count);
            Assert.Equal(2, Regex.Matches(command.Template, "FETCH NEXT ").Count);
            Assert.Equal(2, Regex.Matches(command.Template, "ORDER BY ").Count);
            Assert.Contains("INNER JOIN ( SELECT", command.Template);
            Assert.Equal(2, Regex.Matches(command.Template, " = __ob_bulk_target.TenantId").Count);
            Assert.Equal(2, Regex.Matches(command.Template, " = __ob_bulk_target.Id").Count);
            Assert.Contains(skip, command.Values);
            Assert.Contains(take, command.Values);
            Assert.DoesNotContain("UPDATE OracleRows AS", command.Template);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Left_join_nullable_values_and_multiple_old_value_setters_keep_the_complete_source(bool async)
        {
            var capture = new Capture();
            await using var context = Create(capture);
            var query = context.Set<Row>().GroupJoin(context.Set<Group>(), row => row.GroupId, group => group.Id,
                    (row, groups) => new { Row = row, Groups = groups })
                .SelectMany(pair => pair.Groups.DefaultIfEmpty(), (pair, group) => new { pair.Row, Group = group });
            Assert.Equal(4, async ? await query.ExecuteUpdateAsync(setters => setters
                .SetProperty(pair => pair.Row.Name, pair => pair.Group == null ? null : pair.Group.Code)
                .SetProperty(pair => pair.Row.Count, pair => pair.Row.Previous)
                .SetProperty(pair => pair.Row.Previous, pair => pair.Row.Count))
                : query.ExecuteUpdate(setters => setters
                    .SetProperty(pair => pair.Row.Name, pair => pair.Group == null ? null : pair.Group.Code)
                    .SetProperty(pair => pair.Row.Count, pair => pair.Row.Previous)
                    .SetProperty(pair => pair.Row.Previous, pair => pair.Row.Count)));
            var command = Assert.Single(capture.Commands);
            Assert.Equal(4, Regex.Matches(command.Template, "LEFT JOIN OracleGroups").Count);
            Assert.Equal(3, Regex.Matches(command.Template, @" = \(SELECT ").Count);
            Assert.Contains("Name = (SELECT o0.Code", command.Template);
            Assert.Contains("Count = (SELECT o.Previous", command.Template);
            Assert.Contains("Previous = (SELECT o.Count", command.Template);
            Assert.Single(Regex.Matches(command.Template, "WHERE EXISTS"));
            Assert.DoesNotContain("SELECT DISTINCT", command.Template);
            Assert.DoesNotContain("MIN(", command.Template);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public async Task Target_on_the_joined_side_uses_its_own_key_and_target_Boolean_encoding(bool async, bool filtered)
        {
            var capture = new Capture();
            await using var context = Create(capture);
            var query = context.Set<Group>().Join(context.Set<Row>(), group => group.Id, row => row.GroupId,
                (group, row) => new { Group = group, Row = row });
            if (!filtered) query = query.IgnoreQueryFilters();
            Assert.Equal(4, async ? await query.ExecuteUpdateAsync(setters => setters.SetProperty(pair => pair.Row.Active, pair => pair.Group.Enabled))
                : query.ExecuteUpdate(setters => setters.SetProperty(pair => pair.Row.Active, pair => pair.Group.Enabled)));
            var command = Assert.Single(capture.Commands);
            Assert.StartsWith("UPDATE OracleRows __ob_bulk_target SET Active = (SELECT CASE", command.Template);
            Assert.Contains("THEN N'Y'", command.Template);
            Assert.Contains("THEN N'N'", command.Template);
            var target = filtered ? "o2" : "o0";
            Assert.Equal(2, Regex.Matches(command.Template, $@"{target}.TenantId = __ob_bulk_target.TenantId AND {target}.Id = __ob_bulk_target.Id").Count);
            Assert.Equal(filtered ? new object[] { context.TenantId, context.TenantId } : [], command.Values);
            Assert.DoesNotContain("__ob_bulk_target.GroupId", command.Template);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Explicit_schema_and_renamed_converted_key_identifiers_are_quoted_and_all_keys_correlate(bool async)
        {
            var capture = new Capture();
            await using var context = Create(capture);
            var replacement = Guid.Parse("7ce4ee34-2b8e-4864-a217-85d67e3b3ac7");
            var query = context.Set<QuotedRow>().Join(context.Set<Group>(), row => row.GroupId, group => group.Id,
                (row, group) => new { Row = row, Group = group });
            Assert.Equal(4, async ? await query.ExecuteUpdateAsync(setters => setters.SetProperty(pair => pair.Row.BusinessId, replacement))
                : query.ExecuteUpdate(setters => setters.SetProperty(pair => pair.Row.BusinessId, replacement)));
            var command = Assert.Single(capture.Commands);
            Assert.StartsWith("UPDATE \"Own\"\"Scope\".\"Rows\"\"Target\" \"__ob_bulk_target\"", command.Raw);
            Assert.Contains("\"business\"\"key\" = (SELECT", command.Raw);
            Assert.Equal(2, Regex.Matches(command.Raw, @" = ""__ob_bulk_target""\.""business""""key""").Count);
            Assert.Equal(2, Regex.Matches(command.Raw, @" = ""__ob_bulk_target""\.""TenantId""").Count);
            Assert.Contains(replacement.ToString(), command.Values);
            Assert.DoesNotContain("MERGE", command.Raw);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Self_joins_correlate_the_target_alias_and_keep_the_second_source_alias(bool async)
        {
            var capture = new Capture();
            await using var context = Create(capture);
            var query = context.Set<Row>().Join(context.Set<Row>(), first => first.GroupId, second => second.Id,
                (first, second) => new { Target = first, Source = second });
            Assert.Equal(4, async ? await query.ExecuteUpdateAsync(setters => setters.SetProperty(pair => pair.Target.Name, pair => pair.Source.Name))
                : query.ExecuteUpdate(setters => setters.SetProperty(pair => pair.Target.Name, pair => pair.Source.Name)));
            var command = Assert.Single(capture.Commands);
            Assert.Contains("Name = (SELECT o1.Name", command.Template);
            Assert.Contains("INNER JOIN ( SELECT o0.Id, o0.Name FROM OracleRows o0 WHERE o0.TenantId = ? ) o1", command.Template);
            Assert.Equal(new object[] { context.TenantId, context.TenantId, context.TenantId, context.TenantId }, command.Values);
            Assert.Equal(2, Regex.Matches(command.Template, @"o.TenantId = __ob_bulk_target.TenantId AND o.Id = __ob_bulk_target.Id").Count);
            Assert.DoesNotContain("o0.Id = __ob_bulk_target.Id", command.Template);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Nullable_left_join_targets_use_their_own_key_and_preserve_nullable_Boolean_encodings(bool async)
        {
            var capture = new Capture();
            await using var context = Create(capture);
            var query = context.Set<Row>().GroupJoin(context.Set<Group>(), row => row.GroupId, group => group.Id,
                    (row, groups) => new { Row = row, Groups = groups })
                .SelectMany(pair => pair.Groups.DefaultIfEmpty(), (pair, group) => new { pair.Row, Group = group });
            Assert.Equal(4, async ? await query.ExecuteUpdateAsync(setters => setters.SetProperty(pair => pair.Group!.OptionalEnabled, pair => pair.Row.OptionalActive))
                : query.ExecuteUpdate(setters => setters.SetProperty(pair => pair.Group!.OptionalEnabled, pair => pair.Row.OptionalActive)));
            var command = Assert.Single(capture.Commands);
            Assert.StartsWith("UPDATE OracleGroups __ob_bulk_target SET OptionalEnabled = (SELECT CASE", command.Template);
            Assert.Contains("o.OptionalActive = N'Y' THEN N'T'", command.Template);
            Assert.Contains("o.OptionalActive = N'N' THEN N'F' END", command.Template);
            Assert.Equal(2, Regex.Matches(command.Template, "LEFT JOIN OracleGroups o0").Count);
            Assert.Equal(2, Regex.Matches(command.Template, @"o0.Id = __ob_bulk_target.Id").Count);
            Assert.DoesNotContain("__ob_bulk_target.TenantId", command.Template);
            Assert.DoesNotContain("ELSE N'F'", command.Template);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Selectively_disabled_named_filters_do_not_remove_the_remaining_filter_or_correlation(bool async)
        {
            var capture = new Capture();
            await using var context = Create(capture);
            var code = "visible";
            var query = context.Set<QuotedRow>().Join(context.Set<Group>(), row => row.GroupId, group => group.Id,
                (row, group) => new { Row = row, Group = group }).IgnoreQueryFilters(["Tenant"])
                .Where(pair => pair.Group.Code == code);
            Assert.Equal(4, async ? await query.ExecuteUpdateAsync(setters => setters.SetProperty(pair => pair.Row.GroupId, pair => pair.Group.Id))
                : query.ExecuteUpdate(setters => setters.SetProperty(pair => pair.Row.GroupId, pair => pair.Group.Id)));
            var command = Assert.Single(capture.Commands);
            Assert.Equal(2, Regex.Matches(command.Template, "GroupId > 0").Count);
            Assert.DoesNotContain("TenantId = ?", command.Template);
            Assert.Equal(2, Regex.Matches(command.Template, @" = __ob_bulk_target.TenantId").Count);
            Assert.Equal(new object[] { code, code }, command.Values);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Joined_keyless_targets_reject_before_open_instead_of_updating_unrelated_rows(bool async)
        {
            var capture = new Capture();
            await using var context = Create(capture);
            var query = context.Set<KeylessRow>().Join(context.Set<Group>(), row => row.GroupId, group => group.Id,
                (row, group) => new { Row = row, Group = group });
            async Task<int> Update() => async ? await query.ExecuteUpdateAsync(setters => setters.SetProperty(pair => pair.Row.Name, pair => pair.Group.Code))
                : query.ExecuteUpdate(setters => setters.SetProperty(pair => pair.Row.Name, pair => pair.Group.Code));
            var error = await Assert.ThrowsAsync<NotSupportedException>(Update);
            Assert.Contains("primary key", error.Message);
            Assert.Equal(0, capture.Opens);
            Assert.Empty(capture.Commands);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public async Task Rewritten_commands_preserve_failure_identity_and_allow_a_later_zero_row_operation(bool async, bool cancellation)
        {
            var capture = new Capture();
            await using var context = Create(capture);
            var tracked = new Row { TenantId = 3, Id = 1, Name = "original" };
            context.Attach(tracked);
            using var source = new CancellationTokenSource();
            Exception failure = cancellation ? new OperationCanceledException(source.Token) : new InvalidOperationException("native command failure");
            capture.Failure = failure;
            if (cancellation) capture.BeforeFailure = source.Cancel;
            var query = context.Set<Row>().Join(context.Set<Group>(), row => row.GroupId, group => group.Id,
                (row, group) => new { Row = row, Group = group });
            async Task<int> Update(CancellationToken token) => async ? await query.ExecuteUpdateAsync(setters => setters.SetProperty(pair => pair.Row.Name, pair => pair.Group.Code), token)
                : query.ExecuteUpdate(setters => setters.SetProperty(pair => pair.Row.Name, pair => pair.Group.Code));
            Assert.Same(failure, await Record.ExceptionAsync(() => Update(source.Token)));
            var command = Assert.Single(capture.Commands);
            Assert.Contains("WHERE EXISTS", command.Template);
            Assert.Equal(async ? source.Token : default, command.Token);
            Assert.Null(command.Transaction);
            Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
            capture.Failure = null;
            capture.BeforeFailure = null;
            capture.AffectedRows = 0;
            Assert.Equal(0, await Update(default));
            Assert.Equal(2, capture.Commands.Count);
            Assert.Equal("original", tracked.Name);
            Assert.Equal(EntityState.Unchanged, context.Entry(tracked).State);
        }

        private static TestContext Create(Capture capture) => new(new DbContextOptionsBuilder<TestContext>()
            .UseOceanBase("Server=offline;Database=offline", OceanBaseCompatibilityMode.Oracle).AddInterceptors(capture).Options);

        private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
        {
            public int TenantId { get; set; } = 3;
            protected override void OnModelCreating(ModelBuilder model)
            {
                var row = model.Entity<Row>().ToTable("OracleRows");
                row.HasKey(value => new { value.TenantId, value.Id });
                row.HasQueryFilter("Tenant", value => value.TenantId == TenantId);
                row.Property(value => value.Active).HasConversion(new BoolToStringConverter("N", "Y"));
                row.Property(value => value.OptionalActive).HasConversion(new BoolToStringConverter("N", "Y"));
                var group = model.Entity<Group>().ToTable("OracleGroups");
                group.Property(value => value.Enabled).HasConversion(new BoolToStringConverter("F", "T"));
                group.Property(value => value.OptionalEnabled).HasConversion(new BoolToStringConverter("F", "T"));
                var quoted = model.Entity<QuotedRow>().ToTable("Rows\"Target", "Own\"Scope");
                quoted.HasKey(value => new { value.TenantId, value.BusinessId });
                quoted.Property(value => value.BusinessId).HasColumnName("business\"key").HasConversion<string>();
                quoted.HasQueryFilter("Tenant", value => value.TenantId == TenantId);
                quoted.HasQueryFilter("PositiveGroup", value => value.GroupId > 0);
                model.Entity<KeylessRow>().ToTable("OracleKeylessRows").HasNoKey();
            }
        }
        private sealed class Row
        {
            public int TenantId { get; set; }
            public int Id { get; set; }
            public int GroupId { get; set; }
            public string? Name { get; set; }
            public int Count { get; set; }
            public int Previous { get; set; }
            public bool Active { get; set; }
            public bool? OptionalActive { get; set; }
        }
        private sealed class Group { public int Id { get; set; } public string Code { get; set; } = ""; public bool Enabled { get; set; } public bool? OptionalEnabled { get; set; } }
        private sealed class QuotedRow { public int TenantId { get; set; } public Guid BusinessId { get; set; } public int GroupId { get; set; } }
        private sealed class KeylessRow { public int GroupId { get; set; } public string? Name { get; set; } }
        private sealed record Command(string Raw, string Template, object[] Values, int Timeout, bool Async, CancellationToken Token, DbTransaction? Transaction);
        private sealed class Capture : DbConnectionInterceptor, IDbCommandInterceptor
        {
            public List<Command> Commands { get; } = [];
            public int Opens { get; private set; }
            public Exception? Failure { get; set; }
            public Action? BeforeFailure { get; set; }
            public int AffectedRows { get; set; } = 4;
            public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
            { Opens++; return InterceptionResult.Suppress(); }
            public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
                InterceptionResult result, CancellationToken cancellationToken = default)
            { cancellationToken.ThrowIfCancellationRequested(); return ValueTask.FromResult(ConnectionOpening(connection, eventData, result)); }
            public InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result) => Execute(command, false, default);
            public ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
                InterceptionResult<int> result, CancellationToken cancellationToken = default) => ValueTask.FromResult(Execute(command, true, cancellationToken));
            private InterceptionResult<int> Execute(DbCommand command, bool async, CancellationToken token)
            {
                var parameters = command.Parameters.Cast<DbParameter>().ToArray();
                var values = Regex.Matches(command.CommandText, @":\w+", RegexOptions.CultureInvariant).Select(bind =>
                    parameters.Single(parameter => parameter.ParameterName.TrimStart(':') == bind.Value[1..]).Value!).ToArray();
                var template = Regex.Replace(command.CommandText.Replace("\"", "", StringComparison.Ordinal), @":\w+", "?");
                template = Regex.Replace(template, @"\s+", " ").Trim();
                Commands.Add(new Command(command.CommandText, template, values, command.CommandTimeout, async, token, command.Transaction));
                if (Failure is { } failure) { BeforeFailure?.Invoke(); throw failure; }
                return InterceptionResult<int>.SuppressWithResult(AffectedRows);
            }
        }
    }
}
