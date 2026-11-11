using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace OceanBase.EntityFrameworkCore.Query
{
    internal sealed class OceanBaseSearchConditionVisitor(ISqlExpressionFactory factory) : ExpressionVisitor
    {
        private bool _condition;

        private Expression? VisitInContext(Expression? expression, bool condition)
        {
            var previous = _condition;
            _condition = condition;
            try
            {
                return Visit(expression);
            }
            finally
            {
                _condition = previous;
            }
        }

        protected override Expression VisitExtension(Expression node)
        {
            var condition = _condition;
            switch (node)
            {
                case SelectExpression select:
                    return select.Update(
                        select.Tables.Select(table => (TableExpressionBase)VisitInContext(table, false)!).ToArray(),
                        (SqlExpression?)VisitInContext(select.Predicate, true),
                        select.GroupBy.Select(group => (SqlExpression)VisitInContext(group, false)!).ToArray(),
                        (SqlExpression?)VisitInContext(select.Having, true),
                        select.Projection.Select(projection => (ProjectionExpression)VisitInContext(projection, false)!).ToArray(),
                        select.Orderings.Select(ordering => (OrderingExpression)VisitInContext(ordering, false)!).ToArray(),
                        (SqlExpression?)VisitInContext(select.Offset, false),
                        (SqlExpression?)VisitInContext(select.Limit, false));
                case PredicateJoinExpressionBase join:
                    return join.Update((TableExpressionBase)VisitInContext(join.Table, false)!,
                        (SqlExpression)VisitInContext(join.JoinPredicate, true)!);
                case CaseExpression @case:
                    return Convert(@case.Update(
                        (SqlExpression?)VisitInContext(@case.Operand, false),
                        @case.WhenClauses.Select(clause => new CaseWhenClause(
                            (SqlExpression)VisitInContext(clause.Test, @case.Operand is null)!,
                            (SqlExpression)VisitInContext(clause.Result, false)!)).ToArray(),
                        (SqlExpression?)VisitInContext(@case.ElseResult, false)), condition, false);
                case SqlBinaryExpression binary:
                    var logical = binary.OperatorType is ExpressionType.AndAlso or ExpressionType.OrElse;
                    var comparison = logical || binary.OperatorType == ExpressionType.ExclusiveOr && binary.Type == typeof(bool)
                        || binary.OperatorType is ExpressionType.Equal or ExpressionType.NotEqual
                        or ExpressionType.LessThan or ExpressionType.LessThanOrEqual
                        or ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual;
                    return Convert(binary.Update(
                        (SqlExpression)VisitInContext(binary.Left, logical)!,
                        (SqlExpression)VisitInContext(binary.Right, logical)!), condition, comparison);
                case SqlUnaryExpression unary:
                    var negation = unary.OperatorType == ExpressionType.Not && unary.Type == typeof(bool);
                    return Convert(unary.Update((SqlExpression)VisitInContext(unary.Operand, negation)!), condition,
                        negation || unary.OperatorType is ExpressionType.Equal or ExpressionType.NotEqual);
                default:
                    _condition = false;
                    Expression visited;
                    try
                    {
                        visited = base.VisitExtension(node);
                    }
                    finally
                    {
                        _condition = condition;
                    }

                    return visited is SqlExpression sql
                        ? Convert(sql, condition, sql is ExistsExpression or InExpression or LikeExpression)
                        : visited;
            }
        }

        private SqlExpression Convert(SqlExpression expression, bool condition, bool searchCondition)
        {
            if (condition && !searchCondition)
            {
                return factory.Equal(expression, factory.Constant(true, expression.TypeMapping));
            }

            if (!condition && searchCondition)
            {
                // A single ELSE 0 would turn SQL UNKNOWN into false for nullable Boolean projections.
                return factory.Case(
                    [new CaseWhenClause(expression, factory.Constant(true, expression.TypeMapping)),
                        new CaseWhenClause(factory.Not(expression), factory.Constant(false, expression.TypeMapping))],
                    factory.Constant(null, typeof(bool), expression.TypeMapping));
            }

            return expression;
        }
    }
}
