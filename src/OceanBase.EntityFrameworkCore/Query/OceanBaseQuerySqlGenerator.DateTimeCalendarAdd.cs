using System;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace OceanBase.EntityFrameworkCore.Query
{
    internal sealed partial class OceanBaseQuerySqlGenerator
    {
        private Expression GenerateDateTimeCalendarAdd(SqlFunctionExpression expression)
        {
            if (expression is not { IsBuiltIn: true, Schema: null, Instance: null, Arguments.Count: 2,
                    IsNullable: true, ArgumentsPropagateNullability: [false, false], Type: var resultType,
                    TypeMapping: { } resultMapping }
                || resultType != typeof(DateTime)
                || !OceanBaseTemporalMethodTranslator.IsDateTimeCalendarMapping(resultMapping, _mode)
                || expression.Arguments[0] is not { Type: var inputType, TypeMapping: { } inputMapping }
                || inputType != typeof(DateTime)
                || !OceanBaseTemporalMethodTranslator.IsDateTimeCalendarMapping(inputMapping, _mode)
                || expression.Arguments[1] is not { Type: var amountType, TypeMapping: { } amountMapping }
                || amountType != typeof(int) || amountMapping.Converter?.ConvertsNulls == true
                || (amountMapping.Converter?.ProviderClrType ?? amountMapping.ClrType) != typeof(int))
                throw new InvalidOperationException("Invalid internal DateTime calendar-add expression.");

            var input = expression.Arguments[0];
            var amount = expression.Arguments[1];
            var years = expression.Name == OceanBaseTemporalMethodTranslator.DateTimeAddYearsMarker;
            if (_mode == OceanBaseCompatibilityMode.MySql)
            {
                Sql.Append("DATE_ADD(");
                Visit(input);
                Sql.Append(", INTERVAL ");
                Visit(amount);
                Sql.Append(years ? " YEAR)" : " MONTH)");
                return expression;
            }

            // Clamp the day from a month-start anchor; ADD_MONTHS alone also loses timestamp fractions.
            var date = resultMapping.StoreTypeNameBase.Trim().Equals("DATE", StringComparison.OrdinalIgnoreCase);
            if (date) Sql.Append("CAST(");
            Sql.Append("(CAST(");
            AppendMonthStart();
            Sql.Append(" + LEAST(EXTRACT(DAY FROM ");
            Visit(input);
            Sql.Append(") - 1, EXTRACT(DAY FROM LAST_DAY(");
            AppendMonthStart();
            Sql.Append(")) - 1) AS TIMESTAMP(7)) + (CAST(");
            Visit(input);
            Sql.Append(" AS TIMESTAMP(7)) - CAST(TRUNC(");
            Visit(input);
            Sql.Append(") AS TIMESTAMP(7))))");
            if (date) Sql.Append(" AS DATE)");
            return expression;

            void AppendMonthStart()
            {
                Sql.Append("ADD_MONTHS(TRUNC(");
                Visit(input);
                Sql.Append(", 'MM'), ");
                if (years) Sql.Append("(");
                Visit(amount);
                if (years) Sql.Append(") * 12");
                Sql.Append(")");
            }
        }
    }
}
