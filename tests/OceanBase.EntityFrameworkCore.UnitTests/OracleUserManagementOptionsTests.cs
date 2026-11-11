using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBase.EntityFrameworkCore.Internal;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests
{
    public sealed class OracleUserManagementOptionsTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void User_ownership_and_password_factory_clone_without_invocation_or_log_identity(bool typed)
        {
            Func<string> password = () => throw new InvalidOperationException("Must not request a password.");
            var builder = new DbContextOptionsBuilder<TestContext>();
            Assert.Same(builder, typed ? builder.EnableOceanBaseUserManagement("owned", password)
                : ((DbContextOptionsBuilder)builder).EnableOceanBaseUserManagement("owned", password));
            builder.UseOceanBase("Server=offline;User Id=owned@tenant", OceanBaseCompatibilityMode.Oracle);
            var original = builder.Options.FindExtension<OceanBaseOptionsExtension>()!;
            Assert.True(original.DatabaseManagementEnabled);
            Assert.Equal("owned", original.SchemaManagementScope);
            Assert.Same(password, original.OracleUserPasswordFactory);
            var clone = new DbContextOptionsBuilder(builder.Options).EnableOceanBaseSchemaManagement("other");
            Assert.Null(clone.Options.FindExtension<OceanBaseOptionsExtension>()!.OracleUserPasswordFactory);
            using var first = new TestContext(builder.Options);
            using var second = new TestContext((DbContextOptions<TestContext>)clone.Options);
            Assert.Same(first.GetService<ISqlGenerationHelper>(), second.GetService<ISqlGenerationHelper>());
            var debug = new Dictionary<string, string>();
            original.Info.PopulateDebugInfo(debug);
            Assert.DoesNotContain("owned", original.Info.LogFragment);
            Assert.DoesNotContain("Password", string.Join(" ", debug.Keys));
            builder.EnableOceanBaseDatabaseManagement("mysql_database");
            Assert.Null(builder.Options.FindExtension<OceanBaseOptionsExtension>()!.OracleUserPasswordFactory);
            Assert.Same(password, original.OracleUserPasswordFactory);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("bad\0name")]
        [InlineData("sys")]
        [InlineData("SYSTEM")]
        [InlineData("public")]
        [InlineData("LBACSYS")]
        [InlineData("ORAAUDITOR")]
        [InlineData("owned@tenant")]
        [InlineData("SYS@tenant")]
        public void Invalid_or_system_users_cannot_be_owned(string? name)
        {
            var builder = new DbContextOptionsBuilder();
            Assert.ThrowsAny<ArgumentException>(() => builder.EnableOceanBaseUserManagement(name!, () => "synthetic"));
            Assert.Null(builder.Options.FindExtension<OceanBaseOptionsExtension>());
        }

        [Fact]
        public void Null_builders_and_factories_reject()
        {
            Assert.Throws<ArgumentNullException>(() => ((DbContextOptionsBuilder)null!).EnableOceanBaseUserManagement("owned", () => "synthetic"));
            Assert.Throws<ArgumentNullException>(() => new DbContextOptionsBuilder().EnableOceanBaseUserManagement("owned", null!));
        }

        private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options);
    }
}
