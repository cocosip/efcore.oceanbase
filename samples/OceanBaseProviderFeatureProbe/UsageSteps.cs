using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace OceanBaseProviderFeatureProbe;

internal static partial class Scenarios
{
    private static Step[] GetUsageSteps() =>
    [
        new("scalar-values-and-defaults", Shape.Usage, ScalarValues),
        new("linq-grouping-and-pagination", Shape.Usage, LinqQueries),
        new("global-filters-and-tracking", Shape.Usage, FiltersAndTracking),
        new("parameterized-raw-sql", Shape.Usage, RawSql),
        new("bulk-update-and-delete", Shape.Usage, BulkWrites),
        new("optimistic-concurrency", Shape.Usage, Concurrency),
        new("transaction-and-savepoint", Shape.Usage, Savepoints)
    ];

    private static void ConfigureUsage(ModelBuilder model, string root, Context context)
    {
        var record = model.Entity<SampleRecord>().ToTable(root + "R");
        record.Property(row => row.Id).ValueGeneratedNever();
        record.Property(row => row.Title).HasMaxLength(60);
        record.Property(row => row.Amount).HasPrecision(12, 2);
        record.Property(row => row.Score).HasDefaultValue(10);
        record.Property(row => row.Payload).HasMaxLength(24);
        record.Property(row => row.Version).IsConcurrencyToken();
        record.HasIndex(row => new { row.Workspace, row.Title }).IsUnique();
        record.HasQueryFilter(row => row.Workspace == context.Workspace && row.Active);
    }

    private static async Task Seed(Context context, bool useAsync)
    {
        context.AddRange(NewRow(1, "alpha", 12.5m), NewRow(2, "alpine", 7.5m), NewRow(3, "beta", 4m),
            NewRow(4, "archived", 2m, active: false), NewRow(5, "other", 1m, workspace: 2));
        await Save(context, useAsync);
        context.ChangeTracker.Clear();
    }

    private static SampleRecord NewRow(int id, string title, decimal amount, bool active = true, int workspace = 1)
        => new() { Id = id, Title = title, Amount = amount, Active = active, Workspace = workspace, Version = 1 };

    private static async Task ScalarValues(Context context, bool useAsync, Action<string> stage)
    {
        stage("write scalar and nullable values and read a database default");
        var sample = NewRow(1, "测试 record", 1234.56m);
        sample.ExternalId = Guid.NewGuid();
        sample.Day = new DateOnly(2024, 2, 29);
        sample.Payload = [0, 1, 127, 255];
        context.AddRange(sample, NewRow(2, "nullable", 2m));
        await Save(context, useAsync);
        Assert.Equal(10, sample.Score);
        context.ChangeTracker.Clear();
        var rows = await Read(context.Set<SampleRecord>().OrderBy(row => row.Id), useAsync);
        Assert.Collection(rows, row =>
        {
            Assert.Equal((sample.Title, sample.Amount, sample.ExternalId, sample.Day, 10),
                (row.Title, row.Amount, row.ExternalId, row.Day, row.Score));
            Assert.True(sample.Payload.SequenceEqual(row.Payload ?? []));
        }, row =>
        {
            Assert.Null(row.ExternalId);
            Assert.Null(row.Day);
            Assert.Null(row.Payload);
        });
    }

    private static async Task LinqQueries(Context context, bool useAsync, Action<string> stage)
    {
        stage("filter, group, project and page in LINQ");
        await Seed(context, useAsync);
        var prefix = "al";
        var query = context.Set<SampleRecord>().Where(row => row.Title.StartsWith(prefix));
        var group = Assert.Single(await Read(query.GroupBy(row => row.Workspace)
            .Select(rows => new { Count = rows.Count(), Total = rows.Sum(row => row.Amount) }), useAsync));
        Assert.Equal((2, 20m), (group.Count, group.Total));
        Assert.Equal("alpine", Assert.Single(await Read(query.OrderBy(row => row.Title).Skip(1).Take(1).Select(row => row.Title), useAsync)));
    }

    private static async Task FiltersAndTracking(Context context, bool useAsync, Action<string> stage)
    {
        stage("apply the composed filter, disable all filters and reload without tracking");
        await Seed(context, useAsync);
        var source = context.Set<SampleRecord>();
        Assert.Equal(3, (await Read(source.AsNoTracking(), useAsync)).Count);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(5, (await Read(source.IgnoreQueryFilters().AsNoTracking(), useAsync)).Count);
        context.Workspace = 2;
        Assert.Equal("other", Assert.Single(await Read(source, useAsync)).Title);
        Assert.Single(context.ChangeTracker.Entries<SampleRecord>());
    }

    private static async Task RawSql(Context context, bool useAsync, Action<string> stage)
    {
        stage("bind a raw SQL parameter and compose a scalar query");
        var title = "quote' ? :p";
        context.Add(NewRow(1, title, 4m));
        await Save(context, useAsync);
        context.ChangeTracker.Clear();
        var helper = context.GetService<ISqlGenerationHelper>();
        var table = helper.DelimitIdentifier(context.Root + "R");
        var titleColumn = helper.DelimitIdentifier(nameof(SampleRecord.Title));
        var id = helper.DelimitIdentifier(nameof(SampleRecord.Id));
        var value = helper.DelimitIdentifier("Value");
        var entitySql = $"SELECT * FROM {table} WHERE {titleColumn} = {{0}}";
        Assert.Equal(title, Assert.Single(await Read(context.Set<SampleRecord>().FromSqlRaw(entitySql, title), useAsync)).Title);
        var scalarSql = $"SELECT {id} AS {value} FROM {table} WHERE {titleColumn} = {{0}}";
        Assert.Equal(1, Assert.Single(await Read(context.Database.SqlQueryRaw<int>(scalarSql, title).Where(row => row > 0), useAsync)));
    }

    private static async Task BulkWrites(Context context, bool useAsync, Action<string> stage)
    {
        stage("execute set-based update and delete, then query their effects");
        await Seed(context, useAsync);
        var update = context.Set<SampleRecord>().Where(row => row.Id == 1);
        var updated = useAsync ? await update.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Score, row => row.Score + 1))
            : update.ExecuteUpdate(setters => setters.SetProperty(row => row.Score, row => row.Score + 1));
        Assert.Equal(1, updated);
        Assert.Equal(11, Assert.Single(await Read(update.Select(row => row.Score), useAsync)));
        var delete = context.Set<SampleRecord>().Where(row => row.Id == 2);
        Assert.Equal(1, useAsync ? await delete.ExecuteDeleteAsync() : delete.ExecuteDelete());
        Assert.Empty(await Read(delete, useAsync));
    }

    private static async Task Concurrency(Context context, bool useAsync, Action<string> stage)
    {
        stage("reject a tracked update with a stale concurrency token");
        context.Add(NewRow(1, "original", 1m));
        await Save(context, useAsync);
        context.ChangeTracker.Clear();
        var source = context.Set<SampleRecord>();
        var stale = Assert.Single(await Read(source, useAsync));
        var affected = useAsync ? await source.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Version, 2))
            : source.ExecuteUpdate(setters => setters.SetProperty(row => row.Version, 2));
        Assert.Equal(1, affected);
        stale.Title = "stale change";
        try
        {
            await Save(context, useAsync);
            throw new CheckFailedException("Expected DbUpdateConcurrencyException.");
        }
        catch (DbUpdateConcurrencyException error)
        {
            Assert.Same(stale, Assert.Single(error.Entries).Entity);
        }
        context.ChangeTracker.Clear();
        var actual = Assert.Single(await Read(source, useAsync));
        Assert.Equal(("original", 2), (actual.Title, actual.Version));
    }

    private static async Task Savepoints(Context context, bool useAsync, Action<string> stage)
    {
        stage("commit one write and roll back another using a savepoint");
        await using (var transaction = useAsync ? await context.Database.BeginTransactionAsync() : context.Database.BeginTransaction())
        {
            context.Add(NewRow(1, "kept", 1m));
            await Save(context, useAsync);
            const string savepoint = "BEFORE_SECOND";
            if (useAsync) await transaction.CreateSavepointAsync(savepoint);
            else transaction.CreateSavepoint(savepoint);
            context.Add(NewRow(2, "discarded", 2m));
            await Save(context, useAsync);
            if (useAsync)
            {
                await transaction.RollbackToSavepointAsync(savepoint);
                await transaction.CommitAsync();
            }
            else
            {
                transaction.RollbackToSavepoint(savepoint);
                transaction.Commit();
            }
        }
        context.ChangeTracker.Clear();
        Assert.Equal("kept", Assert.Single(await Read(context.Set<SampleRecord>(), useAsync)).Title);
    }

    private sealed class SampleRecord
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public int Workspace { get; set; }
        public bool Active { get; set; }
        public int Version { get; set; }
        public decimal Amount { get; set; }
        public int Score { get; set; }
        public Guid? ExternalId { get; set; }
        public DateOnly? Day { get; set; }
        public byte[]? Payload { get; set; }
    }
}
