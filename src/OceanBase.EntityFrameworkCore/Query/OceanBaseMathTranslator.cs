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
    internal sealed class OceanBaseMathTranslator(
        ISqlExpressionFactory sqlExpressionFactory,
        IRelationalTypeMappingSource mappingSource,
        OceanBaseCompatibilityMode mode) : IMethodCallTranslator
    {
        private static readonly Dictionary<MethodInfo, string> Functions = CreateFunctions();

        private static Dictionary<MethodInfo, string> CreateFunctions()
        {
            var functions = new Dictionary<MethodInfo, string>();
            foreach (var type in new[] { typeof(sbyte), typeof(short), typeof(int), typeof(long), typeof(float), typeof(double), typeof(decimal) })
            {
                Add(typeof(Math), nameof(Math.Abs), type, type, "ABS");
                Add(typeof(Math), nameof(Math.Sign), type, typeof(int), "SIGN");
            }
            Add(typeof(MathF), nameof(MathF.Abs), typeof(float), typeof(float), "ABS");
            Add(typeof(decimal), nameof(decimal.Abs), typeof(decimal), typeof(decimal), "ABS");
            Add(typeof(MathF), nameof(MathF.Sign), typeof(float), typeof(int), "SIGN");
            Add(typeof(decimal), nameof(decimal.Sign), typeof(decimal), typeof(int), "SIGN");
            Add(typeof(Math), nameof(Math.Truncate), typeof(double), typeof(double), "TRUNC");
            Add(typeof(Math), nameof(Math.Truncate), typeof(decimal), typeof(decimal), "TRUNC");
            Add(typeof(MathF), nameof(MathF.Truncate), typeof(float), typeof(float), "TRUNC");
            Add(typeof(decimal), nameof(decimal.Truncate), typeof(decimal), typeof(decimal), "TRUNC");
            return functions;

            void Add(Type owner, string name, Type input, Type output, string function)
            {
                var method = owner.GetMethod(name, BindingFlags.Public | BindingFlags.Static, [input])!;
                if (method.ReturnType != output)
                    throw new InvalidOperationException($"Unexpected numeric signature: {method}.");
                functions.Add(method, function);
            }
        }

        public SqlExpression? Translate(SqlExpression? instance, MethodInfo method, IReadOnlyList<SqlExpression> arguments,
            IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        {
            if (instance is not null || !Functions.TryGetValue(method, out var function))
                return null;

            var input = sqlExpressionFactory.ApplyDefaultTypeMapping(arguments[0]);
            var mapping = input.TypeMapping;
            if (mapping is null || mapping.Converter?.ConvertsNulls == true
                || !IsNumeric(mapping.Converter?.ProviderClrType ?? mapping.ClrType))
                return null;

            try
            {
                // Probe the owning numeric declaration validator without replacing the operand's converter.
                var validated = mappingSource.FindMapping(mapping.Converter?.ProviderClrType ?? mapping.ClrType,
                    storeTypeName: mapping.StoreType);
                if (validated is null || !IsNumeric(validated.Converter?.ProviderClrType ?? validated.ClrType))
                    return null;
            }
            catch (ArgumentException)
            {
                return null;
            }

            var intMapping = mappingSource.FindMapping(typeof(int))!;
            if (function != "SIGN" && mapping.ClrType != method.ReturnType)
                return null;
            var resultMapping = function == "SIGN" ? intMapping : mapping;
            return function == "TRUNC" && mode == OceanBaseCompatibilityMode.MySql
                ? sqlExpressionFactory.Function("TRUNCATE", [input, sqlExpressionFactory.Constant(0, intMapping)],
                    nullable: true, argumentsPropagateNullability: [false, false], method.ReturnType, resultMapping)
                : sqlExpressionFactory.Function(function, [input], nullable: true,
                    argumentsPropagateNullability: [false], method.ReturnType, resultMapping);
        }

        private static bool IsNumeric(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            return type == typeof(sbyte) || type == typeof(byte) || type == typeof(short) || type == typeof(ushort)
                || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong)
                || type == typeof(float) || type == typeof(double) || type == typeof(decimal);
        }
    }
}
