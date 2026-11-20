using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace OceanBase.EntityFrameworkCore.LiveTests;

public sealed class LiveScaffoldPrefixIndexTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Prefix_indexes_keep_native_lengths_order_and_uniqueness(bool unique)
        => LiveStorageTable.WithTable<PrefixContext>(OceanBaseCompatibilityMode.MySql, async: false, noBackslashEscapes: false,
            (options, table) => new PrefixContext(new DbContextOptionsBuilder<PrefixContext>(options)
                .ReplaceService<IModelCacheKeyFactory, CacheKey>().Options, table), (context, _, stage) =>
            {
                stage("create a native composite prefix index");
                var helper = context.GetService<ISqlGenerationHelper>();
                var table = helper.DelimitIdentifier(context.Table);
                var declaration = unique ? "CREATE UNIQUE INDEX" : "CREATE INDEX";
                Execute($"{declaration} `IX_Prefix` ON {table} (`Code`(9) DESC, `Number`)");
                stage("scaffold prefix length and uniqueness through Connector");
#pragma warning disable EF1001
                using var services = (ServiceProvider)new DesignTimeServicesBuilder(typeof(LiveScaffoldPrefixIndexTests).Assembly,
                    typeof(LiveScaffoldPrefixIndexTests).Assembly, new OperationReporter(null), [])
                    .Build(typeof(OceanBaseCompatibilityMode).Assembly.GetName().Name!);
#pragma warning restore EF1001
                context.Database.OpenConnection();
                var connection = context.Database.GetDbConnection();
                using var tables = connection.GetSchema("Tables", [connection.Database, null]);
                var catalogName = Assert.Single(tables.Rows.Cast<DataRow>().Select(row => row.Field<string>("TABLE_NAME")!),
                    name => string.Equals(name, context.Table, StringComparison.OrdinalIgnoreCase));
                var factory = services.GetRequiredService<IDatabaseModelFactory>();
                var scope = new DatabaseModelFactoryOptions(tables: [catalogName]);
                var catalog = Assert.Single(factory.Create(connection, scope).Tables);
                var index = Assert.Single(catalog.Indexes);
                Assert.Empty(catalog.UniqueConstraints);
                Assert.Equal(unique, index.IsUnique);
                Assert.Equal(new[] { "Code", "Number" }, index.Columns.Select(column => column.Name));
                Assert.Equal(new[] { false, false }, ReadNativeDirections());
                Assert.Equal(new[] { false, false }, index.IsDescending);
                Assert.Equal(new[] { 9, 0 }, Assert.IsType<int[]>(index["OceanBase:IndexPrefixLengths"]));
                stage("recreate native index with generated migration SQL");
                var operation = new CreateIndexOperation
                {
                    Name = Assert.IsType<string>(index.Name), Table = context.Table, Columns = index.Columns.Select(column => column.Name).ToArray(),
                    IsUnique = index.IsUnique, IsDescending = index.IsDescending.ToArray()
                };
                operation["OceanBase:IndexPrefixLengths"] = index["OceanBase:IndexPrefixLengths"];
                Execute($"DROP INDEX `IX_Prefix` ON {table}");
                var command = Assert.Single(context.GetService<IMigrationsSqlGenerator>().Generate([operation]));
                command.ExecuteNonQuery(context.GetService<IRelationalConnection>());
                var rebuilt = Assert.Single(Assert.Single(factory.Create(connection, scope).Tables).Indexes);
                Assert.Equal(index.IsUnique, rebuilt.IsUnique);
                Assert.Equal(new[] { false, false }, rebuilt.IsDescending);
                Assert.Equal(new[] { 9, 0 }, Assert.IsType<int[]>(rebuilt["OceanBase:IndexPrefixLengths"]));
                return Task.CompletedTask;

                bool[] ReadNativeDirections()
                {
                    using var command = connection.CreateCommand();
                    command.CommandText = $"SHOW INDEX FROM {table}";
                    using var reader = command.ExecuteReader();
                    var members = new System.Collections.Generic.SortedDictionary<int, bool>();
                    while (reader.Read())
                    {
                        if (!string.Equals(reader.GetString(reader.GetOrdinal("Key_name")), "IX_Prefix", StringComparison.OrdinalIgnoreCase)) continue;
                        var direction = reader.GetString(reader.GetOrdinal("Collation"));
                        Assert.Contains(direction, new[] { "A", "D" });
                        members.Add(Convert.ToInt32(reader["Seq_in_index"]), direction == "D");
                    }
                    return members.Values.ToArray();
                }

                void Execute(string sql)
                {
                    using var command = context.Database.GetDbConnection().CreateCommand();
                    command.CommandText = sql;
                    context.Database.OpenConnection();
                    try { command.ExecuteNonQuery(); }
                    finally { context.Database.CloseConnection(); }
                }
            });

    private sealed class PrefixContext(DbContextOptions<PrefixContext> options, string table) : DbContext(options)
    {
        public string Table => table;

        protected override void OnModelCreating(ModelBuilder model)
        {
            var row = model.Entity<PrefixRow>();
            row.ToTable(table);
            row.Property(value => value.Code).HasMaxLength(64);
        }
    }

    private sealed class CacheKey : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => (((PrefixContext)context).Table, designTime);
    }

    private sealed class PrefixRow
    {
        public int Id { get; set; }
        public string Code { get; set; } = "";
        public int Number { get; set; }
    }
}
