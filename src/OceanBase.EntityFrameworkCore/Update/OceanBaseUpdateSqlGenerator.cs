using System;
using System.Linq;
using System.Text;
using Microsoft.EntityFrameworkCore.Update;

namespace OceanBase.EntityFrameworkCore.Update
{
    internal sealed class OceanBaseUpdateSqlGenerator : UpdateSqlGenerator
    {
        private readonly OceanBaseCompatibilityMode _mode;

        public OceanBaseUpdateSqlGenerator(UpdateSqlGeneratorDependencies dependencies, OceanBaseCompatibilityMode mode)
            : base(dependencies)
        {
            _mode = mode;
        }

        public override ResultSetMapping AppendInsertOperation(
            StringBuilder builder, IReadOnlyModificationCommand command, int commandPosition, out bool requiresTransaction)
        {
            var writes = command.ColumnModifications.Where(column => column.IsWrite).ToList();
            var reads = command.ColumnModifications.Where(column => column.IsRead).ToList();
            requiresTransaction = _mode == OceanBaseCompatibilityMode.MySql && reads.Count > 0;
            if (writes.Count == 0)
            {
                builder.Append("INSERT INTO ");
                SqlGenerationHelper.DelimitIdentifier(builder, command.TableName, command.Schema);
                if (_mode == OceanBaseCompatibilityMode.MySql)
                {
                    builder.Append(" () VALUES ()");
                }
                else
                {
                    var key = command.ColumnModifications.FirstOrDefault(column => column.IsKey)
                        ?? throw new NotSupportedException("An Oracle default-only insert requires a mapped key column.");
                    builder.Append(" (");
                    SqlGenerationHelper.DelimitIdentifier(builder, key.ColumnName);
                    builder.Append(") VALUES (DEFAULT)");
                }

                AppendReturningClause(builder, reads);
                builder.AppendLine(SqlGenerationHelper.StatementTerminator);
            }
            else
            {
                AppendInsertCommand(builder, command.TableName, command.Schema, writes, reads);
            }

            return ResultSetMapping.NoResults;
        }

        public override ResultSetMapping AppendUpdateOperation(
            StringBuilder builder, IReadOnlyModificationCommand command, int commandPosition, out bool requiresTransaction)
        {
            var reads = command.ColumnModifications.Where(column => column.IsRead).ToList();
            requiresTransaction = _mode == OceanBaseCompatibilityMode.MySql && reads.Count > 0;
            AppendUpdateCommand(builder, command.TableName, command.Schema,
                command.ColumnModifications.Where(column => column.IsWrite).ToList(), reads,
                command.ColumnModifications.Where(column => column.IsCondition).ToList());
            return ResultSetMapping.NoResults;
        }

        public override ResultSetMapping AppendDeleteOperation(
            StringBuilder builder, IReadOnlyModificationCommand command, int commandPosition, out bool requiresTransaction)
        {
            requiresTransaction = false;
            AppendDeleteCommand(builder, command.TableName, command.Schema, [],
                command.ColumnModifications.Where(column => column.IsCondition).ToList());
            return ResultSetMapping.NoResults;
        }

        protected override void AppendReturningClause(
            StringBuilder builder, System.Collections.Generic.IReadOnlyList<IColumnModification> operations,
            string? additionalValues = null)
        {
            if (_mode != OceanBaseCompatibilityMode.Oracle || operations.Count == 0)
            {
                return;
            }

            builder.Append(" RETURNING ");
            for (var index = 0; index < operations.Count; index++)
            {
                if (index > 0) builder.Append(", ");
                SqlGenerationHelper.DelimitIdentifier(builder, operations[index].ColumnName);
            }

            builder.Append(" INTO ");
            for (var index = 0; index < operations.Count; index++)
            {
                if (index > 0) builder.Append(", ");
                SqlGenerationHelper.GenerateParameterNamePlaceholder(builder, "ob_out_" + index);
            }
        }
    }
}
