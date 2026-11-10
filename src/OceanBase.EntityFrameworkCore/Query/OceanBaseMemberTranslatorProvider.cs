using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;

namespace OceanBase.EntityFrameworkCore.Query
{
    internal sealed class OceanBaseMemberTranslatorProvider : RelationalMemberTranslatorProvider
    {
        public OceanBaseMemberTranslatorProvider(RelationalMemberTranslatorProviderDependencies dependencies,
            IRelationalTypeMappingSource mappingSource, OceanBaseCompatibilityMode mode)
            : base(dependencies)
            => AddTranslators([
                new OceanBaseStringTranslator(dependencies.SqlExpressionFactory, mappingSource, mode),
                new OceanBaseTemporalMemberTranslator(dependencies.SqlExpressionFactory, mappingSource, mode)
            ]);
    }
}
