using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace OceanBase.EntityFrameworkCore.Query
{
    internal sealed partial class OceanBaseQuerySqlGenerator
    {
        protected override Expression VisitCrossApply(CrossApplyExpression crossApplyExpression)
        {
            // EF may lift the correlated projection and leave a plain, uncorrelated table operand.
            var table = crossApplyExpression.Table is TableExpression;
            Sql.Append(table ? "CROSS JOIN " : "JOIN LATERAL ");
            Visit(crossApplyExpression.Table);
            if (!table) Sql.Append(" ON 1 = 1");
            return crossApplyExpression;
        }

        protected override Expression VisitOuterApply(OuterApplyExpression outerApplyExpression)
        {
            Sql.Append(outerApplyExpression.Table is TableExpression ? "LEFT JOIN " : "LEFT JOIN LATERAL ");
            Visit(outerApplyExpression.Table);
            Sql.Append(" ON 1 = 1");
            return outerApplyExpression;
        }
    }
}
