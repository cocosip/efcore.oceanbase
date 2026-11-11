using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace OceanBase.EntityFrameworkCore.SpecificationTests
{
    public sealed class FixtureTests
    {
        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public async Task Fixture_uses_real_provider_services_and_blocks_sync_and_async_database_access(OceanBaseCompatibilityMode mode)
        {
            var helper = new OceanBaseModelTestHelpers(mode);
            using var services = (IDisposable)helper.CreateServiceProvider();
            await using var context = helper.CreateContext((IServiceProvider)services);
            var providerAssembly = typeof(OceanBaseCompatibilityMode).Assembly;
            Assert.True(context.Database.IsOceanBase());
            Assert.Equal(providerAssembly, context.GetService<IRelationalTypeMappingSource>().GetType().Assembly);
            Assert.Equal(providerAssembly, context.GetService<IProviderConventionSetBuilder>().GetType().Assembly);
            var native = Assert.IsType<OceanBaseConnector.OceanBaseConnection>(context.Database.GetDbConnection());
            var configured = new OceanBaseConnector.OceanBaseConnectionStringBuilder(native.ConnectionString);
            Assert.Equal(mode == OceanBaseCompatibilityMode.MySql ? OceanBaseConnector.OceanBaseCompatibilityMode.MySql
                : OceanBaseConnector.OceanBaseCompatibilityMode.Oracle, configured.ExpectedMode);
            var sync = Assert.Throws<InvalidOperationException>(() => context.Database.OpenConnection());
            var async = await Assert.ThrowsAsync<InvalidOperationException>(() => context.Database.OpenConnectionAsync());
            Assert.Equal("Model specification tests cannot access a database.", sync.Message);
            Assert.Equal(sync.Message, async.Message);
            Assert.Equal(System.Data.ConnectionState.Closed, native.State);
        }
    }
}
