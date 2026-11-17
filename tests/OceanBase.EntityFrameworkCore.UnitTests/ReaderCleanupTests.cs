using System;
using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using OceanBase.EntityFrameworkCore.Storage;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests
{
    public sealed class ReaderCleanupTests
    {
        [Fact]
        public void Wrapper_preserves_provider_specific_type_and_value_APIs()
        {
            using var reader = new OceanBaseRetryReaderInterceptor.Reader(new Reader());
            Assert.Equal(typeof(int), reader.GetFieldType(0));
            Assert.Equal(typeof(long), reader.GetProviderSpecificFieldType(0));
            Assert.Equal(2L, reader.GetProviderSpecificValue(0));
            var values = new object[1];
            Assert.Equal(1, reader.GetProviderSpecificValues(values));
            Assert.Equal(2L, values[0]);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task Healthy_reader_consumes_all_results_before_close_or_dispose(bool async, bool close)
        {
            using var connection = new Connection();
            var inner = new Reader();
            var reader = new OceanBaseRetryReaderInterceptor.Reader(inner, connection);
            Assert.True(reader.Read());
            if (close)
            {
                if (async) await reader.CloseAsync(); else reader.Close();
            }
            else if (async) await reader.DisposeAsync(); else reader.Dispose();
            Assert.Equal(7, inner.Reads);
            Assert.Equal(2, inner.Results);
            Assert.Equal(async ? 6 : 0, inner.AsyncReads);
            Assert.Equal(async ? 2 : 0, inner.AsyncResults);
            Assert.Equal(1, inner.Closes);
            Assert.True(inner.IsClosed);
            if (async) await reader.DisposeAsync(); else reader.Dispose();
            Assert.Equal(7, inner.Reads);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Drain_failure_survives_disposal_failure_and_inner_reader_is_always_disposed(bool async)
        {
            using var connection = new Connection();
            var primary = new InvalidOperationException("drain failed");
            var cleanup = new InvalidOperationException("dispose failed");
            var inner = new Reader { ReadFailure = primary, DisposeFailure = cleanup };
            var reader = new OceanBaseRetryReaderInterceptor.Reader(inner, connection);
            Assert.True(reader.Read());
            var actual = async ? await Record.ExceptionAsync(() => reader.DisposeAsync().AsTask()) : Record.Exception(reader.Dispose);
            Assert.Same(primary, actual);
            Assert.Same(cleanup, actual.Data["OceanBase.ReaderCleanupError"]);
            Assert.True(inner.IsClosed);
            Assert.Equal(2, inner.Reads);
            if (async) await reader.DisposeAsync(); else reader.Dispose();
            Assert.Equal(2, inner.Reads);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Existing_read_failure_is_preserved_and_disposal_does_not_attempt_further_reads(bool async)
        {
            using var connection = new Connection();
            var primary = new OperationCanceledException();
            var cleanup = new InvalidOperationException("dispose failed");
            var inner = new Reader { ReadFailure = primary, DisposeFailure = cleanup };
            var reader = new OceanBaseRetryReaderInterceptor.Reader(inner, connection);
            Assert.True(reader.Read());
            Assert.Same(primary, Record.Exception(() => reader.Read()));
            if (async) await reader.DisposeAsync(); else reader.Dispose();
            Assert.Equal(2, inner.Reads);
            Assert.Same(cleanup, primary.Data["OceanBase.ReaderCleanupError"]);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Broken_connection_is_disposed_without_attempting_to_drain(bool async)
        {
            using var connection = new Connection { CurrentState = ConnectionState.Broken };
            var inner = new Reader();
            var reader = new OceanBaseRetryReaderInterceptor.Reader(inner, connection);
            if (async) await reader.DisposeAsync(); else reader.Dispose();
            Assert.Equal(0, inner.Reads);
            Assert.True(inner.IsClosed);
        }

        private sealed class Connection : DbConnection
        {
            public ConnectionState CurrentState { get; set; } = ConnectionState.Open;
            [AllowNull] public override string ConnectionString { get; set; } = "";
            public override ConnectionState State => CurrentState;
            public override string Database => "offline";
            public override string DataSource => "offline";
            public override string ServerVersion => "test";
            public override void Open() => CurrentState = ConnectionState.Open;
            public override void Close() => CurrentState = ConnectionState.Closed;
            public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
            protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
            protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
        }

        private sealed class Reader : DbDataReader
        {
            private int _row;
            private bool _closed;
            public Exception? ReadFailure { get; init; }
            public Exception? DisposeFailure { get; init; }
            public int Reads { get; private set; }
            public int Results { get; private set; }
            public int AsyncReads { get; private set; }
            public int AsyncResults { get; private set; }
            public int Closes { get; private set; }
            public override bool Read()
            {
                if (++Reads == 2 && ReadFailure is not null) throw ReadFailure;
                return ++_row <= (Results == 0 ? 2 : 3);
            }
            public override Task<bool> ReadAsync(CancellationToken cancellationToken)
            { cancellationToken.ThrowIfCancellationRequested(); AsyncReads++; return Task.FromResult(Read()); }
            public override bool NextResult() { _row = 0; return ++Results < 2; }
            public override Task<bool> NextResultAsync(CancellationToken cancellationToken)
            { cancellationToken.ThrowIfCancellationRequested(); AsyncResults++; return Task.FromResult(NextResult()); }
            public override void Close() { _closed = true; Closes++; }
            protected override void Dispose(bool disposing) { if (disposing) { Close(); if (DisposeFailure is not null) throw DisposeFailure; } }
            public override ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
            public override bool IsClosed => _closed;
            public override int FieldCount => 1;
            public override int Depth => 0;
            public override bool HasRows => true;
            public override int RecordsAffected => -1;
            public override object this[int ordinal] => throw new NotSupportedException();
            public override object this[string name] => throw new NotSupportedException();
            public override object GetValue(int ordinal) => 1;
            public override object GetProviderSpecificValue(int ordinal) => 2L;
            public override int GetProviderSpecificValues(object[] values) { values[0] = 2L; return 1; }
            public override Type GetProviderSpecificFieldType(int ordinal) => typeof(long);
            public override int GetValues(object[] values) => throw new NotSupportedException();
            public override bool IsDBNull(int ordinal) => throw new NotSupportedException();
            public override int GetOrdinal(string name) => throw new NotSupportedException();
            public override string GetName(int ordinal) => throw new NotSupportedException();
            public override string GetDataTypeName(int ordinal) => throw new NotSupportedException();
            public override Type GetFieldType(int ordinal) => typeof(int);
            public override bool GetBoolean(int ordinal) => throw new NotSupportedException();
            public override byte GetByte(int ordinal) => throw new NotSupportedException();
            public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
            public override char GetChar(int ordinal) => throw new NotSupportedException();
            public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
            public override short GetInt16(int ordinal) => throw new NotSupportedException();
            public override int GetInt32(int ordinal) => throw new NotSupportedException();
            public override long GetInt64(int ordinal) => throw new NotSupportedException();
            public override float GetFloat(int ordinal) => throw new NotSupportedException();
            public override double GetDouble(int ordinal) => throw new NotSupportedException();
            public override decimal GetDecimal(int ordinal) => throw new NotSupportedException();
            public override DateTime GetDateTime(int ordinal) => throw new NotSupportedException();
            public override Guid GetGuid(int ordinal) => throw new NotSupportedException();
            public override string GetString(int ordinal) => throw new NotSupportedException();
            public override IEnumerator GetEnumerator() => throw new NotSupportedException();
        }
    }
}
