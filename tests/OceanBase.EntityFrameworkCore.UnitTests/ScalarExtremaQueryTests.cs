using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Caching.Memory;
using OceanBase.EntityFrameworkCore.Query;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests
{
    public sealed class ScalarExtremaQueryTests
    {
        private static readonly Type[] NumericTypes =
        [
            typeof(sbyte), typeof(byte), typeof(short), typeof(ushort), typeof(int), typeof(uint),
            typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal)
        ];

        public static IEnumerable<object[]> ExactMethods()
        {
            foreach (var name in new[] { nameof(Math.Min), nameof(Math.Max) })
            {
                foreach (var type in NumericTypes)
                    yield return [typeof(Math), name, type];
                yield return [typeof(MathF), name, typeof(float)];
                yield return [typeof(decimal), name, typeof(decimal)];
            }
        }

        [Theory]
        [MemberData(nameof(ExactMethods))]
        public void Exact_scalar_methods_translate_in_server_predicates(Type owner, string name, Type type)
        {
            using var context = CreateContext(name == "Min" ? OceanBaseCompatibilityMode.MySql : OceanBaseCompatibilityMode.Oracle);
            var method = owner.GetMethod(name, [type, type])!;
            Assert.Equal(type, method.ReturnType);
            var row = Expression.Parameter(typeof(Row), "row");
            var call = Expression.Call(method, Expression.Property(row, type.Name), Expression.Default(type));
            var sql = context.Rows.Where(Expression.Lambda<Func<Row, bool>>(
                Expression.Equal(call, Expression.Default(type)), row)).ToQueryString();
            Assert.Contains(name == "Min" ? "LEAST(" : "GREATEST(", sql);
            Assert.Contains("WHERE", sql);

            // Visit the original identities as well: EF can forward MathF and decimal calls to Math.
            var direct = CreateVisitor(context).Translate(Expression.Call(method, Expression.Constant(Convert.ChangeType(1, type)),
                Expression.Constant(Convert.ChangeType(2, type))));
            Assert.Equal(name == "Min" ? "LEAST" : "GREATEST", Native(direct).Name);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void All_numeric_families_support_typed_variadic_arrays_and_nullable_operands(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var row = Expression.Parameter(typeof(Row), "r");
            foreach (var numeric in NumericTypes)
            foreach (var type in new[] { numeric, typeof(Nullable<>).MakeGenericType(numeric) })
            {
                var property = Expression.Property(row, numeric.Name);
                var operand = type == numeric ? (Expression)property : Expression.Convert(property, type);
                var bound = Expression.Convert(Expression.Constant(Convert.ChangeType(1, numeric)), type);
                var initializer = Expression.NewArrayInit(type, operand, bound, Expression.Default(type));
                var values = Array.CreateInstance(type, 2);
                values.SetValue(Convert.ChangeType(1, numeric), 0);
                var predicate = Expression.AndAlso(Expression.LessThanOrEqual(Variadic("Least", type, initializer), bound),
                    Expression.GreaterThanOrEqual(Variadic("Greatest", type, Expression.Constant(values)), operand));
                var sql = context.Rows.Where(Expression.Lambda<Func<Row, bool>>(predicate, row)).ToQueryString();
                Assert.Contains("LEAST(", sql);
                Assert.Contains("GREATEST(", sql);
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void First_mapping_owns_result_and_preserves_independent_operand_converters(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var visitor = CreateVisitor(context);
            var source = context.GetService<IRelationalTypeMappingSource>();
            var entity = context.Model.FindEntityType(typeof(Row))!;
            var plain = entity.FindProperty(nameof(Row.Decimal))!.GetRelationalTypeMapping();
            var reversed = entity.FindProperty(nameof(Row.AlternateDecimal))!.GetRelationalTypeMapping();
            var first = new SqlConstantExpression(2m, typeof(decimal), reversed);
            var later = new SqlConstantExpression(3m, typeof(decimal), plain);
            var unmapped = Expression.Constant(1m);
            var max = typeof(decimal).GetMethod(nameof(decimal.Max), [typeof(decimal), typeof(decimal)])!;
            var result = Native(visitor.Translate(Expression.Call(max, first, later)));
            Assert.True(result.IsNullable);
            Assert.All(result.ArgumentsPropagateNullability!, Assert.False);
            Assert.Same(first, result.Arguments![0]);
            Assert.Same(later, result.Arguments[1]);
            Assert.NotSame(reversed, result.TypeMapping);
            Assert.Same(reversed.Converter, result.TypeMapping!.Converter);
            Assert.Equal(reversed.StoreType, result.TypeMapping.StoreType);
            Assert.Equal((13, 5), (result.TypeMapping.Precision, result.TypeMapping.Scale));
            Assert.Equal(nameof(DbDataReader.GetValue), result.TypeMapping.GetDataReaderMethod().Name);
            Assert.Same(reversed, Native(visitor.Translate(Expression.Call(max, unmapped, first))).Arguments![0].TypeMapping);
            Assert.Same(plain, Native(visitor.Translate(Expression.Call(max, later, first))).Arguments![0].TypeMapping);
            Assert.Same(first, visitor.Translate(Variadic("Least", typeof(decimal), Expression.NewArrayInit(typeof(decimal), first))));

            var doubleCarrier = (RelationalTypeMapping)source.FindMapping(typeof(double))!
                .WithComposedConverter(new ValueConverter<float, double>(v => v, v => (float)v));
            var floatModel = new SqlConstantExpression(2f, typeof(float), doubleCarrier);
            var min = typeof(Math).GetMethod(nameof(Math.Min), [typeof(float), typeof(float)])!;
            var doubleResult = Assert.IsType<SqlFunctionExpression>(visitor.Translate(Expression.Call(min, floatModel, Expression.Constant(1f))));
            Assert.Equal("LEAST", doubleResult.Name);
            Assert.Same(doubleCarrier, doubleResult.TypeMapping);

            var singleCarrier = (RelationalTypeMapping)source.FindMapping(typeof(float))!
                .WithComposedConverter(new ValueConverter<decimal, float>(v => (float)v, v => (decimal)v));
            var singleModel = new SqlConstantExpression(2m, typeof(decimal), singleCarrier);
            var mixedResult = Assert.IsType<SqlFunctionExpression>(visitor.Translate(Expression.Call(max, singleModel, first)));
            Assert.Equal(OceanBaseMathTranslator.SingleResultMarker, mixedResult.Name);
            Assert.Same(singleCarrier, mixedResult.TypeMapping);
            Assert.Same(first, Native(mixedResult).Arguments![1]);

            var text = (RelationalTypeMapping)source.FindMapping(typeof(string))!
                .WithComposedConverter(new ValueConverter<decimal, string>(v => v.ToString(), v => decimal.Parse(v)));
#pragma warning disable EF1001 // Test that converters which transform null are rejected.
            var changesNull = (RelationalTypeMapping)plain.WithComposedConverter(
                new ValueConverter<decimal, decimal>(v => v, v => v, convertsNulls: true));
#pragma warning restore EF1001
            foreach (var rejected in new[] { text, changesNull, new DecimalTypeMapping(plain.StoreType),
                new DecimalTypeMapping(mode == OceanBaseCompatibilityMode.MySql ? "DATETIME" : "DATE") })
            {
                var invalid = new SqlConstantExpression(1m, typeof(decimal), rejected);
                Assert.Null(visitor.Translate(Expression.Call(max, invalid, later)));
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Cache_keys_freeze_only_numeric_extrema_values_and_preserve_other_key_parts(OceanBaseCompatibilityMode mode)
        {
            using var cache = new MemoryCache(new MemoryCacheOptions());
            using var context = CreateContext(mode, collectionMode: ParameterTranslationMode.Constant, configure: b => b.UseMemoryCache(cache));
            using var other = CreateContext(mode, collectionMode: ParameterTranslationMode.MultipleParameters, configure: b => b.UseMemoryCache(cache));
            var keys = context.GetService<ICompiledQueryCacheKeyGenerator>();
            var otherKeys = other.GetService<ICompiledQueryCacheKeyGenerator>();
            int[] values = [11, 2, 3, 4, 5, 6, 7, 8, 9];
            var call = Variadic("Least", typeof(int), Expression.Constant(values));
            var original = keys.GenerateCacheKey(call, false);
            var hash = original.GetHashCode();
            values[0] = 22;
            var changed = keys.GenerateCacheKey(call, false);
            Assert.Equal(hash, original.GetHashCode());
            Assert.Equal(hash, changed.GetHashCode());
            Assert.NotEqual(original, changed);
            Assert.Equal(changed, keys.GenerateCacheKey(Variadic("Least", typeof(int), Expression.Constant(values.ToArray())), false));
            values[0] = 11;
            Assert.Equal(original, keys.GenerateCacheKey(call, false));
            Assert.NotEqual(original, keys.GenerateCacheKey(call, true));
            Assert.Same(context.Model, other.Model);
            Assert.NotEqual(original, otherKeys.GenerateCacheKey(call, false));

            Expression<Func<Row, bool>> first = r => r.Id > 0;
            Expression<Func<Row, bool>> second = r => r.Id > 0;
            Assert.Equal(keys.GenerateCacheKey(first, false), keys.GenerateCacheKey(second, false));
            Assert.NotEqual(keys.GenerateCacheKey(first, false), otherKeys.GenerateCacheKey(first, false));
        }

        [Fact]
        public void Reexecuting_an_unrelated_query_reuses_compilation()
        {
            // Other query tests can fill EF's bounded shared cache between these two executions.
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var compilations = 0;
            using var context = CreateContext(OceanBaseCompatibilityMode.MySql, configure: b =>
                b.UseMemoryCache(cache).LogTo(_ => compilations++, [CoreEventId.QueryCompilationStarting]));
            Assert.Same(cache, context.GetService<IMemoryCache>());
            var threshold = 105;
            var query = context.Rows.Where(r => r.Id > threshold).Select(r => r.Text);
            Assert.Contains("105", query.ToQueryString());
            threshold = 701;
            Assert.Contains("701", query.ToQueryString());
            Assert.Equal(1, compilations);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        public void Extrema_compose_with_subqueries_aggregates_math_and_coalesce(OceanBaseCompatibilityMode mode, bool nulls)
        {
            using var context = CreateContext(mode, nulls);
            var query = context.Rows.Where(r => context.Rows.Any(inner => inner.Id != r.Id && EF.Functions.Greatest(inner.Optional, r.Int32) > 1))
                .GroupBy(r => r.Id).OrderBy(g => Math.Max(g.Min(r => r.Decimal), 2m))
                .Select(g => g.Sum(r => Math.Abs(EF.Functions.Least(r.Optional, 7) ?? 0)));
            var sql = query.ToQueryString();
            Assert.Contains("EXISTS (", sql);
            Assert.Contains("GREATEST(", sql);
            Assert.Contains("ORDER BY GREATEST(MIN(", sql);
            Assert.Contains("SUM(ABS(COALESCE(LEAST(", sql);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Nested_single_and_mixed_declarations_keep_operand_order_and_result_only_cast(
            OceanBaseCompatibilityMode mode, bool relationalNulls)
        {
            using var context = CreateContext(mode, relationalNulls);
            var quote = mode == OceanBaseCompatibilityMode.MySql ? '`' : '"';
            var cast = mode == OceanBaseCompatibilityMode.MySql ? "FLOAT" : "BINARY_FLOAT";
            var query = context.Rows.Where(r => MathF.Min(r.WideSingle, MathF.Min(r.Single, r.WideSingle)) > 0)
                .OrderBy(r => Math.Max(r.Decimal, Math.Max(r.AlternateDecimal, r.Decimal)));
            var sql = query.ToQueryString();
            Assert.Contains($"CAST(LEAST({Column("WideSingle")}, CAST(LEAST({Column("Single")}, {Column("WideSingle")}) AS {cast})) AS {cast})", sql);
            Assert.Contains($"GREATEST({Column("Decimal")}, GREATEST({Column("AlternateDecimal")}, {Column("Decimal")}))", sql);

            string Column(string name) => $"{quote}r{quote}.{quote}{name}{quote}";
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Variadic_shapes_translate_and_keep_independent_null_tests(
            OceanBaseCompatibilityMode mode, bool relationalNulls)
        {
            using var context = CreateContext(mode, relationalNulls);
            int?[] captured = [null, 7, -5];
            var query = context.Rows
                .Where(r => EF.Functions.Least(r.Optional, r.Int32, null) == null
                    && EF.Functions.Greatest(r.Optional, r.Int32) != null
                    && EF.Functions.Greatest(new int?[] { -4, null }) == null
                    && EF.Functions.Least(captured) == null
                    && EF.Functions.Least(8, 4, 2) < r.Id
                    && EF.Functions.Greatest(r.Optional) == r.Optional)
                .Select(r => EF.Functions.Least(r.Optional, r.Int32) ?? EF.Functions.Greatest(new[] { 2 }));
            var sql = query.ToQueryString();
            Assert.Contains("GREATEST(-4, NULL) IS NULL", sql);
            Assert.Contains("LEAST(NULL, 7, -5) IS NULL", sql);
            Assert.Contains("LEAST(8, 4, 2)", sql);
            Assert.Contains("IS NOT NULL", sql);
            Assert.Contains("COALESCE(LEAST(", sql);
            Assert.DoesNotContain("GREATEST(2)", sql);
            var singletonNull = context.Rows.Where(r => r.Optional == EF.Functions.Least(new int?[] { null })).ToQueryString();
            Assert.Contains("IS NULL", singletonNull);
            Assert.DoesNotContain("LEAST(", singletonNull);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Invalid_shapes_and_method_families_do_not_translate(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            int[]? absent = null;
            Assert.Throws<InvalidOperationException>(() => context.Rows.Where(r => EF.Functions.Least<int>() == r.Id).ToQueryString());
            Assert.Throws<InvalidOperationException>(() => context.Rows.Where(r => EF.Functions.Greatest(new int[] { }) == r.Id).ToQueryString());
            Assert.Throws<InvalidOperationException>(() => context.Rows.Where(r => EF.Functions.Least(absent!) == r.Id).ToQueryString());
            Assert.Throws<InvalidOperationException>(() => context.Rows.Where(r => EF.Functions.Least(new int[r.Id]) == r.Id).ToQueryString());
            Assert.Throws<InvalidOperationException>(() => context.Rows.Where(r => EF.Functions.Greatest(r.Text, "abc") == "xyz").ToQueryString());
            Assert.Throws<InvalidOperationException>(() => context.Rows.Where(r => EF.Functions.Least(r.Id > 0, false)).ToQueryString());
            Assert.Throws<InvalidOperationException>(() => context.Rows.Where(r => float.MinNumber(r.Single, 1f) > 0).ToQueryString());
            Assert.Throws<InvalidOperationException>(() => context.Rows.Where(r => Math.Min((nint)r.Id, 0) == 0).ToQueryString());
            Assert.Throws<InvalidOperationException>(() => context.Rows.Where(r => Min(r.Id, 0) == 0).ToQueryString());
            Assert.Null(CreateVisitor(context).Translate(Variadic("Least", typeof(int),
                new QueryParameterExpression("values", typeof(int[])))));
        }

        private static int Min(int a, int b) => Math.Min(a, b);

        private static MethodCallExpression Variadic(string name, Type type, Expression values)
            => Expression.Call(typeof(RelationalDbFunctionsExtensions), name, [type], Expression.Property(null, typeof(EF), nameof(EF.Functions)), values);

        private static RelationalSqlTranslatingExpressionVisitor CreateVisitor(TestContext context)
        {
            var compilation = context.GetService<IQueryCompilationContextFactory>().Create(async: false);
            var queryable = context.GetService<IQueryableMethodTranslatingExpressionVisitorFactory>().Create(compilation);
            return context.GetService<IRelationalSqlTranslatingExpressionVisitorFactory>().Create(compilation, queryable);
        }

        private static SqlFunctionExpression Native(SqlExpression? result)
        {
            var function = Assert.IsType<SqlFunctionExpression>(result);
            return function.Name == OceanBaseMathTranslator.SingleResultMarker
                ? Assert.IsType<SqlFunctionExpression>(Assert.Single(function.Arguments!)) : function;
        }

        private static TestContext CreateContext(OceanBaseCompatibilityMode mode, bool relationalNulls = false,
            ParameterTranslationMode? collectionMode = null, Action<DbContextOptionsBuilder<TestContext>>? configure = null)
        {
            var builder = new DbContextOptionsBuilder<TestContext>().UseOceanBase("Server=localhost;Database=test", mode);
            var extension = builder.Options.Extensions.OfType<RelationalOptionsExtension>().Single().WithUseRelationalNulls(relationalNulls);
            if (collectionMode.HasValue)
                extension = extension.WithUseParameterizedCollectionMode(collectionMode.Value);
            ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(extension);
            configure?.Invoke(builder);
            return new TestContext(builder.Options, mode);
        }

        private sealed class TestContext(DbContextOptions<TestContext> options, OceanBaseCompatibilityMode mode) : DbContext(options)
        {
            public DbSet<Row> Rows => Set<Row>();

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                var row = modelBuilder.Entity<Row>();
                row.Property(r => r.WideSingle).HasColumnType(mode == OceanBaseCompatibilityMode.MySql ? "DOUBLE" : "BINARY_DOUBLE");
                row.Property(r => r.Decimal).HasPrecision(8, 3);
                row.Property(r => r.AlternateDecimal).HasConversion(v => -2m * v, v => v / -2m).HasPrecision(13, 5);
            }
        }

        private sealed class Row
        {
            public int Id { get; set; }
            public sbyte SByte { get; set; }
            public byte Byte { get; set; }
            public short Int16 { get; set; }
            public ushort UInt16 { get; set; }
            public int Int32 { get; set; }
            public uint UInt32 { get; set; }
            public long Int64 { get; set; }
            public ulong UInt64 { get; set; }
            public float Single { get; set; }
            public double Double { get; set; }
            public decimal Decimal { get; set; }
            public float WideSingle { get; set; }
            public decimal AlternateDecimal { get; set; }
            public int? Optional { get; set; }
            public string Text { get; set; } = "";
        }
    }
}
