using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace OceanBase.EntityFrameworkCore.Query
{
    internal sealed class OceanBaseQueryableMethodTranslatingExpressionVisitorFactory(
        QueryableMethodTranslatingExpressionVisitorDependencies dependencies,
        RelationalQueryableMethodTranslatingExpressionVisitorDependencies relationalDependencies)
        : IQueryableMethodTranslatingExpressionVisitorFactory
    {
        internal const string EmptyElementTypeAnnotation = "OceanBase:EmptyInlineElementType";

        public QueryableMethodTranslatingExpressionVisitor Create(QueryCompilationContext queryCompilationContext)
            => new Visitor(dependencies, relationalDependencies, (RelationalQueryCompilationContext)queryCompilationContext);

        private sealed class Visitor : RelationalQueryableMethodTranslatingExpressionVisitor
        {
            public Visitor(QueryableMethodTranslatingExpressionVisitorDependencies dependencies,
                RelationalQueryableMethodTranslatingExpressionVisitorDependencies relationalDependencies,
                RelationalQueryCompilationContext queryCompilationContext)
                : base(dependencies, relationalDependencies, queryCompilationContext) { }

            private Visitor(Visitor parent) : base(parent) { }

            protected override QueryableMethodTranslatingExpressionVisitor CreateSubqueryVisitor() => new Visitor(this);

            protected override ShapedQueryExpression? TranslateInlineQueryRoot(InlineQueryRootExpression inlineQueryRootExpression)
            {
                var translated = base.TranslateInlineQueryRoot(inlineQueryRootExpression);
                if (inlineQueryRootExpression.Values.Count == 0
                    && translated?.QueryExpression is SelectExpression { Tables: [ValuesExpression values] } select)
                {
#pragma warning disable EF1001 // The base translator returns a mutable SELECT; Update cannot replace its table yet.
                    select.SetTables([(ValuesExpression)values.AddAnnotation(EmptyElementTypeAnnotation, inlineQueryRootExpression.ElementType)]);
#pragma warning restore EF1001
                }
                return translated;
            }
        }
    }
}
