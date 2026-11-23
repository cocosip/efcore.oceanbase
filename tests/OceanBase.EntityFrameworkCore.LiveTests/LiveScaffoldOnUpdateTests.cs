using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Design.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace OceanBase.EntityFrameworkCore.LiveTests;

public sealed class LiveScaffoldOnUpdateTests
{
    [Theory]
    [InlineData(false, "DATETIME")]
    [InlineData(true, "DATETIME")]
    [InlineData(false, "TIMESTAMP")]
    [InlineData(true, "TIMESTAMP")]
    public Task Directory_on_update_and_json_round_trip_through_model_ddl_and_writes(bool async, string temporalType)
        => LiveStorageTable.WithTable<SourceContext>(OceanBaseCompatibilityMode.MySql, async, noBackslashEscapes: false,
            (options, table) => new SourceContext(new DbContextOptionsBuilder<SourceContext>(options)
                .ReplaceService<IModelCacheKeyFactory, CacheKey>().Options, table), async (source, _, stage) =>
            {
                stage("provision update-only and defaulted timestamps");
                if (async) await source.Database.ExecuteSqlRawAsync(source.AddTimestampsSql(temporalType));
                else source.Database.ExecuteSqlRaw(source.AddTimestampsSql(temporalType));

                stage("scaffold table through Connector directory");
#pragma warning disable EF1001
                using var services = (ServiceProvider)new DesignTimeServicesBuilder(typeof(LiveScaffoldOnUpdateTests).Assembly,
                    typeof(LiveScaffoldOnUpdateTests).Assembly, new OperationReporter(null), [])
                    .Build(typeof(OceanBaseCompatibilityMode).Assembly.GetName().Name!);
#pragma warning restore EF1001
                var connection = source.Database.GetDbConnection();
                source.Database.OpenConnection();
                string catalogName;
                try
                {
                    using var tables = connection.GetSchema("Tables", [connection.Database, null]);
                    catalogName = Assert.Single(tables.Rows.Cast<DataRow>().Select(row => row.Field<string>("TABLE_NAME")!),
                        name => string.Equals(name, source.Table, StringComparison.OrdinalIgnoreCase));
                }
                finally
                {
                    source.Database.CloseConnection();
                }
                var database = services.GetRequiredService<IDatabaseModelFactory>()
                    .Create(connection, new DatabaseModelFactoryOptions(tables: [catalogName]));
                var catalog = Assert.Single(database.Tables);
                var document = catalog.Columns.Single(column => column.Name == "Document");
                Assert.Equal("json", document.StoreType, ignoreCase: true);
                var updated = catalog.Columns.Single(column => column.Name == "Updated");
                var touched = catalog.Columns.Single(column => column.Name == "Touched");
                Assert.Equal(temporalType + "(6)", updated.StoreType, ignoreCase: true);
                Assert.Equal(temporalType + "(6)", touched.StoreType, ignoreCase: true);
                Assert.Equal(ValueGenerated.OnUpdate, updated.ValueGenerated);
                Assert.Equal(ValueGenerated.OnAddOrUpdate, touched.ValueGenerated);
                Assert.Equal("CURRENT_TIMESTAMP(6)", updated["OceanBase:OnUpdateSql"]);
                Assert.Equal("CURRENT_TIMESTAMP(6)", touched["OceanBase:OnUpdateSql"]);
                var model = services.GetRequiredService<IScaffoldingModelFactory>()
                    .Create(database, new ModelReverseEngineerOptions { UseDatabaseNames = true, NoPluralize = true });
                var generation = new ModelCodeGenerationOptions
                {
                    Language = "C#", ContextName = "OnUpdateContext", ContextNamespace = "Scaffolded", ModelNamespace = "Scaffolded",
                    SuppressOnConfiguring = true, ConnectionString = "Server=offline;Database=app;CompatibilityMode=MySql"
                };
                var code = services.GetRequiredService<IModelCodeGeneratorSelector>().Select(generation).GenerateModel(model, generation);
                var generated = Compile(code);
                var contextType = generated.GetType("Scaffolded.OnUpdateContext", throwOnError: true)!;
                var optionsType = typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType);
                var optionsBuilder = (DbContextOptionsBuilder)Activator.CreateInstance(optionsType)!;
                optionsBuilder.UseOceanBase(connection, OceanBaseCompatibilityMode.MySql);
                using var context = (DbContext)Activator.CreateInstance(contextType, optionsBuilder.Options)!;
                var currentModel = context.GetService<IDesignTimeModel>().Model;
                var entity = Assert.Single(currentModel.GetEntityTypes());
                var documentProperty = entity.FindProperty("Document")!;
                Assert.Equal(temporalType + "(6)", entity.FindProperty("Updated")!.GetColumnType(), ignoreCase: true);
                Assert.Equal(typeof(string), documentProperty.ClrType);
                Assert.Equal("json", documentProperty.GetColumnType(), ignoreCase: true);
                Assert.True(documentProperty.IsNullable);

                stage("generate create and migration SQL from scaffolded model");
                Assert.Contains("ON UPDATE CURRENT_TIMESTAMP(6)", context.Database.GenerateCreateScript());
                var operations = context.GetService<IMigrationsModelDiffer>().GetDifferences(null, currentModel.GetRelationalModel());
                var columns = Assert.Single(operations.OfType<CreateTableOperation>()).Columns
                    .Where(column => column.Name is "Updated" or "Touched" or "Document").ToArray();
                Assert.Equal(3, columns.Length);
                var generatedDocument = Assert.Single(columns, column => column.Name == "Document");
                Assert.Equal("json", generatedDocument.ColumnType, ignoreCase: true);
                Assert.True(generatedDocument.IsNullable);
                var generator = context.GetService<IMigrationsSqlGenerator>();
                var commands = generator.Generate(columns, currentModel);
                Assert.Equal(2, commands.Count(command => command.CommandText.Contains("ON UPDATE CURRENT_TIMESTAMP(6)", StringComparison.Ordinal)));
                if (async) await source.Database.ExecuteSqlRawAsync(source.DropScaffoldedColumnsSql);
                else source.Database.ExecuteSqlRaw(source.DropScaffoldedColumnsSql);
                var relational = context.GetService<IRelationalConnection>();
                foreach (var command in commands)
                {
                    if (async) await command.ExecuteNonQueryAsync(relational);
                    else command.ExecuteNonQuery(relational);
                }

                stage("insert with explicit update-only timestamp and defaulted timestamp");
                var row = Activator.CreateInstance(Assert.Single(context.Model.GetEntityTypes()).ClrType)!;
                var initial = new DateTime(2001, 1, 1).AddTicks(1234560);
                var entry = context.Entry(row);
                entry.Property("Id").CurrentValue = 1;
                entry.Property("Amount").CurrentValue = 2;
                entry.Property("Updated").CurrentValue = initial;
                entry.Property("Document").CurrentValue = "{\"name\":\"海\"}";
                entry.State = EntityState.Added;
                Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
                var inserted = Assert.IsType<DateTime>(entry.Property("Touched").CurrentValue);
                Assert.True(inserted > initial);
                if (async) await entry.ReloadAsync();
                else entry.Reload();
                var reloaded = Assert.IsType<DateTime>(entry.Property("Updated").CurrentValue);
                Assert.Equal(DateTimeKind.Unspecified, reloaded.Kind);
                Assert.Equal(initial, reloaded);
                Assert.Equal(inserted, entry.Property("Touched").CurrentValue);

                stage("update and read back both timestamps");
                entry.Property("Amount").CurrentValue = 3;
                entry.Property("Document").CurrentValue = "{\"name\":\"é海\"}";
                Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
                var updatedValue = Assert.IsType<DateTime>(entry.Property("Updated").CurrentValue);
                var touchedValue = Assert.IsType<DateTime>(entry.Property("Touched").CurrentValue);
                Assert.True(updatedValue > initial);
                Assert.True(touchedValue > inserted);
                if (async) await entry.ReloadAsync();
                else entry.Reload();
                Assert.Equal(updatedValue, entry.Property("Updated").CurrentValue);
                Assert.Equal(touchedValue, entry.Property("Touched").CurrentValue);
                using var json = JsonDocument.Parse(Assert.IsType<string>(entry.Property("Document").CurrentValue));
                Assert.Equal("é海", json.RootElement.GetProperty("name").GetString());
            });

    internal static Assembly Compile(ScaffoldedModel model)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Concat(AppDomain.CurrentDomain.GetAssemblies().Where(assembly => !assembly.IsDynamic && assembly.Location.Length > 0)
                .Select(assembly => assembly.Location)).Distinct();
        var sources = model.AdditionalFiles.Prepend(model.ContextFile);
        var compilation = CSharpCompilation.Create("Scaffold_" + Guid.NewGuid().ToString("N"),
            sources.Select(file => CSharpSyntaxTree.ParseText(file.Code)), references.Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));
        return Assembly.Load(stream.ToArray());
    }

    private sealed class SourceContext(DbContextOptions<SourceContext> options, string table) : DbContext(options)
    {
        public string Table => table;
        public string AddTimestampsSql(string temporalType) => $"ALTER TABLE `{table}` "
            + $"ADD COLUMN `Updated` {temporalType}(6) NULL ON UPDATE CURRENT_TIMESTAMP(6), "
            + $"ADD COLUMN `Touched` {temporalType}(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6)";
        public string DropScaffoldedColumnsSql => $"ALTER TABLE `{table}` DROP COLUMN `Updated`, DROP COLUMN `Touched`, DROP COLUMN `Document`";

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var row = modelBuilder.SharedTypeEntity<Dictionary<string, object>>(table);
            row.IndexerProperty<int>("Id").ValueGeneratedNever();
            row.IndexerProperty<int>("Amount");
            row.IndexerProperty<string>("Document").HasColumnType("json").IsRequired(false);
            row.HasKey("Id");
        }
    }

    private sealed class CacheKey : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => (((SourceContext)context).Table, designTime);
    }
}
