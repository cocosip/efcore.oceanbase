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

        public static IEnumerable<object[]> ExactNumberMethods()
        {
            yield return [typeof(float), nameof(float.MinNumber), typeof(float), "Single", "LEAST"];
            yield return [typeof(float), nameof(float.MaxNumber), typeof(float), "Single", "GREATEST"];
            yield return [typeof(double), nameof(double.MinNumber), typeof(double), "Double", "LEAST"];
            yield return [typeof(double), nameof(double.MaxNumber), typeof(double), "Double", "GREATEST"];
        }

        [Theory]
        [MemberData(nameof(ExactNumberMethods))]
        public void Exact_number_methods_translate_in_server_predicates(
            Type owner, string name, Type type, string property, string function)
        {
            foreach (var mode in Enum.GetValues<OceanBaseCompatibilityMode>())
            {
                using var context = CreateContext(mode);
                var method = owner.GetMethod(name, [type, type])!;
                var row = Expression.Parameter(typeof(Row), "row");
                var call = Expression.Call(method, Expression.Property(row, property), Expression.Default(type));
                var sql = context.Rows.Where(Expression.Lambda<Func<Row, bool>>(
                    Expression.Equal(call, Expression.Default(type)), row)).ToQueryString();
                Assert.Contains(function + "(", sql);
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
        public void First_mapping_owns_result_and_preserves_independent_operand_converters(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var visitor = CreateVisitor(context);
            var source = context.GetService<IRelationalTypeMappingSource>();
            var entity = context.Model.FindEntityType(typeof(Row))!;
            var plain = entity.FindProperty(nameof(Row.Decimal))!.GetRelationalTypeMapping();
            var reversed = entity.FindProperty(nameof(Row.AlternateDecimal))!.GetRelationalTypeMapping();
            var first = new SqlConstantExpression(Expression.Constant(2m), reversed);
            var later = new SqlConstantExpression(Expression.Constant(3m), plain);
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

            var doubleCarrier = (RelationalTypeMapping)source.FindMapping(typeof(double))!
                .WithComposedConverter(new ValueConverter<float, double>(v => v, v => (float)v));
            var floatModel = new SqlConstantExpression(Expression.Constant(2f), doubleCarrier);
            var min = typeof(Math).GetMethod(nameof(Math.Min), [typeof(float), typeof(float)])!;
            var doubleResult = Assert.IsType<SqlFunctionExpression>(visitor.Translate(Expression.Call(min, floatModel, Expression.Constant(1f))));
            Assert.Equal("LEAST", doubleResult.Name);
            Assert.Same(doubleCarrier, doubleResult.TypeMapping);

            var singleCarrier = (RelationalTypeMapping)source.FindMapping(typeof(float))!
                .WithComposedConverter(new ValueConverter<decimal, float>(v => (float)v, v => (decimal)v));
            var singleModel = new SqlConstantExpression(Expression.Constant(2m), singleCarrier);
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
                var invalid = new SqlConstantExpression(Expression.Constant(1m), rejected);
                Assert.Null(visitor.Translate(Expression.Call(max, invalid, later)));
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Cache_keys_preserve_async_and_relational_null_semantics(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            using var other = CreateContext(mode, relationalNulls: true);
            var keys = context.GetService<ICompiledQueryCacheKeyGenerator>();
            var otherKeys = other.GetService<ICompiledQueryCacheKeyGenerator>();
            Assert.Same(context.Model, other.Model);

            Expression<Func<Row, bool>> first = r => r.Id > 0;
            Expression<Func<Row, bool>> second = r => r.Id > 0;
            Assert.Equal(keys.GenerateCacheKey(first, false), keys.GenerateCacheKey(second, false));
            Assert.NotEqual(keys.GenerateCacheKey(first, false), keys.GenerateCacheKey(first, true));
            Assert.NotEqual(keys.GenerateCacheKey(first, false), otherKeys.GenerateCacheKey(first, false));
        }

        [Fact]
        public void Reexecuting_an_unrelated_query_reuses_compilation()
        {
            // Other query tests can fill EF's bounded shared cache between these two executions.
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var compilations = 0;
            using var context = CreateContext(OceanBaseCompatibilityMode.MySql, configure: b =>
                b.EnableServiceProviderCaching(false).UseMemoryCache(cache).LogTo(_ => compilations++, [CoreEventId.QueryCompilationStarting]));
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
            var query = context.Rows.Where(r => context.Rows.Any(inner => inner.Id != r.Id && Math.Max(inner.Optional ?? 0, r.Int32) > 1))
                .GroupBy(r => r.Id).OrderBy(g => Math.Max(g.Min(r => r.Decimal), 2m))
                .Select(g => g.Sum(r => Math.Abs(Math.Min(r.Optional ?? 0, 7))));
            var sql = query.ToQueryString();
            Assert.Contains("EXISTS (", sql);
            Assert.Contains("GREATEST(", sql);
            Assert.Contains("ORDER BY GREATEST(MIN(", sql);
            Assert.Contains("SUM(ABS(LEAST(COALESCE(", sql);
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
            var inner = Normalize($"LEAST({Column("Single")}, {Column("WideSingle")})");
            Assert.Contains(Normalize($"LEAST({Column("WideSingle")}, {inner})"), sql);
            Assert.Contains($"GREATEST({Column("Decimal")}, GREATEST({Column("AlternateDecimal")}, {Column("Decimal")}))", sql);

            string Column(string name) => $"{quote}r{quote}.{quote}{name}{quote}";
            string Normalize(string value) => mode == OceanBaseCompatibilityMode.MySql
                ? $"CAST(CAST({value} AS FLOAT) AS DOUBLE)" : $"CAST({value} AS {cast})";
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Invalid_shapes_and_method_families_do_not_translate(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            Assert.Throws<InvalidOperationException>(() => context.Rows.Where(r => Math.Min((nint)r.Id, 0) == 0).ToQueryString());
            Assert.Throws<InvalidOperationException>(() => context.Rows.Where(r => Min(r.Id, 0) == 0).ToQueryString());
        }

        private static int Min(int a, int b) => Math.Min(a, b);

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

        private static TestContext CreateContext(OceanBaseCompatibilityMode mode, bool relationalNulls = false, Action<DbContextOptionsBuilder<TestContext>>? configure = null)
        {
            var builder = new DbContextOptionsBuilder<TestContext>().UseOceanBase("Server=localhost;Database=test", mode);
            var extension = builder.Options.Extensions.OfType<RelationalOptionsExtension>().Single().WithUseRelationalNulls(relationalNulls);
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
