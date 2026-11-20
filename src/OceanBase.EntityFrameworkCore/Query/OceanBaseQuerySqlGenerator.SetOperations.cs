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
            for (var index = 0; index < setOperation.Source1.Projection.Count; index++)
            {
                var left = setOperation.Source1.Projection[index].Expression;
                var right = setOperation.Source2.Projection[index].Expression;
                if (left.Type == typeof(TimeSpan) && right.Type == typeof(TimeSpan)
                    && OceanBaseTemporalMethodTranslator.IsTickDurationMapping(left.TypeMapping)
                        != OceanBaseTemporalMethodTranslator.IsTickDurationMapping(right.TypeMapping))
                    throw new NotSupportedException("Set operations mixing tick-based date differences and stored duration carriers are not supported.");
            }
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

        protected override void GenerateSetOperationOperand(SetOperationBase setOperation, SelectExpression operand)
        {
            // EF9 leaves nested operands of the same kind ungrouped, including non-associative difference.
            if (operand is
                {
                    Tables: [SetOperationBase nested],
                    Predicate: null, Orderings: [], Offset: null, Limit: null,
                    IsDistinct: false, Having: null, GroupBy: []
                }
                && nested.GetType() == setOperation.GetType()
                && (nested is ExceptExpression || nested.IsDistinct != setOperation.IsDistinct)
                && operand.Projection.Count == nested.Source1.Projection.Count
                && operand.Projection.Select((projection, i) => projection.Expression is ColumnExpression column
                    && column.TableAlias == nested.Alias && column.Name == nested.Source1.Projection[i].Alias).All(matches => matches))
            {
                Sql.AppendLine("(");
                using (Sql.Indent())
                {
                    Visit(operand);
                }
                Sql.AppendLine().Append(")");
                return;
            }

            base.GenerateSetOperationOperand(setOperation, operand);
        }

        private static bool HasLobProjection(SelectExpression select)
            => select.Projection.Any(p => p.Expression.TypeMapping?.StoreTypeNameBase.Trim().ToUpperInvariant() is "CLOB" or "NCLOB" or "BLOB");
    }
}
