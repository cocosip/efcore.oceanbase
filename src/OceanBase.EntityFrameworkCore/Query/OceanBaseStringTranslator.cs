using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

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
            [typeof(string).GetMethod(nameof(string.EndsWith), [typeof(string)])!] = EndsWithSearch
        };
        internal const string OracleSearchMarker = "__oceanbase_internal_string_search";

        public SqlExpression? Translate(SqlExpression? instance, MethodInfo method, IReadOnlyList<SqlExpression> arguments,
            IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        {
            var isSearch = SearchMethods.TryGetValue(method, out var search);
            var isFunction = Functions.TryGetValue(method, out var function);
            if (instance is null || !isSearch && !isFunction)
                return null;

            instance = sqlExpressionFactory.ApplyDefaultTypeMapping(instance);
            var mapping = instance.TypeMapping!;
            if (mapping.Converter?.ProviderClrType is { } providerType && providerType != typeof(string))
                return null;

            if (isSearch)
            {
                var needle = sqlExpressionFactory.ApplyTypeMapping(arguments[0], mapping);
                if (needle.TypeMapping?.Converter?.ProviderClrType is { } needleProvider && needleProvider != typeof(string))
                    return null;
                return mode == OceanBaseCompatibilityMode.Oracle
                    ? sqlExpressionFactory.Function(OracleSearchMarker,
                        [instance, needle, sqlExpressionFactory.Constant(search)], nullable: false,
                        argumentsPropagateNullability: [false, false, false], typeof(bool))
                    : BuildSearch(sqlExpressionFactory, mode, search, instance, needle,
                        needle is SqlConstantExpression { Value: "" } && needle.TypeMapping?.Converter is null);
            }

            if (function is "TRIM" or "LTRIM" or "RTRIM")
            {
                // Trimming fixed text returns variable text; keep national/LOB families and value conversion.
                mapping = mappingSource.FindMapping(typeof(string),
                    storeTypeName: mapping.StoreTypeNameBase.Trim().ToUpperInvariant() is "CLOB" or "NCLOB" ? mapping.StoreType : null,
                    unicode: mapping.IsUnicode, size: mapping.Size, fixedLength: false)!;
                if (instance.TypeMapping!.Converter is { } converter)
                    mapping = (RelationalTypeMapping)mapping.WithComposedConverter(converter);
            }

            // Oracle all-space trim can return NULL for required input; do not simplify result null tests.
            return sqlExpressionFactory.Function(function!, [instance], nullable: true,
                argumentsPropagateNullability: [false], typeof(string), mapping);
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
                var suffix = mode == OceanBaseCompatibilityMode.MySql
                    ? factory.Function("RIGHT", [source, length], nullable: true,
                        argumentsPropagateNullability: [false, false], typeof(string), source.TypeMapping)
                    : factory.Function("SUBSTR", [source, factory.Negate(length)], nullable: true,
                        argumentsPropagateNullability: [false, false], typeof(string), source.TypeMapping);
                matched = factory.Equal(suffix, needle);
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
