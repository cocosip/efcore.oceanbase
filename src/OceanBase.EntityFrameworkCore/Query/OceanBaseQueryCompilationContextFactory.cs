using Microsoft.EntityFrameworkCore.Query;

namespace OceanBase.EntityFrameworkCore.Query;

internal sealed class OceanBaseQueryCompilationContextFactory(
    QueryCompilationContextDependencies dependencies, RelationalQueryCompilationContextDependencies relationalDependencies)
    : IQueryCompilationContextFactory
{
    public QueryCompilationContext Create(bool async) => new Context(dependencies, relationalDependencies, async);

#pragma warning disable EF9100 // Preserve EF10's experimental precompilation entry point when replacing its factory.
    public QueryCompilationContext CreatePrecompiled(bool async) => new Context(dependencies, relationalDependencies, async, precompiling: true);

    private sealed class Context(QueryCompilationContextDependencies dependencies,
        RelationalQueryCompilationContextDependencies relationalDependencies, bool async, bool precompiling = false)
        : RelationalQueryCompilationContext(dependencies, relationalDependencies, async, precompiling)
    {
        // OceanBase has one active reader per connection; consume each result before opening the next.
        public override bool IsBuffering => base.IsBuffering
            || QuerySplittingBehavior == Microsoft.EntityFrameworkCore.QuerySplittingBehavior.SplitQuery;
    }
#pragma warning restore EF9100
}
