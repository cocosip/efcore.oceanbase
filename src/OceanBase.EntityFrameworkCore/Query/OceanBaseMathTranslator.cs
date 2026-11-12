using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBase.EntityFrameworkCore.Storage;

namespace OceanBase.EntityFrameworkCore.Query
{
    internal sealed class OceanBaseMathTranslator(
        ISqlExpressionFactory sqlExpressionFactory,
        IRelationalTypeMappingSource mappingSource,
        OceanBaseCompatibilityMode mode) : IMethodCallTranslator
    {
        internal const string SingleResultMarker = "__oceanbase_internal_single_result";
        private static readonly Dictionary<MethodInfo, string> Functions = CreateFunctions();
        private static readonly HashSet<MethodInfo> ClampMethods = CreateClampMethods();

        private static HashSet<MethodInfo> CreateClampMethods()
        {
            var methods = new HashSet<MethodInfo>();
            foreach (var (owner, type) in new[]
            {
                (typeof(Math), typeof(sbyte)), (typeof(Math), typeof(byte)),
                (typeof(Math), typeof(short)), (typeof(Math), typeof(ushort)),
                (typeof(Math), typeof(int)), (typeof(Math), typeof(uint)),
                (typeof(Math), typeof(long)), (typeof(Math), typeof(ulong)),
                (typeof(Math), typeof(float)), (typeof(Math), typeof(double)),
                (typeof(Math), typeof(decimal)), (typeof(decimal), typeof(decimal))
            })
                methods.Add(owner.GetMethod("Clamp", [type, type, type])
                    ?? throw new InvalidOperationException($"Unexpected Clamp signature for {owner} and {type}."));
            return methods;
        }

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
            foreach (var (owner, type) in new[] { (typeof(Math), typeof(double)), (typeof(Math), typeof(decimal)),
                (typeof(MathF), typeof(float)), (typeof(decimal), typeof(decimal)) })
            {
                Add(owner, "Floor", type, type, "FLOOR");
                Add(owner, "Ceiling", type, type, "CEIL");
            }
            foreach (var (owner, type) in new[] { (typeof(Math), typeof(double)), (typeof(MathF), typeof(float)) })
            {
                Add(owner, "Sqrt", type, type, "SQRT");
                Add(owner, "Exp", type, type, "EXP");
                Add(owner, "Log", type, type, "LN");
                Add(owner, "Log", type, type, "LOG", arity: 2);
                Add(owner, "Log2", type, type, "LOG2");
                Add(owner, "Log10", type, type, "LOG10");
                Add(owner, "Pow", type, type, "POWER", arity: 2);
            }
            return functions;

            void Add(Type owner, string name, Type input, Type output, string function, int arity = 1)
            {
                var method = owner.GetMethod(name, BindingFlags.Public | BindingFlags.Static,
                    arity == 1 ? [input] : [input, input])!;
                if (method.ReturnType != output)
                    throw new InvalidOperationException($"Unexpected numeric signature: {method}.");
                functions.Add(method, function);
            }
        }

        public SqlExpression? Translate(SqlExpression? instance, MethodInfo method, IReadOnlyList<SqlExpression> arguments,
            IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        {
            if (instance is null && ClampMethods.Contains(method))
                return TranslateClamp(method, arguments);

            if (instance is not null || !Functions.TryGetValue(method, out var function)
                || arguments.Count != method.GetParameters().Length)
                return null;

            var inputs = new SqlExpression[arguments.Count];
            for (var index = 0; index < inputs.Length; index++)
            {
                inputs[index] = sqlExpressionFactory.ApplyDefaultTypeMapping(arguments[index]);
                if (!IsValidNumeric(inputs[index].TypeMapping, mappingSource))
                    return null;
            }
            var input = inputs[0];
            var mapping = input.TypeMapping!;

            var intMapping = mappingSource.FindMapping(typeof(int))!;
            if (function != "SIGN" && mapping.ClrType != method.ReturnType)
                return null;
            if (function is "SQRT" or "EXP" or "LN" or "LOG" or "LOG2" or "LOG10" or "POWER")
                return TranslateTranscendental(function, method.ReturnType, inputs, mapping);
            var resultMapping = function == "SIGN" ? intMapping : mapping;
            if (mode == OceanBaseCompatibilityMode.MySql && function is "FLOOR" or "CEIL")
            {
                var truncated = sqlExpressionFactory.Function("TRUNCATE", [input, sqlExpressionFactory.Constant(0, intMapping)],
                    nullable: true, argumentsPropagateNullability: [false, false], method.ReturnType, resultMapping);
                var carrier = mapping.Converter?.ProviderClrType ?? mapping.ClrType;
                carrier = Nullable.GetUnderlyingType(carrier) ?? carrier;
                // Syntax one belongs to stored numeric space, independently of the model converter and write facets.
                var one = sqlExpressionFactory.Constant(Convert.ChangeType(1, carrier), mappingSource.FindMapping(carrier));
                var floor = function == "FLOOR";
                return sqlExpressionFactory.Case(
                    [new CaseWhenClause(floor ? sqlExpressionFactory.LessThan(input, truncated) : sqlExpressionFactory.GreaterThan(input, truncated),
                        floor ? sqlExpressionFactory.Subtract(truncated, one, resultMapping) : sqlExpressionFactory.Add(truncated, one, resultMapping))],
                    truncated);
            }
            return function == "TRUNC" && mode == OceanBaseCompatibilityMode.MySql
                ? sqlExpressionFactory.Function("TRUNCATE", [input, sqlExpressionFactory.Constant(0, intMapping)],
                    nullable: true, argumentsPropagateNullability: [false, false], method.ReturnType, resultMapping)
                : sqlExpressionFactory.Function(function, [input], nullable: true,
                    argumentsPropagateNullability: [false], method.ReturnType, resultMapping);
        }

        private SqlExpression? TranslateClamp(MethodInfo method, IReadOnlyList<SqlExpression> arguments)
        {
            if (arguments.Count != 3 || arguments.Any(argument => argument.Type != method.ReturnType))
                return null;

            var inputs = arguments.Select(sqlExpressionFactory.ApplyDefaultTypeMapping).ToArray();
            if (inputs.Any(input => input is null || input.TypeMapping is not RelationalTypeMapping))
                return null;
            var value = inputs[0]!;
            var mapping = (RelationalTypeMapping)value.TypeMapping!;
            if (mapping.ClrType != method.ReturnType
                || !inputs.All(input => IsValidNumeric((RelationalTypeMapping)input!.TypeMapping!, mappingSource)))
                return null;

            if (inputs[1] is SqlConstantExpression { Value: IComparable minimum }
                && inputs[2] is SqlConstantExpression { Value: { } maximum }
                && minimum.CompareTo(maximum) > 0)
                return null;

            var lower = sqlExpressionFactory.ApplyTypeMapping(inputs[1]!, mapping)!;
            var upper = sqlExpressionFactory.ApplyTypeMapping(inputs[2]!, mapping)!;
            var result = sqlExpressionFactory.Case(
                [
                    new CaseWhenClause(sqlExpressionFactory.LessThan(value, lower), lower),
                    new CaseWhenClause(sqlExpressionFactory.GreaterThan(value, upper), upper)
                ], value);
            return sqlExpressionFactory.ApplyTypeMapping(result, mapping);
        }

        private SqlExpression? TranslateTranscendental(string function, Type returnType, SqlExpression[] inputs,
            RelationalTypeMapping mapping)
        {
            var carrier = Nullable.GetUnderlyingType(mapping.Converter?.ProviderClrType ?? mapping.ClrType)
                ?? mapping.Converter?.ProviderClrType ?? mapping.ClrType;
            if (carrier == typeof(decimal) && mapping is not OceanBaseDecimalTypeMapping)
                return null;
            var resultMapping = mapping is OceanBaseDecimalTypeMapping decimalMapping ? decimalMapping.AsQueryResult() : mapping;
            var single = carrier == typeof(float);
            var nativeType = single ? typeof(double) : returnType;
            var nativeMapping = single ? mappingSource.FindMapping(typeof(double))! : resultMapping;
            SqlExpression body;
            if (function == "LOG" && mode == OceanBaseCompatibilityMode.MySql)
            {
                // Keep both logarithms at native precision until the completed quotient is normalized.
                var doubleMapping = mappingSource.FindMapping(typeof(double))!;
                var numerator = Native("LN", [inputs[0]], typeof(double), doubleMapping);
                var denominator = Native("LN", [inputs[1]], typeof(double), doubleMapping);
                body = new SqlBinaryExpression(ExpressionType.Divide, numerator, denominator, nativeType, nativeMapping);
            }
            else
            {
                var operands = function == "LOG" ? new[] { inputs[1], inputs[0] }
                    : function == "LOG2" && mode == OceanBaseCompatibilityMode.Oracle
                        ? new[] { sqlExpressionFactory.Constant(2, mappingSource.FindMapping(typeof(int))), inputs[0] }
                    : function == "LOG10" && mode == OceanBaseCompatibilityMode.Oracle
                        ? [sqlExpressionFactory.Constant(10, mappingSource.FindMapping(typeof(int))), inputs[0]] : inputs;
                body = Native((function is "LOG2" or "LOG10") && mode == OceanBaseCompatibilityMode.Oracle ? "LOG" : function,
                    operands, nativeType, nativeMapping);
            }
            // Uniform normalization also covers Oracle SQRT on physically wider Single declarations.
            return single ? Native(SingleResultMarker, [body], returnType, resultMapping) : body;

            SqlExpression Native(string name, SqlExpression[] operands, Type type, RelationalTypeMapping typeMapping)
                => sqlExpressionFactory.Function(name, operands, nullable: true,
                    argumentsPropagateNullability: operands.Select(_ => false), type, typeMapping);
        }

        internal static bool IsValidNumeric(RelationalTypeMapping? mapping, IRelationalTypeMappingSource mappingSource)
        {
            if (mapping is null || mapping.Converter?.ConvertsNulls == true
                || !IsNumeric(mapping.Converter?.ProviderClrType ?? mapping.ClrType))
                return false;
            try
            {
                // Probe the declaration owner without replacing the operand's converter or facets.
                var validated = mappingSource.FindMapping(mapping.Converter?.ProviderClrType ?? mapping.ClrType,
                    storeTypeName: mapping.StoreType);
                return validated is not null && IsNumeric(validated.Converter?.ProviderClrType ?? validated.ClrType);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        internal static bool IsNumeric(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            return type == typeof(sbyte) || type == typeof(byte) || type == typeof(short) || type == typeof(ushort)
                || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong)
                || type == typeof(float) || type == typeof(double) || type == typeof(decimal);
        }
    }
}
