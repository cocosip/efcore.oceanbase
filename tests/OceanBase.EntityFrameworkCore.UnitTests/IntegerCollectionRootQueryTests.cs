using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
    public sealed class IntegerCollectionRootQueryTests
    {
        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Collection_roots_normalize_every_Oracle_UNION_branch_to_the_declared_carrier(OceanBaseCompatibilityMode mode)
        {
            Check<sbyte>(sbyte.MinValue, sbyte.MaxValue);
            Check<byte>(byte.MinValue, byte.MaxValue);
            Check<SignedState>(SignedState.Minimum, SignedState.Maximum);
            Check<bool>(false, true);
            Check<short>(short.MinValue, short.MaxValue);

            void Check<T>(T minimum, T maximum) where T : struct
            {
                using var context = Create<T>(mode, false);
                T?[] values = [minimum, maximum, null, minimum];
                var query = context.Rows.SelectMany(row => values.Where(value => row.Id > 0));
                var sql = query.ToQueryString();
                Assert.Equal(mode == OceanBaseCompatibilityMode.Oracle ? 4 : 1, Regex.Matches(sql, @"CAST\(").Count);
                Assert.Equal(3, Regex.Matches(sql, "UNION ALL SELECT").Count);
                values = [];
                sql = query.ToQueryString();
                Assert.Equal(2, Regex.Matches(sql, @"CAST\(").Count);
                Assert.Contains("WHERE 1 = 0", sql);
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void National_text_roots_use_one_charset_across_every_Oracle_UNION_branch(OceanBaseCompatibilityMode mode)
        {
            using var context = Create<int>(mode, false);
            string?[] values = ["alpha", "beta", null];
            var query = context.Rows.SelectMany(row => values.Where(value => row.Id > 0));
            Assert.Equal(mode == OceanBaseCompatibilityMode.Oracle ? 3 : 0,
                Regex.Matches(query.ToQueryString(), mode == OceanBaseCompatibilityMode.Oracle ? @"TO_NCHAR\(" : @"CAST\(").Count);
        }

        public static IEnumerable<object[]> Modes()
        {
            foreach (var mode in new[] { OceanBaseCompatibilityMode.MySql, OceanBaseCompatibilityMode.Oracle })
            foreach (var relationalNulls in new[] { false, true })
                yield return new object[] { mode, relationalNulls };
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public void All_integer_roots_preserve_extrema_duplicates_nulls_and_empty_transitions(
            OceanBaseCompatibilityMode mode, bool relationalNulls)
        {
            Check<sbyte>(sbyte.MinValue, sbyte.MaxValue, false);
            Check<byte>(byte.MinValue, byte.MaxValue, true);
            Check<short>(short.MinValue, short.MaxValue, false);
            Check<ushort>(ushort.MinValue, ushort.MaxValue, true);
            Check<int>(int.MinValue, int.MaxValue, false);
            Check<uint>(uint.MinValue, uint.MaxValue, true);
            Check<long>(long.MinValue, long.MaxValue, false);
            Check<ulong>(ulong.MinValue, ulong.MaxValue, true);
            Check<UnsignedState>(UnsignedState.Zero, UnsignedState.Maximum, true);
            Check<SignedState>(SignedState.Minimum, SignedState.Maximum, false);

            void Check<T>(T minimum, T maximum, bool unsigned) where T : struct
            {
                using var context = Create<T>(mode, relationalNulls);
                T?[] values = [];
                List<T> list = [];
                var arrayQuery = context.Rows.SelectMany(row => values.Where(value => row.Id > 0));
                var listQuery = context.Rows.SelectMany(row => list.Where(value => row.Id > 0));
                foreach (var capture in new T?[][] { [], [minimum, maximum, minimum, null], [null, null], [maximum], [] })
                {
                    values = capture;
                    list = capture.Where(value => value.HasValue).Select(value => value!.Value).ToList();
                    foreach (var item in new (IQueryable Query, int Count)[]
                    {
                        (arrayQuery, values.Length), (listQuery, list.Count),
                        (context.Rows.Join(values, row => row.Optional, value => value, (row, value) => value), values.Length)
                    })
                    {
                        var sql = item.Query.ToQueryString();
                        if (typeof(T) == typeof(int) || typeof(T) == typeof(long))
                        {
                            Assert.Contains("JSON_TABLE(", sql);
                            continue;
                        }
                        Assert.Contains(mode == OceanBaseCompatibilityMode.Oracle ? " AS NUMBER)" : unsigned ? " AS UNSIGNED)" : " AS SIGNED)", sql);
                        Assert.Equal(Math.Max(0, item.Count - 1), sql.Split("UNION ALL SELECT").Length - 1);
                        Assert.Equal(item.Count == 0, sql.Contains("WHERE 1 = 0", StringComparison.Ordinal));
                        Assert.DoesNotContain("UNION ALL VALUES", sql);
                        Assert.DoesNotContain("JSON", sql);
                        if (mode == OceanBaseCompatibilityMode.Oracle)
                            Assert.Equal(Math.Max(1, item.Count), sql.Split("FROM DUAL").Length - 1);
                    }
                    Assert.Equal(capture, values);
                }
            }
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public void Inline_integer_roots_keep_the_original_type_through_empty_pruning_and_ordinal_access(
            OceanBaseCompatibilityMode mode, bool relationalNulls)
        {
            Check<sbyte>(sbyte.MinValue, sbyte.MaxValue, false);
            Check<byte>(byte.MinValue, byte.MaxValue, true);
            Check<short>(short.MinValue, short.MaxValue, false);
            Check<ushort>(ushort.MinValue, ushort.MaxValue, true);
            Check<int>(int.MinValue, int.MaxValue, false);
            Check<uint>(uint.MinValue, uint.MaxValue, true);
            Check<long>(long.MinValue, long.MaxValue, false);
            Check<ulong>(ulong.MinValue, ulong.MaxValue, true);
            Check<UnsignedState>(UnsignedState.Zero, UnsignedState.Maximum, true);
            Check<SignedState>(SignedState.Minimum, SignedState.Maximum, false);

            void Check<T>(T minimum, T maximum, bool unsigned) where T : struct
            {
                using var context = Create<T>(mode, relationalNulls);
                foreach (var item in new (IQueryable Query, bool OrdinalOnly)[]
                {
                    (context.Rows.SelectMany(row => new T[] { }.Join(context.Rows, value => value,
                        other => other.Value, (value, other) => row.Id)), false),
                    (context.Rows.SelectMany(row => new T?[] { }.Join(context.Rows, value => value,
                        other => other.Optional, (value, other) => row.Id)), false),
                    (context.Rows.Select(row => new T[] { }.Count(value => row.Id > 0)), false),
                    (context.Rows.Select(row => new T?[] { }.Select(value => row.Id + 1).ElementAt(row.Id)), true),
                    (context.Rows.Select(row => new T[] { }.ElementAt(row.Id)), false)
                })
                {
                    var sql = item.Query.ToQueryString();
                    Assert.Contains("WHERE 1 = 0", sql);
                    Assert.Contains(mode == OceanBaseCompatibilityMode.Oracle ? "CAST(NULL AS NUMBER)"
                        : unsigned && !item.OrdinalOnly ? "CAST(NULL AS UNSIGNED)" : "CAST(NULL AS SIGNED)", sql);
                }
                var inline = context.Rows.SelectMany(row => new T?[] { minimum, maximum, minimum, null }
                    .Where(value => value.Equals(row.Optional))).ToQueryString();
                Assert.Equal(3, inline.Split("UNION ALL SELECT").Length - 1);
                Assert.Contains(mode == OceanBaseCompatibilityMode.Oracle ? " AS NUMBER)" : unsigned ? " AS UNSIGNED)" : " AS SIGNED)", inline);
            }
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public void Nullable_converter_roots_keep_their_mapping_through_captures_and_originally_empty_inline_values(
            OceanBaseCompatibilityMode mode, bool relationalNulls)
        {
            using var context = Create<int>(mode, relationalNulls);
            var mapping = context.Model.FindEntityType(typeof(NullableConvertedRow))!.FindProperty(nameof(NullableConvertedRow.Value))!.GetTypeMapping();
            Assert.Equal(typeof(int?), mapping.ClrType);
            Assert.Equal(typeof(ulong?), mapping.Converter!.ProviderClrType);
            Assert.False(mapping.Converter.ConvertsNulls);
            int?[] values = [null, 0, 17, int.MaxValue, 17];
            var query = context.Set<NullableConvertedRow>().Where(row => values.Where(value => value > row.Value)
                .Contains(row.Value)).Select(row => row.Value);
            foreach (var input in new int?[][] { values, [], [null, null], [17], [] })
            {
                values = input;
                var sql = query.ToQueryString();
                Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? " AS UNSIGNED)" : " AS NUMBER)", sql);
                Assert.Equal(input.Length == 0, sql.Contains("WHERE 1 = 0", StringComparison.Ordinal)
                    || sql.Contains("WHERE 0", StringComparison.Ordinal));
            }
            var empty = context.Set<NullableConvertedRow>()
                .Select(row => new int?[] { }.Count(value => value > row.Value)).ToQueryString();
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "CAST(NULL AS UNSIGNED)" : "CAST(NULL AS NUMBER)", empty);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Captured_integer_roots_rebind_current_values_and_empty_metadata(
            OceanBaseCompatibilityMode mode, bool relationalNulls)
        {
            using var context = Create<int>(mode, relationalNulls);
            int[] values = [int.MinValue, 17, int.MaxValue, 17];
            var query = context.Rows.Where(row => values.Contains(row.Id));

            foreach (var input in new[] { values, new[] { 17 }, Array.Empty<int>() })
            {
                values = input;
                var sql = query.ToQueryString();
                Assert.Contains("JSON_TABLE(", sql);
                Assert.Contains(System.Text.Json.JsonSerializer.Serialize(input), sql);
                Assert.DoesNotContain("UNION ALL VALUES", sql);
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Captured_nullable_integer_roots_preserve_null_and_empty_boundaries(
            OceanBaseCompatibilityMode mode, bool relationalNulls)
        {
            using var context = Create<int>(mode, relationalNulls);
            int?[] values = [null, 17, null, 17];
            var query = context.Rows.Where(row => values.Contains(row.Optional));

            foreach (var input in new int?[][] { values, [null], [] })
            {
                values = input;
                var sql = query.ToQueryString();
                Assert.Contains("JSON_TABLE(", sql);
                Assert.Contains(System.Text.Json.JsonSerializer.Serialize(input), sql);
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Captured_integer_lists_translate_and_rebind(
            OceanBaseCompatibilityMode mode)
        {
            using var context = Create<int>(mode, false);
            List<int> values = [1, 2, 1];
            var query = context.Rows.Where(row => values.Contains(row.Id));

            foreach (var input in new[] { new List<int> { 1, 2, 1 }, new List<int> { 17 }, new List<int>() })
            {
                values = input;
                var sql = query.ToQueryString();
                Assert.Contains("JSON_TABLE(", sql);
                Assert.Contains(System.Text.Json.JsonSerializer.Serialize(input), sql);
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Nonnumeric_and_null_converting_root_mappings_reject_before_expansion(OceanBaseCompatibilityMode mode)
        {
            using var context = Create<int>(mode, false);
            var source = context.GetService<IRelationalTypeMappingSource>();
            var mappings = new[]
            {
                source.FindMapping(typeof(long))!.WithComposedConverter(new ValueConverter<string, long>(value => long.Parse(value), value => value.ToString())),
                source.FindMapping(typeof(int))!.WithComposedConverter(new ValueConverter<int, int>(value => value, value => value, convertsNulls: true)),
                source.FindMapping(typeof(decimal))!.WithComposedConverter(new ValueConverter<decimal, decimal>(value => value, value => value, convertsNulls: true)),
                source.FindMapping(typeof(float))!.WithComposedConverter(new ValueConverter<float, float>(value => value, value => value, convertsNulls: true)),
                source.FindMapping(typeof(double))!.WithComposedConverter(new ValueConverter<double, double>(value => value, value => value, convertsNulls: true)),
                new DecimalTypeMapping("DECIMAL(65,30)")
            };
            foreach (var core in mappings)
            {
                var mapping = (RelationalTypeMapping)core;
                var type = mapping.ClrType.MakeArrayType();
                var parameters = new Dictionary<string, object?> { ["values"] = Array.CreateInstance(mapping.ClrType, 1) };
                var parameter = new SqlParameterExpression("values", type, source.FindMapping(type, context.Model, mapping));
                var visitor = new OceanBaseCollectionParameterExpandingVisitor(context.GetService<ISqlExpressionFactory>(),
                    parameters, () => { }, source, mode);
                Assert.Contains("numeric", Assert.Throws<NotSupportedException>(() => visitor.Visit(
                    new TableValuedFunctionExpression("v", "OceanBaseCollection", [parameter])
                        .AddAnnotation(OceanBaseQueryableMethodTranslatingExpressionVisitorFactory.ParameterCollectionAnnotation, new[] { "Value" }))).Message);
                Assert.Single(parameters);
            }

        }

        private static TestContext<T> Create<T>(OceanBaseCompatibilityMode mode, bool relationalNulls) where T : struct
        {
            var builder = new DbContextOptionsBuilder<TestContext<T>>()
                .UseOceanBase("Server=offline;Database=integer_roots", mode).EnableServiceProviderCaching(false);
            var extension = builder.Options.Extensions.OfType<RelationalOptionsExtension>().Single().WithUseRelationalNulls(relationalNulls);
            ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(extension);
            return new TestContext<T>(builder.Options);
        }

        private sealed class TestContext<T>(DbContextOptions<TestContext<T>> options) : DbContext(options) where T : struct
        {
            public DbSet<Row<T>> Rows => Set<Row<T>>();
            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity<NullableConvertedRow>().Property(row => row.Value).HasConversion(
                    new ValueConverter<int?, ulong?>(
                        value => value.HasValue ? (ulong?)((ulong)value.Value + 9223372036854775808UL) : null,
                        value => value.HasValue ? (int?)checked((int)(value.Value - 9223372036854775808UL)) : null));
            }
        }

        private sealed class NullableConvertedRow
        {
            public int Id { get; set; }
            public int? Value { get; set; }
        }

        private sealed class Row<T> where T : struct
        {
            public int Id { get; set; }
            public T Value { get; set; }
            public T? Optional { get; set; }
        }

        private enum UnsignedState : ulong { Zero = 0, Maximum = ulong.MaxValue }
        private enum SignedState : sbyte { Minimum = sbyte.MinValue, Maximum = sbyte.MaxValue }
    }
}
