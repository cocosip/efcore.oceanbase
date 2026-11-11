using System;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace OceanBase.EntityFrameworkCore.Query
{
    internal sealed partial class OceanBaseQuerySqlGenerator
    {
        protected override Expression VisitUpdate(UpdateExpression update)
        {
            if (_mode == OceanBaseCompatibilityMode.Oracle)
                return OceanBaseBulkDmlVisitor.RequiresOracleRewrite(update) ? GenerateOracleCorrelatedUpdate(update) : base.VisitUpdate(update);
            var select = update.SelectExpression;
            if (select.Offset is not null || select.Limit is not null || select.IsDistinct || select.Having is not null
                || select.Orderings.Count != 0 || select.GroupBy.Count != 0 || select.Projection.Count != 0
                || select.Tables.Count == 0)
                throw new NotSupportedException("Unsupported MySQL bulk update query shape.");

            Sql.Append("UPDATE ");
            for (var i = 0; i < select.Tables.Count; i++)
            {
                if (i > 0) Sql.AppendLine();
                Visit(select.Tables[i]);
            }
            Sql.AppendLine().Append("SET ");
            for (var i = 0; i < update.ColumnValueSetters.Count; i++)
            {
                if (i > 0) Sql.Append(",").AppendLine();
                Visit(update.ColumnValueSetters[i].Column);
                Sql.Append(" = ");
                Visit(update.ColumnValueSetters[i].Value);
            }
            if (select.Predicate is not null)
            {
                Sql.AppendLine().Append("WHERE ");
                Visit(select.Predicate);
            }
            return update;
        }
    }
}
