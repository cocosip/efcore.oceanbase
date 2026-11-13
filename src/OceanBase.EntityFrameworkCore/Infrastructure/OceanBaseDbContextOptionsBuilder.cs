using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OceanBase.EntityFrameworkCore.Internal;

namespace OceanBase.EntityFrameworkCore.Infrastructure
{
    /// <summary>Configures OceanBase migrations through the UseOceanBase callback.</summary>
    public sealed class OceanBaseDbContextOptionsBuilder
    {
        private readonly DbContextOptionsBuilder _optionsBuilder;

        internal OceanBaseDbContextOptionsBuilder(DbContextOptionsBuilder optionsBuilder) => _optionsBuilder = optionsBuilder;

        private OceanBaseOptionsExtension Options => _optionsBuilder.Options.FindExtension<OceanBaseOptionsExtension>()!;

        /// <summary>Selects the assembly containing migrations; null restores the context assembly default.</summary>
        public OceanBaseDbContextOptionsBuilder MigrationsAssembly(string? assemblyName)
        {
            if (assemblyName is not null) ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
            Update(Options.WithMigrationsAssembly(assemblyName));
            return this;
        }

        /// <summary>Selects the migrations history table within the opted-in database or schema.</summary>
        public OceanBaseDbContextOptionsBuilder MigrationsHistoryTable(string tableName, string? schema = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
            if (schema is not null) ArgumentException.ThrowIfNullOrWhiteSpace(schema);
            if (tableName.Contains('\0') || schema?.Contains('\0') == true)
                throw new ArgumentException("Migration history identifiers cannot contain NUL.");
            Update(Options.WithMigrationsHistoryTableName(tableName).WithMigrationsHistoryTableSchema(schema));
            return this;
        }

        private void Update(RelationalOptionsExtension extension)
            => ((IDbContextOptionsBuilderInfrastructure)_optionsBuilder).AddOrUpdateExtension((OceanBaseOptionsExtension)extension);
    }
}
