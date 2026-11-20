using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace OceanBase.EntityFrameworkCore.Query
{
    internal sealed class OceanBaseExplicitTrimTranslator(
        ISqlExpressionFactory factory, IRelationalTypeMappingSource mappings, OceanBaseCompatibilityMode mode)
        : IMethodCallTranslator
    {
        private static readonly HashSet<MethodInfo> Methods = new(
            from name in new[] { nameof(string.Trim), nameof(string.TrimStart), nameof(string.TrimEnd) }
            from type in new[] { typeof(char), typeof(char[]) }
            select typeof(string).GetMethod(name, [type])!);
        private static readonly char[] Whitespace = Enumerable.Range(0, char.MaxValue + 1)
            .Select(value => (char)value).Where(char.IsWhiteSpace).ToArray();

        internal static bool IsArrayTrim(MethodInfo method)
            => Methods.Contains(method) && method.GetParameters()[0].ParameterType == typeof(char[]);

        internal static string EncodeParameter(QueryContext context, string name, OceanBaseCompatibilityMode mode)
            => EncodeSet((char[]?)context.ParameterValues[name], mode);

        internal static string EncodeSet(char[]? characters, OceanBaseCompatibilityMode mode)
        {
            if (characters is null || characters.Length == 0) characters = Whitespace;
            if (characters.Any(char.IsSurrogate))
                throw new ArgumentException("Explicit trim characters must be valid BMP characters.");
            return mode == OceanBaseCompatibilityMode.Oracle ? new string(characters)
                : string.Concat(characters.Distinct().Select(value => "\\x{" + ((int)value).ToString("x4", CultureInfo.InvariantCulture) + "}"));
        }

        internal static SqlExpression? CharacterSet(ISqlExpressionFactory factory, RelationalTypeMapping text,
            OceanBaseCompatibilityMode mode, IReadOnlyList<SqlExpression> characters)
        {
            SqlExpression? set = null;
            SqlExpression? valid = null;
            foreach (var character in characters)
            {
                var mapped = factory.ApplyTypeMapping(character,
                    (RelationalTypeMapping)text.WithComposedConverter(new CharToStringConverter()));
                if (mapped.TypeMapping?.Converter is not CharToStringConverter)
                    return null;
                valid = valid is null ? factory.IsNotNull(mapped) : factory.AndAlso(valid, factory.IsNotNull(mapped));
                // The carrier is already text; SUBSTR gives a string result without an invalid LOB CAST.
                SqlExpression token = factory.Function("SUBSTR", [mapped, factory.Constant(1)], nullable: true,
                    argumentsPropagateNullability: [false, false], typeof(string), text);
                if (mode == OceanBaseCompatibilityMode.MySql)
                {
                    // Escape ICU class operators, including its intersection operator.
                    foreach (var value in new[] { "\\", "[", "]", "^", "-", "&" })
                        token = factory.Function("REPLACE", [token, factory.Constant(value, text), factory.Constant("\\" + value, text)],
                            nullable: true, argumentsPropagateNullability: [false, false, false], typeof(string), text);
                }
                set = set is null ? token : factory.Function("CONCAT", [set, token], nullable: true,
                    argumentsPropagateNullability: [false, false], typeof(string), text);
            }
            return set is null ? factory.Constant(EncodeSet([], mode), text)
                : factory.Case([new CaseWhenClause(valid!, set)], factory.Constant(null, typeof(string), text));
        }

        public SqlExpression? Translate(SqlExpression? instance, MethodInfo method, IReadOnlyList<SqlExpression> arguments,
            IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        {
            if (instance is null || !Methods.Contains(method)) return null;
            instance = factory.ApplyDefaultTypeMapping(instance);
            var mapping = instance.TypeMapping!;
            if (mapping.Converter?.ProviderClrType is { } provider && provider != typeof(string)) return null;
            var text = mappings.FindMapping(typeof(string), storeTypeName: null, unicode: mapping.IsUnicode)!;
            var set = method.GetParameters()[0].ParameterType == typeof(char)
                ? CharacterSet(factory, text, mode, arguments) : arguments[0];
            if (set is null || set.Type != typeof(string)) return null;
            var resultMapping = OceanBaseStringTranslator.TrimResultMapping(mappings, mapping);
            if (mode == OceanBaseCompatibilityMode.Oracle)
            {
                var result = instance;
                if (method.Name != nameof(string.TrimStart)) result = Trim("RTRIM", result);
                if (method.Name != nameof(string.TrimEnd)) result = Trim("LTRIM", result);
                return result;

                SqlExpression Trim(string name, SqlExpression source)
                    => factory.Function(name, [source, set], nullable: true,
                        argumentsPropagateNullability: [false, false], typeof(string), resultMapping);
            }
            var start = Concat(factory.Constant("\\A[", text), set, factory.Constant("]+", text));
            var end = Concat(factory.Constant("[", text), set, factory.Constant("]+\\z", text));
            var pattern = method.Name == nameof(string.TrimStart) ? start : method.Name == nameof(string.TrimEnd) ? end
                : Concat(start, factory.Constant("|", text), end);
            return factory.Function("REGEXP_REPLACE", [instance, pattern, factory.Constant("", text),
                    factory.Constant(1), factory.Constant(0), factory.Constant("c", text)], nullable: true,
                argumentsPropagateNullability: [false, false, false, false, false, false], typeof(string), resultMapping);

            SqlExpression Concat(params SqlExpression[] values)
                => factory.Function("CONCAT", values, nullable: true,
                    argumentsPropagateNullability: [false, false, false], typeof(string), text);
        }
    }
}
