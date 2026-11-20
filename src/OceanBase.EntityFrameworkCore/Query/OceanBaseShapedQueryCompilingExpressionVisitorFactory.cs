#pragma warning disable EF1001 // EF8 non-query execution uses the relational command cache.
using System;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Microsoft.EntityFrameworkCore.Storage;

namespace OceanBase.EntityFrameworkCore.Query
{
    internal sealed class OceanBaseShapedQueryCompilingExpressionVisitorFactory(
        ShapedQueryCompilingExpressionVisitorDependencies dependencies,
        RelationalShapedQueryCompilingExpressionVisitorDependencies relationalDependencies)
        : IShapedQueryCompilingExpressionVisitorFactory
    {
        public ShapedQueryCompilingExpressionVisitor Create(QueryCompilationContext queryCompilationContext)
            => new Visitor(dependencies, relationalDependencies, queryCompilationContext);

        private sealed class Visitor(
            ShapedQueryCompilingExpressionVisitorDependencies dependencies,
            RelationalShapedQueryCompilingExpressionVisitorDependencies relationalDependencies,
            QueryCompilationContext queryCompilationContext)
            : RelationalShapedQueryCompilingExpressionVisitor(dependencies, relationalDependencies, queryCompilationContext)
        {
            protected override Expression VisitNonQuery(NonQueryExpression nonQueryExpression)
            {
                var command = base.VisitNonQuery(nonQueryExpression);
                if (!QueryCompilationContext.IsAsync) return command;

                // EF8's relational non-query executor omits QueryContext.CancellationToken.
                var arguments = ((MethodCallExpression)command).Arguments;
                return Expression.Call(((Func<RelationalQueryContext, RelationalCommandCache, Type,
                    CommandSource, bool, Task<int>>)ExecuteAsync).Method, arguments);
            }

            private static async Task<int> ExecuteAsync(RelationalQueryContext context,
                RelationalCommandCache commandCache, Type contextType, CommandSource commandSource, bool threadSafetyChecks)
            {
                try
                {
                    using var criticalSection = threadSafetyChecks ? context.ConcurrencyDetector.EnterCriticalSection() : default;
                    return await context.ExecutionStrategy.ExecuteAsync((context, commandCache, commandSource),
                        static async (_, state, cancellationToken) =>
                        {
                            EntityFrameworkEventSource.Log.QueryExecuting();
                            var command = state.commandCache.RentAndPopulateRelationalCommand(state.context);
                            try
                            {
                                return await command.ExecuteNonQueryAsync(new RelationalCommandParameterObject(
                                    state.context.Connection, state.context.ParameterValues, null, state.context.Context,
                                    state.context.CommandLogger, state.commandSource), cancellationToken).ConfigureAwait(false);
                            }
                            finally
                            {
                                state.context.Connection.ReturnCommand(command);
                            }
                        }, null, context.CancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    if (context.ExceptionDetector.IsCancellation(exception, context.CancellationToken))
                        context.QueryLogger.QueryCanceled(contextType);
                    else if (commandSource == CommandSource.ExecuteDelete)
                        context.QueryLogger.ExecuteDeleteFailed(contextType, exception);
                    else if (commandSource == CommandSource.ExecuteUpdate)
                        context.QueryLogger.ExecuteUpdateFailed(contextType, exception);
                    else
                        context.QueryLogger.NonQueryOperationFailed(contextType, exception);
                    throw;
                }
            }
        }
    }
}
