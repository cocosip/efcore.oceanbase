using System;
using System.Collections;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace OceanBase.EntityFrameworkCore.Storage;

internal sealed class OceanBaseRetryReaderInterceptor : DbCommandInterceptor
{
    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
        => eventData.Context is { } context && (ExecutionStrategy.Current is not null
            || context.Database.CreateExecutionStrategy().RetriesOnFailure) ? new Reader(result) : result;

    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
        DbDataReader result, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(ReaderExecuted(command, eventData, result));

    // EF buffers in a try/finally: native disposal must not replace an unknown read outcome with a retryable error.
    private sealed class Reader(DbDataReader inner) : DbDataReader
    {
        private Exception? _failure;
        private bool _disposed;
        private T ReadValue<T>(Func<T> read)
        {
            try { return read(); }
            catch (Exception error) { _failure ??= error; throw; }
        }
        private async Task<T> ReadValueAsync<T>(Func<Task<T>> read)
        {
            try { return await read().ConfigureAwait(false); }
            catch (Exception error) { _failure ??= error; throw; }
        }
        private bool PreserveFailure(Exception cleanup)
        {
            OceanBaseRetryingExecutionStrategy.PreventRetry(cleanup);
            if (_failure is null) return false;
            OceanBaseRetryingExecutionStrategy.PreventRetry(_failure);
            _failure.Data["OceanBase.ReaderCleanupError"] = cleanup;
            return true;
        }
        public override void Close()
        {
            try { inner.Close(); }
            catch (Exception error) when (PreserveFailure(error)) { }
        }
        protected override void Dispose(bool disposing)
        {
            if (!disposing || _disposed) return;
            _disposed = true;
            try { inner.Dispose(); }
            catch (Exception error) when (PreserveFailure(error)) { }
        }
        public override async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            try { await inner.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) when (PreserveFailure(error)) { }
        }
        public override bool Read() => ReadValue(inner.Read);
        public override Task<bool> ReadAsync(CancellationToken cancellationToken) => ReadValueAsync(() => inner.ReadAsync(cancellationToken));
        public override bool NextResult() => ReadValue(inner.NextResult);
        public override Task<bool> NextResultAsync(CancellationToken cancellationToken) => ReadValueAsync(() => inner.NextResultAsync(cancellationToken));
        public override object GetValue(int ordinal) => ReadValue(() => inner.GetValue(ordinal));
        public override int GetValues(object[] values) => ReadValue(() => inner.GetValues(values));
        public override bool IsDBNull(int ordinal) => ReadValue(() => inner.IsDBNull(ordinal));
        public override Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken) => ReadValueAsync(() => inner.IsDBNullAsync(ordinal, cancellationToken));
        public override T GetFieldValue<T>(int ordinal) => ReadValue(() => inner.GetFieldValue<T>(ordinal));
        public override Task<T> GetFieldValueAsync<T>(int ordinal, CancellationToken cancellationToken) => ReadValueAsync(() => inner.GetFieldValueAsync<T>(ordinal, cancellationToken));
        public override bool GetBoolean(int ordinal) => ReadValue(() => inner.GetBoolean(ordinal));
        public override byte GetByte(int ordinal) => ReadValue(() => inner.GetByte(ordinal));
        public override long GetBytes(int ordinal, long offset, byte[]? buffer, int bufferOffset, int length) => ReadValue(() => inner.GetBytes(ordinal, offset, buffer, bufferOffset, length));
        public override char GetChar(int ordinal) => ReadValue(() => inner.GetChar(ordinal));
        public override long GetChars(int ordinal, long offset, char[]? buffer, int bufferOffset, int length) => ReadValue(() => inner.GetChars(ordinal, offset, buffer, bufferOffset, length));
        public override DateTime GetDateTime(int ordinal) => ReadValue(() => inner.GetDateTime(ordinal));
        public override decimal GetDecimal(int ordinal) => ReadValue(() => inner.GetDecimal(ordinal));
        public override double GetDouble(int ordinal) => ReadValue(() => inner.GetDouble(ordinal));
        public override float GetFloat(int ordinal) => ReadValue(() => inner.GetFloat(ordinal));
        public override Guid GetGuid(int ordinal) => ReadValue(() => inner.GetGuid(ordinal));
        public override short GetInt16(int ordinal) => ReadValue(() => inner.GetInt16(ordinal));
        public override int GetInt32(int ordinal) => ReadValue(() => inner.GetInt32(ordinal));
        public override long GetInt64(int ordinal) => ReadValue(() => inner.GetInt64(ordinal));
        public override string GetString(int ordinal) => ReadValue(() => inner.GetString(ordinal));
        public override Stream GetStream(int ordinal) => ReadValue(() => inner.GetStream(ordinal));
        public override TextReader GetTextReader(int ordinal) => ReadValue(() => inner.GetTextReader(ordinal));
        public override string GetName(int ordinal) => ReadValue(() => inner.GetName(ordinal));
        public override string GetDataTypeName(int ordinal) => ReadValue(() => inner.GetDataTypeName(ordinal));
        public override Type GetFieldType(int ordinal) => ReadValue(() => inner.GetFieldType(ordinal));
        public override int GetOrdinal(string name) => ReadValue(() => inner.GetOrdinal(name));
        public override DataTable? GetSchemaTable() => ReadValue(inner.GetSchemaTable);
        public override IEnumerator GetEnumerator() => new DbEnumerator(this);
        public override object this[int ordinal] => GetValue(ordinal);
        public override object this[string name] => GetValue(GetOrdinal(name));
        public override int Depth => inner.Depth;
        public override int FieldCount => inner.FieldCount;
        public override int VisibleFieldCount => inner.VisibleFieldCount;
        public override bool HasRows => inner.HasRows;
        public override bool IsClosed => inner.IsClosed;
        public override int RecordsAffected => inner.RecordsAffected;
    }
}
