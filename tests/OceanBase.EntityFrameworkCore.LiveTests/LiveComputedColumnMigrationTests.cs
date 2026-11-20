using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.LiveTests
{
    public sealed class LiveComputedColumnMigrationTests
    {
        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public Task MySql_virtual_nullability_migrations_preserve_rows_expression_and_comments(bool async, bool changeComment)
            => LiveStorageTable.WithTable<ComputedContext>(OceanBaseCompatibilityMode.MySql, async, false,
                (options, table) => new ComputedContext(new DbContextOptionsBuilder<ComputedContext>(options)
                    .ReplaceService<IModelCacheKeyFactory, CacheKey>().Options, table), async (context, _, stage) =>
                {
                    var nullable = context.GetService<IDesignTimeModel>().Model;
                    var builder = new ModelBuilder(context.GetService<IConventionSetBuilder>().CreateConventionSet());
                    Configure(builder, context.Table, required: true, changeComment ? "changed" : "original");
                    var required = context.GetService<IModelRuntimeInitializer>().Initialize(builder.FinalizeModel(), designTime: true);
                    var differ = context.GetService<IMigrationsModelDiffer>();
                    var generator = context.GetService<IMigrationsSqlGenerator>();
                    var table = "`" + context.Table + "`";
                    context.Add(new Row { Id = 1, BaseValue = 10, BaseText = "initial text", BaseBytes = [0, 1, 255] });
                    if (async) await context.SaveChangesAsync();
                    else context.SaveChanges();
                    context.ChangeTracker.Clear();

                    stage("nullable generated value before requiring; no synthesized backfill");
                    await Execute("INSERT INTO " + table + " (`Id`, `BaseValue`) VALUES (2, NULL)");
                    Assert.Null(await Computed(2));
                    await Rejected(() => Apply(nullable, required), 1138);
                    Assert.Null(await Computed(2));
                    Assert.Equal(11, await Computed(1, "initial text", [0, 1, 255]));
                    await Execute("DELETE FROM " + table + " WHERE `Id` = 2");

                    stage("require virtual computed value and change comment");
                    await Apply(nullable, required);
                    await Catalog("NO", changeComment ? "changed" : "original");
                    await Rejected(() => Execute("INSERT INTO " + table + " (`Id`, `BaseValue`) VALUES (2, NULL)"), 1048);
                    await Rejected(() => Execute("INSERT INTO " + table + " (`Id`, `BaseValue`, `BaseText`, `BaseBytes`) VALUES (2, 10, NULL, X'01')"), 1048);
                    await Rejected(() => Execute("INSERT INTO " + table + " (`Id`, `BaseValue`, `BaseText`, `BaseBytes`) VALUES (2, 10, 'valid', NULL)"), 1048);
                    Assert.Equal(11, await Computed(1, "initial text", [0, 1, 255]));

                    stage("restore nullable virtual value and original comment");
                    await Apply(required, nullable);
                    await Catalog("YES", "original");
                    await Execute("INSERT INTO " + table + " (`Id`, `BaseValue`) VALUES (2, NULL)");
                    Assert.Null(await Computed(2));
                    await Execute("UPDATE " + table + " SET `BaseValue` = 20, `BaseText` = 'updated text', `BaseBytes` = X'7F00FF' WHERE `Id` = 1");
                    Assert.Equal(21, await Computed(1, "updated text", [127, 0, 255]));

                    async Task Apply(IModel from, IModel to)
                    {
                        foreach (var command in generator.Generate(differ.GetDifferences(from.GetRelationalModel(), to.GetRelationalModel()), to))
                            await Execute(command.CommandText);
                    }

                    Task Execute(string sql)
                        => async ? context.Database.ExecuteSqlRawAsync(sql) : Task.FromResult(context.Database.ExecuteSqlRaw(sql));

                    async Task<int?> Computed(int id, string? expectedText = null, byte[]? expectedBytes = null)
                    {
                        var query = context.Set<Row>().Where(row => row.Id == id)
                            .Select(row => new { row.Computed, row.ComputedText, row.ComputedBytes });
                        var values = async ? await query.SingleAsync() : query.Single();
                        Assert.Equal(expectedText, values.ComputedText);
                        Assert.Equal(expectedBytes, values.ComputedBytes);
                        return values.Computed;
                    }

                    async Task Catalog(string nullableValue, string comment)
                    {
                        foreach (var column in new[] { "Computed", "ComputedText", "ComputedBytes" })
                        {
                            const string source = " FROM information_schema.COLUMNS WHERE table_schema = DATABASE() AND table_name = {0} AND column_name = {1}";
                            var nullableQuery = context.Database.SqlQueryRaw<string>("SELECT IS_NULLABLE AS `Value`" + source, context.Table, column);
                            var commentQuery = context.Database.SqlQueryRaw<string>("SELECT COLUMN_COMMENT AS `Value`" + source, context.Table, column);
                            Assert.Equal(nullableValue, Assert.Single(async ? await nullableQuery.ToArrayAsync() : nullableQuery.ToArray()));
                            Assert.Equal(comment, Assert.Single(async ? await commentQuery.ToArrayAsync() : commentQuery.ToArray()));
                        }
                    }
                });

        private static async Task Rejected(Func<Task> command, int serverCode)
        {
            var rejected = false;
            try { await command(); }
            catch (OceanBaseException error)
            {
                Assert.Equal(serverCode, error.ServerErrorCode);
                rejected = true;
            }
            Assert.True(rejected, "Expected the database to reject a null generated value.");
        }

        private static void Configure(ModelBuilder builder, string table, bool required, string comment)
        {
            var row = builder.Entity<Row>();
            row.ToTable(table);
            row.Property(value => value.Id).ValueGeneratedNever();
            row.Property(value => value.BaseText).HasMaxLength(50);
            row.Property(value => value.BaseBytes).HasMaxLength(50);
            row.Property(value => value.ComputedText).HasMaxLength(50).IsRequired(required).HasComputedColumnSql("BaseText", stored: false).HasComment(comment);
            row.Property(value => value.ComputedBytes).HasMaxLength(50).IsRequired(required).HasComputedColumnSql("BaseBytes", stored: false).HasComment(comment);
            row.Property(value => value.Computed).IsRequired(required).HasComputedColumnSql("BaseValue + 1", stored: false).HasComment(comment);
        }

        private sealed class ComputedContext(DbContextOptions<ComputedContext> options, string table) : DbContext(options)
        {
            public string Table => table;
            protected override void OnModelCreating(ModelBuilder modelBuilder)
                => Configure(modelBuilder, table, required: false, "original");
        }

        private sealed class CacheKey : IModelCacheKeyFactory
        {
            public object Create(DbContext context, bool designTime) => (((ComputedContext)context).Table, designTime);
        }

        private sealed class Row
        {
            public int Id { get; set; }
            public int? BaseValue { get; set; }
            public int? Computed { get; set; }
            public string? BaseText { get; set; }
            public string? ComputedText { get; set; }
            public byte[]? BaseBytes { get; set; }
            public byte[]? ComputedBytes { get; set; }
        }
    }
}
