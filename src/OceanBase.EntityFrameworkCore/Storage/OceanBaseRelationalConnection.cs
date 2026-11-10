using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBaseConnector;

namespace OceanBase.EntityFrameworkCore.Storage
{
    internal sealed class OceanBaseRelationalConnection : RelationalConnection
    {
        private readonly OceanBaseCompatibilityMode _compatibilityMode;

        public OceanBaseRelationalConnection(RelationalConnectionDependencies dependencies)
            : base(dependencies)
        {
            var options = dependencies.ContextOptions.FindExtension<Internal.OceanBaseOptionsExtension>();
            _compatibilityMode = options?.CompatibilityMode ?? OceanBaseCompatibilityMode.MySql;
            if (options?.Connection is { } connection)
            {
                ValidateConnection(connection);
            }
        }

        private OceanBaseConnector.OceanBaseCompatibilityMode ExpectedMode
            => _compatibilityMode == OceanBaseCompatibilityMode.Oracle
                ? OceanBaseConnector.OceanBaseCompatibilityMode.Oracle
                : OceanBaseConnector.OceanBaseCompatibilityMode.MySql;

        protected override DbConnection CreateDbConnection()
            => new OceanBaseConnection(ValidateConnectionString(GetValidatedConnectionString()));

        public override void SetDbConnection(DbConnection? value, bool contextOwnsConnection)
        {
            if (value is not null)
            {
                ValidateConnection(value);
            }

            base.SetDbConnection(value, contextOwnsConnection);
        }

        public override bool Open(bool errorsExpected = false)
        {
            ValidateConnection(DbConnection);
            return base.Open(errorsExpected);
        }

        public override Task<bool> OpenAsync(CancellationToken cancellationToken, bool errorsExpected = false)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateConnection(DbConnection);
            return base.OpenAsync(cancellationToken, errorsExpected);
        }

        private string ValidateConnectionString(string connectionString)
        {
            var builder = new OceanBaseConnectionStringBuilder(connectionString);
            if (builder.ExpectedMode is { } configuredMode && configuredMode != ExpectedMode)
            {
                throw new InvalidOperationException($"The connection mode does not match '{ExpectedMode}'.");
            }

            if (_compatibilityMode == OceanBaseCompatibilityMode.MySql && builder.UseAffectedRows)
            {
                throw new InvalidOperationException("EF Core requires UseAffectedRows=false to detect matched rows for concurrency checks.");
            }

            builder.ExpectedMode = ExpectedMode;
            return builder.ConnectionString;
        }

        private void ValidateConnection(DbConnection connection)
        {
            if (connection is not OceanBaseConnection oceanBaseConnection)
            {
                throw new InvalidOperationException("The OceanBase provider requires an OceanBaseConnector connection.");
            }

            var connectionString = ValidateConnectionString(connection.ConnectionString);
            if (oceanBaseConnection.CompatibilityMode is { } actualMode && actualMode != ExpectedMode)
            {
                throw new InvalidOperationException($"The connection mode does not match '{ExpectedMode}'.");
            }

            // EF also reads DbConnection during cleanup, so validation belongs at use boundaries.
            if (connection.State == ConnectionState.Closed && connection.ConnectionString != connectionString)
            {
                connection.ConnectionString = connectionString;
            }
        }
    }
}
