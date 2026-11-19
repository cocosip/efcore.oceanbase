using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace OceanBase.EntityFrameworkCore.LiveTests
{
    public sealed class LiveJsonScalarTests
    {
        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        public Task Explicit_json_column_round_trips_text_and_both_kinds_of_null(OceanBaseCompatibilityMode mode, bool async)
            => LiveStorageTable.WithTable<Context>(mode, async, false,
                (options, table) => new Context(new DbContextOptionsBuilder<Context>(options)
                    .ReplaceService<IModelCacheKeyFactory, CacheKey>().Options, table),
                async (context, _, stage) =>
                {
                    stage("insert and read JSON text and SQL NULL");
                    var row = new Row { Id = 1, Document = "{\"text\":\"é海\",\"missing\":null}" };
                    context.AddRange(row, new Row { Id = 2 });
                    Assert.Equal(2, async ? await context.SaveChangesAsync() : context.SaveChanges());
                    context.ChangeTracker.Clear();
                    var document = await Find(context, 1, async);
                    using var parsed = JsonDocument.Parse(document.Document!);
                    Assert.Equal("é海", parsed.RootElement.GetProperty("text").GetString());
                    Assert.Equal(JsonValueKind.Null, parsed.RootElement.GetProperty("missing").ValueKind);
                    Assert.Null((await Find(context, 2, async)).Document);

                    stage("update the stored JSON document to a JSON null scalar");
                    document.Document = "null";
                    Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
                    context.ChangeTracker.Clear();
                    Assert.Equal("null", (await Find(context, 1, async)).Document);
                });

        private static async Task<Row> Find(Context context, int id, bool async)
            => (async ? await context.Set<Row>().FindAsync(id) : context.Set<Row>().Find(id))!;

        private sealed class Context(DbContextOptions<Context> options, string table) : DbContext(options)
        {
            internal string Table => table;

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                var row = modelBuilder.Entity<Row>().ToTable(table);
                row.Property(value => value.Id).ValueGeneratedNever();
                row.Property(value => value.Document).HasColumnType("json");
            }
        }

        private sealed class Row
        {
            public int Id { get; set; }
            public string? Document { get; set; }
        }

        private sealed class CacheKey : IModelCacheKeyFactory
        {
            public object Create(DbContext context, bool designTime) => (((Context)context).Table, designTime);
        }
    }
}
