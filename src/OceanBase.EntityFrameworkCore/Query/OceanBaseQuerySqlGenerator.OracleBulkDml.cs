using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace OceanBase.EntityFrameworkCore.Query
{
    internal sealed partial class OceanBaseQuerySqlGenerator
    {
        private Expression GenerateOracleCorrelatedUpdate(UpdateExpression update)
        {
            var key = OceanBaseBulkDmlVisitor.GetOracleCorrelationKey(update);
            var aliases = new BulkAliases();
            aliases.Visit(update);
            var outerAlias = "__ob_bulk_target";
            for (var suffix = 0; aliases.Names.Contains(outerAlias);)
                outerAlias = "__ob_bulk_target" + ++suffix;
            var helper = Dependencies.SqlGenerationHelper;
            Sql.Append("UPDATE ").Append(helper.DelimitIdentifier(update.Table.Name, update.Table.Schema))
                .Append(" ").Append(helper.DelimitIdentifier(outerAlias)).AppendLine().Append("SET ");
            for (var index = 0; index < update.ColumnValueSetters.Count; index++)
            {
                if (index > 0) Sql.Append(",").AppendLine();
                var setter = update.ColumnValueSetters[index];
                Sql.Append(helper.DelimitIdentifier(setter.Column.Name)).Append(" = (SELECT ");
                Visit(setter.Value);
                GenerateSource();
                Sql.Append(")");
            }
            Sql.AppendLine().Append("WHERE EXISTS (SELECT 1");
            GenerateSource();
            Sql.Append(")");
            return update;

            void GenerateSource()
            {
                var select = update.SelectExpression;
                GenerateFrom(select);
                Sql.AppendLine().Append("WHERE ");
                if (select.Predicate is not null)
                {
                    Sql.Append("(");
                    Visit(select.Predicate);
                    Sql.Append(") AND ");
                }
                for (var index = 0; index < key.Count; index++)
                {
                    if (index > 0) Sql.Append(" AND ");
                    var column = helper.DelimitIdentifier(key[index].Name);
                    Sql.Append(helper.DelimitIdentifier(update.Table.Alias)).Append(".").Append(column)
                        .Append(" = ").Append(helper.DelimitIdentifier(outerAlias)).Append(".").Append(column);
                }
            }
        }

        private sealed class BulkAliases : ExpressionVisitor
        {
            public HashSet<string> Names { get; } = new(StringComparer.OrdinalIgnoreCase);
            protected override Expression VisitExtension(Expression node)
            {
                if (node is TableExpressionBase { Alias: { } alias }) Names.Add(alias);
                if (node is ColumnExpression column) Names.Add(column.TableAlias);
                return base.VisitExtension(node);
            }
        }
    }
}
