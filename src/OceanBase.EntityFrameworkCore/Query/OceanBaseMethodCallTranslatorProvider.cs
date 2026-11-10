using Microsoft.EntityFrameworkCore.Query;

namespace OceanBase.EntityFrameworkCore.Query
{
    internal sealed class OceanBaseMethodCallTranslatorProvider : RelationalMethodCallTranslatorProvider
    {
        public OceanBaseMethodCallTranslatorProvider(RelationalMethodCallTranslatorProviderDependencies dependencies, OceanBaseCompatibilityMode mode)
            : base(dependencies)
            => AddTranslators([new OceanBaseStringTranslator(dependencies.SqlExpressionFactory, dependencies.RelationalTypeMappingSource, mode)]);
    }
}
