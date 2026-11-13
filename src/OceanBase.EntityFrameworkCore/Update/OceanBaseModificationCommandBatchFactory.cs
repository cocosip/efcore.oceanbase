using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Update;

namespace OceanBase.EntityFrameworkCore.Update
{
    internal sealed class OceanBaseModificationCommandBatchFactory : IModificationCommandBatchFactory
    {
        private readonly ModificationCommandBatchFactoryDependencies _dependencies;
        private readonly OceanBaseCompatibilityMode _mode;
        private bool _modelValidated;

        public OceanBaseModificationCommandBatchFactory(
            ModificationCommandBatchFactoryDependencies dependencies, IDbContextOptions options)
        {
            _dependencies = dependencies;
            _mode = options.FindExtension<Internal.OceanBaseOptionsExtension>()!.CompatibilityMode;
        }

        public ModificationCommandBatch Create()
        {
            if (!_modelValidated)
            {
                Metadata.OceanBaseRelationalAnnotationProvider.ValidateTpcKeyGeneration(
                    _dependencies.CurrentContext.Context.Model.GetEntityTypes());
                _modelValidated = true;
            }
            return new OceanBaseModificationCommandBatch(_dependencies, _mode);
        }
    }
}
