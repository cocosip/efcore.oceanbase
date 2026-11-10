using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;

namespace OceanBase.EntityFrameworkCore.Query;

internal sealed class OceanBaseAggregateMethodCallTranslatorProvider : RelationalAggregateMethodCallTranslatorProvider
{
    public OceanBaseAggregateMethodCallTranslatorProvider(RelationalAggregateMethodCallTranslatorProviderDependencies dependencies,
        IRelationalTypeMappingSource mappingSource) : base(dependencies)
        => AddTranslators([new OceanBaseAggregateMethodTranslator(dependencies.SqlExpressionFactory, mappingSource)]);
}
