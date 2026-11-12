using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OceanBase.EntityFrameworkCore.Query;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests;

public sealed class CopySignQueryTests
{
    public static IEnumerable<object[]> Modes()
    {
        foreach (var mode in Enum.GetValues<OceanBaseCompatibilityMode>())
            yield return [mode];
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void CopySign_translates_exact_double_overload_and_preserves_mapping(OceanBaseCompatibilityMode mode)
    {
        using var context = CreateContext(mode);
        var source = context.GetService<IRelationalTypeMappingSource>();
        var factory = context.GetService<ISqlExpressionFactory>();
        var translator = new OceanBaseMathTranslator(factory, source, mode);
        var logger = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>();
        var mapping = source.FindMapping(typeof(double))!;
        var method = typeof(Math).GetMethod(nameof(Math.CopySign), [typeof(double), typeof(double)])!;
        var translated = Assert.IsType<SqlBinaryExpression>(translator.Translate(null, method,
            [factory.Constant(-2.5d, mapping), factory.Constant(1d, mapping)], logger));

        Assert.Equal(ExpressionType.Multiply, translated.OperatorType);
        Assert.Same(mapping, translated.TypeMapping);
        Assert.Contains("ABS(", Render(context, mode, translated));
        Assert.Contains("CASE", Render(context, mode, translated));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void CopySign_translates_float_overload_and_rejects_invalid_sign_carriers(OceanBaseCompatibilityMode mode)
    {
        using var context = CreateContext(mode);
        var source = context.GetService<IRelationalTypeMappingSource>();
        var factory = context.GetService<ISqlExpressionFactory>();
        var translator = new OceanBaseMathTranslator(factory, source, mode);
        var logger = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>();
        var mapping = source.FindMapping(typeof(float))!;
        var method = typeof(MathF).GetMethod(nameof(MathF.CopySign), [typeof(float), typeof(float)])!;
        var translated = Assert.IsType<SqlBinaryExpression>(translator.Translate(null, method,
            [factory.Constant(-2.5f, mapping), factory.Constant(1f, mapping)], logger));
        Assert.Same(mapping, translated.TypeMapping);

#pragma warning disable EF1001
        var nullConverter = new ValueConverter<float, float>(value => value, value => value, convertsNulls: true);
#pragma warning restore EF1001
        var invalid = (RelationalTypeMapping)mapping.WithComposedConverter(nullConverter);
        Assert.Null(translator.Translate(null, method,
            [factory.Constant(-2.5f, mapping), factory.Constant(1f, invalid)], logger));
        Assert.Null(translator.Translate(null, method,
            [factory.Constant(-2.5f, mapping), factory.Constant(1f, source.FindMapping(typeof(double))!)], logger));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void CopySign_preserves_first_operand_converter_and_supports_nesting(OceanBaseCompatibilityMode mode)
    {
        using var context = CreateContext(mode);
        var source = context.GetService<IRelationalTypeMappingSource>();
        var factory = context.GetService<ISqlExpressionFactory>();
        var logger = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>();
        var translator = new OceanBaseMathTranslator(factory, source, mode);
        var converter = new ValueConverter<double, double>(value => value + 10d, value => value - 10d);
        var mapping = (RelationalTypeMapping)source.FindMapping(typeof(double))!.WithComposedConverter(converter);
        var method = typeof(Math).GetMethod(nameof(Math.CopySign), [typeof(double), typeof(double)])!;
        var translated = Assert.IsType<SqlBinaryExpression>(translator.Translate(null, method,
            [factory.Constant(-2.5d, mapping), factory.Constant(1d, mapping)], logger));

        Assert.Same(mapping, translated.TypeMapping);
        Assert.Same(mapping, translated.Left.TypeMapping);

        var decimalMapping = (RelationalTypeMapping)source.FindMapping(typeof(decimal))!.WithComposedConverter(
            new ValueConverter<double, decimal>(value => (decimal)value, value => (double)value));
        var decimalResult = Assert.IsType<SqlBinaryExpression>(translator.Translate(null, method,
            [factory.Constant(-2.5d, decimalMapping), factory.Constant(1d, decimalMapping)], logger));
        Assert.Same(decimalMapping, decimalResult.TypeMapping);

        var unsignedMapping = (RelationalTypeMapping)source.FindMapping(typeof(uint))!.WithComposedConverter(
            new ValueConverter<double, uint>(value => checked((uint)value), value => value));
        Assert.Null(translator.Translate(null, method,
            [factory.Constant(2.5d, unsignedMapping), factory.Constant(1d, unsignedMapping)], logger));

        var sql = context.Rows.Select(row => Math.CopySign(
            Math.CopySign(row.Value!.Value, row.Sign!.Value), row.Sign!.Value)).ToQueryString();
        Assert.Equal(2, sql.Split("ABS(", StringSplitOptions.None).Length - 1);
        Assert.Equal(2, sql.Split("CASE", StringSplitOptions.None).Length - 1);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void CopySign_composes_in_query_and_keeps_nullable_null_tests(OceanBaseCompatibilityMode mode)
    {
        using var context = CreateContext(mode);
        var sql = context.Rows
            .Where(row => Math.CopySign(row.Value!.Value, row.Sign!.Value) > 0)
            .Select(row => (double?)Math.CopySign(row.Value!.Value, row.Sign!.Value))
            .Where(value => value == null)
            .ToQueryString();

        Assert.Contains("ABS(", sql);
        Assert.Contains("CASE", sql);
        Assert.Contains(" IS NULL", sql);
    }

    private static TestContext CreateContext(OceanBaseCompatibilityMode mode)
        => new(new DbContextOptionsBuilder<TestContext>()
            .UseOceanBase("Server=offline;Database=copy_sign", mode)
            .Options);

    private static string Render(TestContext context, OceanBaseCompatibilityMode mode, SqlExpression expression)
        => new OceanBaseQuerySqlGenerator(context.GetService<QuerySqlGeneratorDependencies>(), mode)
            .GetCommand(new ValuesExpression("v", [new RowValueExpression([expression])], ["Value"])).CommandText;

    private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
    {
        public DbSet<Row> Rows => Set<Row>();
    }

    private sealed class Row
    {
        public int Id { get; set; }
        public double? Value { get; set; }
        public double? Sign { get; set; }
    }
}
