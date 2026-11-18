using Microsoft.EntityFrameworkCore.Query;

namespace OceanBase.EntityFrameworkCore.Query
{
    internal sealed class OceanBaseMethodCallTranslatorProvider : RelationalMethodCallTranslatorProvider
    {
        public OceanBaseMethodCallTranslatorProvider(RelationalMethodCallTranslatorProviderDependencies dependencies, OceanBaseCompatibilityMode mode)
            : base(dependencies)
            => AddTranslators([
                new OceanBaseStringTranslator(dependencies.SqlExpressionFactory, dependencies.RelationalTypeMappingSource, mode),
                new OceanBaseStringIndexOfTranslator(dependencies.SqlExpressionFactory, dependencies.RelationalTypeMappingSource, mode),
                new OceanBaseExplicitTrimTranslator(dependencies.SqlExpressionFactory, dependencies.RelationalTypeMappingSource, mode),
                new OceanBaseBooleanTranslator(dependencies.SqlExpressionFactory, dependencies.RelationalTypeMappingSource),
                new OceanBaseMathTranslator(dependencies.SqlExpressionFactory, dependencies.RelationalTypeMappingSource, mode),
                new OceanBaseRoundTranslator(dependencies.SqlExpressionFactory, dependencies.RelationalTypeMappingSource, mode),
                new OceanBaseTemporalMethodTranslator(dependencies.SqlExpressionFactory, dependencies.RelationalTypeMappingSource, mode)]);
    }
}
