using Microsoft.EntityFrameworkCore.Query;

namespace OceanBase.EntityFrameworkCore.Query;

internal sealed class OceanBaseQueryCompilationContextFactory(
    QueryCompilationContextDependencies dependencies, RelationalQueryCompilationContextDependencies relationalDependencies)
    : IQueryCompilationContextFactory
{
    public QueryCompilationContext Create(bool async) => new Context(dependencies, relationalDependencies, async);

    internal sealed class Context(QueryCompilationContextDependencies dependencies,
        RelationalQueryCompilationContextDependencies relationalDependencies, bool async)
        : RelationalQueryCompilationContext(dependencies, relationalDependencies, async)
    {
        public bool TranslatingUpdate { get; set; }

        // OceanBase has one active reader per connection; consume each result before opening the next.
        public override bool IsBuffering => base.IsBuffering
            || QuerySplittingBehavior == Microsoft.EntityFrameworkCore.QuerySplittingBehavior.SplitQuery;
    }
}
