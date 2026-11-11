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
    public sealed class IntegerCollectionRootQueryTests
    {
        public static IEnumerable<object[]> Modes()
        {
            foreach (var mode in new[] { OceanBaseCompatibilityMode.MySql, OceanBaseCompatibilityMode.Oracle })
            foreach (var relationalNulls in new[] { false, true })
            foreach (var translation in new ParameterTranslationMode?[] { null, ParameterTranslationMode.Constant, ParameterTranslationMode.MultipleParameters })
                yield return new object[] { mode, relationalNulls, translation! };
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public void All_integer_roots_preserve_extrema_duplicates_nulls_and_empty_transitions(
            OceanBaseCompatibilityMode mode, bool relationalNulls, ParameterTranslationMode? translation)
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
                using var context = Create<T>(mode, relationalNulls, translation);
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
            OceanBaseCompatibilityMode mode, bool relationalNulls, ParameterTranslationMode? translation)
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
                    Assert.Contains(mode == OceanBaseCompatibilityMode.Oracle ? "CAST(NULL AS NUMBER)"
                        : unsigned && !item.OrdinalOnly ? "CAST(NULL AS UNSIGNED)" : "CAST(NULL AS SIGNED)", sql);
                }
                var inline = context.Rows.Join(new T?[] { minimum, maximum, minimum, null },
                    row => row.Optional, value => value, (row, value) => value).ToQueryString();
                Assert.Equal(3, inline.Split("UNION ALL SELECT").Length - 1);
                Assert.Contains(mode == OceanBaseCompatibilityMode.Oracle ? " AS NUMBER)" : unsigned ? " AS UNSIGNED)" : " AS SIGNED)", inline);
            }
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public void Nullable_converter_roots_keep_their_mapping_through_captures_and_originally_empty_inline_values(
            OceanBaseCompatibilityMode mode, bool relationalNulls, ParameterTranslationMode? translation)
        {
            using var context = Create<int>(mode, relationalNulls, translation);
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
                Assert.Equal(input.Length == 0, sql.Contains("WHERE 1 = 0", StringComparison.Ordinal));
            }
            var empty = context.Set<NullableConvertedRow>()
                .Select(row => new int?[] { }.Count(value => value > row.Value)).ToQueryString();
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "CAST(NULL AS UNSIGNED)" : "CAST(NULL AS NUMBER)", empty);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Noninteger_and_null_converting_root_mappings_reject_before_expansion(OceanBaseCompatibilityMode mode)
        {
            using var context = Create<int>(mode, false, null);
            var source = context.GetService<IRelationalTypeMappingSource>();
            var mappings = new[]
            {
                source.FindMapping(typeof(int))!.WithComposedConverter(new ValueConverter<bool, int>(value => value ? 1 : 0, value => value != 0)),
                source.FindMapping(typeof(long))!.WithComposedConverter(new ValueConverter<decimal, long>(value => (long)value, value => value)),
                source.FindMapping(typeof(decimal))!.WithComposedConverter(new ValueConverter<int, decimal>(value => value, value => (int)value)),
                source.FindMapping(typeof(int))!.WithComposedConverter(new ValueConverter<int, int>(value => value, value => value, convertsNulls: true))
            };
            foreach (var core in mappings)
            {
                var mapping = (RelationalTypeMapping)core;
                var type = mapping.ClrType.MakeArrayType();
                var parameters = new Dictionary<string, object?> { ["values"] = Array.CreateInstance(mapping.ClrType, 1) };
                var parameter = new SqlParameterExpression("values", type, source.FindMapping(type, context.Model, mapping));
                var visitor = new OceanBaseCollectionParameterExpandingVisitor(context.GetService<ISqlExpressionFactory>(),
                    new RelationalParameterBasedSqlProcessorParameters(false, ParameterTranslationMode.MultipleParameters),
                    new ParametersCacheDecorator(parameters), source, mode);
                Assert.Contains("integer", Assert.Throws<NotSupportedException>(() => visitor.Visit(
                    new ValuesExpression("v", parameter, new[] { "Value" }))).Message);
                Assert.Single(parameters);
            }
            using var wide = Create<ulong>(mode, false, null);
            ulong[] values = [0, ulong.MaxValue];
            Assert.Throws<InvalidOperationException>(() => wide.Rows.Where(row => EF.Parameter(values)
                .Where(value => value > row.Value).Contains(row.Value)).ToQueryString());
        }

        private static TestContext<T> Create<T>(OceanBaseCompatibilityMode mode, bool relationalNulls,
            ParameterTranslationMode? translation) where T : struct
        {
            var builder = new DbContextOptionsBuilder<TestContext<T>>()
                .UseOceanBase("Server=offline;Database=integer_roots", mode).EnableServiceProviderCaching(false);
            var extension = builder.Options.Extensions.OfType<RelationalOptionsExtension>().Single().WithUseRelationalNulls(relationalNulls);
            if (translation.HasValue) extension = extension.WithUseParameterizedCollectionMode(translation.Value);
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
