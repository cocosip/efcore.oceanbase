using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Design.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Scaffolding.Internal;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;
using static OceanBase.EntityFrameworkCore.Metadata.OceanBaseRelationalAnnotationProvider;

namespace OceanBase.EntityFrameworkCore.Design
{
    // EF's sequence visitor does not copy provider annotations.
#pragma warning disable EF1001
    internal sealed class OceanBaseScaffoldingModelFactory(IOperationReporter reporter,
        ICandidateNamingService candidateNamingService, IPluralizer pluralizer, ICSharpUtilities cSharpUtilities,
        IScaffoldingTypeMapper scaffoldingTypeMapper, IModelRuntimeInitializer modelRuntimeInitializer)
        : RelationalScaffoldingModelFactory(reporter, candidateNamingService, pluralizer, cSharpUtilities,
            scaffoldingTypeMapper, modelRuntimeInitializer)
    {
        protected override SequenceBuilder? VisitSequence(ModelBuilder modelBuilder, DatabaseSequence sequence)
        {
            var builder = base.VisitSequence(modelBuilder, sequence);
            if (builder is not null)
            {
                foreach (var name in new[] { SequenceCacheAnnotation, SequenceOrderedAnnotation })
                    if (sequence.FindAnnotation(name) is { } annotation)
                        builder.HasAnnotation(name, annotation.Value);
            }
            return builder;
        }
    }
#pragma warning restore EF1001
}
