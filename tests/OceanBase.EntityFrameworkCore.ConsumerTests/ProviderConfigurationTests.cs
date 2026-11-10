using Microsoft.EntityFrameworkCore;
using Xunit;

namespace OceanBase.EntityFrameworkCore.ConsumerTests
{
    public sealed class ProviderConfigurationTests
    {
        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void UseOceanBase_registers_the_provider(OceanBaseCompatibilityMode mode)
        {
            var options = new DbContextOptionsBuilder<ConsumerContext>()
                .UseOceanBase(
                    "Server=localhost;User ID=test;Password=test;Database=test",
                    mode)
                .Options;

            using var context = new ConsumerContext(options);

            Assert.True(context.Database.IsOceanBase());
            Assert.Equal("OceanBase.EntityFrameworkCore", context.Database.ProviderName);
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
