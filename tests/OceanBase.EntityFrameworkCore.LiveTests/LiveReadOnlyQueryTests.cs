using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OceanBase.EntityFrameworkCore;
using OceanBaseConnector;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;
using Mode = OceanBase.EntityFrameworkCore.OceanBaseCompatibilityMode;

namespace OceanBase.EntityFrameworkCore.LiveTests
{
    public sealed class LiveReadOnlyQueryTests(ITestOutputHelper output)
    {
        [Theory]
        [InlineData(Mode.MySql)]
        [InlineData(Mode.Oracle)]
        public async Task Tenant_executes_the_read_only_EF_query_profile(Mode mode)
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("OCEANBASE_RUN_LIVE_TESTS"), "1", StringComparison.Ordinal))
                throw new InvalidOperationException("Set OCEANBASE_RUN_LIVE_TESTS=1 to run live OceanBase tests.");
            var key = mode == Mode.MySql ? "ConnectionStrings:OceanBaseMySql" : "ConnectionStrings:OceanBaseOracle";
            var connectionString = new ConfigurationBuilder().AddUserSecrets<LiveConnectionTests>(optional: true).Build()[key];
            if (string.IsNullOrWhiteSpace(connectionString)) throw new InvalidOperationException($"Missing User Secret '{key}'.");
            connectionString = LiveTestConfiguration.ConnectionString(connectionString);

            var stage = "opening (authentication and SQL not established)";
            var commands = new ReadOnlyQueryProfile.Commands();
            try
            {
                await using var context = new ReadOnlyQueryProfile.Context(new DbContextOptionsBuilder<ReadOnlyQueryProfile.Context>()
                    .UseOceanBase(connectionString, mode).AddInterceptors(commands).Options);
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                await context.Database.OpenConnectionAsync(timeout.Token);
                output.WriteLine($"{mode}: connection opened; server reports {context.Database.GetDbConnection().ServerVersion}.");
                foreach (var async in new[] { false, true })
                {
                    stage = async ? "async EF queries" : "sync EF queries";
                    await ReadOnlyQueryProfile.Verify(context, mode, async, timeout.Token);
                }
                stage = "connection cleanup";
                await context.Database.CloseConnectionAsync();
                Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
                output.WriteLine($"{mode}: read-only sync/async profile passed; {commands.Readers} EF reader commands, no test objects.");
            }
            catch (Exception error) when (error is not XunitException)
            {
                // Connector messages/inner exceptions may contain connection data; retain only safe diagnostics.
                var code = error is OceanBaseException native ? native.ServerErrorCode.ToString() : "none";
                throw new InvalidOperationException($"OceanBase {mode} read-only profile failed at {stage}; error={error.GetType().Name}; server-code={code}; EF-readers={commands.Readers}. No test objects were created.");
            }
        }
    }
}
