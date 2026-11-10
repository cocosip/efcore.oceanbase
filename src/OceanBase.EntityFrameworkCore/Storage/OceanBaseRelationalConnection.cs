using System;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBaseConnector;

namespace OceanBase.EntityFrameworkCore.Storage;

internal sealed class OceanBaseRelationalConnection : RelationalConnection
{
    private readonly OceanBaseCompatibilityMode _compatibilityMode;

    public OceanBaseRelationalConnection(RelationalConnectionDependencies dependencies)
        : base(dependencies)
    {
        _compatibilityMode = dependencies.ContextOptions.FindExtension<Internal.OceanBaseOptionsExtension>()
            ?.CompatibilityMode
            ?? OceanBaseCompatibilityMode.MySql;
    }

    protected override DbConnection CreateDbConnection()
    {
        var connectionString = GetValidatedConnectionString();
        var builder = new OceanBaseConnectionStringBuilder(connectionString);
        var expectedMode = _compatibilityMode == OceanBaseCompatibilityMode.Oracle
            ? OceanBaseConnector.OceanBaseCompatibilityMode.Oracle
            : OceanBaseConnector.OceanBaseCompatibilityMode.MySql;

        if (builder.ExpectedMode is { } configuredMode && configuredMode != expectedMode)
        {
            throw new InvalidOperationException(
                $"The connection string ExpectedMode '{configuredMode}' does not match '{expectedMode}'.");
        }

        builder.ExpectedMode = expectedMode;
        return new OceanBaseConnection(builder.ConnectionString);
    }
}
