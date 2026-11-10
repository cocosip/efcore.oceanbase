using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;

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
                if (mode == OceanBaseCompatibilityMode.Oracle)
                {
                    queryExpression = new OceanBaseOracleStringVisitor(Dependencies.SqlExpressionFactory, parametersDecorator)
                        .Visit(queryExpression);
                }

                var result = base.Process(queryExpression, parametersDecorator);
                return mode == OceanBaseCompatibilityMode.Oracle
                    ? new OceanBaseSearchConditionVisitor(Dependencies.SqlExpressionFactory).Visit(result)
                    : result;
            }
        }
    }
}
