using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OceanBase.EntityFrameworkCore.Storage;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests
{
    public sealed class StringMappingTests
    {
        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Scalar_json_uses_native_binding_and_rejects_incompatible_facets(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var source = Source(context);
            var mapping = source.FindMapping(typeof(string), "JSON");
            Assert.NotNull(mapping);
            Assert.Equal(DbType.String, mapping.DbType);
            using var command = new OceanBaseCommand();
            var document = "{\"text\":\"海🌊\",\"null\":null}";
            var parameter = Assert.IsType<OceanBaseParameter>(mapping.CreateParameter(command, "json", document));
            Assert.Equal(OceanBaseDbType.Json, parameter.OceanBaseDbType);
            Assert.Equal(document, parameter.Value);
            Assert.Equal("null", mapping.CreateParameter(command, "jsonNull", "null").Value);
            Assert.Equal(DBNull.Value, mapping.CreateParameter(command, "sqlNull", null).Value);
            var clone = mapping.WithStoreTypeAndSize("JSON", null);
            Assert.Equal(OceanBaseDbType.Json, Assert.IsType<OceanBaseParameter>(clone.CreateParameter(command, "clone", document)).OceanBaseDbType);
            Assert.Null(source.FindMapping(typeof(List<int>), "JSON"));
            Assert.Throws<ArgumentException>(() => source.FindMapping(typeof(string), "JSON", precision: 1));
            Assert.Throws<ArgumentException>(() => source.FindMapping(typeof(string), "JSON", scale: 1));
            Assert.Throws<ArgumentException>(() => source.FindMapping(typeof(string), "JSON(50)"));
            Assert.Throws<ArgumentException>(() => source.FindMapping(typeof(string), "JSON", size: 50));
            Assert.Throws<ArgumentException>(() => source.FindMapping(typeof(string), "JSON", fixedLength: true));
            Assert.Throws<ArgumentException>(() => source.FindMapping(typeof(string), "JSON", unicode: false));
            Assert.Throws<ArgumentException>(() => source.FindMapping(typeof(string), "JSON", keyOrIndex: true));

        }

        [Theory]
        [InlineData("CHAR(32)")]
        [InlineData("VARCHAR(32)")]
        [InlineData("TINYTEXT")]
        [InlineData("TEXT")]
        [InlineData("MEDIUMTEXT")]
        [InlineData("LONGTEXT")]
        public void MySql_control_text_uses_a_character_literal_valid_in_column_defaults(string declaration)
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.MySql);
            var mapping = Source(context).FindMapping(typeof(string), declaration)!;
            foreach (var text in new[] { "a\\'b", "海🌊\r\n", "\t\0", "' OR 1=1--\n" })
                Assert.Equal("_utf8mb4 X'" + Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(text)) + "'",
                    mapping.GenerateSqlLiteral(text));
            Assert.Equal("NULL", mapping.GenerateSqlLiteral(null));
            Assert.Equal("''", mapping.GenerateSqlLiteral(""));
            Assert.Equal("'Ready''s'", mapping.GenerateSqlLiteral("Ready's"));
            Assert.Throws<ArgumentException>(() => mapping.GenerateSqlLiteral("\ud800\n"));
            var converted = (RelationalTypeMapping)mapping.WithComposedConverter(
                new ValueConverter<Caption, string>(value => value.Value + "\n", value => new Caption(value)));
            Assert.Equal("_utf8mb4 X'610A'", converted.GenerateSqlLiteral(new Caption("a")));
        }

        public static IEnumerable<object?[]> NationalLiteralCases()
        {
            foreach (var declaration in new[] { "NCHAR(20)", "NVARCHAR2(20)", "NCLOB" })
            foreach (var (value, sql) in new (string?, string)[]
            {
                (null, "NULL"), ("", "N''"), ("Ready's", "N'Ready''s'"),
                ("海", @"u'\6d77'"), ("海🌊", @"u'\6d77\d83c\df0a'"),
                (@"\6d77", @"u'\005c\0036\0064\0037\0037'"),
                ("' OR 1=1--\n\0", @"u'\0027\0020\004f\0052\0020\0031\003d\0031\002d\002d\000a\0000'"),
                ("\u007f\u0085", @"u'\007f\0085'")
            }) yield return [declaration, value, sql];
        }

        [Theory]
        [MemberData(nameof(NationalLiteralCases))]
        public void Oracle_national_literals_preserve_text_without_client_encoding(
            string declaration, string? value, string expected)
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.Oracle);
            var mapping = Source(context).FindMapping(typeof(string), declaration)!;
            Assert.Equal(expected, mapping.GenerateSqlLiteral(value));
        }

        [Theory]
        [InlineData("CHAR(20)")]
        [InlineData("VARCHAR2(20)")]
        [InlineData("CLOB")]
        public void Oracle_ordinary_literals_without_newlines_keep_their_character_set_contract(string declaration)
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.Oracle);
            var mapping = Source(context).FindMapping(typeof(string), declaration)!;
            Assert.Equal("'海🌊\\'''", mapping.GenerateSqlLiteral("海🌊\\'"));
        }

        public static IEnumerable<object?[]> OrdinaryNewlineCases()
        {
            foreach (var declaration in new[] { "CHAR(20 CHAR)", "VARCHAR2(20 CHAR)", "CLOB" })
            foreach (var (value, sql) in new (string?, string)[]
            {
                (null, "NULL"), ("", "''"), ("a'\\b", "'a''\\b'"),
                ("\n", "CAST(CHR(10) AS CHAR(1))"),
                ("a\rb", "(('a' || CAST(CHR(13) AS CHAR(1))) || 'b')"),
                ("a\r\nb", "(('a' || CAST(CHR(13) AS CHAR(1))) || (CAST(CHR(10) AS CHAR(1)) || 'b'))"),
                ("a'\\\nb", @"(('a''\' || CAST(CHR(10) AS CHAR(1))) || 'b')"),
                ("海🌊\n", "('海🌊' || CAST(CHR(10) AS CHAR(1)))"),
                ("\r\n\t\0", "((CAST(CHR(13) AS CHAR(1)) || CAST(CHR(10) AS CHAR(1))) || '\t\0')"),
                ("\n\r\n", "((CAST(CHR(10) AS CHAR(1)) || CAST(CHR(13) AS CHAR(1))) || CAST(CHR(10) AS CHAR(1)))"),
                ("a\nb\nc", "((('a' || CAST(CHR(10) AS CHAR(1))) || ('b' || CAST(CHR(10) AS CHAR(1)))) || 'c')")
            }) yield return [declaration, value, sql];
        }

        [Theory]
        [MemberData(nameof(OrdinaryNewlineCases))]
        public void Oracle_ordinary_newline_literals_preserve_CHAR_operands(string declaration, string? value, string expected)
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.Oracle);
            var mapping = Source(context).FindMapping(typeof(string), declaration)!;
            Assert.Equal(expected, mapping.GenerateSqlLiteral(value));
        }

        [Fact]
        public void Oracle_ordinary_newline_literals_survive_clones_and_converters_without_changing_parameters()
        {
            using var context = CreateContext(OceanBaseCompatibilityMode.Oracle);
            var mapping = Source(context).FindMapping(typeof(string), "VARCHAR2(2 CHAR)")!;
            var converted = (RelationalTypeMapping)mapping.WithStoreTypeAndSize("VARCHAR2(4 CHAR)", 4)
                .WithComposedConverter(new ValueConverter<Caption, string>(value => value.Value + "\n", value => new Caption(value)));
            Assert.Equal("('海🌊' || CAST(CHR(10) AS CHAR(1)))", converted.GenerateSqlLiteral(new Caption("海🌊")));
            using var command = new OceanBaseCommand();
            var parameter = converted.CreateParameter(command, "text", new Caption("海🌊"));
            Assert.Equal("海🌊\n", parameter.Value);
            Assert.Equal(DbType.AnsiString, parameter.DbType);
            Assert.Null(((OceanBaseParameter)parameter).OceanBaseDbType);
            Assert.Throws<ArgumentException>(() => converted.GenerateSqlLiteral(new Caption("\ud800")));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, "CHAR", 1, true, true)]
        [InlineData(OceanBaseCompatibilityMode.MySql, " char ( 4 ) ", 4, true, true)]
        [InlineData(OceanBaseCompatibilityMode.MySql, "VARCHAR(16383)", 16383, false, true)]
        [InlineData(OceanBaseCompatibilityMode.MySql, "TEXT", null, false, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NCHAR", 1, true, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "CHAR", 1, true, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, " varchar2 ( 4 byte ) ", 4, false, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "CHAR(2000 CHAR)", 2000, true, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NCHAR(1000)", 1000, true, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NVARCHAR2(2000)", 2000, false, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "CLOB", null, false, false)]
        public void Full_declarations_preserve_normalized_facets(OceanBaseCompatibilityMode mode,
            string declaration, int? size, bool fixedLength, bool unicode)
        {
            using var context = CreateContext(mode);
            var mapping = Source(context).FindMapping(typeof(string), declaration)!;
            Assert.Equal(declaration.Trim(), mapping.StoreType);
            Assert.Equal(size, mapping.Size);
            Assert.Equal(fixedLength, mapping.IsFixedLength);
            Assert.Equal(unicode, mapping.IsUnicode);
            using var command = new OceanBaseCommand();
            Assert.Equal(size ?? 0, mapping.CreateParameter(command, "text", null, direction: ParameterDirection.Output).Size);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, "VARCHAR")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "VARCHAR(0)")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "VARCHAR(16384)")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "CHAR(256)")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "CHAR(4) ignored")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "VARCHAR(4,2)")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "VARCHAR(4 CHAR)")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "TEXT(4)")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "NVARCHAR2(4)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "VARCHAR2")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "VARCHAR2(4001)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NVARCHAR2(2001)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NCHAR(1001)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "CHAR(2001)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "CHAR(-1)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "VARCHAR2(9999999999999999999)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "VARCHAR2(4 FOO)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NVARCHAR2(4 CHAR)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "NCLOB(4)")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "VARCHAR(4)")]
        public void Invalid_declarations_reject_instead_of_silently_mapping(OceanBaseCompatibilityMode mode, string declaration)
        {
            using var context = CreateContext(mode);
            Assert.ThrowsAny<ArgumentException>(() => Source(context).FindMapping(typeof(string), declaration));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Conflicting_facets_and_lob_indexes_reject_on_direct_and_property_paths(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var source = Source(context);
            var declaration = mode == OceanBaseCompatibilityMode.Oracle ? "NVARCHAR2(4)" : "VARCHAR(4)";
            Assert.ThrowsAny<ArgumentException>(() => source.FindMapping(typeof(string), declaration, fixedLength: true));
            Assert.ThrowsAny<ArgumentException>(() => source.FindMapping(typeof(string), declaration, size: 3));
            Assert.ThrowsAny<ArgumentException>(() => source.FindMapping(typeof(string), declaration, precision: 4));
            Assert.ThrowsAny<ArgumentException>(() => source.FindMapping(typeof(string), declaration, scale: 1));
            Assert.ThrowsAny<ArgumentException>(() => source.FindMapping(typeof(string), "CHAR", size: 4));
            Assert.ThrowsAny<ArgumentException>(() => source.FindMapping(typeof(string),
                mode == OceanBaseCompatibilityMode.Oracle ? " NCLOB " : " LONGTEXT ", keyOrIndex: true));
            Assert.ThrowsAny<ArgumentException>(() => source.FindMapping(typeof(string), null, size: 20000, keyOrIndex: true));
            var builder = new ModelBuilder(new ConventionSet());
            var entity = builder.Entity<TextRow>();
            entity.Property(row => row.Text).HasColumnType(declaration).IsFixedLength();
            Assert.ThrowsAny<ArgumentException>(() => source.FindMapping((IProperty)entity.Metadata.FindProperty(nameof(TextRow.Text))!));
            if (mode == OceanBaseCompatibilityMode.Oracle)
            {
                Assert.ThrowsAny<ArgumentException>(() => source.FindMapping(typeof(string), declaration, unicode: false));
                entity.Property(row => row.Text).IsFixedLength(false).IsUnicode(false);
                Assert.ThrowsAny<ArgumentException>(() => source.FindMapping((IProperty)entity.Metadata.FindProperty(nameof(TextRow.Text))!));
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Converted_writes_count_characters_while_queries_keep_lossless_transport(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var mapping = Source(context).FindMapping(typeof(string), null, size: 2)!;
            var validation = Assert.IsAssignableFrom<IOceanBaseWriteValidation>(mapping);
            foreach (var value in new object?[] { null, DBNull.Value, "", "海🌊", "a\u0301" }) validation.ValidateWriteValue(value);
            Assert.Throws<ArgumentException>(() => validation.ValidateWriteValue("海🌊洋"));
            using var command = new OceanBaseCommand();
            var parameter = mapping.CreateParameter(command, "text", "longer🌊query");
            Assert.Equal("longer🌊query", parameter.Value);
            Assert.Equal("longer🌊query".Length, parameter.Size);
            Assert.Equal(mode == OceanBaseCompatibilityMode.Oracle
                ? @"u'\006c\006f\006e\0067\0065\0072\d83c\df0a\0071\0075\0065\0072\0079'"
                : "'longer🌊query'", mapping.GenerateSqlLiteral("longer🌊query"));
            var converted = (RelationalTypeMapping)mapping.WithStoreTypeAndSize(
                mode == OceanBaseCompatibilityMode.Oracle ? "NVARCHAR2(4)" : "VARCHAR(4)", 4)
                .WithComposedConverter(new ValueConverter<Caption, string>(value => value.Value + "!", value => new Caption(value)));
            var convertedValidation = Assert.IsAssignableFrom<IOceanBaseWriteValidation>(converted);
            convertedValidation.ValidateWriteValue(new Caption("海🌊洋"));
            Assert.Throws<ArgumentException>(() => convertedValidation.ValidateWriteValue(new Caption("海🌊洋河")));
            Assert.Equal("海🌊洋!", converted.CreateParameter(command, "text", new Caption("海🌊洋")).Value);
            Assert.Equal(mode == OceanBaseCompatibilityMode.Oracle
                ? @"u'\6d77\d83c\df0a\6d0b\0021'" : "'海🌊洋!'",
                converted.GenerateSqlLiteral(new Caption("海🌊洋")));
            Assert.ThrowsAny<ArgumentException>(() => mapping.WithPrecisionAndScale(3, null));
            Assert.ThrowsAny<ArgumentException>(() => mapping.WithStoreTypeAndSize("CHAR(2) ignored", 2));
            var lob = Source(context).FindMapping(typeof(string), mode == OceanBaseCompatibilityMode.Oracle ? "NCLOB" : "LONGTEXT", size: 2)!;
            Assert.Throws<ArgumentException>(() => ((IOceanBaseWriteValidation)lob).ValidateWriteValue("abc"));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Invalid_unicode_never_becomes_replacement_text(OceanBaseCompatibilityMode mode)
        {
            using var context = CreateContext(mode);
            var mapping = Source(context).FindMapping(typeof(string), null, size: 4)!;
            using var command = new OceanBaseCommand();
            foreach (var value in new[] { "\ud800", "x\udc00", "\ud800x", "\\\udfff" })
            {
                Assert.Throws<ArgumentException>(() => mapping.CreateParameter(command, "text", value));
                Assert.Throws<ArgumentException>(() => mapping.GenerateSqlLiteral(value));
                Assert.Throws<ArgumentException>(() => ((IOceanBaseWriteValidation)mapping).ValidateWriteValue(value));
            }
        }

        private static IRelationalTypeMappingSource Source(DbContext context) => context.GetService<IRelationalTypeMappingSource>();
        private static TestContext CreateContext(OceanBaseCompatibilityMode mode)
            => new(new DbContextOptionsBuilder<TestContext>().UseOceanBase("Server=localhost;Database=test", mode).Options);
        private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options);
        private sealed class TextRow { public string Text { get; set; } = string.Empty; }
        private sealed record Caption(string Value);
    }
}
