using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace OceanBase.EntityFrameworkCore.Internal
{
    internal sealed class OceanBaseModelCacheKeyFactory : ModelCacheKeyFactory
    {
        private readonly OceanBaseCompatibilityMode _compatibilityMode;

        public OceanBaseModelCacheKeyFactory(
            ModelCacheKeyFactoryDependencies dependencies,
            IDbContextOptions options)
            : base(dependencies)
        {
            _compatibilityMode = options.FindExtension<OceanBaseOptionsExtension>()?.CompatibilityMode
                ?? OceanBaseCompatibilityMode.MySql;
        }

        public override object Create(DbContext context, bool designTime)
        {
            return new OceanBaseModelCacheKey(
                base.Create(context, designTime),
                _compatibilityMode);
        }

        private readonly record struct OceanBaseModelCacheKey(
            object BaseKey,
            OceanBaseCompatibilityMode CompatibilityMode);
    }
}
