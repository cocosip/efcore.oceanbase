using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.LiveTests
{
    public sealed class LiveTemporalStorageTests
    {
        private static readonly DateTime SeedStamp = new(2024, 2, 29, 12, 34, 56, 123);
        private static readonly DateOnly SeedDay = new(2024, 2, 29);
        private static readonly TimeOnly SeedClock = new(12, 34, 56, 123);
        private static readonly TimeSpan SeedDuration = -new TimeSpan(2, 3, 4, 5).Add(TimeSpan.FromMilliseconds(123));
        private static readonly DateTimeOffset SeedZoned = new(SeedStamp, TimeSpan.FromMinutes(345));

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Oracle_exact_temporal_readers_reject_subtick_and_CLR_range_loss(bool async)
            => await WithTable(OceanBaseCompatibilityMode.Oracle, async, async (context, _, stage) =>
            {
                stage("native exact wrapper readers reject lossy CLR results");
                await Reject<DateTime, OverflowException>("TIMESTAMP '2024-02-29 12:34:56.123456789'");
                await Reject<DateTimeOffset, OverflowException>("TO_TIMESTAMP_TZ('2024-02-29 12:34:56.123456789 +05:45', 'YYYY-MM-DD HH24:MI:SS.FF9 TZH:TZM')");
                await Reject<TimeSpan, OverflowException>("INTERVAL '+000000000 00:00:00.000000001' DAY(9) TO SECOND(9)");
                await Reject<TimeOnly, OverflowException>("INTERVAL '+000000000 00:00:00.000000001' DAY(9) TO SECOND(9)");
                await Reject<TimeSpan, OverflowException>("INTERVAL '+999999999 00:00:00.0000000' DAY(9) TO SECOND(7)");
                await Reject<TimeOnly, ArgumentOutOfRangeException>("INTERVAL '+1 00:00:00' DAY(1) TO SECOND(0)");
                await Reject<TimeOnly, ArgumentOutOfRangeException>("INTERVAL '-0 00:00:01' DAY(1) TO SECOND(0)");

                async Task Reject<T, TError>(string expression) where T : struct where TError : Exception
                {
                    var sql = "SELECT " + expression + " AS \"Value\" FROM DUAL";
                    var query = context.Database.SqlQueryRaw<T>(sql);
                    var error = async ? await Record.ExceptionAsync(async () => await query.ToArrayAsync())
                        : Record.Exception(() => query.ToArray());
                    Assert.NotNull(error);
                    Assert.IsType<TError>(error.InnerException ?? error);
                }
            });

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task MySql_4422_out_of_range_raw_TIME_parameters_lose_fraction_while_literals_reject(bool async)
            => await WithTable(OceanBaseCompatibilityMode.MySql, async, async (context, _, stage) =>
            {
                stage("direct public parameter versus server TIME range");
                if (async) await context.Database.OpenConnectionAsync();
                else context.Database.OpenConnection();
                Assert.Contains("4.4.2.2", context.Database.GetDbConnection().ServerVersion, StringComparison.Ordinal);
                var table = context.GetService<ISqlGenerationHelper>().DelimitIdentifier(context.Table);
                var bound = new TimeSpan(0, 838, 59, 59);
                foreach (var sign in new[] { 1, -1 })
                foreach (var micros in new[] { 1, 999999 })
                {
                    var input = (bound + TimeSpan.FromTicks(micros * 10L)) * sign;
                    var parameter = new OceanBaseParameter { ParameterName = "p", DbType = DbType.Time, Scale = 6, Value = input };
                    var insert = "INSERT INTO " + table + " (`Id`, `Duration`) VALUES (2, {0})";
                    Assert.Equal(1, async ? await context.Database.ExecuteSqlRawAsync(insert, parameter) : context.Database.ExecuteSqlRaw(insert, parameter));
                    var stored = async ? await context.Set<Row>().SingleAsync() : context.Set<Row>().Single();
                    Assert.Equal(bound * sign, stored.Duration);
                    var rawSql = "SELECT CAST(`Duration` AS CHAR) AS `Value` FROM " + table;
                    var raw = context.Database.SqlQueryRaw<string>(rawSql);
                    Assert.Equal((sign == -1 ? "-" : "") + "838:59:59.000000", Assert.Single(async ? await raw.ToArrayAsync() : raw.ToArray()));
                    context.ChangeTracker.Clear();
                    var delete = "DELETE FROM " + table;
                    if (async) await context.Database.ExecuteSqlRawAsync(delete);
                    else context.Database.ExecuteSqlRaw(delete);
                    var literal = "INSERT INTO " + table + " (`Id`, `Duration`) VALUES (2, TIME '" + (sign == -1 ? "-" : "")
                        + "838:59:59." + micros.ToString("D6", System.Globalization.CultureInfo.InvariantCulture) + "')";
                    var error = async ? await Assert.ThrowsAsync<OceanBaseException>(() => context.Database.ExecuteSqlRawAsync(literal))
                        : Assert.Throws<OceanBaseException>(() => context.Database.ExecuteSqlRaw(literal));
                    Assert.Equal(1525, error.ServerErrorCode);
                    Assert.False(async ? await context.Set<Row>().AnyAsync() : context.Set<Row>().Any());
                }
            });

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Oracle_DATE_preserves_wall_clock_seconds_and_DateOnly_rejects_non_midnight_storage(bool async)
            => await WithTable(OceanBaseCompatibilityMode.Oracle, async, async (context, observer, stage) =>
            {
                stage("Oracle DATE time component and fractional comparand");
                var stamp = new DateTime(2024, 2, 29, 12, 34, 56);
                await InsertAndRead(context, new Row { Id = 2, OracleDate = stamp, Day = SeedDay }, async);
                var comparand = stamp.AddTicks(1);
                var query = context.Set<Row>().Where(row => row.OracleDate < comparand && row.OracleDate < EF.Constant(comparand));
                Assert.Single(async ? await query.ToArrayAsync() : query.ToArray());
                var invalid = new Row { Id = 99, OracleDate = comparand };
                context.Add(invalid);
                var writes = observer.ParameterWrites;
                var rejected = async ? await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync())
                    : Assert.Throws<DbUpdateException>(() => context.SaveChanges());
                Assert.IsType<ArgumentException>(rejected.InnerException);
                Assert.Same(invalid, Assert.Single(rejected.Entries).Entity);
                Assert.Equal(writes, observer.ParameterWrites);
                context.ChangeTracker.Clear();

                stage("DateOnly rejects a non-midnight value inserted by raw SQL");
                var table = context.GetService<ISqlGenerationHelper>().DelimitIdentifier(context.Table);
                var update = "UPDATE " + table + " SET \"Day\" = TO_DATE('2024-02-29 12:34:56', 'YYYY-MM-DD HH24:MI:SS')";
                if (async) await context.Database.ExecuteSqlRawAsync(update);
                else context.Database.ExecuteSqlRaw(update);
                var days = context.Set<Row>().Select(row => row.Day);
                var error = async ? await Record.ExceptionAsync(async () => await days.ToArrayAsync())
                    : Record.Exception(() => days.ToArray());
                Assert.NotNull(error);
                Assert.IsType<InvalidCastException>(error.InnerException ?? error);
                var restore = "UPDATE " + table + " SET \"Day\" = DATE '2024-02-29'";
                if (async) await context.Database.ExecuteSqlRawAsync(restore);
                else context.Database.ExecuteSqlRaw(restore);
                Assert.Equal(SeedDay, Assert.Single(async ? await days.ToArrayAsync() : days.ToArray()));
            });

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Temporal_literals_store_mapping_owned_fraction_sign_and_offset(OceanBaseCompatibilityMode mode, bool async)
            => await WithTable(mode, async, async (context, _, stage) =>
            {
                stage("five-type literal INSERT and real readback");
                var quantum = mode == OceanBaseCompatibilityMode.MySql ? 10 : 1;
                var expected = Seed() with { Id = 2, Stamp = SeedStamp.AddTicks(quantum), Zoned = SeedZoned.AddTicks(quantum),
                    Clock = new TimeOnly(TimeSpan.TicksPerDay - quantum), Duration = SeedDuration.Add(TimeSpan.FromTicks(-quantum)),
                    ConvertedStamp = null, ConvertedDuration = null };
                var helper = context.GetService<ISqlGenerationHelper>();
                var values = new (string Name, object Value)[] { (nameof(Row.Stamp), expected.Stamp!.Value),
                    (nameof(Row.Zoned), expected.Zoned!.Value), (nameof(Row.Day), expected.Day!.Value),
                    (nameof(Row.Clock), expected.Clock!.Value), (nameof(Row.Duration), expected.Duration!.Value) };
                var entity = context.Model.FindEntityType(typeof(Row))!;
                var sql = "INSERT INTO " + helper.DelimitIdentifier(context.Table) + " (" + helper.DelimitIdentifier(nameof(Row.Id))
                    + ", " + string.Join(", ", values.Select(value => helper.DelimitIdentifier(value.Name))) + ") VALUES (2, "
                    + string.Join(", ", values.Select(value => ((RelationalTypeMapping)entity.FindProperty(value.Name)!.GetTypeMapping()).GenerateSqlLiteral(value.Value))) + ")";
                if (async) await context.Database.ExecuteSqlRawAsync(sql);
                else context.Database.ExecuteSqlRaw(sql);
                AssertStored(expected, async ? await context.Set<Row>().SingleAsync() : context.Set<Row>().Single(), mode);
            });

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Numeric_offset_storage_is_independent_of_session_timezone(OceanBaseCompatibilityMode mode, bool async)
            => await WithTable(mode, async, async (context, _, stage) =>
            {
                if (async) await context.Database.OpenConnectionAsync();
                else context.Database.OpenConnection();
                var offsets = new[] { TimeSpan.Zero, TimeSpan.FromMinutes(345), TimeSpan.FromMinutes(-210), TimeSpan.FromHours(-12), TimeSpan.FromHours(14) };
                foreach (var session in new[] { "+05:45", "-03:30" })
                {
                    stage("session timezone=" + session);
                    var sql = mode == OceanBaseCompatibilityMode.MySql ? "SET SESSION time_zone = '" + session + "'"
                        : "ALTER SESSION SET TIME_ZONE = '" + session + "'";
                    if (async) await context.Database.ExecuteSqlRawAsync(sql);
                    else context.Database.ExecuteSqlRaw(sql);
                    for (var index = 0; index < offsets.Length; index++)
                    {
                        var id = index + 2;
                        if (session == "+05:45")
                            await InsertAndRead(context, new Row { Id = id, Stamp = SeedStamp, Zoned = new DateTimeOffset(SeedStamp, offsets[index]) }, async);
                        var stored = async ? await context.Set<Row>().AsNoTracking().SingleAsync(row => row.Id == id)
                            : context.Set<Row>().AsNoTracking().Single(row => row.Id == id);
                        Assert.Equal(SeedStamp, stored.Stamp);
                        Assert.Equal(DateTimeKind.Unspecified, stored.Stamp!.Value.Kind);
                        Assert.Equal(new DateTimeOffset(SeedStamp, offsets[index]).UtcTicks, stored.Zoned!.Value.UtcTicks);
                        Assert.Equal(mode == OceanBaseCompatibilityMode.MySql ? TimeSpan.Zero : offsets[index], stored.Zoned.Value.Offset);
                    }
                }
            });

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Query_comparands_CASE_and_original_values_do_not_inherit_write_precision(OceanBaseCompatibilityMode mode, bool async)
            => await WithTable(mode, async, async (context, observer, stage) =>
            {
                var initial = new Row { Id = 2, FacetedStamp = SeedStamp, FacetedClock = SeedClock, FacetedZoned = SeedZoned,
                    FacetedDuration = TimeSpan.FromHours(1) };
                await InsertAndRead(context, initial, async);
                stage("sub-column-precision comparands and CASE");
                var quantum = mode == OceanBaseCompatibilityMode.MySql ? 10 : 1;
                var stamp = SeedStamp.AddTicks(quantum);
                var clock = SeedClock.Add(TimeSpan.FromTicks(quantum));
                var zoned = SeedZoned.AddTicks(quantum);
                var duration = TimeSpan.FromHours(1).Add(TimeSpan.FromTicks(quantum));
                var query = context.Set<Row>().Select(row => new { Stamp = row.FacetedStamp < stamp, Clock = row.FacetedClock < clock,
                    Zoned = row.FacetedZoned < zoned, Duration = row.FacetedDuration < duration, Choice = row.FacetedStamp == stamp ? 7 : 9 });
                var result = async ? await query.SingleAsync() : query.Single();
                Assert.True(result.Stamp && result.Clock && result.Zoned && result.Duration);
                Assert.Equal(9, result.Choice);
                var literal = context.Set<Row>().Where(row => row.FacetedStamp < EF.Constant(stamp) && row.FacetedClock < EF.Constant(clock)
                    && row.FacetedDuration < EF.Constant(duration) && row.FacetedZoned < EF.Constant(zoned));
                Assert.Single(async ? await literal.ToArrayAsync() : literal.ToArray());
                if (mode == OceanBaseCompatibilityMode.Oracle)
                {
                    var large = TimeSpan.FromDays(100).Add(TimeSpan.FromTicks(1));
                    var largerLeading = context.Set<Row>().Where(row => row.FacetedDuration < large);
                    Assert.Single(async ? await largerLeading.ToArrayAsync() : largerLeading.ToArray());
                }

                stage("original value retains fraction and reports concurrency conflict");
                var tracked = async ? await context.Set<Row>().SingleAsync() : context.Set<Row>().Single();
                context.Entry(tracked).Property(row => row.FacetedStamp).OriginalValue = stamp;
                tracked.FacetedStamp = SeedStamp.AddMilliseconds(1);
                var writes = observer.ParameterWrites;
                var error = async ? await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => context.SaveChangesAsync())
                    : Assert.Throws<DbUpdateConcurrencyException>(() => context.SaveChanges());
                Assert.Same(tracked, Assert.Single(error.Entries).Entity);
                Assert.Equal(writes + 1, observer.ParameterWrites);
                context.ChangeTracker.Clear();
                var unchanged = async ? await context.Set<Row>().SingleAsync() : context.Set<Row>().Single();
                AssertStored(initial, unchanged, mode);
                unchanged.FacetedStamp = SeedStamp.AddMilliseconds(2);
                Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
                context.ChangeTracker.Clear();
                AssertStored(unchanged, async ? await context.Set<Row>().SingleAsync() : context.Set<Row>().Single(), mode);
            });

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Five_temporal_types_defaults_converters_endpoints_and_updates_round_trip(OceanBaseCompatibilityMode mode, bool async)
            => await WithTable(mode, async, async (context, _, stage) =>
            {
                stage("five-type NULL/endpoints/fractions");
                var quantum = mode == OceanBaseCompatibilityMode.MySql ? 10 : 1;
                var maximumDuration = mode == OceanBaseCompatibilityMode.MySql
                    ? new TimeSpan(0, 838, 59, 59) : TimeSpan.MaxValue;
                var minimumDuration = mode == OceanBaseCompatibilityMode.MySql ? -maximumDuration : TimeSpan.MinValue;
                var stamps = new DateTime?[] { null, DateTime.MinValue, DateTime.MaxValue.AddTicks(1 - quantum), SeedStamp.AddTicks(quantum) };
                var days = new DateOnly?[] { null, DateOnly.MinValue, DateOnly.MaxValue, SeedDay };
                var clocks = new TimeOnly?[] { null, TimeOnly.MinValue, new TimeOnly(TimeSpan.TicksPerDay - quantum), SeedClock.Add(TimeSpan.FromTicks(quantum)) };
                var durations = new TimeSpan?[] { null, minimumDuration, maximumDuration, SeedDuration.Add(TimeSpan.FromTicks(quantum)) };
                for (var index = 0; index < stamps.Length; index++)
                {
                    stage($"five-type boundary index={index}");
                    await InsertAndRead(context, new Row
                    {
                        Id = index + 2, Stamp = stamps[index], Day = days[index], Clock = clocks[index], Duration = durations[index],
                        Zoned = stamps[index] is { } stamp ? new DateTimeOffset(stamp, TimeSpan.Zero) : null,
                        ConvertedStamp = stamps[index] is { } converted ? new StampValue(converted) : null,
                        ConvertedDuration = durations[index] is { } duration ? new DurationValue(duration) : null
                    }, async);
                }
                if (mode == OceanBaseCompatibilityMode.MySql)
                {
                    stage("last microsecond below signed TIME endpoints");
                    foreach (var sign in new[] { 1, -1 })
                        await InsertAndRead(context, new Row { Id = sign == 1 ? 10 : 11,
                            Duration = (maximumDuration - TimeSpan.FromTicks(10)) * sign,
                            ConvertedDuration = new DurationValue((maximumDuration - TimeSpan.FromTicks(10)) * sign) }, async);
                }
                stage("tracked update through all temporal carriers");
                var tracked = async ? await context.Set<Row>().SingleAsync(row => row.Id == 2) : context.Set<Row>().Single(row => row.Id == 2);
                tracked.Stamp = SeedStamp;
                tracked.Day = SeedDay;
                tracked.Clock = SeedClock;
                tracked.Duration = SeedDuration;
                tracked.Zoned = SeedZoned;
                tracked.ConvertedStamp = new StampValue(SeedStamp);
                tracked.ConvertedDuration = new DurationValue(SeedDuration);
                Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
                context.ChangeTracker.Clear();
                AssertStored(tracked, async ? await context.Set<Row>().SingleAsync(row => row.Id == 2) : context.Set<Row>().Single(row => row.Id == 2), mode);
            });

        [Theory]
        [InlineData(OceanBaseCompatibilityMode.MySql, false)]
        [InlineData(OceanBaseCompatibilityMode.MySql, true)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, false)]
        [InlineData(OceanBaseCompatibilityMode.Oracle, true)]
        public async Task Temporal_precision_and_kind_rejections_preserve_entries_and_table(OceanBaseCompatibilityMode mode, bool async)
            => await WithTable(mode, async, async (context, observer, stage) =>
            {
                stage("explicit facets accept exact milliseconds");
                await InsertAndRead(context, new Row { Id = 2, FacetedStamp = SeedStamp, FacetedClock = SeedClock,
                    FacetedDuration = TimeSpan.FromHours(-1).Add(TimeSpan.FromMilliseconds(-123)), FacetedZoned = SeedZoned }, async);
                stage("invalid temporal writes reject before DML");
                var invalid = new[]
                {
                    new Row { Id = 99, Stamp = DateTime.SpecifyKind(SeedStamp, DateTimeKind.Utc) },
                    new Row { Id = 99, ConvertedStamp = new StampValue(DateTime.SpecifyKind(SeedStamp, DateTimeKind.Local)) },
                    new Row { Id = 99, FacetedStamp = SeedStamp.AddTicks(10) },
                    new Row { Id = 99, FacetedClock = SeedClock.Add(TimeSpan.FromTicks(10)) },
                    new Row { Id = 99, FacetedDuration = TimeSpan.FromTicks(-10) },
                    new Row { Id = 99, FacetedZoned = SeedZoned.AddTicks(10) }
                };
                if (mode == OceanBaseCompatibilityMode.MySql)
                    invalid = invalid.Concat(new[] { new Row { Id = 99, Stamp = SeedStamp.AddTicks(1) },
                        new Row { Id = 99, Duration = TimeSpan.FromHours(839) },
                        new Row { Id = 99, Duration = new TimeSpan(0, 838, 59, 59).Add(TimeSpan.FromTicks(10)) },
                        new Row { Id = 99, ConvertedDuration = new DurationValue(-(new TimeSpan(0, 838, 59, 59) + TimeSpan.FromTicks(10))) } }).ToArray();
                else invalid = invalid.Append(new Row { Id = 99, FacetedDuration = TimeSpan.FromDays(10) }).ToArray();
                foreach (var row in invalid)
                {
                    context.Add(row);
                    var writes = observer.ParameterWrites;
                    var error = async ? await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync())
                        : Assert.Throws<DbUpdateException>(() => context.SaveChanges());
                    Assert.IsAssignableFrom<ArgumentException>(error.InnerException);
                    Assert.Same(row, Assert.Single(error.Entries).Entity);
                    Assert.Equal(writes, observer.ParameterWrites);
                    context.ChangeTracker.Clear();
                    Assert.False(async ? await context.Set<Row>().AnyAsync(row => row.Id == 99) : context.Set<Row>().Any(row => row.Id == 99));
                }
                await InsertAndRead(context, new Row { Id = 3, Stamp = SeedStamp }, async);
            });

        private static async Task InsertAndRead(TemporalContext context, Row row, bool async)
        {
            context.Add(row);
            Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
            Assert.Equal(SeedStamp, row.DefaultStamp);
            Assert.Equal(DateTimeKind.Unspecified, row.DefaultStamp.Kind);
            Assert.Equal(SeedDay, row.DefaultDay);
            Assert.Equal(SeedClock, row.DefaultClock);
            Assert.Equal(SeedDuration, row.DefaultDuration);
            Assert.Equal(SeedZoned.UtcTicks, row.DefaultZoned.UtcTicks);
            Assert.Equal(context.Mode == OceanBaseCompatibilityMode.MySql ? TimeSpan.Zero : SeedZoned.Offset, row.DefaultZoned.Offset);
            context.ChangeTracker.Clear();
            var query = context.Set<Row>().AsNoTracking().Where(value => value.Id == row.Id);
            AssertStored(row, Assert.Single(async ? await query.ToArrayAsync() : query.ToArray()), context.Mode);
        }

        private static void AssertStored(Row expected, Row actual, OceanBaseCompatibilityMode mode)
        {
            if (mode == OceanBaseCompatibilityMode.MySql)
                expected = expected with { Zoned = expected.Zoned?.ToUniversalTime(), FacetedZoned = expected.FacetedZoned?.ToUniversalTime(),
                    DefaultZoned = expected.DefaultZoned.ToUniversalTime() };
            Assert.Equal(expected, actual);
            Assert.Equal(expected.Zoned?.Offset, actual.Zoned?.Offset);
            Assert.Equal(expected.FacetedZoned?.Offset, actual.FacetedZoned?.Offset);
            Assert.Equal(expected.DefaultZoned.Offset, actual.DefaultZoned.Offset);
            foreach (var value in new[] { actual.Stamp, actual.OracleDate, actual.FacetedStamp, actual.ConvertedStamp?.Value, actual.DefaultStamp })
                if (value is { } stamp) Assert.Equal(DateTimeKind.Unspecified, stamp.Kind);
        }

        private static Task WithTable(OceanBaseCompatibilityMode mode, bool async,
            Func<TemporalContext, LiveStorageTable.Commands, Action<string>, Task> run)
            => LiveStorageTable.WithTable<TemporalContext>(mode, async, false,
                (options, table) => new TemporalContext(new DbContextOptionsBuilder<TemporalContext>(options)
                    .ReplaceService<IModelCacheKeyFactory, CacheKey>().Options, mode, table), async (context, observer, stage) =>
                {
                    stage("temporal HasData readback");
                    var seed = async ? await context.Set<Row>().SingleAsync() : context.Set<Row>().Single();
                    AssertStored(Seed(), seed, mode);
                    context.ChangeTracker.Clear();
                    var sql = "DELETE FROM " + context.GetService<ISqlGenerationHelper>().DelimitIdentifier(context.Table);
                    if (async) await context.Database.ExecuteSqlRawAsync(sql);
                    else context.Database.ExecuteSqlRaw(sql);
                    await run(context, observer, stage);
                });

        private static Row Seed() => new() { Id = 1, Stamp = SeedStamp, Zoned = SeedZoned, Day = SeedDay, Clock = SeedClock,
            Duration = SeedDuration, ConvertedStamp = new StampValue(SeedStamp), ConvertedDuration = new DurationValue(SeedDuration),
            DefaultStamp = SeedStamp, DefaultDay = SeedDay, DefaultClock = SeedClock, DefaultDuration = SeedDuration, DefaultZoned = SeedZoned };

        private sealed class TemporalContext(DbContextOptions<TemporalContext> options, OceanBaseCompatibilityMode mode, string table) : DbContext(options)
        {
            public OceanBaseCompatibilityMode Mode => mode;
            public string Table => table;
            protected override void OnModelCreating(ModelBuilder model)
            {
                var row = model.Entity<Row>();
                row.ToTable(table);
                row.Property(value => value.Id).ValueGeneratedNever();
                if (mode == OceanBaseCompatibilityMode.Oracle) row.Property(value => value.OracleDate).HasColumnType("DATE");
                else row.Ignore(value => value.OracleDate);
                row.Property(value => value.FacetedStamp).HasPrecision(3).IsConcurrencyToken();
                row.Property(value => value.FacetedClock).HasPrecision(3);
                row.Property(value => value.FacetedDuration).HasColumnType(mode == OceanBaseCompatibilityMode.MySql ? "TIME(3)" : "INTERVAL DAY(1) TO SECOND(3)");
                row.Property(value => value.FacetedZoned).HasPrecision(3);
                row.Property(value => value.ConvertedStamp).HasConversion(value => value!.Value, value => new StampValue(value));
                row.Property(value => value.ConvertedDuration).HasConversion(value => value!.Value, value => new DurationValue(value));
                row.Property(value => value.DefaultStamp).HasDefaultValue(SeedStamp);
                row.Property(value => value.DefaultDay).HasDefaultValue(SeedDay);
                row.Property(value => value.DefaultClock).HasDefaultValue(SeedClock);
                row.Property(value => value.DefaultDuration).HasDefaultValue(SeedDuration);
                row.Property(value => value.DefaultZoned).HasDefaultValue(SeedZoned);
                row.HasData(Seed());
            }
        }

        private sealed class CacheKey : IModelCacheKeyFactory
        {
            public object Create(DbContext context, bool designTime)
                => (context.GetType(), ((TemporalContext)context).Mode, ((TemporalContext)context).Table, designTime);
        }

        private sealed record StampValue(DateTime Value);
        private sealed record DurationValue(TimeSpan Value);
        private sealed record Row
        {
            public int Id { get; set; }
            public DateTime? Stamp { get; set; }
            public DateTime? OracleDate { get; set; }
            public DateTimeOffset? Zoned { get; set; }
            public DateOnly? Day { get; set; }
            public TimeOnly? Clock { get; set; }
            public TimeSpan? Duration { get; set; }
            public DateTime? FacetedStamp { get; set; }
            public DateTimeOffset? FacetedZoned { get; set; }
            public TimeOnly? FacetedClock { get; set; }
            public TimeSpan? FacetedDuration { get; set; }
            public StampValue? ConvertedStamp { get; set; }
            public DurationValue? ConvertedDuration { get; set; }
            public DateTime DefaultStamp { get; set; }
            public DateTimeOffset DefaultZoned { get; set; }
            public DateOnly DefaultDay { get; set; }
            public TimeOnly DefaultClock { get; set; }
            public TimeSpan DefaultDuration { get; set; }
        }
    }
}
