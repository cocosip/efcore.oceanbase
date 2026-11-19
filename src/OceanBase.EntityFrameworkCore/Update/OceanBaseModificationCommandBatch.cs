using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using OceanBaseConnector;
using OceanBase.EntityFrameworkCore.Storage;

namespace OceanBase.EntityFrameworkCore.Update
{
    // One row per batch keeps output binding and affected-row checks independent of multi-statement support.
    internal sealed class OceanBaseModificationCommandBatch : ModificationCommandBatch
    {
        private readonly ModificationCommandBatchFactoryDependencies _dependencies;
        private readonly OceanBaseCompatibilityMode _mode;
        private readonly List<IReadOnlyModificationCommand> _commands = [];
        private readonly Dictionary<string, object?> _values = [];
        private readonly List<(IColumnModification Column, DbParameter Parameter)> _outputs = [];
        private IRelationalCommand? _write;
        private IRelationalCommand? _read;
        private DbParameter? _rowsAffected;
        private bool _complete;
        private bool _requiresTransaction;
        private bool _moreBatches;

        public OceanBaseModificationCommandBatch(
            ModificationCommandBatchFactoryDependencies dependencies, OceanBaseCompatibilityMode mode)
        {
            _dependencies = dependencies;
            _mode = mode;
        }

        public override IReadOnlyList<IReadOnlyModificationCommand> ModificationCommands => _commands;
        public override bool RequiresTransaction => _requiresTransaction;
        public override bool AreMoreBatchesExpected => _moreBatches;

        public override bool TryAddCommand(IReadOnlyModificationCommand modificationCommand)
        {
            if (_complete) throw new InvalidOperationException("The command batch is already complete.");
            if (_commands.Count > 0) return false;
            _commands.Add(modificationCommand);
            return true;
        }

        public override void Complete(bool moreBatchesExpected)
        {
            if (_complete) throw new InvalidOperationException("The command batch is already complete.");
            var command = _commands.Single();
            if (command.StoreStoredProcedure is { } procedure)
            {
                _write = BuildProcedure(command, procedure);
                _requiresTransaction = true;
                _complete = true;
                _moreBatches = moreBatchesExpected;
                return;
            }

            var sql = new StringBuilder();
            switch (command.EntityState)
            {
                case EntityState.Added:
                    _dependencies.UpdateSqlGenerator.AppendInsertOperation(sql, command, 0, out _requiresTransaction);
                    break;
                case EntityState.Modified:
                    _dependencies.UpdateSqlGenerator.AppendUpdateOperation(sql, command, 0, out _requiresTransaction);
                    break;
                case EntityState.Deleted:
                    _dependencies.UpdateSqlGenerator.AppendDeleteOperation(sql, command, 0, out _requiresTransaction);
                    break;
                default:
                    throw new InvalidOperationException("Only insert, update and delete commands can be executed.");
            }

            var builder = _dependencies.CommandBuilderFactory.Create();
            builder.Append(sql.ToString());
            foreach (var column in command.ColumnModifications)
            {
                // NULL conditions use IS NULL; only NULL values actually written need a parameter.
                if (column.UseCurrentValueParameter && (column.IsWrite || column.Value is not null))
                    AddInput(builder, column, column.ParameterName!, column.Value);
                if (column.UseOriginalValueParameter && column.OriginalValue is not null)
                    AddInput(builder, column, column.OriginalParameterName!, column.OriginalValue);
            }

            var reads = command.ColumnModifications.Where(column => column.IsRead).ToList();
            if (_mode == OceanBaseCompatibilityMode.Oracle)
            {
                using var parameterCommand = new OceanBaseCommand();
                for (var index = 0; index < reads.Count; index++)
                {
                    var column = reads[index];
                    var name = "ob_out_" + index;
                    var parameter = column.TypeMapping!.CreateParameter(
                        parameterCommand, _dependencies.SqlGenerationHelper.GenerateParameterName(name), null,
                        column.IsNullable, ParameterDirection.Output);
                    builder.AddRawParameter(name, parameter);
                    _outputs.Add((column, parameter));
                }
            }
            else if (reads.Count > 0)
            {
                _read = BuildReadCommand(command, reads);
            }

            _write = builder.Build();
            _complete = true;
            _moreBatches = moreBatchesExpected;
        }

        private IRelationalCommand BuildProcedure(IReadOnlyModificationCommand command, IStoreStoredProcedure procedure)
        {
            var columns = command.ColumnModifications;
            if (procedure.ResultColumns.Any()
                || command.EntityState != EntityState.Added
                && command.RowsAffectedColumn is not (IStoreStoredProcedureParameter or IStoreStoredProcedureReturnValue))
            {
                throw new NotSupportedException("OceanBase routine mappings require output parameters for generated values and an output parameter or function return value for update/delete row counts.");
            }

            if (procedure.ReturnValue is not null && (command.EntityState == EntityState.Added
                || procedure.Parameters.Any(parameter => parameter.Direction != ParameterDirection.Input)))
            {
                throw new NotSupportedException("OceanBase rows-affected function mappings support update/delete with input-only arguments.");
            }

            var builder = _dependencies.CommandBuilderFactory.Create();
            builder.Append(_dependencies.SqlGenerationHelper.DelimitIdentifier(procedure.Name, procedure.Schema));
            OceanBaseRelationalCommandBuilderFactory.ConfigureStoredProcedure(builder);
            using var parameterCommand = new OceanBaseCommand();
            foreach (var column in columns.OrderBy(column => column.Column is IStoreStoredProcedureParameter parameter ? parameter.Position : -1))
            {
                if (column.Column is IStoreStoredProcedureReturnValue)
                {
                    const string returnName = "ob_result";
                    _rowsAffected = column.TypeMapping!.CreateParameter(
                        parameterCommand, returnName, null, column.IsNullable, ParameterDirection.ReturnValue);
                    builder.AddRawParameter(returnName, _rowsAffected);
                    continue;
                }

                var metadata = (IStoreStoredProcedureParameter)column.Column!;
                object? input = null;
                if (metadata.Direction is ParameterDirection.Input or ParameterDirection.InputOutput)
                {
                    if (column.UseOriginalValue)
                    {
                        input = column.OriginalValue;
                    }
                    else if (column.UseCurrentValue)
                    {
                        input = column.Value;
                    }
                }
                // The connector binds procedure arguments by position.
                var name = "ob_arg_" + metadata.Position;
                var parameter = column.TypeMapping!.CreateParameter(parameterCommand, name, input,
                    column.IsNullable, metadata.Direction);
                builder.AddRawParameter(name, parameter);
                if (metadata == command.RowsAffectedColumn)
                {
                    _rowsAffected = parameter;
                }
                else if (column.IsRead)
                {
                    _outputs.Add((column, parameter));
                }
            }

            return builder.Build();
        }

        private void AddInput(IRelationalCommandBuilder builder, IColumnModification column, string name, object? value)
        {
            if (_values.TryAdd(name, value))
            {
                builder.AddParameter(name, _dependencies.SqlGenerationHelper.GenerateParameterName(name),
                    column.TypeMapping!, column.IsNullable);
            }
        }

        private IRelationalCommand BuildReadCommand(IReadOnlyModificationCommand command, List<IColumnModification> reads)
        {
            var keys = command.ColumnModifications.Where(column => column.IsKey).ToList();
            if (keys.Count == 0 || keys.Count(column => column.IsRead) > 1)
            {
                throw new NotSupportedException("Generated MySQL values require a key with at most one generated identity column.");
            }

            foreach (var key in keys.Where(column => column.IsRead))
            {
                var type = key.TypeMapping!.Converter?.ProviderClrType ?? key.TypeMapping.ClrType;
                if (command.EntityState != EntityState.Added || type != typeof(int) && type != typeof(long)
                    || key.Property?.GetDefaultValueSql() is not null
                    || key.Property?.GetComputedColumnSql() is not null
                    || key.Property?.FindAnnotation(RelationalAnnotationNames.DefaultValue) is not null)
                {
                    throw new NotSupportedException(
                        "Generated MySQL keys require an integer identity without a configured default or computed expression.");
                }
            }

            var helper = _dependencies.SqlGenerationHelper;
            var builder = _dependencies.CommandBuilderFactory.Create();
            builder.Append("SELECT " + string.Join(", ", reads.Select(column => helper.DelimitIdentifier(column.ColumnName))));
            builder.Append(" FROM " + helper.DelimitIdentifier(command.TableName, command.Schema) + " WHERE ");
            for (var index = 0; index < keys.Count; index++)
            {
                if (index > 0) builder.Append(" AND ");
                var key = keys[index];
                builder.Append(helper.DelimitIdentifier(key.ColumnName) + " = ");
                if (key.IsRead && command.EntityState == EntityState.Added)
                {
                    builder.Append("LAST_INSERT_ID()");
                }
                else
                {
                    var name = "ob_key_" + index;
                    AddInput(builder, key, name, key.Value);
                    builder.Append(helper.GenerateParameterNamePlaceholder(name));
                }
            }

            return builder.Build();
        }

        public override void Execute(IRelationalConnection connection)
        {
            if (!_complete) throw new InvalidOperationException("The command batch must be completed before execution.");
            try
            {
                ValidateProcedureTransaction(connection);
                ValidateWriteValues();
                var affectedRows = ResolveRowsAffected(_write!.ExecuteNonQuery(Parameters(connection)));
                if (affectedRows != 1)
                {
                    ThrowConcurrencyException($"Expected one affected row, but OceanBase reported {affectedRows}.");
                    return;
                }
                if (_read is not null)
                {
                    using var reader = _read.ExecuteReader(Parameters(connection));
                    if (!reader.Read())
                    {
                        ThrowConcurrencyException("The generated row could not be read.");
                        return;
                    }
                    _commands[0].PropagateResults(reader);
                    // Connector releases the operation only after the complete readback response is consumed.
                    do { while (reader.Read()) { } } while (reader.DbDataReader.NextResult());
                }

                PropagateOutputs();
            }
            catch (Exception exception) when (exception is not DbUpdateException and not OperationCanceledException)
            {
                throw new DbUpdateException("An error occurred while saving OceanBase changes.", exception, _commands[0].Entries);
            }
        }

        public override async Task ExecuteAsync(IRelationalConnection connection, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_complete) throw new InvalidOperationException("The command batch must be completed before execution.");
            try
            {
                ValidateProcedureTransaction(connection);
                ValidateWriteValues();
                var affectedRows = ResolveRowsAffected(await _write!.ExecuteNonQueryAsync(Parameters(connection), cancellationToken).ConfigureAwait(false));
                if (affectedRows != 1)
                {
                    await ThrowConcurrencyExceptionAsync(
                        $"Expected one affected row, but OceanBase reported {affectedRows}.", cancellationToken).ConfigureAwait(false);
                    return;
                }
                if (_read is not null)
                {
                    await using var reader = await _read.ExecuteReaderAsync(Parameters(connection), cancellationToken).ConfigureAwait(false);
                    if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        await ThrowConcurrencyExceptionAsync("The generated row could not be read.", cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    _commands[0].PropagateResults(reader);
                    do { while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) { } }
                    while (await reader.DbDataReader.NextResultAsync(cancellationToken).ConfigureAwait(false));
                }

                PropagateOutputs();
            }
            catch (Exception exception) when (exception is not DbUpdateException and not OperationCanceledException)
            {
                throw new DbUpdateException("An error occurred while saving OceanBase changes.", exception, _commands[0].Entries);
            }
        }

        private RelationalCommandParameterObject Parameters(IRelationalConnection connection)
            => new(connection, _values, null, _dependencies.CurrentContext.Context, _dependencies.Logger, CommandSource.SaveChanges);

        private void ValidateProcedureTransaction(IRelationalConnection connection)
        {
            if (_commands[0].StoreStoredProcedure is not null && connection.CurrentTransaction is null)
            {
                throw new InvalidOperationException("OceanBase stored-procedure mappings require an active EF transaction to validate outputs before commit.");
            }
        }

        private int ResolveRowsAffected(int commandResult)
        {
            if (_rowsAffected is null)
            {
                return _commands[0].StoreStoredProcedure is null ? commandResult : 1;
            }

            if (_rowsAffected.Value is null or DBNull)
            {
                throw new InvalidOperationException("The OceanBase routine did not supply its rows-affected value.");
            }

            return (int)OceanBaseNumericConversion.ConvertValue(_rowsAffected.Value, typeof(int));
        }

        private void ValidateWriteValues()
        {
            // Query operands can exceed a column's capacity; only values actually written need its bounds.
            foreach (var column in _commands[0].ColumnModifications)
            {
                if (column.IsWrite && column.TypeMapping is IOceanBaseWriteValidation validation)
                {
                    validation.ValidateWriteValue(column.Value);
                }
            }
        }

        private void ThrowConcurrencyException(string message)
        {
            var entries = _commands[0].Entries;
            var exception = new DbUpdateConcurrencyException(message, entries);
            if (!_dependencies.UpdateLogger.OptimisticConcurrencyException(
                _dependencies.CurrentContext.Context, entries, exception, createEventData: null).IsSuppressed)
            {
                throw exception;
            }
        }

        private async Task ThrowConcurrencyExceptionAsync(string message, CancellationToken cancellationToken)
        {
            var entries = _commands[0].Entries;
            var exception = new DbUpdateConcurrencyException(message, entries);
            if (!(await _dependencies.UpdateLogger.OptimisticConcurrencyExceptionAsync(
                _dependencies.CurrentContext.Context, entries, exception, createEventData: null, cancellationToken)
                .ConfigureAwait(false)).IsSuppressed)
            {
                throw exception;
            }
        }

        private void PropagateOutputs()
        {
            foreach (var (column, parameter) in _outputs)
            {
                var mapping = column.TypeMapping!;
                var targetType = mapping.Converter?.ProviderClrType ?? mapping.ClrType;
                var value = parameter.Value is DBNull ? null : parameter.Value;
                if (value is not null && !targetType.IsInstanceOfType(value))
                {
                    value = OceanBaseTemporalConversions.ConvertValue(value, targetType);
                }

                column.Value = mapping.Converter is { } converter ? converter.ConvertFromProvider(value) : value;
            }
        }
    }
}
