using System;
using System.Linq;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace OceanBase.EntityFrameworkCore.Query
{
    internal sealed partial class OceanBaseQuerySqlGenerator
    {
        protected override void GenerateSetOperation(SetOperationBase setOperation)
        {
            if (_mode == OceanBaseCompatibilityMode.Oracle)
            {
                if (!setOperation.IsDistinct && setOperation is ExceptExpression or IntersectExpression)
                    throw new NotSupportedException("OceanBase Oracle mode does not support MINUS ALL or INTERSECT ALL.");
                if (HasLobProjection(setOperation.Source1) || HasLobProjection(setOperation.Source2))
                    throw new NotSupportedException("OceanBase Oracle mode set operations do not support CLOB, NCLOB or BLOB projections; project bounded scalar columns.");
                if (setOperation is ExceptExpression)
                {
                    GenerateSetOperationOperand(setOperation, setOperation.Source1);
                    Sql.AppendLine().AppendLine("MINUS");
                    GenerateSetOperationOperand(setOperation, setOperation.Source2);
                    return;
                }
            }
            base.GenerateSetOperation(setOperation);
        }

        private static bool HasLobProjection(SelectExpression select)
            => select.Projection.Any(p => p.Expression.TypeMapping?.StoreTypeNameBase.Trim().ToUpperInvariant() is "CLOB" or "NCLOB" or "BLOB");
    }
}
