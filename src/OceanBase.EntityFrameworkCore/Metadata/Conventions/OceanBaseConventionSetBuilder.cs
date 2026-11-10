using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace OceanBase.EntityFrameworkCore.Metadata.Conventions
{
    internal sealed class OceanBaseConventionSetBuilder : RelationalConventionSetBuilder
    {
        public OceanBaseConventionSetBuilder(
            ProviderConventionSetBuilderDependencies dependencies,
            RelationalConventionSetBuilderDependencies relationalDependencies)
            : base(dependencies, relationalDependencies)
        {
        }
    }
}
