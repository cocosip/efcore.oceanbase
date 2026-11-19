using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
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
        private OceanBaseParameter? _resultCursor;
        private int _routineResultWidth;
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
            _routineResultWidth = procedure.ResultColumns.Count();
            if (_routineResultWidth == 0 && command.EntityState != EntityState.Added
                && command.RowsAffectedColumn is not (IStoreStoredProcedureParameter or IStoreStoredProcedureReturnValue))
            {
                throw new NotSupportedException("OceanBase routine update/delete mappings require a result set, an output parameter or a function return value to report concurrency.");
            }

            if (procedure.ReturnValue is not null && (_routineResultWidth != 0 || command.EntityState == EntityState.Added
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
                if (column.Column is IStoreStoredProcedureResultColumn) continue;
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

            if (_routineResultWidth != 0 && _mode == OceanBaseCompatibilityMode.Oracle)
            {
                const string cursorName = "ob_result_cursor";
                _resultCursor = new OceanBaseParameter
                {
                    ParameterName = cursorName,
                    OceanBaseDbType = OceanBaseDbType.RefCursor,
                    Direction = ParameterDirection.Output
                };
                builder.AddRawParameter(cursorName, _resultCursor);
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
                if (_routineResultWidth != 0)
                {
                    ExecuteRoutineWithResults(connection);
                    return;
                }
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
                if (_routineResultWidth != 0)
                {
                    await ExecuteRoutineWithResultsAsync(connection, cancellationToken).ConfigureAwait(false);
                    return;
                }
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

        private void ExecuteRoutineWithResults(IRelationalConnection connection)
        {
            (int AffectedRows, List<(IColumnModification Column, object? Value)> Values) result;
            if (_resultCursor is null)
            {
                using var reader = _write!.ExecuteReader(Parameters(connection));
                result = ConsumeRoutineResults(reader);
            }
            else
            {
                _write!.ExecuteNonQuery(Parameters(connection));
                using var cursor = GetResultCursor();
                using var reader = cursor.GetDataReader();
                result = ConsumeRoutineResults(new RoutineReader(reader));
            }

            if (result.AffectedRows != 1) ThrowConcurrencyException("Expected one row from the OceanBase routine.");
            else ApplyRoutineResult(result.Values);
        }

        private async Task ExecuteRoutineWithResultsAsync(IRelationalConnection connection, CancellationToken cancellationToken)
        {
            (int AffectedRows, List<(IColumnModification Column, object? Value)> Values) result;
            if (_resultCursor is null)
            {
                await using var reader = await _write!.ExecuteReaderAsync(Parameters(connection), cancellationToken).ConfigureAwait(false);
                result = await ConsumeRoutineResultsAsync(reader, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await _write!.ExecuteNonQueryAsync(Parameters(connection), cancellationToken).ConfigureAwait(false);
                await using var cursor = GetResultCursor();
                await using var reader = await cursor.GetDataReaderAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                result = await ConsumeRoutineResultsAsync(new RoutineReader(reader), cancellationToken).ConfigureAwait(false);
            }

            if (result.AffectedRows != 1)
                await ThrowConcurrencyExceptionAsync("Expected one row from the OceanBase routine.", cancellationToken).ConfigureAwait(false);
            else ApplyRoutineResult(result.Values);
        }

        private (int AffectedRows, List<(IColumnModification Column, object? Value)> Values) ConsumeRoutineResults(
            RelationalDataReader reader)
        {
            var dataReader = reader.DbDataReader;
            var result = ReadRoutineResult(reader, reader.Read(), out var decodeError);
            var invalidShape = dataReader.FieldCount != _routineResultWidth || reader.Read();
            // Drain the cursor before throwing so EF can still roll back to its savepoint.
            while (dataReader.NextResult())
            {
                invalidShape |= _resultCursor is not null || dataReader.FieldCount != 0;
            }

            decodeError?.Throw();
            if (invalidShape) throw InvalidRoutineResult();
            return result;
        }

        private async Task<(int AffectedRows, List<(IColumnModification Column, object? Value)> Values)> ConsumeRoutineResultsAsync(
            RelationalDataReader reader, CancellationToken cancellationToken)
        {
            var dataReader = reader.DbDataReader;
            var result = ReadRoutineResult(reader,
                await reader.ReadAsync(cancellationToken).ConfigureAwait(false), out var decodeError);
            var invalidShape = dataReader.FieldCount != _routineResultWidth
                || await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            while (await dataReader.NextResultAsync(cancellationToken).ConfigureAwait(false))
            {
                invalidShape |= _resultCursor is not null || dataReader.FieldCount != 0;
            }

            decodeError?.Throw();
            if (invalidShape) throw InvalidRoutineResult();
            return result;
        }

        private (int AffectedRows, List<(IColumnModification Column, object? Value)> Values) ReadRoutineResult(
            RelationalDataReader reader, bool hasRow, out ExceptionDispatchInfo? decodeError)
        {
            decodeError = null;
            if (reader.DbDataReader.FieldCount != _routineResultWidth) return (0, []);
            if (!hasRow) return (0, []);

            var command = _commands[0];
            var values = new List<(IColumnModification Column, object? Value)>();
            try
            {
                var affectedRows = command.RowsAffectedColumn is IStoreStoredProcedureResultColumn count
                    ? (int)OceanBaseNumericConversion.ConvertValue(reader.DbDataReader.GetValue(count.Position), typeof(int)) : 1;
                if (affectedRows == 1)
                {
                    var detailedErrorsEnabled = _dependencies.CurrentContext.Context.GetService<IDbContextOptions>()
                        .FindExtension<CoreOptionsExtension>()?.DetailedErrorsEnabled == true;
                    foreach (var column in command.ColumnModifications)
                    {
                        if (column.IsRead && column.Column is IStoreStoredProcedureResultColumn result && result != command.RowsAffectedColumn)
                            values.Add((column, column.Property!.GetReaderFieldValue(reader, result.Position, detailedErrorsEnabled)));
                    }
                }
                return (affectedRows, values);
            }
            // A user value converter can throw any application exception.
            catch (Exception error) when (error is not (OperationCanceledException or DbException or IOException
                or TimeoutException or OutOfMemoryException))
            {
                decodeError = ExceptionDispatchInfo.Capture(error);
                return (0, []);
            }
        }

        private OceanBaseRefCursor GetResultCursor()
            => _resultCursor!.Value as OceanBaseRefCursor
                ?? throw new InvalidOperationException("The Oracle routine did not return its final SYS_REFCURSOR argument.");

        private static InvalidOperationException InvalidRoutineResult()
            => new("The OceanBase routine must return one result set with the mapped columns and at most one row.");

        private void ApplyRoutineResult(List<(IColumnModification Column, object? Value)> values)
        {
            foreach (var (column, value) in values) column.Value = value;
            PropagateOutputs();
        }

        private sealed class RoutineReader(DbDataReader reader) : RelationalDataReader
        {
            public override DbDataReader DbDataReader => reader;
            public override bool Read() => reader.Read();
            public override Task<bool> ReadAsync(CancellationToken cancellationToken) => reader.ReadAsync(cancellationToken);
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
