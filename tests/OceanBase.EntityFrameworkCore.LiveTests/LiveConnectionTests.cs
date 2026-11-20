using System;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OceanBase.EntityFrameworkCore;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.LiveTests;

public sealed class LiveConnectionTests
{
    [Fact]
    public Task MySql_tenant_executes_a_non_destructive_probe()
        => RunProbe(OceanBaseCompatibilityMode.MySql, "ConnectionStrings:OceanBaseMySql", "SELECT 1");

    [Fact]
    public Task Oracle_tenant_executes_a_non_destructive_probe()
        => RunProbe(OceanBaseCompatibilityMode.Oracle, "ConnectionStrings:OceanBaseOracle", "SELECT 1 FROM DUAL");

    private static async Task RunProbe(
        OceanBaseCompatibilityMode mode, string connectionKey, string sql)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("OCEANBASE_RUN_LIVE_TESTS"), "1", StringComparison.Ordinal))
            throw new InvalidOperationException("Set OCEANBASE_RUN_LIVE_TESTS=1 to run the live OceanBase tests.");

        var connectionString = new ConfigurationBuilder()
            .AddUserSecrets<LiveConnectionTests>(optional: true)
            .Build()[connectionKey];
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException($"Missing User Secret '{connectionKey}'.");
        connectionString = LiveTestConfiguration.ConnectionString(connectionString);

        await using var context = new DbContext(new DbContextOptionsBuilder<DbContext>()
            .UseOceanBase(connectionString, mode)
            .Options);
        await context.Database.OpenConnectionAsync();
        try
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            var result = await command.ExecuteScalarAsync();
            Assert.Equal(1m, result is OceanBaseDecimal number ? number.ToDecimal() : Convert.ToDecimal(result));

            if (mode == OceanBaseCompatibilityMode.MySql)
            {
                command.CommandText = "SELECT DATABASE()";
                Assert.Equal(context.Database.GetDbConnection().Database, Convert.ToString(await command.ExecuteScalarAsync()));
            }
            else
            {
                command.CommandText = "SELECT SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA') FROM DUAL";
                Assert.False(string.IsNullOrWhiteSpace(Convert.ToString(await command.ExecuteScalarAsync())));
            }
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }
}
