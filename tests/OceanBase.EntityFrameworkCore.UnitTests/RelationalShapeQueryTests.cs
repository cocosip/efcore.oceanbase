using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Json;
using Microsoft.EntityFrameworkCore.Storage.Internal;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests
{
    public sealed class RelationalShapeQueryTests
    {
        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Captured_integer_arrays_and_lists_expand_to_scalar_membership(OceanBaseCompatibilityMode mode, bool relationalNulls)
        {
            using var context = CreateContext(mode, relationalNulls);
            int[] values = [2, 7, 11];
            int?[] optional = [2, null, 11];
            List<int> list = [2, 7, 11];
            List<int?> nullableList = [2, null, 11];
            IQueryable[] queries =
            [
                context.Rows.Where(row => Enumerable.Contains(values, row.Id)),
                context.Rows.Where(row => Enumerable.Contains(optional, row.Optional)),
                context.Rows.Where(row => list.Contains(row.Id)),
                context.Rows.Where(row => nullableList.Contains(row.Optional))
            ];
            foreach (var query in queries)
            {
                var sql = query.ToQueryString();
                Assert.Contains(" IN (", sql);
                Assert.True(sql.Contains(mode == OceanBaseCompatibilityMode.MySql ? "@" : ":", StringComparison.Ordinal), query.Expression + "\n" + sql);
                Assert.DoesNotContain("JSON", sql, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Integer_metadata_and_clones_preserve_exact_carriers_and_checked_hooks(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var source = context.GetService<IRelationalTypeMappingSource>();
            (Type Carrier, Type Writer)[] carriers =
            [
                (typeof(sbyte), typeof(JsonSByteReaderWriter)), (typeof(byte), typeof(JsonByteReaderWriter)),
                (typeof(short), typeof(JsonInt16ReaderWriter)), (typeof(ushort), typeof(JsonUInt16ReaderWriter)),
                (typeof(int), typeof(JsonInt32ReaderWriter)), (typeof(uint), typeof(JsonUInt32ReaderWriter)),
                (typeof(long), typeof(JsonInt64ReaderWriter)), (typeof(ulong), typeof(JsonUInt64ReaderWriter))
            ];
            foreach (var (carrier, writer) in carriers)
            {
                var mapping = source.FindMapping(carrier)!;
                Assert.IsType(writer, mapping.JsonValueReaderWriter);
                var clone = mapping.Clone(comparer: mapping.Comparer, keyComparer: mapping.KeyComparer, providerValueComparer: mapping.ProviderValueComparer);
                Assert.Same(mapping.JsonValueReaderWriter, clone.JsonValueReaderWriter);
                Assert.Same(mapping.Comparer, clone.Comparer);
                Assert.Same(mapping.KeyComparer, clone.KeyComparer);
                Assert.Same(mapping.ProviderValueComparer, clone.ProviderValueComparer);
                Assert.Same(mapping.Converter, clone.Converter);
                Assert.Equal(mapping.GetDataReaderMethod(), clone.GetDataReaderMethod());
                using var command = new OceanBaseCommand();
                var value = Convert.ChangeType(7, carrier);
                Assert.Equal(value, clone.CreateParameter(command, "value", value).Value);
                var read = clone.CustomizeDataReaderExpression(Expression.Constant(new OceanBaseDecimal(7m), typeof(object)));
                Assert.Equal(value, Expression.Lambda<Func<object>>(Expression.Convert(read, typeof(object))).Compile()());
            }
            var boolean = source.FindMapping(typeof(bool))!;
            Assert.Equal(typeof(bool), boolean.JsonValueReaderWriter!.ValueType);
            var booleanClone = boolean.Clone(comparer: boolean.Comparer);
            Assert.Same(boolean.JsonValueReaderWriter, booleanClone.JsonValueReaderWriter);
            Assert.Same(boolean.Converter, booleanClone.Converter);
            Assert.Same(boolean.Comparer, booleanClone.Comparer);
            using var booleanCommand = new OceanBaseCommand();
            Assert.Equal(mode == OceanBaseCompatibilityMode.Oracle ? 1m : (object)true,
                booleanClone.CreateParameter(booleanCommand, "value", true).Value);
            if (mode == OceanBaseCompatibilityMode.MySql) Assert.IsType<JsonBoolReaderWriter>(boolean.JsonValueReaderWriter);
            else
            {
                Assert.Equal(typeof(decimal), boolean.Converter!.ProviderClrType);
                Assert.IsType<JsonDecimalReaderWriter>(Assert.IsAssignableFrom<ICompositeJsonValueReaderWriter>(boolean.JsonValueReaderWriter).InnerReaderWriter);
            }
            foreach (var name in new[] { nameof(Row.Converted), nameof(Row.State) })
            {
                var mapping = (RelationalTypeMapping)source.FindMapping(context.Model.FindEntityType(typeof(Row))!.FindProperty(name)!)!;
                var clone = mapping.Clone(comparer: mapping.Comparer, keyComparer: mapping.KeyComparer, providerValueComparer: mapping.ProviderValueComparer);
                Assert.NotNull(mapping.Converter);
                Assert.Same(mapping.Converter, clone.Converter);
                Assert.Same(mapping.JsonValueReaderWriter, clone.JsonValueReaderWriter);
                Assert.Same(mapping.Comparer, clone.Comparer);
                Assert.Equal(name == nameof(Row.State) ? typeof(RowState) : typeof(int), mapping.JsonValueReaderWriter!.ValueType);
                var writer = Assert.IsAssignableFrom<ICompositeJsonValueReaderWriter>(mapping.JsonValueReaderWriter).InnerReaderWriter;
                Assert.IsType(name == nameof(Row.Converted) ? typeof(JsonInt64ReaderWriter) : typeof(JsonInt32ReaderWriter), writer);
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Membership_modes_and_inline_converted_values_keep_the_inherited_boundaries(OceanBaseCompatibilityMode mode, bool relationalNulls)
        {
            using var context = CreateContext(mode, relationalNulls);
            int[] values = [int.MinValue, 7, int.MaxValue];
            Assert.Contains(" IN (", context.Rows.Where(row => Enumerable.Contains(new[] { 2, 7, 11 }, row.Id)).ToQueryString());
            Assert.Contains(" IN (", context.Rows.Where(row => Enumerable.Contains(values, row.Converted)).ToQueryString());
            RowState[] states = [RowState.Inactive, RowState.Active];
            Assert.Contains(" IN (", context.Rows.Where(row => Enumerable.Contains(states, row.State)).ToQueryString());
            var constant = context.Rows.Where(row => Enumerable.Contains(EF.Constant(values), row.Id)).ToQueryString();
            Assert.Contains("-2147483648, 7, 2147483647", constant);
            Assert.DoesNotContain("@values", constant);
            Assert.DoesNotContain(":values", constant);
            using var constants = CreateContext(mode, relationalNulls, ParameterTranslationMode.Constant);
            Assert.Contains("-2147483648, 7, 2147483647", constants.Rows.Where(row => Enumerable.Contains(values, row.Id)).ToQueryString());
            using var parameters = CreateContext(mode, relationalNulls, ParameterTranslationMode.Parameter);
            Assert.Contains(" IN (", parameters.Rows.Where(row => Enumerable.Contains(values, row.Id)).ToQueryString());
            Assert.Throws<InvalidOperationException>(() => context.Rows.Where(row => Enumerable.Contains(EF.Parameter(values), row.Id)).ToQueryString());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Context_collection_modes_do_not_reuse_another_modes_compiled_query(OceanBaseCompatibilityMode mode, bool constantFirst)
        {
            int[] values = [43, 47, 53];
            foreach (var constant in new[] { constantFirst, !constantFirst, constantFirst })
            {
                using var context = CreateContext(mode, collectionMode: constant ? ParameterTranslationMode.Constant : null);
                var sql = context.Rows.TagWith(constantFirst ? "constant-first" : "default-first")
                    .Where(row => Enumerable.Contains(values, row.Id)).ToQueryString();
                Assert.Equal(constant, sql.Contains("43, 47, 53", StringComparison.Ordinal));
                Assert.Equal(!constant, sql.Contains(mode == OceanBaseCompatibilityMode.MySql ? "@values" : ":values", StringComparison.Ordinal));
            }
            using var defaults = CreateContext(mode);
            using var multiple = CreateContext(mode, collectionMode: ParameterTranslationMode.MultipleParameters);
            Assert.Equal(defaults.Rows.Where(row => Enumerable.Contains(values, row.Id)).ToQueryString(),
                multiple.Rows.Where(row => Enumerable.Contains(values, row.Id)).ToQueryString());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Membership_null_empty_negation_projection_and_case_are_processed_by_EF(OceanBaseCompatibilityMode mode, bool relationalNulls)
        {
            using var context = CreateContext(mode, relationalNulls);
            int?[] values = [];
            var query = context.Rows.Select(row => new { Match = Enumerable.Contains(values, row.Optional),
                Not = !Enumerable.Contains(values, row.Optional), Choice = Enumerable.Contains(values, row.Optional) ? 7 : 9 });
            var empty = query.ToQueryString();
            Assert.DoesNotContain(" IN (", empty);
            Assert.DoesNotContain("IN ()", empty);
            Assert.Contains("9", empty);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql
                ? "SELECT 0 AS `Match`, 1 AS `Not`, 9 AS `Choice`"
                : "SELECT 0 \"Match\", 1 \"Not\", 9 \"Choice\"", empty);
            values = [null, null];
            var allNull = query.ToQueryString();
            Assert.DoesNotContain("IN ()", allNull);
            Assert.Equal(!relationalNulls, allNull.Contains("IS NULL", StringComparison.Ordinal));
            values = [2, null, 2, 7];
            var mixed = query.ToQueryString();
            Assert.Contains(" IN (", mixed);
            Assert.Contains("CASE", mixed);
            Assert.Equal(!relationalNulls, mixed.Contains("IS NULL", StringComparison.Ordinal));
            Assert.Contains(" IN (", context.Rows.Select(row => (bool?)Enumerable.Contains(values, row.Optional)).ToQueryString());
            Assert.Contains(" IN (", context.Rows.Where(row => !Enumerable.Contains(values, row.Optional)).ToQueryString());
            values = null!;
            Assert.Equal(empty, query.ToQueryString());
            Assert.DoesNotContain(" IN (", context.Rows.Where(row => !Enumerable.Contains(values!, row.Optional)).ToQueryString());
            Assert.DoesNotContain(" IN (", context.Rows.Where(row => Enumerable.Contains(Array.Empty<int>(), row.Id)).ToQueryString());
            int[] required = [];
            var requiredQuery = context.Rows.Select(row => Enumerable.Contains(required, row.Id));
            var emptyRequired = requiredQuery.ToQueryString();
            required = null!;
            Assert.Equal(emptyRequired, requiredQuery.ToQueryString());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Ordinary_joins_keep_composite_keys_nullable_right_and_filter_placement(OceanBaseCompatibilityMode mode, bool relationalNulls)
        {
            using var context = CreateContext(mode, relationalNulls);
            var inner = context.Rows.Join(context.Rows, a => a.Id, b => b.ParentId, (a, b) => new { a.Id, Right = b.Id });
            Assert.Contains("INNER JOIN", inner.ToQueryString());
            var composite = context.Rows.Join(context.Rows,
                a => new { A = a.Id, B = a.Optional }, b => new { A = b.ParentId, B = b.Optional },
                (a, b) => new { a.Id, Right = b.Id });
            Assert.Contains(" AND ", composite.ToQueryString());
            var left = from a in context.Rows
                       join b in context.Rows.Where(row => row.Optional > 3) on a.Id equals b.ParentId into matches
                       from b in matches.DefaultIfEmpty()
                       select new { a.Id, Right = (int?)b.Id };
            var leftSql = left.ToQueryString();
            Assert.Contains("LEFT JOIN", leftSql);
            Assert.Contains(" > 3", leftSql);
            Assert.DoesNotContain("APPLY", leftSql);
            var filtered = left.Where(row => row.Right > 3).ToQueryString();
            Assert.Contains("WHERE", filtered);
            Assert.NotEqual(leftSql, filtered);
            var exact = context.Rows.LeftJoin(context.Rows, a => a.Id, b => b.ParentId,
                (a, b) => new { a.Id, Right = (int?)b!.Id });
            Assert.Contains("LEFT JOIN", exact.ToQueryString());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Correlated_quantifiers_scalar_first_and_queryable_contains_use_existing_relational_paths(OceanBaseCompatibilityMode mode, bool relationalNulls)
        {
            using var context = CreateContext(mode, relationalNulls);
            var any = context.Rows.Where(a => context.Rows.Any(b => b.ParentId == a.Id && b.Optional > 3)).ToQueryString();
            Assert.Contains("EXISTS (", any);
            var all = context.Rows.Where(a => context.Rows.Where(b => b.ParentId == a.Id).All(b => b.Optional > 3)).ToQueryString();
            Assert.Contains("NOT EXISTS (", all);
            var projected = context.Rows.Select(a => new { Any = context.Rows.Any(b => b.ParentId == a.Id),
                Not = !context.Rows.Any(b => b.ParentId == a.Id), All = context.Rows.Where(b => b.ParentId == a.Id).All(b => b.Optional > 3) }).ToQueryString();
            Assert.Contains("EXISTS (", projected);
            Assert.Contains("NOT EXISTS (", projected);
            if (mode == OceanBaseCompatibilityMode.Oracle) Assert.Contains("CASE", projected);
            var first = context.Rows.Select(a => context.Rows.Where(b => b.ParentId == a.Id)
                .OrderBy(b => b.Id).Select(b => b.Optional).FirstOrDefault()).ToQueryString();
            Assert.Contains("ORDER BY", first);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "LIMIT 1" : "FETCH FIRST 1 ROWS ONLY", first);
            var contains = context.Rows.Where(a => context.Rows.Select(b => b.Optional).Contains(a.Optional)).ToQueryString();
            Assert.True(contains.Contains(" IN (", StringComparison.Ordinal) || contains.Contains("EXISTS (", StringComparison.Ordinal), contains);
            Assert.DoesNotContain("APPLY", first);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Counts_group_nullable_keys_count_rows_and_keep_predicate_distinct_and_having(OceanBaseCompatibilityMode mode, bool relationalNulls)
        {
            using var context = CreateContext(mode, relationalNulls);
            var grouped = context.Rows.GroupBy(row => row.Optional).Where(group => group.Count() > 1)
                .Select(group => new { group.Key, Count = group.Count(), Long = group.LongCount(),
                    Predicate = group.Count(row => row.Id > 3), LongPredicate = group.LongCount(row => row.Id > 3),
                    NullableRows = group.Select(row => row.Optional).Count(),
                    Distinct = group.Where(row => row.Id > 3).Select(row => row.Optional).Distinct().Count() }).ToQueryString();
            Assert.Contains("GROUP BY", grouped);
            Assert.Contains("HAVING COUNT(*) > 1", grouped);
            Assert.Contains("COUNT(*)", grouped);
            Assert.Contains("COUNT(CASE", grouped);
            Assert.Contains("COUNT(DISTINCT", grouped);
            Assert.DoesNotContain("COUNT_BIG", grouped);
            var nullableCount = context.Rows.GroupBy(row => row.Optional)
                .Select(group => group.Select(row => row.Optional).Count()).ToQueryString();
            Assert.Contains("COUNT(*)", nullableCount);
            var converted = context.Rows.GroupBy(row => row.Converted).Select(group => new { group.Key, Count = group.Count(), Long = group.LongCount() }).ToQueryString();
            Assert.Contains("COUNT(*)", converted);
            Assert.DoesNotContain("COUNT_BIG", converted);
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
                modelBuilder.Entity<Row>().Property(row => row.Converted).HasConversion<long>();
                modelBuilder.Entity<Row>().Property(row => row.State).HasConversion<int>();
            }
        }

        private enum RowState { Inactive, Active }
        private sealed class Row
        {
            public int Id { get; set; }
            public int ParentId { get; set; }
            public int? Optional { get; set; }
            public int Converted { get; set; }
            public RowState State { get; set; }
        }
    }
}
