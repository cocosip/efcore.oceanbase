using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBase.EntityFrameworkCore.Internal;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests
{
    public sealed class SchemaManagementOptionsTests
    {
        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Scope_survives_typed_untyped_and_relational_option_clones_without_leaking_into_logs(OceanBaseCompatibilityMode mode)
        {
            var original = new DbContextOptionsBuilder().EnableOceanBaseSchemaManagement("first_scope")
                .UseOceanBase("Server=offline;Database=first_scope", mode);
            var changed = new DbContextOptionsBuilder(original.Options).EnableOceanBaseSchemaManagement("second_scope")
                .UseOceanBase("Server=offline;Database=second_scope", mode);
            using var first = new DbContext(original.Options);
            using var second = new DbContext(changed.Options);
            var old = first.GetService<IDbContextOptions>().FindExtension<OceanBaseOptionsExtension>()!;
            var current = second.GetService<IDbContextOptions>().FindExtension<OceanBaseOptionsExtension>()!;
            Assert.Equal("first_scope", old.SchemaManagementScope);
            Assert.Equal("second_scope", current.SchemaManagementScope);
            Assert.Same(first.GetService<ISqlGenerationHelper>(), second.GetService<ISqlGenerationHelper>());
            Assert.Equal(mode, current.CompatibilityMode);
            var debug = new Dictionary<string, string>();
            current.Info.PopulateDebugInfo(debug);
            Assert.DoesNotContain("second_scope", current.Info.LogFragment);
            Assert.DoesNotContain("second_scope", string.Join(" ", debug.Values));

            var typed = new DbContextOptionsBuilder<TestContext>().EnableOceanBaseSchemaManagement("typed_scope")
                .UseOceanBase("Server=offline;Database=typed_scope", mode);
            var native = new OceanBaseConnector.OceanBaseConnection("Server=offline;Database=typed_scope");
            using (native)
            {
                typed.UseOceanBase(native, mode);
                Assert.Equal("typed_scope", typed.Options.FindExtension<OceanBaseOptionsExtension>()!.SchemaManagementScope);
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("bad\0scope")]
        public void Invalid_scopes_are_rejected(string? scope)
        {
            Assert.ThrowsAny<ArgumentException>(() => new DbContextOptionsBuilder().EnableOceanBaseSchemaManagement(scope!));
            Assert.ThrowsAny<ArgumentException>(() => new DbContextOptionsBuilder().EnableOceanBaseDatabaseManagement(scope!));
        }

        [Theory]
        [InlineData("mysql")]
        [InlineData("OceanBase")]
        [InlineData("SYS")]
        [InlineData("information_schema")]
        [InlineData("performance_schema")]
        public void System_databases_cannot_be_owned(string name)
            => Assert.Throws<ArgumentException>(() => new DbContextOptionsBuilder().EnableOceanBaseDatabaseManagement(name));

        [Fact]
        public void Explicit_database_ownership_has_typed_and_untyped_options_without_reusing_table_only_permission()
        {
            var builder = new DbContextOptionsBuilder().UseOceanBase("Server=offline;Database=owned", OceanBaseCompatibilityMode.MySql);
            Assert.Same(builder, builder.EnableOceanBaseDatabaseManagement("owned"));
            var extension = builder.Options.FindExtension<OceanBaseOptionsExtension>()!;
            Assert.Equal("owned", extension.SchemaManagementScope);
            Assert.True(extension.DatabaseManagementEnabled);
            builder.UseOceanBase("Server=offline;Database=owned", OceanBaseCompatibilityMode.MySql);
            Assert.True(builder.Options.FindExtension<OceanBaseOptionsExtension>()!.DatabaseManagementEnabled);
            builder.EnableOceanBaseSchemaManagement("owned");
            Assert.False(builder.Options.FindExtension<OceanBaseOptionsExtension>()!.DatabaseManagementEnabled);
            Assert.True(extension.DatabaseManagementEnabled);
            var typed = new DbContextOptionsBuilder<TestContext>();
            Assert.Same(typed, typed.EnableOceanBaseDatabaseManagement("owned"));
            typed.UseOceanBase("Server=offline;Database=owned", OceanBaseCompatibilityMode.MySql);
            using var namespaceContext = new TestContext(typed.Options);
            using var tableContext = new DbContext(builder.Options);
            Assert.Same(namespaceContext.GetService<ISqlGenerationHelper>(), tableContext.GetService<ISqlGenerationHelper>());
            Assert.DoesNotContain("owned", extension.Info.LogFragment);
            var debug = new Dictionary<string, string>();
            extension.Info.PopulateDebugInfo(debug);
            Assert.DoesNotContain("owned", string.Join(" ", debug.Values));
        }

        private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options);
    }
}
