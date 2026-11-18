using System;
using System.Globalization;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace OceanBase.EntityFrameworkCore.Query
{
    internal sealed partial class OceanBaseQuerySqlGenerator
    {
        private Expression GenerateDateTimeNumericAdd(SqlFunctionExpression expression)
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
                || amountType != typeof(double) || !OceanBaseTemporalMethodTranslator.IsNumericOffsetMapping(amountMapping, _mode))
                throw new InvalidOperationException("Invalid internal DateTime numeric-add expression.");

            var ticksPerUnit = expression.Name == OceanBaseTemporalMethodTranslator.DateTimeAddDaysMarker
                ? TimeSpan.TicksPerDay : TimeSpan.TicksPerHour;
            var scale = ticksPerUnit.ToString(CultureInfo.InvariantCulture);
            var amount = expression.Arguments[1];
            Sql.Append("(CASE WHEN ABS(");
            Visit(amount);
            Sql.Append(") <= ").Append((DateTime.MaxValue.Ticks / ticksPerUnit).ToString(CultureInfo.InvariantCulture)).Append(" THEN ");
            if (_mode == OceanBaseCompatibilityMode.MySql)
            {
                // A CLR tick below MySQL's microsecond precision must not be rounded away.
                Sql.Append("CASE WHEN MOD(");
                AppendTicks();
                Sql.Append(", 10) = 0 THEN CAST(TIMESTAMPADD(MICROSECOND, (");
                AppendTicks();
                Sql.Append(") / 10, ");
                Visit(expression.Arguments[0]);
                Sql.Append(") AS DATETIME(6)) END");
            }
            else
            {
                // Keep intervals positive: target OceanBase loses negative subsecond interval signs.
                Sql.Append("CAST((CASE WHEN ");
                Visit(amount);
                Sql.Append(" < 0 THEN ");
                AppendOracleArithmetic(subtract: true);
                Sql.Append(" ELSE ");
                AppendOracleArithmetic(subtract: false);
                Sql.Append(" END) AS TIMESTAMP(7))");
            }
            Sql.Append(" END)");
            return expression;

            void AppendOracleArithmetic(bool subtract)
            {
                Sql.Append("(CAST(");
                Visit(expression.Arguments[0]);
                Sql.Append(subtract ? " AS TIMESTAMP(7)) - NUMTODSINTERVAL(ABS(" : " AS TIMESTAMP(7)) + NUMTODSINTERVAL(ABS(");
                AppendTicks();
                Sql.Append(") / 10000000, 'SECOND'))");
            }

            void AppendTicks()
            {
                // .NET AddUnits truncates the whole and fractional parts separately, before adding integer ticks.
                Sql.Append("(CAST(");
                AppendTruncatedAmount();
                Sql.Append(_mode == OceanBaseCompatibilityMode.MySql ? " AS DECIMAL(19,0)) * " : " AS NUMBER(19,0)) * ");
                Sql.Append(scale).Append(" + CAST(");
                Sql.Append(_mode == OceanBaseCompatibilityMode.MySql ? "TRUNCATE((" : "TRUNC((");
                Visit(amount);
                Sql.Append(" - ");
                AppendTruncatedAmount();
                Sql.Append(") * ").Append(scale).Append(_mode == OceanBaseCompatibilityMode.MySql ? "E0" : "D");
                Sql.Append(_mode == OceanBaseCompatibilityMode.MySql ? ", 0) AS DECIMAL(19,0)))" : ") AS NUMBER(19,0)))");
            }

            void AppendTruncatedAmount()
            {
                Sql.Append(_mode == OceanBaseCompatibilityMode.MySql ? "TRUNCATE(" : "TRUNC(");
                Visit(amount);
                Sql.Append(_mode == OceanBaseCompatibilityMode.MySql ? ", 0)" : ")");
            }
        }

        private Expression GenerateDateTimeDurationAdd(SqlFunctionExpression expression)
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
                || amountType != typeof(TimeSpan) || !OceanBaseTemporalMethodTranslator.IsDurationMapping(amountMapping, _mode))
                throw new InvalidOperationException("Invalid internal DateTime duration-add expression.");

            var subtract = expression.Name == OceanBaseTemporalMethodTranslator.DateTimeSubtractDurationMarker;
            if (_mode == OceanBaseCompatibilityMode.MySql)
            {
                Sql.Append("CAST(TIMESTAMPADD(MICROSECOND, ");
                if (subtract) Sql.Append("-");
                Sql.Append("(TIME_TO_SEC(");
                Visit(expression.Arguments[1]);
                // OceanBase TIME_TO_SEC omits fractions; restore their sign even below one second.
                Sql.Append(") * 1000000 + ");
                AppendMySqlTimeSpanSign(expression.Arguments[1]);
                Sql.Append(" * ABS(MICROSECOND(");
                Visit(expression.Arguments[1]);
                Sql.Append("))), ");
                Visit(expression.Arguments[0]);
                Sql.Append(") AS DATETIME(6))");
            }
            else
            {
                Sql.Append("CAST((CAST(");
                Visit(expression.Arguments[0]);
                Sql.Append(subtract ? " AS TIMESTAMP(7)) - (" : " AS TIMESTAMP(7)) + (");
                Visit(expression.Arguments[1]);
                Sql.Append(")) AS TIMESTAMP(7))");
            }
            return expression;
        }

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
            AppendOracleCalendarDate(input, amount, years);
            Sql.Append(" AS TIMESTAMP(7)) + (CAST(");
            Visit(input);
            Sql.Append(" AS TIMESTAMP(7)) - CAST(TRUNC(");
            Visit(input);
            Sql.Append(") AS TIMESTAMP(7))))");
            if (date) Sql.Append(" AS DATE)");
            return expression;
        }

        private void AppendOracleCalendarDate(SqlExpression input, SqlExpression amount, bool years)
        {
            AppendMonthStart();
            Sql.Append(" + LEAST(EXTRACT(DAY FROM ");
            Visit(input);
            Sql.Append(") - 1, EXTRACT(DAY FROM LAST_DAY(");
            AppendMonthStart();
            Sql.Append(")) - 1)");

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
