using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;
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
                if (RequiresOracleRewrite(update)) GetBulkCorrelationKey(update, mode);
                return update.Update(update.SelectExpression, setters);
            }

            var ordered = GetMySqlAssignmentOrder(update.SelectExpression, setters);
            if (ordered is null) GetBulkCorrelationKey(update, mode);
            return update.Update(update.SelectExpression, ordered ?? setters);
        }

        // Null means assignment order cannot preserve original values and a snapshot is required.
        internal static IReadOnlyList<ColumnValueSetter>? GetMySqlAssignmentOrder(SelectExpression select,
            IReadOnlyList<ColumnValueSetter> setters)
        {
            var targets = setters.Select(setter => (setter.Column.TableAlias, setter.Column.Name)).ToHashSet();
            var reads = setters.Select(setter =>
            {
                var visitor = new ColumnReads();
                visitor.Visit(setter.Value);
                visitor.Columns.Remove((setter.Column.TableAlias, setter.Column.Name));
                return visitor.Columns;
            }).ToArray();
            // Derived projections can hide reads of assigned target columns, so snapshot them conservatively.
            if (select.Tables.Count > 1 && (reads.Any(columns => columns.Overlaps(targets))
                || select.Tables.Any(table => (table is JoinExpressionBase join ? join.Table : table) is SelectExpression))) return null;
            var remaining = Enumerable.Range(0, setters.Count).ToList();
            var ordered = new List<ColumnValueSetter>(setters.Count);
            while (remaining.Count > 0)
            {
                // A reader must run before another assignment overwrites the column it reads.
                var next = remaining.FindIndex(candidate => !remaining.Any(reader =>
                    reads[reader].Contains((setters[candidate].Column.TableAlias, setters[candidate].Column.Name))));
                if (next < 0) return null;
                ordered.Add(setters[remaining[next]]);
                remaining.RemoveAt(next);
            }
            return ordered;
        }

        internal static bool RequiresOracleRewrite(UpdateExpression update)
            => update.SelectExpression.Tables.Count != 1 || update.SelectExpression.Tables[0] != update.Table;

        internal static IReadOnlyList<IColumn> GetBulkCorrelationKey(UpdateExpression update, OceanBaseCompatibilityMode mode)
        {
            var select = update.SelectExpression;
            if (select.Offset is not null || select.Limit is not null || select.IsDistinct || select.Having is not null
                || select.Orderings.Count != 0 || select.GroupBy.Count != 0 || select.Projection.Count != 0
                || select.Tables.Count == 0)
                throw new NotSupportedException($"Unsupported {mode} correlated/snapshot bulk update query shape.");
            return (update.Table.Table as ITable)?.PrimaryKey?.Columns
                ?? throw new NotSupportedException($"{mode} correlated/snapshot bulk updates require a physical table primary key for correlation.");
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
