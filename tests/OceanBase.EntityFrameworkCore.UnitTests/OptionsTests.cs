using System;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBase.EntityFrameworkCore.Internal;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests
{
    public sealed class OptionsTests
    {
        [Fact]
        public void UseOceanBase_with_connection_string_preserves_mode_and_connection_string()
        {
            var options = new DbContextOptionsBuilder<TestContext>()
                .UseOceanBase("Server=localhost;Database=test", OceanBaseCompatibilityMode.Oracle)
                .Options;

            var extension = options.FindExtension<OceanBaseOptionsExtension>();

            Assert.NotNull(extension);
            Assert.Equal(OceanBaseCompatibilityMode.Oracle, extension!.CompatibilityMode);
            Assert.Equal("Server=localhost;Database=test", extension.ConnectionString);
        }

        [Fact]
        public void UseOceanBase_with_existing_connection_preserves_connection_and_mode()
        {
            using var connection = new StubDbConnection();
            var options = new DbContextOptionsBuilder<TestContext>()
                .UseOceanBase(connection, OceanBaseCompatibilityMode.MySql)
                .Options;

            var extension = options.FindExtension<OceanBaseOptionsExtension>();

            Assert.NotNull(extension);
            Assert.Same(connection, extension!.Connection);
            Assert.False(extension.IsConnectionOwned);
            Assert.Equal(OceanBaseCompatibilityMode.MySql, extension.CompatibilityMode);
        }

        [Fact]
        public void Model_cache_key_includes_compatibility_mode()
        {
            var mysqlOptions = CreateOptions(OceanBaseCompatibilityMode.MySql);
            var oracleOptions = CreateOptions(OceanBaseCompatibilityMode.Oracle);
            using var mysql = new TestContext(mysqlOptions);
            using var oracle = new TestContext(oracleOptions);

            var mysqlKey = new OceanBaseModelCacheKeyFactory(
                    new ModelCacheKeyFactoryDependencies(),
                    mysqlOptions)
                .Create(mysql, designTime: false);
            var oracleKey = new OceanBaseModelCacheKeyFactory(
                    new ModelCacheKeyFactoryDependencies(),
                    oracleOptions)
                .Create(oracle, designTime: false);

            Assert.NotEqual(mysqlKey, oracleKey);
        }

        [Fact]
        public void Database_provider_identifies_the_registered_options()
        {
            var options = CreateOptions(OceanBaseCompatibilityMode.MySql);
            var provider = new DatabaseProvider<OceanBaseOptionsExtension>(new DatabaseProviderDependencies());

            Assert.Equal("OceanBase.EntityFrameworkCore", provider.Name);
            Assert.True(provider.IsConfigured(options));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, "ExpectedMode=MySql")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "ExpectedMode=Oracle")]
        public void Relational_connection_uses_connector_and_mode(
            OceanBaseCompatibilityMode mode,
            string expectedMode)
        {
            using var context = new TestContext(CreateOptions(mode));

            var connection = context.GetService<IRelationalConnection>().DbConnection;

            Assert.Equal("OceanBaseConnector.OceanBaseConnection", connection.GetType().FullName);
            Assert.Contains(expectedMode, connection.ConnectionString);
        }

        [Fact]
        public void Relational_connection_rejects_a_conflicting_connector_mode()
        {
            using var context = new TestContext(
                new DbContextOptionsBuilder<TestContext>()
                    .UseOceanBase(
                        "Server=localhost;Database=test;ExpectedMode=Oracle",
                        OceanBaseCompatibilityMode.MySql)
                    .Options);

            var exception = Assert.Throws<InvalidOperationException>(
                () => context.GetService<IRelationalConnection>().DbConnection);

            Assert.Contains("does not match", exception.Message);
        }

        private static DbContextOptions<TestContext> CreateOptions(OceanBaseCompatibilityMode mode)
            => new DbContextOptionsBuilder<TestContext>()
                .UseOceanBase("Server=localhost;Database=test", mode)
                .Options;

        private sealed class TestContext : DbContext
        {
            public TestContext(DbContextOptions<TestContext> options)
                : base(options)
            {
            }
        }

        private sealed class StubDbConnection : DbConnection
        {
            [AllowNull]
            public override string ConnectionString { get; set; } = string.Empty;
            public override string Database => string.Empty;
            public override string DataSource => string.Empty;
            public override string ServerVersion => string.Empty;
            public override System.Data.ConnectionState State => System.Data.ConnectionState.Closed;

            public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
            public override void Close()
            {
            }

            public override void Open() => throw new NotSupportedException();
            protected override DbTransaction BeginDbTransaction(System.Data.IsolationLevel isolationLevel)
                => throw new NotSupportedException();
            protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
        }
    }
}
