using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace OceanBase.EntityFrameworkCore.Query
{
    internal sealed class OceanBaseStringIndexOfTranslator(
        ISqlExpressionFactory factory, IRelationalTypeMappingSource mappings, OceanBaseCompatibilityMode mode)
        : IMethodCallTranslator
    {
        internal const string OracleMarker = "__oceanbase_internal_string_indexof";
        private static readonly HashSet<MethodInfo> Methods =
        [
            typeof(string).GetMethod(nameof(string.IndexOf), [typeof(string)])!,
            typeof(string).GetMethod(nameof(string.IndexOf), [typeof(string), typeof(int)])!,
            typeof(string).GetMethod(nameof(string.IndexOf), [typeof(string), typeof(int), typeof(int)])!,
            typeof(string).GetMethod(nameof(string.IndexOf), [typeof(char)])!,
            typeof(string).GetMethod(nameof(string.IndexOf), [typeof(char), typeof(int)])!,
            typeof(string).GetMethod(nameof(string.IndexOf), [typeof(char), typeof(int), typeof(int)])!
        ];

        public SqlExpression? Translate(SqlExpression? instance, MethodInfo method, IReadOnlyList<SqlExpression> arguments,
            IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        {
            if (instance is null || !Methods.Contains(method)) return null;
            instance = factory.ApplyDefaultTypeMapping(instance);
            var text = instance.TypeMapping!;
            if (text.Converter?.ProviderClrType is { } carrier && carrier != typeof(string)) return null;
            var needle = factory.ApplyTypeMapping(arguments[0], arguments[0].Type == typeof(char)
                ? (RelationalTypeMapping)text.WithComposedConverter(new CharToStringConverter()) : text);
            if (needle.TypeMapping?.Converter?.ProviderClrType is { } needleCarrier && needleCarrier != typeof(string)) return null;
            var integer = mappings.FindMapping(typeof(int))!;
            var wide = mappings.FindMapping(typeof(long))!;
            var start = arguments.Count < 2 ? factory.Constant(0L, wide)
                : factory.Convert(factory.ApplyDefaultTypeMapping(arguments[1]), typeof(long), wide);
            var count = arguments.Count == 3 ? factory.Convert(factory.ApplyDefaultTypeMapping(arguments[2]), typeof(long), wide)
                : factory.Subtract(Length(factory, mode, instance, wide), start, wide);
            return mode == OceanBaseCompatibilityMode.Oracle
                ? factory.Function(OracleMarker, [instance, needle, start, count], nullable: true,
                    argumentsPropagateNullability: [false, false, false, false], typeof(int), integer)
                : Build(factory, mode, instance, needle, start, count, integer, false);
        }

        internal static SqlExpression Build(ISqlExpressionFactory factory, OceanBaseCompatibilityMode mode,
            SqlExpression source, SqlExpression needle, SqlExpression start, SqlExpression count,
            RelationalTypeMapping integer, bool knownEmpty)
        {
            var wide = start.TypeMapping!;
            var zero = factory.Constant(0L, wide);
            var one = factory.Constant(1L, wide);
            var minusOne = factory.Constant(-1L, wide);
            var length = Length(factory, mode, source, wide);
            var valid = factory.AndAlso(factory.IsNotNull(source), factory.AndAlso(
                factory.AndAlso(factory.GreaterThanOrEqual(start, zero), factory.LessThanOrEqual(start, length)),
                factory.AndAlso(factory.GreaterThanOrEqual(count, zero), factory.LessThanOrEqual(count, factory.Subtract(length, start, wide)))));
            SqlExpression result = start;
            if (!knownEmpty)
            {
                var needleLength = Length(factory, mode, needle, wide);
                valid = factory.AndAlso(valid, factory.IsNotNull(needleLength));
                var window = factory.Function(mode == OceanBaseCompatibilityMode.MySql ? "SUBSTRING" : "SUBSTR",
                    [source, factory.Add(start, one, wide), count], nullable: true,
                    argumentsPropagateNullability: [false, false, false], typeof(string), source.TypeMapping);
                var position = factory.Function("INSTR", [window, needle], nullable: true,
                    argumentsPropagateNullability: [false, false], typeof(long), wide);
                var match = factory.Function("NULLIF", [position, zero], nullable: true,
                    argumentsPropagateNullability: [true, false], typeof(long), wide);
                var offset = factory.Subtract(factory.Add(start, match, wide), one, wide);
                result = factory.Case([new CaseWhenClause(factory.Equal(needleLength, zero), start)],
                    factory.Coalesce(offset, minusOne, wide));
            }
            return factory.Case([new CaseWhenClause(valid, factory.Convert(result, typeof(int), integer))],
                factory.Constant(null, typeof(int), integer));
        }

        private static SqlExpression Length(ISqlExpressionFactory factory, OceanBaseCompatibilityMode mode,
            SqlExpression value, RelationalTypeMapping mapping)
            => factory.Function(mode == OceanBaseCompatibilityMode.MySql ? "CHAR_LENGTH" : "LENGTH", [value],
                nullable: true, argumentsPropagateNullability: [false], typeof(long), mapping);
    }
}
