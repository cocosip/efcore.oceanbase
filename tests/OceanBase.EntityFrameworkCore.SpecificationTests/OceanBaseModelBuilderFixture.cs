using System;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.ModelBuilding;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace OceanBase.EntityFrameworkCore.SpecificationTests
{
    public sealed class OceanBaseModelBuilderFixture(OceanBaseCompatibilityMode mode)
        : RelationalModelBuilderTest.RelationalModelBuilderFixture
    {
        public override TestHelpers TestHelpers { get; } = new OceanBaseModelTestHelpers(mode);
    }

    internal sealed class OceanBaseModelTestHelpers : RelationalTestHelpers
    {
        private readonly OceanBaseCompatibilityMode _mode;

        public OceanBaseModelTestHelpers(OceanBaseCompatibilityMode mode)
        {
            _mode = mode;
            using var context = new DbContext(UseProviderOptions(new DbContextOptionsBuilder()).Options);
            LoggingDefinitions = context.GetService<LoggingDefinitions>();
        }

        public override LoggingDefinitions LoggingDefinitions { get; }

        public override IServiceCollection AddProviderServices(IServiceCollection services)
        {
            var options = UseProviderOptions(new DbContextOptionsBuilder()).Options;
            options.Extensions.Single(extension => extension.Info.IsDatabaseProvider).ApplyServices(services);
            return services;
        }

        public override DbContextOptionsBuilder UseProviderOptions(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseOceanBase("Server=offline;Database=specification_metadata", _mode)
                .AddInterceptors(NoDatabaseAccess.Instance);

        private sealed class NoDatabaseAccess : DbConnectionInterceptor
        {
            public static NoDatabaseAccess Instance { get; } = new();

            public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData,
                InterceptionResult result) => throw new InvalidOperationException("Model specification tests cannot access a database.");

            public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,
                ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
                => throw new InvalidOperationException("Model specification tests cannot access a database.");
        }
    }
}
