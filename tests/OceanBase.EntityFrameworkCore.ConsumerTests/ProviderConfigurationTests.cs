using Microsoft.EntityFrameworkCore;
using Xunit;

namespace OceanBase.EntityFrameworkCore.ConsumerTests
{
    public sealed class ProviderConfigurationTests
    {
        [Fact]
        public void UseOceanBase_registers_the_provider()
        {
            var options = new DbContextOptionsBuilder<ConsumerContext>()
                .UseOceanBase(
                    "Server=localhost;User ID=test;Password=test;Database=test",
                    OceanBaseCompatibilityMode.MySql)
                .Options;

            using var context = new ConsumerContext(options);

            Assert.True(context.Database.IsOceanBase());
        }

        private sealed class ConsumerContext : DbContext
        {
            public ConsumerContext(DbContextOptions<ConsumerContext> options)
                : base(options)
            {
            }
        }
    }
}
