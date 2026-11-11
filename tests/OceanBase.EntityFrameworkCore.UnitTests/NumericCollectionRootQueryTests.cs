using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OceanBase.EntityFrameworkCore.Query;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests
{
    public sealed class NumericCollectionRootQueryTests
    {
        public static IEnumerable<object[]> Modes() => IntegerCollectionRootQueryTests.Modes();

        [Theory]
        [MemberData(nameof(Modes))]
        public void Captured_numeric_roots_keep_carriers_duplicates_nulls_and_current_values(
            OceanBaseCompatibilityMode mode, bool relationalNulls, ParameterTranslationMode? translation)
        {
            Check(decimal.MinValue, decimal.MaxValue, 0.0000000000000000000000000001m, "DECIMAL(65,30)", "NUMBER");
            Check(float.MinValue, float.MaxValue, float.Epsilon, "FLOAT", "BINARY_FLOAT");
            Check(double.MinValue, double.MaxValue, double.Epsilon, "DOUBLE", "BINARY_DOUBLE");

            void Check<T>(T minimum, T maximum, T tiny, string mysql, string oracle) where T : struct
            {
                using var context = Create<T>(mode, relationalNulls, translation);
                T?[] values = [];
                List<T> list = [];
                var arrayQuery = context.Rows.SelectMany(row => values.Where(value => row.Id > 0));
                var listQuery = context.Rows.SelectMany(row => list.Where(value => row.Id > 0));
                foreach (var input in new T?[][] { [], [minimum, maximum, tiny, minimum, null], [null, null], [maximum], [] })
                {
                    values = input;
                    list = input.Where(value => value.HasValue).Select(value => value!.Value).ToList();
                    foreach (var item in new (IQueryable Query, int Count)[]
                    {
                        (arrayQuery, input.Length), (listQuery, list.Count),
                        (context.Rows.Join(values, row => row.Optional, value => value, (row, value) => value), input.Length)
                    })
                    {
                        var sql = item.Query.ToQueryString();
                        Assert.Contains(" AS " + (mode == OceanBaseCompatibilityMode.MySql ? mysql : oracle) + ")", sql);
                        Assert.Equal(Math.Max(0, item.Count - 1), sql.Split("UNION ALL SELECT").Length - 1);
                        Assert.Equal(item.Count == 0, sql.Contains("WHERE 1 = 0", StringComparison.Ordinal));
                        Assert.DoesNotContain("JSON", sql);
                        if (mode == OceanBaseCompatibilityMode.Oracle)
                            Assert.Equal(Math.Max(1, item.Count), sql.Split("FROM DUAL").Length - 1);
                    }
                    Assert.Equal(input, values);
                }
                values = [minimum, minimum, maximum, null];
                var distinct = context.Rows.SelectMany(row => values.Where(value => row.Id > 0).Distinct().Take(2)).ToQueryString();
                Assert.Contains("DISTINCT", distinct);
                Assert.Contains(" AS " + (mode == OceanBaseCompatibilityMode.MySql ? mysql : oracle) + ")", distinct);
            }
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public void Inline_numeric_roots_keep_original_types_when_empty_or_pruned(
            OceanBaseCompatibilityMode mode, bool relationalNulls, ParameterTranslationMode? translation)
        {
            Check(decimal.MinValue, decimal.MaxValue, "DECIMAL(65,30)", "NUMBER");
            Check(float.MinValue, float.MaxValue, "FLOAT", "BINARY_FLOAT");
            Check(double.MinValue, double.MaxValue, "DOUBLE", "BINARY_DOUBLE");

            void Check<T>(T minimum, T maximum, string mysql, string oracle) where T : struct
            {
                using var context = Create<T>(mode, relationalNulls, translation);
                foreach (var item in new (IQueryable Query, bool OrdinalOnly)[]
                {
                    (context.Rows.Join(new T[] { }, row => row.Value, value => value, (row, value) => value), false),
                    (context.Rows.Join(new T?[] { }, row => row.Optional, value => value, (row, value) => value), false),
                    (context.Rows.Select(row => new T[] { }.Count(value => row.Id > 0)), false),
                    (context.Rows.Select(row => new T?[] { }.Select(value => row.Id + 1).ElementAt(row.Id)), true),
                    (context.Rows.Select(row => new T[] { }.ElementAt(row.Id)), false)
                })
                {
                    var sql = item.Query.ToQueryString();
                    Assert.Contains("WHERE 1 = 0", sql);
                    var target = item.OrdinalOnly ? mode == OceanBaseCompatibilityMode.MySql ? "SIGNED" : "NUMBER"
                        : mode == OceanBaseCompatibilityMode.MySql ? mysql : oracle;
                    Assert.Contains("CAST(NULL AS " + target + ")", sql);
                }
                var inline = context.Rows.Join(new T?[] { minimum, maximum, minimum, null },
                    row => row.Optional, value => value, (row, value) => value).ToQueryString();
                Assert.Equal(3, inline.Split("UNION ALL SELECT").Length - 1);
                Assert.Contains(" AS " + (mode == OceanBaseCompatibilityMode.MySql ? mysql : oracle) + ")", inline);
            }
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public void Contextual_numeric_converters_choose_the_provider_carrier_without_column_facets(
            OceanBaseCompatibilityMode mode, bool relationalNulls, ParameterTranslationMode? translation)
        {
            using var context = Create<decimal>(mode, relationalNulls, translation);
            decimal?[] decimals = [null, decimal.MinValue, decimal.MaxValue, 0.0000000000000000000000000001m];
            var query = context.Set<ConvertedRow>().Where(row => decimals.Where(value => value > row.Value).Contains(row.Value));
            foreach (var input in new decimal?[][] { decimals, [], [null, null], [17m], [] })
            {
                decimals = input;
                var sql = query.ToQueryString();
                Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? " AS DOUBLE)" : " AS BINARY_DOUBLE)", sql);
                Assert.Equal(input.Length == 0, sql.Contains("WHERE 1 = 0", StringComparison.Ordinal));
            }
            var empty = context.Set<ConvertedRow>().Select(row => new decimal?[] { }.Count(value => value > row.Value)).ToQueryString();
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "CAST(NULL AS DOUBLE)" : "CAST(NULL AS BINARY_DOUBLE)", empty);
            var narrow = context.Set<NarrowRow>().Select(row => decimals.Where(value => value > row.Value).Count()).ToQueryString();
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? " AS DECIMAL(65,30))" : " AS NUMBER)", narrow);
            Assert.DoesNotContain(" AS DECIMAL(5,2))", narrow);
            Assert.DoesNotContain(" AS NUMBER(5,2))", narrow);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Numeric_roots_reject_nonfinite_constants_and_explicit_single_parameters(OceanBaseCompatibilityMode mode)
        {
            using var context = Create<double>(mode, false, ParameterTranslationMode.Constant);
            double[] values = [double.NaN];
            var query = context.Rows.SelectMany(row => values.Where(value => row.Id > 0));
            foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                values = [value];
                Assert.Throws<ArgumentOutOfRangeException>(() => query.ToQueryString());
            }
            values = [double.Epsilon, double.MaxValue];
            Assert.Throws<InvalidOperationException>(() => context.Rows.Where(row => EF.Parameter(values)
                .Where(value => value > row.Value).Contains(row.Value)).ToQueryString());
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public void Contextually_converted_element_projections_use_the_property_carrier(
            OceanBaseCompatibilityMode mode, bool relationalNulls, ParameterTranslationMode? translation)
        {
            using var context = Create<decimal>(mode, relationalNulls, translation);
            decimal?[] values = [null, 17.25m];
            IQueryable[] Queries() =>
            [
                context.Set<ConvertedRow>().SelectMany(row => values.Where(value => value > row.Value)),
                context.Set<ConvertedRow>().Select(row => values.Where(value => value > row.Value).FirstOrDefault()),
                context.Set<ConvertedRow>().Join(values, row => row.Value, value => value, (row, value) => value),
                context.Set<ConvertedRow>().Join(new decimal?[] { 17.25m, null, 17.25m }, row => row.Value, value => value, (row, value) => value),
                context.Set<ConvertedRow>().Join(new decimal?[] { }, row => row.Value, value => value, (row, value) => value),
                context.Set<ConvertedRow>().SelectMany(row => values.Where(value => value > row.Value).Distinct().Take(2)),
                context.Set<ConvertedRow>().SelectMany(row => values.Where(value => value > row.Value).Take(2).Where(value => value != null)),
                context.Set<ConvertedRow>().Where(row => values.Where(value => value > row.Value).FirstOrDefault() == null),
                context.Set<ConvertedRow>().Where(row => values.Where(value => value > row.Value).FirstOrDefault() != null)
            ];
            foreach (var input in new decimal?[][] { values, [], [null, null], [17.25m], [] })
            {
                values = input;
                foreach (var query in Queries())
                {
                    string sql;
                    try { sql = query.ToQueryString(); }
                    catch (InvalidOperationException error) { throw new InvalidOperationException(query.Expression.ToString(), error); }
                    Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? " AS DOUBLE)" : " AS BINARY_DOUBLE)", sql);
                }
            }

            foreach (var query in new IQueryable[]
            {
                context.Set<SameStoreRow>().SelectMany(row => values.Where(value => value > row.Value)),
                context.Set<SameStoreRow>().Select(row => values.Where(value => value > row.Value).FirstOrDefault()),
                context.Set<SameStoreRow>().Join(new decimal?[] { }, row => row.Value, value => value, (row, value) => value),
                context.Set<NarrowRow>().SelectMany(row => values.Where(value => value > row.Value))
            })
                Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? " AS DECIMAL(65,30))" : " AS NUMBER)", query.ToQueryString());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void One_root_cannot_apply_different_same_store_converters_or_property_mappings(OceanBaseCompatibilityMode mode)
        {
            using var context = Create<decimal>(mode, false, null);
            decimal?[] values = [17.25m, null];
            decimal[] nonnullable = [17m, 18m];
            foreach (var query in new IQueryable[]
            {
                context.Set<ConvertedRow>().SelectMany(row => values.Where(value => value > row.Value && value < row.Other)),
                context.Set<ConvertedRow>().Select(row => values.Where(value => value > row.Value && value < row.Other).FirstOrDefault()),
                context.Set<ConvertedRow>().SelectMany(row => values.Where(value => value > row.Value && value < row.Native)),
                context.Set<ConvertedRow>().Where(row => values.Where(value => value > row.Other).FirstOrDefault() < row.Native),
                context.Set<ConvertedRow>().Select(row => values.Where(value => value > row.Other).FirstOrDefault() < row.Native),
                context.Set<ConvertedRow>().Select(row => values.Where(value => value > row.Other).FirstOrDefault() < 20m),
                context.Set<ConvertedRow>().Where(row => values.Where(value => value > row.Other).FirstOrDefault() < row.Value),
                context.Set<SameStoreRow>().Select(row => values.Where(value => value > row.Value).FirstOrDefault() < 20m),
                context.Set<ConvertedRow>().Select(row => nonnullable.Where(value => value > row.Other).FirstOrDefault() < 20m),
                context.Set<ConvertedRow>().Where(row => nonnullable.Where(value => value > row.Other).FirstOrDefault() < row.Native)
            })
                Assert.Contains("Conflicting type mappings", Assert.Throws<InvalidOperationException>(() => query.ToQueryString()).Message);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Projected_column_object_sharing_cannot_erase_a_native_predicate_requirement(OceanBaseCompatibilityMode mode, bool shared)
        {
            using var context = Create<decimal>(mode, false, null);
            var source = context.GetService<IRelationalTypeMappingSource>();
            var factory = context.GetService<ISqlExpressionFactory>();
            var native = source.FindMapping(typeof(decimal))!;
            var converted = (RelationalTypeMapping)context.Model.FindEntityType(typeof(ConvertedRow))!
                .FindProperty(nameof(ConvertedRow.Other))!.GetTypeMapping();
            var root = new ValuesExpression("v", new SqlParameterExpression("values", typeof(decimal?[]), null), ["Value"]);
            var projected = new ColumnExpression("Value", "v", typeof(decimal), native, true);
            var predicateColumn = shared ? projected : new ColumnExpression("Value", "v", typeof(decimal), native, true);
            var contextual = new ColumnExpression("Value", "v", typeof(decimal), converted, true);
            var predicate = factory.AndAlso(factory.GreaterThan(predicateColumn, factory.Constant(0m, native)),
                factory.LessThan(contextual, factory.Constant(100m, converted)));
            var select = new SelectExpression(null, [root], predicate, [], null, [new ProjectionExpression(projected, "Value")],
                false, [], null, null, new SqlAliasManager());
            Assert.Contains("Conflicting type mappings", Assert.Throws<InvalidOperationException>(() =>
                new OceanBaseCollectionProjectionTypeMappingNormalizer(source, context.Model).Normalize(select)).Message);
        }

        private static TestContext<T> Create<T>(OceanBaseCompatibilityMode mode, bool relationalNulls,
            ParameterTranslationMode? translation) where T : struct
        {
            var builder = new DbContextOptionsBuilder<TestContext<T>>()
                .UseOceanBase("Server=offline;Database=numeric_roots", mode).EnableServiceProviderCaching(false);
            var extension = builder.Options.Extensions.OfType<RelationalOptionsExtension>().Single().WithUseRelationalNulls(relationalNulls);
            if (translation.HasValue) extension = extension.WithUseParameterizedCollectionMode(translation.Value);
            ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(extension);
            return new TestContext<T>(builder.Options, mode);
        }

        private sealed class TestContext<T>(DbContextOptions<TestContext<T>> options, OceanBaseCompatibilityMode mode)
            : DbContext(options) where T : struct
        {
            public DbSet<Row<T>> Rows => Set<Row<T>>();
            protected override void OnModelCreating(ModelBuilder builder)
            {
                builder.Entity<ConvertedRow>().Property(row => row.Value).HasConversion(
                    new ValueConverter<decimal?, double?>(value => value.HasValue ? (double?)value.Value : null,
                        value => value.HasValue ? (decimal?)value.Value : null));
                builder.Entity<ConvertedRow>().Property(row => row.Other).HasConversion(
                    new ValueConverter<decimal?, double?>(value => value.HasValue ? (double?)value.Value + 100d : null,
                        value => value.HasValue ? (decimal?)(value.Value - 100d) : null));
                builder.Entity<SameStoreRow>().Property(row => row.Value).HasConversion(
                    new ValueConverter<decimal?, decimal?>(value => value + 100m, value => value - 100m));
                builder.Entity<NarrowRow>().Property(row => row.Value).HasColumnType(
                    mode == OceanBaseCompatibilityMode.MySql ? "DECIMAL(5,2)" : "NUMBER(5,2)");
            }
        }

        private sealed class Row<T> where T : struct
        {
            public int Id { get; set; }
            public T Value { get; set; }
            public T? Optional { get; set; }
        }

        private sealed class ConvertedRow
        {
            public int Id { get; set; }
            public decimal? Value { get; set; }
            public decimal? Other { get; set; }
            public decimal? Native { get; set; }
        }

        private sealed class NarrowRow
        {
            public int Id { get; set; }
            public decimal? Value { get; set; }
        }

        private sealed class SameStoreRow
        {
            public int Id { get; set; }
            public decimal? Value { get; set; }
        }
    }
}
