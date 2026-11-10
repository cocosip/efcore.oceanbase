using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace OceanBase.EntityFrameworkCore.Internal
{
    internal sealed class OceanBaseModelCacheKeyFactory : ModelCacheKeyFactory
    {
        private readonly OceanBaseCompatibilityMode _compatibilityMode;

        internal OceanBaseModelCacheKeyFactory(
            ModelCacheKeyFactoryDependencies dependencies,
            OceanBaseCompatibilityMode compatibilityMode)
            : base(dependencies)
        {
            _compatibilityMode = compatibilityMode;
        }

        public OceanBaseModelCacheKeyFactory(
            ModelCacheKeyFactoryDependencies dependencies,
            IDbContextOptions options)
            : this(
                dependencies,
                options.FindExtension<OceanBaseOptionsExtension>()?.CompatibilityMode
                    ?? OceanBaseCompatibilityMode.MySql)
        {
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
