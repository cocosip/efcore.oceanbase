using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace OceanBase.EntityFrameworkCore.Query
{
    internal sealed partial class OceanBaseQuerySqlGenerator
    {
        private Expression GenerateMySqlSnapshotUpdate(UpdateExpression update)
        {
            var keys = OceanBaseBulkDmlVisitor.GetBulkCorrelationKey(update, _mode);
            var targetAlias = GetBulkAlias(update, "__ob_bulk_target");
            var snapshotAlias = GetBulkAlias(update, "__ob_bulk_snapshot");
            var helper = Dependencies.SqlGenerationHelper;
            var target = helper.DelimitIdentifier(targetAlias);
            var snapshot = helper.DelimitIdentifier(snapshotAlias);
            Sql.Append("UPDATE ").Append(helper.DelimitIdentifier(update.Table.Name, update.Table.Schema))
                .Append(" AS ").Append(target).AppendLine().Append("INNER JOIN (").AppendLine()
                .Append("SELECT /*+ NO_MERGE */ ");
            for (var index = 0; index < keys.Count; index++)
            {
                if (index > 0) Sql.Append(", ");
                Sql.Append(helper.DelimitIdentifier(update.Table.Alias)).Append(".")
                    .Append(helper.DelimitIdentifier(keys[index].Name)).Append(" AS ")
                    .Append(helper.DelimitIdentifier("__ob_key_" + index));
            }
            for (var index = 0; index < update.ColumnValueSetters.Count; index++)
            {
                Sql.Append(", ");
                Visit(update.ColumnValueSetters[index].Value);
                Sql.Append(" AS ").Append(helper.DelimitIdentifier("__ob_value_" + index));
            }
            var select = update.SelectExpression;
            GenerateFrom(select);
            if (select.Predicate is not null)
            {
                Sql.AppendLine().Append("WHERE ");
                Visit(select.Predicate);
            }
            Sql.AppendLine().Append(") AS ").Append(snapshot).Append(" ON ");
            for (var index = 0; index < keys.Count; index++)
            {
                if (index > 0) Sql.Append(" AND ");
                Sql.Append(target).Append(".").Append(helper.DelimitIdentifier(keys[index].Name))
                    .Append(" = ").Append(snapshot).Append(".").Append(helper.DelimitIdentifier("__ob_key_" + index));
            }
            Sql.AppendLine().Append("SET ");
            for (var index = 0; index < update.ColumnValueSetters.Count; index++)
            {
                if (index > 0) Sql.Append(", ");
                Sql.Append(target).Append(".").Append(helper.DelimitIdentifier(update.ColumnValueSetters[index].Column.Name))
                    .Append(" = ").Append(snapshot).Append(".").Append(helper.DelimitIdentifier("__ob_value_" + index));
            }
            return update;
        }
    }
}
