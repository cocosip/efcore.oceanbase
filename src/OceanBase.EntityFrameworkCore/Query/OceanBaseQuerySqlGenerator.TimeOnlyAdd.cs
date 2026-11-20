using System;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace OceanBase.EntityFrameworkCore.Query
{
    internal sealed partial class OceanBaseQuerySqlGenerator
    {
        private Expression GenerateTimeOnlyAdd(SqlFunctionExpression expression)
        {
            var clock = expression.Name == OceanBaseTemporalMethodTranslator.TimeOnlyFromTicksMarker;
            if (expression is not { IsBuiltIn: true, Schema: null, Instance: null, Arguments.Count: 1,
                    IsNullable: true, ArgumentsPropagateNullability: [false], TypeMapping: { } resultMapping }
                || expression.Arguments[0].TypeMapping is not { } inputMapping
                || (clock ? expression.Type != typeof(TimeOnly) || !OceanBaseTemporalMethodTranslator.IsClockMapping(resultMapping, _mode)
                        || expression.Arguments[0].Type != typeof(long) || !OceanBaseTemporalMethodTranslator.IsTickOffsetMapping(inputMapping, _mode)
                    : expression.Type != typeof(long) || !OceanBaseTemporalMethodTranslator.IsTickOffsetMapping(resultMapping, _mode)
                        || expression.Arguments[0].Type != typeof(double) || !OceanBaseTemporalMethodTranslator.IsNumericOffsetMapping(inputMapping, _mode)))
                throw new InvalidOperationException("Invalid internal TimeOnly addition expression.");
            var input = expression.Arguments[0];
            if (clock)
            {
                if (_mode == OceanBaseCompatibilityMode.MySql)
                {
                    Sql.Append("(CASE WHEN MOD(");
                    Visit(input);
                    Sql.Append(", 10) = 0 THEN CAST(TIMESTAMPADD(MICROSECOND, (");
                    Visit(input);
                    // TIMESTAMPADD inherits its anchor precision before the outer TIME cast.
                    Sql.Append(") / 10, CAST(TIMESTAMP '2000-01-01 00:00:00' AS DATETIME(6))) AS TIME(6)) END)");
                }
                else
                {
                    Sql.Append("CAST(NUMTODSINTERVAL((");
                    Visit(input);
                    Sql.Append(") / 10000000, 'SECOND') AS ").Append(resultMapping.StoreType).Append(")");
                }
                return expression;
            }

            // .NET 10 TimeOnly casts the entire binary product to Int64 with saturation, then wraps ticks.
            var scale = expression.Name == OceanBaseTemporalMethodTranslator.TimeOnlyHoursToTicksMarker ? "36000000000" : "600000000";
            var suffix = _mode == OceanBaseCompatibilityMode.MySql ? "E0" : "D";
            Sql.Append("(CASE WHEN ");
            Visit(input);
            Sql.Append(" >= 9223372036854775808").Append(suffix).Append(" THEN 9223372036854775807 WHEN ");
            Visit(input);
            Sql.Append(" <= -9223372036854775808").Append(suffix).Append(" THEN -9223372036854775808 ELSE CASE WHEN ");
            Product();
            Sql.Append(" >= 9223372036854775808").Append(suffix).Append(" THEN 9223372036854775807 WHEN ");
            Product();
            Sql.Append(" <= -9223372036854775808").Append(suffix).Append(" THEN -9223372036854775808 ELSE ");
            if (_mode == OceanBaseCompatibilityMode.MySql)
            {
                Sql.Append("CAST(TRUNCATE(");
                Product();
                Sql.Append(", 0) AS SIGNED)");
            }
            else
            {
                // Direct floating-to-decimal casts lose low ticks near Int64 limits; convert exact 32-bit pieces.
                Sql.Append("(CAST(TRUNC(");
                Product();
                Sql.Append(" / 4294967296D) AS NUMBER(10,0)) * 4294967296 + CAST(TRUNC(MOD(");
                Product();
                Sql.Append(", 4294967296D)) AS NUMBER(10,0)))");
            }
            Sql.Append(" END END)");
            return expression;

            void Product()
            {
                Sql.Append("(");
                Visit(input);
                Sql.Append(" * ").Append(scale).Append(suffix).Append(")");
            }
        }
    }
}
