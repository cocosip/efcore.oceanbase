using System;
using System.Collections.Generic;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using Microsoft.Extensions.DependencyInjection;
using OceanBase.EntityFrameworkCore.Storage;
using OceanBase.EntityFrameworkCore.Metadata.Conventions;
using OceanBase.EntityFrameworkCore.Query;
using OceanBase.EntityFrameworkCore.Update;

namespace OceanBase.EntityFrameworkCore.Internal
{
    internal sealed class OceanBaseOptionsExtension : RelationalOptionsExtension
    {
        private sealed class ExtensionInfo : RelationalExtensionInfo
        {
            private OceanBaseOptionsExtension OceanBaseExtension => (OceanBaseOptionsExtension)base.Extension;

            public ExtensionInfo(IDbContextOptionsExtension extension)
                : base(extension)
            {
            }

            public override string LogFragment => $"CompatibilityMode={OceanBaseExtension.CompatibilityMode} ";

            public override int GetServiceProviderHashCode()
                => OceanBaseExtension.CompatibilityMode.GetHashCode();

            public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
                => other is ExtensionInfo extensionInfo
                    && OceanBaseExtension.CompatibilityMode == extensionInfo.OceanBaseExtension.CompatibilityMode;

            public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
                => debugInfo["OceanBase.EntityFrameworkCore:CompatibilityMode"]
                    = OceanBaseExtension.CompatibilityMode.ToString();
        }

        private DbContextOptionsExtensionInfo? _info;

        public OceanBaseOptionsExtension()
        {
        }

        internal OceanBaseOptionsExtension(OceanBaseOptionsExtension copyFrom)
            : base(copyFrom)
        {
            CompatibilityMode = copyFrom.CompatibilityMode;
        }

        public OceanBaseCompatibilityMode CompatibilityMode { get; private set; }

        public override DbContextOptionsExtensionInfo Info
            => _info ??= new ExtensionInfo(this);

        public OceanBaseOptionsExtension WithCompatibilityMode(OceanBaseCompatibilityMode mode)
        {
            if (mode is not OceanBaseCompatibilityMode.MySql and not OceanBaseCompatibilityMode.Oracle)
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }

            var clone = new OceanBaseOptionsExtension(this)
            {
                CompatibilityMode = mode
            };
            return clone;
        }

        public override OceanBaseOptionsExtension WithConnectionString(string? connectionString)
            => new OceanBaseOptionsExtension((OceanBaseOptionsExtension)base.WithConnectionString(connectionString))
            {
                CompatibilityMode = CompatibilityMode
            };

        public override OceanBaseOptionsExtension WithConnection(DbConnection? connection)
            => new OceanBaseOptionsExtension((OceanBaseOptionsExtension)base.WithConnection(connection))
            {
                CompatibilityMode = CompatibilityMode
            };

        protected override RelationalOptionsExtension Clone()
            => new OceanBaseOptionsExtension(this);

        public override void ApplyServices(IServiceCollection services)
        {
            var builder = new EntityFrameworkRelationalServicesBuilder(services)
                .TryAdd<LoggingDefinitions, OceanBaseLoggingDefinitions>()
                .TryAdd<IDatabaseProvider, DatabaseProvider<OceanBaseOptionsExtension>>()
                .TryAdd<IProviderConventionSetBuilder, OceanBaseConventionSetBuilder>()
                .TryAdd<IModelRuntimeInitializer, RelationalModelRuntimeInitializer>()
                .TryAdd<IModelCustomizer, RelationalModelCustomizer>()
                .TryAdd<IModelValidator, RelationalModelValidator>()
                .TryAdd<ICompiledQueryCacheKeyGenerator, OceanBaseCompiledQueryCacheKeyGenerator>()
                .TryAdd<IQueryableMethodTranslatingExpressionVisitorFactory, OceanBaseQueryableMethodTranslatingExpressionVisitorFactory>()
                .TryAdd<IRelationalSqlTranslatingExpressionVisitorFactory, OceanBaseSqlTranslatingExpressionVisitorFactory>()
                .TryAdd<IQueryTranslationPostprocessorFactory, OceanBaseQueryTranslationPostprocessorFactory>()
                .TryAdd<IRelationalConnection, OceanBaseRelationalConnection>()
                .TryAdd<IRelationalDatabaseCreator, OceanBaseDatabaseCreator>()
                .TryAdd<IModificationCommandBatchFactory, OceanBaseModificationCommandBatchFactory>();

            builder.TryAdd<IRelationalCommandBuilderFactory>(serviceProvider =>
                new OceanBaseRelationalCommandBuilderFactory(
                    serviceProvider.GetRequiredService<RelationalCommandBuilderDependencies>(), CompatibilityMode));
            builder.TryAdd<ISqlGenerationHelper>(serviceProvider =>
                new OceanBaseSqlGenerationHelper(
                    serviceProvider.GetRequiredService<RelationalSqlGenerationHelperDependencies>(),
                    CompatibilityMode));
            builder.TryAdd<IMemberTranslatorProvider>(serviceProvider =>
                new OceanBaseMemberTranslatorProvider(
                    serviceProvider.GetRequiredService<RelationalMemberTranslatorProviderDependencies>(),
                    serviceProvider.GetRequiredService<IRelationalTypeMappingSource>(), CompatibilityMode));
            builder.TryAdd<IMethodCallTranslatorProvider>(serviceProvider =>
                new OceanBaseMethodCallTranslatorProvider(
                    serviceProvider.GetRequiredService<RelationalMethodCallTranslatorProviderDependencies>(), CompatibilityMode));
            builder.TryAdd<IAggregateMethodCallTranslatorProvider>(serviceProvider =>
                new OceanBaseAggregateMethodCallTranslatorProvider(
                    serviceProvider.GetRequiredService<RelationalAggregateMethodCallTranslatorProviderDependencies>(),
                    serviceProvider.GetRequiredService<IRelationalTypeMappingSource>()));
            builder.TryAdd<IUpdateSqlGenerator>(serviceProvider =>
                new OceanBaseUpdateSqlGenerator(
                    serviceProvider.GetRequiredService<UpdateSqlGeneratorDependencies>(), CompatibilityMode));
            builder.TryAdd<IQuerySqlGeneratorFactory>(serviceProvider =>
                new OceanBaseQuerySqlGeneratorFactory(
                    serviceProvider.GetRequiredService<QuerySqlGeneratorDependencies>(), CompatibilityMode));
            builder.TryAdd<IRelationalParameterBasedSqlProcessorFactory>(serviceProvider =>
                new OceanBaseParameterBasedSqlProcessorFactory(
                    serviceProvider.GetRequiredService<RelationalParameterBasedSqlProcessorDependencies>(), CompatibilityMode));
            builder.TryAdd<IRelationalTypeMappingSource>(serviceProvider =>
                new OceanBaseTypeMappingSource(
                    serviceProvider.GetRequiredService<TypeMappingSourceDependencies>(),
                    serviceProvider.GetRequiredService<RelationalTypeMappingSourceDependencies>(),
                    CompatibilityMode));
            builder.TryAdd<IModelCacheKeyFactory>(serviceProvider =>
                new OceanBaseModelCacheKeyFactory(
                    serviceProvider.GetRequiredService<ModelCacheKeyFactoryDependencies>(),
                    CompatibilityMode));

            builder.TryAddCoreServices();
        }
    }
}
