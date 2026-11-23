using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Design.Internal;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests;

public sealed class SequenceScaffoldingTests
{
    [Theory]
    [InlineData(0L, false)]
    [InlineData(4L, true)]
    public void Sequence_allocation_annotations_survive_database_to_EF_model(long cache, bool ordered)
    {
        using var services = Services();
        var database = new DatabaseModel { DatabaseName = "app", DefaultSchema = "app" };
        var sequence = new DatabaseSequence
        {
            Database = database, Name = "KeySequence", Schema = "app", StoreType = "NUMBER(18,0)",
            StartValue = 17, MinValue = 1, MaxValue = 1000, IncrementBy = 5
        };
        sequence["OceanBase:SequenceCache"] = cache;
        sequence["OceanBase:SequenceOrdered"] = ordered;
        database.Sequences.Add(sequence);
        var model = services.GetRequiredService<IScaffoldingModelFactory>().Create(database,
            new ModelReverseEngineerOptions { UseDatabaseNames = true });
        var scaffolded = Assert.Single(model.GetSequences());
        Assert.Equal(cache, scaffolded["OceanBase:SequenceCache"]);
        Assert.Equal(ordered, scaffolded["OceanBase:SequenceOrdered"]);
        Assert.Equal(17, scaffolded.StartValue);
        Assert.Equal(5, scaffolded.IncrementBy);
    }

    [Theory]
    [InlineData(null, "seq\"line\nnext")]
    [InlineData("app", "seq\"line\nnext")]
    [InlineData("schema\"quoted", "seq\"line\nnext")]
    [InlineData("app", "        OnModelCreatingPartial(modelBuilder);")]
    public void Sequence_CSharp_preserves_allocation_options_and_escapes_names(string? schema, string name)
    {
        using var services = Services();
        var builder = new ModelBuilder();
        builder.HasDefaultSchema("app");
        builder.HasSequence<long>(name, schema).StartsAt(17).IncrementsBy(5).HasMin(1).HasMax(1000)
            .HasAnnotation("OceanBase:SequenceCache", 4L).HasAnnotation("OceanBase:SequenceOrdered", true);
        var generation = new ModelCodeGenerationOptions
        {
            Language = "C#", ContextName = "CatalogContext", ConnectionString = "Server=offline;CompatibilityMode=Oracle",
            ModelNamespace = "Generated", ContextNamespace = "Generated"
        };
        var code = services.GetRequiredService<IModelCodeGeneratorSelector>().Select(generation)
            .GenerateModel(builder.FinalizeModel(), generation).ContextFile.Code;
        var helper = services.GetRequiredService<ICSharpHelper>();
        Assert.Contains(".HasAnnotation(\"OceanBase:SequenceCache\", 4L)", code);
        Assert.Contains(".HasAnnotation(\"OceanBase:SequenceOrdered\", true)", code);
        Assert.Contains("HasSequence<long>(" + helper.Literal(name) + (schema is null or "app" ? "" : ", " + helper.Literal(schema)) + ")", code);
        Assert.True(code.IndexOf("OceanBase:SequenceCache", StringComparison.Ordinal)
            < code.IndexOf("\n        OnModelCreatingPartial(modelBuilder);", StringComparison.Ordinal));
    }

    private static ServiceProvider Services()
        => (ServiceProvider)new DesignTimeServicesBuilder(typeof(SequenceScaffoldingTests).Assembly,
            typeof(SequenceScaffoldingTests).Assembly, new OperationReporter(null), [])
            .Build(typeof(OceanBaseCompatibilityMode).Assembly.GetName().Name!);
}
