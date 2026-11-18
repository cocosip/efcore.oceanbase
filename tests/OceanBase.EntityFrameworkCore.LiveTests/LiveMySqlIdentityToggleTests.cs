using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace OceanBase.EntityFrameworkCore.LiveTests;

public sealed class LiveMySqlIdentityToggleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Toggle_preserves_rows_and_generated_keys_in_an_owned_table(bool async)
        => LiveStorageTable.WithTable<IdentityContext>(OceanBaseCompatibilityMode.MySql, async, false, (options, table) =>
            new IdentityContext(new DbContextOptionsBuilder<IdentityContext>(options)
                .ReplaceService<IModelCacheKeyFactory, CacheKey>().Options, table), async (context, _, stage) =>
            {
                var assigned = context.GetService<IDesignTimeModel>().Model;
                var builder = new ModelBuilder(context.GetService<IConventionSetBuilder>().CreateConventionSet());
                Configure(builder, context.Table, generated: true);
                var generated = context.GetService<IModelRuntimeInitializer>().Initialize(builder.FinalizeModel(), designTime: true);
                var differ = context.GetService<IMigrationsModelDiffer>();
                var generator = context.GetService<IMigrationsSqlGenerator>();
                var identifier = context.GetService<ISqlGenerationHelper>().DelimitIdentifier(context.Table);
                var original = new IdentityRow { Id = 100, Caption = "original" };
                context.Add(original);
                Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());

                stage("enable AUTO_INCREMENT and insert a generated key");
                await Apply(assigned, generated);
                Assert.Contains("auto_increment", await ReadExtra(), StringComparison.OrdinalIgnoreCase);
                await Execute($"INSERT INTO {identifier} (`Caption`) VALUES ('generated')");
                var afterUpgrade = await ReadRows();
                Assert.Equal((100L, "original"), (afterUpgrade[0].Id, afterUpgrade[0].Caption));
                Assert.Equal("generated", afterUpgrade[1].Caption);
                Assert.True(afterUpgrade[1].Id > original.Id);

                stage("remove AUTO_INCREMENT and insert an assigned key");
                await Apply(generated, assigned);
                Assert.DoesNotContain("auto_increment", await ReadExtra(), StringComparison.OrdinalIgnoreCase);
                var explicitRow = new IdentityRow { Id = afterUpgrade[1].Id + 10, Caption = "assigned" };
                context.Add(explicitRow);
                Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
                Assert.Equal(afterUpgrade.Append(explicitRow).Select(row => (row.Id, row.Caption)),
                    (await ReadRows()).Select(row => (row.Id, row.Caption)));

                async Task Apply(IModel from, IModel to)
                {
                    var operations = differ.GetDifferences(from.GetRelationalModel(), to.GetRelationalModel());
                    await Execute(Assert.Single(generator.Generate(operations, to)).CommandText);
                }

                Task<int> Execute(string sql) => async ? context.Database.ExecuteSqlRawAsync(sql) : Task.FromResult(context.Database.ExecuteSqlRaw(sql));

                Task<IdentityRow[]> ReadRows()
                {
                    var query = context.Set<IdentityRow>().AsNoTracking().OrderBy(row => row.Id);
                    return async ? query.ToArrayAsync() : Task.FromResult(query.ToArray());
                }

                async Task<string> ReadExtra()
                {
                    var query = context.Database.SqlQueryRaw<string>(
                        "SELECT EXTRA AS `Value` FROM information_schema.COLUMNS WHERE table_schema = DATABASE() AND table_name = {0} AND column_name = 'Id'",
                        context.Table);
                    return Assert.Single(async ? await query.ToArrayAsync() : query.ToArray());
                }
            });

    private static void Configure(ModelBuilder model, string table, bool generated)
    {
        var entity = model.Entity<IdentityRow>().ToTable(table);
        entity.Property(row => row.Caption).HasMaxLength(32);
        if (!generated) entity.Property(row => row.Id).ValueGeneratedNever();
    }

    private sealed class IdentityContext(DbContextOptions<IdentityContext> options, string table) : DbContext(options)
    {
        public string Table => table;
        protected override void OnModelCreating(ModelBuilder modelBuilder) => Configure(modelBuilder, table, generated: false);
    }

    private sealed class CacheKey : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => (((IdentityContext)context).Table, designTime);
    }

    private sealed class IdentityRow
    {
        public long Id { get; set; }
        public string Caption { get; set; } = "";
    }
}
