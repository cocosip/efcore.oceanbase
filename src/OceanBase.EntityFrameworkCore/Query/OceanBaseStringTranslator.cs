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
    internal sealed class OceanBaseStringTranslator(
        ISqlExpressionFactory sqlExpressionFactory,
        IRelationalTypeMappingSource mappingSource,
        OceanBaseCompatibilityMode mode) : IMethodCallTranslator, IMemberTranslator
    {
        private static readonly Dictionary<MethodInfo, string> Functions = new()
        {
            [typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!] = "LOWER",
            [typeof(string).GetMethod(nameof(string.ToUpper), Type.EmptyTypes)!] = "UPPER",
            [typeof(string).GetMethod(nameof(string.Trim), Type.EmptyTypes)!] = "TRIM",
            [typeof(string).GetMethod(nameof(string.TrimStart), Type.EmptyTypes)!] = "LTRIM",
            [typeof(string).GetMethod(nameof(string.TrimEnd), Type.EmptyTypes)!] = "RTRIM"
        };
        private static readonly MemberInfo StringLength = typeof(string).GetProperty(nameof(string.Length))!;
        private const int ContainsSearch = 0;
        private const int StartsWithSearch = 1;
        private const int EndsWithSearch = 2;
        private static readonly Dictionary<MethodInfo, int> SearchMethods = new()
        {
            [typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!] = ContainsSearch,
            [typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string)])!] = StartsWithSearch,
            [typeof(string).GetMethod(nameof(string.EndsWith), [typeof(string)])!] = EndsWithSearch,
            [typeof(string).GetMethod(nameof(string.Contains), [typeof(char)])!] = ContainsSearch,
            [typeof(string).GetMethod(nameof(string.StartsWith), [typeof(char)])!] = StartsWithSearch,
            [typeof(string).GetMethod(nameof(string.EndsWith), [typeof(char)])!] = EndsWithSearch
        };
        private static readonly MethodInfo SubstringStart = typeof(string).GetMethod(nameof(string.Substring), [typeof(int)])!;
        private static readonly MethodInfo SubstringStartLength = typeof(string).GetMethod(nameof(string.Substring), [typeof(int), typeof(int)])!;
        private static readonly MethodInfo RemoveStart = typeof(string).GetMethod(nameof(string.Remove), [typeof(int)])!;
        private static readonly MethodInfo RemoveStartCount = typeof(string).GetMethod(nameof(string.Remove), [typeof(int), typeof(int)])!;
        private static readonly MethodInfo ReplaceText = typeof(string).GetMethod(nameof(string.Replace), [typeof(string), typeof(string)])!;
        private static readonly MethodInfo ReplaceCharacters = typeof(string).GetMethod(nameof(string.Replace), [typeof(char), typeof(char)])!;
        private static readonly MethodInfo InsertText = typeof(string).GetMethod(nameof(string.Insert), [typeof(int), typeof(string)])!;
        internal const string OracleSearchMarker = "__oceanbase_internal_string_search";
        internal const string OracleInsertLengthMarker = "__oceanbase_internal_insert_length";

        public SqlExpression? Translate(SqlExpression? instance, MethodInfo method, IReadOnlyList<SqlExpression> arguments,
            IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        {
            var isSearch = SearchMethods.TryGetValue(method, out var search);
            var isFunction = Functions.TryGetValue(method, out var function);
            var isSubstring = method == SubstringStart || method == SubstringStartLength;
            var isRemove = method == RemoveStart || method == RemoveStartCount;
            var isReplace = method == ReplaceText || method == ReplaceCharacters;
            var isInsert = method == InsertText;
            if (instance is null || !isSearch && !isFunction && !isSubstring && !isRemove && !isReplace && !isInsert)
                return null;

            instance = sqlExpressionFactory.ApplyDefaultTypeMapping(instance);
            var mapping = instance.TypeMapping!;
            if (mapping.Converter?.ProviderClrType is { } providerType && providerType != typeof(string))
                return null;

            if (isReplace)
                return TranslateReplace(instance, arguments, mapping, method == ReplaceCharacters);

            if (isInsert)
                return TranslateInsert(instance, arguments, mapping);

            if (isRemove || function is "TRIM" or "LTRIM" or "RTRIM")
            {
                // Shortening fixed text returns variable text; keep national/LOB families and value conversion.
                mapping = TrimResultMapping(mappingSource, mapping);
            }

            if (isRemove)
                return TranslateRemove(instance, arguments, mapping);

            if (isSubstring)
            {
                var start = sqlExpressionFactory.ApplyDefaultTypeMapping(arguments[0]);
                var longMapping = mappingSource.FindMapping(typeof(long))!;
                var position = start is SqlConstantExpression { Value: int value } && start.TypeMapping?.Converter is null
                    ? sqlExpressionFactory.Constant((long)value + 1L, longMapping)
                    : sqlExpressionFactory.Add(start, sqlExpressionFactory.Constant(1L, longMapping), longMapping);
                var args = method == SubstringStart
                    ? new[] { instance, position }
                    : [instance, position, sqlExpressionFactory.ApplyDefaultTypeMapping(arguments[1])];
                return sqlExpressionFactory.Function(mode == OceanBaseCompatibilityMode.MySql ? "SUBSTRING" : "SUBSTR",
                    args, nullable: true, argumentsPropagateNullability: method == SubstringStart
                        ? [false, false] : [false, false, false], typeof(string), mapping);
            }

            if (isSearch)
            {
                var needleMapping = arguments[0].Type == typeof(char)
                    ? (RelationalTypeMapping)mapping.WithComposedConverter(new CharToStringConverter())
                    : mapping;
                var needle = sqlExpressionFactory.ApplyTypeMapping(arguments[0], needleMapping);
                if (needle.TypeMapping?.Converter?.ProviderClrType is { } needleProvider && needleProvider != typeof(string))
                    return null;
                return mode == OceanBaseCompatibilityMode.Oracle
                    ? sqlExpressionFactory.Function(OracleSearchMarker,
                        [instance, needle, sqlExpressionFactory.Constant(search)], nullable: false,
                        argumentsPropagateNullability: [false, false, false], typeof(bool))
                    : BuildSearch(sqlExpressionFactory, mode, search, instance, needle,
                        needle is SqlConstantExpression { Value: "" } && needle.TypeMapping?.Converter is null);
            }

            // Oracle all-space trim can return NULL for required input; do not simplify result null tests.
            return sqlExpressionFactory.Function(function!, [instance], nullable: true,
                argumentsPropagateNullability: [false], typeof(string), mapping);
        }

        private SqlExpression? TranslateInsert(SqlExpression source, IReadOnlyList<SqlExpression> arguments,
            RelationalTypeMapping mapping)
        {
            var factory = sqlExpressionFactory;
            var text = mappingSource.FindMapping(typeof(string), storeTypeName: null, unicode: mapping.IsUnicode)!;
            var inserted = factory.ApplyTypeMapping(arguments[1], text);
            if (inserted.TypeMapping?.Converter?.ProviderClrType is { } carrier && carrier != typeof(string))
                return null;

            var wide = mappingSource.FindMapping(typeof(long))!;
            var start = factory.Convert(factory.ApplyDefaultTypeMapping(arguments[0]), typeof(long), wide);
            var one = factory.Constant(1L, wide);
            var mysql = mode == OceanBaseCompatibilityMode.MySql;
            var length = factory.Function(mysql ? "CHAR_LENGTH" : "LENGTH", [source], nullable: true,
                argumentsPropagateNullability: [false], typeof(long), wide);
            var insertedLength = factory.Function(mysql ? "CHAR_LENGTH" : OracleInsertLengthMarker, [inserted], nullable: true,
                argumentsPropagateNullability: [false], typeof(long), wide);
            var valid = factory.AndAlso(factory.AndAlso(factory.GreaterThanOrEqual(start, factory.Constant(0L, wide)),
                factory.LessThanOrEqual(start, length)), factory.IsNotNull(insertedLength));

            var resultMapping = mapping.IsFixedLength ? TrimResultMapping(mappingSource, mapping) : mapping;
            var substring = mysql ? "SUBSTRING" : "SUBSTR";
            var prefix = factory.Function(substring, [source, one, start], nullable: true,
                argumentsPropagateNullability: [false, false, false], typeof(string), resultMapping);
            var suffix = factory.Function(substring, [source, factory.Add(start, one, wide)], nullable: true,
                argumentsPropagateNullability: [false, false], typeof(string), resultMapping);
            var first = factory.Function("CONCAT", [prefix, inserted], nullable: true,
                argumentsPropagateNullability: [false, false], typeof(string), resultMapping);
            var result = factory.Function("CONCAT", [first, suffix], nullable: true,
                argumentsPropagateNullability: [false, false], typeof(string), resultMapping);
            return factory.Case([new CaseWhenClause(valid, result)], factory.Constant(null, typeof(string), resultMapping));
        }

        private SqlExpression? TranslateReplace(SqlExpression instance, IReadOnlyList<SqlExpression> arguments,
            RelationalTypeMapping mapping, bool characters)
        {
            var factory = sqlExpressionFactory;
            var text = mappingSource.FindMapping(typeof(string), storeTypeName: null, unicode: mapping.IsUnicode)!;
            var tokenMapping = characters ? (RelationalTypeMapping)text.WithComposedConverter(new CharToStringConverter()) : text;
            var oldValue = factory.ApplyTypeMapping(arguments[0], tokenMapping);
            var newValue = factory.ApplyTypeMapping(arguments[1], tokenMapping);
            if (characters && (oldValue.TypeMapping?.Converter is not CharToStringConverter
                || newValue.TypeMapping?.Converter is not CharToStringConverter)
                || oldValue.TypeMapping?.Converter?.ProviderClrType is { } oldCarrier && oldCarrier != typeof(string)
                || newValue.TypeMapping?.Converter?.ProviderClrType is { } newCarrier && newCarrier != typeof(string))
                return null;

            var lengthFunction = mode == OceanBaseCompatibilityMode.MySql ? "CHAR_LENGTH" : "LENGTH";
            var oldLength = factory.Function(lengthFunction, [oldValue], nullable: true,
                argumentsPropagateNullability: [false], typeof(int));
            var valid = factory.GreaterThan(oldLength, factory.Constant(0));
            if (characters)
            {
                var newLength = factory.Function(lengthFunction, [newValue], nullable: true,
                    argumentsPropagateNullability: [false], typeof(int));
                valid = factory.AndAlso(valid, factory.IsNotNull(newLength));
            }
            else if (mode == OceanBaseCompatibilityMode.MySql)
            {
                newValue = factory.Coalesce(newValue, factory.Constant("", text), newValue.TypeMapping);
            }

            var resultMapping = mapping.IsFixedLength ? TrimResultMapping(mappingSource, mapping) : mapping;
            var replaced = factory.Function("REPLACE", [instance, oldValue, newValue], nullable: true,
                argumentsPropagateNullability: [false, false, false], typeof(string), resultMapping);
            return factory.Case([new CaseWhenClause(valid, replaced)], factory.Constant(null, typeof(string), resultMapping));
        }

        internal static RelationalTypeMapping TrimResultMapping(IRelationalTypeMappingSource mappings, RelationalTypeMapping source)
        {
            var result = mappings.FindMapping(typeof(string),
                storeTypeName: source.StoreTypeNameBase.Trim().ToUpperInvariant() is "CLOB" or "NCLOB" ? source.StoreType : null,
                unicode: source.IsUnicode, size: source.Size, fixedLength: false)!;
            return source.Converter is { } converter ? (RelationalTypeMapping)result.WithComposedConverter(converter) : result;
        }

        private SqlExpression TranslateRemove(SqlExpression source, IReadOnlyList<SqlExpression> arguments,
            RelationalTypeMapping mapping)
        {
            var factory = sqlExpressionFactory;
            var longMapping = mappingSource.FindMapping(typeof(long))!;
            var zero = factory.Constant(0L, longMapping);
            var one = factory.Constant(1L, longMapping);
            var start = factory.ApplyDefaultTypeMapping(arguments[0]);
            var length = factory.Function(mode == OceanBaseCompatibilityMode.MySql ? "CHAR_LENGTH" : "LENGTH",
                [source], nullable: true, argumentsPropagateNullability: [false], typeof(long), longMapping);
            var valid = factory.AndAlso(factory.IsNotNull(source),
                factory.AndAlso(factory.GreaterThanOrEqual(start, zero), factory.LessThanOrEqual(start, length)));
            var function = mode == OceanBaseCompatibilityMode.MySql ? "SUBSTRING" : "SUBSTR";
            var prefix = factory.Function(function, [source, one, start], nullable: true,
                argumentsPropagateNullability: [false, false, false], typeof(string), mapping);
            SqlExpression result = prefix;
            if (arguments.Count == 2)
            {
                var count = factory.ApplyDefaultTypeMapping(arguments[1]);
                valid = factory.AndAlso(valid, factory.AndAlso(factory.GreaterThanOrEqual(count, zero),
                    factory.LessThanOrEqual(count, factory.Subtract(length, start, longMapping))));
                var end = factory.Add(factory.Add(start, count, longMapping), one, longMapping);
                var suffix = factory.Function(function, [source, end], nullable: true,
                    argumentsPropagateNullability: [false, false], typeof(string), mapping);
                result = factory.Function("CONCAT", [prefix, suffix], nullable: true,
                    argumentsPropagateNullability: [false, false], typeof(string), mapping);
            }
            return factory.Case([new CaseWhenClause(valid, result)], factory.Constant(null, typeof(string), mapping));
        }

        internal static SqlExpression BuildSearch(ISqlExpressionFactory factory, OceanBaseCompatibilityMode mode,
            int search, SqlExpression source, SqlExpression needle, bool knownEmpty)
        {
            var present = factory.IsNotNull(source);
            if (knownEmpty)
                return present;

            var valid = factory.AndAlso(present, factory.IsNotNull(needle));
            SqlExpression matched;
            if (search == EndsWithSearch)
            {
                var length = factory.Function(mode == OceanBaseCompatibilityMode.MySql ? "CHAR_LENGTH" : "LENGTH",
                    [needle], nullable: true, argumentsPropagateNullability: [false], typeof(int));
                var sourceLength = factory.Function(mode == OceanBaseCompatibilityMode.MySql ? "CHAR_LENGTH" : "LENGTH",
                    [source], nullable: true, argumentsPropagateNullability: [false], typeof(int));
                if (mode == OceanBaseCompatibilityMode.Oracle
                    && source.TypeMapping?.StoreTypeNameBase.Trim().ToUpperInvariant() is "CLOB" or "NCLOB")
                {
                    // Oracle LOBs cannot use ordinary equality; locate the final occurrence instead.
                    var position = factory.Function("INSTR", [source, needle, factory.Constant(-1)], nullable: true,
                        argumentsPropagateNullability: [false, false, false], typeof(int));
                    matched = factory.Equal(position, factory.Add(factory.Subtract(sourceLength, length), factory.Constant(1)));
                }
                else
                {
                    var suffix = mode == OceanBaseCompatibilityMode.MySql
                        ? factory.Function("RIGHT", [source, length], nullable: true,
                            argumentsPropagateNullability: [false, false], typeof(string), source.TypeMapping)
                        : factory.Function("SUBSTR", [source, factory.Negate(length)], nullable: true,
                            argumentsPropagateNullability: [false, false], typeof(string), source.TypeMapping);
                    matched = factory.Equal(suffix, needle);
                }
                // PAD SPACE equality and a missing LOB match must not accept a longer needle.
                matched = factory.AndAlso(factory.GreaterThanOrEqual(sourceLength, length), matched);
            }
            else
            {
                var position = factory.Function("INSTR", [source, needle], nullable: true,
                    argumentsPropagateNullability: [false, false], typeof(int));
                matched = search == ContainsSearch ? factory.GreaterThan(position, factory.Constant(0))
                    : factory.Equal(position, factory.Constant(1));
            }

            if (mode == OceanBaseCompatibilityMode.MySql)
            {
                var length = factory.Function("CHAR_LENGTH", [needle], nullable: true,
                    argumentsPropagateNullability: [false], typeof(int));
                matched = factory.OrElse(factory.Equal(length, factory.Constant(0)), matched);
            }

            return factory.Case([new CaseWhenClause(factory.AndAlso(valid, matched), factory.Constant(true))],
                factory.Constant(false));
        }

        public SqlExpression? Translate(SqlExpression? instance, MemberInfo member, Type returnType,
            IDiagnosticsLogger<DbLoggerCategory.Query> logger)
            => instance is not null && member.Equals(StringLength)
                && (instance.TypeMapping?.Converter?.ProviderClrType is not { } providerType || providerType == typeof(string))
                ? sqlExpressionFactory.Function(mode == OceanBaseCompatibilityMode.MySql ? "CHAR_LENGTH" : "LENGTH",
                    [sqlExpressionFactory.ApplyDefaultTypeMapping(instance)], nullable: true,
                    argumentsPropagateNullability: [false], typeof(int), mappingSource.FindMapping(typeof(int)))
                : null;
    }
}
