using System;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using OceanBase.EntityFrameworkCore;
using OceanBase.EntityFrameworkCore.Internal;

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
