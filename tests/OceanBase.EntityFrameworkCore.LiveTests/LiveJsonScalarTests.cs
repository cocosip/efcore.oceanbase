using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.LiveTests
{
    public sealed class LiveJsonScalarTests
    {
        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public Task Json_value_projects_filters_and_preserves_native_nulls(OceanBaseCompatibilityMode mode, bool async)
            => LiveStorageTable.WithTable<Context>(mode, async, false,
                (options, table) => new Context(new DbContextOptionsBuilder<Context>(options)
                    .ReplaceService<IModelCacheKeyFactory, CacheKey>().Options, table),
                async (context, _, stage) =>
                {
                    stage("insert JSON path fixtures");
                    context.AddRange(new Row
                    {
                        Id = 1,
                        Document = JsonSerializer.Serialize(new Dictionary<string, object?>
                        {
                            ["name"] = "Ada 海🌊", ["count"] = 42, ["enabled"] = true, ["absent"] = null,
                            ["items"] = new[] { new { name = "nested" } },
                            ["key' OR 1=1 --"] = "safe", ["back\\slash"] = "slash",
                            ["limit"] = new string('x', mode == OceanBaseCompatibilityMode.MySql ? 512 : 4000),
                            ["over"] = new string('x', mode == OceanBaseCompatibilityMode.MySql ? 513 : 4001),
                            ["unicode"] = new string('海', mode == OceanBaseCompatibilityMode.MySql ? 512 : 1333),
                            ["unicodeOver"] = new string('海', mode == OceanBaseCompatibilityMode.MySql ? 513 : 1334)
                        })
                    }, new Row { Id = 2 }, new Row { Id = 3, Document = "null" });
                    Assert.Equal(3, async ? await context.SaveChangesAsync() : context.SaveChanges());
                    context.ChangeTracker.Clear();

                    stage("execute JSON_VALUE predicate and scalar projections");
                    var rows = context.Set<Row>();
                    var match = rows.Where(row => EF.Functions.OceanBaseJsonValue(row.Document, "$.name") == "Ada 海🌊");
                    Assert.Equal(1, Assert.Single(await Read(match.Select(row => row.Id), async)));
                    Assert.Equal("nested", Assert.Single(await Read(match.Select(row =>
                        EF.Functions.OceanBaseJsonValue(row.Document, "$.items[0].name")), async)));
                    Assert.Equal("42", Assert.Single(await Read(match.Select(row =>
                        EF.Functions.OceanBaseJsonValue(row.Document, "$.count")), async)));
                    Assert.Equal("true", Assert.Single(await Read(match.Select(row =>
                        EF.Functions.OceanBaseJsonValue(row.Document, "$.enabled")), async)));

                    stage("preserve missing path, JSON null, SQL NULL and nonscalar native results");
                    foreach (var nullPath in new[] { "$.missing", "$.absent" })
                        Assert.All(await Read(rows.Select(row => EF.Functions.OceanBaseJsonValue(row.Document, nullPath)), async), Assert.Null);
                    var nonscalar = Assert.Single(await Read(match.Select(row =>
                        EF.Functions.OceanBaseJsonValue(row.Document, "$.items")), async));
                    if (mode == OceanBaseCompatibilityMode.Oracle) Assert.Null(nonscalar);
                    else
                    {
                        using var parsed = JsonDocument.Parse(nonscalar!);
                        Assert.Equal("nested", parsed.RootElement[0].GetProperty("name").GetString());
                    }
                    Assert.Equal(new[] { 2, 3 }, await Read(rows.Where(row =>
                        EF.Functions.OceanBaseJsonValue(row.Document, "$.name") == null).OrderBy(row => row.Id).Select(row => row.Id), async));

                    stage("preserve native default scalar length and overflow behavior");
                    Assert.Equal(mode == OceanBaseCompatibilityMode.MySql ? 512 : 4000,
                        Assert.Single(await Read(match.Select(row => EF.Functions.OceanBaseJsonValue(row.Document, "$.limit")), async))!.Length);
                    Assert.Null(Assert.Single(await Read(match.Select(row => EF.Functions.OceanBaseJsonValue(row.Document, "$.over")), async)));
                    Assert.Equal(mode == OceanBaseCompatibilityMode.MySql ? 512 : 1333,
                        Assert.Single(await Read(match.Select(row => EF.Functions.OceanBaseJsonValue(row.Document, "$.unicode")), async))!.Length);
                    Assert.Null(Assert.Single(await Read(match.Select(row => EF.Functions.OceanBaseJsonValue(row.Document, "$.unicodeOver")), async)));

                    stage("quote JSON paths and refresh captured constant paths");
                    var path = "$.\"key' OR 1=1 --\"";
                    var extracted = match.Select(row => EF.Functions.OceanBaseJsonValue(row.Document, path));
                    Assert.Equal("safe", Assert.Single(await Read(extracted, async)));
                    path = "$.\"back\\\\slash\"";
                    Assert.Equal("slash", Assert.Single(await Read(extracted, async)));

                    stage("preserve native malformed JSON and invalid path handling");
                    Assert.Null(Assert.Single(await Read(match.Select(row => EF.Functions.OceanBaseJsonValue("{", "$.name")), async)));
                    if (async)
                        await Assert.ThrowsAsync<OceanBaseException>(() => Read(match.Select(row =>
                            EF.Functions.OceanBaseJsonValue(row.Document, "$[")), true));
                    else
                        Assert.Throws<OceanBaseException>(() => match.Select(row =>
                            EF.Functions.OceanBaseJsonValue(row.Document, "$[")).ToArray());
                });

        private static async Task<T[]> Read<T>(IQueryable<T> query, bool async)
            => async ? await query.ToArrayAsync() : query.ToArray();

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public Task Explicit_json_column_round_trips_text_and_both_kinds_of_null(OceanBaseCompatibilityMode mode, bool async)
            => LiveStorageTable.WithTable<Context>(mode, async, false,
                (options, table) => new Context(new DbContextOptionsBuilder<Context>(options)
                    .ReplaceService<IModelCacheKeyFactory, CacheKey>().Options, table),
                async (context, _, stage) =>
                {
                    stage("insert and read JSON text and SQL NULL");
                    var text = "é海🌊" + new string('x', 5000);
                    var row = new Row { Id = 1, Document = JsonSerializer.Serialize(new { text, missing = (string?)null }) };
                    context.AddRange(row, new Row { Id = 2 });
                    Assert.Equal(2, async ? await context.SaveChangesAsync() : context.SaveChanges());
                    context.ChangeTracker.Clear();
                    var document = await Find(context, 1, async);
                    using var parsed = JsonDocument.Parse(document.Document!);
                    Assert.Equal(text, parsed.RootElement.GetProperty("text").GetString());
                    Assert.Equal(JsonValueKind.Null, parsed.RootElement.GetProperty("missing").ValueKind);
                    Assert.Null((await Find(context, 2, async)).Document);

                    stage("update the stored JSON document to a JSON null scalar");
                    document.Document = "null";
                    Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
                    context.ChangeTracker.Clear();
                    Assert.Equal("null", (await Find(context, 1, async)).Document);

                    stage("update the stored JSON null to SQL NULL");
                    var updated = await Find(context, 1, async);
                    updated.Document = null;
                    Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
                    context.ChangeTracker.Clear();
                    Assert.Null((await Find(context, 1, async)).Document);
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
