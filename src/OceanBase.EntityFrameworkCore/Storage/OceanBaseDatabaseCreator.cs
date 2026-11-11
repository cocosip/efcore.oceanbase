using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBaseConnector;

namespace OceanBase.EntityFrameworkCore.Storage
{
    internal sealed class OceanBaseDatabaseCreator(
        IRelationalConnection connection, IRawSqlCommandBuilder commands, ICurrentDbContext currentContext,
        IRelationalCommandDiagnosticsLogger logger, IDbContextOptions options) : IRelationalDatabaseCreator
    {
        private readonly OceanBaseCompatibilityMode _mode = options.FindExtension<Internal.OceanBaseOptionsExtension>()!.CompatibilityMode;

        public bool Exists()
        {
            var opened = false;
            try
            {
                connection.Open(errorsExpected: true);
                opened = true;
                return true;
            }
            catch (OceanBaseException error) when (_mode == OceanBaseCompatibilityMode.MySql && error.ServerErrorCode == 1049)
            {
                return false;
            }
            finally
            {
                if (opened) connection.Close();
            }
        }

        public async Task<bool> ExistsAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var opened = false;
            try
            {
                await connection.OpenAsync(cancellationToken, errorsExpected: true).ConfigureAwait(false);
                opened = true;
                return true;
            }
            catch (OceanBaseException error) when (_mode == OceanBaseCompatibilityMode.MySql && error.ServerErrorCode == 1049)
            {
                return false;
            }
            finally
            {
                if (opened) await connection.CloseAsync().ConfigureAwait(false);
            }
        }

        public bool CanConnect()
        {
            try { return Exists(); }
            catch (Exception error) when (error is not OperationCanceledException) { return false; }
        }

        public async Task<bool> CanConnectAsync(CancellationToken cancellationToken = default)
        {
            try { return await ExistsAsync(cancellationToken).ConfigureAwait(false); }
            catch (Exception error) when (error is not OperationCanceledException) { return false; }
        }

        private IRelationalCommand TableProbe() => commands.Build(_mode == OceanBaseCompatibilityMode.MySql
            ? "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_type = 'BASE TABLE'"
            : "SELECT COUNT(*) FROM ALL_TABLES WHERE OWNER = SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA')");

        private RelationalCommandParameterObject Parameters()
            => new(connection, null, null, currentContext.Context, logger, CommandSource.Unknown);

        public bool HasTables() => ReadTableCount(TableProbe().ExecuteScalar(Parameters()));

        public async Task<bool> HasTablesAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ReadTableCount(await TableProbe().ExecuteScalarAsync(Parameters(), cancellationToken).ConfigureAwait(false));
        }

        private static bool ReadTableCount(object? value)
            => value is null or DBNull
                ? throw new InvalidOperationException("The OceanBase table probe returned no count.")
                : (long)OceanBaseNumericConversion.ConvertValue(value, typeof(long)) > 0;

        private static NotSupportedException SchemaNotSupported()
            => new("OceanBase schema creation and deletion require the not-yet-implemented schema ownership profile. Apply schema externally until SCH-02/SCH-03 are implemented.");

        public void Create() => throw SchemaNotSupported();
        public Task CreateAsync(CancellationToken cancellationToken = default) => throw SchemaNotSupported();
        public void Delete() => throw SchemaNotSupported();
        public Task DeleteAsync(CancellationToken cancellationToken = default) => throw SchemaNotSupported();
        public void CreateTables() => throw SchemaNotSupported();
        public Task CreateTablesAsync(CancellationToken cancellationToken = default) => throw SchemaNotSupported();
        public string GenerateCreateScript() => throw SchemaNotSupported();
        public bool EnsureCreated() => throw SchemaNotSupported();
        public Task<bool> EnsureCreatedAsync(CancellationToken cancellationToken = default) => throw SchemaNotSupported();
        public bool EnsureDeleted() => throw SchemaNotSupported();
        public Task<bool> EnsureDeletedAsync(CancellationToken cancellationToken = default) => throw SchemaNotSupported();
    }
}
