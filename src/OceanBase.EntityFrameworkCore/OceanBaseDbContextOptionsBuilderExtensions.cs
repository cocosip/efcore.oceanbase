using System;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using OceanBase.EntityFrameworkCore;
using OceanBase.EntityFrameworkCore.Internal;
using OceanBaseConnection = OceanBaseConnector.OceanBaseConnection;

namespace Microsoft.EntityFrameworkCore
{
    /// <summary>Provides EF Core options extensions for the OceanBase provider.</summary>
    public static class OceanBaseDbContextOptionsBuilderExtensions
    {
        /// <summary>Configures an OceanBase connection string and compatibility mode.</summary>
        public static DbContextOptionsBuilder UseOceanBase(
            this DbContextOptionsBuilder optionsBuilder,
            string? connectionString,
            OceanBaseCompatibilityMode compatibilityMode)
        {
            ArgumentNullException.ThrowIfNull(optionsBuilder);

            var extension = GetOrCreateExtension(optionsBuilder)
                .WithConnectionString(connectionString)
                .WithCompatibilityMode(compatibilityMode);

            ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);
            ConfigureWarnings(optionsBuilder);
            return optionsBuilder;
        }

        /// <summary>Configures a typed builder with an OceanBase connection string and compatibility mode.</summary>
        public static DbContextOptionsBuilder<TContext> UseOceanBase<TContext>(
            this DbContextOptionsBuilder<TContext> optionsBuilder,
            string? connectionString,
            OceanBaseCompatibilityMode compatibilityMode)
            where TContext : DbContext
            => (DbContextOptionsBuilder<TContext>)(object)((DbContextOptionsBuilder)optionsBuilder)
                .UseOceanBase(connectionString, compatibilityMode);

        /// <summary>Configures an existing OceanBase connection and compatibility mode.</summary>
        public static DbContextOptionsBuilder UseOceanBase(
            this DbContextOptionsBuilder optionsBuilder,
            DbConnection connection,
            OceanBaseCompatibilityMode compatibilityMode)
        {
            ArgumentNullException.ThrowIfNull(optionsBuilder);
            ArgumentNullException.ThrowIfNull(connection);

            var extension = GetOrCreateExtension(optionsBuilder)
                .WithConnection(connection)
                .WithCompatibilityMode(compatibilityMode);

            ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);
            ConfigureWarnings(optionsBuilder);
            return optionsBuilder;
        }

        /// <summary>Configures a typed builder with an existing OceanBase connection and compatibility mode.</summary>
        public static DbContextOptionsBuilder<TContext> UseOceanBase<TContext>(
            this DbContextOptionsBuilder<TContext> optionsBuilder,
            DbConnection connection,
            OceanBaseCompatibilityMode compatibilityMode)
            where TContext : DbContext
            => (DbContextOptionsBuilder<TContext>)(object)((DbContextOptionsBuilder)optionsBuilder)
                .UseOceanBase(connection, compatibilityMode);

        private static OceanBaseOptionsExtension GetOrCreateExtension(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.Options.FindExtension<OceanBaseOptionsExtension>()
                is { } extension
                    ? new OceanBaseOptionsExtension(extension)
                    : new OceanBaseOptionsExtension();

        /// <summary>
        /// Enables table creation and migrations in the named, already provisioned MySQL database or Oracle schema.
        /// The current schema must match exactly. This does not enable database/user creation or deletion.
        /// </summary>
        public static DbContextOptionsBuilder EnableOceanBaseSchemaManagement(
            this DbContextOptionsBuilder optionsBuilder, string databaseOrSchema)
        {
            ArgumentNullException.ThrowIfNull(optionsBuilder);
            var extension = GetOrCreateExtension(optionsBuilder).WithSchemaManagementScope(databaseOrSchema);
            ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);
            return optionsBuilder;
        }

        /// <summary>Enables table creation and migrations within an explicitly named existing database or schema.</summary>
        public static DbContextOptionsBuilder<TContext> EnableOceanBaseSchemaManagement<TContext>(
            this DbContextOptionsBuilder<TContext> optionsBuilder, string databaseOrSchema)
            where TContext : DbContext
            => (DbContextOptionsBuilder<TContext>)(object)((DbContextOptionsBuilder)optionsBuilder)
                .EnableOceanBaseSchemaManagement(databaseOrSchema);

        /// <summary>
        /// Enables creation and deletion of the entire named MySQL database, including non-model objects.
        /// Requires a closed target connection for that exact database. UseOceanBaseManagementConnection
        /// supplies explicit administration credentials/routing; without it, only an unchanged provider-created
        /// string-configured connection without native hooks is supported. Oracle uses EnableOceanBaseUserManagement.
        /// </summary>
        public static DbContextOptionsBuilder EnableOceanBaseDatabaseManagement(
            this DbContextOptionsBuilder optionsBuilder, string databaseName)
        {
            ArgumentNullException.ThrowIfNull(optionsBuilder);
            var extension = GetOrCreateExtension(optionsBuilder).WithDatabaseManagementScope(databaseName);
            ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);
            return optionsBuilder;
        }

        /// <summary>Declares ownership of an entire MySQL database for creation and deletion.</summary>
        public static DbContextOptionsBuilder<TContext> EnableOceanBaseDatabaseManagement<TContext>(
            this DbContextOptionsBuilder<TContext> optionsBuilder, string databaseName)
            where TContext : DbContext
            => (DbContextOptionsBuilder<TContext>)(object)((DbContextOptionsBuilder)optionsBuilder)
                .EnableOceanBaseDatabaseManagement(databaseName);

        /// <summary>
        /// Owns the entire named Oracle user/schema, including non-model objects; deletion uses CASCADE.
        /// The owned user name must not contain tenant routing (@). Requires an independent
        /// UseOceanBaseManagementConnection and a closed target with the exact user login.
        /// The password factory is invoked only when creating a user. Empty passwords, double quotes and control
        /// characters reject before DDL. EF command logs redact the password even with sensitive logging enabled;
        /// trusted application interceptors can inspect executable SQL and must log LogCommandText instead.
        /// Creation grants CREATE SESSION and CREATE TABLE. DDL failures can require manual cleanup.
        /// </summary>
        public static DbContextOptionsBuilder EnableOceanBaseUserManagement(
            this DbContextOptionsBuilder optionsBuilder, string userName, Func<string> passwordFactory)
        {
            ArgumentNullException.ThrowIfNull(optionsBuilder);
            var extension = GetOrCreateExtension(optionsBuilder).WithUserManagementScope(userName, passwordFactory);
            ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);
            return optionsBuilder;
        }

        /// <summary>Declares ownership of an entire Oracle user/schema for creation and CASCADE deletion.</summary>
        public static DbContextOptionsBuilder<TContext> EnableOceanBaseUserManagement<TContext>(
            this DbContextOptionsBuilder<TContext> optionsBuilder, string userName, Func<string> passwordFactory)
            where TContext : DbContext
            => (DbContextOptionsBuilder<TContext>)(object)((DbContextOptionsBuilder)optionsBuilder)
                .EnableOceanBaseUserManagement(userName, passwordFactory);

        /// <summary>
        /// Supplies a fresh, closed management connection for each explicitly enabled namespace operation.
        /// The provider owns and disposes it. The factory must select the same tenant as the target, with
        /// appropriate privileges, routing and TLS policy; MySQL must have no selected database.
        /// This does not enable namespace creation/deletion and is not used by ordinary queries or table creation.
        /// </summary>
        public static DbContextOptionsBuilder UseOceanBaseManagementConnection(
            this DbContextOptionsBuilder optionsBuilder, Func<OceanBaseConnection> connectionFactory)
        {
            ArgumentNullException.ThrowIfNull(optionsBuilder);
            var extension = GetOrCreateExtension(optionsBuilder).WithManagementConnectionFactory(connectionFactory);
            ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);
            return optionsBuilder;
        }

        /// <summary>Supplies a provider-owned, independent connection for explicit namespace management.</summary>
        public static DbContextOptionsBuilder<TContext> UseOceanBaseManagementConnection<TContext>(
            this DbContextOptionsBuilder<TContext> optionsBuilder, Func<OceanBaseConnection> connectionFactory)
            where TContext : DbContext
            => (DbContextOptionsBuilder<TContext>)(object)((DbContextOptionsBuilder)optionsBuilder)
                .UseOceanBaseManagementConnection(connectionFactory);

        private static void ConfigureWarnings(DbContextOptionsBuilder optionsBuilder)
        {
            var coreOptions = optionsBuilder.Options.FindExtension<CoreOptionsExtension>()
                ?? new CoreOptionsExtension();
            coreOptions = RelationalOptionsExtension.WithDefaultWarningConfiguration(coreOptions);
            ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(coreOptions);
        }
    }

    /// <summary>Provides OceanBase checks for EF Core database facades.</summary>
    public static class OceanBaseDatabaseFacadeExtensions
    {
        /// <summary>Returns whether the facade is configured with the OceanBase provider.</summary>
        public static bool IsOceanBase(this Infrastructure.DatabaseFacade databaseFacade)
        {
            ArgumentNullException.ThrowIfNull(databaseFacade);
            var serviceProvider = ((IInfrastructure<IServiceProvider>)databaseFacade).Instance;
            return serviceProvider.GetRequiredService<IDbContextOptions>()
                .FindExtension<OceanBaseOptionsExtension>() is not null;
        }
    }
}
