using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using OceanBase.EntityFrameworkCore.Storage;

namespace OceanBase.EntityFrameworkCore.Query
{
    internal sealed class OceanBaseBulkDmlVisitor(OceanBaseCompatibilityMode mode, ParametersCacheDecorator parameters,
        ISqlExpressionFactory factory) : ExpressionVisitor
    {
        protected override Expression VisitExtension(Expression node)
        {
            if (node is not UpdateExpression update) return base.VisitExtension(node);
            IReadOnlyList<ColumnValueSetter> setters = update.ColumnValueSetters
                .Select(setter => new ColumnValueSetter(setter.Column, EncodeValue(setter))).ToArray();
            var targets = new HashSet<(string Alias, string Name)>();
            foreach (var setter in setters)
            {
                if (!targets.Add((setter.Column.TableAlias, setter.Column.Name)))
                    throw new NotSupportedException("Bulk updates cannot assign the same column more than once.");
                if (setter.Column.TypeMapping is not IOceanBaseWriteValidation validation) continue;
                switch (setter.Value)
                {
                    case SqlConstantExpression constant:
                        validation.ValidateWriteValue(constant.Value);
                        break;
                    case SqlParameterExpression parameter:
                        validation.ValidateWriteValue(parameters.GetAndDisableCaching()[parameter.Name]);
                        break;
                }
            }
            if (mode == OceanBaseCompatibilityMode.Oracle)
            {
                if (update.SelectExpression.Tables.Count != 1 || update.SelectExpression.Tables[0] != update.Table)
                    throw new NotSupportedException("Oracle joined bulk updates require a native rewrite that is not implemented.");
                return update.Update(update.SelectExpression, setters);
            }

            var reads = setters.Select(setter =>
            {
                var visitor = new ColumnReads();
                visitor.Visit(setter.Value);
                visitor.Columns.Remove((setter.Column.TableAlias, setter.Column.Name));
                return visitor.Columns;
            }).ToArray();
            if (update.SelectExpression.Tables.Count > 1 && reads.Any(columns => columns.Overlaps(targets)))
                throw new NotSupportedException("Dependent MySQL joined assignments require snapshot values because multi-table assignment order is not guaranteed.");
            var remaining = Enumerable.Range(0, setters.Count).ToList();
            var ordered = new List<ColumnValueSetter>(setters.Count);
            while (remaining.Count > 0)
            {
                // A reader must run before another assignment overwrites the column it reads.
                var next = remaining.FindIndex(candidate => !remaining.Any(reader =>
                    reads[reader].Contains((setters[candidate].Column.TableAlias, setters[candidate].Column.Name))));
                if (next < 0)
                    throw new NotSupportedException("Cyclic MySQL bulk assignments require snapshot-based simultaneous values, which are not implemented.");
                ordered.Add(setters[remaining[next]]);
                remaining.RemoveAt(next);
            }
            return update.Update(update.SelectExpression, ordered);
        }

        private SqlExpression EncodeValue(ColumnValueSetter setter)
        {
            var value = setter.Value;
            if (value is SqlConstantExpression or SqlParameterExpression) return value;
            var target = setter.Column.TypeMapping!;
            var source = value.TypeMapping;
            if (ReferenceEquals(target.Converter, source?.Converter)) return value;
            if ((Nullable.GetUnderlyingType(value.Type) ?? value.Type) == typeof(bool)
                && target.Converter?.ConvertsNulls != true && source?.Converter?.ConvertsNulls != true)
            {
                return factory.Case(
                    [new CaseWhenClause(factory.Equal(value, factory.Constant(true, source)), factory.Constant(true, target)),
                        new CaseWhenClause(factory.Equal(value, factory.Constant(false, source)), factory.Constant(false, target))],
                    factory.Constant(null, value.Type, target));
            }
            throw new NotSupportedException("Bulk server expressions with incompatible value converters require an explicit provider translation.");
        }

        private sealed class ColumnReads : ExpressionVisitor
        {
            public HashSet<(string Alias, string Name)> Columns { get; } = [];
            protected override Expression VisitExtension(Expression node)
            {
                if (node is ColumnExpression column) Columns.Add((column.TableAlias, column.Name));
                return base.VisitExtension(node);
            }
        }
    }
}
