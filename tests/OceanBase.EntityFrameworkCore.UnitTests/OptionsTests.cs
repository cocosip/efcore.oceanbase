using System;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
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

        [Fact]
        public void Caller_owned_connection_rejects_a_conflicting_connector_mode()
        {
            using var connection = new OceanBaseConnector.OceanBaseConnection("Server=localhost;ExpectedMode=Oracle");
            using var context = new TestContext(new DbContextOptionsBuilder<TestContext>()
                .UseOceanBase(connection, OceanBaseCompatibilityMode.MySql).Options);

            Assert.Throws<InvalidOperationException>(() => context.GetService<IRelationalConnection>().DbConnection);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Caller_owned_connection_receives_the_expected_mode(OceanBaseCompatibilityMode mode)
        {
            using var connection = new OceanBaseConnector.OceanBaseConnection("Server=localhost");
            using var context = new TestContext(new DbContextOptionsBuilder<TestContext>()
                .UseOceanBase(connection, mode).Options);

            Assert.Same(connection, context.GetService<IRelationalConnection>().DbConnection);
            Assert.Equal(mode.ToString(), new OceanBaseConnector.OceanBaseConnectionStringBuilder(connection.ConnectionString)
                .ExpectedMode.ToString());
        }

        [Fact]
        public void MySql_connection_rejects_changed_row_counts_before_execution()
        {
            var connectionString = new OceanBaseConnector.OceanBaseConnectionStringBuilder
            {
                Server = "localhost",
                UseAffectedRows = true
            }.ConnectionString;
            using var context = new TestContext(new DbContextOptionsBuilder<TestContext>()
                .UseOceanBase(connectionString, OceanBaseCompatibilityMode.MySql).Options);

            var exception = Assert.Throws<InvalidOperationException>(() => context.GetService<IRelationalConnection>().DbConnection);

            Assert.Contains("UseAffectedRows", exception.Message);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Mutated_connection_is_rejected_before_open_and_disposes_safely(
            OceanBaseCompatibilityMode mode, bool async)
        {
            var context = new TestContext(CreateOptions(mode));
            var relational = context.GetService<IRelationalConnection>();
            var connection = relational.DbConnection;
            var conflictingMode = mode == OceanBaseCompatibilityMode.MySql ? "Oracle" : "MySql";
            connection.ConnectionString = $"Server=localhost;ExpectedMode={conflictingMode}";

            if (async)
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => relational.OpenAsync(CancellationToken.None));
                await context.DisposeAsync();
            }
            else
            {
                Assert.Throws<InvalidOperationException>(() => relational.Open());
                context.Dispose();
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Invalid_replacement_does_not_replace_the_existing_connection(OceanBaseCompatibilityMode mode)
        {
            using var context = new TestContext(CreateOptions(mode));
            var relational = context.GetService<IRelationalConnection>();
            var original = relational.DbConnection;
            using var invalid = new OceanBaseConnector.OceanBaseConnection(
                $"Server=localhost;ExpectedMode={(mode == OceanBaseCompatibilityMode.MySql ? "Oracle" : "MySql")}");

            Assert.Throws<InvalidOperationException>(() => relational.SetDbConnection(invalid, contextOwnsConnection: false));
            Assert.Same(original, relational.DbConnection);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Context_disposes_only_owned_connector_connections(OceanBaseCompatibilityMode mode, bool async)
        {
            using var supplied = new OceanBaseConnector.OceanBaseConnection("Server=localhost");
            var callerContext = new TestContext(new DbContextOptionsBuilder<TestContext>()
                .UseOceanBase(supplied, mode).Options);
            Assert.Same(supplied, callerContext.GetService<IRelationalConnection>().DbConnection);
            var providerContext = new TestContext(CreateOptions(mode));
            var owned = providerContext.GetService<IRelationalConnection>().DbConnection;

            if (async)
            {
                await callerContext.DisposeAsync();
                await providerContext.DisposeAsync();
            }
            else
            {
                callerContext.Dispose();
                providerContext.Dispose();
            }

            supplied.ConnectionString = "Server=localhost;Database=still_usable";
            Assert.Throws<ObjectDisposedException>(() => owned.ConnectionString = "Server=localhost");
        }

        [Fact]
        public void Non_connector_connection_is_rejected_when_services_are_resolved()
        {
            using var supplied = new StubDbConnection();
            using var context = new TestContext(new DbContextOptionsBuilder<TestContext>()
                .UseOceanBase(supplied, OceanBaseCompatibilityMode.MySql).Options);

            Assert.Throws<InvalidOperationException>(() => context.GetService<IRelationalConnection>());
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
