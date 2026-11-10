using System;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace OceanBase.EntityFrameworkCore.Query
{
    internal sealed class OceanBaseOracleStringVisitor(
        ISqlExpressionFactory factory, ParametersCacheDecorator parameters) : ExpressionVisitor
    {
        protected override Expression VisitExtension(Expression node)
        {
            switch (node)
            {
                case SqlBinaryExpression { OperatorType: ExpressionType.Add } binary when binary.Type == typeof(string):
                    // Oracle concat may return NULL, but does not propagate a single NULL operand.
                    return factory.Function("CONCAT", [(SqlExpression)Visit(binary.Left), (SqlExpression)Visit(binary.Right)],
                        nullable: true, argumentsPropagateNullability: [false, false], typeof(string), binary.TypeMapping);
                case SqlFunctionExpression { IsBuiltIn: true, Name: OceanBaseStringTranslator.OracleSearchMarker,
                    Arguments: { Count: 3 } args, Type: var type }
                    when type == typeof(bool) && args[2] is SqlConstantExpression { Value: int }:
                    var source = (SqlExpression)Visit(args[0]);
                    var needle = args[1];
                    object? value = needle switch
                    {
                        SqlConstantExpression constant => ProviderValue(constant, constant.Value),
                        SqlParameterExpression parameter => ProviderValue(parameter, parameters.GetAndDisableCaching()[parameter.Name]),
                        _ => null
                    };
                    if (needle is SqlConstantExpression or SqlParameterExpression && value is null or "")
                        return factory.ApplyDefaultTypeMapping(value is "" ? factory.IsNotNull(source) : factory.Constant(false));
                    return factory.ApplyDefaultTypeMapping(OceanBaseStringTranslator.BuildSearch(factory, OceanBaseCompatibilityMode.Oracle,
                        (int)((SqlConstantExpression)args[2]).Value!, source, (SqlExpression)Visit(needle), false));
                case SqlConstantExpression constant when IsProviderString(constant):
                    return Normalize(constant, constant.Value);
                case SqlParameterExpression parameter when IsProviderString(parameter):
                    // EF's standard cache key distinguishes null only; Oracle also treats empty as NULL.
                    return Normalize(parameter, parameters.GetAndDisableCaching()[parameter.Name]);
                default:
                    return base.VisitExtension(node);
            }
        }

        private static bool IsProviderString(SqlExpression expression)
            => expression.TypeMapping?.ElementTypeMapping is null
                && (expression.TypeMapping?.Converter?.ProviderClrType ?? expression.Type) == typeof(string);

        private SqlExpression Normalize(SqlExpression expression, object? value)
        {
            return ProviderValue(expression, value) is null or ""
                ? factory.Constant(null, expression.Type, expression.TypeMapping)
                : expression;
        }

        private static object? ProviderValue(SqlExpression expression, object? value)
        {
            var converter = expression.TypeMapping?.Converter;
            if (converter?.ConvertsNulls == true)
            {
                throw new InvalidOperationException("Oracle queries with null-converting string value converters are not supported.");
            }

            var modelType = converter is null ? expression.Type : Nullable.GetUnderlyingType(converter.ModelClrType) ?? converter.ModelClrType;
            if (value is not null && modelType.IsEnum && !value.GetType().IsEnum)
            {
                // EF comparisons can carry an enum's underlying integer before mapping conversion.
                value = Enum.ToObject(modelType, value);
            }

            return converter is null ? value : converter.ConvertToProvider(value);
        }
    }
}
