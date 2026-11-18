using System;
using System.Collections.Generic;
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
            protected override Expression ExpandFromSqlParameter(Expression queryExpression, ParametersCacheDecorator parametersDecorator)
            {
                var expanded = base.ExpandFromSqlParameter(queryExpression, parametersDecorator);
                return mode == OceanBaseCompatibilityMode.Oracle ? new ParameterNameVisitor().Visit(expanded) : expanded;
            }

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

        private sealed class ParameterNameVisitor : ExpressionVisitor
        {
            private readonly Dictionary<string, List<SqlParameterExpression>> _parameters = new();

            protected override Expression VisitExtension(Expression node)
            {
                if (node is not SqlParameterExpression parameter || parameter.InvariantName.Length == 0
                    || !char.IsDigit(parameter.InvariantName[0]) && !parameter.InvariantName.StartsWith("_ob_", StringComparison.OrdinalIgnoreCase))
                    return base.VisitExtension(node);
                if (!_parameters.TryGetValue(parameter.InvariantName, out var existing))
                {
                    existing = [];
                    _parameters.Add(parameter.InvariantName, existing);
                }
                foreach (var candidate in existing)
                {
                    if (string.Equals(candidate.TypeMapping?.StoreType, parameter.TypeMapping?.StoreType, StringComparison.OrdinalIgnoreCase)
                        && Equals(candidate.TypeMapping?.Converter, parameter.TypeMapping?.Converter))
                    {
                        // EF10's repeated-parameter path can restore the name from before uniquification.
                        return candidate.Name == parameter.Name ? parameter : new SqlParameterExpression(
                            parameter.InvariantName, candidate.Name, parameter.Type, parameter.IsNullable,
                            parameter.TranslationMode, parameter.TypeMapping);
                    }
                }
                existing.Add(parameter);
                return parameter;
            }
        }
    }
}
