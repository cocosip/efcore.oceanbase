using System;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace OceanBase.EntityFrameworkCore.UnitTests;

internal sealed class TestColumnExpression(string name, string tableAlias, Type type,
    RelationalTypeMapping? mapping, bool nullable) : ColumnExpression(type, mapping)
{
    public override string Name => name;
    public override TableExpressionBase Table { get; } = new TableValuedFunctionExpression(tableAlias, tableAlias, []);
    public override string TableAlias => tableAlias;
    public override bool IsNullable => nullable;

    public override ColumnExpression MakeNullable()
        => nullable ? this : new TestColumnExpression(name, tableAlias, Type, TypeMapping, nullable: true);

    public override SqlExpression ApplyTypeMapping(RelationalTypeMapping? typeMapping)
        => new TestColumnExpression(name, tableAlias, Type, typeMapping, nullable);

    protected override Expression VisitChildren(ExpressionVisitor visitor) => this;
}
