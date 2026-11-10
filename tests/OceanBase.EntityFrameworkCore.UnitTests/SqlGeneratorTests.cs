using System.Linq;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Update;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests
{
    public sealed class SqlGeneratorTests
    {
        [Theory]
        [InlineData(0, "WHERE \"c\".\"Enabled\" = 1")]
        [InlineData(1, "CASE")]
        [InlineData(2, "CASE")]
        [InlineData(3, "\"Enabled\" = 1")]
        [InlineData(4, "WHEN \"c\".\"Enabled\" = 1")]
        [InlineData(5, "CASE")]
        [InlineData(6, "CASE")]
        public void Oracle_converts_between_numeric_Boolean_values_and_conditions(int scenario, string expected)
        {
            using var context = new TestContext(new DbContextOptionsBuilder<TestContext>()
                .UseOceanBase("Server=localhost;Database=test", OceanBaseCompatibilityMode.Oracle).Options);
            IQueryable query = scenario switch
            {
                0 => context.Customers.Where(customer => customer.Enabled),
                1 => context.Customers.Select(customer => customer.Id > 0),
                2 => context.Customers.Select(customer => !customer.Enabled),
                3 => context.Customers.Where(customer => customer.Enabled && (customer.Id > 0 || !customer.Enabled)),
                4 => context.Customers.Select(customer => customer.Enabled ? 10 : 20),
                5 => context.Customers.Select(customer => context.Customers.Any(other => other.Id == customer.Id)),
                _ => context.Customers.Select(customer => EF.Functions.Like(customer.Name, "A%"))
            };

            Assert.Contains(expected, query.ToQueryString());
        }

        [Fact]
        public void Oracle_nullable_Boolean_negation_projection_preserves_unknown()
        {
            var builder = new DbContextOptionsBuilder<TestContext>()
                .UseOceanBase("Server=localhost;Database=test", OceanBaseCompatibilityMode.Oracle);
            ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(
                builder.Options.Extensions.OfType<RelationalOptionsExtension>().Single().WithUseRelationalNulls(true));
            using var context = new TestContext(builder.Options);

            var sql = context.Customers.Select(customer => !customer.OptionalFlag).ToQueryString();

            Assert.Contains("CASE", sql);
            Assert.Contains("THEN 1", sql);
            Assert.True(sql.Contains("THEN 0", System.StringComparison.Ordinal), sql);
            Assert.DoesNotContain("ELSE 0", sql);
            Assert.True(!sql.Contains("ELSE", System.StringComparison.Ordinal)
                || sql.Contains("ELSE NULL", System.StringComparison.Ordinal), sql);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, "`Customers`", "LIMIT")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "\"Customers\"", "OFFSET")]
        public void Generates_mode_specific_select_sql(
            OceanBaseCompatibilityMode mode,
            string tableIdentifier,
            string pagingToken)
        {
            using var context = new TestContext(
                new DbContextOptionsBuilder<TestContext>()
                    .UseOceanBase("Server=localhost;Database=test", mode)
                    .Options);

            var sql = context.Customers
                .Where(customer => customer.Id > 1)
                .OrderBy(customer => customer.Name)
                .Skip(1)
                .Take(2)
                .ToQueryString();

            Assert.Contains(tableIdentifier, sql);
            Assert.Contains(pagingToken, sql);
            Assert.Contains("WHERE", sql);
            if (mode == OceanBaseCompatibilityMode.MySql)
            {
                Assert.DoesNotContain("FETCH", sql);
            }
            else
            {
                Assert.DoesNotContain(" AS ", sql);
                Assert.DoesNotContain("LIMIT", sql);
                Assert.Contains("FETCH NEXT", sql);
            }
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, "LIMIT 18446744073709551615 OFFSET")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "OFFSET")]
        public void Generates_offset_without_limit(OceanBaseCompatibilityMode mode, string paging)
        {
            using var context = new TestContext(new DbContextOptionsBuilder<TestContext>()
                .UseOceanBase("Server=localhost;Database=test", mode).Options);

            var sql = context.Customers.OrderBy(customer => customer.Id).Skip(3).ToQueryString();

            Assert.Contains(paging, sql);
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql)]
        [InlineData(OceanBaseCompatibilityMode.Oracle)]
        public void Generates_join_and_grouped_aggregate(OceanBaseCompatibilityMode mode)
        {
            using var context = new TestContext(new DbContextOptionsBuilder<TestContext>()
                .UseOceanBase("Server=localhost;Database=test", mode).Options);
            var join = context.Customers.Join(context.Customers,
                customer => customer.Id, other => other.Id, (customer, other) => new { customer.Id, other.Name });
            var grouped = context.Customers.GroupBy(customer => customer.Name)
                .Select(group => new { Name = group.Key, Count = group.Count() });

            Assert.Contains("INNER JOIN", join.ToQueryString());
            Assert.Contains("GROUP BY", grouped.ToQueryString());
            Assert.Contains("COUNT(*)", grouped.ToQueryString());
        }

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, "`Customers`")]
        [InlineData(OceanBaseCompatibilityMode.Oracle, "\"Customers\"")]
        public void Generates_insert_without_sql_server_output(
            OceanBaseCompatibilityMode mode,
            string tableIdentifier)
        {
            using var context = new TestContext(new DbContextOptionsBuilder<TestContext>()
                .UseOceanBase("Server=localhost;Database=test", mode).Options);
            var factory = context.GetService<IModificationCommandFactory>();
            var command = factory.CreateNonTrackedModificationCommand(
                new NonTrackedModificationCommandParameters("Customers", null, false));
            command.EntityState = EntityState.Added;
            command.AddColumnModification(new ColumnModificationParameters(
                "Name", null, "Alice", null, "TEXT",
                context.GetService<Microsoft.EntityFrameworkCore.Storage.IRelationalTypeMappingSource>().FindMapping(typeof(string))!,
                read: false, write: true, key: false, condition: false, sensitiveLoggingEnabled: false, isNullable: false));

            var builder = new StringBuilder();
            context.GetService<IUpdateSqlGenerator>().AppendInsertOperation(builder, command, 0, out _);

            Assert.Contains("INSERT INTO " + tableIdentifier, builder.ToString());
            Assert.DoesNotContain("OUTPUT", builder.ToString());
        }

        private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
        {
            public DbSet<Customer> Customers => Set<Customer>();

        }

        private sealed class Customer
        {
            public int Id { get; set; }
            public int? OptionalId { get; set; }
            public bool Enabled { get; set; }
            public bool? OptionalFlag { get; set; }
            public string Name { get; set; } = string.Empty;
        }
    }
}
