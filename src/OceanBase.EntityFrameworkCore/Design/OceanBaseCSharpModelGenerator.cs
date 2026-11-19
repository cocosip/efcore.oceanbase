using System;
using System.Linq;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Design.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Scaffolding.Internal;
using static OceanBase.EntityFrameworkCore.Metadata.OceanBaseRelationalAnnotationProvider;

namespace OceanBase.EntityFrameworkCore.Design
{
    // EF10's sequence C# fragment omits provider annotations and has no override seam.
#pragma warning disable EF1001
    internal sealed class OceanBaseCSharpModelGenerator(ModelCodeGeneratorDependencies dependencies,
        IOperationReporter reporter, IServiceProvider serviceProvider, ICSharpHelper code)
        : CSharpModelGenerator(dependencies, reporter, serviceProvider)
    {
        public override ScaffoldedModel GenerateModel(IModel model, ModelCodeGenerationOptions options)
        {
            var scaffolded = base.GenerateModel(model, options);
            var allocation = new StringBuilder();
            foreach (var sequence in model.GetSequences())
            {
                var annotations = sequence.GetAnnotations().Where(annotation => annotation.Name
                    is SequenceCacheAnnotation or SequenceOrderedAnnotation).ToArray();
                if (annotations.Length == 0) continue;
                allocation.Append("        modelBuilder.HasSequence<").Append(code.Reference(sequence.Type)).Append(">(")
                    .Append(code.Literal(sequence.Name));
                if (!string.IsNullOrEmpty(sequence.Schema) && sequence.Schema != model.GetDefaultSchema())
                    allocation.Append(", ").Append(code.Literal(sequence.Schema));
                allocation.Append(')');
                foreach (var annotation in annotations)
                    allocation.AppendLine().Append("            .HasAnnotation(").Append(code.Literal(annotation.Name))
                        .Append(", ").Append(code.UnknownLiteral(annotation.Value)).Append(')');
                allocation.AppendLine(";");
            }
            if (allocation.Length == 0) return scaffolded;

            const string marker = "\n        OnModelCreatingPartial(modelBuilder);";
            var generated = scaffolded.ContextFile.Code;
            var position = generated.IndexOf(marker, StringComparison.Ordinal);
            if (position < 0 || position != generated.LastIndexOf(marker, StringComparison.Ordinal))
                throw new InvalidOperationException("The EF Core C# context template cannot preserve OceanBase sequence annotations.");
            scaffolded.ContextFile = new ScaffoldedFile(scaffolded.ContextFile.Path,
                generated.Insert(position + 1, allocation.AppendLine().ToString()));
            return scaffolded;
        }
    }
#pragma warning restore EF1001
}
