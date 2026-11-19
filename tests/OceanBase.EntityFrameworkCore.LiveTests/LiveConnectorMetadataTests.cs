using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
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

public sealed class LiveConnectorMetadataTests
{
    [Theory]
    [InlineData("ALWAYS", 0L, false)]
    [InlineData("BY DEFAULT", 4L, true)]
    [InlineData("BY DEFAULT ON NULL", 0L, false)]
    public Task Oracle_identity_expression_index_and_sequence_metadata_survive_reverse_engineering(string generation, long cache, bool nonKeyIdentity)
        => LiveStorageTable.WithTable<MetadataContext>(OceanBaseCompatibilityMode.Oracle, async: true, noBackslashEscapes: false,
            (options, table) => new MetadataContext(new DbContextOptionsBuilder<MetadataContext>(options)
                .ReplaceService<IModelCacheKeyFactory, CacheKey>().Options, table, generation, cache, nonKeyIdentity), async (context, _, stage) =>
            {
                await context.Database.OpenConnectionAsync();
                var sql = context.GetService<ISqlGenerationHelper>();
                var sequenceName = context.Table + "_S";
                var sequenceSql = "SELECT " + sql.DelimitIdentifier(sequenceName) + ".NEXTVAL FROM DUAL";
                stage("consume sequence before reverse engineering");
                using (var command = context.Database.GetDbConnection().CreateCommand())
                {
                    command.CommandText = sequenceSql;
                    Assert.Equal("17", (await command.ExecuteScalarAsync())!.ToString());
                    Assert.Equal("22", (await command.ExecuteScalarAsync())!.ToString());
                }
                stage("insert identity and expression-indexed row");
                var identityName = nonKeyIdentity ? "Generated" : "Id";
                var row = new Row { Label = "mixed case", Id = nonKeyIdentity ? 7 : 0 };
                context.Add(row);
                Assert.Equal(1, await context.SaveChangesAsync());
                Assert.Equal(17L, nonKeyIdentity ? row.Generated : row.Id);

#pragma warning disable EF1001
                using var services = (ServiceProvider)new DesignTimeServicesBuilder(typeof(LiveConnectorMetadataTests).Assembly,
                    typeof(LiveConnectorMetadataTests).Assembly, new OperationReporter(null), [])
                    .Build(typeof(OceanBaseCompatibilityMode).Assembly.GetName().Name!);
#pragma warning restore EF1001
                stage("scaffold public Connector metadata");
                var database = services.GetRequiredService<IDatabaseModelFactory>().Create(context.Database.GetDbConnection(),
                    new DatabaseModelFactoryOptions(tables: [context.Table]));
                var table = Assert.Single(database.Tables);
                var identity = table.Columns.Single(column => column.Name == identityName);
                Assert.Equal(generation, identity["OceanBase:IdentityGeneration"]);
                Assert.Contains("START WITH 17", Assert.IsType<string>(identity["OceanBase:IdentityOptions"]), StringComparison.OrdinalIgnoreCase);
                Assert.Equal(new[] { "UNIQUE", "UPPER(\"Label\")", "\"Id\"" }, table["OceanBase:ExpressionIndex:" + context.Table + "_IX"]);
                var sequence = Assert.Single(database.Sequences, sequence => sequence.Name == sequenceName);
                Assert.Equal(17L, sequence.StartValue);
                Assert.Equal(5, sequence.IncrementBy);
                Assert.Equal(cache, sequence["OceanBase:SequenceCache"]);
                Assert.Equal(false, sequence["OceanBase:SequenceOrdered"]);
                stage("generate model and CSharp from catalog");
                var model = services.GetRequiredService<IScaffoldingModelFactory>().Create(database,
                    new ModelReverseEngineerOptions { UseDatabaseNames = true });
                var generated = services.GetRequiredService<IModelCodeGeneratorSelector>().Select(new ModelCodeGenerationOptions { Language = "C#" })
                    .GenerateModel(model, new ModelCodeGenerationOptions
                    {
                        Language = "C#", ContextName = "MetadataContext", ConnectionString = "Name=Metadata",
                        ModelNamespace = "Generated", ContextNamespace = "Generated", SuppressOnConfiguring = true
                    });
                Assert.Contains("OceanBase:ExpressionIndex:", generated.ContextFile.Code);
                Assert.Contains("OceanBase:IdentityGeneration", generated.ContextFile.Code);
                Assert.Contains("StartsAt(17L)", generated.ContextFile.Code);
                stage("metadata did not advance the sequence");
                using var next = context.Database.GetDbConnection().CreateCommand();
                next.CommandText = sequenceSql;
                Assert.Equal("27", (await next.ExecuteScalarAsync())!.ToString());
                stage("compile scaffolded CSharp and generate migration operations");
                var assembly = LiveScaffoldOnUpdateTests.Compile(generated);
                var contextType = assembly.GetType("Generated.MetadataContext", throwOnError: true)!;
                var optionsBuilder = (DbContextOptionsBuilder)Activator.CreateInstance(typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType))!;
                optionsBuilder.UseOceanBase(context.Database.GetDbConnection(), OceanBaseCompatibilityMode.Oracle);
                optionsBuilder.EnableOceanBaseSchemaManagement(database.DefaultSchema!);
                using var scaffolded = (DbContext)Activator.CreateInstance(contextType, optionsBuilder.Options)!;
                var recreatedModel = scaffolded.GetService<IDesignTimeModel>().Model;
                var operations = scaffolded.GetService<IMigrationsModelDiffer>().GetDifferences(null, recreatedModel.GetRelationalModel())
                    .Where(operation => operation is CreateTableOperation create && create.Name == context.Table
                        || operation is CreateSequenceOperation createSequence && createSequence.Name == sequenceName).ToArray();
                Assert.Equal(2, operations.Length);
                var commands = scaffolded.GetService<IMigrationsSqlGenerator>().Generate(operations, recreatedModel);
                stage("recreate only the owned table and sequence from generated model");
                var dropTable = "DROP TABLE " + sql.DelimitIdentifier(context.Table);
                var dropSequence = "DROP SEQUENCE " + sql.DelimitIdentifier(sequenceName);
                await context.Database.ExecuteSqlRawAsync(dropTable);
                await context.Database.ExecuteSqlRawAsync(dropSequence);
                foreach (var command in commands) await command.ExecuteNonQueryAsync(scaffolded.GetService<IRelationalConnection>());
                stage("verify recreated identity options and expression index");
                var rebuilt = services.GetRequiredService<IDatabaseModelFactory>().Create(context.Database.GetDbConnection(),
                    new DatabaseModelFactoryOptions(tables: [context.Table]));
                Assert.Equal(generation, Assert.Single(rebuilt.Tables).Columns.Single(column => column.Name == identityName)["OceanBase:IdentityGeneration"]);
                Assert.Equal(table["OceanBase:ExpressionIndex:" + context.Table + "_IX"], Assert.Single(rebuilt.Tables)["OceanBase:ExpressionIndex:" + context.Table + "_IX"]);
                stage("write and reload through the compiled scaffolded context");
                var instance = Activator.CreateInstance(Assert.Single(scaffolded.Model.GetEntityTypes()).ClrType)!;
                scaffolded.Entry(instance).Property("Label").CurrentValue = "recreated";
                if (nonKeyIdentity) scaffolded.Entry(instance).Property("Id").CurrentValue = 7m;
                scaffolded.Add(instance);
                Assert.Equal(1, await scaffolded.SaveChangesAsync());
                Assert.Equal(17L, Convert.ToInt64(scaffolded.Entry(instance).Property(identityName).CurrentValue));
                await scaffolded.Entry(instance).ReloadAsync();
                Assert.Equal("recreated", scaffolded.Entry(instance).Property("Label").CurrentValue);
                next.CommandText = sequenceSql;
                Assert.Equal("17", (await next.ExecuteScalarAsync())!.ToString());
                stage("expression index migration adds its column first and drops the index before its column");
                var annotation = "OceanBase:ExpressionIndex:" + context.Table + "_EX";
                var change = new AlterTableOperation { Name = context.Table };
                change[annotation] = new[] { "NONUNIQUE", "UPPER(\"Extra\")" };
                var add = new AddColumnOperation { Table = context.Table, Name = "Extra", ClrType = typeof(string), ColumnType = "VARCHAR2(40)", IsNullable = true };
                var generator = context.GetService<IMigrationsSqlGenerator>();
                foreach (var command in generator.Generate([change, add])) await command.ExecuteNonQueryAsync(context.GetService<IRelationalConnection>());
                var remove = new AlterTableOperation { Name = context.Table };
                remove.OldTable[annotation] = change[annotation];
                var drop = new DropColumnOperation { Table = context.Table, Name = "Extra" };
                foreach (var command in generator.Generate([drop, remove])) await command.ExecuteNonQueryAsync(context.GetService<IRelationalConnection>());
            }, sequenceSuffix: "_S");

    private sealed class MetadataContext(DbContextOptions<MetadataContext> options, string table, string generation, long cache, bool nonKeyIdentity) : DbContext(options)
    {
        public string Table => table;
        protected override void OnModelCreating(ModelBuilder builder)
        {
            var row = builder.Entity<Row>();
            row.ToTable(Table);
            if (nonKeyIdentity) row.Property(value => value.Id).ValueGeneratedNever();
            row.Property<long>(nonKeyIdentity ? "Generated" : "Id").ValueGeneratedOnAdd().HasAnnotation("OceanBase:IdentityGeneration", generation)
                .HasAnnotation("OceanBase:IdentityOptions", "START WITH 17 INCREMENT BY 5 NOCACHE");
            row.Property(value => value.Label).HasMaxLength(40);
            row.HasAnnotation("OceanBase:ExpressionIndex:" + Table + "_IX", new[] { "UNIQUE", "UPPER(\"Label\")", "\"Id\"" });
            builder.HasSequence<long>(Table + "_S").StartsAt(17).IncrementsBy(5).HasMin(1).HasMax(1000)
                .HasAnnotation("OceanBase:SequenceCache", cache).HasAnnotation("OceanBase:SequenceOrdered", false);
        }
    }

    private sealed class CacheKey : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => (((MetadataContext)context).Table, designTime);
    }

    private sealed class Row
    {
        public long Id { get; set; }
        public long Generated { get; set; }
        public string Label { get; set; } = "";
    }
}
