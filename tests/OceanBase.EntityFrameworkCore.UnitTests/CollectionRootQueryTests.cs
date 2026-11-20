using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBase.EntityFrameworkCore.Query;
using Xunit;

#pragma warning disable EF1001 // EF query expression internals.

namespace OceanBase.EntityFrameworkCore.UnitTests
{
    public sealed class CollectionRootQueryTests
    {
        public static IEnumerable<object[]> Modes()
        {
            foreach (var mode in new[] { OceanBaseCompatibilityMode.MySql, OceanBaseCompatibilityMode.Oracle })
            foreach (var relationalNulls in new[] { false, true })
                yield return new object[] { mode, relationalNulls };
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Captured_integral_general_roots_bind_current_values(OceanBaseCompatibilityMode mode, bool relationalNulls)
        {
            using var context = CreateContext(mode, relationalNulls);
            int?[] values = [1, null, 1, 3, 5, 7];
            var query = context.Rows.Where(row => values.Where(value => value > row.Id).Contains(row.Optional));
            var sql = query.ToQueryString();
            Assert.Contains("JSON_TABLE(", sql);
            Assert.DoesNotContain("UNION ALL VALUES", sql);
            foreach (var capture in new int?[][] { [], [1], [null, null], Enumerable.Range(0, 11).Select(value => (int?)value).ToArray() })
            {
                values = capture;
                using var command = query.CreateDbCommand();
                Assert.Contains("JSON_TABLE(", command.CommandText);
                Assert.Contains(command.Parameters.Cast<System.Data.Common.DbParameter>(),
                    parameter => Equals(parameter.Value, System.Text.Json.JsonSerializer.Serialize(capture)));
            }
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public void Captured_arrays_lists_and_composed_shapes_use_json(
            OceanBaseCompatibilityMode mode, bool relationalNulls)
        {
            using var context = CreateContext(mode, relationalNulls);
            int?[] values = [];
            List<int?> list = [];
            int[] integers = [];
            List<int> integerList = [];
            IQueryable[] Queries() =>
            [
                context.Rows.Where(row => values.Where(value => value > row.Id).Contains(row.Optional)),
                context.Rows.Where(row => !list.Where(value => value != row.Optional).Contains(row.Optional)),
                context.Rows.Select(row => new { Match = values.Where(value => value > row.Id).Contains(row.Optional),
                    Not = !list.Where(value => value > row.Id).Contains(row.Optional) }),
                context.Rows.Select(row => values.Count(value => value > row.Id)),
                context.Rows.Select(row => values.Any(value => value > row.Id)),
                context.Rows.SelectMany(row => values.Where(value => value > row.Id).Select(value => value + row.Id)),
                context.Rows.Join(values, row => row.Optional, value => value, (row, value) => value),
                context.Rows.Where(row => integers.Where(value => value > row.Id).Contains(row.Id)),
                context.Rows.Join(integerList, row => row.Id, value => value, (row, value) => value),
                context.Rows.Select(row => integers.ElementAt(row.Id))
            ];
            foreach (var capture in new int?[][] { [], [1], [1, 1, null], [null, null],
                [1, 2, 3, 4, 5, 6], [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11], [] })
            {
                values = capture;
                list = capture.ToList();
                integers = capture.Select(value => value ?? 0).ToArray();
                integerList = integers.ToList();
                foreach (var query in Queries())
                {
                    var sql = query.ToQueryString();
                    Assert.DoesNotContain("UNION ALL VALUES", sql);
                    Assert.DoesNotContain("AS INT)", sql);
                    Assert.DoesNotContain("AS NUMBER(3))", sql);
                    Assert.Contains("JSON_TABLE(", sql);
                    Assert.DoesNotContain("UNION ALL SELECT", sql);
                }
                Assert.Equal(capture, list);
            }
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public void Mapped_constant_and_nonempty_inline_roots_keep_lossless_carrier_casts(
            OceanBaseCompatibilityMode mode, bool relationalNulls)
        {
            using var context = CreateContext(mode, relationalNulls);
            var queries = new IQueryable[]
            {
                context.Rows.Where(row => new[] { int.MinValue, int.MaxValue, 17, 17 }
                    .Where(value => value > row.Converted).Contains(row.Converted)),
                context.Rows.Where(row => new int?[] { null, 1, 1 }.Where(value => value > row.Id).Contains(row.Optional))
            };
            foreach (var query in queries)
            {
                var sql = query.ToQueryString();
                Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? " AS SIGNED)" : " AS NUMBER)", sql);
                Assert.DoesNotContain("AS MEDIUMINT)", sql);
                Assert.DoesNotContain("AS NUMBER(3))", sql);
                Assert.Contains("UNION ALL SELECT", sql);
                if (query == queries[0])
                {
                    Assert.Contains("-2147483648", sql);
                    Assert.Contains("2147483647", sql);
                    Assert.DoesNotContain("_value", sql);
                }
            }
            var ordered = context.Rows.Select(row => new[] { 7, 3, 7 }.ElementAt(row.Id)).ToQueryString();
            Assert.Contains("ORDER BY", ordered);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "`_ord`" : "\"_ord\"", ordered);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Standard_signed_integer_roots_use_a_single_json_parameter(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            int[] values = [1, 2];
            Assert.Contains("JSON_TABLE(", context.Rows.Where(row => values
                .Where(value => value > row.Id).Contains(row.Id)).ToQueryString());
            var join = context.Rows.Join(values, row => row.Converted, value => value, (row, value) => value).ToQueryString();
            Assert.DoesNotContain("JSON_TABLE(", join);
            Assert.Contains("JOIN", join);
            Assert.Contains("UNION ALL", join);
            values = null!;
            // A null source evaluated by ordinary LINQ fails before it can become a translated parameter root.
            var error = Assert.Throws<InvalidOperationException>(() => context.Rows.Where(row => values.AsQueryable()
                .Where(value => value > row.Id).Contains(row.Id)).ToQueryString());
            Assert.IsType<ArgumentNullException>(error.InnerException);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Single_json_collection_parameters_reject_contextual_converters_in_composed_queries(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            int[] values = [3, 20];
            Assert.Throws<InvalidOperationException>(() => context.Rows
                .Where(row => values.Any(value => value == row.Converted && value > 0)).ToQueryString());
            Assert.Throws<InvalidOperationException>(() => context.Rows
                .Where(row => values.Any(value => value == row.Converted || value == row.Id)).ToQueryString());
            Assert.Throws<InvalidOperationException>(() => context.Rows
                .Where(row => values.Any(value => value == row.Id || value == row.Converted)).ToQueryString());
            Assert.Throws<NotSupportedException>(() => context.Rows
                .Where(row => values.Any(value => value + row.Id == row.Converted)).ToQueryString());
            Assert.Throws<InvalidOperationException>(() => context.Rows
                .Where(row => values.AsQueryable().Take(3).Any(value => value == row.Converted && value > row.Id)).ToQueryString());
            Assert.Throws<NotSupportedException>(() => context.Rows
                .Where(row => values.Where(value => value > row.Id)
                    .Select(value => value + row.Id).Contains(row.Converted)).ToQueryString());

            Assert.Contains("JSON_TABLE(", context.Rows
                .Where(row => row.Converted > 0 && values.Contains(row.Id)).ToQueryString());
            Assert.Contains("JSON_TABLE(", context.Rows
                .Where(row => values.Contains(row.Converted == 7 ? row.Id : row.Id + 1)).ToQueryString());
            Assert.Contains("JSON_TABLE(", context.Rows
                .Where(row => values.Any(value => value == row.Id) == (row.Converted == 7)).ToQueryString());
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public void Originally_empty_inline_integral_roots_keep_zero_rows_in_subqueries_and_joins(
            OceanBaseCompatibilityMode mode, bool relationalNulls)
        {
            using var context = CreateContext(mode, relationalNulls);
            foreach (var query in new IQueryable[]
            {
                context.Rows.Select(row => new int[] { }.Count(value => value > row.Id)),
                context.Rows.Select(row => new int[] { }.Any(value => value > row.Id)),
                context.Rows.SelectMany(row => new int[] { }.Where(value => value > row.Id)),
                context.Rows.SelectMany(row => new int[] { }.Where(value => value > row.Id)
                    .Join(context.Rows, value => value, item => item.Id, (value, item) => value)),
                context.Rows.Select(row => new int?[] { }.Count(value => row.Id > 0)),
                context.Rows.Select(row => new int?[] { }.Any(value => value > row.Optional)),
                context.Rows.SelectMany(row => new int?[] { }.Where(value => value != row.Optional).Select(value => value)),
                context.Rows.Where(row => context.Rows.Any(other => new int[] { }.Any(value => value > other.Id + row.Id))),
                context.Rows.Select(row => new int[] { }.ElementAt(row.Id)),
                context.Rows.Select(row => new int[] { }.Select(value => row.Id + 1).ElementAt(row.Id)),
                context.Rows.SelectMany(row => new int[] { }.Select(value => row.Id)),
                context.Rows.SelectMany(row => new int?[] { }.Where(value => value > row.Id).DefaultIfEmpty()),
                context.Rows.Select(row => new int[] { }.Where(value => value > row.Converted).Count()),
                context.Rows.Select(row => new { First = new int[] { }.Count(value => value > row.Id),
                    Second = new int?[] { }.Any(value => value > row.Optional) })
            })
            {
                string sql;
                try { sql = query.ToQueryString(); }
                catch (Exception error) { throw new InvalidOperationException(query.Expression.ToString(), error); }
                Assert.True(sql.Contains("WHERE 1 = 0", StringComparison.Ordinal), query.Expression + "\n" + sql);
                Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "CAST(NULL AS SIGNED)" : "CAST(NULL AS NUMBER)", sql);
                Assert.DoesNotContain("UNION ALL VALUES", sql);
                if (mode == OceanBaseCompatibilityMode.Oracle) Assert.Contains("FROM DUAL", sql);
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Originally_empty_inline_count_does_not_guess_nonnumeric_element_types_from_its_int_result(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var text = context.Rows.Select(row => new string[] { }.Count(value => row.Id > 0)).ToQueryString();
            Assert.Contains("WHERE 1 = 0", text);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "NULL AS `Value`" : "TO_NCHAR(NULL)", text);
            var temporal = context.Rows.Select(row => new DateTime[] { }.Count(value => row.Id > 0)).ToQueryString();
            Assert.Contains("WHERE 1 = 0", temporal);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "CAST(NULL AS DATETIME" : "CAST(NULL AS TIMESTAMP", temporal);
            Assert.Contains("WHERE 1 = 0", context.Rows.Join(new int[] { }, row => row.Converted,
                value => value, (row, value) => value).ToQueryString());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Originally_empty_inline_metadata_keeps_original_types_and_contextual_mapping(
            OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var integer = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(int));
            var converted = context.Model.FindEntityType(typeof(Row))!.FindProperty(nameof(Row.Converted))!.GetTypeMapping();
            var cases = new (IQueryable Query, Type Type, CoreTypeMapping Mapping)[]
            {
                (context.Rows.Select(row => new int?[] { }.Count(value => row.Id > 0)), typeof(int?), integer!),
                (context.Rows.Select(row => new int[] { }.ElementAt(row.Id)), typeof(int), integer!),
                (context.Rows.Select(row => new int[] { }.Select(value => row.Id + 1).ElementAt(row.Id)), typeof(int), integer!),
                (context.Rows.Select(row => new int[] { }.Count(value => value > row.Converted)), typeof(int), converted)
            };
            foreach (var item in cases)
            {
                var compilation = context.GetService<IQueryCompilationContextFactory>().Create(false);
                var preprocessed = context.GetService<IQueryTranslationPreprocessorFactory>().Create(compilation).Process(item.Query.Expression);
                var translated = context.GetService<IQueryableMethodTranslatingExpressionVisitorFactory>().Create(compilation).Translate(preprocessed);
                var postprocessed = context.GetService<IQueryTranslationPostprocessorFactory>().Create(compilation).Process(translated);
                var after = new EmptyCollectionCollector();
                after.Visit(postprocessed);
                var remaining = Assert.Single(after.Values);
                var prototypes = remaining.Arguments;
                Assert.Same(item.Type, remaining.FindAnnotation(OceanBaseQueryableMethodTranslatingExpressionVisitorFactory.EmptyElementTypeAnnotation)!.Value);
                Assert.Collection(prototypes,
                    ordinal => Assert.Same(integer, ordinal.TypeMapping),
                    value =>
                    {
                        Assert.Same(typeof(int), value.Type);
                        Assert.Same(item.Mapping, value.TypeMapping);
                    });
                Assert.Contains("WHERE 1 = 0", item.Query.ToQueryString());
            }
        }

        private sealed class EmptyCollectionCollector : ExpressionVisitor
        {
            public List<TableValuedFunctionExpression> Values { get; } = [];
            protected override Expression VisitExtension(Expression node)
            {
                if (node is TableValuedFunctionExpression values
                    && values.FindAnnotation(OceanBaseQueryableMethodTranslatingExpressionVisitorFactory.EmptyElementTypeAnnotation) is not null)
                    Values.Add(values);
                return node is ShapedQueryExpression shaped ? Visit(shaped.QueryExpression) : base.VisitExtension(node);
            }
        }

        [Fact]
        public void Inline_empty_services_are_scoped_and_modes_do_not_share_compilation_metadata()
        {
            using var first = CreateContext(OceanBaseCompatibilityMode.MySql);
            using var second = CreateContext(OceanBaseCompatibilityMode.MySql);
            using var oracle = CreateContext(OceanBaseCompatibilityMode.Oracle);
            Assert.IsType<OceanBaseQueryableMethodTranslatingExpressionVisitorFactory>(first.GetService<IQueryableMethodTranslatingExpressionVisitorFactory>());
            Assert.Same(first.GetService<IQueryableMethodTranslatingExpressionVisitorFactory>(), first.GetService<IQueryableMethodTranslatingExpressionVisitorFactory>());
            Assert.NotSame(first.GetService<IQueryableMethodTranslatingExpressionVisitorFactory>(), second.GetService<IQueryableMethodTranslatingExpressionVisitorFactory>());
            Assert.NotSame(first.GetService<IQueryTranslationPostprocessorFactory>(), second.GetService<IQueryTranslationPostprocessorFactory>());
            foreach (var context in new[] { first, oracle, second, oracle })
            {
                var query = context.Rows.Select(row => new
                {
                    Count = new int[] { }.Count(value => value > row.Converted),
                    Any = new int?[] { }.Any(value => value > row.Id)
                });
                var compilation = context.GetService<IQueryCompilationContextFactory>().Create(false);
                var normalized = context.GetService<IQueryTranslationPreprocessorFactory>().Create(compilation).Process(query.Expression);
                var translated = context.GetService<IQueryableMethodTranslatingExpressionVisitorFactory>().Create(compilation).Translate(normalized);
                var processed = context.GetService<IQueryTranslationPostprocessorFactory>().Create(compilation).Process(translated);
                var collector = new EmptyCollectionCollector();
                collector.Visit(processed);
                Assert.Equal(2, collector.Values.Count);
                Assert.NotSame(collector.Values[0], collector.Values[1]);
                Assert.Equal(new[] { typeof(int), typeof(int?) }, collector.Values.Select(value =>
                    value.FindAnnotation(OceanBaseQueryableMethodTranslatingExpressionVisitorFactory.EmptyElementTypeAnnotation)!.Value));
                var mappings = collector.Values.Select(value => value.Arguments[1].TypeMapping).ToArray();
                Assert.Same(context.Model.FindEntityType(typeof(Row))!.FindProperty(nameof(Row.Converted))!.GetTypeMapping(), mappings[0]);
                Assert.Same(context.Model.FindEntityType(typeof(Row))!.FindProperty(nameof(Row.Id))!.GetTypeMapping(), mappings[1]);
                var sql = query.ToQueryString();
                Assert.Contains(context == oracle ? "CAST(NULL AS NUMBER)" : "CAST(NULL AS SIGNED)", sql);
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Runtime_roots_snapshot_once_and_preserve_mapping_indices(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var factory = context.GetService<ISqlExpressionFactory>();
            var source = context.GetService<IRelationalTypeMappingSource>();
            var raw = new CountingEnumerable([17, null, 17, 3, 5, 7]);
            var dictionary = new Dictionary<string, object?> { ["values"] = raw, ["values_value0"] = 99 };
            var canCache = true;
            var visitor = new OceanBaseCollectionParameterExpandingVisitor(factory,
                dictionary, () => canCache = false, source, mode);
            var results = new List<ValuesExpression>();
            foreach (var property in new[] { nameof(Row.Optional), nameof(Row.Converted) })
            {
                var mapping = (RelationalTypeMapping)context.Model.FindEntityType(typeof(Row))!.FindProperty(property)!.GetTypeMapping();
                var parameter = new SqlParameterExpression("values", typeof(IEnumerable<int?>),
                    source.FindMapping(typeof(IEnumerable<int?>), context.Model, mapping));
                var root = ParameterRoot(parameter, new[] { "_ord", "Value" });
                var result = Assert.IsType<ValuesExpression>(visitor.Visit(root));
                results.Add(result);
                Assert.Equal(6, result.RowValues!.Count);
                Assert.Equal(result.RowValues, Assert.IsType<ValuesExpression>(visitor.Visit(root)).RowValues);
                for (var index = 0; index < result.RowValues.Count; index++)
                {
                    var cells = result.RowValues[index].Values;
                    Assert.Equal(index, Assert.IsType<SqlConstantExpression>(cells[0]).Value);
                    Assert.Same(source.FindMapping(typeof(int)), cells[0].TypeMapping);
                    var scalar = index == 0 ? Assert.IsType<SqlUnaryExpression>(cells[1]).Operand : cells[1];
                    Assert.Same(mapping, scalar.TypeMapping);
                    Assert.Equal(raw.Values[index], Assert.IsType<SqlConstantExpression>(scalar).Value);
                }
            }
            Assert.NotSame(results[0].RowValues![0].Values[1], results[1].RowValues![0].Values[1]);
            Assert.Equal(1, raw.Enumerations);
            Assert.Same(raw, dictionary["values"]);
            Assert.Equal(99, dictionary["values_value0"]);
            Assert.False(canCache);
            Assert.Equal(2, dictionary.Count);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Runtime_null_roots_keep_immutable_typed_remaining_columns(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var factory = context.GetService<ISqlExpressionFactory>();
            var source = context.GetService<IRelationalTypeMappingSource>();
            var mapping = (RelationalTypeMapping)context.Model.FindEntityType(typeof(Row))!.FindProperty(nameof(Row.Converted))!.GetTypeMapping();
            foreach (var columns in new[] { new[] { "Value" }, new[] { "_ord", "Value" } })
            {
                var dictionary = new Dictionary<string, object?> { ["values"] = null };
                var canCache = true;
                var root = ParameterRoot(new SqlParameterExpression("values", typeof(int[]),
                    source.FindMapping(typeof(int[]), context.Model, mapping)), columns);
                var visitor = new OceanBaseCollectionParameterExpandingVisitor(factory,
                    dictionary, () => canCache = false, source, mode);
                var expanded = Assert.IsType<TableValuedFunctionExpression>(visitor.Visit(root));
                Assert.Equal(columns, expanded.FindAnnotation(OceanBaseCollectionParameterExpandingVisitor.EmptyColumnsAnnotation)!.Value);
                var prototypes = expanded.Arguments;
                Assert.Equal(columns.Length, prototypes.Count);
                Assert.Same(mapping, prototypes[^1].TypeMapping);
                Assert.Throws<NotSupportedException>(() => ((IList<SqlExpression>)prototypes)[0] = factory.Constant(7));
                var sql = new OceanBaseQuerySqlGenerator(context.GetService<QuerySqlGeneratorDependencies>(), mode).GetCommand(expanded).CommandText;
                Assert.Contains("WHERE 1 = 0", sql);
                Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "CAST(NULL AS SIGNED)" : "CAST(NULL AS NUMBER)", sql);
                Assert.Equal(mode == OceanBaseCompatibilityMode.Oracle, sql.Contains("FROM DUAL", StringComparison.Ordinal));
                Assert.False(canCache);
                Assert.Null(dictionary["values"]);
                Assert.Single(dictionary);
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Expansion_rejects_unmapped_unsupported_and_unknown_layouts_and_preserves_mysql_text_membership(
            OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var factory = context.GetService<ISqlExpressionFactory>();
            var source = context.GetService<IRelationalTypeMappingSource>();
            var dictionary = new Dictionary<string, object?> { ["values"] = new[] { 1, 2 }, ["texts"] = new[] { "one", "two" } };
            var visitor = new OceanBaseCollectionParameterExpandingVisitor(factory,
                dictionary, () => { }, source, mode);
            var mapping = source.FindMapping(typeof(int))!;
            var parameter = new SqlParameterExpression("values", typeof(int[]), source.FindMapping(typeof(int[]), context.Model, mapping));
            Assert.Contains("column layout", Assert.Throws<NotSupportedException>(() => visitor.Visit(
                ParameterRoot(parameter, new[] { "Unknown" }))).Message);
            Assert.Contains("numeric", Assert.Throws<NotSupportedException>(() => visitor.Visit(
                ParameterRoot(new SqlParameterExpression("values", typeof(int[]), null), new[] { "Value" }))).Message);
            var textMapping = source.FindMapping(typeof(string))!;
            var texts = new SqlParameterExpression("texts", typeof(string[]), source.FindMapping(typeof(string[]), context.Model, textMapping));
            var textRoot = Assert.IsType<ValuesExpression>(visitor.Visit(ParameterRoot(texts, new[] { "Value" })));
            Assert.Equal(2, textRoot.RowValues!.Count);
            Assert.All(textRoot.RowValues, row => Assert.Same(textMapping, row.Values[0].TypeMapping));
            var binaryMapping = source.FindMapping(typeof(byte[]))!;
            var binary = new SqlParameterExpression("binary", typeof(byte[]),
                source.FindMapping(typeof(byte[]), context.Model, binaryMapping));
            Assert.Contains("numeric, Boolean, text, binary or temporal", Assert.Throws<NotSupportedException>(() => visitor.Visit(
                ParameterRoot(binary, new[] { "Value" }))).Message);
            var membership = factory.In(new TestColumnExpression("Text", "r", typeof(string), textMapping, true), texts);
            if (mode == OceanBaseCompatibilityMode.MySql) Assert.Same(membership, visitor.Visit(membership));
            else Assert.Null(Assert.IsType<InExpression>(visitor.Visit(membership)).ValuesParameter);
            dictionary["values"] = 1;
            Assert.Contains("enumerable", Assert.Throws<InvalidOperationException>(() => visitor.Visit(
                ParameterRoot(parameter, new[] { "Value" }))).Message);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Renderer_preserves_explicit_numeric_conversions_outside_synthetic_collection_typing(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var factory = context.GetService<ISqlExpressionFactory>();
            var source = context.GetService<IRelationalTypeMappingSource>();
            var integer = source.FindMapping(typeof(int))!;
            var wide = source.FindMapping(typeof(long))!;
            var converted = new SqlUnaryExpression(ExpressionType.Convert, factory.Constant(17, integer), typeof(long), wide);
            var root = new ValuesExpression("v", new[] { new RowValueExpression(new[] { converted }) }, new[] { "Value" });
            var sql = new OceanBaseQuerySqlGenerator(context.GetService<QuerySqlGeneratorDependencies>(), mode).GetCommand(root).CommandText;
            var wideTarget = mode == OceanBaseCompatibilityMode.MySql ? "SIGNED" : wide.StoreType;
            Assert.Contains("CAST(17 AS " + wideTarget + ")", sql);

            var original = factory.Constant(17L, wide);
            var explicitConversion = new SqlUnaryExpression(ExpressionType.Convert, original, typeof(int), integer);
            var synthetic = new SqlUnaryExpression(ExpressionType.Convert, explicitConversion, typeof(int), integer);
            var nestedRoot = new ValuesExpression("v", new[] { new RowValueExpression(new[] { synthetic }) }, new[] { "Value" });
            var nestedSql = new OceanBaseQuerySqlGenerator(context.GetService<QuerySqlGeneratorDependencies>(), mode)
                .GetCommand(nestedRoot).CommandText;
            var target = mode == OceanBaseCompatibilityMode.MySql ? "SIGNED" : "NUMBER";
            var integerTarget = mode == OceanBaseCompatibilityMode.MySql ? "SIGNED" : integer.StoreType;
            Assert.Contains("CAST(CAST(17 AS " + integerTarget + ") AS " + target + ")", nestedSql);
            Assert.Same(synthetic, nestedRoot.RowValues![0].Values[0]);
            Assert.Same(explicitConversion, synthetic.Operand);
            Assert.Same(original, explicitConversion.Operand);
            Assert.Same(integer, synthetic.TypeMapping);
            Assert.Same(integer, explicitConversion.TypeMapping);
            Assert.Same(wide, original.TypeMapping);
        }

        private sealed class CountingEnumerable(int?[] values) : IEnumerable<int?>
        {
            public int?[] Values => values;
            public int Enumerations { get; private set; }
            public IEnumerator<int?> GetEnumerator()
            {
                Enumerations++;
                return ((IEnumerable<int?>)values).GetEnumerator();
            }
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private static TableExpressionBase ParameterRoot(SqlParameterExpression parameter, string[] columns)
            => new TableValuedFunctionExpression("v", "OceanBaseCollection", [parameter])
                .AddAnnotation(OceanBaseQueryableMethodTranslatingExpressionVisitorFactory.ParameterCollectionAnnotation, columns);

        private static TestContext CreateContext(OceanBaseCompatibilityMode mode, bool relationalNulls = false)
        {
            var builder = new DbContextOptionsBuilder<TestContext>().UseOceanBase("Server=localhost;Database=test", mode);
            var extension = builder.Options.Extensions.OfType<RelationalOptionsExtension>().Single().WithUseRelationalNulls(relationalNulls);
            ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(extension);
            return new TestContext(builder.Options, mode);
        }

        private sealed class TestContext(DbContextOptions<TestContext> options, OceanBaseCompatibilityMode mode) : DbContext(options)
        {
            public DbSet<Row> Rows => Set<Row>();
            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity<Row>().Property(row => row.Converted).HasConversion<long>()
                    .HasColumnType(mode == OceanBaseCompatibilityMode.Oracle ? "NUMBER(3)" : "MEDIUMINT");
            }
        }

        private sealed class Row
        {
            public int Id { get; set; }
            public int? Optional { get; set; }
            public int Converted { get; set; }
        }
    }
}
