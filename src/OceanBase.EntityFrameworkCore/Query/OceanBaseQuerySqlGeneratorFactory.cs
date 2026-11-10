using Microsoft.EntityFrameworkCore.Query;

namespace OceanBase.EntityFrameworkCore.Query
{
    internal sealed class OceanBaseQuerySqlGeneratorFactory : IQuerySqlGeneratorFactory
    {
        private readonly QuerySqlGeneratorDependencies _dependencies;
        private readonly OceanBaseCompatibilityMode _mode;

        public OceanBaseQuerySqlGeneratorFactory(QuerySqlGeneratorDependencies dependencies, OceanBaseCompatibilityMode mode)
        {
            _dependencies = dependencies;
            _mode = mode;
        }

        public QuerySqlGenerator Create() => new OceanBaseQuerySqlGenerator(_dependencies, _mode);
    }
}
