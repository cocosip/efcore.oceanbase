using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;

namespace OceanBase.EntityFrameworkCore.Query
{
    internal sealed class OceanBaseShapedQueryCompilingExpressionVisitorFactory(
        ShapedQueryCompilingExpressionVisitorDependencies dependencies,
        RelationalShapedQueryCompilingExpressionVisitorDependencies relationalDependencies,
        QuerySqlGeneratorDependencies sqlDependencies,
        RelationalCommandBuilderDependencies commandDependencies,
        OceanBaseCompatibilityMode mode) : IShapedQueryCompilingExpressionVisitorFactory
    {
        public ShapedQueryCompilingExpressionVisitor Create(QueryCompilationContext queryCompilationContext)
        {
            var effectiveDependencies = relationalDependencies;
#pragma warning disable EF9100 // Preserve EF10 precompilation; NativeAOT publication remains a separate gate.
            if (queryCompilationContext.IsPrecompiling && mode == OceanBaseCompatibilityMode.MySql)
#pragma warning restore EF9100
            {
                // EF serializes only mapped parameters; the rented command restores positional binding at runtime.
                effectiveDependencies = relationalDependencies with
                {
                    QuerySqlGeneratorFactory = new OceanBaseQuerySqlGeneratorFactory(sqlDependencies with
                    {
                        RelationalCommandBuilderFactory = new RelationalCommandBuilderFactory(commandDependencies)
                    }, mode)
                };
            }

            return new RelationalShapedQueryCompilingExpressionVisitor(dependencies, effectiveDependencies, queryCompilationContext);
        }
    }
}
