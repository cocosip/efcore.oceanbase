using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBase.EntityFrameworkCore.Storage;

namespace OceanBase.EntityFrameworkCore.Query
{
    internal sealed class OceanBaseSqlTranslatingExpressionVisitorFactory(
        RelationalSqlTranslatingExpressionVisitorDependencies dependencies) : IRelationalSqlTranslatingExpressionVisitorFactory
    {
        internal const string LeftShiftMarker = "__oceanbase_internal_left_shift";
        internal const string RightShiftMarker = "__oceanbase_internal_right_shift";
        internal const string BooleanValueMarker = "__oceanbase_internal_boolean_value";
        private static readonly MethodInfo Least =
            ((Func<DbFunctions, int[], int>)RelationalDbFunctionsExtensions.Least<int>).Method.GetGenericMethodDefinition();
        private static readonly MethodInfo Greatest =
            ((Func<DbFunctions, int[], int>)RelationalDbFunctionsExtensions.Greatest<int>).Method.GetGenericMethodDefinition();
        private static readonly Dictionary<MethodInfo, string> ScalarMethods = CreateScalarMethods();

        internal static bool IsCanonicalBoolean(RelationalTypeMapping? mapping)
        {
            if (mapping is null || mapping.ClrType != typeof(bool) || mapping.Converter?.ConvertsNulls == true)
                return false;
            var converter = mapping.Converter;
            var trueValue = converter is null ? true : converter.ConvertToProvider(true);
            var falseValue = converter is null ? false : converter.ConvertToProvider(false);
            if (trueValue is true && falseValue is false)
                return true;
            var carrier = converter?.ProviderClrType ?? mapping.ClrType;
            var code = Type.GetTypeCode(Nullable.GetUnderlyingType(carrier) ?? carrier);
            return code switch
            {
                TypeCode.Single => trueValue is 1f && falseValue is 0f,
                TypeCode.Double => trueValue is 1d && falseValue is 0d,
                TypeCode.SByte or TypeCode.Byte or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32 or TypeCode.UInt32
                    or TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Decimal => trueValue is not null && falseValue is not null
                        && Convert.ToDecimal(trueValue) == 1m && Convert.ToDecimal(falseValue) == 0m,
                _ => false
            };
        }

        public RelationalSqlTranslatingExpressionVisitor Create(QueryCompilationContext queryCompilationContext,
            QueryableMethodTranslatingExpressionVisitor queryableMethodTranslatingExpressionVisitor)
            => new Visitor(dependencies, queryCompilationContext, queryableMethodTranslatingExpressionVisitor);

        private static string? GetVariadicFunction(MethodInfo method)
        {
            if (!method.IsGenericMethod || method.DeclaringType != typeof(RelationalDbFunctionsExtensions))
                return null;
            var definition = method.GetGenericMethodDefinition();
            return definition == Least ? "LEAST" : definition == Greatest ? "GREATEST" : null;
        }

        internal static Array? GetConstantValues(MethodCallExpression call)
            => GetVariadicFunction(call.Method) is not null && OceanBaseMathTranslator.IsNumeric(call.Type)
                && call.Arguments[1] is ConstantExpression { Value: Array { Length: > 0 } values } constant
                && constant.Type == call.Type.MakeArrayType() && values.GetType() == constant.Type
                    ? values : null;

        private static Dictionary<MethodInfo, string> CreateScalarMethods()
        {
            var methods = new Dictionary<MethodInfo, string>();
            foreach (var type in new[] { typeof(sbyte), typeof(byte), typeof(short), typeof(ushort), typeof(int), typeof(uint),
                typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal) })
                Add(typeof(Math), type);
            Add(typeof(MathF), typeof(float));
            Add(typeof(decimal), typeof(decimal));
            return methods;

            void Add(Type owner, Type type)
            {
                methods.Add(owner.GetMethod(nameof(Math.Min), [type, type])!, "LEAST");
                methods.Add(owner.GetMethod(nameof(Math.Max), [type, type])!, "GREATEST");
            }
        }

        private sealed class Visitor(RelationalSqlTranslatingExpressionVisitorDependencies dependencies,
            QueryCompilationContext queryCompilationContext, QueryableMethodTranslatingExpressionVisitor queryableVisitor)
            : RelationalSqlTranslatingExpressionVisitor(dependencies, queryCompilationContext, queryableVisitor)
        {
            public override Expression? Visit(Expression? expression)
            {
                var visited = base.Visit(expression);
                if (visited is not SqlExpression { Type: var type } || type != typeof(bool))
                    return visited;
                var mapping = Dependencies.TypeMappingSource.FindMapping(typeof(bool));
                // SQL predicates produce native truth even when the model's default Boolean mapping is text.
                return visited switch
                {
                    SqlBinaryExpression binary when binary.OperatorType is ExpressionType.Equal or ExpressionType.NotEqual
                        or ExpressionType.LessThan or ExpressionType.LessThanOrEqual or ExpressionType.GreaterThan
                        or ExpressionType.GreaterThanOrEqual or ExpressionType.AndAlso or ExpressionType.OrElse
                        or ExpressionType.ExclusiveOr => new SqlBinaryExpression(binary.OperatorType, binary.Left, binary.Right, typeof(bool), mapping),
                    SqlUnaryExpression unary when unary.OperatorType is ExpressionType.Equal or ExpressionType.NotEqual
                        || unary.OperatorType == ExpressionType.Not && unary.Operand.Type == typeof(bool)
                        => new SqlUnaryExpression(unary.OperatorType, unary.Operand, typeof(bool), mapping),
                    LikeExpression like => new LikeExpression(like.Match, like.Pattern, like.EscapeChar, mapping),
                    ExistsExpression exists => new ExistsExpression(exists.Subquery, mapping),
                    InExpression @in => @in.ApplyTypeMapping(mapping),
                    _ => visited
                };
            }

            public override SqlExpression? Translate(Expression expression, bool applyDefaultTypeMapping = true)
            {
                var result = base.Translate(expression, applyDefaultTypeMapping);
                return result?.Type == typeof(bool) && (applyDefaultTypeMapping || result.TypeMapping is not null)
                    ? BooleanOperand(result) : result;
            }

            private SqlExpression BooleanOperand(SqlExpression expression)
            {
                var factory = Dependencies.SqlExpressionFactory;
                expression = factory.ApplyDefaultTypeMapping(expression);
                return IsCanonicalBoolean(expression.TypeMapping) ? expression
                    : factory.Function(BooleanValueMarker, [expression], nullable: true,
                        argumentsPropagateNullability: [true], typeof(bool),
                        Dependencies.TypeMappingSource.FindMapping(typeof(bool)));
            }

            protected override Expression VisitUnary(UnaryExpression expression)
                => expression.NodeType == ExpressionType.Not && Unwrap(expression.Type) == typeof(bool)
                    ? Visit(expression.Operand) is SqlExpression operand
                        ? new SqlUnaryExpression(ExpressionType.Not, BooleanOperand(operand), typeof(bool),
                            Dependencies.TypeMappingSource.FindMapping(typeof(bool)))
                        : QueryCompilationContext.NotTranslatedExpression
                    : base.VisitUnary(expression);

            protected override Expression VisitConditional(ConditionalExpression expression)
            {
                if (Visit(expression.Test) is not SqlExpression test || Visit(expression.IfTrue) is not SqlExpression ifTrue
                    || Visit(expression.IfFalse) is not SqlExpression ifFalse)
                    return QueryCompilationContext.NotTranslatedExpression;
                if (Unwrap(expression.Type) == typeof(bool))
                {
                    ifTrue = BooleanOperand(ifTrue);
                    ifFalse = BooleanOperand(ifFalse);
                }
                return Dependencies.SqlExpressionFactory.Case(
                    [new CaseWhenClause(BooleanOperand(test), ifTrue)], ifFalse);
            }

            protected override Expression VisitBinary(BinaryExpression expression)
            {
                if (expression.Method is null && Unwrap(expression.Left.Type) == typeof(bool)
                    && Unwrap(expression.Right.Type) == typeof(bool)
                    && expression.NodeType is ExpressionType.Equal or ExpressionType.NotEqual or ExpressionType.And
                        or ExpressionType.AndAlso or ExpressionType.Or or ExpressionType.OrElse or ExpressionType.Coalesce)
                {
                    if (Visit(expression.Left) is not SqlExpression left || Visit(expression.Right) is not SqlExpression right)
                        return QueryCompilationContext.NotTranslatedExpression;
                    left = BooleanOperand(left);
                    right = BooleanOperand(right);
                    var mapping = Dependencies.TypeMappingSource.FindMapping(typeof(bool));
                    if (expression.NodeType == ExpressionType.Coalesce)
                        return Dependencies.SqlExpressionFactory.Coalesce(left, right, mapping);
                    var operation = expression.NodeType == ExpressionType.And ? ExpressionType.AndAlso
                        : expression.NodeType == ExpressionType.Or ? ExpressionType.OrElse : expression.NodeType;
                    return new SqlBinaryExpression(operation, left, right, typeof(bool), mapping);
                }
                if (expression.Method is null && expression.NodeType == ExpressionType.ExclusiveOr
                    && Unwrap(expression.Type) == typeof(bool))
                {
                    if (Visit(expression.Left) is not SqlExpression left || Visit(expression.Right) is not SqlExpression right)
                        return QueryCompilationContext.NotTranslatedExpression;
                    var factory = Dependencies.SqlExpressionFactory;
                    // Keep XOR until after null processing; nullable inequality has different compensation.
                    return new SqlBinaryExpression(ExpressionType.ExclusiveOr,
                        factory.ApplyDefaultTypeMapping(left), factory.ApplyDefaultTypeMapping(right), typeof(bool),
                        Dependencies.TypeMappingSource.FindMapping(typeof(bool)));
                }
                if (expression.Method is null && expression.NodeType is ExpressionType.LeftShift or ExpressionType.RightShift)
                {
                    if (Visit(expression.Left) is not SqlExpression value || Visit(expression.Right) is not SqlExpression count)
                        return QueryCompilationContext.NotTranslatedExpression;
                    var factory = Dependencies.SqlExpressionFactory;
                    value = factory.ApplyDefaultTypeMapping(value);
                    count = factory.ApplyDefaultTypeMapping(count);
                    return factory.Function(expression.NodeType == ExpressionType.LeftShift ? LeftShiftMarker : RightShiftMarker,
                        [value, count], nullable: true, argumentsPropagateNullability: [true, true], value.Type, value.TypeMapping);
                }
                if (expression.Method is null
                    && expression.NodeType is ExpressionType.And or ExpressionType.Or or ExpressionType.ExclusiveOr
                    && Unwrap(expression.Type) != typeof(bool))
                {
                    // EF removes small-integer promotions here, losing the bitwise result's width and sign.
                    return Visit(expression.Left) is SqlExpression left && Visit(expression.Right) is SqlExpression right
                        ? Dependencies.SqlExpressionFactory.MakeBinary(expression.NodeType, left, right, typeMapping: null)
                            ?? QueryCompilationContext.NotTranslatedExpression
                        : QueryCompilationContext.NotTranslatedExpression;
                }
                return base.VisitBinary(expression);
            }

            protected override Expression VisitMethodCall(MethodCallExpression call)
            {
                // The base visitor flattens nested Math extrema, losing intermediate native coercion and Single normalization.
                if (ScalarMethods.TryGetValue(call.Method, out var function))
                    return TranslateArguments(function, call.Type, call.Arguments);

                function = GetVariadicFunction(call.Method);
                if (function is null)
                    return base.VisitMethodCall(call);
                if (!OceanBaseMathTranslator.IsNumeric(call.Type))
                    return QueryCompilationContext.NotTranslatedExpression;

                var type = Unwrap(call.Type);
                if (call.Arguments[1] is NewArrayExpression { NodeType: ExpressionType.NewArrayInit } initializer)
                    return TranslateArguments(function, type, initializer.Expressions);
                if (GetConstantValues(call) is not { } values)
                    return QueryCompilationContext.NotTranslatedExpression;

                var operands = new SqlExpression[values.Length];
                for (var i = 0; i < operands.Length; i++)
                    operands[i] = new SqlConstantExpression(values.GetValue(i), type, typeMapping: null);
                return Build(function, type, operands) ?? QueryCompilationContext.NotTranslatedExpression;
            }

            private Expression TranslateArguments(string function, Type type, IReadOnlyList<Expression> arguments)
            {
                var operands = new SqlExpression[arguments.Count];
                for (var i = 0; i < operands.Length; i++)
                {
                    if (Visit(arguments[i]) is not SqlExpression sql)
                        return QueryCompilationContext.NotTranslatedExpression;
                    operands[i] = sql;
                }
                return Build(function, type, operands) ?? QueryCompilationContext.NotTranslatedExpression;
            }

            private SqlExpression? Build(string function, Type type, SqlExpression[] operands)
            {
                if (operands.Length == 0)
                    return null;
                var mappingSource = Dependencies.TypeMappingSource;
                var factory = Dependencies.SqlExpressionFactory;
                RelationalTypeMapping? anchor = null;
                foreach (var operand in operands)
                {
                    if (Unwrap(operand.Type) != type)
                        return null;
                    if (operand.TypeMapping is { } mapping)
                    {
                        if (!IsValid(mapping))
                            return null;
                        anchor ??= mapping;
                    }
                }
                if (anchor is null)
                {
                    anchor = mappingSource.FindMapping(type, Dependencies.Model);
                    if (!IsValid(anchor))
                        return null;
                }
                for (var i = 0; i < operands.Length; i++)
                {
                    if (operands[i].TypeMapping is not null)
                        continue;
                    var mapped = factory.ApplyTypeMapping(operands[i], anchor);
                    if (!ReferenceEquals(mapped.TypeMapping, anchor) && !IsValid(mapped.TypeMapping))
                        return null;
                    operands[i] = mapped;
                }
                if (operands.Length == 1)
                    return operands[0];

                var owner = operands[0].TypeMapping!;
                var carrier = Unwrap(owner.Converter?.ProviderClrType ?? owner.ClrType);
                if (carrier == typeof(decimal) && owner is not OceanBaseDecimalTypeMapping)
                    return null;
                var result = owner is OceanBaseDecimalTypeMapping decimalMapping ? decimalMapping.AsQueryResult() : owner;
                var native = factory.Function(function, operands, nullable: true,
                    argumentsPropagateNullability: operands.Select(_ => false), type, result);
                return carrier == typeof(float)
                    ? factory.Function(OceanBaseMathTranslator.SingleResultMarker, [native], nullable: true,
                        argumentsPropagateNullability: [false], type, result)
                    : native;

                bool IsValid(RelationalTypeMapping? mapping) => mapping is not null && Unwrap(mapping.ClrType) == type
                    && OceanBaseMathTranslator.IsValidNumeric(mapping, mappingSource);
            }

            private static Type Unwrap(Type type) => Nullable.GetUnderlyingType(type) ?? type;
        }
    }
}
