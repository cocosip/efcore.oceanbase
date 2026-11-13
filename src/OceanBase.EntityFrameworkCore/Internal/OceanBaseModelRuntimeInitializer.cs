using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace OceanBase.EntityFrameworkCore.Internal;

internal sealed class OceanBaseModelRuntimeInitializer(ModelRuntimeInitializerDependencies dependencies,
    RelationalModelRuntimeInitializerDependencies relationalDependencies, OceanBaseCompatibilityMode mode)
    : RelationalModelRuntimeInitializer(dependencies, relationalDependencies)
{
    internal const string ModeAnnotation = "OceanBase:CompatibilityMode";

    public override IModel Initialize(IModel model, bool designTime = true,
        IDiagnosticsLogger<DbLoggerCategory.Model.Validation>? validationLogger = null)
    {
        if (model[ModeAnnotation] is int modelMode && modelMode != (int)mode)
            throw new InvalidOperationException("The supplied model uses a different OceanBase compatibility mode. Generate and select a model for the configured mode.");
        return base.Initialize(model, designTime, validationLogger);
    }
}

internal sealed class OceanBaseModelCustomizer(ModelCustomizerDependencies dependencies, OceanBaseCompatibilityMode mode)
    : RelationalModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        modelBuilder.HasAnnotation(OceanBaseModelRuntimeInitializer.ModeAnnotation, (int)mode);
        base.Customize(modelBuilder, context);
    }
}
