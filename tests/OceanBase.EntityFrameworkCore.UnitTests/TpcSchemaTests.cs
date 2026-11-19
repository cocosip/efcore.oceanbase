using System;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using OceanBase.EntityFrameworkCore.Metadata;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests;

public sealed class TpcSchemaTests
{
    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql)]
    [InlineData(OceanBaseCompatibilityMode.Oracle)]
    public void Concrete_tables_repeat_inherited_columns_without_hierarchy_foreign_keys_or_identities(OceanBaseCompatibilityMode mode)
    {
        Check<int>(mode);
        Check<long>(mode);
        Check<Guid>(mode);
        Check<int>(mode, composite: true);
    }

    private static void Check<T>(OceanBaseCompatibilityMode mode, bool composite = false) where T : struct
    {
        using var context = Create<T>(mode, composite: composite);
        var model = context.GetService<IDesignTimeModel>().Model;
        var operations = context.GetService<IMigrationsModelDiffer>().GetDifferences(null, model.GetRelationalModel());
        var tables = operations.OfType<CreateTableOperation>().OrderBy(t => t.Name).ToArray();
        Assert.Equal(new[] { "LeafRows", "RootRows", "SiblingRows" }, tables.Select(t => t.Name));
        foreach (var table in tables)
        {
            Assert.Equal("app", table.Schema);
            Assert.Equal(composite ? new[] { "AssetId", "TenantId" } : new[] { "AssetId" }, table.PrimaryKey!.Columns);
            Assert.Empty(table.ForeignKeys);
            Assert.All(table.Columns, c => Assert.NotEqual(true, c[OceanBaseRelationalAnnotationProvider.IdentityAnnotation]));
            Assert.Contains(table.Columns, c => c.Name == "Name");
            Assert.Contains(table.Columns, c => c.Name == "Version" && c.IsNullable);
        }
        Assert.Contains(tables[0].Columns, c => c.Name == "Intermediate");
        Assert.Contains(tables[0].Columns, c => c.Name == "Reading");
        Assert.DoesNotContain(tables[1].Columns, c => c.Name == "Reading");
        var indexes = operations.OfType<CreateIndexOperation>().ToArray();
        Assert.Equal(3, indexes.Length);
        Assert.All(indexes, i => Assert.Equal(new[] { "TenantId" }, i.Columns));
        foreach (var sql in new[] { context.Database.GenerateCreateScript(), string.Concat(context.GetService<IMigrationsSqlGenerator>()
            .Generate(operations, model).Select(c => c.CommandText)) })
        {
            Assert.Equal(3, Regex.Matches(sql, "CREATE TABLE").Count);
            Assert.DoesNotContain("FOREIGN KEY", sql);
            Assert.DoesNotContain("AUTO_INCREMENT", sql);
            Assert.DoesNotContain("AS IDENTITY", sql);
            Assert.DoesNotContain("MiddleRows", sql);
            Assert.Contains("AssetId", sql);
        }
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql)]
    [InlineData(OceanBaseCompatibilityMode.Oracle)]
    public void Inherited_property_evolution_updates_each_concrete_table_and_reverses(OceanBaseCompatibilityMode mode)
    {
        using var before = Create<int>(mode);
        using var after = Create<int>(mode, expanded: true);
        var model = before.GetService<IDesignTimeModel>().Model;
        var next = after.GetService<IDesignTimeModel>().Model;
        var differ = before.GetService<IMigrationsModelDiffer>();
        var changes = differ.GetDifferences(model.GetRelationalModel(), next.GetRelationalModel());
        Assert.Equal(new[] { "LeafRows", "RootRows", "SiblingRows" }, changes.OfType<AddColumnOperation>().Select(c => c.Table).Order());
        Assert.All(changes, c => Assert.Equal("Revision", Assert.IsType<AddColumnOperation>(c).Name));
        var reverse = differ.GetDifferences(next.GetRelationalModel(), model.GetRelationalModel());
        Assert.Equal(3, reverse.Count);
        Assert.All(reverse, c => Assert.Equal("Revision", Assert.IsType<DropColumnOperation>(c).Name));
        Assert.Equal(3, Regex.Matches(string.Concat(before.GetService<IMigrationsSqlGenerator>()
            .Generate(changes, next).Select(c => c.CommandText)), "ALTER TABLE").Count);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, "identity")]
    [InlineData(OceanBaseCompatibilityMode.Oracle, "identity")]
    [InlineData(OceanBaseCompatibilityMode.MySql, "default")]
    [InlineData(OceanBaseCompatibilityMode.Oracle, "default")]
    public void Store_generated_keys_require_a_hierarchy_wide_strategy(OceanBaseCompatibilityMode mode, string generation)
    {
        using var context = Create<int>(mode, generation: generation);
        var failure = Assert.Throws<NotSupportedException>(() => context.Database.GenerateCreateScript());
        Assert.Contains("ValueGeneratedNever", failure.Message);
        Assert.Contains("Guid", failure.Message);
        var model = context.GetService<IDesignTimeModel>().Model;
        var operations = context.GetService<IMigrationsModelDiffer>().GetDifferences(null, model.GetRelationalModel());
        var migrationFailure = Assert.Throws<NotSupportedException>(() => context.GetService<IMigrationsSqlGenerator>().Generate(operations, model));
        Assert.Contains("ValueGeneratedNever", migrationFailure.Message);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.Oracle, "sequence", true)]
    [InlineData(OceanBaseCompatibilityMode.MySql, "sequence", false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, "cycle", false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, "wide", false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, "undeclared", false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, "unqualified", false)]
    public void Tpc_sequence_defaults_require_a_valid_shared_Oracle_sequence(
        OceanBaseCompatibilityMode mode, string generation, bool supported)
    {
        using var context = Create<int>(mode, generation: generation);
        var model = context.GetService<IDesignTimeModel>().Model;
        var operations = context.GetService<IMigrationsModelDiffer>().GetDifferences(null, model.GetRelationalModel());
        var generator = context.GetService<IMigrationsSqlGenerator>();
        if (!supported)
        {
            Assert.Throws<NotSupportedException>(() => context.Database.GenerateCreateScript());
            Assert.Throws<NotSupportedException>(() => generator.Generate(operations, model));
            return;
        }

        foreach (var sql in new[] { context.Database.GenerateCreateScript(),
            string.Concat(generator.Generate(operations, model).Select(command => command.CommandText)) })
        {
            Assert.Equal(3, Regex.Matches(sql, "DEFAULT \\(\"app\"\\.\"Ids\"\\.NEXTVAL\\)").Count);
            Assert.True(sql.IndexOf("CREATE SEQUENCE", StringComparison.Ordinal) < sql.IndexOf("CREATE TABLE", StringComparison.Ordinal));
            Assert.DoesNotContain("AS IDENTITY", sql);
        }
    }

    [Fact]
    public void Tpc_sequence_default_survives_the_dictionary_based_migration_target_model()
    {
        using var context = Create<int>(OceanBaseCompatibilityMode.Oracle, generation: "sequence");
        var builder = new ModelBuilder();
        builder.HasDefaultSchema("app");
        builder.HasSequence<long>("Ids", "app").HasMax(int.MaxValue);
        var root = builder.Entity("SnapshotRoot").UseTpcMappingStrategy().ToTable("RootRows");
        root.Property<int>("AssetId").HasColumnType("NUMBER(10)")
            .HasDefaultValueSql("\"app\".\"Ids\".NEXTVAL").ValueGeneratedOnAdd();
        root.HasKey("AssetId");
        builder.Entity("SnapshotLeaf").HasBaseType("SnapshotRoot").ToTable("LeafRows");
        var target = context.GetService<IModelRuntimeInitializer>().Initialize(builder.FinalizeModel());
        var operations = context.GetService<IMigrationsModelDiffer>().GetDifferences(null, target.GetRelationalModel());
        Assert.Contains(context.GetService<IMigrationsSqlGenerator>().Generate(operations, target), command =>
            command.CommandText.Contains("DEFAULT (\"app\".\"Ids\".NEXTVAL)", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("sequence-restart")]
    [InlineData("sequence-replacement")]
    public void Tpc_sequence_migration_cannot_reset_allocation_on_populated_tables(string generation)
    {
        using var before = Create<int>(OceanBaseCompatibilityMode.Oracle, generation: "sequence");
        using var after = Create<int>(OceanBaseCompatibilityMode.Oracle, generation: generation);
        var source = before.GetService<IDesignTimeModel>().Model;
        var target = after.GetService<IDesignTimeModel>().Model;
        var changes = after.GetService<IMigrationsModelDiffer>().GetDifferences(source.GetRelationalModel(), target.GetRelationalModel());
        Assert.NotEmpty(changes);
        Assert.Contains("data-preserving migration", Assert.Throws<NotSupportedException>(() =>
            after.GetService<IMigrationsSqlGenerator>().Generate(changes, target)).Message);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, "default")]
    [InlineData(OceanBaseCompatibilityMode.Oracle, "default")]
    [InlineData(OceanBaseCompatibilityMode.MySql, "constant")]
    [InlineData(OceanBaseCompatibilityMode.Oracle, "constant")]
    [InlineData(OceanBaseCompatibilityMode.MySql, "computed-never")]
    [InlineData(OceanBaseCompatibilityMode.Oracle, "computed-never")]
    [InlineData(OceanBaseCompatibilityMode.MySql, "numeric-guid")]
    [InlineData(OceanBaseCompatibilityMode.Oracle, "numeric-guid")]
    public void Unsafe_Guid_generation_is_not_mistaken_for_client_generation(OceanBaseCompatibilityMode mode, string generation)
    {
        using var context = Create<Guid>(mode, generation: generation);
        Assert.Contains("client-generated Guid", Assert.Throws<NotSupportedException>(() => context.Database.GenerateCreateScript()).Message);
        var model = context.GetService<IDesignTimeModel>().Model;
        var operations = context.GetService<IMigrationsModelDiffer>().GetDifferences(null, model.GetRelationalModel());
        Assert.Throws<NotSupportedException>(() => context.GetService<IMigrationsSqlGenerator>().Generate(operations, model));
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql)]
    [InlineData(OceanBaseCompatibilityMode.Oracle)]
    public void Concrete_relationship_foreign_key_targets_the_leaf_table_and_inherited_key(OceanBaseCompatibilityMode mode)
    {
        using var context = Create<int>(mode, relationship: true);
        var model = context.GetService<IDesignTimeModel>().Model;
        var operations = context.GetService<IMigrationsModelDiffer>().GetDifferences(null, model.GetRelationalModel());
        var table = Assert.Single(operations.OfType<CreateTableOperation>(), t => t.Name == "Links");
        var foreignKey = Assert.Single(table.ForeignKeys);
        Assert.Equal("LeafRows", foreignKey.PrincipalTable);
        Assert.Equal("app", foreignKey.PrincipalSchema);
        Assert.Equal(new[] { "AssetId" }, foreignKey.PrincipalColumns);
        Assert.Equal(new[] { "LeafId" }, foreignKey.Columns);
        Assert.Contains(operations.OfType<CreateIndexOperation>(), i => i.Table == "Links" && i.Columns.SequenceEqual(new[] { "LeafId" }));
        foreach (var sql in new[] { context.Database.GenerateCreateScript(), string.Concat(context.GetService<IMigrationsSqlGenerator>()
            .Generate(operations, model).Select(c => c.CommandText)) })
        {
            Assert.Single(Regex.Matches(sql, "FOREIGN KEY"));
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "REFERENCES `app`.`LeafRows` (`AssetId`)"
                : "REFERENCES \"app\".\"LeafRows\" (\"AssetId\")", sql);
        }
    }

    private static HierarchyContext<T> Create<T>(OceanBaseCompatibilityMode mode, bool expanded = false,
        bool composite = false, string generation = "assigned", bool relationship = false) where T : struct
        => new(new DbContextOptionsBuilder().UseOceanBase("Server=offline;Database=app", mode)
            .EnableOceanBaseSchemaManagement("app").EnableServiceProviderCaching(false).Options, expanded, composite, generation, relationship);

    private sealed class HierarchyContext<T>(DbContextOptions options, bool expanded, bool composite, string generation, bool relationship) : DbContext(options) where T : struct
    {
        protected override void OnModelCreating(ModelBuilder model)
        {
            model.HasDefaultSchema("app");
            var root = model.Entity<Root<T>>();
            root.UseTpcMappingStrategy().ToTable("RootRows");
            root.Property(r => r.Id).HasColumnName("AssetId");
            root.Property(r => r.Name).HasMaxLength(40);
            root.Property(r => r.Version).IsConcurrencyToken();
            root.HasIndex(r => r.TenantId);
            if (typeof(T) != typeof(Guid)) root.Property(r => r.Id).ValueGeneratedNever();
            if (composite) root.HasKey(r => new { r.Id, r.TenantId });
            if (generation == "identity") root.Property(r => r.Id).ValueGeneratedOnAdd();
            if (generation == "default") root.Property(r => r.Id).HasDefaultValueSql("42").ValueGeneratedOnAdd();
            if (generation == "constant") root.Property(r => r.Id).HasDefaultValue(default(T)).ValueGeneratedOnAdd();
            if (generation == "computed-never") root.Property(r => r.Id).HasComputedColumnSql("42").ValueGeneratedNever();
            if (generation == "numeric-guid") root.Property(r => r.Id).HasConversion(v => (long)v.GetHashCode(), v => default(T));
            if (generation is "sequence" or "cycle" or "wide" or "undeclared" or "unqualified"
                or "sequence-restart" or "sequence-replacement")
            {
                var sequence = model.HasSequence<long>(generation == "sequence-replacement" ? "NewIds" : "Ids", "app");
                if (generation != "wide") sequence.HasMax(int.MaxValue);
                if (generation == "cycle") sequence.IsCyclic();
                if (generation == "sequence-restart") sequence.StartsAt(101);
                var defaultSql = generation switch
                {
                    "undeclared" => "\"app\".\"OtherIds\".NEXTVAL",
                    "unqualified" => "\"Ids\".NEXTVAL",
                    _ => "\"app\".\"" + sequence.Metadata.Name + "\".NEXTVAL"
                };
                root.Property(r => r.Id).HasDefaultValueSql(defaultSql).ValueGeneratedOnAdd();
            }
            model.Entity<Middle<T>>();
            model.Entity<Leaf<T>>().ToTable("LeafRows");
            model.Entity<Sibling<T>>().ToTable("SiblingRows");
            if (expanded) root.Property<int?>("Revision");
            if (relationship)
            {
                model.Entity<Link<T>>().ToTable("Links").Property(l => l.Id).ValueGeneratedNever();
                model.Entity<Link<T>>().HasOne(l => l.Leaf).WithMany().HasForeignKey(l => l.LeafId);
            }
        }
    }
    private class Root<T> where T : struct { public T Id { get; set; } public int TenantId { get; set; } public string? Name { get; set; } public int? Version { get; set; } }
    private abstract class Middle<T> : Root<T> where T : struct { public int Intermediate { get; set; } }
    private sealed class Leaf<T> : Middle<T> where T : struct { public int? Reading { get; set; } }
    private sealed class Sibling<T> : Root<T> where T : struct { public int Pages { get; set; } }
    private sealed class Link<T> where T : struct { public int Id { get; set; } public T LeafId { get; set; } public Leaf<T> Leaf { get; set; } = null!; }
}
