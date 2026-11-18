using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.LiveTests;

public sealed class LiveAggregateQueryTests
{
    private static readonly Type[] NumericTypes = [typeof(sbyte), typeof(byte), typeof(short), typeof(ushort),
        typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal)];

    public static IEnumerable<object[]> NumericCases()
    {
        foreach (var mode in Enum.GetValues<OceanBaseCompatibilityMode>())
        foreach (var async in new[] { false, true })
        foreach (var type in NumericTypes)
        {
            yield return [mode, async, type, false];
            if (type == typeof(decimal)) yield return [mode, async, type, true];
        }
    }

    public static IEnumerable<object[]> GroupCases()
    {
        foreach (var mode in Enum.GetValues<OceanBaseCompatibilityMode>())
        foreach (var async in new[] { false, true })
        foreach (var nulls in new[] { false, true }) yield return [mode, async, nulls];
    }

    [Theory]
    [MemberData(nameof(NumericCases))]
    public async Task Numeric_aggregates_keep_results_and_characterize_default_MySql_decimal_metadata_boundary(
        OceanBaseCompatibilityMode mode, bool async, Type type, bool faceted)
        => await (Task)typeof(LiveAggregateQueryTests).GetMethod(nameof(CheckNumeric), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(type).Invoke(null, [mode, async, faceted])!;

    private static Task CheckNumeric<T>(OceanBaseCompatibilityMode mode, bool async, bool faceted) where T : struct
        => WithTable(mode, async, false, async (context, stage) =>
        {
            var row = Expression.Parameter(typeof(Row), "row");
            var property = Expression.Property(row, faceted ? nameof(Row.FacetedDecimal) : typeof(T).Name);
            var selector = Expression.Lambda<Func<Row, T?>>(property, row);
            var select = selector.Compile();
            var values = context.Set<Row>().Select(selector);
            var expected = Seed().Select(select).ToArray();
            stage("numeric " + property.Member.Name + " populated/nullable");
            Assert.Equal(expected.Min(), await Scalar<T?, T?>(values, "Min", async));
            Assert.Equal(expected.Max(), await Scalar<T?, T?>(values, "Max", async));
            Assert.Equal(6, await Scalar<T?, int>(values, "Count", async));
            Assert.Equal(6L, await Scalar<T?, long>(values, "LongCount", async));

            var required = values.Where(value => value.HasValue).Select(value => value!.Value);
            Assert.Equal(expected.Min()!.Value, await Scalar<T, T>(required, "Min", async));
            Assert.Equal(expected.Max()!.Value, await Scalar<T, T>(required, "Max", async));
            Assert.Equal(4, await Scalar<T, int>(required, "Count", async));
            var summable = typeof(T) == typeof(int) || typeof(T) == typeof(long) || typeof(T) == typeof(float)
                || typeof(T) == typeof(double) || typeof(T) == typeof(decimal);
            var defaultMySqlDecimal = mode == OceanBaseCompatibilityMode.MySql && typeof(T) == typeof(decimal) && !faceted;
            if (defaultMySqlDecimal)
            {
                await MetadataFailure(() => Scalar<T?, T?>(values, "Sum", async));
                await MetadataFailure(() => Scalar<T, T>(required, "Sum", async));
                await MetadataFailure(() => Scalar<T?, T?>(values, "Average", async));
                await MetadataFailure(() => Scalar<T, T>(required, "Average", async));
                await DirectDecimalMetadataBoundary(context, async);
            }
            else if (summable)
            {
                var sum = expected.Where(value => value.HasValue).Sum(value => Convert.ToDecimal(value!.Value));
                Assert.Equal((T)Convert.ChangeType(sum, typeof(T)), await Scalar<T?, T?>(values, "Sum", async));
                Assert.Equal((T)Convert.ChangeType(sum, typeof(T)), await Scalar<T, T>(required, "Sum", async));
                if (typeof(T) == typeof(int) || typeof(T) == typeof(long))
                {
                    Assert.Equal((double)(sum / 4), await Scalar<T?, double?>(values, "Average", async));
                    Assert.Equal((double)(sum / 4), await Scalar<T, double>(required, "Average", async));
                }
                else
                {
                    Assert.Equal((T)Convert.ChangeType(sum / 4, typeof(T)), await Scalar<T?, T?>(values, "Average", async));
                    Assert.Equal((T)Convert.ChangeType(sum / 4, typeof(T)), await Scalar<T, T>(required, "Average", async));
                }
            }

            foreach (var empty in new[] { false, true })
            {
                stage(typeof(T).Name + (empty ? " empty" : " all-null"));
                var absent = context.Set<Row>().Where(value => empty ? value.Id < 0 : value.Id == 2 || value.Id == 6).Select(selector);
                Assert.Null(await Scalar<T?, T?>(absent, "Min", async));
                Assert.Null(await Scalar<T?, T?>(absent, "Max", async));
                Assert.Equal(empty ? 0 : 2, await Scalar<T?, int>(absent, "Count", async));
                Assert.Equal(empty ? 0L : 2L, await Scalar<T?, long>(absent, "LongCount", async));
                var emptyRequired = absent.Where(value => value.HasValue).Select(value => value!.Value);
                await Assert.ThrowsAsync<InvalidOperationException>(() => Scalar<T, T>(emptyRequired, "Min", async));
                await Assert.ThrowsAsync<InvalidOperationException>(() => Scalar<T, T>(emptyRequired, "Max", async));
                if (summable)
                {
                    if (defaultMySqlDecimal)
                    {
                        await MetadataFailure(() => Scalar<T?, T?>(absent, "Sum", async));
                        await MetadataFailure(() => Scalar<T, T>(emptyRequired, "Sum", async));
                    }
                    else
                    {
                        Assert.Equal(default(T), await Scalar<T?, T?>(absent, "Sum", async));
                        Assert.Equal(default(T), await Scalar<T, T>(emptyRequired, "Sum", async));
                    }
                    if (typeof(T) == typeof(int) || typeof(T) == typeof(long))
                    {
                        Assert.Null(await Scalar<T?, double?>(absent, "Average", async));
                        await Assert.ThrowsAsync<InvalidOperationException>(() => Scalar<T, double>(emptyRequired, "Average", async));
                    }
                    else
                    {
                        Assert.Null(await Scalar<T?, T?>(absent, "Average", async));
                        await Assert.ThrowsAsync<InvalidOperationException>(() => Scalar<T, T>(emptyRequired, "Average", async));
                    }
                }
                Assert.Equal(6, async ? await context.Set<Row>().CountAsync() : context.Set<Row>().Count());
            }
            Assert.Empty(context.ChangeTracker.Entries());
        });

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task Promoted_average_checked_sums_and_converter_owned_results_use_native_readers(OceanBaseCompatibilityMode mode, bool async)
        => await WithTable(mode, async, false, async (context, stage) =>
        {
            var rows = context.Set<Row>();
            stage("fractional integral AVG promotion and checked SUM result domains");
            Assert.Equal(3.5d, async ? await rows.AverageAsync(row => row.Id) : rows.Average(row => row.Id));
            Assert.Equal(3.5d, async ? await rows.AverageAsync(row => (long)row.Id) : rows.Average(row => (long)row.Id));
            var intOverflow = rows.Where(row => row.Id <= 2).Select(row => int.MaxValue - 2 + row.Id);
            var longOverflow = rows.Where(row => row.Id <= 2).Select(row => long.MaxValue - 2 + row.Id);
            await Assert.ThrowsAsync<OverflowException>(() => Scalar<int, int>(intOverflow, "Sum", async));
            await Assert.ThrowsAsync<OverflowException>(() => Scalar<long, long>(longOverflow, "Sum", async));
            Assert.Equal(6, async ? await rows.CountAsync() : rows.Count());

            stage("provider-domain aggregation and one inverse conversion, including empty SUM zero");
            var seed = Seed();
            Assert.Equal(seed.Sum(row => row.Offset + 10) - 10, async ? await rows.SumAsync(row => row.Offset) : rows.Sum(row => row.Offset));
            Assert.Equal(seed.Average(row => row.Offset + 10), async ? await rows.AverageAsync(row => row.Offset) : rows.Average(row => row.Offset));
            Assert.Equal(seed.Sum(row => row.ConvertedSingle + 10f) - 10f, async ? await rows.SumAsync(row => row.ConvertedSingle) : rows.Sum(row => row.ConvertedSingle));
            Assert.Equal(seed.Average(row => row.ConvertedSingle + 10f) - 10f, async ? await rows.AverageAsync(row => row.ConvertedSingle) : rows.Average(row => row.ConvertedSingle));
            Assert.Equal(seed.Sum(row => row.ConvertedDecimal + 10m) - 10m, async ? await rows.SumAsync(row => row.ConvertedDecimal) : rows.Sum(row => row.ConvertedDecimal));
            Assert.Equal(seed.Average(row => row.ConvertedDecimal + 10m) - 10m, async ? await rows.AverageAsync(row => row.ConvertedDecimal) : rows.Average(row => row.ConvertedDecimal));
            foreach (var empty in new[] { false, true })
            {
                var absent = rows.Where(row => empty ? row.Id < 0 : row.Id == 2 || row.Id == 6);
                Assert.Equal(0, async ? await absent.SumAsync(row => row.Offset) : absent.Sum(row => row.Offset));
                Assert.Equal(0f, async ? await absent.SumAsync(row => row.ConvertedSingle) : absent.Sum(row => row.ConvertedSingle));
                Assert.Equal(0m, async ? await absent.SumAsync(row => row.ConvertedDecimal) : absent.Sum(row => row.ConvertedDecimal));
                Assert.Null(async ? await absent.AverageAsync(row => row.ConvertedDecimal) : absent.Average(row => row.ConvertedDecimal));
            }
        });

    private static async Task DirectDecimalMetadataBoundary(QueryContext context, bool async)
    {
        if (async) await context.Database.OpenConnectionAsync();
        else context.Database.OpenConnection();
        Assert.Contains("4.4.2.2", context.Database.GetDbConnection().ServerVersion, StringComparison.Ordinal);
        var identifiers = context.GetService<ISqlGenerationHelper>();
        foreach (var aggregate in new[] { "SUM", "AVG" })
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            var body = aggregate + "(" + identifiers.DelimitIdentifier(nameof(Row.Decimal)) + ")";
            command.CommandText = "SELECT " + body + ", CAST(" + body + " AS CHAR) FROM " + identifiers.DelimitIdentifier(context.Table);
            await using var reader = async ? await command.ExecuteReaderAsync() : command.ExecuteReader();
            Assert.True(async ? await reader.ReadAsync() : reader.Read());
            Assert.Equal(aggregate == "SUM" ? 17m : 4.25m, decimal.Parse(reader.GetString(1), CultureInfo.InvariantCulture));
            foreach (var read in new Func<object>[] { () => reader.GetValue(0), () => reader.GetProviderSpecificValue(0),
                () => reader.GetFieldValue<OceanBaseDecimal>(0) })
            {
                var error = Assert.Throws<NotSupportedException>(read);
                Assert.Equal("The declared decimal metadata exceeds the supported mode limits.", error.Message);
            }
            do { while (async ? await reader.ReadAsync() : reader.Read()) { } }
            while (async ? await reader.NextResultAsync() : reader.NextResult());
        }
    }

    [Theory]
    [MemberData(nameof(GroupCases))]
    public async Task Group_aggregates_keep_null_keys_filtered_distinct_having_order_and_rebound_pages(
        OceanBaseCompatibilityMode mode, bool async, bool nulls)
        => await WithTable(mode, async, nulls, async (context, stage) =>
        {
            stage("nullable/composite/converted group keys and native aggregate readers");
            var rows = context.Set<Row>();
            var grouped = rows.GroupBy(row => new { row.Bucket, row.Tag }).Select(group => new
            {
                group.Key.Bucket, group.Key.Tag, Count = group.Count(), LongCount = group.LongCount(),
                NonNull = group.Count(row => row.Int32 != null), Sum = group.Sum(row => row.FacetedDecimal),
                Average = group.Average(row => row.Int32), Min = group.Min(row => row.Single), Max = group.Max(row => row.Double),
                Filtered = group.Where(row => row.Int32 > 0).Select(row => row.Int32).Distinct().Sum(),
                DistinctAverage = group.Where(row => row.Int32 > 0).Select(row => row.Int32).Distinct().Average()
            }).OrderBy(group => group.Bucket ?? -1).ThenBy(group => group.Tag);
            var expected = Seed().GroupBy(row => new { row.Bucket, row.Tag }).Select(group => new
            {
                group.Key.Bucket, group.Key.Tag, Count = group.Count(), LongCount = group.LongCount(),
                NonNull = group.Count(row => row.Int32 != null), Sum = group.Sum(row => row.FacetedDecimal),
                Average = group.Average(row => row.Int32), Min = group.Min(row => row.Single), Max = group.Max(row => row.Double),
                Filtered = group.Where(row => row.Int32 > 0).Select(row => row.Int32).Distinct().Sum(),
                DistinctAverage = group.Where(row => row.Int32 > 0).Select(row => row.Int32).Distinct().Average()
            }).OrderBy(group => group.Bucket ?? -1).ThenBy(group => group.Tag).ToArray();
            Assert.Equal(4, expected.Length);
            Assert.Equal(expected, await Read(grouped, async));
            var minimum = 1;
            var skip = 0;
            var take = 1;
            var pages = () => rows.GroupBy(row => row.Bucket).Where(group => group.Count() > minimum)
                .Select(group => new { Key = group.Key, Sum = group.Sum(row => row.Int32) })
                .OrderBy(group => group.Key ?? -1).Skip(skip).Take(take);
            foreach (var bounds in new[] { (1, 0, 2), (1, 1, 1), (2, 0, 2), (0, 10, 1) })
            {
                (minimum, skip, take) = bounds;
                stage($"HAVING and page min={minimum} skip={skip} take={take}");
                Assert.Equal(Seed().GroupBy(row => row.Bucket).Where(group => group.Count() > minimum)
                    .Select(group => new { Key = group.Key, Sum = group.Sum(row => row.Int32) })
                    .OrderBy(group => group.Key ?? -1).Skip(skip).Take(take), await Read(pages(), async));
            }
            stage("SUM HAVING and nullable aggregate ordering with refreshed threshold");
            var floor = 0;
            var having = rows.GroupBy(row => row.Bucket).Where(group => group.Sum(row => row.Int32) > floor)
                .OrderBy(group => group.Average(row => row.Int32) ?? -99).ThenBy(group => group.Key ?? -1)
                .Select(group => new { Key = group.Key, Sum = group.Sum(row => row.Int32), Average = group.Average(row => row.Int32) });
            foreach (var threshold in new[] { 0, 7, 8, -1 })
            {
                floor = threshold;
                Assert.Equal(Seed().GroupBy(row => row.Bucket).Where(group => group.Sum(row => row.Int32) > floor)
                    .OrderBy(group => group.Average(row => row.Int32) ?? -99).ThenBy(group => group.Key ?? -1)
                    .Select(group => new { Key = group.Key, Sum = group.Sum(row => row.Int32), Average = group.Average(row => row.Int32) }), await Read(having, async));
            }
            stage("selector overloads, distinct scalar aggregates and empty group source");
            Assert.Equal(Seed().Sum(row => row.FacetedDecimal), async ? await rows.SumAsync(row => row.FacetedDecimal) : rows.Sum(row => row.FacetedDecimal));
            Assert.Equal(Seed().Average(row => row.Int64), async ? await rows.AverageAsync(row => row.Int64) : rows.Average(row => row.Int64));
            Assert.Equal(Seed().Count(row => row.Int32 != null), async ? await rows.CountAsync(row => row.Int32 != null) : rows.Count(row => row.Int32 != null));
            Assert.Equal(Seed().LongCount(row => row.Int32 != null), async ? await rows.LongCountAsync(row => row.Int32 != null) : rows.LongCount(row => row.Int32 != null));
            var distinct = rows.Select(row => row.FacetedDecimal).Distinct();
            Assert.Equal(Seed().Select(row => row.FacetedDecimal).Distinct().Sum(), async ? await distinct.SumAsync() : distinct.Sum());
            Assert.Empty(await Read(rows.Where(row => row.Id < 0).GroupBy(row => row.Bucket).Select(group => group.Sum(row => row.Int32)), async));
        });

    [Theory]
    [MemberData(nameof(GroupCases))]
    public async Task Grouped_collections_and_first_entities_keep_null_correlations_and_current_bindings(
        OceanBaseCompatibilityMode mode, bool async, bool nulls)
        => await WithTable(mode, async, nulls, async (context, stage) =>
        {
            var floor = 0;
            var skip = 0;
            var take = 4;
            foreach (var bounds in new[] { (0, 0, 4), (1, 0, 4), (0, 1, 1), (6, 0, 4), (0, 0, 4) })
            {
                (floor, skip, take) = bounds;
                stage($"grouped collections floor={floor} skip={skip} take={take}");
                var query = context.Set<Row>().Where(row => row.Id > floor).GroupBy(row => new { row.Bucket, row.Tag })
                    .OrderBy(group => group.Key.Bucket ?? -1).ThenBy(group => group.Key.Tag).Skip(skip).Take(take)
                    .Select(group => new { group.Key, Count = group.Count(), Values = group.OrderBy(row => row.Id).Select(row => row.Int32).ToList() });
                var expected = Seed().Where(row => row.Id > floor).GroupBy(row => new { row.Bucket, row.Tag })
                    .OrderBy(group => group.Key.Bucket ?? -1).ThenBy(group => group.Key.Tag).Skip(skip).Take(take).ToArray();
                var actual = await Read(query, async);
                Assert.Equal(expected.Length, actual.Length);
                for (var index = 0; index < expected.Length; index++)
                {
                    Assert.Equal(expected[index].Key, actual[index].Key);
                    Assert.Equal(expected[index].Count(), actual[index].Count);
                    Assert.Equal(expected[index].OrderBy(row => row.Id).Select(row => row.Int32), actual[index].Values);
                }
            }
            foreach (var tracking in Enum.GetValues<QueryTrackingBehavior>())
            foreach (var resultSelector in new[] { false, true })
            {
                stage($"group first tracking={tracking} resultSelector={resultSelector}");
                context.ChangeTracker.Clear();
                context.ChangeTracker.QueryTrackingBehavior = tracking;
                var rows = context.Set<Row>();
                var query = resultSelector
                    ? rows.GroupBy(row => row.Bucket, (key, group) => new { Key = key, Count = group.Count(), First = group.OrderBy(row => row.Id).FirstOrDefault() }).OrderBy(group => group.Key ?? -1)
                    : rows.GroupBy(row => row.Bucket).Select(group => new { Key = group.Key, Count = group.Count(), First = group.OrderBy(row => row.Id).FirstOrDefault() }).OrderBy(group => group.Key ?? -1);
                var expected = Seed().GroupBy(row => row.Bucket).Select(group => new { Key = group.Key, Count = group.Count(), First = group.OrderBy(row => row.Id).FirstOrDefault() }).OrderBy(group => group.Key ?? -1).ToArray();
                var actual = await Read(query, async);
                Assert.Equal(expected, actual);
                var again = await Read(query, async);
                Assert.Equal(tracking == QueryTrackingBehavior.TrackAll, ReferenceEquals(actual[0].First, again[0].First));
                Assert.Equal(tracking == QueryTrackingBehavior.TrackAll ? 3 : 0, context.ChangeTracker.Entries().Count());
            }
        });

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, false)]
    [InlineData(OceanBaseCompatibilityMode.MySql, true)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
    [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
    public async Task Text_and_temporal_extrema_preserve_native_values_and_empty_cardinality(OceanBaseCompatibilityMode mode, bool async)
        => await WithTable(mode, async, false, async (context, stage) =>
        {
            stage("bounded ASCII text extrema under declared collation domain");
            var rows = context.Set<Row>();
            Assert.Equal("alpha", async ? await rows.MinAsync(row => row.Label) : rows.Min(row => row.Label));
            Assert.Equal("gamma", async ? await rows.MaxAsync(row => row.Label) : rows.Max(row => row.Label));
            var absentText = rows.Where(row => row.Id == 2 || row.Id == 6).Select(row => row.Label);
            Assert.Null(async ? await absentText.MinAsync() : absentText.Min());
            Assert.Null(async ? await absentText.MaxAsync() : absentText.Max());
            var emptyText = rows.Where(row => row.Id < 0).Select(row => row.Label);
            Assert.Null(async ? await emptyText.MinAsync() : emptyText.Min());
            Assert.Null(async ? await emptyText.MaxAsync() : emptyText.Max());
            await Temporal(context, row => row.Stamp, async, stage);
            await Temporal(context, row => row.Zoned, async, stage);
            await Temporal(context, row => row.Day, async, stage);
            await Temporal(context, row => row.Time, async, stage);
            await Temporal(context, row => row.Duration, async, stage);
            stage("grouped text and temporal extrema including the all-null group");
            var grouped = rows.GroupBy(row => row.Bucket).Select(group => new
            {
                Key = group.Key, TextMin = group.Min(row => row.Label), TextMax = group.Max(row => row.Label),
                StampMin = group.Min(row => row.Stamp), StampMax = group.Max(row => row.Stamp),
                ZonedMin = group.Min(row => row.Zoned), ZonedMax = group.Max(row => row.Zoned),
                DayMin = group.Min(row => row.Day), DayMax = group.Max(row => row.Day),
                TimeMin = group.Min(row => row.Time), TimeMax = group.Max(row => row.Time),
                DurationMin = group.Min(row => row.Duration), DurationMax = group.Max(row => row.Duration)
            }).OrderBy(group => group.Key ?? -1);
            var expected = Seed().GroupBy(row => row.Bucket).Select(group => new
            {
                Key = group.Key, TextMin = group.Min(row => row.Label), TextMax = group.Max(row => row.Label),
                StampMin = group.Min(row => row.Stamp), StampMax = group.Max(row => row.Stamp),
                ZonedMin = group.Min(row => row.Zoned), ZonedMax = group.Max(row => row.Zoned),
                DayMin = group.Min(row => row.Day), DayMax = group.Max(row => row.Day),
                TimeMin = group.Min(row => row.Time), TimeMax = group.Max(row => row.Time),
                DurationMin = group.Min(row => row.Duration), DurationMax = group.Max(row => row.Duration)
            }).OrderBy(group => group.Key ?? -1);
            Assert.Equal(expected, await Read(grouped, async));
        });

    private static async Task Temporal<T>(QueryContext context, Expression<Func<Row, T?>> selector, bool async, Action<string> stage) where T : struct
    {
        stage("temporal extrema " + typeof(T).Name);
        var values = context.Set<Row>().Select(selector);
        var expected = Seed().Select(selector.Compile()).ToArray();
        Assert.Equal(expected.Min(), await Scalar<T?, T?>(values, "Min", async));
        Assert.Equal(expected.Max(), await Scalar<T?, T?>(values, "Max", async));
        var required = values.Where(value => value.HasValue).Select(value => value!.Value);
        Assert.Equal(expected.Min()!.Value, await Scalar<T, T>(required, "Min", async));
        Assert.Equal(expected.Max()!.Value, await Scalar<T, T>(required, "Max", async));
        foreach (var empty in new[] { false, true })
        {
            var absent = context.Set<Row>().Where(row => empty ? row.Id < 0 : row.Id == 2 || row.Id == 6).Select(selector);
            Assert.Null(await Scalar<T?, T?>(absent, "Min", async));
            Assert.Null(await Scalar<T?, T?>(absent, "Max", async));
            var emptyRequired = absent.Where(value => value.HasValue).Select(value => value!.Value);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Scalar<T, T>(emptyRequired, "Min", async));
            await Assert.ThrowsAsync<InvalidOperationException>(() => Scalar<T, T>(emptyRequired, "Max", async));
            Assert.Equal(6, async ? await context.Set<Row>().CountAsync() : context.Set<Row>().Count());
        }
    }

    private static async Task<TResult> Scalar<T, TResult>(IQueryable<T> query, string name, bool async)
    {
        // Construct the same public Queryable call for all numeric overload families.
        var method = typeof(Queryable).GetMethods().Single(candidate => candidate.Name == name && candidate.GetParameters().Length == 1
            && (name is "Min" or "Max" or "Count" or "LongCount" ? candidate.IsGenericMethodDefinition
                : !candidate.IsGenericMethod && candidate.GetParameters()[0].ParameterType == typeof(IQueryable<T>)));
        if (method.IsGenericMethodDefinition) method = method.MakeGenericMethod(typeof(T));
        var expression = Expression.Call(method, query.Expression);
        return async ? await ((IAsyncQueryProvider)query.Provider).ExecuteAsync<Task<TResult>>(expression)
            : query.Provider.Execute<TResult>(expression);
    }

    private static async Task MetadataFailure(Func<Task> execute)
    {
        var error = await Assert.ThrowsAsync<NotSupportedException>(execute);
        Assert.Equal("The declared decimal metadata exceeds the supported mode limits.", error.Message);
    }

    private static Task<T[]> Read<T>(IQueryable<T> query, bool async) => async ? query.ToArrayAsync() : Task.FromResult(query.ToArray());

    private static Task WithTable(OceanBaseCompatibilityMode mode, bool async, bool nulls, Func<QueryContext, Action<string>, Task> run)
        => LiveStorageTable.WithTable<QueryContext>(mode, async, false, (options, table) =>
        {
            var builder = new DbContextOptionsBuilder<QueryContext>(options).UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
                .ReplaceService<IModelCacheKeyFactory, CacheKey>();
            var extension = builder.Options.Extensions.OfType<RelationalOptionsExtension>().Single().WithUseRelationalNulls(nulls);
            ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(extension);
            return new QueryContext(builder.Options, mode, table);
        }, async (context, _, stage) =>
        {
            stage("seed verification");
            Assert.Equal(Seed(), await Read(context.Set<Row>().OrderBy(row => row.Id), async));
            await run(context, stage);
        });

    private static Row[] Seed() => Enumerable.Range(1, 6).Select(id =>
    {
        int? value = id switch { 1 => -4, 3 or 5 => 4, 4 => 12, _ => null };
        var stamp = value.HasValue ? new DateTime(2026, 11, 1, 10, 30, 0).AddDays(value.Value).AddTicks(1234560) : (DateTime?)null;
        return new Row
        {
            Id = id, Bucket = id is 2 or 6 ? null : id is 1 or 4 ? 2 : 1, Tag = id is 2 or 5 or 6 ? 7 : 8,
            SByte = (sbyte?)value, Byte = value.HasValue ? (byte)Math.Abs(value.Value) : null,
            Int16 = (short?)value, UInt16 = value.HasValue ? (ushort)Math.Abs(value.Value) : null,
            Int32 = value, UInt32 = value.HasValue ? (uint)Math.Abs(value.Value) : null, Int64 = value,
            UInt64 = value.HasValue ? (ulong)Math.Abs(value.Value) : null,
            Single = value + 0.25f, Double = value + 0.25d, Decimal = value + 0.25m, FacetedDecimal = value + 0.25m,
            Offset = value, ConvertedSingle = value + 0.25f, ConvertedDecimal = value + 0.25m,
            Label = id switch { 1 or 4 => "alpha", 3 => "beta", 5 => "gamma", _ => null },
            Stamp = stamp, Zoned = stamp.HasValue ? new DateTimeOffset(stamp.Value, TimeSpan.FromMinutes(345)) : null,
            Day = stamp.HasValue ? DateOnly.FromDateTime(stamp.Value) : null,
            Time = stamp.HasValue ? new TimeOnly(10 + value!.Value / 4, 30, 0).Add(TimeSpan.FromTicks(1234560)) : null,
            Duration = value.HasValue ? TimeSpan.FromSeconds(value.Value).Add(TimeSpan.FromTicks(1234560)) : null
        };
    }).ToArray();

    private sealed class QueryContext(DbContextOptions<QueryContext> options, OceanBaseCompatibilityMode mode, string table) : DbContext(options)
    {
        public OceanBaseCompatibilityMode Mode => mode;
        public string Table => table;
        protected override void OnModelCreating(ModelBuilder model)
        {
            var row = model.Entity<Row>();
            row.ToTable(table);
            row.Property(value => value.Id).ValueGeneratedNever();
            row.Property(value => value.Tag).HasConversion(value => value + 100, value => value - 100);
            row.Property(value => value.FacetedDecimal).HasPrecision(18, 4);
            row.Property(value => value.Offset).HasConversion(value => value + 10L, value => (int?)(value - 10L));
            row.Property(value => value.ConvertedSingle).HasConversion(value => value + 10f, value => value - 10f);
            row.Property(value => value.ConvertedDecimal).HasConversion(value => value + 10m, value => value - 10m).HasPrecision(18, 4);
            row.Property(value => value.Label).HasMaxLength(16).IsUnicode(false);
            row.HasData(Seed());
        }
    }

    private sealed class CacheKey : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => (context.GetType(), ((QueryContext)context).Mode, ((QueryContext)context).Table, designTime);
    }

    private sealed record Row
    {
        public int Id { get; set; }
        public int? Bucket { get; set; }
        public int Tag { get; set; }
        public sbyte? SByte { get; set; }
        public byte? Byte { get; set; }
        public short? Int16 { get; set; }
        public ushort? UInt16 { get; set; }
        public int? Int32 { get; set; }
        public uint? UInt32 { get; set; }
        public long? Int64 { get; set; }
        public ulong? UInt64 { get; set; }
        public float? Single { get; set; }
        public double? Double { get; set; }
        public decimal? Decimal { get; set; }
        public decimal? FacetedDecimal { get; set; }
        public int? Offset { get; set; }
        public float? ConvertedSingle { get; set; }
        public decimal? ConvertedDecimal { get; set; }
        public string? Label { get; set; }
        public DateTime? Stamp { get; set; }
        public DateTimeOffset? Zoned { get; set; }
        public DateOnly? Day { get; set; }
        public TimeOnly? Time { get; set; }
        public TimeSpan? Duration { get; set; }
    }
}
