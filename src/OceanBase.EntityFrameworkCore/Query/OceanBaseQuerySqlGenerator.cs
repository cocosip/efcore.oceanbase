using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace OceanBase.EntityFrameworkCore.Query
{
    internal sealed partial class OceanBaseQuerySqlGenerator : QuerySqlGenerator
    {
        private readonly OceanBaseCompatibilityMode _mode;

        public OceanBaseQuerySqlGenerator(QuerySqlGeneratorDependencies dependencies, OceanBaseCompatibilityMode mode)
            : base(dependencies)
        {
            _mode = mode;
        }

        protected override string AliasSeparator => _mode == OceanBaseCompatibilityMode.Oracle ? " " : " AS ";

        protected override void GenerateValues(ValuesExpression valuesExpression)
        {
            var rows = valuesExpression.RowValues
                ?? throw new InvalidOperationException("Collection parameters must be expanded before SQL generation.");
            var empty = rows.Count == 0;
            var prototypes = empty ? valuesExpression.FindAnnotation(OceanBaseCollectionParameterExpandingVisitor.EmptyColumnsAnnotation)
                ?.Value as IReadOnlyList<SqlExpression> : null;
            if (empty && (prototypes is null || prototypes.Count != valuesExpression.ColumnNames.Count))
                throw new NotSupportedException("An empty collection root requires retained integral element mapping metadata.");
            for (var row = 0; row < (empty ? 1 : rows.Count); row++)
            {
                if (row > 0) Sql.AppendLine().Append("UNION ALL ");
                Sql.Append("SELECT ");
                var columns = empty ? prototypes! : rows[row].Values;
                for (var column = 0; column < columns.Count; column++)
                {
                    if (column > 0) Sql.Append(", ");
                    var expression = columns[column];
                    if (empty) GenerateCollectionCast(expression);
                    else if (row == 0 && expression is SqlUnaryExpression { OperatorType: ExpressionType.Convert } conversion
                        && conversion.Type == conversion.Operand.Type
                        && ReferenceEquals(conversion.TypeMapping, conversion.Operand.TypeMapping))
                        GenerateCollectionCast(conversion.Operand);
                    else Visit(expression);
                    if (row == 0)
                        Sql.Append(AliasSeparator).Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(valuesExpression.ColumnNames[column]));
                }
                GeneratePseudoFromClause();
                if (empty) Sql.Append(" WHERE 1 = 0");
            }
        }

        private void GenerateCollectionCast(SqlExpression expression)
        {
            if (expression.TypeMapping is not { } mapping || !OceanBaseCollectionParameterExpandingVisitor.IsSupportedIntegral(mapping))
                throw new NotSupportedException("Collection typing casts currently require int/int? elements with signed int or long storage carriers.");
            // Use the lossless query carrier, not a property's narrower physical write declaration.
            Sql.Append("CAST(");
            Visit(expression);
            Sql.Append(_mode == OceanBaseCompatibilityMode.MySql ? " AS SIGNED)" : " AS NUMBER)");
        }

        protected override Expression VisitSqlBinary(SqlBinaryExpression sqlBinaryExpression)
        {
            if (sqlBinaryExpression.OperatorType is ExpressionType.And or ExpressionType.Or or ExpressionType.ExclusiveOr
                && IntegerCode(sqlBinaryExpression.Type) is { } code)
            {
                ValidateInteger(sqlBinaryExpression);
                ValidateInteger(sqlBinaryExpression.Left);
                ValidateInteger(sqlBinaryExpression.Right);
                if (_mode == OceanBaseCompatibilityMode.MySql)
                {
                    var signed = UnsignedMaximum(code) is null;
                    Sql.Append(signed ? "CAST(" : "(");
                    BitwiseOperand(sqlBinaryExpression.Left);
                    Sql.Append(sqlBinaryExpression.OperatorType == ExpressionType.And ? " & "
                        : sqlBinaryExpression.OperatorType == ExpressionType.Or ? " | " : " ^ ");
                    BitwiseOperand(sqlBinaryExpression.Right);
                    Sql.Append(signed ? " AS SIGNED)" : ")");
                }
                else if (sqlBinaryExpression.OperatorType == ExpressionType.And)
                    OracleBitAnd(sqlBinaryExpression, code);
                else
                {
                    Sql.Append("(");
                    BitwiseOperand(sqlBinaryExpression.Left);
                    Sql.Append(" + ");
                    BitwiseOperand(sqlBinaryExpression.Right);
                    Sql.Append(sqlBinaryExpression.OperatorType == ExpressionType.Or ? " - " : " - 2 * ");
                    OracleBitAnd(sqlBinaryExpression, code);
                    Sql.Append(")");
                }
                return sqlBinaryExpression;
            }

            if (sqlBinaryExpression.OperatorType == ExpressionType.Add && sqlBinaryExpression.Type == typeof(string))
            {
                Sql.Append(_mode == OceanBaseCompatibilityMode.Oracle ? "(" : "CONCAT(");
                Visit(sqlBinaryExpression.Left);
                Sql.Append(_mode == OceanBaseCompatibilityMode.Oracle ? " || " : ", ");
                Visit(sqlBinaryExpression.Right);
                Sql.Append(")");
                return sqlBinaryExpression;
            }

            if (_mode == OceanBaseCompatibilityMode.Oracle)
            {
                if (IsIntegerDivision(sqlBinaryExpression))
                {
                    Sql.Append("TRUNC(");
                    base.VisitSqlBinary(sqlBinaryExpression);
                    Sql.Append(")");
                    return sqlBinaryExpression;
                }

                if (sqlBinaryExpression.OperatorType == ExpressionType.Modulo)
                {
                    Sql.Append("MOD(");
                    Visit(sqlBinaryExpression.Left);
                    Sql.Append(", ");
                    Visit(sqlBinaryExpression.Right);
                    Sql.Append(")");
                    return sqlBinaryExpression;
                }
            }

            return base.VisitSqlBinary(sqlBinaryExpression);
        }

        protected override string GetOperator(SqlBinaryExpression binaryExpression)
            => _mode == OceanBaseCompatibilityMode.MySql && IsIntegerDivision(binaryExpression)
                ? " DIV " : base.GetOperator(binaryExpression);

        protected override Expression VisitSqlUnary(SqlUnaryExpression expression)
        {
            // C# lowers enum complement to underlying conversions, promoting byte/short to Int32 first.
            if (expression is { OperatorType: ExpressionType.Convert, Operand: SqlUnaryExpression
                { OperatorType: ExpressionType.Not, Operand: SqlUnaryExpression
                    { OperatorType: ExpressionType.Convert } promotion } }
                && IntegerCode(expression.Type) is { } enumCode && IntegerCode(promotion.Operand.Type) == enumCode)
                return VisitSqlUnary(new SqlUnaryExpression(ExpressionType.Not, promotion.Operand,
                    expression.Type, expression.TypeMapping));

            if (expression.OperatorType is ExpressionType.Not or ExpressionType.OnesComplement
                && IntegerCode(expression.Type) is { } code)
            {
                ValidateInteger(expression);
                ValidateInteger(expression.Operand);
                var maximum = UnsignedMaximum(code);
                if (_mode == OceanBaseCompatibilityMode.Oracle)
                {
                    Sql.Append("(").Append(maximum ?? "-1").Append(" - ");
                    BitwiseOperand(expression.Operand);
                    Sql.Append(")");
                }
                else
                {
                    Sql.Append(maximum is null ? "CAST(~" : "(~");
                    BitwiseOperand(expression.Operand);
                    if (maximum is not null && code != TypeCode.UInt64)
                        Sql.Append(" & ").Append(maximum);
                    Sql.Append(maximum is null ? " AS SIGNED)" : ")");
                }
                return expression;
            }

            if (_mode == OceanBaseCompatibilityMode.MySql && expression.OperatorType == ExpressionType.Convert
                && IntegerCode(expression.Type) is { } target && IntegerCode(expression.Operand.Type) is not null
                && expression.TypeMapping is { } mapping && IntegerCode(mapping.Converter?.ProviderClrType ?? mapping.ClrType) is not null)
            {
                Sql.Append("CAST(");
                Visit(expression.Operand);
                Sql.Append(UnsignedMaximum(target) is null ? " AS SIGNED)" : " AS UNSIGNED)");
                return expression;
            }
            return base.VisitSqlUnary(expression);
        }

        private void BitwiseOperand(SqlExpression operand)
        {
            Sql.Append("(");
            Visit(operand);
            Sql.Append(")");
        }

        private void OracleBitAnd(SqlBinaryExpression expression, TypeCode code)
        {
            if (code == TypeCode.UInt64)
            {
                // OceanBase's NUMBER BITAND path converts to Int64; each half must stay below 2^32.
                Sql.Append("(BITAND(TRUNC(");
                BitwiseOperand(expression.Left);
                Sql.Append(" / 4294967296), TRUNC(");
                BitwiseOperand(expression.Right);
                Sql.Append(" / 4294967296)) * 4294967296 + BITAND(MOD(");
                Visit(expression.Left);
                Sql.Append(", 4294967296), MOD(");
                Visit(expression.Right);
                Sql.Append(", 4294967296)))");
            }
            else
            {
                Sql.Append("BITAND(");
                Visit(expression.Left);
                Sql.Append(", ");
                Visit(expression.Right);
                Sql.Append(")");
            }
        }

        private static void ValidateInteger(SqlExpression expression)
        {
            var mapping = expression.TypeMapping;
            if (mapping is null || mapping.Converter?.ConvertsNulls == true
                || IntegerCode(mapping.Converter?.ProviderClrType ?? mapping.ClrType) is null)
                throw new NotSupportedException("Integer bitwise operations require integer numeric storage carriers.");
            if (expression is SqlUnaryExpression { OperatorType: ExpressionType.Convert } conversion
                && IntegerCode(conversion.Operand.Type) is not null)
                ValidateInteger(conversion.Operand);
        }

        private static TypeCode? IntegerCode(Type type)
        {
            var code = Type.GetTypeCode(Nullable.GetUnderlyingType(type) ?? type);
            return code is TypeCode.SByte or TypeCode.Byte or TypeCode.Int16 or TypeCode.UInt16
                or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64 ? code : null;
        }

        private static string? UnsignedMaximum(TypeCode code)
            => code switch
            {
                TypeCode.Byte => "255", TypeCode.UInt16 => "65535", TypeCode.UInt32 => "4294967295",
                TypeCode.UInt64 => "18446744073709551615", _ => null
            };

        private static bool IsIntegerDivision(SqlBinaryExpression expression)
            => expression.OperatorType == ExpressionType.Divide
                && Type.GetTypeCode(Nullable.GetUnderlyingType(expression.Type) ?? expression.Type)
                    is TypeCode.SByte or TypeCode.Byte or TypeCode.Int16 or TypeCode.UInt16
                        or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64;

        protected override Expression VisitSqlFunction(SqlFunctionExpression sqlFunctionExpression)
        {
            if (sqlFunctionExpression.Name is OceanBaseSqlTranslatingExpressionVisitorFactory.LeftShiftMarker
                or OceanBaseSqlTranslatingExpressionVisitorFactory.RightShiftMarker)
                return GenerateShift(sqlFunctionExpression);

            if (sqlFunctionExpression.Name == OceanBaseMathTranslator.SingleResultMarker)
            {
                var mapping = sqlFunctionExpression.TypeMapping;
                var carrier = mapping?.Converter?.ProviderClrType ?? mapping?.ClrType;
                if (sqlFunctionExpression is not { IsBuiltIn: true, Instance: null, Schema: null, Arguments.Count: 1,
                        IsNullable: true, ArgumentsPropagateNullability: [false] }
                    || mapping is null || mapping.Converter?.ConvertsNulls == true
                    || sqlFunctionExpression.Type != mapping.ClrType
                    || !OceanBaseMathTranslator.IsNumeric(sqlFunctionExpression.Type)
                    || (Nullable.GetUnderlyingType(carrier!) ?? carrier) != typeof(float)
                    || sqlFunctionExpression.Arguments[0].Type != typeof(double)
                        && sqlFunctionExpression.Arguments[0].Type != sqlFunctionExpression.Type
                        // EF's SUM compensation derives COALESCE.Type from its Int32 zero constant.
                        && !IsCompensatedSingleSum(sqlFunctionExpression.Arguments[0], sqlFunctionExpression.Type, mapping))
                    throw new InvalidOperationException("Invalid internal Single result expression.");
                Sql.Append("CAST(");
                Visit(sqlFunctionExpression.Arguments[0]);
                Sql.Append(_mode == OceanBaseCompatibilityMode.MySql ? " AS FLOAT)" : " AS BINARY_FLOAT)");
                return sqlFunctionExpression;
            }

            if (_mode == OceanBaseCompatibilityMode.Oracle
                && sqlFunctionExpression.Name == OceanBaseTemporalMemberTranslator.ExtractMarker)
            {
                if (sqlFunctionExpression is not { IsBuiltIn: true, Arguments.Count: 2 }
                    || sqlFunctionExpression.Arguments[0] is not SqlFragmentExpression { Sql: var unit }
                    || unit is not ("YEAR" or "MONTH" or "DAY" or "HOUR" or "MINUTE" or "SECOND"))
                    throw new InvalidOperationException("Invalid internal EXTRACT expression.");

                Sql.Append("EXTRACT(").Append(unit).Append(" FROM ");
                Visit(sqlFunctionExpression.Arguments[1]);
                Sql.Append(")");
                return sqlFunctionExpression;
            }

            if (_mode == OceanBaseCompatibilityMode.Oracle
                && sqlFunctionExpression is { IsBuiltIn: true, Name: "CONCAT", Arguments.Count: 2, Type: var type }
                && type == typeof(string))
            {
                Sql.Append("(");
                Visit(sqlFunctionExpression.Arguments[0]);
                Sql.Append(" || ");
                Visit(sqlFunctionExpression.Arguments[1]);
                Sql.Append(")");
                return sqlFunctionExpression;
            }

            return base.VisitSqlFunction(sqlFunctionExpression);
        }

        private static bool IsCompensatedSingleSum(SqlExpression body, Type modelType, RelationalTypeMapping mapping)
            => body is SqlFunctionExpression { IsBuiltIn: true, Name: "COALESCE", Arguments:
                    [SqlFunctionExpression { IsBuiltIn: true, Name: "SUM" } sum, SqlConstantExpression { Value: 0 } zero] }
                && sum.Type == modelType && ReferenceEquals(sum.TypeMapping, mapping)
                && ReferenceEquals(body.TypeMapping, mapping) && ReferenceEquals(zero.TypeMapping, mapping);

        protected override void GeneratePseudoFromClause()
        {
            if (_mode == OceanBaseCompatibilityMode.Oracle)
            {
                Sql.AppendLine().Append("FROM DUAL");
            }
        }

        protected override void GenerateLimitOffset(SelectExpression selectExpression)
        {
            if (_mode == OceanBaseCompatibilityMode.Oracle)
            {
                base.GenerateLimitOffset(selectExpression);
                return;
            }

            if (selectExpression.Limit is not null || selectExpression.Offset is not null)
            {
                Sql.AppendLine().Append("LIMIT ");
                if (selectExpression.Limit is not null)
                {
                    Visit(selectExpression.Limit);
                }
                else
                {
                    Sql.Append("18446744073709551615");
                }

                if (selectExpression.Offset is not null)
                {
                    Sql.Append(" OFFSET ");
                    Visit(selectExpression.Offset);
                }
            }
        }
    }
}
