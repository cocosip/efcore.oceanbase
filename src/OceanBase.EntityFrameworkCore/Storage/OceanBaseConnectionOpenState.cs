using System;
using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Storage;

namespace OceanBase.EntityFrameworkCore.Storage
{
    internal readonly struct OceanBaseConnectionOpenState(RelationalConnection connection)
    {
        private readonly DbConnection _dbConnection = connection.DbConnection;
        private readonly bool _wasOpen = connection.DbConnection.State == ConnectionState.Open;
        private readonly int _openedCount = OpenedCount(connection);
        private readonly bool _openedInternally = OpenedInternally(connection);

        public void Restore(Exception openingError)
        {
            try
            {
                if (!_wasOpen && _dbConnection.State != ConnectionState.Closed)
                    _dbConnection.Close();
            }
            catch (Exception cleanupError)
            {
                openingError.Data["OceanBase.OpenCleanupError"] = cleanupError;
            }
            finally
            {
                OpenedCount(connection) = _openedCount;
                OpenedInternally(connection) = _openedInternally;
            }
        }

        public async Task RestoreAsync(Exception openingError)
        {
            try
            {
                if (!_wasOpen && _dbConnection.State != ConnectionState.Closed)
                    await _dbConnection.CloseAsync().ConfigureAwait(false);
            }
            catch (Exception cleanupError)
            {
                openingError.Data["OceanBase.OpenCleanupError"] = cleanupError;
            }
            finally
            {
                OpenedCount(connection) = _openedCount;
                OpenedInternally(connection) = _openedInternally;
            }
        }

        // EF10 has no accounting rollback API across both failed-open phases; verify these fields on every EF upgrade.
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_openedCount")]
        private static extern ref int OpenedCount(RelationalConnection connection);

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_openedInternally")]
        private static extern ref bool OpenedInternally(RelationalConnection connection);
    }
}
