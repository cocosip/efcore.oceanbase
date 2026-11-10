using System;
using System.Collections.Generic;
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

        protected override void GenerateValues(ValuesExpression valuesExpression)
        {
            var rows = valuesExpression.RowValues
                ?? throw new InvalidOperationException("Collection parameters must be expanded before SQL generation.");
            var empty = rows.Count == 0;
            var prototypes = empty ? valuesExpression.FindAnnotation(OceanBaseCollectionParameterExpandingVisitor.EmptyColumnsAnnotation)
                ?.Value as IReadOnlyList<SqlExpression> : null;
            if (empty && (prototypes is null || prototypes.Count != valuesExpression.ColumnNames.Count))
                throw new NotSupportedException("An empty inline collection root has no retained element mapping; only captured empty integral roots are supported.");
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
