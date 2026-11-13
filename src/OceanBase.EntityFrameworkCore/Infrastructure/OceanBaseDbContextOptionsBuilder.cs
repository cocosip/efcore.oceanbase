using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OceanBase.EntityFrameworkCore.Internal;
using OceanBase.EntityFrameworkCore.Storage;

namespace OceanBase.EntityFrameworkCore.Infrastructure
{
    /// <summary>Configures OceanBase relational behavior through the UseOceanBase callback.</summary>
    public sealed class OceanBaseDbContextOptionsBuilder
    {
        private readonly DbContextOptionsBuilder _optionsBuilder;

        internal OceanBaseDbContextOptionsBuilder(DbContextOptionsBuilder optionsBuilder) => _optionsBuilder = optionsBuilder;

        private OceanBaseOptionsExtension Options => _optionsBuilder.Options.FindExtension<OceanBaseOptionsExtension>()!;

        /// <summary>Enables operation-level retries for confirmed transient server errors.</summary>
        /// <remarks>Unknown outcomes, cancellation and XA never retry. Schema/migration execution rejects retries.</remarks>
        public OceanBaseDbContextOptionsBuilder EnableRetryOnFailure(int maxRetryCount = 6,
            TimeSpan? maxRetryDelay = null, IEnumerable<int>? additionalErrorCodes = null)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(maxRetryCount);
            if (maxRetryDelay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maxRetryDelay));
            var codes = additionalErrorCodes?.ToArray();
            if (codes?.Contains(0) == true) throw new ArgumentException("Code zero identifies client-local errors and cannot be retried.", nameof(additionalErrorCodes));
            Update(Options.WithExecutionStrategyFactory(dependencies =>
                new OceanBaseRetryingExecutionStrategy(dependencies, maxRetryCount, maxRetryDelay, codes)));
            return this;
        }

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
