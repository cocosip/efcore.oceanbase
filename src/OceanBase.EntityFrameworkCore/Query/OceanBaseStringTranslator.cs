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

        public SqlExpression? Translate(SqlExpression? instance, MethodInfo method, IReadOnlyList<SqlExpression> arguments,
            IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        {
            if (instance is null || !Functions.TryGetValue(method, out var function))
                return null;

            instance = sqlExpressionFactory.ApplyDefaultTypeMapping(instance);
            var mapping = instance.TypeMapping!;
            if (mapping.Converter?.ProviderClrType is { } providerType && providerType != typeof(string))
                return null;

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
            return sqlExpressionFactory.Function(function, [instance], nullable: true,
                argumentsPropagateNullability: [false], typeof(string), mapping);
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
