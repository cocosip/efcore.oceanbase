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

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Pad_translates_all_four_signatures_without_truncation(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var width = 8;
            var token = '海';
            var query = context.Rows.Where(row => row.Required.PadLeft(width).PadRight(12, token) == "body"
                || row.Optional!.PadRight(width).PadLeft(2, row.Character!.Value) == "other");
            var sql = query.ToQueryString();
            Assert.Contains("PAD(", sql);
            Assert.Contains(Length(mode), sql);
            Assert.Contains("CASE", sql);
            Assert.Throws<InvalidOperationException>(() => context.Rows.Where(row =>
                row.Required.PadLeft(width, row.NumericCharacter) == "body").ToQueryString());
            Assert.Throws<InvalidOperationException>(() => context.Rows.Where(row =>
                row.BinaryConverted.PadRight(width) == "body").ToQueryString());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, "CHAR(8)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NCHAR(8)")]
        public void Pad_returns_variable_text_for_fixed_receivers(OceanBaseCompatibilityMode mode, string storeType)
        {
            using var context = CreateContext(mode);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(string), storeTypeName: storeType)!;
            Assert.True(mapping.IsFixedLength);
            var factory = context.GetService<ISqlExpressionFactory>();
            var padded = context.GetService<IMethodCallTranslatorProvider>().Translate(context.Model, factory.Constant("x", mapping),
                typeof(string).GetMethod(nameof(string.PadRight), [typeof(int)])!, [factory.Constant(1)],
                context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>());
            var resultMapping = Assert.IsType<CaseExpression>(padded).TypeMapping!;
            Assert.False(resultMapping.IsFixedLength);
            Assert.Equal(mapping.IsUnicode, resultMapping.IsUnicode);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Insert_uses_guarded_native_character_offsets(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var start = 1;
            string? inserted = "海";
            var query = context.Rows.Where(row => row.Optional!.Insert(start, inserted!) == "body");
            var sql = query.ToQueryString();
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "SUBSTRING(" : "SUBSTR(", sql);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "CONCAT(" : " || ", sql);
            Assert.Contains("CASE", sql);
            Assert.DoesNotContain("__oceanbase_internal", sql);
            inserted = "";
            Assert.DoesNotContain("__oceanbase_internal", query.ToQueryString());
            Assert.Throws<InvalidOperationException>(() => context.Rows.Where(row =>
                row.BinaryConverted.Insert(start, "x") == "body").ToQueryString());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Replace_translates_ordinary_signatures_and_guards_search_tokens(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var oldCharacter = 'x';
            var sql = context.Rows.Where(row => row.Required.Replace(oldCharacter, 'y').Replace("yy", null) == "body")
                .ToQueryString();
            Assert.Contains("REPLACE(", sql);
            Assert.Contains(Length(mode), sql);
            Assert.Contains("CASE", sql);
            Assert.Throws<InvalidOperationException>(() => context.Rows.Where(row =>
                row.Required.Replace("x", "y", StringComparison.OrdinalIgnoreCase) == "body").ToQueryString());
            Assert.Throws<InvalidOperationException>(() => context.Rows.Where(row =>
                row.BinaryConverted.Replace("x", "y") == "body").ToQueryString());
            Assert.Throws<InvalidOperationException>(() => context.Rows.Where(row =>
                row.Required.Replace(row.NumericCharacter, 'x') == "body").ToQueryString());
            Assert.Throws<InvalidOperationException>(() => context.Rows.Where(row =>
                row.Required.Replace('x', row.ExpandedCharacter) == "body").ToQueryString());
        }

        [Theory]
        [InlineData("TEXT")]
        [InlineData("MEDIUMTEXT")]
        [InlineData("LONGTEXT")]
        public void Replace_keeps_explicit_MySql_lob_mapping_with_size_metadata(string storeType)
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.MySql);
            var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(string), storeTypeName: storeType, size: 64)!;
            var factory = context.GetService<ISqlExpressionFactory>();
            var replaced = context.GetService<IMethodCallTranslatorProvider>().Translate(context.Model, factory.Constant("x", mapping),
                typeof(string).GetMethod(nameof(string.Replace), [typeof(string), typeof(string)])!, [factory.Constant("x"), factory.Constant("expanded")],
                context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>());
            Assert.Equal(mapping.StoreType, Assert.IsType<CaseExpression>(replaced).TypeMapping!.StoreType);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Explicit_trim_translates_six_signatures_and_keeps_null_results(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            char[]? characters = ['x', 'y'];
            var character = 'x';
            IQueryable[] queries =
            [
                context.Rows.Where(row => row.Required.Trim('x') == "body"),
                context.Rows.Where(row => row.Required.TrimStart(character) == "body"),
                context.Rows.Where(row => row.Required.TrimEnd('x') == "body"),
                context.Rows.Where(row => row.Required.Trim(new[] { 'x', 'y' }) == "body"),
                context.Rows.Where(row => row.Required.TrimStart(characters) == "body"),
                context.Rows.Where(row => row.Required.TrimEnd(characters) == "body"),
                context.Rows.Where(row => row.Required.Trim((char[]?)null) == null),
                context.Rows.Where(row => row.Required.Trim(Array.Empty<char>()) == null)
            ];
            foreach (var query in queries)
            {
                var sql = query.ToQueryString();
                Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "REGEXP_REPLACE(" : "TRIM(", sql);
                Assert.DoesNotContain("WHERE 0 = 1", sql);
            }
            characters = [];
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "REGEXP_REPLACE(" : "LTRIM(", queries[4].ToQueryString());
            Assert.Throws<InvalidOperationException>(() => context.Rows.Where(row => row.BinaryConverted.Trim('x') == "body").ToQueryString());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Explicit_trim_columns_and_constants_preserve_text_mapping_and_reject_invalid_carriers(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var columns = context.Rows.Where(row => row.Required.Trim(row.Character!.Value) == "body"
                && row.National.TrimStart(new[] { row.Character.Value, ' ' }) == "body").ToQueryString();
            Assert.Contains("Character", columns);
            Assert.DoesNotContain(" AS longtext)", columns, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("COALESCE(", columns);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "REGEXP_REPLACE(" : "LTRIM(", columns);
            var source = "xxbodyxx";
            Assert.Contains("Character", context.Rows.Where(row => "xxbodyxx".Trim(new[] { row.Character!.Value }) == "body").ToQueryString());
            Assert.Contains("Character", context.Rows.Where(row => source.TrimStart(new[] { row.Character!.Value }) == "bodyxx").ToQueryString());
            Assert.Throws<InvalidOperationException>(() => context.Rows.Where(row => row.Required.Trim(row.NumericCharacter) == "body").ToQueryString());
            Assert.Throws<InvalidOperationException>(() => context.Rows.Where(row => row.Required.Trim(row.ExpandedCharacter) == "body").ToQueryString());
            foreach (var name in new[] { nameof(Row.Fixed), nameof(Row.National), nameof(Row.Converted) })
            {
                var mapping = (RelationalTypeMapping)context.Model.FindEntityType(typeof(Row))!.FindProperty(name)!.GetTypeMapping();
                var factory = context.GetService<ISqlExpressionFactory>();
                var result = context.GetService<IMethodCallTranslatorProvider>().Translate(context.Model, factory.Constant("xxbody!", mapping),
                    typeof(string).GetMethod(nameof(string.TrimStart), [typeof(char)])!, [factory.Constant('x')],
                    context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>())!;
                Assert.False(result.TypeMapping!.IsFixedLength);
                Assert.Same(mapping.Converter, result.TypeMapping.Converter);
                Assert.Equal(mapping.IsUnicode, result.TypeMapping.IsUnicode);
            }
            var first = context.Rows.Where(row => row.Required.Trim(new[] { 'x' }) == "body").ToQueryString();
            var second = context.Rows.Where(row => row.Required.Trim(new[] { 'y' }) == "body").ToQueryString();
            Assert.DoesNotContain("_ob_trim_", first);
            Assert.NotEqual(first, second);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Explicit_trim_array_keeps_plugin_precedence_for_nontext_receivers(OceanBaseCompatibilityMode mode)
        {
            var options = new DbContextOptionsBuilder<TestContext>().UseOceanBase("Server=localhost;Database=test", mode)
                .ReplaceService<IMethodCallTranslatorProvider, PluginMethodProvider>().Options;
            using var context = new TestContext(options);
            char[] characters = ['x'];
            var sql = context.Rows.Where(row => row.BinaryConverted.Trim(characters) == "plugin").Select(row => row.Id).ToQueryString();
            Assert.DoesNotContain("BinaryConverted", sql);
        }

        private sealed class PluginMethodProvider : RelationalMethodCallTranslatorProvider
        {
            public PluginMethodProvider(RelationalMethodCallTranslatorProviderDependencies dependencies)
                : base(new RelationalMethodCallTranslatorProviderDependencies(dependencies.SqlExpressionFactory,
                    [new OverridePlugin(dependencies.SqlExpressionFactory)], dependencies.RelationalTypeMappingSource)) { }
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
                modelBuilder.Entity<Row>().Property(row => row.Character).HasConversion<string>().HasMaxLength(1);
                modelBuilder.Entity<Row>().Property(row => row.NumericCharacter).HasConversion<int>();
                modelBuilder.Entity<Row>().Property(row => row.ExpandedCharacter).HasConversion(value => value + "!", value => value[0]).HasMaxLength(2);
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
            public char? Character { get; set; }
            public char NumericCharacter { get; set; }
            public char ExpandedCharacter { get; set; }
        }
    }
}
