using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OceanBase.EntityFrameworkCore.Query;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests
{
    public sealed class TextCollectionQueryTests
    {
        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Captured_text_general_roots_expand_to_typed_select_unions(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            string?[] values = ["one", null, "two"];
            var query = context.Rows.SelectMany(row => values.Where(value => value != row.Text));
            var sql = query.ToQueryString();
            Assert.Contains("UNION ALL SELECT", sql);
            Assert.DoesNotContain("numeric", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("JSON", sql, StringComparison.OrdinalIgnoreCase);

            values = [];
            Assert.Contains("WHERE 1 = 0", query.ToQueryString());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void General_text_roots_reject_null_converting_mappings(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var factory = context.GetService<ISqlExpressionFactory>();
            var source = context.GetService<IRelationalTypeMappingSource>();
            var mapping = (RelationalTypeMapping)context.Model.FindEntityType(typeof(Row))!
                .FindProperty(nameof(Row.NullConverted))!.GetTypeMapping();
            var parameter = new SqlParameterExpression("values", typeof(string[]),
                source.FindMapping(typeof(string[]), context.Model, mapping));
            var dictionary = new Dictionary<string, object?> { ["values"] = new[] { "value" } };
            var visitor = new OceanBaseCollectionParameterExpandingVisitor(factory,
                dictionary, () => { }, source, mode);
            var error = Assert.Throws<NotSupportedException>(() => visitor.Visit(
                new TableValuedFunctionExpression("v", "OceanBaseCollection", [parameter])
                    .AddAnnotation(OceanBaseQueryableMethodTranslatingExpressionVisitorFactory.ParameterCollectionAnnotation, new[] { "Value" })));
            Assert.Contains("numeric, Boolean, text, binary or temporal", error.Message);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Captured_text_membership_has_element_mapping(OceanBaseCompatibilityMode mode, bool relationalNulls)
        {
            using var context = CreateContext(mode, relationalNulls);
            string?[] values = ["one", "two"];
            Assert.Contains(" IN (", context.Rows.Where(row => values.Contains(row.Text)).ToQueryString());
            List<string?> list = ["one", "two"];
            Assert.Contains(" IN (", context.Rows.Where(row => list.Contains(row.Text)).ToQueryString());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Captured_text_roots_translate_and_rebind(
            OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            string?[] values = ["one", null, "two"];
            var query = context.Rows.Where(row => values.Contains(row.Text));

            foreach (var input in new string?[][] { values, ["one"], [] })
            {
                values = input;
                var sql = query.ToQueryString();
                Assert.DoesNotContain("IN ()", sql);
                Assert.Equal(input.Length == 0, sql.Contains("WHERE 1 = 0", StringComparison.Ordinal)
                    || sql.Contains("WHERE 0", StringComparison.Ordinal));
            }

            List<string?> list = ["one", null];
            Assert.DoesNotContain("IN ()", context.Rows
                .Where(row => list.Contains(row.Text)).ToQueryString());
            string?[] converted = ["one", null];
            Assert.DoesNotContain("IN ()", context.Rows
                .Where(row => converted.Contains(row.Suffix)).ToQueryString());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Oracle_converted_empty_collection_values_reach_EF_null_processing(bool relationalNulls)
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.Oracle, relationalNulls);
            string?[] values = ["empty", ""];
            var sql = context.Rows.Where(row => values.Contains(row.EmptyConverted)).ToQueryString();
            Assert.DoesNotContain(":__values", sql);
            Assert.Contains(relationalNulls ? "IN (NULL, NULL)" : "\"EmptyConverted\" IS NULL", sql);
            if (!relationalNulls) Assert.DoesNotContain(" IN (", sql);
            var projection = context.Rows.Select(row => new { Match = values.Contains(row.EmptyConverted),
                Not = !values.Contains(row.EmptyConverted), Choice = values.Contains(row.EmptyConverted) ? 7 : 9 }).ToQueryString();
            Assert.Contains(relationalNulls ? "IN (NULL, NULL)" : "\"EmptyConverted\" IS NULL", projection);
            Assert.Contains("CASE", projection);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Membership_reprocesses_captures_nulls_negation_projection_and_case(
            OceanBaseCompatibilityMode mode, bool relationalNulls)
        {
            using var context = CreateContext(mode, relationalNulls);
            string?[] values = [];
            var query = context.Rows.Select(row => new { Match = values.Contains(row.Text),
                Not = !values.Contains(row.Text), Choice = values.Contains(row.Text) ? 7 : 9 });
            var empty = query.ToQueryString();
            Assert.DoesNotContain(" IN (", empty);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql
                ? "SELECT 0 AS `Match`, 1 AS `Not`, 9 AS `Choice`"
                : "SELECT 0 \"Match\", 1 \"Not\", 9 \"Choice\"", empty);
            foreach (var capture in new string?[][] { ["a", "b"], [null, null], [null, ""],
                ["a", null, "b", "c", "d", "e"], [], null! })
            {
                values = capture;
                var sql = query.ToQueryString();
                Assert.DoesNotContain("IN ()", sql);
                if (capture is null || capture.Length == 0) Assert.Equal(empty, sql);
                else
                {
                    Assert.Equal(!relationalNulls, sql.Contains("IS NULL", StringComparison.Ordinal));
                    if (mode == OceanBaseCompatibilityMode.Oracle && capture.All(value => string.IsNullOrEmpty(value)))
                        Assert.DoesNotContain(":values", sql);
                    Assert.DoesNotContain("IN ()", context.Rows.Where(row => !values.Contains(row.Text)).ToQueryString());
                    Assert.DoesNotContain("IN ()", context.Rows.Select(row => (bool?)values.Contains(row.Text)).ToQueryString());
                }
            }
            var inline = context.Rows.Where(row => new string?[] { "one", null, "" }.Contains(row.Text)).ToQueryString();
            Assert.DoesNotContain("VALUES", inline);
            Assert.DoesNotContain("IN ()", inline);
            Assert.Equal(mode == OceanBaseCompatibilityMode.MySql, inline.Contains("''", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Mapping_owned_literals_and_converted_carriers_keep_their_boundaries(
            OceanBaseCompatibilityMode mode, bool relationalNulls)
        {
            using var context = CreateContext(mode, relationalNulls);
            string?[] values = ["", "O'Reilly", "海\\\n'_%--"];
            var literal = context.Rows.Where(row => new[] { "", "O'Reilly", "海\\\n'_%--" }.Contains(row.Suffix)).ToQueryString();
            Assert.Contains("O''Reilly!", literal);
            Assert.Contains(mode == OceanBaseCompatibilityMode.Oracle ? "N'!'" : "'!'", literal);
            Assert.DoesNotContain(mode == OceanBaseCompatibilityMode.MySql ? "@values" : ":values", literal);
            if (mode == OceanBaseCompatibilityMode.MySql) Assert.Contains("_utf8mb4 X'", literal);
            Assert.Throws<InvalidOperationException>(() => context.Rows.Where(row => values.Contains(row.Text, StringComparer.Ordinal)).ToQueryString());
            int[] numbers = [0, 17, 23];
            State[] states = [State.Empty, State.Present];
            var numberSql = context.Rows.Where(row => numbers.Contains(row.Number)).ToQueryString();
            Assert.Contains(" IN (", numberSql);
            Assert.DoesNotContain("JSON_TABLE(", numberSql);
            Assert.Contains(" IN (", context.Rows.Where(row => states.Contains(row.State)).ToQueryString());
            State?[] optionalStates = [State.Empty, State.Present];
            var enumEmpty = context.Rows.Where(row => optionalStates.Contains(row.EmptyState)).ToQueryString();
            Assert.Equal(mode == OceanBaseCompatibilityMode.Oracle && !relationalNulls,
                enumEmpty.Contains("IS NULL", StringComparison.Ordinal));
            var inlineEnum = context.Rows.Where(row => new State?[] { State.Empty, State.Present }.Contains(row.EmptyState)).ToQueryString();
            Assert.DoesNotContain("VALUES", inlineEnum);
            var inlineSuffix = context.Rows.Where(row => new[] { "", "x" }.Contains(row.Suffix)).ToQueryString();
            Assert.Contains(mode == OceanBaseCompatibilityMode.Oracle ? "N'!'" : "'!'", inlineSuffix);
            var inlineEmpty = context.Rows.Where(row => new[] { "", "empty" }.Contains(row.EmptyConverted)).ToQueryString();
            if (mode == OceanBaseCompatibilityMode.Oracle)
                Assert.Contains(relationalNulls ? "IN (NULL, NULL)" : "\"EmptyConverted\" IS NULL", inlineEmpty);
            string[] binary = ["0A", "0B"];
            if (mode == OceanBaseCompatibilityMode.Oracle)
            {
                Assert.Contains("BLOB", Assert.Throws<NotSupportedException>(() => context.Rows.Where(row => binary.Contains(row.Binary)).ToQueryString()).Message);
                Assert.Contains("BLOB", Assert.Throws<NotSupportedException>(() => context.Rows
                    .Where(row => new[] { "", "0A" }.Contains(row.Binary)).ToQueryString()).Message);
            }
            else
            {
                var binarySql = context.Rows.Where(row => binary.Contains(row.Binary)).ToQueryString();
                Assert.Contains(" IN (", binarySql);
                Assert.DoesNotContain("IS NULL", binarySql);
                binary = ["", "0A"];
                Assert.Contains(" IN (", context.Rows.Where(row => binary.Contains(row.Binary)).ToQueryString());
            }
            Assert.Contains(" IN (", context.Rows.Where(row => values.Contains(row.Preserved)).ToQueryString());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Oracle_normalizes_captured_item_and_rejects_null_converting_collection_elements(bool relationalNulls)
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.Oracle, relationalNulls);
            string?[] values = [null, "", "present"];
            string? item = "item";
            var query = context.Rows.Where(row => values.Contains(row.Text ?? item));
            Assert.Contains("COALESCE(", query.ToQueryString());
            foreach (var candidate in new string?[] { "", null, "item-again" })
            {
                item = candidate;
                var sql = query.ToQueryString();
                if (!relationalNulls) Assert.Equal(!string.IsNullOrEmpty(item), sql.Contains("COALESCE(", StringComparison.Ordinal));
                Assert.Equal(!string.IsNullOrEmpty(item), sql.Contains(":__item", StringComparison.Ordinal));
                Assert.DoesNotContain("N''", sql);
            }
            var error = Assert.Throws<InvalidOperationException>(() => context.Rows.Where(row => values.Contains(row.NullConverted)).ToQueryString());
            Assert.Contains("null-converting string value converters", error.Message);
            error = Assert.Throws<InvalidOperationException>(() => context.Rows.Where(row => new[] { "", "x" }.Contains(row.NullConverted)).ToQueryString());
            Assert.Contains("null-converting string value converters", error.Message);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Text_collection_metadata_clones_retain_facets_converters_and_binding_hooks(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var source = context.GetService<IRelationalTypeMappingSource>();
            foreach (var declaration in mode == OceanBaseCompatibilityMode.Oracle
                ? new[] { "CHAR(4)", "VARCHAR2(8 CHAR)", "NCHAR(4)", "NVARCHAR2(8)", "CLOB", "NCLOB" }
                : new[] { "CHAR(4)", "VARCHAR(8)", "TINYTEXT", "TEXT", "MEDIUMTEXT", "LONGTEXT" })
            {
                var mapping = source.FindMapping(typeof(string), declaration)!;
                Assert.IsType<JsonStringReaderWriter>(mapping.JsonValueReaderWriter);
                var converted = (RelationalTypeMapping)mapping.WithComposedConverter(
                    new ValueConverter<string, string>(value => value + "!", value => value));
                var clone = converted.Clone(comparer: converted.Comparer, keyComparer: converted.KeyComparer,
                    providerValueComparer: converted.ProviderValueComparer);
                Assert.Same(converted.JsonValueReaderWriter, clone.JsonValueReaderWriter);
                Assert.Same(converted.Converter, clone.Converter);
                Assert.Same(converted.Comparer, clone.Comparer);
                Assert.Same(converted.KeyComparer, clone.KeyComparer);
                Assert.Same(converted.ProviderValueComparer, clone.ProviderValueComparer);
                Assert.Equal(mapping.StoreType, clone.StoreType);
                Assert.Equal(mapping.Size, clone.Size);
                Assert.Equal(mapping.IsFixedLength, clone.IsFixedLength);
                Assert.Equal(mapping.IsUnicode, clone.IsUnicode);
                Assert.Equal(mapping.GetDataReaderMethod(), clone.GetDataReaderMethod());
                using var command = new OceanBaseCommand();
                var parameter = clone.CreateParameter(command, "value", "海");
                Assert.Equal("海!", parameter.Value);
                Assert.Equal(mapping.CreateParameter(command, "original", "海!").DbType, parameter.DbType);
                Assert.True(parameter.Size >= 2);
                if (declaration.StartsWith("NCHAR", StringComparison.Ordinal)) Assert.Equal(OceanBaseDbType.NChar, ((OceanBaseParameter)parameter).OceanBaseDbType);
                if (declaration.StartsWith("NVARCHAR2", StringComparison.Ordinal)) Assert.Equal(OceanBaseDbType.NVarchar2, ((OceanBaseParameter)parameter).OceanBaseDbType);
                Assert.NotNull(source.FindMapping(typeof(string[]), context.Model, clone));
            }
        }

        [Fact]
        public void Oracle_expansion_snapshots_once_and_keeps_contextual_mappings()
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.Oracle);
            var factory = context.GetService<ISqlExpressionFactory>();
            var source = context.GetService<IRelationalTypeMappingSource>();
            string?[] values = ["", "empty", null, "x", "y", "last"];
            var raw = new CountingEnumerable(values);
            var dictionary = new Dictionary<string, object?> { ["values"] = raw };
            var canCache = true;
            var visitor = new OceanBaseCollectionParameterExpandingVisitor(factory, dictionary,
                () => canCache = false, source, OceanBaseCompatibilityMode.Oracle);
            foreach (var property in new[] { nameof(Row.Suffix), nameof(Row.EmptyConverted) })
            {
                var mapping = (RelationalTypeMapping)context.Model.FindEntityType(typeof(Row))!.FindProperty(property)!.GetTypeMapping();
                var parameter = new SqlParameterExpression("values", typeof(IEnumerable<string?>),
                    source.FindMapping(typeof(IEnumerable<string?>), context.Model, mapping));
                var membership = factory.In(new TestColumnExpression(property, "r", typeof(string), mapping, true), parameter);
                var result = Assert.IsType<InExpression>(visitor.Visit(membership));
                Assert.All(result.Values!, scalar => Assert.Same(mapping, scalar.TypeMapping));
                Assert.Equal(values.Cast<object?>(), result.Values!.Select(value => Assert.IsType<SqlConstantExpression>(value).Value));
            }
            Assert.False(canCache);
            Assert.Equal(1, raw.Enumerations);
            Assert.Same(raw, dictionary["values"]);
            Assert.Single(dictionary);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Oracle_processor_disables_caching_for_expanded_text_membership(bool relationalNulls)
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.Oracle, relationalNulls);
            var factory = context.GetService<ISqlExpressionFactory>();
            var source = context.GetService<IRelationalTypeMappingSource>();
            var mapping = (RelationalTypeMapping)context.Model.FindEntityType(typeof(Row))!.FindProperty(nameof(Row.EmptyState))!.GetTypeMapping();
            var collectionMapping = source.FindMapping(typeof(State?[]), context.Model, mapping);
            var item = new TestColumnExpression("EmptyState", "r", typeof(State), mapping, true);
            var parameter = new SqlParameterExpression("values", typeof(State?[]), collectionMapping);
            State?[] values = [State.Empty, State.Present, null];
            var dictionary = new Dictionary<string, object?> { ["values"] = values };
            var processor = context.GetService<IRelationalParameterBasedSqlProcessorFactory>().Create(relationalNulls);
            var select = factory.Select(factory.In(item, parameter));
            select.ApplyProjection();
            _ = processor.Optimize(select, dictionary, out var canCache);
            Assert.False(canCache);
            Assert.Same(values, dictionary["values"]);
            Assert.Single(dictionary);
        }

        private sealed class CountingEnumerable(string?[] values) : IEnumerable<string?>
        {
            public int Enumerations { get; private set; }
            public IEnumerator<string?> GetEnumerator()
            {
                Enumerations++;
                return ((IEnumerable<string?>)values).GetEnumerator();
            }
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private static TestContext CreateContext(OceanBaseCompatibilityMode mode, bool relationalNulls = false)
        {
            var builder = new DbContextOptionsBuilder<TestContext>().UseOceanBase("Server=localhost;Database=test", mode);
            var extension = builder.Options.Extensions.OfType<RelationalOptionsExtension>().Single()
                .WithUseRelationalNulls(relationalNulls);
            ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(extension);
            return new TestContext(builder.Options);
        }

        private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
        {
            public DbSet<Row> Rows => Set<Row>();
            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity<Row>().Property(row => row.Suffix)
                    .HasConversion(value => value + "!", value => value);
                modelBuilder.Entity<Row>().Property(row => row.EmptyConverted)
                    .HasConversion(value => value == "empty" ? "" : value, value => value);
                modelBuilder.Entity<Row>().Property(row => row.Preserved)
                    .HasConversion(value => value, value => value);
                modelBuilder.Entity<Row>().Property(row => row.Number).HasConversion<string>();
                modelBuilder.Entity<Row>().Property(row => row.State).HasConversion<string>();
                modelBuilder.Entity<Row>().Property(row => row.EmptyState)
                    .HasConversion(value => value == State.Empty ? "" : "present", value => State.Present);
                modelBuilder.Entity<Row>().Property(row => row.Binary)
                    .HasConversion(value => Convert.FromHexString(value), value => Convert.ToHexString(value));
                modelBuilder.Entity<Row>().Property(row => row.NullConverted).HasConversion(
                    new ValueConverter<string?, string?>(value => value ?? "sentinel", value => value, convertsNulls: true));
            }
        }

        private enum State { Empty, Present }
        private sealed class Row
        {
            public int Id { get; set; }
            public string? Text { get; set; }
            public string? Suffix { get; set; }
            public string? EmptyConverted { get; set; }
            public string? Preserved { get; set; }
            public int Number { get; set; }
            public State State { get; set; }
            public State? EmptyState { get; set; }
            public string Binary { get; set; } = "";
            public string? NullConverted { get; set; }
        }
    }
}
