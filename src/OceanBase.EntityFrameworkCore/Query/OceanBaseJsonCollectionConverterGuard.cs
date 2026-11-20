using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace OceanBase.EntityFrameworkCore.Query
{
    internal sealed class OceanBaseJsonCollectionConverterGuard : ExpressionVisitor
    {
        protected override Expression VisitExtension(Expression node)
        {
            switch (node)
            {
                case ShapedQueryExpression shaped:
                    Visit(shaped.QueryExpression);
                    return shaped;
                case SqlBinaryExpression { OperatorType: not (ExpressionType.AndAlso or ExpressionType.OrElse) } binary:
                    Check(binary.Left, binary.Right);
                    break;
                case InExpression { Subquery.Projection: [var projection] } membership:
                    Check(membership.Item, projection.Expression);
                    break;
                case SqlExpression { TypeMapping.Converter: not null } scalar
                    when scalar.Type == typeof(int) || scalar.Type == typeof(long):
                    if (Inspect(scalar).HasJsonValue) ThrowContextualConverter();
                    break;
            }
            return base.VisitExtension(node);
        }

        private static void Check(SqlExpression left, SqlExpression right)
        {
            var lhs = Inspect(left);
            var rhs = Inspect(right);
            if ((lhs.HasConverter && rhs.HasJsonValue) || (rhs.HasConverter && lhs.HasJsonValue))
                ThrowContextualConverter();
        }

        private static OperandVisitor Inspect(SqlExpression expression)
        {
            var visitor = new OperandVisitor();
            visitor.Visit(expression);
            return visitor;
        }

        private static void ThrowContextualConverter()
            => throw new NotSupportedException("Single JSON collection parameters cannot use a contextual value converter.");

        private sealed class OperandVisitor : ExpressionVisitor
        {
            private readonly HashSet<ColumnExpression> _visitedColumns = new(ReferenceEqualityComparer.Instance);
            public bool HasJsonValue { get; private set; }
            public bool HasConverter { get; private set; }

            protected override Expression VisitExtension(Expression node)
            {
                if (node is SqlExpression { Type: var type } && type == typeof(bool)) return node;
                HasConverter |= node is SqlExpression { TypeMapping.Converter: not null };
                switch (node)
                {
                    case ColumnExpression column:
                        if (_visitedColumns.Add(column))
                            InspectColumn(column.Table is JoinExpressionBase join ? join.Table : column.Table, column.Name);
                        return node;
                    case ScalarSubqueryExpression { Subquery.Projection: [var projection] }:
                        Visit(projection.Expression);
                        return node;
                    case CaseExpression conditional:
                        foreach (var clause in conditional.WhenClauses) Visit(clause.Result);
                        Visit(conditional.ElseResult);
                        return node;
                }
                return base.VisitExtension(node);
            }

            private void InspectColumn(TableExpressionBase table, string name)
            {
                switch (table)
                {
                    case TableValuedFunctionExpression function when name == "Value" && function.FindAnnotation(
                        OceanBaseQueryableMethodTranslatingExpressionVisitorFactory.JsonCollectionAnnotation) is not null:
                        HasJsonValue = true;
                        break;
                    case SelectExpression select:
                        Visit(select.Projection.FirstOrDefault(projection => projection.Alias == name)?.Expression);
                        break;
                    case SetOperationBase set:
                        var index = set.Source1.Projection.ToList().FindIndex(projection => projection.Alias == name);
                        if (index >= 0)
                        {
                            Visit(set.Source1.Projection[index].Expression);
                            Visit(set.Source2.Projection[index].Expression);
                        }
                        break;
                }
            }
        }
    }
}
