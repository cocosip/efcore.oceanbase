using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;

namespace OceanBase.EntityFrameworkCore.Query
{
    internal sealed class OceanBaseCompiledQueryCacheKeyGenerator(
        CompiledQueryCacheKeyGeneratorDependencies dependencies,
        RelationalCompiledQueryCacheKeyGeneratorDependencies relationalDependencies)
        : RelationalCompiledQueryCacheKeyGenerator(dependencies, relationalDependencies)
    {
        public override object GenerateCacheKey(Expression query, bool async)
            => (base.GenerateCacheKey(query, async),
                RelationalOptionsExtension.Extract(RelationalDependencies.ContextOptions).ParameterizedCollectionMode);
    }
}
