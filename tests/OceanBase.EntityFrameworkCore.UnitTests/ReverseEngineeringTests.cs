using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design.Internal;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using CatalogConnection = OceanBase.EntityFrameworkCore.TestUtilities.ScaffoldingCatalogConnection;

namespace OceanBase.EntityFrameworkCore.UnitTests;

public sealed class ReverseEngineeringTests
{
    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql)]
    [InlineData(OceanBaseCompatibilityMode.Oracle)]
    public void Context_free_services_reverse_engineer_the_catalog_without_losing_relational_metadata(OceanBaseCompatibilityMode mode)
    {
        using var services = Services();
        using var connection = new CatalogConnection(mode);
        var database = services.GetRequiredService<IDatabaseModelFactory>().Create(connection, new DatabaseModelFactoryOptions());
        Assert.Equal(ConnectionState.Closed, connection.State);
        Assert.Equal(1, connection.Opens);
        Assert.Equal(1, connection.Closes);
        Assert.Equal(0, connection.Disposals);
        Assert.Equal("app", database.DefaultSchema);
        Assert.Equal(3, database.Tables.Count);
        var parent = database.Tables.Single(t => t.Name == "Parents");
        Assert.Equal(new[] { "Id", "Name" }, parent.Columns.Select(c => c.Name));
        Assert.Equal(ValueGenerated.OnAdd, parent.Columns[0].ValueGenerated);
        Assert.Equal(mode == OceanBaseCompatibilityMode.MySql ? typeof(int) : typeof(long), parent.Columns[0]["ClrType"]);
        Assert.Same(parent.Columns[0], Assert.Single(parent.PrimaryKey!.Columns));
        Assert.Equal("Name", Assert.Single(Assert.Single(parent.UniqueConstraints).Columns).Name);
        var child = database.Tables.Single(t => t.Name == "Children");
        Assert.Equal(new[] { "ParentId", "Seq" }, child.PrimaryKey!.Columns.Select(c => c.Name));
        var foreign = Assert.Single(child.ForeignKeys);
        Assert.Same(parent, foreign.PrincipalTable);
        Assert.Equal("ParentId", Assert.Single(foreign.Columns).Name);
        Assert.Equal("Id", Assert.Single(foreign.PrincipalColumns).Name);
        Assert.Equal(Microsoft.EntityFrameworkCore.Migrations.ReferentialAction.Cascade, foreign.OnDelete);
        var index = Assert.Single(child.Indexes);
        Assert.Equal(new[] { "Note", "Seq" }, index.Columns.Select(c => c.Name));
        Assert.Equal(new[] { true, false }, index.IsDescending);
        var computed = child.Columns.Single(c => c.Name == "Total");
        Assert.Equal("1 + 1", computed.ComputedColumnSql);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, computed.ValueGenerated);
        Assert.IsType<DatabaseView>(database.Tables.Single(t => t.Name == "Recent"));
        Assert.All(connection.Restrictions, r => Assert.Equal("app", r[mode == OceanBaseCompatibilityMode.MySql ? 0 : 1]));
        Assert.Contains("Columns", connection.Collections);
        Assert.DoesNotContain(connection.Commands, c => c.Sql.Contains("COLUMNS", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql)]
    [InlineData(OceanBaseCompatibilityMode.Oracle)]
    public void Real_EF_model_and_CSharp_generation_work_without_an_existing_context(OceanBaseCompatibilityMode mode)
    {
        using var services = Services();
        using var connection = new CatalogConnection(mode);
        var database = services.GetRequiredService<IDatabaseModelFactory>().Create(connection, new DatabaseModelFactoryOptions());
        var model = services.GetRequiredService<IScaffoldingModelFactory>().Create(database,
            new ModelReverseEngineerOptions { UseDatabaseNames = true });
        var parent = model.GetEntityTypes().Single(e => e.GetTableName() == "Parents");
        Assert.Equal(ValueGenerated.OnAdd, parent.FindProperty("Id")!.ValueGenerated);
        Assert.Single(model.GetEntityTypes().Single(e => e.GetTableName() == "Children").GetForeignKeys());
        Assert.Null(model.GetEntityTypes().Single(e => e.GetViewName() == "Recent").FindPrimaryKey());
        var code = services.GetRequiredService<IModelCodeGeneratorSelector>().Select(new ModelCodeGenerationOptions { Language = "C#" })
            .GenerateModel(model, new ModelCodeGenerationOptions
            {
                Language = "C#", ContextName = "CatalogContext", ConnectionString = connection.ConnectionString,
                ModelNamespace = "Generated", ContextNamespace = "Generated", UseNullableReferenceTypes = true
            });
        Assert.Contains("UseOceanBase", code.ContextFile.Code);
        Assert.Contains("OceanBaseCompatibilityMode." + mode, code.ContextFile.Code);
        Assert.Contains("HasForeignKey", code.ContextFile.Code);
        Assert.Contains("HasComputedColumnSql", code.ContextFile.Code);
        Assert.Contains("ToView", code.ContextFile.Code);
        Assert.Equal(3, code.AdditionalFiles.Count);
        Assert.DoesNotContain("OceanBase:Identity", code.ContextFile.Code);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public void Caller_ownership_survives_success_and_catalog_failure(OceanBaseCompatibilityMode mode, bool failure)
    {
        using var services = Services();
        using var connection = new CatalogConnection(mode) { CatalogFailure = failure };
        connection.Open();
        if (failure) Assert.Equal("Synthetic catalog failure.", Assert.Throws<InvalidOperationException>(() => services.GetRequiredService<IDatabaseModelFactory>()
            .Create(connection, new DatabaseModelFactoryOptions())).Message);
        else services.GetRequiredService<IDatabaseModelFactory>().Create(connection, new DatabaseModelFactoryOptions());
        Assert.Equal(ConnectionState.Open, connection.State);
        Assert.Equal(0, connection.Closes);
        Assert.Equal(0, connection.Disposals);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql)]
    [InlineData(OceanBaseCompatibilityMode.Oracle)]
    public void Failed_catalog_read_closes_only_the_connection_the_factory_opened(OceanBaseCompatibilityMode mode)
    {
        using var services = Services();
        using var connection = new CatalogConnection(mode) { CatalogFailure = true };
        Assert.Equal("Synthetic catalog failure.", Assert.Throws<InvalidOperationException>(() => services.GetRequiredService<IDatabaseModelFactory>()
            .Create(connection, new DatabaseModelFactoryOptions())).Message);
        Assert.Equal(1, connection.Closes);
        Assert.Equal(ConnectionState.Closed, connection.State);
        Assert.Equal(0, connection.Disposals);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql)]
    [InlineData(OceanBaseCompatibilityMode.Oracle)]
    public void Table_selection_excludes_unselected_unsupported_columns(OceanBaseCompatibilityMode mode)
    {
        using var services = Services();
        using var connection = new CatalogConnection(mode) { UnknownChildType = true };
        var model = services.GetRequiredService<IDatabaseModelFactory>().Create(connection,
            new DatabaseModelFactoryOptions(["\"app\".\"Parents\""], []));
        Assert.Equal("Parents", Assert.Single(model.Tables).Name);
        using var other = new CatalogConnection(mode) { UnknownChildType = true };
        Assert.Throws<NotSupportedException>(() => services.GetRequiredService<IDatabaseModelFactory>()
            .Create(other, new DatabaseModelFactoryOptions()));
    }

    [Fact]
    public void Missing_explicit_mode_rejects_before_open()
    {
        using var services = Services();
        using var connection = new CatalogConnection(OceanBaseCompatibilityMode.MySql) { ConnectionString = "Server=offline;Database=app" };
        var error = Assert.Throws<ArgumentException>(() => services.GetRequiredService<IDatabaseModelFactory>()
            .Create(connection, new DatabaseModelFactoryOptions()));
        Assert.Contains("ExpectedMode", error.Message);
        Assert.Equal(0, connection.Opens);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, "decimal(65,30)", typeof(decimal))]
    [InlineData(OceanBaseCompatibilityMode.MySql, "date", typeof(DateOnly))]
    [InlineData(OceanBaseCompatibilityMode.MySql, "bigint unsigned", typeof(ulong))]
    [InlineData(OceanBaseCompatibilityMode.Oracle, "DATE", typeof(DateTime))]
    [InlineData(OceanBaseCompatibilityMode.Oracle, "TIMESTAMP(9) WITH TIME ZONE", typeof(DateTimeOffset))]
    [InlineData(OceanBaseCompatibilityMode.Oracle, "TIMESTAMP(9)", typeof(DateTime))]
    [InlineData(OceanBaseCompatibilityMode.Oracle, "TIMESTAMP(6)", typeof(DateTime))]
    [InlineData(OceanBaseCompatibilityMode.Oracle, "CHAR(10 CHAR)", typeof(string))]
    [InlineData(OceanBaseCompatibilityMode.Oracle, "CHAR(10 BYTE)", typeof(string))]
    [InlineData(OceanBaseCompatibilityMode.Oracle, "INTERVAL DAY(3) TO SECOND(6)", typeof(TimeSpan))]
    public void Scalar_declarations_survive_actual_model_generation(OceanBaseCompatibilityMode mode, string store, Type clr)
    {
        using var services = Services();
        using var connection = new CatalogConnection(mode)
        {
            Rewrite = (collection, rows) =>
            {
                if (collection != "Columns") return;
                var row = rows.Rows.Cast<DataRow>().Single(r => (string)r["TABLE_NAME"] == "Children" && (string)r["COLUMN_NAME"] == "Total");
                row["TYPE_DECLARATION"] = store;
                row["IS_COMPUTED"] = false;
                row["COMPUTED_EXPRESSION"] = DBNull.Value;
                row["IS_STORED"] = DBNull.Value;
            }
        };
        var database = services.GetRequiredService<IDatabaseModelFactory>().Create(connection, new DatabaseModelFactoryOptions());
        var model = services.GetRequiredService<IScaffoldingModelFactory>().Create(database, new ModelReverseEngineerOptions());
        var property = model.GetEntityTypes().Single(e => e.GetTableName() == "Children").FindProperty("Total")!;
        Assert.Equal(clr, Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType);
        Assert.Equal(store, property.GetColumnType());
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql)]
    [InlineData(OceanBaseCompatibilityMode.Oracle)]
    public void Quoted_table_selectors_preserve_identifier_spaces_and_escaped_quotes(OceanBaseCompatibilityMode mode)
    {
        const string name = " Parent.\"` ";
        using var services = Services();
        using var connection = new CatalogConnection(mode)
        {
            Rewrite = (_, rows) =>
            {
                foreach (DataRow row in rows.Rows)
                    foreach (var column in new[] { "TABLE_NAME", "REFERENCED_TABLE_NAME" })
                        if (rows.Columns.Contains(column) && Equals(row[column], "Parents")) row[column] = name;
            }
        };
        var selector = "\"app\".\"" + name.Replace("\"", "\"\"") + "\"";
        var database = services.GetRequiredService<IDatabaseModelFactory>().Create(connection, new DatabaseModelFactoryOptions([selector], []));
        Assert.Equal(name, Assert.Single(database.Tables).Name);
    }

    [Theory]
    [InlineData("app..Parents")]
    [InlineData("\"\".Parents")]
    [InlineData("app.\"Parents")]
    [InlineData("app.\"Parents\"suffix")]
    public void Invalid_table_selection_is_rejected(string selector)
    {
        using var services = Services();
        using var connection = new CatalogConnection(OceanBaseCompatibilityMode.MySql);
        Assert.Throws<ArgumentException>(() => services.GetRequiredService<IDatabaseModelFactory>()
            .Create(connection, new DatabaseModelFactoryOptions([selector], [])));
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Theory]
    [InlineData("Tables", "TABLE_CATALOG", "other")]
    [InlineData("ConstraintColumns", "ORDINAL_POSITION", 7)]
    [InlineData("IndexColumns", "ORDINAL_POSITION", 7)]
    [InlineData("ForeignKeyColumns", "ORDINAL_POSITION", 7)]
    [InlineData("Indexes", "IS_UNIQUE", "UNKNOWN")]
    [InlineData("IndexColumns", "SORT_ORDER", "UNKNOWN")]
    [InlineData("IndexColumns", "PREFIX_LENGTH", 4)]
    [InlineData("ForeignKeys", "UPDATE_RULE", "CASCADE")]
    public void Inconsistent_or_unrepresentable_selected_catalog_cannot_silently_generate_a_model(string collection, string field, object value)
    {
        using var services = Services();
        using var connection = new CatalogConnection(OceanBaseCompatibilityMode.MySql)
        {
            Rewrite = (name, rows) => { if (name == collection) rows.Rows[^1][field] = value; }
        };
        Assert.Throws<NotSupportedException>(() => services.GetRequiredService<IDatabaseModelFactory>()
            .Create(connection, new DatabaseModelFactoryOptions()));
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql)]
    [InlineData(OceanBaseCompatibilityMode.Oracle)]
    public void Separately_named_unique_index_is_retained(OceanBaseCompatibilityMode mode)
    {
        using var services = Services();
        using var connection = new CatalogConnection(mode)
        {
            Rewrite = (name, rows) =>
            {
                if (name is not ("Indexes" or "IndexColumns")) return;
                var source = rows.Rows.Cast<DataRow>().Single(r => Equals(r["INDEX_NAME"], "UQ_Name"));
                var added = rows.NewRow(); added.ItemArray = source.ItemArray; added["INDEX_NAME"] = "IX_SeparateName"; rows.Rows.Add(added);
            }
        };
        var database = services.GetRequiredService<IDatabaseModelFactory>().Create(connection, new DatabaseModelFactoryOptions());
        Assert.Equal("IX_SeparateName", Assert.Single(database.Tables.Single(t => t.Name == "Parents").Indexes).Name);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql)]
    [InlineData(OceanBaseCompatibilityMode.Oracle)]
    public void Documented_030_unknown_behavior_warns_and_keeps_usable_columns(OceanBaseCompatibilityMode mode)
    {
        var warnings = new List<string>();
        using var services = Services(warnings);
        using var connection = new CatalogConnection(mode) { Driver030Profile = true };
        var database = services.GetRequiredService<IDatabaseModelFactory>().Create(connection, new DatabaseModelFactoryOptions());
        var parent = database.Tables.Single(t => t.Name == "Parents");
        Assert.Equal(3, database.Tables.Count);
        Assert.Contains(warnings, w => w.Contains("HAS_DEFAULT", StringComparison.Ordinal));
        Assert.Null(parent.Columns[1].DefaultValueSql);
        if (mode == OceanBaseCompatibilityMode.MySql) Assert.Same(DBNull.Value, parent.Columns[1].DefaultValue);
        if (mode == OceanBaseCompatibilityMode.Oracle)
        {
            Assert.Null(parent.Columns[0].ValueGenerated);
            Assert.Contains(warnings, w => w.Contains("IS_IDENTITY", StringComparison.Ordinal));
            Assert.Contains(warnings, w => w.Contains("IS_COMPUTED", StringComparison.Ordinal));
            Assert.Contains(warnings, w => w.Contains("HAS_ON_UPDATE", StringComparison.Ordinal));
        }
        var model = services.GetRequiredService<IScaffoldingModelFactory>().Create(database, new ModelReverseEngineerOptions());
        var code = services.GetRequiredService<IModelCodeGeneratorSelector>().Select(new ModelCodeGenerationOptions { Language = "C#" })
            .GenerateModel(model, new ModelCodeGenerationOptions { Language = "C#", ContextName = "PartialContext", ConnectionString = connection.ConnectionString });
        Assert.Contains("UseOceanBase", code.ContextFile.Code);
        if (mode == OceanBaseCompatibilityMode.MySql) Assert.Contains("HasDefaultValue()", code.ContextFile.Code);
    }

    [Theory]
    [InlineData("IS_IDENTITY")]
    [InlineData("IS_COMPUTED")]
    [InlineData("HAS_DEFAULT")]
    [InlineData("HAS_ON_UPDATE")]
    public void Unknown_behavior_is_not_coerced_to_false(string field)
    {
        var warnings = new List<string>();
        using var services = Services(warnings);
        using var connection = new CatalogConnection(OceanBaseCompatibilityMode.MySql)
        {
            Rewrite = (name, rows) =>
            {
                if (name != "Columns") return;
                var row = rows.Rows.Cast<DataRow>().Single(r => Equals(r["TABLE_NAME"], "Children") && Equals(r["COLUMN_NAME"], "Note"));
                row[field] = DBNull.Value;
            }
        };
        var database = services.GetRequiredService<IDatabaseModelFactory>().Create(connection, new DatabaseModelFactoryOptions());
        Assert.Equal(3, database.Tables.Count);
        Assert.Contains(warnings, w => w.Contains(field, StringComparison.Ordinal) && w.Contains("Note", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("TYPE_DECLARATION", null)]
    [InlineData("IS_COMPUTED", false)]
    [InlineData("HAS_DEFAULT", true)]
    [InlineData("HAS_ON_UPDATE", true)]
    public void Missing_type_or_contradictory_generation_and_known_unsupported_update_still_reject(string field, object? value)
    {
        using var services = Services();
        using var connection = new CatalogConnection(OceanBaseCompatibilityMode.MySql)
        {
            Rewrite = (name, rows) => { if (name == "Columns") rows.Rows[5][field] = value ?? DBNull.Value; }
        };
        var error = Assert.Throws<NotSupportedException>(() => services.GetRequiredService<IDatabaseModelFactory>()
            .Create(connection, new DatabaseModelFactoryOptions()));
        Assert.Contains(field, error.Message);
    }

    [Theory]
    [InlineData("COMPUTED_EXPRESSION", true)]
    [InlineData("IS_STORED", true)]
    [InlineData("DEFAULT_VALUE_SQL", false)]
    public void Known_generation_or_default_with_missing_detail_warns_without_inventing_SQL(string field, bool computed)
    {
        var warnings = new List<string>();
        using var services = Services(warnings);
        using var connection = new CatalogConnection(OceanBaseCompatibilityMode.MySql)
        {
            Rewrite = (name, rows) => { if (name == "Columns") rows.Rows[computed ? 5 : 1][field] = DBNull.Value; }
        };
        var database = services.GetRequiredService<IDatabaseModelFactory>().Create(connection, new DatabaseModelFactoryOptions());
        var column = database.Tables.Single(t => t.Name == (computed ? "Children" : "Parents")).Columns[computed ? 3 : 1];
        if (field == "IS_STORED") Assert.Null(column.IsStored);
        else if (computed) Assert.Equal("", column.ComputedColumnSql);
        else { Assert.Null(column.DefaultValueSql); Assert.Same(DBNull.Value, column.DefaultValue); }
        Assert.Contains(warnings, w => w.Contains(field, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("TYPE_DECLARATION")]
    [InlineData("IS_IDENTITY")]
    public void Legacy_or_malformed_column_contract_has_an_actionable_error(string field)
    {
        using var services = Services();
        using var connection = new CatalogConnection(OceanBaseCompatibilityMode.MySql)
        {
            Rewrite = (name, rows) => { if (name == "Columns") rows.Columns.Remove(field); }
        };
        var error = Assert.Throws<NotSupportedException>(() => services.GetRequiredService<IDatabaseModelFactory>()
            .Create(connection, new DatabaseModelFactoryOptions()));
        Assert.Contains(field, error.Message);
        Assert.Contains("0.3.0", error.Message);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("documented column", "documented column")]
    public void Empty_directory_comment_does_not_add_a_schema_comment_but_nonempty_text_is_preserved(string text, string? expected)
    {
        using var services = Services();
        using var connection = new CatalogConnection(OceanBaseCompatibilityMode.MySql)
        {
            Rewrite = (name, rows) => { if (name == "Columns") rows.Rows[0]["COLUMN_COMMENT"] = text; }
        };
        var database = services.GetRequiredService<IDatabaseModelFactory>().Create(connection, new DatabaseModelFactoryOptions());
        Assert.Equal(expected, database.Tables.Single(t => t.Name == "Parents").Columns[0].Comment);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql)]
    [InlineData(OceanBaseCompatibilityMode.Oracle)]
    public void Named_connection_codegen_uses_the_resolved_mode_and_keeps_the_configuration_name(OceanBaseCompatibilityMode mode)
    {
        var collection = new DesignTimeServicesBuilder(typeof(ReverseEngineeringTests).Assembly,
            typeof(ReverseEngineeringTests).Assembly, new OperationReporter(null), [])
            .CreateServiceCollection(typeof(OceanBaseCompatibilityMode).Assembly.GetName().Name!);
        collection.Replace(ServiceDescriptor.Singleton<INamedConnectionStringResolver>(new NamedConnection(mode)));
        using var services = collection.BuildServiceProvider();
        var code = services.GetRequiredService<IProviderConfigurationCodeGenerator>().GenerateUseProvider("Name=Catalog", null);
        Assert.Equal("Name=Catalog", code.Arguments[0]);
        Assert.Equal(mode, code.Arguments[1]);
    }

    private sealed class NamedConnection(OceanBaseCompatibilityMode mode) : INamedConnectionStringResolver
    {
        public string ResolveConnectionString(string connectionString)
        {
            Assert.Equal("Name=Catalog", connectionString);
            return "Server=offline;Database=app;ExpectedMode=" + mode;
        }
    }

    private static ServiceProvider Services(List<string>? warnings = null)
        => (ServiceProvider)new DesignTimeServicesBuilder(typeof(ReverseEngineeringTests).Assembly,
            typeof(ReverseEngineeringTests).Assembly,
            new OperationReporter(warnings is null ? null : new OperationReportHandler(null, warnings.Add, null, null)), [])
            .Build(typeof(OceanBaseCompatibilityMode).Assembly.GetName().Name!);
}
