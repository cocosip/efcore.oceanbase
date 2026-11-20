using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace OceanBase.EntityFrameworkCore.Query
{
    internal sealed class OceanBaseParameterBasedSqlProcessorFactory(
        RelationalParameterBasedSqlProcessorDependencies dependencies,
        IDbContextOptions options,
        OceanBaseCompatibilityMode mode) : IRelationalParameterBasedSqlProcessorFactory
    {
        public RelationalParameterBasedSqlProcessor Create(RelationalParameterBasedSqlProcessorParameters parameters)
            => new Processor(dependencies, parameters, mode,
#pragma warning disable EF1001 // EF9 stores collection policy on the relational options extension.
                RelationalOptionsExtension.Extract(options).ParameterizedCollectionTranslationMode
                    == ParameterizedCollectionTranslationMode.Constantize);
#pragma warning restore EF1001

        private sealed class Processor(
            RelationalParameterBasedSqlProcessorDependencies dependencies,
            RelationalParameterBasedSqlProcessorParameters processorParameters,
            OceanBaseCompatibilityMode mode, bool constantizeCollections)
            : RelationalParameterBasedSqlProcessor(dependencies, processorParameters)
        {
            public override Expression Optimize(Expression queryExpression,
                IReadOnlyDictionary<string, object?> parameters, out bool canCache)
            {
                var valueDependent = false;
                void DisableCaching() => valueDependent = true;

                var tags = queryExpression switch { UpdateExpression update => update.Tags, DeleteExpression delete => delete.Tags, _ => null };
                queryExpression = new OceanBaseBulkDmlVisitor(mode, parameters, DisableCaching, Dependencies.SqlExpressionFactory).Visit(queryExpression);
                var collections = new OceanBaseCollectionParameterExpandingVisitor(
                    Dependencies.SqlExpressionFactory, parameters, DisableCaching,
                    Dependencies.TypeMappingSource, mode, Parameters.ParametersToConstantize, constantizeCollections);
                queryExpression = collections.Visit(queryExpression);
                parameters = collections.ParameterValues;
                queryExpression = new OceanBaseRoundParameterVisitor(
                    Dependencies.SqlExpressionFactory, Dependencies.TypeMappingSource, parameters, DisableCaching, mode)
                    .Visit(queryExpression);
                if (mode == OceanBaseCompatibilityMode.Oracle)
                {
                    queryExpression = new OceanBaseOracleStringVisitor(Dependencies.SqlExpressionFactory, parameters, DisableCaching)
                        .Visit(queryExpression);
                }

                var result = base.Optimize(queryExpression, parameters, out canCache);
                canCache &= !valueDependent;
                result = mode == OceanBaseCompatibilityMode.Oracle
                    ? new OceanBaseSearchConditionVisitor(Dependencies.SqlExpressionFactory,
                        Dependencies.TypeMappingSource.FindMapping(typeof(bool))!).Visit(result)
                    : result;
                result = collections.AttachBindings(result);
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
