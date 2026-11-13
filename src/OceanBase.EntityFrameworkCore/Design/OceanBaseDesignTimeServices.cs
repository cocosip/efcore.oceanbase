using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

[assembly: DesignTimeProviderServices("OceanBase.EntityFrameworkCore.Design.OceanBaseDesignTimeServices")]

namespace OceanBase.EntityFrameworkCore.Design
{
    /// <summary>Registers the relational EF Core services used for OceanBase migrations and database reverse engineering.</summary>
    public sealed class OceanBaseDesignTimeServices : IDesignTimeServices
    {
        /// <inheritdoc />
        public void ConfigureDesignTimeServices(IServiceCollection serviceCollection)
        {
            serviceCollection.TryAddSingleton<OceanBaseScaffoldingTypeMappingSource>();
            serviceCollection.TryAddSingleton<IRelationalTypeMappingSource>(s => s.GetRequiredService<OceanBaseScaffoldingTypeMappingSource>());
            // Reverse engineering starts without a DbContext or a tenant-specific runtime provider.
            new EntityFrameworkRelationalServicesBuilder(serviceCollection)
                .TryAdd<IModelRuntimeInitializer, RelationalModelRuntimeInitializer>()
                .TryAdd<IModelValidator, RelationalModelValidator>()
                .TryAdd<IRelationalAnnotationProvider, RelationalAnnotationProvider>()
                .TryAddCoreServices();
            new EntityFrameworkRelationalDesignServicesBuilder(serviceCollection)
                .TryAdd<IDatabaseModelFactory, OceanBaseDatabaseModelFactory>()
                .TryAdd<IProviderConfigurationCodeGenerator, OceanBaseProviderCodeGenerator>()
                .TryAdd<ITypeMappingSource>(services => services.GetRequiredService<IRelationalTypeMappingSource>())
                .TryAddCoreServices();
        }
    }
}
