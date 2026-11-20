using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace OceanBase.EntityFrameworkCore.LiveTests;

public sealed class LiveModelTriggerTests
{
    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public Task Explicit_triggers_return_generated_values_on_insert_and_update(OceanBaseCompatibilityMode mode, bool async)
        => LiveStorageTable.WithTable<TriggerContext>(mode, async, noBackslashEscapes: false, (options, table) =>
            new TriggerContext(new DbContextOptionsBuilder<TriggerContext>(options)
                .ReplaceService<IModelCacheKeyFactory, CacheKey>().Options, table, mode), async (context, _, stage) =>
            {
                stage("apply explicit trigger migration SQL");
                var sql = context.GetService<ISqlGenerationHelper>();
                var table = sql.DelimitIdentifier(context.Table);
                var input = sql.DelimitIdentifier(nameof(TriggerRow.Input));
                var generated = sql.DelimitIdentifier(nameof(TriggerRow.Generated));
                var migration = new MigrationBuilder(null);
                if (mode == OceanBaseCompatibilityMode.MySql)
                {
                    var insert = sql.DelimitIdentifier(context.Table + "I");
                    var update = sql.DelimitIdentifier(context.Table + "U");
                    migration.Sql($"CREATE TRIGGER {insert} BEFORE INSERT ON {table} FOR EACH ROW SET NEW.{generated} = NEW.{input} * 2");
                    migration.Sql($"CREATE TRIGGER {update} BEFORE UPDATE ON {table} FOR EACH ROW SET NEW.{generated} = NEW.{input} * 3");
                }
                else
                {
                    var trigger = sql.DelimitIdentifier(context.Table + "I");
                    var ddl = $"CREATE TRIGGER {trigger} BEFORE INSERT OR UPDATE ON {table} FOR EACH ROW "
                        + $"BEGIN IF INSERTING THEN :NEW.{generated} := :NEW.{input} * 2; "
                        + $"ELSE :NEW.{generated} := :NEW.{input} * 3; END IF; END;";
                    migration.Sql($"BEGIN EXECUTE IMMEDIATE '{ddl}'; END;");
                }

                var commands = context.GetService<IMigrationsSqlGenerator>()
                    .Generate(migration.Operations, context.GetService<IDesignTimeModel>().Model);
                var connection = context.GetService<IRelationalConnection>();
                foreach (var command in commands)
                {
                    Assert.True(command.TransactionSuppressed);
                    if (async) await command.ExecuteNonQueryAsync(connection);
                    else command.ExecuteNonQuery(connection);
                }

                stage("SaveChanges insert and generated values");
                var row = new TriggerRow { Input = 7 };
                context.Add(row);
                Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
                Assert.True(row.Id > 0);
                Assert.Equal(14, row.Generated);

                stage("SaveChanges update and generated values");
                row.Input = 11;
                Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
                Assert.Equal(33, row.Generated);
                var rows = context.Set<TriggerRow>().AsNoTracking();
                var persisted = async ? await rows.SingleAsync() : rows.Single();
                Assert.Equal((row.Id, 11, 33), (persisted.Id, persisted.Input, persisted.Generated));
            });

    private sealed class TriggerContext(DbContextOptions<TriggerContext> options, string table, OceanBaseCompatibilityMode mode)
        : DbContext(options)
    {
        public string Table => table;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var row = modelBuilder.Entity<TriggerRow>();
            row.ToTable(table, builder =>
            {
                builder.HasTrigger(table + "I");
                if (mode == OceanBaseCompatibilityMode.MySql) builder.HasTrigger(table + "U");
            });
            row.Property(value => value.Generated).HasDefaultValue(0).ValueGeneratedOnAddOrUpdate();
        }
    }

    private sealed class CacheKey : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => (((TriggerContext)context).Table, designTime);
    }

    private sealed class TriggerRow
    {
        public int Id { get; set; }
        public int Input { get; set; }
        public int Generated { get; set; }
    }
}
