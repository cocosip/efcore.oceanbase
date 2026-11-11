using System;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace OceanBase.EntityFrameworkCore.Query
{
    internal sealed partial class OceanBaseQuerySqlGenerator
    {
        private Expression GenerateShift(SqlFunctionExpression expression)
        {
            if (expression is not { IsBuiltIn: true, Instance: null, Schema: null, Arguments.Count: 2,
                    IsNullable: true, ArgumentsPropagateNullability: [true, true] }
                || IntegerCode(expression.Type) is not (TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64)
                || expression.Type != expression.Arguments[0].Type || expression.Arguments[1].Type != typeof(int)
                || !ReferenceEquals(expression.TypeMapping, expression.Arguments[0].TypeMapping))
                throw new InvalidOperationException("Invalid internal integer shift expression.");
            var value = expression.Arguments[0];
            var count = expression.Arguments[1];
            ValidateInteger(value);
            ValidateInteger(count);
            var code = IntegerCode(expression.Type)!.Value;
            var width = code is TypeCode.Int64 or TypeCode.UInt64 ? 64 : 32;
            var signed = UnsignedMaximum(code) is null;
            var left = expression.Name == OceanBaseSqlTranslatingExpressionVisitorFactory.LeftShiftMarker;
            if (_mode == OceanBaseCompatibilityMode.MySql)
            {
                if (left)
                {
                    Sql.Append(signed ? width == 32 ? "(CAST(((" : "CAST(" : "(");
                    MySqlShift(value, count, width, left: true);
                    if (width == 32) Sql.Append(" & 4294967295)");
                    if (signed && width == 32) Sql.Append(" ^ 2147483648)");
                    Sql.Append(signed ? " AS SIGNED)" : width == 32 ? "" : ")");
                    if (signed && width == 32) Sql.Append(" - 2147483648)");
                }
                else if (signed)
                {
                    Sql.Append("(CASE WHEN ");
                    BitwiseOperand(value);
                    Sql.Append(" < 0 THEN CAST(~");
                    MySqlShift(value, count, width, left: false, complement: true);
                    Sql.Append(" AS SIGNED) ELSE CAST(");
                    MySqlShift(value, count, width, left: false);
                    Sql.Append(" AS SIGNED) END)");
                }
                else
                    MySqlShift(value, count, width, left: false);
            }
            else if (left)
            {
                var half = width == 32 ? "2147483648" : "9223372036854775808";
                var modulus = width == 32 ? "4294967296" : "18446744073709551616";
                Sql.Append(signed ? "(MOD(" : "(");
                Sql.Append("MOD(MOD(");
                Visit(value);
                Sql.Append(", ");
                OracleShiftPower(count, width, remaining: true);
                Sql.Append(") + ");
                OracleShiftPower(count, width, remaining: true);
                Sql.Append(", ");
                OracleShiftPower(count, width, remaining: true);
                Sql.Append(") * ");
                OracleShiftPower(count, width, remaining: false);
                if (signed) Sql.Append(" + ").Append(half).Append(", ").Append(modulus).Append(") - ").Append(half);
                Sql.Append(")");
            }
            else
            {
                Sql.Append("FLOOR(");
                BitwiseOperand(value);
                Sql.Append(" / ");
                OracleShiftPower(count, width, remaining: false);
                Sql.Append(")");
            }
            return expression;
        }

        private void MySqlShift(SqlExpression value, SqlExpression count, int width, bool left, bool complement = false)
        {
            Sql.Append("(");
            if (complement) Sql.Append("(~");
            BitwiseOperand(value);
            if (complement) Sql.Append(")");
            Sql.Append(left ? " << " : " >> ");
            ShiftCount(count, width);
            Sql.Append(")");
        }

        private void OracleShiftPower(SqlExpression count, int width, bool remaining)
        {
            Sql.Append("POWER(2, ");
            if (remaining) Sql.Append(width == 32 ? "32 - " : "64 - ");
            ShiftCount(count, width);
            Sql.Append(")");
        }

        private void ShiftCount(SqlExpression count, int width)
        {
            Sql.Append(_mode == OceanBaseCompatibilityMode.MySql ? "(" : "BITAND(");
            if (_mode == OceanBaseCompatibilityMode.MySql) BitwiseOperand(count);
            else Visit(count);
            Sql.Append(_mode == OceanBaseCompatibilityMode.MySql ? " & " : ", ");
            Sql.Append(width == 32 ? "31)" : "63)");
        }
    }
}
