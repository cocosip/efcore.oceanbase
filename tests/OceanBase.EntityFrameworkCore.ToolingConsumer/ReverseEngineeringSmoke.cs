using System;
using System.IO;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design.Internal;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.Extensions.DependencyInjection;
using OceanBase.EntityFrameworkCore;
using OceanBase.EntityFrameworkCore.TestUtilities;

#pragma warning disable EF1001 // Exercise EF10's real context-free provider discovery and code generation.
internal static class ReverseEngineeringSmoke
{
    internal static void Run(string[] args)
    {
        foreach (var profile in new[] { "Complete", "Driver030" })
        foreach (var mode in new[] { OceanBaseCompatibilityMode.MySql, OceanBaseCompatibilityMode.Oracle })
        {
            if (args[0] == "--generate-catalog")
            {
                using var services = (ServiceProvider)new DesignTimeServicesBuilder(typeof(ReverseEngineeringSmoke).Assembly,
                    typeof(ReverseEngineeringSmoke).Assembly, new OperationReporter(new OperationReportHandler(null, Console.Error.WriteLine, null, null)), [])
                    .Build(typeof(OceanBaseCompatibilityMode).Assembly.GetName().Name!);
                using var connection = new ScaffoldingCatalogConnection(mode) { Driver030Profile = profile == "Driver030" };
                var database = services.GetRequiredService<IDatabaseModelFactory>().Create(connection, new DatabaseModelFactoryOptions());
                var model = services.GetRequiredService<IScaffoldingModelFactory>().Create(database, new ModelReverseEngineerOptions { UseDatabaseNames = true });
                var code = services.GetRequiredService<IModelCodeGeneratorSelector>().Select(new ModelCodeGenerationOptions { Language = "C#" })
                    .GenerateModel(model, new ModelCodeGenerationOptions
                    {
                        Language = "C#", ContextName = mode + "CatalogContext", ConnectionString = connection.ConnectionString,
                        ModelNamespace = "Scaffolded." + profile + "." + mode, ContextNamespace = "Scaffolded." + profile + "." + mode,
                        UseNullableReferenceTypes = true, SuppressConnectionStringWarning = true
                    });
                var directory = Path.Combine(args[1], profile, mode.ToString());
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, code.ContextFile.Path), code.ContextFile.Code);
                foreach (var file in code.AdditionalFiles) File.WriteAllText(Path.Combine(directory, file.Path), file.Code);
                if (code.AdditionalFiles.Count != 3 || !code.ContextFile.Code.Contains("OceanBaseCompatibilityMode." + mode, StringComparison.Ordinal))
                    throw new InvalidOperationException("Scaffolding lost entities or the explicit tenant mode.");
            }
            else if (args[0] == "--verify-catalog")
            {
                var type = typeof(ReverseEngineeringSmoke).Assembly.GetType("Scaffolded." + profile + "." + mode + "." + mode + "CatalogContext", throwOnError: true)!;
                using var context = (DbContext)Activator.CreateInstance(type)!;
                var entities = context.Model.GetEntityTypes().ToArray();
                var parent = entities.Single(e => e.GetTableName() == "Parents");
                var child = entities.Single(e => e.GetTableName() == "Children");
                if (entities.Length != 3 || parent.FindProperty("Id")!.ValueGenerated != (profile == "Driver030" && mode == OceanBaseCompatibilityMode.Oracle
                        ? Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never : Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAdd)
                    || child.FindPrimaryKey()!.Properties.Count != 2 || child.GetForeignKeys().Count() != 1
                    || child.FindProperty("Total")!.GetComputedColumnSql() != "1 + 1"
                    || entities.Single(e => e.GetViewName() == "Recent").FindPrimaryKey() is not null)
                    throw new InvalidOperationException("The compiled scaffolded context lost relational metadata.");
                if (profile == "Complete")
                {
                    var script = context.Database.GenerateCreateScript();
                    if (!script.Contains("Parents", StringComparison.Ordinal) || !script.Contains(mode == OceanBaseCompatibilityMode.MySql ? "AUTO_INCREMENT" : "IDENTITY", StringComparison.Ordinal))
                        throw new InvalidOperationException("The compiled context lost generated-key DDL.");
                }
                else if (mode == OceanBaseCompatibilityMode.MySql && parent.FindProperty("Name")!.GetDefaultValue() is not DBNull)
                    throw new InvalidOperationException("The compiled context invented SQL for an unavailable default expression.");
                foreach (var entity in entities)
                {
                    var set = type.GetProperties().Single(p => p.PropertyType.IsGenericType
                        && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>) && p.PropertyType.GenericTypeArguments[0] == entity.ClrType);
                    var query = ((IQueryable)set.GetValue(context)!).ToQueryString();
                    if (!query.Contains(entity.GetViewName() ?? entity.GetTableName()!, StringComparison.Ordinal))
                        throw new InvalidOperationException("A compiled scaffolded query lost its table/view.");
                }
            }
            else throw new ArgumentException("Unknown reverse-engineering smoke command.");
            Console.WriteLine(profile + "/" + mode + "/" + args[0] + " passed.");
        }
    }
}
#pragma warning restore EF1001
