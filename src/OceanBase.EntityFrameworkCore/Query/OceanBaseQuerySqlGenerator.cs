using System;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace OceanBase.EntityFrameworkCore.Query
{
    internal sealed class OceanBaseQuerySqlGenerator : QuerySqlGenerator
    {
        private readonly OceanBaseCompatibilityMode _mode;

        public OceanBaseQuerySqlGenerator(QuerySqlGeneratorDependencies dependencies, OceanBaseCompatibilityMode mode)
            : base(dependencies)
        {
            _mode = mode;
        }

        protected override string AliasSeparator => _mode == OceanBaseCompatibilityMode.Oracle ? " " : " AS ";

        protected override Expression VisitSqlBinary(SqlBinaryExpression sqlBinaryExpression)
        {
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

        private static bool IsIntegerDivision(SqlBinaryExpression expression)
            => expression.OperatorType == ExpressionType.Divide
                && Type.GetTypeCode(Nullable.GetUnderlyingType(expression.Type) ?? expression.Type)
                    is TypeCode.SByte or TypeCode.Byte or TypeCode.Int16 or TypeCode.UInt16
                        or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64;

        protected override Expression VisitSqlFunction(SqlFunctionExpression sqlFunctionExpression)
        {
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
