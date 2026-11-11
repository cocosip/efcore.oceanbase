using System;
using System.Linq.Expressions;
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
            var target = new TableExpression("__ob_bulk_target", table);
            var nestedSource = SelectExpression.CreateImmutable("joined", [new TableExpression("__ob_bulk_target1", table)],
                [new ProjectionExpression(new ColumnExpression("Id", "__ob_bulk_target1", typeof(int), integer, false), "Id")], new SqlAliasManager());
            var setter = new SelectExpression(null, [new TableExpression("__ob_bulk_target2", table)], null, [], null,
                [new ProjectionExpression(new ColumnExpression("Count", "__ob_bulk_target2", typeof(int), integer, false), "")], false, [], null, null, new SqlAliasManager());
            var select = new SelectExpression(null, [target, new CrossJoinExpression(nestedSource)], null, [], null, [], false, [], null, null, new SqlAliasManager());
            var update = new UpdateExpression(target, select,
                [new ColumnValueSetter(new ColumnExpression("Count", target.Alias, typeof(int), integer, false), new ScalarSubqueryExpression(setter))]);
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
            var target = new TableExpression("target", table);
            var column = new ColumnExpression("Count", "target", typeof(int), integer, false);
            var value = new SqlConstantExpression(1, typeof(int), integer);
            var select = new SelectExpression(null, [target, new CrossJoinExpression(new TableExpression("other", table))], null,
                shape == "group" ? [column] : [], shape == "having" ? value : null,
                shape == "projection" ? [new ProjectionExpression(column, "Count")] : [], shape == "distinct",
                shape == "order" ? [new OrderingExpression(column, true)] : [],
                shape == "offset" ? value : null, shape == "limit" ? value : null, new SqlAliasManager());
            var update = new UpdateExpression(target, select, [new ColumnValueSetter(column, value)]);
            Assert.Contains("query shape", Assert.Throws<NotSupportedException>(() => Generator(context).GetCommand(update)).Message);
        }

        [Fact]
        public void MySql_snapshot_aliases_avoid_both_target_and_nested_source_scopes()
        {
            using var context = Create(OceanBaseCompatibilityMode.MySql);
            var table = context.Model.GetRelationalModel().FindTable("Rows", null)!;
            var integer = table.FindColumn("Id")!.StoreTypeMapping;
            var target = new TableExpression("__ob_bulk_target", table);
            var nested = SelectExpression.CreateImmutable("__ob_bulk_snapshot", [new TableExpression("__ob_bulk_target1", table)],
                [new ProjectionExpression(new ColumnExpression("Id", "__ob_bulk_target1", typeof(int), integer, false), "Id")], new SqlAliasManager());
            var select = new SelectExpression(null, [target, new CrossJoinExpression(nested)], null, [], null, [], false, [], null, null, new SqlAliasManager());
            var id = new ColumnExpression("Id", target.Alias, typeof(int), integer, false);
            var count = new ColumnExpression("Count", target.Alias, typeof(int), integer, false);
            var update = new UpdateExpression(target, select, [new ColumnValueSetter(id, count), new ColumnValueSetter(count, id)]);
            var sql = new OceanBaseQuerySqlGenerator(context.GetService<QuerySqlGeneratorDependencies>(), OceanBaseCompatibilityMode.MySql)
                .GetCommand(update).CommandText;
            Assert.StartsWith("UPDATE `Rows` AS `__ob_bulk_target2`", sql);
            Assert.Contains("`__ob_bulk_target2`.`Id` = `__ob_bulk_snapshot1`.`__ob_key_0`", sql);
            Assert.Contains("FROM `Rows` AS `__ob_bulk_target1`", sql);
            Assert.Contains("SET `__ob_bulk_target2`.`Id` = `__ob_bulk_snapshot1`.`__ob_value_0`", sql);
        }

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
