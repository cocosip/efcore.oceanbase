using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBase.EntityFrameworkCore.Query;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests
{
    public sealed class StringFunctionQueryTests
    {
        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Unary_functions_translate_mapped_columns_in_all_query_positions(OceanBaseCompatibilityMode mode, bool relationalNulls)
        {
            using var context = CreateContext(mode, relationalNulls);
            var projection = context.Rows.Select(row => new
            {
                Lower = row.Optional!.ToLower(), Upper = row.Required.ToUpper(), Trim = row.Required.Trim(),
                Start = row.Optional!.TrimStart(), End = row.Optional!.TrimEnd(), Length = (int?)row.Optional!.Length
            }).ToQueryString();
            foreach (var function in new[] { "LOWER(", "UPPER(", "TRIM(", "LTRIM(", "RTRIM(", Length(mode) })
                Assert.Contains(function, projection);
            Assert.Contains("LOWER(", context.Rows.Where(row => row.Optional!.ToLower() == "海'_%\\").ToQueryString());
            Assert.Contains("UPPER(", context.Rows.OrderBy(row => row.Required.ToUpper()).Select(row => row.Id).ToQueryString());
            Assert.Contains("TRIM(", context.Rows.GroupBy(row => row.Required.Trim()).Select(group => new { group.Key, Count = group.Count() }).ToQueryString());
            Assert.Contains(Length(mode), context.Rows.Where(row => row.Required.Length > 2).ToQueryString());
            using var opposite = CreateContext(mode == OceanBaseCompatibilityMode.MySql ? OceanBaseCompatibilityMode.Oracle : OceanBaseCompatibilityMode.MySql);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "LENGTH(" : "CHAR_LENGTH(",
                opposite.Rows.Select(row => row.Required.Length).ToQueryString());
            Assert.Contains(Length(mode), context.Rows.Select(row => row.Required.Length).ToQueryString());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Function_result_null_tests_are_not_replaced_by_input_null_tests(OceanBaseCompatibilityMode mode, bool relationalNulls)
        {
            using var context = CreateContext(mode, relationalNulls);
            IQueryable[] queries =
            [
                context.Rows.Where(row => row.Required.Trim() == null),
                context.Rows.Where(row => row.Required.TrimStart() != null),
                context.Rows.Where(row => !(row.Required.TrimEnd() != null)),
                context.Rows.Where(row => row.Optional!.ToLower() == null),
                context.Rows.Where(row => row.Required.ToUpper() != null),
                context.Rows.Select(row => (int?)row.Optional!.Length).Where(length => length == null),
                context.Rows.Select(row => row.Required.Trim() == null),
                context.Rows.Select(row => row.Required.Trim() != null)
            ];
            foreach (var query in queries)
            {
                var sql = query.ToQueryString();
                Assert.Contains(" IS ", sql);
                Assert.DoesNotContain("WHERE 0 = 1", sql);
                Assert.True(sql.Contains("TRIM(", StringComparison.Ordinal) || sql.Contains("LOWER(", StringComparison.Ordinal)
                    || sql.Contains("UPPER(", StringComparison.Ordinal) || sql.Contains(Length(mode), StringComparison.Ordinal), sql);
            }
            var inequality = context.Rows.Where(row => row.Optional!.Trim() != "x").ToQueryString();
            Assert.Equal(!relationalNulls, inequality.Contains("IS NULL", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Function_composition_keeps_concat_case_coalesce_and_empty_parameter_semantics(OceanBaseCompatibilityMode mode, bool relationalNulls)
        {
            using var context = CreateContext(mode, relationalNulls);
            string? fallback = "present";
            var query = context.Rows.Where(row => (row.Optional!.Trim() ?? fallback) == null);
            foreach (var value in new string?[] { "present", "", null, "again", "", null })
            {
                fallback = value;
                var sql = query.ToQueryString();
                if (value is null || mode == OceanBaseCompatibilityMode.Oracle && value.Length == 0)
                {
                    Assert.Contains("TRIM(", sql);
                    Assert.Contains("IS NULL", sql);
                    Assert.DoesNotContain("COALESCE(", sql);
                }
                else Assert.EndsWith(mode == OceanBaseCompatibilityMode.MySql ? "WHERE 0" : "WHERE 0 = 1", sql);
            }
            var nested = context.Rows.Select(row => (row.Id > 0 ? row.Optional : row.Required)!.ToUpper().Trim() + (row.Optional ?? "").ToLower()).ToQueryString();
            Assert.Contains("TRIM(UPPER(CASE", nested);
            Assert.Contains("LOWER(", nested);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "CONCAT(" : " || ", nested);
            var literal = context.Rows.Where(row => (row.Optional ?? "").Trim() == null).ToQueryString();
            Assert.Contains("TRIM(", literal);
            Assert.Contains("IS NULL", literal);
            var nullLiteral = context.Rows.Select(row => (row.Optional ?? (string?)null)!.ToLower()).ToQueryString();
            Assert.Contains("LOWER(", nullLiteral);
            var whitespace = context.Rows.Select(row => (row.Optional + "\t\n\u00a0\u2003 ").Trim()).ToQueryString();
            Assert.Contains("TRIM(", whitespace);
            Assert.DoesNotContain("REPLACE(", whitespace);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Unsupported_overloads_and_unrelated_members_fail_in_predicates_but_allow_client_projection(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            IQueryable[] queries =
            [
                context.Rows.Where(row => row.Required.ToLowerInvariant() == "x"),
                context.Rows.Where(row => row.Required.ToUpperInvariant() == "x"),
                context.Rows.Where(row => row.Required.ToLower(CultureInfo.InvariantCulture) == "x"),
                context.Rows.Where(row => row.Required.ToUpper(CultureInfo.InvariantCulture) == "x"),
                context.Rows.Where(row => row.Required.Trim(' ') == "x"),
                context.Rows.Where(row => row.Required.Trim(new[] { ' ' }) == "x"),
                context.Rows.Where(row => row.Required.TrimStart(' ') == "x"),
                context.Rows.Where(row => row.Required.TrimEnd(new[] { ' ' }) == "x"),
                context.Rows.Where(row => ToLower(row.Required) == "x"),
                context.Rows.Where(row => new Impostor(row.Required).Length > 0)
            ];
            foreach (var query in queries)
                Assert.Contains("could not be translated", Assert.Throws<InvalidOperationException>(() => query.ToQueryString()).Message);
            Assert.Contains("Required", context.Rows.Select(row => row.Required.ToLowerInvariant()).ToQueryString());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Function_mappings_preserve_input_facets_and_converter_without_fixed_trim_output(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var methods = context.GetService<IMethodCallTranslatorProvider>();
            var members = context.GetService<IMemberTranslatorProvider>();
            var factory = context.GetService<ISqlExpressionFactory>();
            var logger = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>();
            foreach (var name in new[] { nameof(Row.Required), nameof(Row.Fixed), nameof(Row.National), nameof(Row.Converted) })
            {
                var mapping = (RelationalTypeMapping)context.Model.FindEntityType(typeof(Row))!.FindProperty(name)!.GetTypeMapping();
                var input = factory.Constant(" Ab !", mapping);
                foreach (var method in new[] { nameof(string.ToLower), nameof(string.ToUpper), nameof(string.Trim), nameof(string.TrimStart), nameof(string.TrimEnd) })
                {
                    var function = Assert.IsType<SqlFunctionExpression>(methods.Translate(context.Model, input,
                        typeof(string).GetMethod(method, Type.EmptyTypes)!, [], logger));
                    Assert.Same(mapping, Assert.Single(function.Arguments!).TypeMapping);
                    Assert.True(function.IsNullable);
                    Assert.All(function.ArgumentsPropagateNullability!, Assert.False);
                    Assert.Equal(mapping.IsUnicode, function.TypeMapping!.IsUnicode);
                    Assert.Same(mapping.Converter, function.TypeMapping.Converter);
                    if (method.StartsWith("Trim", StringComparison.Ordinal))
                    {
                        Assert.False(function.TypeMapping.IsFixedLength);
                        if (mode == OceanBaseCompatibilityMode.Oracle)
                            Assert.StartsWith(mapping.IsUnicode ? "NVARCHAR2" : "VARCHAR2", function.TypeMapping.StoreType);
                    }
                }
                var length = Assert.IsType<SqlFunctionExpression>(members.Translate(input, typeof(string).GetProperty(nameof(string.Length))!, typeof(int), logger));
                Assert.Equal(typeof(int), length.TypeMapping!.ClrType);
                Assert.Null(length.TypeMapping.Converter);
                Assert.Same(mapping, Assert.Single(length.Arguments!).TypeMapping);
            }
            var sql = context.Rows.Where(row => row.Converted.ToUpper() == "TARGET").ToQueryString();
            Assert.Contains("UPPER(", sql);
            Assert.Contains("TARGET!", sql);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Standard_providers_keep_plugin_and_model_function_precedence_and_scoped_lifetimes(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            using var other = CreateContext(mode);
            var factory = context.GetService<ISqlExpressionFactory>();
            var mappingSource = context.GetService<IRelationalTypeMappingSource>();
            var logger = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>();
            var plugin = new OverridePlugin(factory);
            var methods = new OceanBaseMethodCallTranslatorProvider(new RelationalMethodCallTranslatorProviderDependencies(factory, [plugin], mappingSource), mode);
            var members = new OceanBaseMemberTranslatorProvider(new RelationalMemberTranslatorProviderDependencies(factory, [plugin]), mappingSource, mode);
            Assert.Equal("plugin", Assert.IsType<SqlConstantExpression>(methods.Translate(context.Model, factory.Constant("ABC"),
                typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!, [], logger)).Value);
            Assert.Equal(19, Assert.IsType<SqlConstantExpression>(members.Translate(factory.Constant("ABC"),
                typeof(string).GetProperty(nameof(string.Length))!, typeof(int), logger)).Value);
            var modelBuilder = new ModelBuilder();
            var mapped = typeof(StringFunctionQueryTests).GetMethod(nameof(ToLower), BindingFlags.NonPublic | BindingFlags.Static)!;
            modelBuilder.HasDbFunction(mapped).HasTranslation(_ => factory.Constant("model"));
            Assert.Equal("model", Assert.IsType<SqlConstantExpression>(methods.Translate((Microsoft.EntityFrameworkCore.Metadata.IModel)modelBuilder.Model, null, mapped,
                [factory.Constant("ABC")], logger)).Value);
            Assert.IsType<OceanBaseMethodCallTranslatorProvider>(context.GetService<IMethodCallTranslatorProvider>());
            Assert.IsType<OceanBaseMemberTranslatorProvider>(context.GetService<IMemberTranslatorProvider>());
            Assert.Same(context.GetService<IMethodCallTranslatorProvider>(), context.GetService<IMethodCallTranslatorProvider>());
            Assert.NotSame(context.GetService<IMethodCallTranslatorProvider>(), other.GetService<IMethodCallTranslatorProvider>());
            Assert.NotSame(context.GetService<IMemberTranslatorProvider>(), other.GetService<IMemberTranslatorProvider>());
        }

        [Theory]
        [InlineData("clob")]
        [InlineData("nclob")]
        [InlineData(" clob ")]
        [InlineData(" nclob ")]
        public void Oracle_trim_keeps_explicit_LOB_return_family(string storeType)
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.Oracle);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(string), storeTypeName: storeType)!;
            Assert.Equal(storeType.Trim(), mapping.StoreType.Trim(), ignoreCase: true);
            var factory = context.GetService<ISqlExpressionFactory>();
            var function = Assert.IsType<SqlFunctionExpression>(context.GetService<IMethodCallTranslatorProvider>().Translate(
                context.Model, factory.Constant(" value ", mapping), typeof(string).GetMethod(nameof(string.Trim), Type.EmptyTypes)!, [],
                context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>()));
            Assert.Equal(storeType.Trim(), function.TypeMapping!.StoreType.Trim(), ignoreCase: true);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void String_to_binary_converters_are_not_treated_as_database_text(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            Assert.Throws<InvalidOperationException>(() => context.Rows.Where(row => row.BinaryConverted.ToLower() == "x").ToQueryString());
            Assert.Throws<InvalidOperationException>(() => context.Rows.Where(row => row.BinaryConverted.Length > 0).ToQueryString());
            Assert.Contains("BinaryConverted", context.Rows.Select(row => row.BinaryConverted.ToLower()).ToQueryString());
        }

        private sealed class OverridePlugin(ISqlExpressionFactory factory) : IMethodCallTranslatorPlugin, IMemberTranslatorPlugin,
            IMethodCallTranslator, IMemberTranslator
        {
            IEnumerable<IMethodCallTranslator> IMethodCallTranslatorPlugin.Translators => [this];
            IEnumerable<IMemberTranslator> IMemberTranslatorPlugin.Translators => [this];
            public SqlExpression? Translate(SqlExpression? instance, MethodInfo method, IReadOnlyList<SqlExpression> arguments,
                IDiagnosticsLogger<DbLoggerCategory.Query> logger) => factory.Constant("plugin");
            public SqlExpression? Translate(SqlExpression? instance, MemberInfo member, Type returnType,
                IDiagnosticsLogger<DbLoggerCategory.Query> logger) => factory.Constant(19);
        }

        private static string ToLower(string value) => value;
        private sealed class Impostor(string value) { public int Length => value.Length; }
        private static string Length(OceanBaseCompatibilityMode mode) => mode == OceanBaseCompatibilityMode.MySql ? "CHAR_LENGTH(" : "LENGTH(";
        private static TestContext CreateContext(OceanBaseCompatibilityMode mode, bool relationalNulls = false)
        {
            var builder = new DbContextOptionsBuilder<TestContext>().UseOceanBase("Server=localhost;Database=test", mode);
            ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(
                builder.Options.Extensions.OfType<RelationalOptionsExtension>().Single().WithUseRelationalNulls(relationalNulls));
            return new TestContext(builder.Options);
        }
        private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
        {
            public DbSet<Row> Rows => Set<Row>();
            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity<Row>().Property(row => row.Required).IsUnicode(false).HasMaxLength(12);
                modelBuilder.Entity<Row>().Property(row => row.Fixed).IsUnicode(false).IsFixedLength().HasMaxLength(8);
                modelBuilder.Entity<Row>().Property(row => row.National).IsUnicode().IsFixedLength().HasMaxLength(8);
                modelBuilder.Entity<Row>().Property(row => row.Converted).HasConversion(value => value + "!", value => value.Substring(0, value.Length - 1)).HasMaxLength(16);
                modelBuilder.Entity<Row>().Property(row => row.BinaryConverted).HasConversion(value => Encoding.UTF8.GetBytes(value), value => Encoding.UTF8.GetString(value)).HasMaxLength(16);
            }
        }
        private sealed class Row
        {
            public int Id { get; set; }
            public string Required { get; set; } = "";
            public string? Optional { get; set; }
            public string Fixed { get; set; } = "";
            public string National { get; set; } = "";
            public string Converted { get; set; } = "";
            public string BinaryConverted { get; set; } = "";
        }
    }
}
