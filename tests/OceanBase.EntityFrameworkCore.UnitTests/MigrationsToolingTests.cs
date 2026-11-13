using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Design.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using OceanBase.EntityFrameworkCore.Internal;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests
{
    public sealed class MigrationsToolingTests
    {
        public static IEnumerable<object[]> OptionsCases()
        {
            foreach (var mode in Enum.GetValues<OceanBaseCompatibilityMode>())
                foreach (var typed in new[] { false, true })
                    foreach (var connection in new[] { false, true }) yield return new object[] { mode, typed, connection };
        }

        [Fact]
        public void Provider_design_services_are_discoverable_and_use_EF_relational_lifetimes()
        {
            var assembly = typeof(OceanBaseCompatibilityMode).Assembly;
            var attribute = assembly.GetCustomAttribute<DesignTimeProviderServicesAttribute>();
            Assert.NotNull(attribute);
            var type = assembly.GetType(attribute.TypeName, throwOnError: true)!;
            Assert.True(type.IsPublic);
            var services = new ServiceCollection();
            ((IDesignTimeServices)Activator.CreateInstance(type)!).ConfigureDesignTimeServices(services);
            Assert.Contains(services, s => s.ServiceType == typeof(IAnnotationCodeGenerator) && s.Lifetime == ServiceLifetime.Singleton);
            Assert.DoesNotContain(services, s => s.ServiceType == typeof(Microsoft.EntityFrameworkCore.Scaffolding.IDatabaseModelFactory));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Actual_EF_scaffolder_preserves_identity_and_converted_defaults(OceanBaseCompatibilityMode mode)
        {
            using var context = new ToolingContext(new DbContextOptionsBuilder<ToolingContext>()
                .UseOceanBase("Server=offline;Database=tooling", mode).EnableServiceProviderCaching(false).Options);
            using var services = (ServiceProvider)new DesignTimeServicesBuilder(typeof(ToolingContext).Assembly,
                typeof(ToolingContext).Assembly, new OperationReporter(null), Array.Empty<string>()).Build(context);
            using var scope = services.CreateScope();
            var migration = scope.ServiceProvider.GetRequiredService<IMigrationsScaffolder>()
                .ScaffoldMigration("Initial", "Tooling.Sample", "Migrations", "C#", dryRun: true);
            Assert.Contains("CreateTable(", migration.MigrationCode);
            Assert.Contains("OceanBase:Identity", migration.MigrationCode);
            Assert.Contains("defaultValue: \"Y\"", migration.MigrationCode);
            Assert.Contains("HasDefaultValue(\"Y\")", migration.SnapshotCode);
            Assert.Contains("ValueGeneratedOnAdd()", migration.SnapshotCode);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "VARCHAR(32)" : "NVARCHAR2(32)", migration.MigrationCode);
        }

        [Theory]
        [MemberData(nameof(OptionsCases))]
        public void Migration_configuration_callbacks_preserve_connection_mode_scope_and_legacy_overloads(
            OceanBaseCompatibilityMode mode, bool typed, bool suppliedConnection)
        {
            DbContextOptionsBuilder builder = typed ? new DbContextOptionsBuilder<ToolingContext>() : new DbContextOptionsBuilder();
            builder.EnableServiceProviderCaching(false);
            builder.EnableOceanBaseSchemaManagement("tooling");
            using var connection = new OceanBaseConnector.OceanBaseConnection("Server=offline;Database=tooling");
            var methods = typeof(OceanBaseDbContextOptionsBuilderExtensions).GetMethods().Where(m => m.Name == "UseOceanBase"
                && m.IsGenericMethodDefinition == typed && m.GetParameters()[1].ParameterType == (suppliedConnection ? typeof(DbConnection) : typeof(string))).ToArray();
            Assert.Contains(methods, m => m.GetParameters().Length == 3);
            var method = Assert.Single(methods, m => m.GetParameters().Length == 4);
            if (typed) method = method.MakeGenericMethod(typeof(ToolingContext));
            var calls = 0;
            Action<object> configure = facade =>
            {
                calls++;
                facade.GetType().GetMethod("MigrationsAssembly")!.Invoke(facade, new object[] { typeof(ToolingContext).Assembly.GetName().Name! });
                facade.GetType().GetMethod("MigrationsHistoryTable")!.Invoke(facade, new object[] { "History`\"", "tooling" });
            };
            var result = method.Invoke(null, new object?[] { builder, suppliedConnection ? connection : "Server=offline;Database=tooling", mode, configure });
            Assert.Same(builder, result);
            Assert.Equal(1, calls);
            var relational = RelationalOptionsExtension.Extract(builder.Options);
            Assert.Equal(typeof(ToolingContext).Assembly.GetName().Name, relational.MigrationsAssembly);
            Assert.Equal("History`\"", relational.MigrationsHistoryTableName);
            Assert.Equal("tooling", relational.MigrationsHistoryTableSchema);
            var provider = builder.Options.FindExtension<OceanBaseOptionsExtension>()!;
            Assert.Equal(mode, provider.CompatibilityMode);
            Assert.Equal("tooling", provider.SchemaManagementScope);
            if (suppliedConnection) Assert.Same(connection, relational.Connection);
            using DbContext context = typed ? new ToolingContext((DbContextOptions<ToolingContext>)builder.Options) : new DbContext(builder.Options);
            Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "`History``\"`" : "\"History`\"\"\"",
                context.GetService<IHistoryRepository>().GetCreateIfNotExistsScript());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Migration_options_can_reset_and_resolve_a_separate_assembly(OceanBaseCompatibilityMode mode)
        {
            var builder = new DbContextOptionsBuilder().EnableServiceProviderCaching(false);
            builder.UseOceanBase("Server=offline;Database=tooling", mode, options => options
                .MigrationsAssembly(typeof(OceanBaseCompatibilityMode).Assembly.GetName().Name)
                .MigrationsHistoryTable("CustomHistory", "tooling"));
            using (var context = new DbContext(builder.Options))
                Assert.Same(typeof(OceanBaseCompatibilityMode).Assembly, context.GetService<IMigrationsAssembly>().Assembly);
            builder.UseOceanBase("Server=offline;Database=tooling", mode, options => options
                .MigrationsAssembly(null).MigrationsHistoryTable("ResetHistory"));
            var relational = RelationalOptionsExtension.Extract(builder.Options);
            Assert.Null(relational.MigrationsAssembly);
            Assert.Null(relational.MigrationsHistoryTableSchema);
            Assert.Equal("ResetHistory", relational.MigrationsHistoryTableName);
            Assert.Same(builder, builder.UseOceanBase("Server=offline;Database=tooling", mode, null));
            Assert.Equal("ResetHistory", RelationalOptionsExtension.Extract(builder.Options).MigrationsHistoryTableName);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Migration_configuration_preserves_namespace_ownership_factories(OceanBaseCompatibilityMode mode)
        {
            Func<OceanBaseConnector.OceanBaseConnection> management = () => throw new InvalidOperationException();
            Func<string> password = () => throw new InvalidOperationException();
            using var connection = new OceanBaseConnector.OceanBaseConnection("Server=offline;Database=tooling");
            var builder = new DbContextOptionsBuilder().UseOceanBase(connection, mode)
                .UseOceanBaseManagementConnection(management);
            if (mode == OceanBaseCompatibilityMode.MySql) builder.EnableOceanBaseDatabaseManagement("tooling");
            else builder.EnableOceanBaseUserManagement("tooling", password);
            builder.UseOceanBase(connection, mode, options => options
                .MigrationsAssembly("MigrationsAssembly").MigrationsHistoryTable("History", "tooling"));
            var provider = builder.Options.FindExtension<OceanBaseOptionsExtension>()!;
            Assert.True(provider.DatabaseManagementEnabled);
            Assert.Equal("tooling", provider.SchemaManagementScope);
            Assert.Same(management, provider.ManagementConnectionFactory);
            Assert.Same(mode == OceanBaseCompatibilityMode.Oracle ? password : null, provider.OracleUserPasswordFactory);
            Assert.Same(connection, provider.Connection);
            Assert.False(provider.IsConnectionOwned);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("bad\0name")]
        public void History_identifiers_reject_invalid_values(string? identifier)
        {
            var builder = new DbContextOptionsBuilder();
            Assert.ThrowsAny<ArgumentException>(() => builder.UseOceanBase("Server=offline", OceanBaseCompatibilityMode.MySql,
                options => options.MigrationsHistoryTable(identifier!)));
            if (identifier is not null)
                Assert.ThrowsAny<ArgumentException>(() => builder.UseOceanBase("Server=offline", OceanBaseCompatibilityMode.MySql,
                    options => options.MigrationsHistoryTable("History", identifier)));
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        public void Migration_assembly_rejects_empty_names(string name)
            => Assert.ThrowsAny<ArgumentException>(() => new DbContextOptionsBuilder()
                .UseOceanBase("Server=offline", OceanBaseCompatibilityMode.MySql, options => options.MigrationsAssembly(name)));

        public sealed class ToolingContext(DbContextOptions<ToolingContext> options) : DbContext(options)
        {
            protected override void OnModelCreating(ModelBuilder builder)
            {
                builder.Entity("Row", row =>
                {
                    row.ToTable("Rows");
                    row.Property<int>("Id").ValueGeneratedOnAdd();
                    row.HasKey("Id");
                    row.Property<string>("Name").HasMaxLength(32);
                    row.Property<bool>("Active").HasConversion(new BoolToStringConverter("N", "Y")).HasDefaultValue(true);
                });
            }
        }
    }
}
