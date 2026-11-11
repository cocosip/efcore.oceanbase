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
                new RelationalParameterBasedSqlProcessorParameters(false, ParameterTranslationMode.MultipleParameters),
                new ParametersCacheDecorator(dictionary), source, mode);
            var error = Assert.Throws<NotSupportedException>(() => visitor.Visit(
                new ValuesExpression("v", parameter, new[] { "Value" })));
            Assert.Contains("numeric or text", error.Message);
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
        public void Mapping_owned_literals_modes_and_converted_carriers_keep_their_boundaries(
            OceanBaseCompatibilityMode mode, bool relationalNulls)
        {
            using var context = CreateContext(mode, relationalNulls);
            string?[] values = ["", "O'Reilly", "海\\\n'_%--"];
            var literal = context.Rows.Where(row => EF.Constant(values).Contains(row.Suffix)).ToQueryString();
            Assert.Contains("O''Reilly!", literal);
            Assert.Contains(mode == OceanBaseCompatibilityMode.Oracle ? "N'!'" : "'!'", literal);
            Assert.DoesNotContain(mode == OceanBaseCompatibilityMode.MySql ? "@values" : ":values", literal);
            if (mode == OceanBaseCompatibilityMode.MySql) Assert.Contains("CONVERT(X'", literal);
            using var constants = CreateContext(mode, relationalNulls, ParameterTranslationMode.Constant);
            Assert.Contains("O''Reilly!", constants.Rows.Where(row => values.Contains(row.Suffix)).ToQueryString());
            using var parameters = CreateContext(mode, relationalNulls, ParameterTranslationMode.Parameter);
            Assert.Contains(" IN (", parameters.Rows.Where(row => values.Contains(row.Suffix)).ToQueryString());
            Assert.Throws<InvalidOperationException>(() => context.Rows.Where(row => EF.Parameter(values).Contains(row.Text)).ToQueryString());
            Assert.Throws<InvalidOperationException>(() => context.Rows.Where(row => values.Contains(row.Text, StringComparer.Ordinal)).ToQueryString());
            Assert.Throws<InvalidOperationException>(() => context.Rows.Where(row => EF.Parameter(values).AsQueryable()
                .Where(value => value != null).Contains(row.Text)).ToQueryString());
            int[] numbers = [0, 17, 23];
            State[] states = [State.Empty, State.Present];
            Assert.Contains(" IN (", context.Rows.Where(row => numbers.Contains(row.Number)).ToQueryString());
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
            var binarySql = context.Rows.Where(row => binary.Contains(row.Binary)).ToQueryString();
            Assert.Contains(" IN (", binarySql);
            Assert.DoesNotContain("IS NULL", binarySql);
            binary = ["", "0A"];
            if (mode == OceanBaseCompatibilityMode.Oracle)
                Assert.Contains("empty Oracle binary inputs", Assert.Throws<NotSupportedException>(() => context.Rows
                    .Where(row => binary.Contains(row.Binary)).ToQueryString()).Message);
            else Assert.Contains(" IN (", context.Rows.Where(row => binary.Contains(row.Binary)).ToQueryString());
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
                Assert.Equal(!string.IsNullOrEmpty(item), sql.Contains(":item", StringComparison.Ordinal));
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

        [Theory]
        [InlineData(ParameterTranslationMode.MultipleParameters)]
        [InlineData(ParameterTranslationMode.Constant)]
        [InlineData(ParameterTranslationMode.Parameter)]
        public void Oracle_expansion_snapshots_once_avoids_name_collisions_and_keeps_mapping_identity(ParameterTranslationMode mode)
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.Oracle);
            var factory = context.GetService<ISqlExpressionFactory>();
            var source = context.GetService<IRelationalTypeMappingSource>();
            var raw = new CountingEnumerable(["", "empty", null, "x", "y", "last"]);
            var dictionary = new Dictionary<string, object?> { ["values"] = raw, ["values_text0"] = "existing" };
            var decorator = new ParametersCacheDecorator(dictionary);
            var visitor = new OceanBaseCollectionParameterExpandingVisitor(factory,
                new RelationalParameterBasedSqlProcessorParameters(false, mode), decorator, source, OceanBaseCompatibilityMode.Oracle);
            var results = new List<InExpression>();
            foreach (var property in new[] { nameof(Row.Suffix), nameof(Row.EmptyConverted) })
            {
                var mapping = (RelationalTypeMapping)context.Model.FindEntityType(typeof(Row))!.FindProperty(property)!.GetTypeMapping();
                var parameter = new SqlParameterExpression("values", typeof(IEnumerable<string?>),
                    source.FindMapping(typeof(IEnumerable<string?>), context.Model, mapping));
                Assert.Same(parameter, new OceanBaseOracleStringVisitor(factory, decorator).Visit(parameter));
                var membership = factory.In(new ColumnExpression(property, "r", typeof(string), mapping, true), parameter);
                var result = Assert.IsType<InExpression>(visitor.Visit(membership));
                results.Add(result);
                Assert.Same(result.Values![0], Assert.IsType<InExpression>(visitor.Visit(membership)).Values![0]);
                Assert.Equal(mode == ParameterTranslationMode.MultipleParameters ? 10 : 6, result.Values.Count);
                Assert.All(result.Values, scalar => Assert.Same(mapping, scalar.TypeMapping));
                if (mode != ParameterTranslationMode.Constant)
                {
                    Assert.All(result.Values, scalar =>
                    {
                        var scalarParameter = Assert.IsType<SqlParameterExpression>(scalar);
                        Assert.Equal(scalarParameter.Name, scalarParameter.InvariantName);
                        Assert.Equal(typeof(string), scalarParameter.Type);
                        Assert.True(scalarParameter.IsNullable);
                    });
                }
            }
            Assert.False(decorator.CanCache);
            Assert.Equal(1, raw.Enumerations);
            Assert.Same(raw, dictionary["values"]);
            Assert.Equal("existing", dictionary["values_text0"]);
            Assert.NotSame(results[0].Values![0], results[1].Values![0]);
            if (mode != ParameterTranslationMode.Constant)
            {
                Assert.All(results, result => Assert.Equal("", dictionary[Assert.IsType<SqlParameterExpression>(result.Values![0]).Name]));
                Assert.Equal(mode == ParameterTranslationMode.MultipleParameters ? 22 : 14, dictionary.Count);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Oracle_processor_keeps_nullable_model_carriers_and_effective_modes_per_invocation(bool relationalNulls)
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.Oracle, relationalNulls);
            var factory = context.GetService<ISqlExpressionFactory>();
            var source = context.GetService<IRelationalTypeMappingSource>();
            var mapping = (RelationalTypeMapping)context.Model.FindEntityType(typeof(Row))!.FindProperty(nameof(Row.EmptyState))!.GetTypeMapping();
            var collectionMapping = source.FindMapping(typeof(State?[]), context.Model, mapping);
            State?[] values = [State.Empty, State.Present, null];
            var dictionary = new Dictionary<string, object?> { ["values"] = values };
            var decorator = new ParametersCacheDecorator(dictionary);
            var parameters = new RelationalParameterBasedSqlProcessorParameters(relationalNulls, ParameterTranslationMode.MultipleParameters);
            var visitor = new OceanBaseCollectionParameterExpandingVisitor(factory, parameters, decorator, source, OceanBaseCompatibilityMode.Oracle);
            var item = new ColumnExpression("EmptyState", "r", typeof(State), mapping, true);
            var parameter = new SqlParameterExpression("values", typeof(State?[]), collectionMapping);
            var membership = factory.In(item, parameter);
            var expanded = Assert.IsType<InExpression>(visitor.Visit(membership));
            Assert.All(expanded.Values!, scalar =>
            {
                var value = Assert.IsType<SqlParameterExpression>(scalar);
                Assert.Equal(typeof(State), value.Type);
                Assert.True(value.IsNullable);
                Assert.Same(mapping, value.TypeMapping);
            });
            var explicitConstant = new SqlParameterExpression("values", "values", typeof(State?[]), true,
                ParameterTranslationMode.Constant, collectionMapping);
            var constants = Assert.IsType<InExpression>(visitor.Visit(factory.In(item, explicitConstant)));
            Assert.All(constants.Values!, value => Assert.IsType<SqlConstantExpression>(value));
            Assert.Equal(4, dictionary.Count);
            var processor = context.GetService<IRelationalParameterBasedSqlProcessorFactory>().Create(parameters);
            var freshDictionary = new Dictionary<string, object?> { ["values"] = values };
            var freshDecorator = new ParametersCacheDecorator(freshDictionary);
            _ = processor.Process(membership, freshDecorator);
            Assert.False(freshDecorator.CanCache);
            Assert.Same(values, freshDictionary["values"]);
            Assert.Equal(4, freshDictionary.Count);
            Assert.Same(values, dictionary["values"]);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Oracle_padding_copies_raw_trailing_values_before_normalization(bool trailingNull)
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.Oracle);
            var factory = context.GetService<ISqlExpressionFactory>();
            var source = context.GetService<IRelationalTypeMappingSource>();
            var mapping = (RelationalTypeMapping)context.Model.FindEntityType(typeof(Row))!.FindProperty(nameof(Row.EmptyConverted))!.GetTypeMapping();
            foreach (var size in new[] { 5, 6, 10, 11, 150, 151, 750, 751, 2000, 2001 })
            {
                var values = Enumerable.Range(0, size).Select(index => (string?)index.ToString()).ToArray();
                values[1] = "empty";
                values[^1] = trailingNull ? null : "empty";
                var snapshot = values.ToArray();
                var dictionary = new Dictionary<string, object?> { ["values"] = values };
                var decorator = new ParametersCacheDecorator(dictionary);
                var parameter = new SqlParameterExpression("values", typeof(string[]), source.FindMapping(typeof(string[]), context.Model, mapping));
                var membership = factory.In(new ColumnExpression("EmptyConverted", "r", typeof(string), mapping, true), parameter);
                var visitor = new OceanBaseCollectionParameterExpandingVisitor(factory,
                    new RelationalParameterBasedSqlProcessorParameters(false, ParameterTranslationMode.MultipleParameters), decorator, source, OceanBaseCompatibilityMode.Oracle);
                var expanded = Assert.IsType<InExpression>(visitor.Visit(membership));
                var bucket = size <= 5 ? 1 : size <= 150 ? 10 : size <= 750 ? 50 : size <= 2000 ? 100 : 200;
                var padded = size + (bucket - size % bucket) % bucket;
                Assert.Equal(padded, expanded.Values!.Count);
                Assert.Equal(padded, expanded.Values.Cast<SqlParameterExpression>().Select(value => value.Name).Distinct().Count());
                for (var index = 0; index < padded; index++)
                    Assert.Equal(values[Math.Min(index, size - 1)], dictionary[((SqlParameterExpression)expanded.Values[index]).Name]);
                var normalized = Assert.IsType<InExpression>(new OceanBaseOracleStringVisitor(factory, decorator).Visit(expanded));
                Assert.Equal(padded - size + 2, normalized.Values!.Count(value => value is SqlConstantExpression { Value: null }));
                Assert.Equal(snapshot, values);
                Assert.Same(values, dictionary["values"]);
            }
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

        private static TestContext CreateContext(OceanBaseCompatibilityMode mode, bool relationalNulls = false,
            ParameterTranslationMode? collectionMode = null)
        {
            var builder = new DbContextOptionsBuilder<TestContext>().UseOceanBase("Server=localhost;Database=test", mode);
            var extension = builder.Options.Extensions.OfType<RelationalOptionsExtension>().Single()
                .WithUseRelationalNulls(relationalNulls);
            if (collectionMode.HasValue) extension = extension.WithUseParameterizedCollectionMode(collectionMode.Value);
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
