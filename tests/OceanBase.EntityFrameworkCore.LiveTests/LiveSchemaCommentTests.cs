using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace OceanBase.EntityFrameworkCore.LiveTests;

public sealed class LiveSchemaCommentTests
{
    private const string InitialComment = "Owner's note 海\nSecond line";
    private const string ChangedComment = "Updated 'quote' 🌊 R&D";

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true, false)]
    public Task Comments_round_trip_through_create_alter_add_clear_and_reverse(
        OceanBaseCompatibilityMode mode, bool async, bool noBackslashEscapes)
        => LiveStorageTable.WithTable<CommentContext>(mode, async, noBackslashEscapes, (options, table) =>
            new CommentContext(new DbContextOptionsBuilder<CommentContext>(options)
                .ReplaceService<IModelCacheKeyFactory, CacheKey>().Options, table, mode), async (context, _, stage) =>
            {
                var initial = context.GetService<IDesignTimeModel>().Model;
                var originalText = mode == OceanBaseCompatibilityMode.Oracle ? InitialComment + " \\" : InitialComment;
                var changedText = mode == OceanBaseCompatibilityMode.Oracle ? ChangedComment + " \\" : ChangedComment;
                var changed = Build(context, changedText, addColumn: true, alterColumn: true);
                var cleared = Build(context, null, addColumn: true, alterColumn: true);
                var differ = context.GetService<IMigrationsModelDiffer>();
                var generator = context.GetService<IMigrationsSqlGenerator>();

                stage("catalog comments after CreateTables");
                await AssertComments(context, mode, async, originalText, addColumn: false);
                var row = new CommentRow { Caption = "unchanged row" };
                context.Add(row);
                Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());

                stage("catalog comments after table, identity-column, ordinary-column and add-column operations");
                await Apply(initial, changed);
                await AssertComments(context, mode, async, changedText, addColumn: true);
                var added = new CommentRow { Caption = "new row" };
                context.Add(added);
                Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
                Assert.True(added.Id > row.Id);

                stage("catalog comments after clear and rollback");
                await Apply(changed, cleared);
                await AssertComments(context, mode, async, null, addColumn: true);
                await Apply(cleared, initial);
                await AssertComments(context, mode, async, originalText, addColumn: false);
                context.ChangeTracker.Clear();
                var persisted = await Read(context.Set<CommentRow>().OrderBy(value => value.Id), async);
                Assert.Equal(new[] { (row.Id, row.Caption), (added.Id, added.Caption) },
                    persisted.Select(value => (value.Id, value.Caption)));
                Assert.All(persisted, value => Assert.Equal(14, value.Twice));

                async Task Apply(IModel from, IModel to)
                {
                    var operations = differ.GetDifferences(from.GetRelationalModel(), to.GetRelationalModel());
                    foreach (var command in generator.Generate(operations, to))
                    {
                        if (async) await context.Database.ExecuteSqlRawAsync(command.CommandText);
                        else context.Database.ExecuteSqlRaw(command.CommandText);
                    }
                }
            });

    private static IModel Build(CommentContext context, string? comment, bool addColumn, bool alterColumn)
    {
        var builder = new ModelBuilder(context.GetService<IConventionSetBuilder>().CreateConventionSet());
        Configure(builder, context.Table, comment, addColumn, alterColumn);
        return context.GetService<IModelRuntimeInitializer>().Initialize(builder.FinalizeModel(), designTime: true);
    }

    private static async Task AssertComments(CommentContext context, OceanBaseCompatibilityMode mode, bool async,
        string? comment, bool addColumn)
    {
        var tableSql = mode == OceanBaseCompatibilityMode.MySql
            ? "SELECT TABLE_COMMENT AS `Value` FROM information_schema.TABLES WHERE table_schema = DATABASE() AND table_name = {0}"
            : "SELECT COMMENTS AS \"Value\" FROM USER_TAB_COMMENTS WHERE TABLE_NAME = {0}";
        var columnSql = mode == OceanBaseCompatibilityMode.MySql
            ? "SELECT COLUMN_COMMENT AS `Value` FROM information_schema.COLUMNS WHERE table_schema = DATABASE() AND table_name = {0} AND column_name = {1}"
            : "SELECT COMMENTS AS \"Value\" FROM USER_COL_COMMENTS WHERE TABLE_NAME = {0} AND COLUMN_NAME = {1}";
        Assert.Equal(comment, EmptyToNull(Assert.Single(await Read(context.Database.SqlQueryRaw<string?>(tableSql, context.Table), async))));
        foreach (var column in addColumn ? new[] { "Id", "Caption", "Twice", "Extra" } : new[] { "Id", "Caption", "Twice" })
        {
            var actual = Assert.Single(await Read(context.Database.SqlQueryRaw<string?>(columnSql, context.Table, column), async));
            Assert.Equal(comment is null ? null : column + " " + comment, EmptyToNull(actual));
        }
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static Task<T[]> Read<T>(IQueryable<T> query, bool async)
        => async ? query.ToArrayAsync() : Task.FromResult(query.ToArray());

    private static void Configure(ModelBuilder model, string table, string? comment, bool addColumn, bool alterColumn)
    {
        var row = model.Entity<CommentRow>();
        row.ToTable(table, builder => builder.HasComment(comment));
        row.Property(value => value.Id).HasComment(comment is null ? null : "Id " + comment);
        row.Property(value => value.Caption).HasMaxLength(alterColumn ? 80 : 40)
            .HasDefaultValue("original default").HasComment(comment is null ? null : "Caption " + comment);
        row.Property<int>("BASE_VALUE").HasDefaultValue(7);
        row.Property(value => value.Twice).HasComputedColumnSql("BASE_VALUE * 2", stored: false)
            .HasComment(comment is null ? null : "Twice " + comment);
        if (addColumn) row.Property<int?>("Extra").HasComment(comment is null ? null : "Extra " + comment);
    }

    private sealed class CommentContext(DbContextOptions<CommentContext> options, string table, OceanBaseCompatibilityMode mode) : DbContext(options)
    {
        public string Table => table;
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => Configure(modelBuilder, table, mode == OceanBaseCompatibilityMode.Oracle ? InitialComment + " \\" : InitialComment,
                addColumn: false, alterColumn: false);
    }

    private sealed class CacheKey : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => (((CommentContext)context).Table, designTime);
    }

    private sealed class CommentRow
    {
        public int Id { get; set; }
        public string Caption { get; set; } = "";
        public int Twice { get; set; }
    }
}
