using System;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Storage.Internal;

namespace OceanBase.EntityFrameworkCore.Design
{
    // EF10's named design connection resolver is internal; validate both supported patch baselines.
#pragma warning disable EF1001
    internal sealed class OceanBaseProviderCodeGenerator(ProviderCodeGeneratorDependencies dependencies,
        INamedConnectionStringResolver connectionStrings) : ProviderCodeGenerator(dependencies)
    {
        private static readonly MethodInfo UseProvider = typeof(OceanBaseDbContextOptionsBuilderExtensions).GetMethod(
            nameof(OceanBaseDbContextOptionsBuilderExtensions.UseOceanBase),
            [typeof(DbContextOptionsBuilder), typeof(string), typeof(OceanBaseCompatibilityMode)])!;
        private static readonly MethodInfo UseProviderOptions = typeof(OceanBaseDbContextOptionsBuilderExtensions).GetMethod(
            nameof(OceanBaseDbContextOptionsBuilderExtensions.UseOceanBase),
            [typeof(DbContextOptionsBuilder), typeof(string), typeof(OceanBaseCompatibilityMode),
                typeof(Action<Infrastructure.OceanBaseDbContextOptionsBuilder>)])!;

        public override MethodCallCodeFragment GenerateUseProvider(string connectionString, MethodCallCodeFragment? providerOptions)
        {
            var mode = OceanBaseScaffoldingTypes.Mode(connectionStrings.ResolveConnectionString(connectionString));
            return providerOptions is null ? new MethodCallCodeFragment(UseProvider, connectionString, mode)
                : new MethodCallCodeFragment(UseProviderOptions, connectionString, mode,
                    new NestedClosureCodeFragment("oceanbase", providerOptions));
        }
    }
#pragma warning restore EF1001
}
