using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace OceanBase.EntityFrameworkCore.Query
{
    internal sealed class OceanBaseParameterBasedSqlProcessorFactory(
        RelationalParameterBasedSqlProcessorDependencies dependencies,
        OceanBaseCompatibilityMode mode) : IRelationalParameterBasedSqlProcessorFactory
    {
        public RelationalParameterBasedSqlProcessor Create(RelationalParameterBasedSqlProcessorParameters parameters)
            => new Processor(dependencies, parameters, mode);

        private sealed class Processor(
            RelationalParameterBasedSqlProcessorDependencies dependencies,
            RelationalParameterBasedSqlProcessorParameters parameters,
            OceanBaseCompatibilityMode mode) : RelationalParameterBasedSqlProcessor(dependencies, parameters)
        {
            public override Expression Process(Expression queryExpression, ParametersCacheDecorator parametersDecorator)
            {
                // EF10 bulk expression VisitChildren can recreate a statement without its tags.
                var tags = queryExpression switch { UpdateExpression update => update.Tags, DeleteExpression delete => delete.Tags, _ => null };
                queryExpression = new OceanBaseBulkDmlVisitor(mode, parametersDecorator, Dependencies.SqlExpressionFactory).Visit(queryExpression);
                queryExpression = new OceanBaseCollectionParameterExpandingVisitor(
                    Dependencies.SqlExpressionFactory, Parameters, parametersDecorator,
                    Dependencies.TypeMappingSource, mode).Visit(queryExpression);
                queryExpression = new OceanBaseRoundParameterVisitor(
                    Dependencies.SqlExpressionFactory, Dependencies.TypeMappingSource, parametersDecorator, mode)
                    .Visit(queryExpression);
                if (mode == OceanBaseCompatibilityMode.Oracle)
                {
                    queryExpression = new OceanBaseOracleStringVisitor(Dependencies.SqlExpressionFactory, parametersDecorator)
                        .Visit(queryExpression);
                }

                var result = base.Process(queryExpression, parametersDecorator);
                result = mode == OceanBaseCompatibilityMode.Oracle
                    ? new OceanBaseSearchConditionVisitor(Dependencies.SqlExpressionFactory,
                        Dependencies.TypeMappingSource.FindMapping(typeof(bool))!).Visit(result)
                    : result;
                return result switch
                {
                    UpdateExpression update when tags is not null => update.ApplyTags(tags),
                    DeleteExpression delete when tags is not null => delete.ApplyTags(tags),
                    _ => result
                };
            }
        }
    }
}
