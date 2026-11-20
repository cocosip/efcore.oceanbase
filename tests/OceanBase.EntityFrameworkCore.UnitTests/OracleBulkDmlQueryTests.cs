using System;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using OceanBase.EntityFrameworkCore.Query;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests
{
    public sealed class OracleBulkDmlQueryTests
    {
        [Fact]
        public void Correlation_alias_avoids_target_nested_source_and_nested_setter_aliases()
        {
            using var context = Create();
            var table = context.Model.GetRelationalModel().FindTable("Rows", null)!;
            var integer = table.FindColumn("Id")!.StoreTypeMapping;
            var target = Table(context, "__ob_bulk_target");
            var nestedSource = Select(context, [Table(context, "__ob_bulk_target1")], "joined",
                new TestColumnExpression("Id", "__ob_bulk_target1", typeof(int), integer, false));
            var setter = Select(context, [Table(context, "__ob_bulk_target2")], projection:
                new TestColumnExpression("Count", "__ob_bulk_target2", typeof(int), integer, false));
            var select = Select(context, [target, new CrossJoinExpression(nestedSource)]);
            var update = new UpdateExpression(target, select,
                [new ColumnValueSetter(new TestColumnExpression("Count", target.Alias, typeof(int), integer, false), new ScalarSubqueryExpression(setter))]);
            var sql = Generator(context).GetCommand(update).CommandText;
            Assert.StartsWith("UPDATE \"Rows\" \"__ob_bulk_target3\"", sql);
            Assert.Contains("\"__ob_bulk_target\".\"Id\" = \"__ob_bulk_target3\".\"Id\"", sql);
            Assert.Contains("FROM \"Rows\" \"__ob_bulk_target1\"", sql);
            Assert.Contains("FROM \"Rows\" \"__ob_bulk_target2\"", sql);
            Assert.DoesNotContain("\"__ob_bulk_target\".\"Id\" = \"__ob_bulk_target\".\"Id\"", sql);
        }

        [Theory]
        [InlineData("offset")]
        [InlineData("limit")]
        [InlineData("distinct")]
        [InlineData("order")]
        [InlineData("group")]
        [InlineData("having")]
        [InlineData("projection")]
        public void Unnormalized_outer_operators_reject_instead_of_being_silently_dropped(string shape)
        {
            using var context = Create();
            var table = context.Model.GetRelationalModel().FindTable("Rows", null)!;
            var integer = table.FindColumn("Id")!.StoreTypeMapping;
            var target = Table(context, "target");
            var column = new TestColumnExpression("Count", "target", typeof(int), integer, false);
            var value = new SqlConstantExpression(Expression.Constant(1), integer);
            var select = Select(context, [target, new CrossJoinExpression(Table(context, "other"))],
                projection: shape == "projection" ? column : null, distinct: shape == "distinct");
            select = select.Update(select.Projection, select.Tables, null,
                shape == "group" ? [column] : [], shape == "having" ? value : null,
                shape == "order" ? [new OrderingExpression(column, true)] : [],
                shape == "limit" ? value : null, shape == "offset" ? value : null);
            var update = new UpdateExpression(target, select, [new ColumnValueSetter(column, value)]);
            Assert.Contains("query shape", Assert.Throws<NotSupportedException>(() => Generator(context).GetCommand(update)).Message);
        }

        [Fact]
        public void MySql_snapshot_aliases_avoid_both_target_and_nested_source_scopes()
        {
            using var context = Create(OceanBaseCompatibilityMode.MySql);
            var table = context.Model.GetRelationalModel().FindTable("Rows", null)!;
            var integer = table.FindColumn("Id")!.StoreTypeMapping;
            var target = Table(context, "__ob_bulk_target");
            var nested = Select(context, [Table(context, "__ob_bulk_target1")], "__ob_bulk_snapshot",
                new TestColumnExpression("Id", "__ob_bulk_target1", typeof(int), integer, false));
            var select = Select(context, [target, new CrossJoinExpression(nested)]);
            var id = new TestColumnExpression("Id", target.Alias, typeof(int), integer, false);
            var count = new TestColumnExpression("Count", target.Alias, typeof(int), integer, false);
            var update = new UpdateExpression(target, select, [new ColumnValueSetter(id, count), new ColumnValueSetter(count, id)]);
            var sql = new OceanBaseQuerySqlGenerator(context.GetService<QuerySqlGeneratorDependencies>(), OceanBaseCompatibilityMode.MySql)
                .GetCommand(update).CommandText;
            Assert.StartsWith("UPDATE `Rows` AS `__ob_bulk_target2`", sql);
            Assert.Contains("`__ob_bulk_target2`.`Id` = `__ob_bulk_snapshot1`.`__ob_key_0`", sql);
            Assert.Contains("FROM `Rows` AS `__ob_bulk_target1`", sql);
            Assert.Contains("SET `__ob_bulk_target2`.`Id` = `__ob_bulk_snapshot1`.`__ob_value_0`", sql);
        }

        private static TableExpression Table(DbContext context, string alias)
        {
            var entity = context.Model.FindEntityType(typeof(Row))!;
            var select = context.GetService<ISqlExpressionFactory>().Select(entity);
            var table = Assert.IsType<TableExpression>(Assert.Single(select.Tables));
            SetAlias(table, alias);
            return table;
        }

        private static SelectExpression Select(DbContext context, TableExpressionBase[] tables,
            string? alias = null, SqlExpression? projection = null, bool distinct = false)
        {
            var select = context.GetService<ISqlExpressionFactory>().Select(projection);
            if (distinct) select.ApplyDistinct();
            select.ApplyProjection();
            if (alias is not null) SetAlias(select, alias);
            return select.Update(select.Projection, tables, null, [], null, [], null, null);
        }

        private static void SetAlias(TableExpressionBase table, string alias)
            => typeof(TableExpressionBase).GetProperty(nameof(TableExpressionBase.Alias))!.SetValue(table, alias);

        private static OceanBaseQuerySqlGenerator Generator(DbContext context)
            => new(context.GetService<QuerySqlGeneratorDependencies>(), OceanBaseCompatibilityMode.Oracle);
        private static TestContext Create(OceanBaseCompatibilityMode mode = OceanBaseCompatibilityMode.Oracle) => new(new DbContextOptionsBuilder<TestContext>()
            .UseOceanBase("Server=offline;Database=offline", mode).Options);
        private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
        {
            protected override void OnModelCreating(ModelBuilder model) => model.Entity<Row>().ToTable("Rows");
        }
        private sealed class Row { public int Id { get; set; } public int Count { get; set; } }
    }
}
