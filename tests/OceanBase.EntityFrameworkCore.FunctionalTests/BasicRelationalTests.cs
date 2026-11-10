using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Globalization;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.FunctionalTests
{
    public sealed class BasicRelationalTests
    {
        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, true)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, true)]
        public async Task Originally_empty_inline_commands_keep_zero_row_bodies_and_shape_supplied_results(
            OceanBaseCompatibilityMode mode, bool async, bool relationalNulls)
        {
            using var counts = new DataTable();
            counts.Columns.Add("Count", typeof(object));
            counts.Columns.Add("Any", typeof(bool));
            counts.Rows.Add(new OceanBaseDecimal(0m), false);
            var commands = new CommandCapture { Results = counts };
            await using var context = CreateContext(mode, commands, relationalNulls);
            var aggregates = context.Set<ArithmeticRow>().Select(row => new
            {
                Count = new int[] { }.Count(value => value > row.Id),
                Any = new int?[] { }.Any(value => value > row.Optional)
            });
            // Supplied reader rows verify shaping; the interceptor does not compute these aggregates.
            var aggregate = Assert.Single(async ? await aggregates.ToListAsync() : aggregates.ToList());
            Assert.Equal(0, aggregate.Count);
            Assert.False(aggregate.Any);
            // Oracle's Boolean CASE can visit the EXISTS root twice.
            Assert.True(Assert.Single(commands.Sql).Split("WHERE 1 = 0").Length - 1 >= 2);
            Assert.Empty(commands.Inputs);

            counts.Rows.Clear();
            commands.Sql.Clear();
            var joined = context.Set<ArithmeticRow>().SelectMany(row => new int[] { }.Where(value => value > row.Id)
                .Join(context.Set<ArithmeticRow>(), value => value, other => other.Id, (value, other) => value));
            Assert.Empty(async ? await joined.ToListAsync() : joined.ToList());
            Assert.Contains("WHERE 1 = 0", Assert.Single(commands.Sql));

            counts.Rows.Add(DBNull.Value, DBNull.Value);
            commands.Sql.Clear();
            var nullable = context.Set<ArithmeticRow>().SelectMany(row => new int?[] { }
                .Where(value => value > row.Id).DefaultIfEmpty());
            Assert.Null(Assert.Single(async ? await nullable.ToListAsync() : nullable.ToList()));
            var sql = Assert.Single(commands.Sql);
            Assert.Contains("WHERE 1 = 0", sql);
            Assert.Contains("LEFT JOIN", sql);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "CAST(NULL AS SIGNED)" : "CAST(NULL AS NUMBER)", sql);
            if (mode == OceanBaseCompatibilityMode.Oracle) Assert.Contains("FROM DUAL", sql);

            counts.Rows.Clear();
            commands.Sql.Clear();
            var ordinal = context.Set<ArithmeticRow>().Select(row => new int[] { }
                .Select(value => row.Id + 1).ElementAt(row.Id));
            Assert.Empty(async ? await ordinal.ToListAsync() : ordinal.ToList());
            sql = Assert.Single(commands.Sql);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? " AS `_ord` WHERE 1 = 0" : " \"_ord\"" + Environment.NewLine, sql);
            Assert.Contains("ORDER BY", sql);
            Assert.DoesNotContain(mode == OceanBaseCompatibilityMode.MySql ? " AS `Value`" : " \"Value\"", sql);
            Assert.Empty(commands.Inputs);

            counts.Rows.Add(new OceanBaseDecimal(42m), false);
            var converted = context.Set<ArithmeticRow>().Where(row => new int[] { }.Where(value => value > row.Converted)
                .Contains(row.Converted)).Select(row => row.Converted);
            Assert.Equal(42, Assert.Single(async ? await converted.ToListAsync() : converted.ToList()));
            counts.Rows.Clear();
            counts.Rows.Add(new OceanBaseDecimal((decimal)long.MaxValue + 1), false);
            if (async) await Assert.ThrowsAsync<OverflowException>(() => converted.ToListAsync());
            else Assert.Throws<OverflowException>(() => converted.ToList());
            counts.Rows.Clear();
            counts.Rows.Add(new OceanBaseDecimal((decimal)int.MaxValue + 1), false);
            var native = context.Set<ArithmeticRow>().SelectMany(row => new int[] { }.Where(value => value > row.Id));
            if (async) await Assert.ThrowsAsync<OverflowException>(() => native.ToListAsync());
            else Assert.Throws<OverflowException>(() => native.ToList());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, true)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, true)]
        public async Task Integral_root_commands_rebind_current_values_without_padding_or_mutating_sources(
            OceanBaseCompatibilityMode mode, bool async, bool relationalNulls)
        {
            using var table = new DataTable();
            table.Columns.Add("Value", typeof(object));
            table.Rows.Add(new OceanBaseDecimal(42m));
            var commands = new CommandCapture { Results = table };
            await using var context = CreateContext(mode, commands, relationalNulls);
            int?[] values = [];
            var query = context.Set<ArithmeticRow>().SelectMany(row => values.Where(value => value > row.Id));
            foreach (var capture in new int?[][] { [], [17], [17, null, 17], [null, null],
                [1, 2, 3, 4, 5, 6], [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11], [], null! })
            {
                values = capture;
                var snapshot = capture?.ToArray();
                commands.Sql.Clear();
                commands.Values.Clear();
                commands.Inputs.Clear();
                // The interceptor supplies a row even for a zero-row SQL body: this verifies shaping, not cardinality.
                Assert.Equal(42, Assert.Single(async ? await query.ToListAsync() : query.ToList()));
                Assert.Equal(snapshot, capture);
                var sql = Assert.Single(commands.Sql);
                Assert.DoesNotContain("UNION ALL VALUES", sql);
                if (capture is null || capture.Length == 0)
                {
                    Assert.Contains("WHERE 1 = 0", sql);
                    Assert.Empty(commands.Inputs);
                }
                else
                {
                    Assert.Equal(capture.Length - 1, sql.Split("UNION ALL SELECT").Length - 1);
                    Assert.Equal(capture.Where(value => value.HasValue).Select(value => (object)value!.Value),
                        commands.Values.Where(value => value is not DBNull));
                    Assert.All(commands.Inputs, parameter => Assert.Equal(DbType.Int32, parameter.DbType));
                }
            }

            List<int?> list = [17, null, 17, 3, 5, 7];
            var listQuery = context.Set<ArithmeticRow>().SelectMany(row => list.Where(value => value > row.Id));
            commands.Sql.Clear();
            _ = async ? await listQuery.ToListAsync() : listQuery.ToList();
            Assert.Equal(5, Assert.Single(commands.Sql).Split("UNION ALL SELECT").Length - 1);
            Assert.Equal(new int?[] { 17, null, 17, 3, 5, 7 }, list);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Integral_roots_preserve_long_converter_bindings_and_read_hooks(OceanBaseCompatibilityMode mode, bool async)
        {
            using var table = new DataTable();
            table.Columns.Add("Value", typeof(object));
            table.Rows.Add(new OceanBaseDecimal(42m));
            var commands = new CommandCapture { Results = table };
            await using var context = CreateContext(mode, commands);
            int[] values = [int.MinValue, int.MaxValue, 17, 17, 3, 5];
            var query = context.Set<ArithmeticRow>().Where(row => values.Where(value => value > row.Converted)
                .Contains(row.Converted)).Select(row => row.Id);
            Assert.Equal(42, Assert.Single(async ? await query.ToListAsync() : query.ToList()));
            Assert.Equal(values.Select(value => (object)(long)value), commands.Values);
            Assert.All(commands.Inputs, parameter =>
            {
                Assert.IsType<long>(parameter.Value);
                Assert.Equal(DbType.Int64, parameter.DbType);
            });
            var mapped = context.Set<ArithmeticRow>().Where(row => values.Where(value => value > row.Converted)
                .Contains(row.Converted)).Select(row => row.Converted);
            Assert.Equal(42, Assert.Single(async ? await mapped.ToListAsync() : mapped.ToList()));
            table.Rows.Clear();
            table.Rows.Add(new OceanBaseDecimal((decimal)long.MaxValue + 1));
            if (async) await Assert.ThrowsAsync<OverflowException>(() => mapped.ToListAsync());
            else Assert.Throws<OverflowException>(() => mapped.ToList());

            table.Rows.Clear();
            table.Rows.Add(new OceanBaseDecimal((decimal)int.MaxValue + 1));
            var native = context.Set<ArithmeticRow>().SelectMany(row => values.Where(value => value > row.Id));
            if (async) await Assert.ThrowsAsync<OverflowException>(() => native.ToListAsync());
            else Assert.Throws<OverflowException>(() => native.ToList());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Integral_roots_materialize_supplied_nullable_rows_and_constants_without_bindings(
            OceanBaseCompatibilityMode mode, bool async)
        {
            using var table = new DataTable();
            table.Columns.Add("Value", typeof(object));
            table.Rows.Add(DBNull.Value);
            table.Rows.Add(new OceanBaseDecimal(17m));
            var commands = new CommandCapture { Results = table };
            await using var context = CreateContext(mode, commands);
            int?[] values = [null, 17, 17];
            var query = context.Set<ArithmeticRow>().SelectMany(row => EF.Constant(values).Where(value => value > row.Id));
            var rows = async ? await query.ToListAsync() : query.ToList();
            Assert.Equal(new int?[] { null, 17 }, rows);
            Assert.Empty(commands.Inputs);
            Assert.Contains("UNION ALL SELECT", Assert.Single(commands.Sql));
            commands.Sql.Clear();
            var inline = context.Set<ArithmeticRow>().SelectMany(row => new int?[] { null, 17, 17 }.Where(value => value > row.Id));
            Assert.Equal(new int?[] { null, 17 }, async ? await inline.ToListAsync() : inline.ToList());
            Assert.Empty(commands.Inputs);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, true)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, true)]
        public async Task Text_membership_commands_rebind_raw_captures_and_keep_converters_separate(
            OceanBaseCompatibilityMode mode, bool async, bool relationalNulls)
        {
            using var table = new DataTable();
            table.Columns.Add("Id", typeof(object));
            table.Rows.Add(new OceanBaseDecimal(42m));
            var commands = new CommandCapture { Results = table };
            await using var context = CreateContext(mode, commands, relationalNulls);
            string?[] values = [];
            var query = context.Set<TextMembershipRow>().Where(row => values.Contains(row.Suffix)
                || values.Contains(row.EmptyConverted) || values.Contains(row.Preserved)).Select(row => row.Id);
            List<string?[]> captures = [[], ["", "empty", null, "x"], [null, null], ["", "empty"], [], null!];
            foreach (var size in new[] { 5, 6, 10, 11, 150, 151 })
            {
                foreach (var last in new string?[] { null, "empty" })
                {
                    var capture = Enumerable.Range(0, size).Select(index => (string?)index.ToString()).ToArray();
                    capture[1] = "empty";
                    capture[^1] = last;
                    captures.Add(capture);
                }
            }
            foreach (var capture in captures)
            {
                values = capture;
                var snapshot = capture?.ToArray();
                commands.Sql.Clear();
                commands.Values.Clear();
                commands.Inputs.Clear();
                var rows = async ? await query.ToListAsync() : query.ToList();
                // Supplied rows establish binding and materialization, not SQL membership results.
                Assert.Equal(42, Assert.Single(rows));
                Assert.Equal(snapshot, capture);
                var sql = Assert.Single(commands.Sql);
                Assert.DoesNotContain("IN ()", sql);
                Assert.All(commands.Values, value => Assert.True(value is string or DBNull));
                var expected = (capture ?? []).Where(value => value is not null)
                    .SelectMany(value => new[] { value + "!", value == "empty" ? "" : value, value })
                    .Where(value => mode != OceanBaseCompatibilityMode.Oracle || value != "").Distinct().ToArray();
                foreach (var value in expected) Assert.Contains(value, commands.Values);
                Assert.All(commands.Values.Where(value => value is string), value => Assert.Contains((string)value!, expected));
                Assert.DoesNotContain(commands.Values, value => value is string text && text.EndsWith("!!", StringComparison.Ordinal));
                if (capture is null || capture.Length == 0) Assert.Empty(commands.Inputs);
            }

            var list = new List<string?> { "", "empty", "list" };
            var listSnapshot = list.ToArray();
            commands.Values.Clear();
            var listQuery = context.Set<TextMembershipRow>().Where(row => list.Contains(row.Suffix)).Select(row => row.Id);
            _ = async ? await listQuery.ToListAsync() : listQuery.ToList();
            Assert.Equal(listSnapshot, list);
            Assert.Contains("!", commands.Values);
            Assert.Contains("empty!", commands.Values);
            Assert.Contains("list!", commands.Values);

            int[] numbers = [0, 17, 23];
            RowState[] states = [RowState.Inactive, RowState.Active];
            commands.Values.Clear();
            var converted = context.Set<TextMembershipRow>().Where(row => numbers.Contains(row.Number) && states.Contains(row.State)).Select(row => row.Id);
            _ = async ? await converted.ToListAsync() : converted.ToList();
            foreach (var value in new[] { "0", "17", "23", "Inactive", "Active" }) Assert.Contains(value, commands.Values);
            Assert.All(commands.Values, value => Assert.IsType<string>(value));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Text_membership_commands_preserve_text_family_metadata_and_nontext_carriers(
            OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new CommandCapture();
            await using var context = CreateContext(mode, commands);
            string[] values = ["海", "river"];
            foreach (var property in new[] { nameof(TextMembershipRow.Text), nameof(TextMembershipRow.Fixed),
                nameof(TextMembershipRow.National), nameof(TextMembershipRow.Lob), nameof(TextMembershipRow.NationalLob) })
            {
                commands.Inputs.Clear();
                var query = context.Set<TextMembershipRow>().Where(row => values.Contains(EF.Property<string>(row, property))).Select(row => row.Id);
                _ = async ? await query.ToListAsync() : query.ToList();
                var mapping = (Microsoft.EntityFrameworkCore.Storage.RelationalTypeMapping)context.Model.FindEntityType(typeof(TextMembershipRow))!
                    .FindProperty(property)!.GetTypeMapping();
                using var command = new OceanBaseCommand();
                var prototype = (OceanBaseParameter)mapping.CreateParameter(command, "prototype", "海");
                Assert.Equal(2, commands.Inputs.Count);
                Assert.All(commands.Inputs, parameter =>
                {
                    Assert.Equal(prototype.DbType, parameter.DbType);
                    Assert.Equal(prototype.OceanBaseDbType, parameter.OceanBaseDbType);
                    Assert.True(parameter.Size >= ((string)parameter.Value!).Length);
                });
                Assert.Contains(commands.Inputs, parameter => Equals(parameter.Value, "海"));
                Assert.Contains(commands.Inputs, parameter => Equals(parameter.Value, "river"));
            }

            string[] hex = ["0A", "0B"];
            commands.Values.Clear();
            var binary = context.Set<TextMembershipRow>().Where(row => hex.Contains(row.Binary)).Select(row => row.Id);
            _ = async ? await binary.ToListAsync() : binary.ToList();
            Assert.All(commands.Values, value => Assert.IsType<byte[]>(value));
            Assert.Contains(commands.Values, value => value is byte[] bytes && bytes.SequenceEqual(new byte[] { 10 }));
            Assert.Contains(commands.Values, value => value is byte[] bytes && bytes.SequenceEqual(new byte[] { 11 }));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, true)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, true)]
        public async Task Text_membership_reads_supplied_boolean_and_nullable_results_without_evaluating_SQL(
            OceanBaseCompatibilityMode mode, bool async, bool relationalNulls)
        {
            using var table = new DataTable();
            table.Columns.Add("Match", typeof(object));
            table.Columns.Add("Negated", typeof(object));
            table.Columns.Add("Choice", typeof(object));
            table.Rows.Add(mode == OceanBaseCompatibilityMode.Oracle ? 1m : (object)true,
                mode == OceanBaseCompatibilityMode.Oracle ? 0m : (object)false, new OceanBaseDecimal(7m));
            var commands = new CommandCapture { Results = table };
            await using var context = CreateContext(mode, commands, relationalNulls);
            string?[] values = ["", null, "present"];
            var query = context.Set<TextMembershipRow>().Select(row => new { Match = values.Contains(row.Text),
                Negated = !values.Contains(row.Text), Choice = values.Contains(row.Text) ? 7 : 9 });
            var row = Assert.Single(async ? await query.ToListAsync() : query.ToList());
            Assert.True(row.Match);
            Assert.False(row.Negated);
            Assert.Equal(7, row.Choice);
            Assert.Contains("CASE", Assert.Single(commands.Sql));
            table.Rows.Clear();
            table.Columns.Remove("Negated");
            table.Columns.Remove("Choice");
            table.Rows.Add(DBNull.Value);
            var nullable = context.Set<TextMembershipRow>().Select(row => (bool?)values.Contains(row.Text));
            Assert.Null(Assert.Single(async ? await nullable.ToListAsync() : nullable.ToList()));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, true)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, true)]
        public async Task Membership_commands_rebind_current_scalar_values_without_mutating_captured_arrays(
            OceanBaseCompatibilityMode mode, bool async, bool relationalNulls)
        {
            using var table = new DataTable();
            table.Columns.Add("Id", typeof(object));
            table.Rows.Add(new OceanBaseDecimal(42m));
            var commands = new CommandCapture { Results = table };
            await using var context = CreateContext(mode, commands, relationalNulls);
            int?[] values = [];
            var query = context.Set<ArithmeticRow>().Where(row => Enumerable.Contains(values, row.Optional)).Select(row => row.Id);
            foreach (var current in new int?[][]
                { [], [2, null, 7], [null, null], [3, 5, 7, 9, 11], [3, 5, 7, 9, 11, 13],
                    [1, 2, 3, 4, 5, 6, 7, 8, 9, null], [21, 22, 23, 24, 25, 26, 27, 28, 29, 30, null], [], null! })
            {
                values = current;
                var snapshot = current?.ToArray();
                commands.Sql.Clear();
                commands.Values.Clear();
                commands.Inputs.Clear();
                var rows = async ? await query.ToListAsync() : query.ToList();
                // Supplied rows prove binding/shaping only; the interceptor does not evaluate membership.
                Assert.Equal(42, Assert.Single(rows));
                Assert.Equal(snapshot, current);
                var sql = Assert.Single(commands.Sql);
                Assert.DoesNotContain("IN ()", sql);
                Assert.All(commands.Values, value => Assert.True(value is int or DBNull, $"Unexpected collection parameter: {value?.GetType()}"));
                foreach (var value in (current ?? []).Where(value => value.HasValue).Select(value => value!.Value).Distinct())
                    Assert.Contains(value, commands.Values);
                Assert.All(commands.Values.Where(value => value is int), value => Assert.Contains((int)value!, current!));
                if (current is null || current.Length == 0) Assert.DoesNotContain(" IN (", sql);
                if (current is not null && current.Any(value => value.HasValue)) Assert.NotEmpty(commands.Inputs);
            }
            int[] converted = [int.MinValue, int.MaxValue];
            commands.Values.Clear();
            var convertedQuery = context.Set<ArithmeticRow>().Where(row => Enumerable.Contains(converted, row.Converted)).Select(row => row.Id);
            _ = async ? await convertedQuery.ToListAsync() : convertedQuery.ToList();
            Assert.Contains((long)int.MinValue, commands.Values);
            Assert.Contains((long)int.MaxValue, commands.Values);
            Assert.All(commands.Values, value => Assert.IsType<long>(value));
            List<int> list = [17, 19];
            List<int?> optionalList = [17, null, 19];
            foreach (var listQuery in new[]
                { context.Set<ArithmeticRow>().Where(row => list.Contains(row.Value)).Select(row => row.Id),
                    context.Set<ArithmeticRow>().Where(row => optionalList.Contains(row.Optional)).Select(row => row.Id) })
            {
                commands.Values.Clear();
                _ = async ? await listQuery.ToListAsync() : listQuery.ToList();
                Assert.Contains(17, commands.Values);
                Assert.Contains(19, commands.Values);
                Assert.All(commands.Values.Where(value => value is not DBNull), value => Assert.IsType<int>(value));
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, true)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, true)]
        public async Task Relational_commands_shape_supplied_nullable_right_and_scalar_subquery_rows(
            OceanBaseCompatibilityMode mode, bool async, bool relationalNulls)
        {
            using var table = new DataTable();
            table.Columns.Add("Id", typeof(object));
            table.Columns.Add("Right", typeof(object));
            table.Rows.Add(new OceanBaseDecimal(42m), DBNull.Value);
            table.Rows.Add(new OceanBaseDecimal(42m), new OceanBaseDecimal(7m));
            var commands = new CommandCapture { Results = table };
            await using var context = CreateContext(mode, commands, relationalNulls);
            var source = context.Set<ArithmeticRow>();
            var left = source.LeftJoin(source, a => a.Id, b => b.Value,
                (a, b) => new { a.Id, Right = (int?)b!.Id });
            var groupJoin = from a in source
                            join b in source on a.Id equals b.Value into matches
                            from b in matches.DefaultIfEmpty()
                            select new { a.Id, Right = (int?)b.Id };
            var inner = source.Join(source, a => a.Id, b => b.Value, (a, b) => new { a.Id, Right = (int?)b.Id });
            var composite = source.Join(source, a => new { a.Id, a.Optional }, b => new { Id = b.Value, b.Optional },
                (a, b) => new { a.Id, Right = (int?)b.Id });
            foreach (var (joined, nullableRight) in new[] { (left, true), (groupJoin, true), (inner, false), (composite, false) })
            {
                table.Rows[0][1] = nullableRight ? DBNull.Value : new OceanBaseDecimal(7m);
                var rows = async ? await joined.ToListAsync() : joined.ToList();
                Assert.Equal(2, rows.Count);
                Assert.Equal(nullableRight ? null : (int?)7, rows[0].Right);
                Assert.Equal(7, rows[1].Right);
                Assert.Contains("JOIN", commands.Sql.Last());
            }
            Assert.Contains("LEFT JOIN", commands.Sql[0]);
            Assert.Contains("LEFT JOIN", commands.Sql[1]);
            Assert.Contains("INNER JOIN", commands.Sql[2]);
            Assert.Contains(" AND ", commands.Sql[3]);
            table.Rows.Clear();
            table.Columns.RemoveAt(1);
            table.Rows.Add(DBNull.Value);
            var scalar = source.Select(a => source.Where(b => b.Value == a.Id).OrderBy(b => b.Id).Select(b => b.Optional).FirstOrDefault());
            var nullable = async ? await scalar.ToListAsync() : scalar.ToList();
            Assert.Null(Assert.Single(nullable));
            Assert.Contains("ORDER BY", commands.Sql.Last());
            table.Rows.Clear();
            table.Rows.Add(new OceanBaseDecimal(42m));
            var membership = source.Where(a => source.Select(b => b.Optional).Contains(a.Optional)).Select(a => a.Id);
            Assert.Equal(42, Assert.Single(async ? await membership.ToListAsync() : membership.ToList()));
            Assert.True(commands.Sql.Last().Contains(" IN (", StringComparison.Ordinal)
                || commands.Sql.Last().Contains("EXISTS (", StringComparison.Ordinal));
            table.Rows.Clear();
            var noRows = async ? await left.ToListAsync() : left.ToList();
            Assert.Empty(noRows);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, true)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, true)]
        public async Task Root_quantifiers_and_membership_shape_supplied_boolean_and_unknown_results(
            OceanBaseCompatibilityMode mode, bool async, bool relationalNulls)
        {
            using var table = new DataTable();
            table.Columns.Add("Result", typeof(object));
            table.Rows.Add(mode == OceanBaseCompatibilityMode.Oracle ? new OceanBaseDecimal(1m) : (object)true);
            var commands = new CommandCapture { Results = table };
            await using var context = CreateContext(mode, commands, relationalNulls);
            var source = context.Set<ArithmeticRow>();
            Assert.True(async ? await source.AnyAsync(row => row.Value > 3) : source.Any(row => row.Value > 3));
            Assert.True(async ? await source.AllAsync(row => row.Optional > 3) : source.All(row => row.Optional > 3));
            Assert.Contains("EXISTS (", commands.Sql[0]);
            Assert.Contains("NOT EXISTS (", commands.Sql[1]);
            if (mode == OceanBaseCompatibilityMode.Oracle)
                Assert.All(commands.Sql, sql => { Assert.Contains("FROM DUAL", sql); Assert.Contains("CASE", sql); });
            int?[] values = [2, null, 7];
            table.Rows.Clear();
            table.Rows.Add(DBNull.Value);
            var nullableQuery = source.Select(row => (bool?)Enumerable.Contains(values, row.Optional));
            var nullable = async ? await nullableQuery.ToListAsync() : nullableQuery.ToList();
            Assert.Null(Assert.Single(nullable));
            var required = source.Select(row => Enumerable.Contains(values, row.Optional));
            // DBNull in a required bool projection rejects; synthetic UNKNOWN is not silently changed to false.
            if (async) await Assert.ThrowsAsync<InvalidOperationException>(() => required.ToListAsync());
            else Assert.Throws<InvalidOperationException>(() => required.ToList());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, true)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, true)]
        public async Task Count_commands_shape_supplied_zero_int_long_and_checked_overflow(
            OceanBaseCompatibilityMode mode, bool async, bool relationalNulls)
        {
            using var table = new DataTable();
            table.Columns.Add("Count", typeof(object));
            table.Rows.Add(new OceanBaseDecimal(0m));
            var commands = new CommandCapture { Results = table };
            await using var context = CreateContext(mode, commands, relationalNulls);
            var source = context.Set<ArithmeticRow>();
            Assert.Equal(0, async ? await source.CountAsync() : source.Count());
            Assert.Equal(0L, async ? await source.LongCountAsync() : source.LongCount());
            Assert.Equal(0, async ? await source.CountAsync(row => row.Optional > 3) : source.Count(row => row.Optional > 3));
            Assert.Equal(0L, async ? await source.LongCountAsync(row => row.Optional > 3) : source.LongCount(row => row.Optional > 3));
            Assert.Equal(0, async ? await source.Select(row => row.Optional).CountAsync() : source.Select(row => row.Optional).Count());
            Assert.Equal(0, async ? await source.Where(row => row.Id > 3).Select(row => row.Optional).Distinct().CountAsync()
                : source.Where(row => row.Id > 3).Select(row => row.Optional).Distinct().Count());
            Assert.All(commands.Sql, sql => { Assert.Contains("COUNT(", sql); Assert.DoesNotContain("COUNT_BIG", sql); });
            table.Rows.Clear();
            table.Rows.Add(new OceanBaseDecimal((decimal)int.MaxValue + 1));
            Assert.Equal((long)int.MaxValue + 1, async ? await source.LongCountAsync() : source.LongCount());
            if (async) await Assert.ThrowsAsync<OverflowException>(() => source.CountAsync());
            else Assert.Throws<OverflowException>(() => source.Count());
            table.Rows.Clear();
            table.Rows.Add(new OceanBaseDecimal(long.MaxValue));
            Assert.Equal(long.MaxValue, async ? await source.LongCountAsync() : source.LongCount());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, true)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, true)]
        public async Task Grouped_counts_keep_fresh_result_mappings_and_supplied_nullable_keys_and_empty_rows(
            OceanBaseCompatibilityMode mode, bool async, bool relationalNulls)
        {
            using var table = new DataTable();
            foreach (var name in new[] { "Key", "Count", "Long" }) table.Columns.Add(name, typeof(object));
            table.Rows.Add(DBNull.Value, new OceanBaseDecimal(3m), new OceanBaseDecimal(4m));
            table.Rows.Add(new OceanBaseDecimal(12m), new OceanBaseDecimal(3m), new OceanBaseDecimal(4m));
            var commands = new CommandCapture { Results = table };
            await using var context = CreateContext(mode, commands, relationalNulls);
            var query = context.Set<CountGroupRow>().GroupBy(row => row.Converted).Where(group => group.Count() > 1)
                .Select(group => new { group.Key, Count = group.Count(), Long = group.LongCount() });
            var rows = async ? await query.ToListAsync() : query.ToList();
            Assert.Null(rows[0].Key);
            Assert.Equal(6, rows[1].Key);
            Assert.All(rows, row => { Assert.Equal(3, row.Count); Assert.Equal(4L, row.Long); });
            Assert.Contains("HAVING COUNT(*) > 1", Assert.Single(commands.Sql));
            table.Rows.Clear();
            Assert.Empty(async ? await query.ToListAsync() : query.ToList());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, true)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, true)]
        public async Task Math_commands_preserve_numeric_parameters_converters_and_synthetic_nullable_reads(
            OceanBaseCompatibilityMode mode, bool async, bool relationalNulls)
        {
            using var table = new DataTable();
            foreach (var name in new[] { "Abs", "Sign", "Trunc", "Fraction", "Single", "Double", "Optional" })
                table.Columns.Add(name, name is "Trunc" or "Fraction" ? typeof(decimal) : typeof(OceanBaseDecimal));
            var singleResult = new OceanBaseDecimal(BigInteger.One, -38);
            var doubleResult = new OceanBaseDecimal(BigInteger.One, -300);
            // Synthetic rows prove binding and checked/converter readers, not native arithmetic or membership.
            table.Rows.Add(new OceanBaseDecimal(int.MaxValue), new OceanBaseDecimal(-1m), -6m,
                decimal.MaxValue, singleResult, doubleResult, DBNull.Value);
            table.Rows.Add(new OceanBaseDecimal(21m), new OceanBaseDecimal(0m), 0m,
                decimal.MinValue, singleResult, doubleResult, new OceanBaseDecimal(7m));
            var commands = new CommandCapture { Results = table };
            await using var context = CreateContext(mode, commands, relationalNulls);
            var comparison = int.MaxValue;
            var sign = -1;
            var fraction = -12345.6789m;
            var single = float.MinValue;
            var wide = double.MinValue;
            var longMinimum = long.MinValue;
            var transformedComparison = 1.23456789m;
            var query = context.Set<MathRow>().Where(row => Math.Abs(row.Converted) == comparison
                    && Math.Sign(row.Transformed) == sign && Math.Truncate(row.Decimal + fraction) == fraction
                    && MathF.Abs(row.Single + single) == single && Math.Abs(row.Double + wide) == wide
                    && Math.Abs(row.Int64 + longMinimum) == longMinimum
                    && Math.Truncate(row.Transformed) == transformedComparison)
                .Select(row => new { Abs = Math.Abs(row.Converted), Sign = Math.Sign(row.Transformed),
                    Trunc = Math.Truncate(row.Transformed), Fraction = Math.Truncate(row.Decimal),
                    Single = MathF.Abs(row.Single), Double = Math.Truncate(row.Double),
                    Optional = (int?)Math.Abs(row.Optional!.Value) });
            var values = new[] { -12345.6789m, decimal.MinValue, decimal.MaxValue, -0.000000001m };
            for (var index = 0; index < values.Length; index++)
            {
                fraction = values[index];
                single = new[] { float.MinValue, float.MaxValue, float.Epsilon, -0f }[index];
                wide = new[] { double.MinValue, double.MaxValue, double.Epsilon, -0d }[index];
                commands.Inputs.Clear();
                var rows = async ? await query.ToListAsync() : query.ToList();
                Assert.Equal(2, rows.Count);
                Assert.Equal(int.MaxValue, rows[0].Abs);
                Assert.Equal(-1, rows[0].Sign);
                Assert.Equal(3m, rows[0].Trunc);
                Assert.Equal(decimal.MaxValue, rows[0].Fraction);
                Assert.Equal(1E38f, rows[0].Single);
                Assert.Equal(1E300d, rows[0].Double);
                Assert.Null(rows[0].Optional);
                Assert.Equal(decimal.MinValue, rows[1].Fraction);
                Assert.Equal(7, rows[1].Optional);
                Assert.Contains(commands.Inputs, parameter => Equals(parameter.Value, (long)comparison) && parameter.DbType == DbType.Int64);
                Assert.Contains(commands.Inputs, parameter => Equals(parameter.Value, sign) && parameter.DbType == DbType.Int32);
                Assert.Contains(commands.Inputs, parameter => Equals(parameter.Value, longMinimum) && parameter.DbType == DbType.Int64);
                Assert.Contains(commands.Inputs, parameter => Equals(parameter.Value, single) && parameter.DbType == DbType.Single);
                Assert.Contains(commands.Inputs, parameter => Equals(parameter.Value, wide) && parameter.DbType == DbType.Double);
                Assert.Contains(commands.Inputs, parameter => Equals(parameter.Value, transformedComparison * -2m)
                    && parameter.DbType == DbType.Decimal);
                var operand = Assert.Single(commands.Inputs, parameter => Equals(parameter.Value, fraction));
                Assert.Equal(DbType.Decimal, operand.DbType);
                Assert.Equal((byte)0, operand.Precision);
                Assert.Null(operand.DecimalScale);
                Assert.Equal(decimal.GetBits(fraction), decimal.GetBits((decimal)operand.Value!));
                Assert.Contains("ABS(", commands.Sql[^1]);
                Assert.Contains("SIGN(", commands.Sql[^1]);
                Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "TRUNCATE(" : "TRUNC(", commands.Sql[^1]);
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Math_projected_integer_overflow_keeps_existing_checked_reader(OceanBaseCompatibilityMode mode, bool async)
        {
            using var table = new DataTable();
            table.Columns.Add("Abs", typeof(OceanBaseDecimal));
            table.Rows.Add(new OceanBaseDecimal((decimal)int.MaxValue + 1));
            var commands = new CommandCapture { Results = table };
            await using var context = CreateContext(mode, commands);
            var query = context.Set<MathRow>().Where(row => Math.Abs(row.Id) >= 0).Select(row => Math.Abs(row.Id));
            if (async) await Assert.ThrowsAsync<OverflowException>(() => query.ToListAsync());
            else Assert.Throws<OverflowException>(() => query.ToList());
            Assert.Contains("ABS(", Assert.Single(commands.Sql));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, true)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, true)]
        public async Task Temporal_parts_bind_integer_comparisons_and_read_synthetic_results(
            OceanBaseCompatibilityMode mode, bool async, bool relationalNulls)
        {
            var oracle = mode == OceanBaseCompatibilityMode.Oracle;
            using var table = new DataTable();
            foreach (var name in new[] { "Part", "StampPart", "Optional" })
                table.Columns.Add(name, oracle ? typeof(OceanBaseDecimal) : typeof(int));
            table.Rows.Add(oracle ? new OceanBaseDecimal(59m) : 59,
                oracle ? new OceanBaseDecimal(2024m) : 2024,
                oracle ? new OceanBaseDecimal(59m) : 59);
            table.Rows.Add(oracle ? new OceanBaseDecimal(0m) : 0,
                oracle ? new OceanBaseDecimal(2024m) : 2024, DBNull.Value);
            var commands = new CommandCapture { Results = table };
            await using var context = CreateContext(mode, commands, relationalNulls);
            var date = new DateTime(2024, 2, 29, 23, 59, 59);
            var part = 59;
            var hour = 23;
            var query = context.Set<NarrowTemporalRow>()
                .Where(row => row.Date < date && row.Date.Second == part && row.Clock.Hour == hour)
                .Select(row => new { Part = row.Date.Second, StampPart = row.Stamp.Year,
                    Optional = (int?)row.Clock.Second });
            var rows = async ? await query.ToListAsync() : query.ToList();
            Assert.Equal(2, rows.Count);
            Assert.Equal(59, rows[0].Part);
            Assert.Equal(2024, rows[0].StampPart);
            Assert.Equal(59, rows[0].Optional);
            Assert.Null(rows[1].Optional);
            Assert.Contains(commands.Values, value => Equals(value, part));
            Assert.Contains(commands.Values, value => Equals(value, hour));
            Assert.Contains(commands.Values, value => Equals(value, date));
            Assert.Contains(commands.Inputs, parameter => Equals(parameter.Value, part) && parameter.DbType == DbType.Int32);
            Assert.Contains(commands.Inputs, parameter => Equals(parameter.Value, date) && parameter.DbType == DbType.DateTime2);
            Assert.Contains(oracle ? "TRUNC(EXTRACT(SECOND FROM CAST(" : "SECOND(", commands.Sql[^1]);
            Assert.DoesNotContain("__oceanbase_internal", commands.Sql[^1]);

            commands.Values.Clear();
            commands.Inputs.Clear();
            table.Rows[0]["Optional"] = oracle ? new OceanBaseDecimal(29m) : 29;
            var calendar = new DateOnly(2024, 2, 29);
            var clock = new TimeOnly(23, 59, 59).Add(TimeSpan.FromTicks(oracle ? 9999999 : 9999990));
            var calendarQuery = context.Set<TemporalRow>()
                .Where(row => row.Date == calendar && row.Clock == clock && row.Date.Day == 29)
                .Select(row => new { Part = row.Clock.Second, StampPart = row.Date.Year,
                    Optional = (int?)row.OptionalDate!.Value.Day });
            var calendarRows = async ? await calendarQuery.ToListAsync() : calendarQuery.ToList();
            Assert.Equal(59, calendarRows[0].Part);
            Assert.Equal(2024, calendarRows[0].StampPart);
            Assert.Equal(29, calendarRows[0].Optional);
            Assert.Null(calendarRows[1].Optional);
            Assert.Contains(commands.Inputs, parameter => Equals(parameter.Value, calendar.ToDateTime(TimeOnly.MinValue))
                && parameter.DbType == DbType.Date && parameter.Scale == 0);
            Assert.Contains(commands.Inputs, parameter => (oracle
                    ? parameter.Value is OceanBaseIntervalDayToSecond interval && interval.ToTimeSpan() == clock.ToTimeSpan()
                        && parameter.DbType == DbType.Object && parameter.OceanBaseDbType == OceanBaseDbType.IntervalDayToSecond
                        && parameter.Precision == 9 && parameter.Scale == 7
                    : Equals(parameter.Value, clock.ToTimeSpan()) && parameter.DbType == DbType.Time && parameter.Scale == 6));
            Assert.Contains(oracle ? "TRUNC(EXTRACT(SECOND FROM" : "SECOND(", commands.Sql[^1]);
            Assert.DoesNotContain("__oceanbase_internal", commands.Sql[^1]);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, true)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, true)]
        public async Task Substring_commands_bind_dynamic_arguments_and_convert_synthetic_results(
            OceanBaseCompatibilityMode mode, bool async, bool relationalNulls)
        {
            using var table = new DataTable();
            table.Columns.Add("Part", typeof(string));
            table.Columns.Add("Converted", typeof(string));
            table.Columns.Add("IsNull", typeof(bool));
            table.Rows.Add("native", "stored!", true);
            table.Rows.Add(DBNull.Value, DBNull.Value, false);
            var commands = new CommandCapture { Results = table };
            await using var context = CreateContext(mode, commands, relationalNulls);
            var start = 0;
            var length = 1;
            var expected = "model";
            var query = context.Set<ShortTextRow>()
                .Where(row => row.Converted.Substring(start, length) == expected)
                .Select(row => new
                {
                    Part = row.Caption!.Substring(start, length),
                    Converted = row.Converted.Substring(start),
                    IsNull = row.Caption!.Substring(row.Start, row.Length) == null
                });
            foreach (var (nextStart, nextLength) in new[] { (0, 1), (-2, -1), (int.MaxValue, int.MaxValue) })
            {
                start = nextStart;
                length = nextLength;
                commands.Values.Clear();
                commands.StringInputs.Clear();
                var rows = async ? await query.ToListAsync() : query.ToList();
                Assert.Equal("native", rows[0].Part);
                Assert.Equal("stored", rows[0].Converted);
                Assert.True(rows[0].IsNull);
                Assert.Null(rows[1].Part);
                Assert.Null(rows[1].Converted);
                Assert.False(rows[1].IsNull);
                Assert.Contains(commands.Values, value => Equals(value, start));
                Assert.Contains(commands.Values, value => Equals(value, length));
                Assert.Contains(commands.StringInputs, parameter => Equals(parameter.Value, "model!"));
                Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "SUBSTRING(" : "SUBSTR(", commands.Sql[^1]);
                Assert.Contains(" + 1", commands.Sql[^1]);
                Assert.Contains(" IS NULL", commands.Sql[^1]);
            }

            table.Rows.Clear();
            table.Columns.Clear();
            table.Columns.Add("Value", typeof(string));
            table.Rows.Add("海");
            var national = "海洋";
            commands.StringInputs.Clear();
            var nationalQuery = context.Set<SubstringNationalRow>()
                .Where(row => row.Value.Substring(start, length) == national)
                .Select(row => row.Value.Substring(start));
            Assert.Equal("海", Assert.Single(async ? await nationalQuery.ToListAsync() : nationalQuery.ToList()));
            Assert.Contains(commands.StringInputs, parameter => Equals(parameter.Value, national)
                && parameter.OceanBaseDbType == (mode == OceanBaseCompatibilityMode.Oracle ? OceanBaseDbType.NChar : (OceanBaseDbType?)null));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, true)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, true)]
        public async Task String_search_commands_bind_captured_needles_and_read_synthetic_booleans(
            OceanBaseCompatibilityMode mode, bool async, bool relationalNulls)
        {
            using var table = new DataTable();
            table.Columns.Add("Match", typeof(bool));
            table.Columns.Add("Negated", typeof(bool));
            table.Rows.Add(true, false);
            var commands = new CommandCapture { Results = table };
            await using var context = CreateContext(mode, commands, relationalNulls);
            string? needle = "海'_%\\";
            var query = context.Set<ShortTextRow>()
                .Where(row => row.Caption!.Contains(needle!) && row.Caption!.StartsWith(needle!)
                    && !row.Converted.EndsWith(needle!))
                .Select(row => new { Match = row.Caption!.EndsWith(row.Code),
                    Negated = !row.Caption!.Contains(row.Code) });
            foreach (var value in new string?[] { needle, "", null, "repeat", "", null, "海'_%\\" })
            {
                needle = value;
                commands.StringInputs.Clear();
                commands.Values.Clear();
                var row = Assert.Single(async ? await query.ToListAsync() : query.ToList());
                Assert.True(row.Match);
                Assert.False(row.Negated);
                var sql = commands.Sql[^1];
                Assert.DoesNotContain("__oceanbase_internal", sql);
                Assert.Contains("CASE", sql);
                Assert.Contains("IS NOT NULL", sql);
                if (value is { Length: > 0 })
                {
                    Assert.Contains("INSTR(", sql);
                    Assert.Contains(mode == OceanBaseCompatibilityMode.Oracle ? "SUBSTR(" : "RIGHT(", sql);
                    Assert.Contains(commands.StringInputs, parameter => Equals(parameter.Value, value));
                    Assert.Contains(commands.StringInputs, parameter => Equals(parameter.Value, value + "!"));
                }
                else if (value is "")
                {
                    Assert.Contains(commands.StringInputs, parameter => Equals(parameter.Value, "!"));
                    if (mode == OceanBaseCompatibilityMode.MySql)
                        Assert.Contains(commands.StringInputs, parameter => Equals(parameter.Value, ""));
                }
                else if (mode == OceanBaseCompatibilityMode.Oracle)
                    Assert.Empty(commands.StringInputs);
                else
                    Assert.All(commands.Values, bound => Assert.Equal(DBNull.Value, bound));
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, true)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, true)]
        public async Task Unary_string_commands_bind_inputs_and_read_synthetic_function_results(
            OceanBaseCompatibilityMode mode, bool async, bool relationalNulls)
        {
            using var table = new DataTable();
            foreach (var name in new[] { "Lower", "Upper", "Trim", "Start", "End", "Converted" })
                table.Columns.Add(name, typeof(string));
            table.Columns.Add("Length", typeof(int));
            table.Rows.Add("lower", "UPPER", "trim", "start ", " end", "CONVERTED!", 4);
            table.Rows.Add(DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value);
            var commands = new CommandCapture { Results = table };
            await using var context = CreateContext(mode, commands, relationalNulls);
            string? fallback = "海'_%\\";
            var query = context.Set<ShortTextRow>()
                .Where(row => row.Converted.ToUpper() != fallback)
                .Select(row => new
                {
                    Lower = (row.Caption ?? fallback)!.ToLower(), Upper = row.Caption!.ToUpper(),
                    Trim = row.Caption!.Trim(), Start = row.Caption!.TrimStart(), End = row.Caption!.TrimEnd(),
                    Converted = row.Converted.ToUpper(), Length = (int?)row.Caption!.Length
                });
            foreach (var value in new string?[] { fallback, "", null, "again" })
            {
                fallback = value;
                commands.StringInputs.Clear();
                var rows = async ? await query.ToListAsync() : query.ToList();
                Assert.Equal(2, rows.Count);
                Assert.Equal("lower", rows[0].Lower);
                Assert.Equal("UPPER", rows[0].Upper);
                Assert.Equal("trim", rows[0].Trim);
                Assert.Equal("start ", rows[0].Start);
                Assert.Equal(" end", rows[0].End);
                Assert.Equal("CONVERTED", rows[0].Converted);
                Assert.Equal(4, rows[0].Length);
                Assert.Null(rows[1].Lower);
                Assert.Null(rows[1].Trim);
                Assert.Null(rows[1].Converted);
                Assert.Null(rows[1].Length);
                var sql = commands.Sql[^1];
                foreach (var function in new[] { "LOWER(", "UPPER(", "TRIM(", "LTRIM(", "RTRIM(",
                    mode == OceanBaseCompatibilityMode.MySql ? "CHAR_LENGTH(" : "LENGTH(" })
                    Assert.Contains(function, sql);
                if (value is not null)
                    Assert.Contains(commands.StringInputs, parameter => Equals(parameter.Value, value + "!"));
                else Assert.Empty(commands.StringInputs);
                Assert.All(commands.StringInputs, parameter => Assert.True(parameter.Size >= ((string)parameter.Value!).Length));
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Arithmetic_commands_bind_divisors_and_materialize_synthetic_numeric_results(
            OceanBaseCompatibilityMode mode, bool async)
        {
            var oracle = mode == OceanBaseCompatibilityMode.Oracle;
            using var table = new DataTable();
            table.Columns.Add("Quotient", oracle ? typeof(decimal) : typeof(int));
            table.Columns.Add("Remainder", oracle ? typeof(decimal) : typeof(int));
            table.Columns.Add("Optional", oracle ? typeof(decimal) : typeof(int));
            table.Columns.Add("Converted", oracle ? typeof(decimal) : typeof(long));
            table.Columns.Add("Fraction", typeof(decimal));
            // Synthetic values exercise the reader only, not server arithmetic or predicate membership.
            table.Rows.Add(-2, -1, DBNull.Value, -2L, -2.5m);
            table.Rows.Add(2, 1, 2, 2L, 2.5m);
            var commands = new CommandCapture { Results = table };
            await using var context = CreateContext(mode, commands);
            var divisor = 3;
            var query = context.Set<ArithmeticRow>().Where(row => row.Value / divisor != 0)
                .Select(row => new { Quotient = row.Value / divisor, Remainder = row.Value % divisor,
                    Optional = row.Optional / divisor, Converted = row.Converted / divisor,
                    Fraction = row.Fraction / divisor });
            foreach (var value in new[] { 3, -4 })
            {
                divisor = value;
                commands.Values.Clear();
                var results = async ? await query.ToListAsync() : query.ToList();
                Assert.Equal(2, results.Count);
                Assert.Equal(-2, results[0].Quotient);
                Assert.Equal(-1, results[0].Remainder);
                Assert.Null(results[0].Optional);
                Assert.Equal(-2, results[0].Converted);
                Assert.Equal(-2.5m, results[0].Fraction);
                Assert.Equal(2, results[1].Quotient);
                Assert.Equal(1, results[1].Remainder);
                Assert.Equal(2, results[1].Optional);
                Assert.Equal(2, results[1].Converted);
                Assert.Equal(2.5m, results[1].Fraction);
                Assert.NotEmpty(commands.Values);
                Assert.All(commands.Values, parameter => Assert.Equal((decimal)value, Convert.ToDecimal(parameter)));
                var sql = commands.Sql[^1];
                Assert.Contains(oracle ? "TRUNC(" : " DIV ", sql);
                Assert.Contains(oracle ? "MOD(" : " % ", sql);
                Assert.Contains(" / ", sql);
                Assert.Contains("WHERE", sql);
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, true)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, true)]
        public async Task String_concat_commands_bind_parameters_and_materialize_synthetic_native_results(
            OceanBaseCompatibilityMode mode, bool async, bool relationalNulls)
        {
            using var table = new DataTable();
            table.Columns.Add("Value", typeof(string));
            var commands = new CommandCapture { Results = table };
            await using var context = CreateContext(mode, commands, relationalNulls);
            var oracle = mode == OceanBaseCompatibilityMode.Oracle;
            string? suffix = "海'_%\\";
            var query = context.Set<ShortTextRow>().Select(row => row.Caption + suffix);
            foreach (var value in new string?[] { suffix, "", null, "again", "", null })
            {
                suffix = value;
                commands.StringInputs.Clear();
                table.Rows.Clear();
                // The reader supplies known values; this does not execute the server concatenation function.
                object native = oracle && string.IsNullOrEmpty(value) ? DBNull.Value : value ?? "";
                table.Rows.Add(native);
                var result = Assert.Single(async ? await query.ToListAsync() : query.ToList());
                Assert.Equal(native == DBNull.Value ? null : native, result);
                Assert.Contains(oracle ? " || " : "CONCAT(", commands.Sql[^1]);
                Assert.DoesNotContain(" + ", commands.Sql[^1]);
                if (value is not null && (!oracle || value.Length != 0))
                {
                    var parameter = Assert.Single(commands.StringInputs);
                    Assert.Equal(value, parameter.Value);
                    Assert.True(parameter.Size >= value.Length);
                    Assert.Equal(DbType.String, parameter.DbType);
                }
                else
                {
                    Assert.Empty(commands.StringInputs);
                }
            }

            table.Rows.Clear();
            table.Rows.Add("client");
            var clientProjection = context.Set<ShortTextRow>().Select(row => AppendClientSuffix(row.Caption));
            Assert.Equal("client!", Assert.Single(async ? await clientProjection.ToListAsync() : clientProjection.ToList()));
            var convertedValue = "";
            var convertedQuery = context.Set<ShortTextRow>().Where(row => row.Converted == convertedValue).Select(row => row.Caption);
            foreach (var value in new[] { "", "converted" })
            {
                convertedValue = value;
                commands.StringInputs.Clear();
                Assert.Equal("client", Assert.Single(async ? await convertedQuery.ToListAsync() : convertedQuery.ToList()));
                Assert.Equal(value + "!", Assert.Single(commands.StringInputs).Value);
            }
            Assert.Contains(oracle ? "N'!'" : "'!'", context.Set<ShortTextRow>()
                .Where(row => row.Converted == "").ToQueryString());
            var beforeFailure = commands.Sql.Count;
            var unsupported = context.Set<ShortTextRow>().Where(row => AppendClientSuffix(row.Caption) == "x");
            if (async)
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => unsupported.ToListAsync());
            }
            else
            {
                Assert.Throws<InvalidOperationException>(() => unsupported.ToList());
            }
            Assert.Equal(beforeFailure, commands.Sql.Count);
        }

        private static string AppendClientSuffix(string? value) => value + "!";

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task Oracle_concat_null_predicate_reprocesses_empty_coalesce_parameters(bool async, bool relationalNulls)
        {
            using var table = new DataTable();
            table.Columns.Add("Caption", typeof(string));
            var commands = new CommandCapture { Results = table };
            await using var context = CreateContext(OceanBaseCompatibilityMode.Oracle, commands, relationalNulls);
            string? fallback = "present";
            var query = context.Set<ShortTextRow>()
                .Where(row => (row.Caption + row.Caption ?? fallback) == null).Select(row => row.Caption);
            foreach (var value in new string?[] { "present", "", null, "again", "", null })
            {
                fallback = value;
                Assert.Empty(async ? await query.ToListAsync() : query.ToList());
                var sql = commands.Sql[^1];
                if (string.IsNullOrEmpty(value))
                {
                    Assert.Contains(" || ", sql);
                    Assert.Contains("IS NULL", sql);
                    Assert.DoesNotContain("COALESCE(", sql);
                    Assert.DoesNotContain("WHERE 0 = 1", sql);
                }
                else
                {
                    Assert.Contains("WHERE 0 = 1", sql);
                }
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Text_writes_validate_converted_characters_but_leave_query_and_original_values_unbounded(
            OceanBaseCompatibilityMode mode, bool async)
        {
            using var table = new DataTable();
            table.Columns.Add("Caption", typeof(string));
            table.Columns.Add("Converted", typeof(string));
            table.Rows.Add("海🌊", "海🌊河!");
            var commands = new CommandCapture { Results = table };
            await using var context = CreateContext(mode, commands);
            var boundary = "longer🌊query";
            var query = context.Set<ShortTextRow>().Where(row => row.Caption == boundary)
                .Select(row => new { row.Caption, row.Converted });
            var result = Assert.Single(async ? await query.ToListAsync() : query.ToList());
            Assert.Equal("海🌊", result.Caption);
            Assert.Equal("海🌊河", result.Converted);
            Assert.Equal(boundary, Assert.Single(commands.StringInputs).Value);
            Assert.Equal(boundary.Length, commands.StringInputs[0].Size);
            var row = new ShortTextRow { Code = "id", Caption = result.Caption, Converted = result.Converted };
            context.Add(row);
            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            Assert.Contains("海🌊河!", commands.Values);
            row.Caption = null;
            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            row.Caption = "";
            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            context.ChangeTracker.Clear();
            row = new ShortTextRow { Code = "original-key-longer-than-column", Caption = "old" };
            context.Attach(row);
            row.Caption = "海🌊";
            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            Assert.Contains("original-key-longer-than-column", commands.Values);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, 0)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, 0)]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, 1)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, 1)]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, 2)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, 2)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, 0)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, 0)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, 1)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, 1)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, 2)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, 2)]
        public async Task Text_invalid_current_writes_fail_before_interception(
            OceanBaseCompatibilityMode mode, bool async, int invalid)
        {
            var commands = new CommandCapture();
            await using var context = CreateContext(mode, commands);
            context.Add(new ShortTextRow { Code = "id", Caption = invalid == 0 ? "海🌊河" : invalid == 2 ? "\ud800" : "ok",
                Converted = invalid == 1 ? "海🌊河岸" : "ok" });
            var exception = async ? await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync())
                : Assert.Throws<DbUpdateException>(() => context.SaveChanges());
            Assert.IsAssignableFrom<ArgumentException>(exception.InnerException);
            Assert.Empty(commands.Sql);
            Assert.Empty(commands.Values);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task National_text_and_enum_Guid_converters_preserve_parameter_type_and_query_size(
            OceanBaseCompatibilityMode mode, bool async)
        {
            var oracle = mode == OceanBaseCompatibilityMode.Oracle;
            var token = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
            using var table = new DataTable();
            table.Columns.Add("Caption", typeof(string));
            table.Columns.Add("Fixed", typeof(string));
            table.Columns.Add("State", typeof(string));
            table.Columns.Add("Token", typeof(string));
            table.Rows.Add("海洋", "海", "Active", token.ToString());
            var commands = new CommandCapture { Results = table };
            await using var context = CreateContext(mode, commands);
            var boundary = "海洋🌊数据库";
            var query = context.Set<TextParameterRow>().Where(row => row.Caption == boundary && row.State == RowState.Active)
                .Select(row => new { row.Caption, row.Fixed, row.State, row.Token });
            var result = Assert.Single(async ? await query.ToListAsync() : query.ToList());
            Assert.Equal("海洋", result.Caption);
            Assert.Equal("海", result.Fixed);
            Assert.Equal(RowState.Active, result.State);
            Assert.Equal(token, result.Token);
            var operand = Assert.Single(commands.StringInputs);
            Assert.Equal(boundary, operand.Value);
            Assert.Equal(boundary.Length, operand.Size);
            Assert.Equal(oracle ? OceanBaseDbType.NVarchar2 : (OceanBaseDbType?)null, operand.OceanBaseDbType);
            Assert.Contains(oracle ? "N'Active'" : "'Active'", query.ToQueryString());
            context.Add(new TextParameterRow { Id = 1, Caption = result.Caption, Fixed = result.Fixed,
                State = result.State, Token = result.Token });
            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            foreach (var value in new[] { "海洋", "Active", token.ToString() })
            {
                Assert.Contains(commands.StringInputs, parameter => Equals(parameter.Value, value)
                    && parameter.OceanBaseDbType == (oracle ? OceanBaseDbType.NVarchar2 : (OceanBaseDbType?)null));
            }

            Assert.Contains(commands.StringInputs, parameter => Equals(parameter.Value, "海")
                && parameter.OceanBaseDbType == (oracle ? OceanBaseDbType.NChar : (OceanBaseDbType?)null));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Decimal_queries_keep_comparands_and_converted_writes_check_physical_precision(
            OceanBaseCompatibilityMode mode, bool async)
        {
            using var table = new DataTable();
            table.Columns.Add("Value", typeof(decimal));
            table.Columns.Add("Converted", typeof(decimal));
            table.Columns.Add("Optional", typeof(decimal));
            table.Rows.Add(999.99m, 2.5m, DBNull.Value);
            var commands = new CommandCapture { Results = table };
            await using var context = CreateContext(mode, commands);
            var boundary = decimal.MaxValue;
            var query = context.Set<DecimalRow>().Where(row => row.Value < boundary)
                .Select(row => new { row.Value, row.Converted, row.Optional });
            var result = Assert.Single(async ? await query.ToListAsync() : query.ToList());
            Assert.Equal(999.99m, result.Value);
            Assert.Equal(1.25m, result.Converted);
            Assert.Null(result.Optional);
            var operand = Assert.Single(commands.DecimalInputs);
            Assert.Equal(boundary, operand.Value);
            Assert.Equal((byte)0, operand.Precision);
            Assert.Null(operand.DecimalScale);
            Assert.Contains("79228162514264337593543950335.0", context.Set<DecimalRow>()
                .Select(row => row.Value == 1m ? decimal.MaxValue : row.Value).ToQueryString());
            context.Add(new DecimalRow { Id = 1, Value = 999.9900m, Converted = 1.25m });
            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            Assert.Contains(2.5m, commands.Values);
            Assert.All(commands.DecimalInputs, parameter =>
            {
                Assert.Equal((byte)0, parameter.Precision);
                Assert.Null(parameter.DecimalScale);
            });
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, true)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, true)]
        public async Task Decimal_invalid_writes_reject_before_command_execution(
            OceanBaseCompatibilityMode mode, bool converted, bool async)
        {
            var commands = new CommandCapture();
            await using var context = CreateContext(mode, commands);
            context.Add(new DecimalRow { Id = 1, Value = converted ? 1m : 0.001m,
                Converted = converted ? 500m : 1m });
            var exception = async ? await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync())
                : Assert.Throws<DbUpdateException>(() => context.SaveChanges());
            Assert.IsAssignableFrom<ArgumentException>(exception.InnerException);
            Assert.Empty(commands.Sql);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Oracle_fixed_point_alias_queries_are_lossless_and_NUMBER_writes_remain_fractional(bool async)
        {
            using var table = new DataTable();
            table.Columns.Add("Decimal", typeof(decimal));
            table.Columns.Add("Numeric", typeof(decimal));
            table.Columns.Add("Converted", typeof(decimal));
            table.Columns.Add("Unqualified", typeof(decimal));
            table.Rows.Add(12m, -3m, 4m, 0.125m);
            var commands = new CommandCapture { Results = table };
            await using var context = CreateContext(OceanBaseCompatibilityMode.Oracle, commands);
            var upper = decimal.MaxValue / 10;
            var lower = -upper;
            var fractional = 0.125m;
            var query = context.Set<FixedPointAliasRow>().Where(row => row.Decimal < upper && row.Numeric > lower
                    && row.Converted > fractional && row.Unqualified == fractional)
                .Select(row => new { row.Decimal, row.Numeric, row.Converted, row.Unqualified });
            var result = Assert.Single(async ? await query.ToListAsync() : query.ToList());
            Assert.Equal(12m, result.Decimal);
            Assert.Equal(-3m, result.Numeric);
            Assert.Equal(2m, result.Converted);
            Assert.Equal(0.125m, result.Unqualified);
            Assert.Equal(4, commands.DecimalInputs.Count);
            foreach (var value in new[] { upper, lower, 0.25m, fractional })
            {
                Assert.Contains(commands.DecimalInputs, parameter => Equals(parameter.Value, value));
            }

            context.Add(new FixedPointAliasRow { Id = 1, Decimal = decimal.MaxValue, Numeric = decimal.MinValue,
                Converted = 0.5m, Unqualified = 0.125m });
            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            foreach (var value in new[] { decimal.MaxValue, decimal.MinValue, 1m, 0.125m })
            {
                Assert.Contains(value, commands.Values);
            }

            Assert.All(commands.DecimalInputs, parameter =>
            {
                Assert.Equal((byte)0, parameter.Precision);
                Assert.Null(parameter.DecimalScale);
            });
        }

        [Theory]
        [InlineData(false, 0)]
        [InlineData(false, 1)]
        [InlineData(false, 2)]
        [InlineData(true, 0)]
        [InlineData(true, 1)]
        [InlineData(true, 2)]
        public async Task Oracle_fixed_point_alias_fractional_current_writes_reject_before_commands(bool async, int invalid)
        {
            var commands = new CommandCapture();
            await using var context = CreateContext(OceanBaseCompatibilityMode.Oracle, commands);
            context.Add(new FixedPointAliasRow { Id = 1, Decimal = invalid == 0 ? 0.1m : 1m,
                Numeric = invalid == 1 ? 0.1m : 1m, Converted = invalid == 2 ? 0.25m : 0.5m,
                Unqualified = 0.125m });
            var exception = async ? await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync())
                : Assert.Throws<DbUpdateException>(() => context.SaveChanges());
            Assert.IsType<ArgumentException>(exception.InnerException);
            Assert.Empty(commands.Sql);
            Assert.Empty(commands.Values);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Temporal_query_comparands_keep_transport_precision_independent_of_columns(
            OceanBaseCompatibilityMode mode, bool async)
        {
            var oracle = mode == OceanBaseCompatibilityMode.Oracle;
            var stamp = new DateTime(2026, 11, 10, 12, 0, 0).AddTicks(1234560);
            var zoned = new DateTimeOffset(stamp, TimeSpan.FromHours(8));
            var clock = new TimeOnly(stamp.Ticks % TimeSpan.TicksPerDay);
            var duration = TimeSpan.FromDays(oracle ? 100 : 1).Add(TimeSpan.FromTicks(1234560));
            var commands = new CommandCapture { Results = new DataTable() };
            using var results = commands.Results;
            results.Columns.Add("Id", oracle ? typeof(decimal) : typeof(int));
            results.Rows.Add(1);
            await using var context = CreateContext(mode, commands);
            var query = context.Set<NarrowTemporalRow>().Where(row => row.Stamp < stamp && row.Zoned < zoned
                && row.Clock < clock && row.Duration < duration && row.Date < stamp).Select(row => row.Id);
            var sql = query.ToQueryString();
            Assert.Contains(".123456", sql);
            Assert.Contains(".123456", context.Set<NarrowTemporalRow>().Select(row => row.Stamp < new DateTime(2026, 11, 10, 12, 0, 0).AddTicks(1234560)).ToQueryString());
            Assert.Equal(1, Assert.Single(async ? await query.ToListAsync() : query.ToList()));
            context.Add(new NarrowTemporalRow { Id = 2, Stamp = new DateTime(2026, 11, 10),
                Date = new DateTime(2026, 11, 10), Zoned = new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero) });
            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Temporal_column_precision_still_rejects_lossy_writes_before_execution(
            OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new CommandCapture();
            await using var context = CreateContext(mode, commands);
            context.Add(new NarrowTemporalRow { Id = 1, Stamp = new DateTime(2026, 11, 10).AddTicks(10) });
            var exception = async ? await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync())
                : Assert.Throws<DbUpdateException>(() => context.SaveChanges());
            Assert.IsType<ArgumentException>(exception.InnerException);
            Assert.Empty(commands.Sql);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Floating_columns_materialize_alternate_widths_and_keep_CLR_write_binding(
            OceanBaseCompatibilityMode mode, bool async)
        {
            using var table = new DataTable();
            table.Columns.Add("Id", mode == OceanBaseCompatibilityMode.Oracle ? typeof(decimal) : typeof(int));
            table.Columns.Add("Narrow", typeof(float));
            table.Columns.Add("Wide", typeof(double));
            table.Columns.Add("Converted", typeof(float));
            table.Columns.Add("Optional", typeof(double));
            table.Rows.Add(9, 1.25f, 1.25d, 2.5f, DBNull.Value);
            var commands = new CommandCapture { Results = table };
            await using var context = CreateContext(mode, commands);
            var query = context.Set<FloatingRow>().Select(row => new { row.Id, row.Narrow, row.Wide, row.Converted, row.Optional });
            var row = Assert.Single(async ? await query.ToListAsync() : query.ToList());
            Assert.Equal(1.25d, row.Narrow);
            Assert.Equal(1.25f, row.Wide);
            Assert.Equal(1.25d, row.Converted);
            Assert.Null(row.Optional);
            context.Add(new FloatingRow { Id = row.Id, Narrow = row.Narrow, Wide = row.Wide, Converted = row.Converted });
            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            Assert.Contains(1.25d, commands.Values);
            Assert.Contains(1.25f, commands.Values);
            Assert.Contains(2.5d, commands.Values);
            Assert.Contains(DBNull.Value, commands.Values);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Exact_decimal_floating_columns_materialize_through_composed_readers(
            OceanBaseCompatibilityMode mode, bool async)
        {
            using var table = new DataTable();
            table.Columns.Add("Id", typeof(int));
            foreach (var name in new[] { "Narrow", "Wide", "Converted", "Optional" })
                table.Columns.Add(name, typeof(OceanBaseDecimal));
            var cases = new (string Coefficient, int Scale, float Single, double Double)[]
            {
                ("1", -30, 1E30f, 1E30d),
                ("12345678901234567890123456789012345678", 38, 0.123456789f, 0.12345678901234568d),
                ("1", 40, 1E-40f, 1E-40d),
                ("-1", 40, -1E-40f, -1E-40d),
                ("-1", int.MaxValue, -0f, -0d),
                ("0", int.MinValue, 0f, 0d)
            };
            for (var i = 0; i < cases.Length; i++)
            {
                var value = new OceanBaseDecimal(BigInteger.Parse(cases[i].Coefficient, CultureInfo.InvariantCulture), cases[i].Scale);
                table.Rows.Add(i + 1, value, value, value, value);
            }
            var commands = new CommandCapture { Results = table };
            await using var context = CreateContext(mode, commands);
            var query = context.Set<FloatingRow>().OrderBy(row => row.Id)
                .Select(row => new { row.Id, row.Narrow, row.Wide, row.Converted, row.Optional });
            var rows = async ? await query.ToListAsync() : query.ToList();
            Assert.Equal(cases.Length, rows.Count);
            for (var i = 0; i < cases.Length; i++)
            {
                Assert.Equal(BitConverter.DoubleToInt64Bits(cases[i].Double), BitConverter.DoubleToInt64Bits(rows[i].Narrow));
                Assert.Equal(BitConverter.SingleToInt32Bits(cases[i].Single), BitConverter.SingleToInt32Bits(rows[i].Wide));
                Assert.Equal(BitConverter.DoubleToInt64Bits(cases[i].Double / 2), BitConverter.DoubleToInt64Bits(rows[i].Converted));
                Assert.Equal(BitConverter.DoubleToInt64Bits(cases[i].Double), BitConverter.DoubleToInt64Bits(rows[i].Optional!.Value));
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Floating_query_operands_do_not_inherit_column_capacity(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode, new CommandCapture());
            var limit = double.MaxValue;
            var rows = context.Set<FloatingRow>();
            Assert.Contains("1.7976931348623157E+308", rows.Where(row => row.Narrow < limit).ToQueryString());
            Assert.Contains("1.7976931348623157E+308", rows.Select(row => row.Narrow + double.MaxValue).ToQueryString());
            Assert.Contains("1.7976931348623157E+308", rows.Select(row => row.Narrow == 1 ? double.MaxValue : row.Narrow).ToQueryString());
            Assert.Contains("-1", rows.Where(row => row.Narrow < -1d).ToQueryString());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, true)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, true)]
        public async Task Narrow_floating_write_checks_converted_values_before_execution(
            OceanBaseCompatibilityMode mode, bool converted, bool async)
        {
            var commands = new CommandCapture();
            await using var context = CreateContext(mode, commands);
            context.Add(new FloatingRow { Id = 1, Narrow = converted ? 1 : double.MaxValue,
                Converted = converted ? double.MaxValue / 2 : 1 });
            var exception = async ? await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync())
                : Assert.Throws<DbUpdateException>(() => context.SaveChanges());
            Assert.IsType<ArgumentOutOfRangeException>(exception.InnerException);
            Assert.Empty(commands.Sql);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, true)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, true)]
        public async Task Generated_floating_outputs_convert_width_and_reject_finite_overflow(
            OceanBaseCompatibilityMode mode, bool overflow, bool async)
        {
            var output = overflow ? double.MaxValue : 1.25d;
            using var table = new DataTable();
            table.Columns.Add("Converted", typeof(double));
            table.Columns.Add("Value", typeof(double));
            table.Columns.Add("Wide", typeof(double));
            table.Rows.Add(output, output, output);
            var commands = new CommandCapture { Results = table, OutputValue = output };
            await using var context = CreateContext(mode, commands);
            var entity = new GeneratedFloating { Id = 1 };
            context.Add(entity);
            if (overflow)
            {
                var exception = async ? await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync())
                    : Assert.Throws<DbUpdateException>(() => context.SaveChanges());
                Assert.IsType<OverflowException>(exception.InnerException);
            }
            else
            {
                Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
                Assert.Equal(1.25f, entity.Value);
                Assert.Equal(1.25d, entity.Wide);
                Assert.Equal(0.625d, entity.Converted);
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Exact_decimal_generated_floating_outputs_round_and_reject_overflow(
            OceanBaseCompatibilityMode mode, bool async)
        {
            foreach (var item in new (string Coefficient, int Scale, float Single, double Double)[]
            {
                ("1", -30, 1E30f, 1E30d),
                ("12345678901234567890123456789012345678", 38, 0.123456789f, 0.12345678901234568d),
                ("-1", 40, -1E-40f, -1E-40d),
                ("-1", int.MaxValue, -0f, -0d),
                ("0", int.MinValue, 0f, 0d),
                ("1", -39, float.PositiveInfinity, 1E39d),
                ("-1", int.MinValue, float.NegativeInfinity, double.NegativeInfinity)
            })
            {
                var output = new OceanBaseDecimal(BigInteger.Parse(item.Coefficient, CultureInfo.InvariantCulture), item.Scale);
                using var table = new DataTable();
                foreach (var name in new[] { "Converted", "Value", "Wide" })
                    table.Columns.Add(name, typeof(OceanBaseDecimal));
                table.Rows.Add(output, output, output);
                var commands = new CommandCapture { Results = table, OutputValue = output };
                await using var context = CreateContext(mode, commands);
                var entity = new GeneratedFloating { Id = 1 };
                context.Add(entity);
                if (float.IsInfinity(item.Single))
                {
                    var exception = async ? await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync())
                        : Assert.Throws<DbUpdateException>(() => context.SaveChanges());
                    Assert.IsType<OverflowException>(exception.InnerException);
                }
                else
                {
                    Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
                    Assert.Equal(BitConverter.SingleToInt32Bits(item.Single), BitConverter.SingleToInt32Bits(entity.Value));
                    Assert.Equal(BitConverter.DoubleToInt64Bits(item.Double), BitConverter.DoubleToInt64Bits(entity.Wide));
                    Assert.Equal(BitConverter.DoubleToInt64Bits(item.Double / 2), BitConverter.DoubleToInt64Bits(entity.Converted));
                }
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Explicit_Guid_storage_pairs_query_materialization_and_write_conversion(
            OceanBaseCompatibilityMode mode, bool async)
        {
            var token = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
            using var table = new DataTable();
            table.Columns.Add("Id", mode == OceanBaseCompatibilityMode.Oracle ? typeof(decimal) : typeof(int));
            table.Columns.Add("Text", typeof(string));
            table.Columns.Add("Binary", typeof(byte[]));
            table.Columns.Add("Optional", mode == OceanBaseCompatibilityMode.Oracle ? typeof(string) : typeof(byte[]));
            table.Rows.Add(7, "00112233-4455-6677-8899-aabbccddeeff",
                new byte[] { 0x33, 0x22, 0x11, 0, 0x55, 0x44, 0x77, 0x66, 0x88, 0x99, 0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0xff }, DBNull.Value);
            var commands = new CommandCapture { Results = table };
            await using var context = CreateContext(mode, commands);
            var query = context.Set<GuidStorageRow>().Select(row => new { row.Id, row.Text, row.Binary, row.Optional });
            var row = Assert.Single(async ? await query.ToListAsync() : query.ToList());
            Assert.Equal(token, row.Text);
            Assert.Equal(token, row.Binary);
            Assert.Null(row.Optional);
            var sql = context.Set<GuidStorageRow>().Where(row => row.Text == token && row.Binary == token).ToQueryString();
            Assert.Contains("00112233-4455-6677-8899-aabbccddeeff", sql);
            Assert.Contains("33221100554477668899AABBCCDDEEFF", sql);
            context.Add(new GuidStorageRow { Id = row.Id, Text = row.Text, Binary = row.Binary });
            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            Assert.Contains("00112233-4455-6677-8899-aabbccddeeff", commands.Values);
            Assert.Equal(token.ToByteArray(), Assert.Single(commands.Values.OfType<byte[]>()));
            Assert.Contains(DBNull.Value, commands.Values);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Generated_explicit_Guid_outputs_use_the_declared_representation(
            OceanBaseCompatibilityMode mode, bool async)
        {
            var token = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
            object output = mode == OceanBaseCompatibilityMode.Oracle ? token.ToString() : token.ToByteArray();
            using var table = new DataTable();
            table.Columns.Add("Data", output.GetType());
            table.Rows.Add(output);
            var commands = new CommandCapture { Results = table, OutputValue = output };
            await using var context = CreateContext(mode, commands);
            var entity = new GeneratedExplicitGuid { Id = 1 };
            context.Add(entity);
            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            Assert.Equal(token, entity.Data);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Query_operands_and_results_are_not_limited_to_integer_column_capacity(OceanBaseCompatibilityMode mode)
        {
            var commands = new CommandCapture();
            using var context = CreateContext(mode, commands);
            var rows = context.Set<NarrowScalarRow>();
            const long literal = 1000000000L;
            var captured = literal;
            Assert.Contains("1000000000", rows.Select(row => row.Count + literal).ToQueryString());
            Assert.Contains("1000000000", rows.Where(row => row.Count < captured).ToQueryString());
            Assert.Contains("1000000000", rows.Select(row => row.Count == 1 ? literal : row.Count).ToQueryString());
            Assert.Contains("1000000000", rows.Select(row => row.Count * literal).ToQueryString());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Binary_query_comparands_can_exceed_the_column_length(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode, new CommandCapture());
            var bytes = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 };
            var sql = context.Set<BinaryRow>().Where(row => row.Payload == bytes).ToQueryString();
            Assert.Contains("000102030405060708", sql);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Explicit_integer_and_boolean_columns_keep_converters_in_query_and_write(
            OceanBaseCompatibilityMode mode, bool async)
        {
            var oracle = mode == OceanBaseCompatibilityMode.Oracle;
            using var table = new DataTable();
            table.Columns.Add("Id", oracle ? typeof(decimal) : typeof(int));
            table.Columns.Add("Count", oracle ? typeof(decimal) : typeof(int));
            table.Columns.Add("State", oracle ? typeof(decimal) : typeof(sbyte));
            table.Columns.Add("Enabled", oracle ? typeof(decimal) : typeof(sbyte));
            table.Rows.Add(1, 123, 2, 1);
            var commands = new CommandCapture { Results = table };
            await using var context = CreateContext(mode, commands);
            var query = context.Set<NarrowScalarRow>().Select(row => new { row.Id, row.Count, row.State, row.Enabled });

            var row = Assert.Single(async ? await query.ToListAsync() : query.ToList());
            Assert.Equal(123L, row.Count);
            Assert.Equal(RowState.Active, row.State);
            Assert.True(row.Enabled);
            var entityType = context.Model.FindEntityType(typeof(NarrowScalarRow))!;
            Assert.Equal(oracle ? "NUMBER(3)" : "MEDIUMINT", entityType.FindProperty(nameof(NarrowScalarRow.Count))!.GetRelationalTypeMapping().StoreType);
            Assert.Equal(oracle ? "NUMBER(3)" : "BOOLEAN", entityType.FindProperty(nameof(NarrowScalarRow.Enabled))!.GetRelationalTypeMapping().StoreType);
            context.Add(new NarrowScalarRow { Id = row.Id, Count = row.Count, State = row.State, Enabled = row.Enabled });

            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            Assert.Contains(123L, commands.Values);
            Assert.Contains(2, commands.Values);
            Assert.Contains(oracle ? (object)1m : true, commands.Values);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Narrow_integer_columns_reject_oversized_writes_before_execution(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new CommandCapture();
            await using var context = CreateContext(mode, commands);
            context.Add(new NarrowScalarRow { Id = 1, Count = mode == OceanBaseCompatibilityMode.Oracle ? 1000 : 8388608 });
            var exception = async ? await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync())
                : Assert.Throws<DbUpdateException>(() => context.SaveChanges());
            Assert.IsType<ArgumentOutOfRangeException>(exception.InnerException);
            Assert.Empty(commands.Sql);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Query_and_write_apply_bounded_binary_and_custom_converters(OceanBaseCompatibilityMode mode, bool async)
        {
            var bytes = new byte[] { 0, 0x80, 0xff, 0 };
            using var table = new DataTable();
            table.Columns.Add("Id", mode == OceanBaseCompatibilityMode.Oracle ? typeof(decimal) : typeof(int));
            table.Columns.Add("Payload", typeof(byte[]));
            table.Columns.Add("Optional", typeof(byte[]));
            table.Columns.Add("Hex", typeof(byte[]));
            table.Rows.Add(17, bytes, DBNull.Value, bytes);
            var commands = new CommandCapture { Results = table };
            await using var context = CreateContext(mode, commands);
            var query = context.Set<BinaryRow>().Select(row => new { row.Id, row.Payload, row.Optional, row.Hex });

            var row = Assert.Single(async ? await query.ToListAsync() : query.ToList());
            Assert.Equal(bytes, row.Payload);
            Assert.Null(row.Optional);
            Assert.Equal("0080FF00", row.Hex);
            var mapping = context.Model.FindEntityType(typeof(BinaryRow))!.FindProperty(nameof(BinaryRow.Payload))!
                .GetRelationalTypeMapping();
            Assert.Equal(mode == OceanBaseCompatibilityMode.Oracle ? "RAW(8)" : "VARBINARY(8)", mapping.StoreType);
            context.Add(new BinaryRow { Id = row.Id, Payload = row.Payload, Hex = row.Hex });

            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            Assert.Equal(2, commands.Values.OfType<byte[]>().Count(value => value.SequenceEqual(bytes)));
            Assert.Contains(DBNull.Value, commands.Values);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Oversized_binary_write_is_rejected_before_command_execution(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new CommandCapture();
            await using var context = CreateContext(mode, commands);
            context.Add(new BinaryRow { Id = 1, Payload = new byte[9], Hex = "01" });

            var exception = async
                ? await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync())
                : Assert.Throws<DbUpdateException>(() => context.SaveChanges());
            Assert.IsType<ArgumentException>(exception.InnerException);
            Assert.Empty(commands.Sql);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, false, true)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true, true)]
        public async Task Generated_binary_and_guid_outputs_apply_provider_converters(
            OceanBaseCompatibilityMode mode, bool guid, bool async)
        {
            var token = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
            var bytes = new byte[] { 0, 0x80, 0xff };
            object output = guid ? mode == OceanBaseCompatibilityMode.Oracle ? token.ToByteArray() : token.ToString() : bytes;
            using var table = new DataTable();
            table.Columns.Add("Data", output.GetType());
            table.Rows.Add(output);
            var commands = new CommandCapture { Results = table, OutputValue = output };
            await using var context = CreateContext(mode, commands);
            var binaryEntity = new GeneratedBinary { Id = 1 };
            var guidEntity = new GeneratedGuid { Id = 1 };
            context.Add(guid ? (object)guidEntity : binaryEntity);

            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            if (guid) Assert.Equal(token, guidEntity.Data);
            else Assert.Equal(bytes, binaryEntity.Data);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Query_and_write_apply_temporal_transport_conversions(OceanBaseCompatibilityMode mode, bool async)
        {
            var oracle = mode == OceanBaseCompatibilityMode.Oracle;
            var stamp = new DateTime(2026, 11, 10, 12, 30, 45);
            var zoned = new DateTimeOffset(stamp, TimeSpan.FromHours(8));
            using var table = new DataTable();
            table.Columns.Add("Id", oracle ? typeof(decimal) : typeof(int));
            table.Columns.Add("Date", typeof(DateTime));
            table.Columns.Add("Stamp", oracle ? typeof(OceanBaseTimestamp) : typeof(DateTime));
            table.Columns.Add("Zoned", oracle ? typeof(OceanBaseTimestampWithTimeZone) : typeof(DateTime));
            table.Columns.Add("Clock", oracle ? typeof(OceanBaseIntervalDayToSecond) : typeof(TimeSpan));
            table.Columns.Add("Duration", oracle ? typeof(OceanBaseIntervalDayToSecond) : typeof(TimeSpan));
            table.Columns.Add("OptionalDate", typeof(DateTime));
            table.Rows.Add(19, new DateTime(2026, 11, 10), oracle ? new OceanBaseTimestamp(stamp) : stamp,
                oracle ? new OceanBaseTimestampWithTimeZone(zoned) : new DateTime(2026, 11, 10, 4, 30, 45),
                oracle ? new OceanBaseIntervalDayToSecond(false, 0, 12, 30, 45, leadingPrecision: 1, scale: 7) : new TimeSpan(12, 30, 45),
                oracle ? new OceanBaseIntervalDayToSecond(true, 2, 3, 4, 5, scale: 7) : -new TimeSpan(2, 3, 4, 5), DBNull.Value);
            var commands = new CommandCapture { Results = table };
            await using var context = CreateContext(mode, commands);
            var query = context.Set<TemporalRow>().Select(row => new
                { row.Id, row.Date, row.Stamp, row.Zoned, row.Clock, row.Duration, row.OptionalDate });

            var row = Assert.Single(async ? await query.ToListAsync() : query.ToList());

            Assert.Equal(new DateOnly(2026, 11, 10), row.Date);
            Assert.Equal(stamp, row.Stamp);
            Assert.Equal(zoned.UtcTicks, row.Zoned.UtcTicks);
            Assert.Equal(oracle ? TimeSpan.FromHours(8) : TimeSpan.Zero, row.Zoned.Offset);
            Assert.Equal(new TimeOnly(12, 30, 45), row.Clock);
            Assert.Equal(-new TimeSpan(2, 3, 4, 5), row.Duration);
            Assert.Null(row.OptionalDate);
            context.Add(new TemporalRow { Id = row.Id, Date = row.Date, Stamp = row.Stamp, Zoned = row.Zoned,
                Clock = row.Clock, Duration = row.Duration });
            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            Assert.Contains(commands.Values, value => Equals(value, new DateTime(2026, 11, 10)));
            Assert.Contains(commands.Values, value => Equals(value, stamp));
            Assert.Contains(commands.Values, value => Equals(value, DBNull.Value));
            if (oracle)
            {
                Assert.Contains(commands.Values, value => value is OceanBaseIntervalDayToSecond interval && interval.ToTimeSpan() == row.Duration);
                Assert.Contains(commands.Values, value => value is DateTimeOffset date && date.Offset == TimeSpan.FromHours(8));
            }
            else
            {
                Assert.Contains(commands.Values, value => Equals(value, new DateTime(2026, 11, 10, 4, 30, 45)));
                Assert.Contains(commands.Values, value => Equals(value, row.Duration));
            }
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task Oracle_generated_temporal_values_use_exact_public_conversions(bool zoned, bool async)
        {
            var stamp = new DateTime(2026, 11, 10, 12, 30, 45).AddTicks(1234567);
            var date = new DateTimeOffset(stamp, TimeSpan.FromMinutes(-330));
            var commands = new CommandCapture { OutputValue = zoned ? new OceanBaseTimestampWithTimeZone(date) : new OceanBaseTimestamp(stamp) };
            await using var context = CreateContext(OceanBaseCompatibilityMode.Oracle, commands);
            var plainEntity = new GeneratedStamp { Id = 1 };
            var zonedEntity = new GeneratedZoned { Id = 1 };
            context.Add(zoned ? (object)zonedEntity : plainEntity);

            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            if (zoned)
            {
                Assert.Equal(date.UtcTicks, zonedEntity.Stamp.UtcTicks);
                Assert.Equal(date.Offset, zonedEntity.Stamp.Offset);
            }
            else
            {
                Assert.Equal(stamp, plainEntity.Stamp);
            }
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task Oracle_generated_keys_accept_exact_connector_numbers(bool exactNumber, bool async)
        {
            var commands = new CommandCapture { OutputValue = exactNumber ? new OceanBaseDecimal(42m) : 42m };
            await using var context = CreateContext(OceanBaseCompatibilityMode.Oracle, commands);
            var entity = new KeyOnly();
            context.Add(entity);

            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            Assert.Equal(42, entity.Id);
        }

        [Theory]
        [InlineData(false, false, false)]
        [InlineData(false, false, true)]
        [InlineData(false, true, false)]
        [InlineData(false, true, true)]
        [InlineData(true, false, false)]
        [InlineData(true, false, true)]
        [InlineData(true, true, false)]
        [InlineData(true, true, true)]
        public async Task Oracle_generated_integer_and_enum_outputs_reject_floating_fractions(bool single, bool converted, bool async)
        {
            var commands = new CommandCapture { OutputValue = single ? (object)1.5f : 1.5d };
            await using var context = CreateContext(OceanBaseCompatibilityMode.Oracle, commands);
            if (converted) context.Add(new GeneratedState { Id = 1 });
            else context.Add(new KeyOnly());
            var exception = async ? await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync())
                : Assert.Throws<DbUpdateException>(() => context.SaveChanges());
            Assert.IsType<InvalidCastException>(exception.InnerException);
            Assert.Single(commands.Sql);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Oracle_generated_integer_and_enum_outputs_accept_exact_floating_values(bool async)
        {
            foreach (var value in new object[] { 2d, 2f })
            {
                var commands = new CommandCapture { OutputValue = value };
                await using var context = CreateContext(OceanBaseCompatibilityMode.Oracle, commands);
                var entity = new GeneratedState { Id = 1 };
                var key = new KeyOnly();
                context.Add(entity);
                context.Add(key);
                Assert.Equal(2, async ? await context.SaveChangesAsync() : context.SaveChanges());
                Assert.Equal(RowState.Active, entity.State);
                Assert.Equal(2, key.Id);
            }
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task Oracle_generated_integer_keys_reject_fraction_and_overflow(bool overflow, bool async)
        {
            var commands = new CommandCapture { OutputValue = overflow ? (decimal)int.MaxValue + 1 : 1.5m };
            await using var context = CreateContext(OceanBaseCompatibilityMode.Oracle, commands);
            context.Add(new KeyOnly());

            var exception = async
                ? await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync())
                : Assert.Throws<DbUpdateException>(() => context.SaveChanges());

            if (overflow)
            {
                Assert.IsType<OverflowException>(exception.InnerException);
            }
            else
            {
                Assert.IsType<InvalidCastException>(exception.InnerException);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Oracle_generated_Boolean_values_apply_converter_after_numeric_normalization(bool async)
        {
            var commands = new CommandCapture { OutputValue = new OceanBaseDecimal(1m) };
            await using var context = CreateContext(OceanBaseCompatibilityMode.Oracle, commands);
            var entity = new GeneratedFlag { Id = 5 };
            context.Add(entity);

            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            Assert.True(entity.Enabled);
            Assert.Contains(commands.Sql, sql => sql.Contains("RETURNING \"Enabled\"", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Query_and_write_apply_scalar_converters(OceanBaseCompatibilityMode mode, bool async)
        {
            var oracle = mode == OceanBaseCompatibilityMode.Oracle;
            var token = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
            using var table = new DataTable();
            table.Columns.Add("Id", oracle ? typeof(decimal) : typeof(int));
            table.Columns.Add("Count", oracle ? typeof(decimal) : typeof(uint));
            table.Columns.Add("Enabled", oracle ? typeof(decimal) : typeof(long));
            table.Columns.Add("OptionalCount", oracle ? typeof(decimal) : typeof(int));
            table.Columns.Add("OptionalFlag", oracle ? typeof(decimal) : typeof(long));
            table.Columns.Add("State", oracle ? typeof(decimal) : typeof(int));
            table.Columns.Add("Token", oracle ? typeof(byte[]) : typeof(string));
            table.Rows.Add(17, uint.MaxValue, 1, DBNull.Value, DBNull.Value, 2, oracle ? token.ToByteArray() : token.ToString());
            var commands = new CommandCapture { Results = table };
            await using var context = CreateContext(mode, commands);
            var query = context.Set<ScalarRow>().Select(row => new
                { row.Id, row.Count, row.Enabled, row.OptionalCount, row.OptionalFlag, row.State, row.Token });

            var row = Assert.Single(async ? await query.ToListAsync() : query.ToList());

            Assert.Equal(17, row.Id);
            Assert.Equal(uint.MaxValue, row.Count);
            Assert.True(row.Enabled);
            Assert.Null(row.OptionalCount);
            Assert.Null(row.OptionalFlag);
            Assert.Equal(RowState.Active, row.State);
            Assert.Equal(token, row.Token);
            context.Add(new ScalarRow { Id = row.Id, Count = row.Count, Enabled = row.Enabled, State = row.State, Token = row.Token });
            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            Assert.Contains(commands.Values, value => Equals(value, uint.MaxValue));
            Assert.Contains(commands.Values, value => Equals(value, 2));
            Assert.Contains(commands.Values, value => Equals(value, oracle ? 1m : (object)true));
            Assert.Contains(commands.Values, value => Equals(value, DBNull.Value));
            if (oracle)
            {
                Assert.Contains(commands.Values, value => value is byte[] bytes && bytes.SequenceEqual(token.ToByteArray()));
            }
            else
            {
                Assert.Contains(token.ToString(), commands.Values);
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task SaveChanges_binds_values_and_propagates_generated_keys(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new CommandCapture();
            await using var context = CreateContext(mode, commands);
            var customer = new Customer { Name = "O'Reilly", Version = 1 };
            context.Add(customer);

            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            Assert.Equal(42, customer.Id);
            Assert.Contains(commands.Sql, sql => sql.StartsWith("INSERT INTO", StringComparison.Ordinal));
            Assert.Contains(commands.Values, value => Equals(value, "O'Reilly"));
            Assert.All(commands.Sql, sql => Assert.DoesNotContain("O'Reilly", sql));
            if (mode == OceanBaseCompatibilityMode.MySql)
            {
                Assert.Contains(commands.Sql, sql => sql.Contains("LAST_INSERT_ID()", StringComparison.Ordinal));
            }
            else
            {
                Assert.Contains(commands.Sql, sql => sql.Contains("RETURNING \"Id\" INTO :ob_out_0", StringComparison.Ordinal));
                Assert.DoesNotContain(commands.Sql, sql => sql.Contains("LAST_INSERT_ID", StringComparison.Ordinal));
            }

            customer.Name = "Updated";
            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            Assert.Contains(commands.Sql, sql => sql.StartsWith("UPDATE", StringComparison.Ordinal));

            context.Remove(customer);
            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            Assert.Contains(commands.Sql, sql => sql.StartsWith("DELETE", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Zero_affected_rows_throws_concurrency_exception(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new CommandCapture { AffectedRows = 0 };
            await using var context = CreateContext(mode, commands);
            var customer = new Customer { Id = 1, Name = "Previous", Version = 1 };
            context.Attach(customer);
            customer.Name = "Updated";

            if (async)
            {
                await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => context.SaveChangesAsync());
            }
            else
            {
                Assert.Throws<DbUpdateConcurrencyException>(() => context.SaveChanges());
            }

            Assert.Contains(commands.Sql, sql => sql.Contains("Version", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Query_materializes_scalar_results(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new CommandCapture();
            await using var context = CreateContext(mode, commands);
            var minimumId = 3;
            var query = context.Set<Customer>().Where(customer => customer.Id > minimumId).Select(customer => customer.Id);

            var values = async ? await query.ToListAsync() : query.ToList();

            Assert.Equal([42], values);
            Assert.Contains(commands.Sql, sql => sql.Contains("WHERE", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public async Task SaveChangesAsync_preserves_cancellation(OceanBaseCompatibilityMode mode)
        {
            var commands = new CommandCapture();
            await using var context = CreateContext(mode, commands);
            context.Add(new Customer { Name = "Cancelled" });
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.SaveChangesAsync(cancellation.Token));
            Assert.Empty(commands.Sql);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Default_only_insert_uses_mode_specific_syntax(OceanBaseCompatibilityMode mode, bool async)
        {
            var commands = new CommandCapture();
            await using var context = CreateContext(mode, commands);
            var entity = new KeyOnly();
            context.Add(entity);

            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            Assert.Equal(42, entity.Id);
            Assert.Contains(commands.Sql, sql => sql.Contains(
                mode == OceanBaseCompatibilityMode.MySql ? "() VALUES ()" : "(\"Id\") VALUES (DEFAULT)", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task Unsupported_MySql_generated_keys_fail_before_writes(bool stringKey, bool async)
        {
            var commands = new CommandCapture();
            await using var context = CreateContext(OceanBaseCompatibilityMode.MySql, commands);
            if (stringKey)
            {
                context.Add(new StringDefaultKey());
            }
            else
            {
                context.Add(new NumericDefaultKey());
            }

            if (async)
            {
                await Assert.ThrowsAsync<NotSupportedException>(() => context.SaveChangesAsync());
            }
            else
            {
                Assert.Throws<NotSupportedException>(() => context.SaveChanges());
            }

            Assert.Empty(commands.Sql);
        }

        private static TestContext CreateContext(OceanBaseCompatibilityMode mode, CommandCapture commands, bool relationalNulls = false)
        {
            var builder = new DbContextOptionsBuilder<TestContext>()
                .UseOceanBase("Server=localhost;Database=test", mode)
                .AddInterceptors(new NoNetworkConnection(), commands);
            ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(
                builder.Options.Extensions.OfType<RelationalOptionsExtension>().Single().WithUseRelationalNulls(relationalNulls));
            var context = new TestContext(builder.Options, mode);
            // These tests exercise EF commands; transaction semantics are verified separately against OceanBase.
            context.Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
            return context;
        }

        private sealed class TestContext(DbContextOptions<TestContext> options, OceanBaseCompatibilityMode mode) : DbContext(options)
        {
            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                var oracle = mode == OceanBaseCompatibilityMode.Oracle;
                modelBuilder.Entity<TextMembershipRow>().Property(row => row.Text).HasMaxLength(8);
                modelBuilder.Entity<TextMembershipRow>().Property(row => row.Suffix)
                    .HasConversion(value => value + "!", value => value);
                modelBuilder.Entity<TextMembershipRow>().Property(row => row.EmptyConverted)
                    .HasConversion(value => value == "empty" ? "" : value, value => value);
                modelBuilder.Entity<TextMembershipRow>().Property(row => row.Preserved).HasConversion(value => value, value => value);
                modelBuilder.Entity<TextMembershipRow>().Property(row => row.Fixed).HasColumnType("CHAR(8)");
                modelBuilder.Entity<TextMembershipRow>().Property(row => row.National).HasColumnType(oracle ? "NVARCHAR2(8)" : "VARCHAR(8)");
                modelBuilder.Entity<TextMembershipRow>().Property(row => row.Lob).HasColumnType(oracle ? "CLOB" : "TEXT");
                modelBuilder.Entity<TextMembershipRow>().Property(row => row.NationalLob).HasColumnType(oracle ? "NCLOB" : "LONGTEXT");
                modelBuilder.Entity<TextMembershipRow>().Property(row => row.Number).HasConversion<string>();
                modelBuilder.Entity<TextMembershipRow>().Property(row => row.State).HasConversion<string>();
                modelBuilder.Entity<TextMembershipRow>().Property(row => row.Binary)
                    .HasConversion(value => Convert.FromHexString(value), value => Convert.ToHexString(value));
                modelBuilder.Entity<CountGroupRow>().Property(row => row.Converted)
                    .HasConversion(value => value * 2, value => value / 2);
                modelBuilder.Entity<ArithmeticRow>().Property(row => row.Converted).HasConversion<long>();
                modelBuilder.Entity<MathRow>().Property(row => row.Converted).HasConversion<long>();
                modelBuilder.Entity<MathRow>().Property(row => row.Transformed)
                    .HasConversion(value => value * -2m, value => value / -2m).HasPrecision(5, 2);
                modelBuilder.Entity<MathRow>().Property(row => row.Decimal).HasPrecision(5, 2);
                modelBuilder.Entity<Customer>().ToTable("Customers");
                modelBuilder.Entity<Customer>().Property(customer => customer.Version).IsConcurrencyToken();
                modelBuilder.Entity<KeyOnly>().ToTable("Keys");
                modelBuilder.Entity<StringDefaultKey>().Property(entity => entity.Id).HasDefaultValueSql("UUID()");
                modelBuilder.Entity<NumericDefaultKey>().Property(entity => entity.Id).HasDefaultValueSql("123");
                modelBuilder.Entity<ScalarRow>().Property(entity => entity.Id).ValueGeneratedNever();
                modelBuilder.Entity<ScalarRow>().Property(entity => entity.State).HasConversion<int>();
                modelBuilder.Entity<GeneratedFlag>().Property(entity => entity.Id).ValueGeneratedNever();
                modelBuilder.Entity<GeneratedFlag>().Property(entity => entity.Enabled).HasComputedColumnSql("1");
                modelBuilder.Entity<GeneratedState>().Property(entity => entity.Id).ValueGeneratedNever();
                modelBuilder.Entity<GeneratedState>().Property(entity => entity.State).HasConversion<int>().HasComputedColumnSql("2");
                modelBuilder.Entity<TemporalRow>().Property(entity => entity.Id).ValueGeneratedNever();
                modelBuilder.Entity<GeneratedStamp>().Property(entity => entity.Id).ValueGeneratedNever();
                modelBuilder.Entity<GeneratedStamp>().Property(entity => entity.Stamp).HasComputedColumnSql("CURRENT_TIMESTAMP");
                modelBuilder.Entity<GeneratedZoned>().Property(entity => entity.Id).ValueGeneratedNever();
                modelBuilder.Entity<GeneratedZoned>().Property(entity => entity.Stamp).HasComputedColumnSql("CURRENT_TIMESTAMP");
                modelBuilder.Entity<BinaryRow>().Property(entity => entity.Id).ValueGeneratedNever();
                modelBuilder.Entity<BinaryRow>().Property(entity => entity.Payload).HasMaxLength(8);
                modelBuilder.Entity<BinaryRow>().Property(entity => entity.Hex).HasMaxLength(8)
                    .HasConversion(value => Convert.FromHexString(value), value => Convert.ToHexString(value));
                modelBuilder.Entity<GeneratedBinary>().Property(entity => entity.Id).ValueGeneratedNever();
                modelBuilder.Entity<GeneratedBinary>().Property(entity => entity.Data).HasMaxLength(8).HasComputedColumnSql("NULL");
                modelBuilder.Entity<GeneratedGuid>().Property(entity => entity.Id).ValueGeneratedNever();
                modelBuilder.Entity<GeneratedGuid>().Property(entity => entity.Data).HasComputedColumnSql("NULL");
                modelBuilder.Entity<NarrowScalarRow>().Property(entity => entity.Id).ValueGeneratedNever();
                modelBuilder.Entity<NarrowScalarRow>().Property(entity => entity.Count).HasColumnType(oracle ? "NUMBER(3)" : "MEDIUMINT");
                modelBuilder.Entity<NarrowScalarRow>().Property(entity => entity.State).HasConversion<int>()
                    .HasColumnType(oracle ? "NUMBER(2)" : "TINYINT");
                modelBuilder.Entity<NarrowScalarRow>().Property(entity => entity.Enabled).HasColumnType(oracle ? "NUMBER(3)" : "BOOLEAN");
                modelBuilder.Entity<GuidStorageRow>().Property(entity => entity.Id).ValueGeneratedNever();
                modelBuilder.Entity<GuidStorageRow>().Property(entity => entity.Text).HasColumnType(oracle ? "VARCHAR2(36 CHAR)" : "VARCHAR(36)");
                modelBuilder.Entity<GuidStorageRow>().Property(entity => entity.Binary).HasColumnType(oracle ? "RAW(16)" : "BINARY(16)");
                modelBuilder.Entity<GuidStorageRow>().Property(entity => entity.Optional).HasColumnType(oracle ? "NVARCHAR2(36)" : "VARBINARY(16)");
                modelBuilder.Entity<GeneratedExplicitGuid>().Property(entity => entity.Id).ValueGeneratedNever();
                modelBuilder.Entity<GeneratedExplicitGuid>().Property(entity => entity.Data)
                    .HasColumnType(oracle ? "VARCHAR2(36)" : "BINARY(16)").HasComputedColumnSql("NULL");
                modelBuilder.Entity<FloatingRow>().Property(entity => entity.Id).ValueGeneratedNever();
                modelBuilder.Entity<FloatingRow>().Property(entity => entity.Narrow).HasColumnType(oracle ? "BINARY_FLOAT" : "FLOAT(24) UNSIGNED");
                modelBuilder.Entity<FloatingRow>().Property(entity => entity.Wide).HasColumnType(oracle ? "BINARY_DOUBLE" : "DOUBLE PRECISION");
                modelBuilder.Entity<FloatingRow>().Property(entity => entity.Converted)
                    .HasConversion(value => value * 2d, value => value / 2d).HasColumnType(oracle ? "BINARY_FLOAT" : "FLOAT");
                modelBuilder.Entity<GeneratedFloating>().Property(entity => entity.Id).ValueGeneratedNever();
                modelBuilder.Entity<GeneratedFloating>().Property(entity => entity.Value)
                    .HasColumnType(oracle ? "BINARY_DOUBLE" : "DOUBLE").HasComputedColumnSql("NULL");
                modelBuilder.Entity<GeneratedFloating>().Property(entity => entity.Wide).HasComputedColumnSql("NULL");
                modelBuilder.Entity<GeneratedFloating>().Property(entity => entity.Converted)
                    .HasConversion(value => value * 2d, value => value / 2d).HasComputedColumnSql("NULL");
                modelBuilder.Entity<NarrowTemporalRow>().Property(entity => entity.Id).ValueGeneratedNever();
                modelBuilder.Entity<NarrowTemporalRow>().Property(entity => entity.Stamp).HasPrecision(3);
                modelBuilder.Entity<NarrowTemporalRow>().Property(entity => entity.Zoned).HasPrecision(3);
                modelBuilder.Entity<NarrowTemporalRow>().Property(entity => entity.Clock).HasPrecision(3);
                modelBuilder.Entity<NarrowTemporalRow>().Property(entity => entity.Duration)
                    .HasColumnType(oracle ? "INTERVAL DAY(1) TO SECOND(3)" : "TIME(3)");
                modelBuilder.Entity<NarrowTemporalRow>().Property(entity => entity.Date).HasColumnType(oracle ? "DATE" : "DATETIME(0)");
                modelBuilder.Entity<DecimalRow>().Property(entity => entity.Id).ValueGeneratedNever();
                modelBuilder.Entity<DecimalRow>().Property(entity => entity.Value).HasColumnType(oracle ? "NUMBER(5,2)" : "DECIMAL(5,2)");
                modelBuilder.Entity<DecimalRow>().Property(entity => entity.Converted)
                    .HasConversion(value => value * 2m, value => value / 2m).HasPrecision(5, 2);
                modelBuilder.Entity<DecimalRow>().Property(entity => entity.Optional).HasPrecision(5, 2);
                modelBuilder.Entity<FixedPointAliasRow>().Property(entity => entity.Id).ValueGeneratedNever();
                modelBuilder.Entity<FixedPointAliasRow>().Property(entity => entity.Decimal).HasColumnType("DECIMAL");
                modelBuilder.Entity<FixedPointAliasRow>().Property(entity => entity.Numeric).HasColumnType("NUMERIC");
                modelBuilder.Entity<FixedPointAliasRow>().Property(entity => entity.Converted)
                    .HasConversion(value => value * 2m, value => value / 2m).HasColumnType("DECIMAL");
                modelBuilder.Entity<FixedPointAliasRow>().Property(entity => entity.Unqualified)
                    .HasColumnType(oracle ? "NUMBER" : "DECIMAL(65,30)");
                modelBuilder.Entity<TextParameterRow>().Property(entity => entity.Id).ValueGeneratedNever();
                modelBuilder.Entity<TextParameterRow>().Property(entity => entity.Caption).HasMaxLength(4);
                modelBuilder.Entity<TextParameterRow>().Property(entity => entity.Fixed).IsFixedLength().HasMaxLength(4);
                modelBuilder.Entity<TextParameterRow>().Property(entity => entity.State).HasConversion<string>().HasMaxLength(16);
                modelBuilder.Entity<TextParameterRow>().Property(entity => entity.Token).HasColumnType(oracle ? "NVARCHAR2(36)" : "VARCHAR(36)");
                modelBuilder.Entity<ShortTextRow>().HasKey(entity => entity.Code);
                modelBuilder.Entity<ShortTextRow>().Property(entity => entity.Code).ValueGeneratedNever().HasMaxLength(2);
                modelBuilder.Entity<ShortTextRow>().Property(entity => entity.Caption).HasMaxLength(2);
                modelBuilder.Entity<ShortTextRow>().Property(entity => entity.Converted)
                    .HasConversion(value => value + "!", value => value.Substring(0, value.Length - 1)).HasMaxLength(4);
                modelBuilder.Entity<SubstringNationalRow>().Property(entity => entity.Value)
                    .IsUnicode().IsFixedLength().HasMaxLength(8);
            }
        }

        private enum RowState { Inactive = 0, Active = 2 }

        private sealed class CountGroupRow
        {
            public int Id { get; set; }
            public int? Converted { get; set; }
        }

        private sealed class ArithmeticRow
        {
            public int Id { get; set; }
            public int Value { get; set; }
            public int? Optional { get; set; }
            public int Converted { get; set; }
            public decimal Fraction { get; set; }
        }

        private sealed class MathRow
        {
            public int Id { get; set; }
            public long Int64 { get; set; }
            public int? Optional { get; set; }
            public int Converted { get; set; }
            public decimal Transformed { get; set; }
            public decimal Decimal { get; set; }
            public float Single { get; set; }
            public double Double { get; set; }
        }

        private sealed class ShortTextRow
        {
            public string Code { get; set; } = string.Empty;
            public string? Caption { get; set; }
            public int Start { get; set; }
            public int Length { get; set; }
            public string Converted { get; set; } = string.Empty;
        }

        private sealed class SubstringNationalRow
        {
            public int Id { get; set; }
            public string Value { get; set; } = "";
        }

        private sealed class TextParameterRow
        {
            public int Id { get; set; }
            public string Caption { get; set; } = string.Empty;
            public string Fixed { get; set; } = string.Empty;
            public RowState State { get; set; }
            public Guid Token { get; set; }
        }

        private sealed class DecimalRow
        {
            public int Id { get; set; }
            public decimal Value { get; set; }
            public decimal Converted { get; set; }
            public decimal? Optional { get; set; }
        }

        private sealed class FixedPointAliasRow
        {
            public int Id { get; set; }
            public decimal Decimal { get; set; }
            public decimal Numeric { get; set; }
            public decimal Converted { get; set; }
            public decimal Unqualified { get; set; }
        }

        private sealed class NarrowTemporalRow
        {
            public int Id { get; set; }
            public DateTime Stamp { get; set; }
            public DateTime Date { get; set; }
            public DateTimeOffset Zoned { get; set; }
            public TimeOnly Clock { get; set; }
            public TimeSpan Duration { get; set; }
        }

        private sealed class FloatingRow
        {
            public int Id { get; set; }
            public double Narrow { get; set; }
            public float Wide { get; set; }
            public double Converted { get; set; }
            public double? Optional { get; set; }
        }

        private sealed class GeneratedFloating
        {
            public int Id { get; set; }
            public float Value { get; set; }
            public double Wide { get; set; }
            public double Converted { get; set; }
        }

        private sealed class GuidStorageRow
        {
            public int Id { get; set; }
            public Guid Text { get; set; }
            public Guid Binary { get; set; }
            public Guid? Optional { get; set; }
        }

        private sealed class GeneratedExplicitGuid
        {
            public int Id { get; set; }
            public Guid Data { get; set; }
        }

        private sealed class NarrowScalarRow
        {
            public int Id { get; set; }
            public long Count { get; set; }
            public RowState State { get; set; }
            public bool Enabled { get; set; }
        }

        private sealed class BinaryRow
        {
            public int Id { get; set; }
            public byte[] Payload { get; set; } = [];
            public byte[]? Optional { get; set; }
            public string Hex { get; set; } = string.Empty;
        }

        private sealed class GeneratedBinary
        {
            public int Id { get; set; }
            public byte[] Data { get; set; } = [];
        }

        private sealed class GeneratedGuid
        {
            public int Id { get; set; }
            public Guid Data { get; set; }
        }

        private sealed class TemporalRow
        {
            public int Id { get; set; }
            public DateOnly Date { get; set; }
            public DateTime Stamp { get; set; }
            public DateTimeOffset Zoned { get; set; }
            public TimeOnly Clock { get; set; }
            public TimeSpan Duration { get; set; }
            public DateOnly? OptionalDate { get; set; }
        }

        private sealed class GeneratedStamp
        {
            public int Id { get; set; }
            public DateTime Stamp { get; set; }
        }

        private sealed class GeneratedZoned
        {
            public int Id { get; set; }
            public DateTimeOffset Stamp { get; set; }
        }

        private sealed class GeneratedFlag
        {
            public int Id { get; set; }
            public bool Enabled { get; set; }
        }

        private sealed class GeneratedState
        {
            public int Id { get; set; }
            public RowState State { get; set; }
        }

        private sealed class ScalarRow
        {
            public int Id { get; set; }
            public uint Count { get; set; }
            public bool Enabled { get; set; }
            public int? OptionalCount { get; set; }
            public bool? OptionalFlag { get; set; }
            public RowState State { get; set; }
            public Guid Token { get; set; }
        }

        private sealed class Customer
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public int Version { get; set; }
        }

        private sealed class KeyOnly
        {
            public int Id { get; set; }
        }

        private sealed class StringDefaultKey
        {
            public string Id { get; set; } = null!;
        }

        private sealed class NumericDefaultKey
        {
            public int Id { get; set; }
        }

        private sealed class NoNetworkConnection : DbConnectionInterceptor
        {
            public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
                => InterceptionResult.Suppress();

            public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
                DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(InterceptionResult.Suppress());
        }

        private sealed class TextMembershipRow
        {
            public int Id { get; set; }
            public string? Text { get; set; }
            public string? Suffix { get; set; }
            public string? EmptyConverted { get; set; }
            public string? Preserved { get; set; }
            public string? Fixed { get; set; }
            public string? National { get; set; }
            public string? Lob { get; set; }
            public string? NationalLob { get; set; }
            public int Number { get; set; }
            public RowState State { get; set; }
            public string Binary { get; set; } = "";
        }

        private sealed class CommandCapture : DbCommandInterceptor
        {
            public List<string> Sql { get; } = [];
            public List<object?> Values { get; } = [];
            public List<OceanBaseParameter> Inputs { get; } = [];
            public List<OceanBaseParameter> DecimalInputs { get; } = [];
            public List<OceanBaseParameter> StringInputs { get; } = [];
            public int AffectedRows { get; init; } = 1;
            public DataTable? Results { get; init; }
            public object OutputValue { get; init; } = 42m;

            public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
            {
                Sql.Add(command.CommandText);
                DecimalInputs.AddRange(command.Parameters.Cast<OceanBaseParameter>()
                    .Where(parameter => parameter.DbType == DbType.Decimal && parameter.Direction == ParameterDirection.Input));
                StringInputs.AddRange(command.Parameters.Cast<OceanBaseParameter>()
                    .Where(parameter => parameter.Value is string && parameter.Direction == ParameterDirection.Input));
                foreach (DbParameter parameter in command.Parameters)
                {
                    if (parameter.Direction == ParameterDirection.Output)
                    {
                        // Oracle numeric outputs may be returned as decimal by the connector.
                        parameter.Value = OutputValue;
                    }
                    else
                    {
                        Values.Add(parameter.Value);
                    }
                }

                return InterceptionResult<int>.SuppressWithResult(AffectedRows);
            }

            public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
                DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(NonQueryExecuting(command, eventData, result));

            public override InterceptionResult<DbDataReader> ReaderExecuting(
                DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
            {
                Sql.Add(command.CommandText);
                Inputs.AddRange(command.Parameters.Cast<OceanBaseParameter>()
                    .Where(parameter => parameter.Direction == ParameterDirection.Input));
                Values.AddRange(command.Parameters.Cast<DbParameter>().Select(parameter => parameter.Value));
                DecimalInputs.AddRange(command.Parameters.Cast<OceanBaseParameter>()
                    .Where(parameter => parameter.DbType == DbType.Decimal && parameter.Direction == ParameterDirection.Input));
                StringInputs.AddRange(command.Parameters.Cast<OceanBaseParameter>()
                    .Where(parameter => parameter.Value is string && parameter.Direction == ParameterDirection.Input));
                if (Results is not null)
                {
                    return InterceptionResult<DbDataReader>.SuppressWithResult(Results.CreateDataReader());
                }

                var table = new DataTable();
                table.Columns.Add("Id", typeof(int));
                table.Rows.Add(42);
                return InterceptionResult<DbDataReader>.SuppressWithResult(table.CreateDataReader());
            }

            public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
                DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(ReaderExecuting(command, eventData, result));
        }
    }
}
