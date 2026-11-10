using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBase.EntityFrameworkCore.Internal;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests
{
    public sealed class OptionsTests
    {
        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void UseOceanBase_with_connection_string_preserves_mode_and_connection_string(
            OceanBaseCompatibilityMode mode, bool typed)
        {
            var builder = new DbContextOptionsBuilder<TestContext>();
            var result = typed ? builder.UseOceanBase("Server=localhost;Database=test", mode)
                : ((DbContextOptionsBuilder)builder).UseOceanBase("Server=localhost;Database=test", mode);
            var extension = builder.Options.FindExtension<OceanBaseOptionsExtension>();

            Assert.Same(builder, result);
            Assert.NotNull(extension);
            Assert.Equal(mode, extension!.CompatibilityMode);
            Assert.Equal("Server=localhost;Database=test", extension.ConnectionString);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void UseOceanBase_with_existing_connection_preserves_connection_and_mode(
            OceanBaseCompatibilityMode mode, bool typed)
        {
            using var connection = new StubDbConnection();
            var builder = new DbContextOptionsBuilder<TestContext>();
            var result = typed ? builder.UseOceanBase(connection, mode)
                : ((DbContextOptionsBuilder)builder).UseOceanBase(connection, mode);
            var extension = builder.Options.FindExtension<OceanBaseOptionsExtension>();

            Assert.Same(builder, result);
            Assert.NotNull(extension);
            Assert.Same(connection, extension!.Connection);
            Assert.False(extension.IsConnectionOwned);
            Assert.Equal(mode, extension.CompatibilityMode);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void Null_builders_reject_for_each_overload(bool typed, bool suppliedConnection)
        {
            DbContextOptionsBuilder builder = null!;
            DbContextOptionsBuilder<TestContext> typedBuilder = null!;
            using var connection = new StubDbConnection();
            var exception = Assert.Throws<ArgumentNullException>(() => typed
                ? suppliedConnection ? typedBuilder.UseOceanBase(connection, OceanBaseCompatibilityMode.MySql)
                    : typedBuilder.UseOceanBase("Server=localhost", OceanBaseCompatibilityMode.MySql)
                : suppliedConnection ? builder.UseOceanBase(connection, OceanBaseCompatibilityMode.MySql)
                    : builder.UseOceanBase("Server=localhost", OceanBaseCompatibilityMode.MySql));
            Assert.Equal("optionsBuilder", exception.ParamName);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public void Null_connections_reject_for_each_overload(OceanBaseCompatibilityMode mode, bool typed)
        {
            var builder = new DbContextOptionsBuilder<TestContext>();
            var exception = Assert.Throws<ArgumentNullException>(() => typed
                ? builder.UseOceanBase((DbConnection)null!, mode)
                : ((DbContextOptionsBuilder)builder).UseOceanBase((DbConnection)null!, mode));
            Assert.Equal("connection", exception.ParamName);
            Assert.Null(builder.Options.FindExtension<OceanBaseOptionsExtension>());
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void Invalid_modes_reject_without_changing_existing_options(bool typed, bool suppliedConnection)
        {
            var builder = new DbContextOptionsBuilder<TestContext>().UseOceanBase("Server=localhost", OceanBaseCompatibilityMode.Oracle);
            var previous = builder.Options.FindExtension<OceanBaseOptionsExtension>();
            using var connection = new StubDbConnection();
            foreach (var mode in new[] { (OceanBaseCompatibilityMode)(-1), (OceanBaseCompatibilityMode)99 })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => typed
                    ? suppliedConnection ? builder.UseOceanBase(connection, mode) : builder.UseOceanBase("Server=other", mode)
                    : suppliedConnection ? ((DbContextOptionsBuilder)builder).UseOceanBase(connection, mode)
                        : ((DbContextOptionsBuilder)builder).UseOceanBase("Server=other", mode));
                Assert.Same(previous, builder.Options.FindExtension<OceanBaseOptionsExtension>());
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Null_connection_string_allows_model_configuration_and_later_assignment(
            OceanBaseCompatibilityMode mode, bool async)
        {
            using var context = new TestContext(new DbContextOptionsBuilder<TestContext>().UseOceanBase((string?)null, mode).Options);
            Assert.True(context.Database.IsOceanBase());
            Assert.NotNull(context.Model);
            var relational = context.GetService<IRelationalConnection>();
            Assert.Null(relational.ConnectionString);
            ArgumentException exception;
            if (async)
            {
                exception = await Assert.ThrowsAsync<ArgumentException>(() => relational.OpenAsync(CancellationToken.None));
            }
            else
            {
                exception = Assert.Throws<ArgumentException>(() => relational.Open());
            }

            Assert.Contains("Server", exception.Message);
            relational.ConnectionString = "Server=localhost;Database=later";
            var connection = relational.DbConnection;
            Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
            Assert.Equal("later", connection.Database);
            Assert.Equal(mode, context.GetService<IDbContextOptions>().FindExtension<OceanBaseOptionsExtension>()!.CompatibilityMode);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, "not-a-connection-string")]
        [InlineData(OceanBaseCompatibilityMode.MySql, "UnsupportedOceanBaseSetting=value")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "not-a-connection-string")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "UnsupportedOceanBaseSetting=value")]
        public void Invalid_connection_strings_reject_before_allocating_a_connection(OceanBaseCompatibilityMode mode, string connectionString)
        {
            using var context = new TestContext(new DbContextOptionsBuilder<TestContext>().UseOceanBase(connectionString, mode).Options);
            Assert.ThrowsAny<ArgumentException>(() => context.GetService<IRelationalConnection>().DbConnection);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Compatible_contexts_reuse_models_and_singleton_services_but_not_connections(OceanBaseCompatibilityMode mode)
        {
            using var first = CreateCacheContext(mode, "first");
            using var second = CreateCacheContext(mode, "second");
            Assert.Same(first.Model, second.Model);
            Assert.Same(first.GetService<IRelationalTypeMappingSource>(), second.GetService<IRelationalTypeMappingSource>());
            Assert.Same(first.GetService<ISqlGenerationHelper>(), second.GetService<ISqlGenerationHelper>());
            Assert.Same(first.GetService<IModelCacheKeyFactory>(), second.GetService<IModelCacheKeyFactory>());
            Assert.NotSame(first.GetService<IRelationalConnection>(), second.GetService<IRelationalConnection>());
            Assert.Equal("first", first.GetService<IRelationalConnection>().DbConnection.Database);
            Assert.Equal("second", second.GetService<IRelationalConnection>().DbConnection.Database);
            var factory = first.GetService<IModelCacheKeyFactory>();
            Assert.Equal(factory.Create(first, false), factory.Create(second, false));
            Assert.NotEqual(factory.Create(first, false), factory.Create(first, true));
            Assert.Same(first.GetService<IDesignTimeModel>().Model, second.GetService<IDesignTimeModel>().Model);
            Assert.NotSame(first.Model, first.GetService<IDesignTimeModel>().Model);
        }

        [Fact]
        public void Actual_model_and_singleton_service_caches_never_cross_modes()
        {
            using var mysql = CreateCacheContext(OceanBaseCompatibilityMode.MySql, "mysql");
            using var oracle = CreateCacheContext(OceanBaseCompatibilityMode.Oracle, "oracle");
            Assert.NotSame(mysql.Model, oracle.Model);
            Assert.NotSame(mysql.GetService<IRelationalTypeMappingSource>(), oracle.GetService<IRelationalTypeMappingSource>());
            Assert.NotSame(mysql.GetService<ISqlGenerationHelper>(), oracle.GetService<ISqlGenerationHelper>());
            Assert.NotEqual(mysql.GetService<IModelCacheKeyFactory>().Create(mysql, false),
                oracle.GetService<IModelCacheKeyFactory>().Create(oracle, false));
            Assert.Equal("INT", mysql.Model.FindEntityType(typeof(CacheRow))!.FindProperty(nameof(CacheRow.Id))!.GetColumnType());
            Assert.Equal("NUMBER(10)", oracle.Model.FindEntityType(typeof(CacheRow))!.FindProperty(nameof(CacheRow.Id))!.GetColumnType());
            Assert.Equal("`row`", mysql.GetService<ISqlGenerationHelper>().DelimitIdentifier("row"));
            Assert.Equal("\"row\"", oracle.GetService<ISqlGenerationHelper>().DelimitIdentifier("row"));
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Provider_option_debug_information_does_not_expose_connection_details(OceanBaseCompatibilityMode mode)
        {
            var options = new DbContextOptionsBuilder<TestContext>()
                .UseOceanBase("Server=private-host;User ID=private-user;Password=private-password;Database=private-db", mode).Options;
            var info = options.FindExtension<OceanBaseOptionsExtension>()!.Info;
            var debug = new Dictionary<string, string>();
            info.PopulateDebugInfo(debug);
            Assert.Equal($"CompatibilityMode={mode} ", info.LogFragment);
            Assert.Equal(mode.ToString(), Assert.Single(debug).Value);
            Assert.DoesNotContain("private-", info.LogFragment);
        }

        [Fact]
        public void Null_database_facade_rejects_provider_detection()
            => Assert.Equal("databaseFacade", Assert.Throws<ArgumentNullException>(() =>
                OceanBaseDatabaseFacadeExtensions.IsOceanBase(null!)).ParamName);

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

        private static CacheContext CreateCacheContext(OceanBaseCompatibilityMode mode, string database)
            => new(new DbContextOptionsBuilder<CacheContext>().UseOceanBase($"Server=localhost;Database={database}", mode).Options);

        private sealed class CacheContext(DbContextOptions<CacheContext> options) : DbContext(options)
        {
            protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<CacheRow>();
        }

        private sealed class CacheRow
        {
            public int Id { get; set; }
        }

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
