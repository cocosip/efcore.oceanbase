using System;
using System.Data;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using CatalogConnection = OceanBase.EntityFrameworkCore.TestUtilities.ScaffoldingCatalogConnection;

namespace OceanBase.EntityFrameworkCore.UnitTests;

public sealed class ConnectorMetadataTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Oracle_expression_indexes_survive_catalog_model_and_CSharp(bool unique, bool mixed)
    {
        using var services = (ServiceProvider)new DesignTimeServicesBuilder(typeof(ConnectorMetadataTests).Assembly,
            typeof(ConnectorMetadataTests).Assembly, new OperationReporter(null), [])
            .Build(typeof(OceanBaseCompatibilityMode).Assembly.GetName().Name!);
        using var connection = new CatalogConnection(OceanBaseCompatibilityMode.Oracle)
        {
            Rewrite = (name, rows) =>
            {
                if (name == "Indexes") rows.Rows[2]["IS_UNIQUE"] = unique ? "YES" : "NO";
                if (name != "IndexColumns") return;
                rows.Columns.Add("COLUMN_EXPRESSION", typeof(string));
                var member = rows.Rows.Cast<DataRow>().Single(row => Equals(row["COLUMN_NAME"], "Note"));
                member["COLUMN_NAME"] = "SYS_NC00042$";
                member["COLUMN_EXPRESSION"] = "UPPER(\"Note\")";
                member["SORT_ORDER"] = "ASC";
                if (!mixed) rows.Rows.Remove(rows.Rows.Cast<DataRow>().Single(row => Equals(row["COLUMN_NAME"], "Seq")));
            }
        };
        var database = services.GetRequiredService<IDatabaseModelFactory>().Create(connection, new DatabaseModelFactoryOptions());
        var table = database.Tables.Single(table => table.Name == "Children");
        var expected = mixed ? new[] { unique ? "UNIQUE" : "NONUNIQUE", "UPPER(\"Note\")", "\"Seq\"" }
            : new[] { unique ? "UNIQUE" : "NONUNIQUE", "UPPER(\"Note\")" };
        Assert.Equal(expected, Assert.IsType<string[]>(table["OceanBase:ExpressionIndex:IX_Note"]));
        var model = services.GetRequiredService<IScaffoldingModelFactory>().Create(database,
            new ModelReverseEngineerOptions { UseDatabaseNames = true });
        var entity = model.GetEntityTypes().Single(entity => entity.GetTableName() == "Children");
        Assert.Equal(expected, Assert.IsType<string[]>(entity["OceanBase:ExpressionIndex:IX_Note"]));
        Assert.DoesNotContain(entity.GetProperties(), property => property.Name.StartsWith("SYS_NC", StringComparison.Ordinal));
        var code = services.GetRequiredService<IModelCodeGeneratorSelector>().Select(new ModelCodeGenerationOptions { Language = "C#" })
            .GenerateModel(model, new ModelCodeGenerationOptions
            {
                Language = "C#", ContextName = "CatalogContext", ConnectionString = connection.ConnectionString,
                ModelNamespace = "Generated", ContextNamespace = "Generated"
            });
        Assert.Contains("OceanBase:ExpressionIndex:IX_Note", code.ContextFile.Code);
    }

    [Theory]
    [InlineData("ALWAYS")]
    [InlineData("BY DEFAULT")]
    [InlineData("BY DEFAULT ON NULL")]
    public void Oracle_identity_modes_and_options_reach_DDL_and_reject_implicit_alteration(string generation)
    {
        using var context = Context();
        var create = new AddColumnOperation { Table = "Rows", Name = "Id", ClrType = typeof(long), ColumnType = "NUMBER(18)" };
        create["OceanBase:Identity"] = true;
        create["OceanBase:IdentityGeneration"] = generation;
        create["OceanBase:IdentityOptions"] = "START WITH 17 INCREMENT BY 5 NOCACHE";
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var sql = Assert.Single(generator.Generate([create])).CommandText;
        Assert.Contains("GENERATED " + generation + " AS IDENTITY START WITH 17 INCREMENT BY 5 NOCACHE", sql);
        var alter = new AlterColumnOperation { Table = "Rows", Name = "Id", ClrType = typeof(long), ColumnType = "NUMBER(18)" };
        foreach (var annotation in create.GetAnnotations())
        {
            alter[annotation.Name] = annotation.Value;
            alter.OldColumn[annotation.Name] = annotation.Value;
        }
        alter.OldColumn.ClrType = typeof(long);
        alter.OldColumn.ColumnType = "NUMBER(18)";
        alter["OceanBase:IdentityOptions"] = "START WITH 22 INCREMENT BY 5 NOCACHE";
        Assert.Throws<NotSupportedException>(() => generator.Generate([alter]));
    }

    [Fact]
    public void Table_annotation_index_changes_generate_create_drop_and_replacement_DDL()
    {
        using var context = Context();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var create = new CreateTableOperation { Name = "Rows" };
        create.Columns.Add(new AddColumnOperation { Name = "Label", Table = "Rows", ClrType = typeof(string), ColumnType = "VARCHAR2(40)" });
        create["OceanBase:ExpressionIndex:IX_Label"] = new[] { "NONUNIQUE", "UPPER(\"Label\")" };
        Assert.Contains("CREATE INDEX \"IX_Label\" ON \"Rows\" (UPPER(\"Label\"))",
            string.Join("\n", generator.Generate([create]).Select(command => command.CommandText)));
        var alter = new AlterTableOperation { Name = "Rows" };
        alter.OldTable["OceanBase:ExpressionIndex:IX_Label"] = create["OceanBase:ExpressionIndex:IX_Label"];
        alter["OceanBase:ExpressionIndex:IX_Label"] = new[] { "NONUNIQUE", "LOWER(\"Label\")" };
        var sql = string.Join("\n", generator.Generate([alter]).Select(command => command.CommandText));
        Assert.Contains("DROP INDEX \"IX_Label\"", sql);
        Assert.Contains("CREATE INDEX \"IX_Label\" ON \"Rows\" (LOWER(\"Label\"))", sql);
        Assert.True(sql.IndexOf("DROP INDEX", StringComparison.Ordinal) < sql.IndexOf("CREATE INDEX", StringComparison.Ordinal));
    }

    [Fact]
    public void Unique_expression_replacement_requires_creation_before_dropping_the_old_index()
    {
        using var context = Context();
        var alter = new AlterTableOperation { Name = "Rows" };
        alter.OldTable["OceanBase:ExpressionIndex:IX_Old"] = new[] { "UNIQUE", "UPPER(\"Label\")" };
        alter["OceanBase:ExpressionIndex:IX_Old"] = new[] { "UNIQUE", "LOWER(\"Label\")" };
        Assert.Throws<NotSupportedException>(() => context.GetService<IMigrationsSqlGenerator>().Generate([alter]));
        alter.RemoveAnnotation("OceanBase:ExpressionIndex:IX_Old");
        alter["OceanBase:ExpressionIndex:IX_New"] = new[] { "UNIQUE", "LOWER(\"Label\")" };
        var sql = string.Join("\n", context.GetService<IMigrationsSqlGenerator>().Generate([alter]).Select(command => command.CommandText));
        Assert.True(sql.IndexOf("CREATE UNIQUE INDEX", StringComparison.Ordinal) < sql.IndexOf("DROP INDEX", StringComparison.Ordinal));
    }

    [Fact]
    public void Expression_index_changes_are_ordered_around_their_column_operations()
    {
        using var context = Context();
        var alter = new AlterTableOperation { Name = "Rows" };
        alter["OceanBase:ExpressionIndex:IX_Label"] = new[] { "NONUNIQUE", "UPPER(\"NewLabel\")" };
        var add = new AddColumnOperation { Table = "Rows", Name = "NewLabel", ClrType = typeof(string), ColumnType = "VARCHAR2(40)", IsNullable = true };
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var sql = string.Join("\n", generator.Generate([alter, add]).Select(command => command.CommandText));
        Assert.True(sql.IndexOf("ADD", StringComparison.Ordinal) < sql.IndexOf("CREATE INDEX", StringComparison.Ordinal));
        var remove = new AlterTableOperation { Name = "Rows" };
        remove.OldTable["OceanBase:ExpressionIndex:IX_Label"] = alter["OceanBase:ExpressionIndex:IX_Label"];
        var drop = new DropColumnOperation { Table = "Rows", Name = "NewLabel" };
        sql = string.Join("\n", generator.Generate([drop, remove]).Select(command => command.CommandText));
        Assert.True(sql.IndexOf("DROP INDEX", StringComparison.Ordinal) < sql.IndexOf("DROP COLUMN", StringComparison.Ordinal));
    }

    [Fact]
    public void Oracle_non_primary_key_identity_is_supported_by_both_creation_paths()
    {
        using var context = new NonKeyIdentityContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var operations = context.GetService<IMigrationsModelDiffer>().GetDifferences(null, model.GetRelationalModel());
        Assert.Contains("GENERATED ALWAYS AS IDENTITY", Assert.Single(context.GetService<IMigrationsSqlGenerator>().Generate(operations, model)).CommandText);
        Assert.Contains("GENERATED ALWAYS AS IDENTITY", context.Database.GenerateCreateScript());
    }

    [Fact]
    public void Oracle_sequence_cache_and_order_survive_model_diffs_and_DDL()
    {
        using var context = new MetadataContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var operations = context.GetService<IMigrationsModelDiffer>().GetDifferences(null, model.GetRelationalModel());
        var create = Assert.Single(operations.OfType<CreateSequenceOperation>());
        Assert.Equal(4L, create["OceanBase:SequenceCache"]);
        Assert.Equal(true, create["OceanBase:SequenceOrdered"]);
        Assert.Contains("CACHE 4 ORDER", Assert.Single(context.GetService<IMigrationsSqlGenerator>().Generate([create])).CommandText);
        var alter = new AlterSequenceOperation { Name = "Seq", IncrementBy = 5, MinValue = 1, MaxValue = 1000 };
        alter["OceanBase:SequenceCache"] = 0L;
        alter["OceanBase:SequenceOrdered"] = false;
        Assert.Contains("NOCACHE NOORDER", Assert.Single(context.GetService<IMigrationsSqlGenerator>().Generate([alter])).CommandText);
        alter.RemoveAnnotation("OceanBase:SequenceCache");
        alter.RemoveAnnotation("OceanBase:SequenceOrdered");
        alter.OldSequence["OceanBase:SequenceCache"] = 4L;
        alter.OldSequence["OceanBase:SequenceOrdered"] = true;
        Assert.Contains("NOCACHE NOORDER", Assert.Single(context.GetService<IMigrationsSqlGenerator>().Generate([alter])).CommandText);
    }

    [Fact]
    public void Relational_model_propagates_expression_and_identity_annotations()
    {
        using var context = new MetadataContext();
        var model = context.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var table = Assert.Single(model.Tables);
        Assert.Equal(new[] { "NONUNIQUE", "UPPER(\"Label\")" }, table["OceanBase:ExpressionIndex:IX_Label"]);
        Assert.Equal("ALWAYS", table.FindColumn("Id")!["OceanBase:IdentityGeneration"]);
        var operations = context.GetService<IMigrationsModelDiffer>().GetDifferences(null, model);
        var sql = string.Join("\n", context.GetService<IMigrationsSqlGenerator>().Generate(operations, model.Model).Select(command => command.CommandText));
        Assert.Contains("GENERATED ALWAYS AS IDENTITY START WITH 17 INCREMENT BY 5 NOCACHE", sql);
        Assert.Contains("CREATE INDEX \"IX_Label\"", sql);
        var createScript = context.Database.GenerateCreateScript();
        Assert.Contains("GENERATED ALWAYS AS IDENTITY START WITH 17 INCREMENT BY 5 NOCACHE", createScript);
        Assert.Contains("CREATE INDEX \"IX_Label\" ON \"Rows\" (UPPER(\"Label\"))", createScript);
        Assert.Contains("CACHE 4 ORDER", createScript);
    }

    private static DbContext Context() => new(new DbContextOptionsBuilder().UseOceanBase("Server=offline", OceanBaseCompatibilityMode.Oracle).Options);

    private sealed class MetadataContext() : DbContext(new DbContextOptionsBuilder().UseOceanBase("Server=offline", OceanBaseCompatibilityMode.Oracle).Options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            var row = builder.Entity("Row");
            row.ToTable("Rows");
            row.Property<long>("Id").ValueGeneratedOnAdd().HasAnnotation("OceanBase:IdentityGeneration", "ALWAYS")
                .HasAnnotation("OceanBase:IdentityOptions", "START WITH 17 INCREMENT BY 5 NOCACHE");
            row.HasKey("Id");
            row.Property<string>("Label").HasMaxLength(40);
            row.HasAnnotation("OceanBase:ExpressionIndex:IX_Label", new[] { "NONUNIQUE", "UPPER(\"Label\")" });
            builder.HasSequence<long>("Seq").StartsAt(17).IncrementsBy(5).HasMin(1).HasMax(1000)
                .HasAnnotation("OceanBase:SequenceCache", 4L).HasAnnotation("OceanBase:SequenceOrdered", true);
        }
    }

    private sealed class NonKeyIdentityContext() : DbContext(new DbContextOptionsBuilder().UseOceanBase("Server=offline", OceanBaseCompatibilityMode.Oracle).Options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            var row = builder.Entity("Row");
            row.ToTable("Rows");
            row.Property<int>("Id").ValueGeneratedNever();
            row.HasKey("Id");
            row.Property<long>("Generated").ValueGeneratedOnAdd().HasAnnotation("OceanBase:IdentityGeneration", "ALWAYS");
        }
    }
}
