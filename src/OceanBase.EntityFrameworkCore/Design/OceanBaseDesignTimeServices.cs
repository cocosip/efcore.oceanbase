using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

[assembly: DesignTimeProviderServices("OceanBase.EntityFrameworkCore.Design.OceanBaseDesignTimeServices")]

namespace OceanBase.EntityFrameworkCore.Design
{
    /// <summary>Registers the relational EF Core services used to scaffold OceanBase migrations.</summary>
    public sealed class OceanBaseDesignTimeServices : IDesignTimeServices
    {
        /// <inheritdoc />
        public void ConfigureDesignTimeServices(IServiceCollection serviceCollection)
            => new EntityFrameworkRelationalDesignServicesBuilder(serviceCollection)
                .TryAdd<ITypeMappingSource>(services => services.GetRequiredService<IRelationalTypeMappingSource>())
                .TryAddCoreServices();
    }
}
