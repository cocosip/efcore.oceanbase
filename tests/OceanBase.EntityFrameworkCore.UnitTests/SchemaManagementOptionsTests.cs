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
            => Assert.ThrowsAny<ArgumentException>(() => new DbContextOptionsBuilder().EnableOceanBaseSchemaManagement(scope!));

        private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options);
    }
}
