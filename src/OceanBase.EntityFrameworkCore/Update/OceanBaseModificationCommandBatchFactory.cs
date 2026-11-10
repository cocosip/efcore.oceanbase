using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Update;

namespace OceanBase.EntityFrameworkCore.Update
{
    internal sealed class OceanBaseModificationCommandBatchFactory : IModificationCommandBatchFactory
    {
        private readonly ModificationCommandBatchFactoryDependencies _dependencies;
        private readonly OceanBaseCompatibilityMode _mode;

        public OceanBaseModificationCommandBatchFactory(
            ModificationCommandBatchFactoryDependencies dependencies, IDbContextOptions options)
        {
            _dependencies = dependencies;
            _mode = options.FindExtension<Internal.OceanBaseOptionsExtension>()!.CompatibilityMode;
        }

        public ModificationCommandBatch Create() => new OceanBaseModificationCommandBatch(_dependencies, _mode);
    }
}
