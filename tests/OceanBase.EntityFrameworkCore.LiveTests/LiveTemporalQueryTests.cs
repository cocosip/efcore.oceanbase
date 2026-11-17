using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace OceanBase.EntityFrameworkCore.LiveTests
{
    public sealed class LiveTemporalQueryTests
    {
        public static IEnumerable<object[]> Cases()
        {
            foreach (var mode in Enum.GetValues<OceanBaseCompatibilityMode>())
            foreach (var async in new[] { false, true })
            foreach (var nulls in new[] { false, true }) yield return [mode, async, nulls];
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public Task Calendar_and_fractional_members_preserve_native_values_and_NULLs(
            OceanBaseCompatibilityMode mode, bool async, bool nulls)
            => WithTable(mode, async, nulls, async (context, stage) =>
            {
                foreach (var field in new[] { nameof(Row.Stamp), nameof(Row.Day), nameof(Row.Clock), nameof(Row.Zoned), nameof(Row.DateStamp) })
                {
                    var type = Nullable.GetUnderlyingType(typeof(Row).GetProperty(field)!.PropertyType)!;
                    var names = type == typeof(DateOnly)
                        ? new[] { "Year", "Month", "Day", "DayOfYear", "DayOfWeek", "DayNumber" }
                        : type == typeof(TimeOnly)
                            ? ["Hour", "Minute", "Second", "Millisecond", "Microsecond", "Nanosecond", "Ticks"]
                            : new[] { "Year", "Month", "Day", "DayOfYear", "DayOfWeek", "Hour", "Minute", "Second", "Millisecond", "Microsecond", "Nanosecond" };
                    foreach (var name in names)
                    {
                        stage(field + "/" + name);
                        await CheckMember(context, async, field, name);
                    }
                }
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public Task Date_time_of_day_epoch_and_UTC_members_preserve_fractional_results(
            OceanBaseCompatibilityMode mode, bool async, bool nulls)
            => WithTable(mode, async, nulls, async (context, stage) =>
            {
                foreach (var field in new[] { nameof(Row.Stamp), nameof(Row.DateStamp), nameof(Row.Zoned) })
                foreach (var name in field == nameof(Row.Zoned) ? new[] { "Date", "TimeOfDay", "UtcDateTime", "UtcTicks" }
                    : ["Date", "TimeOfDay", "Ticks"])
                {
                    stage(field + "/" + name);
                    await CheckMember(context, async, field, name);
                }
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public Task Signed_duration_components_ticks_and_totals_match_CLR(
            OceanBaseCompatibilityMode mode, bool async, bool nulls)
            => WithTable(mode, async, nulls, async (context, stage) =>
            {
                foreach (var name in new[] { "Days", "Hours", "Minutes", "Seconds", "Milliseconds", "Microseconds", "Nanoseconds", "Ticks",
                    "TotalDays", "TotalHours", "TotalMinutes", "TotalSeconds", "TotalMilliseconds", "TotalMicroseconds", "TotalNanoseconds" })
                {
                    stage("Duration/" + name);
                    await CheckMember(context, async, nameof(Row.Duration), name);
                }
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public Task Calendar_addition_clamps_month_ends_and_preserves_time_with_refreshed_amounts(
            OceanBaseCompatibilityMode mode, bool async, bool nulls)
            => WithTable(mode, async, nulls, async (context, stage) =>
            {
                foreach (var amount in new[] { -13, -1, 0, 1, 13 })
                {
                    stage("captured calendar amount=" + amount);
                    await Check(context, async, row => (DateOnly?)row.Day!.Value.AddDays(amount), row => row.Day?.AddDays(amount));
                    await Check(context, async, row => (DateOnly?)row.Day!.Value.AddMonths(amount), row => row.Day?.AddMonths(amount));
                    await Check(context, async, row => (DateOnly?)row.Day!.Value.AddYears(amount), row => row.Day?.AddYears(amount));
                    await Check(context, async, row => (DateTime?)row.Stamp!.Value.AddMonths(amount), row => row.Stamp?.AddMonths(amount));
                    await Check(context, async, row => (DateTime?)row.Stamp!.Value.AddYears(amount), row => row.Stamp?.AddYears(amount));
                    await Check(context, async, row => (DateTime?)row.DateStamp!.Value.AddMonths(amount), row => row.DateStamp?.AddMonths(amount));
                    await Check(context, async, row => (long?)row.DateStamp!.Value.AddYears(amount).Ticks, row => row.DateStamp?.AddYears(amount).Ticks);
                }
                stage("column and compound calendar amounts");
                await Check(context, async, row => (DateOnly?)row.Day!.Value.AddMonths(row.Amount), row => row.Day?.AddMonths(row.Amount));
                await Check(context, async, row => (DateOnly?)row.Day!.Value.AddYears(row.Amount + 1), row => row.Day?.AddYears(row.Amount + 1));
                await Check(context, async, row => (DateTime?)row.Stamp!.Value.AddYears(row.Amount + 1), row => row.Stamp?.AddYears(row.Amount + 1));
                await Check(context, async, row => (TimeSpan?)row.Stamp!.Value.AddMonths(row.Amount).TimeOfDay, row => row.Stamp?.AddMonths(row.Amount).TimeOfDay);
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public Task Epoch_clock_and_duration_endpoints_keep_exact_tick_results(
            OceanBaseCompatibilityMode mode, bool async, bool nulls)
            => WithTable(mode, async, nulls, async (context, stage) =>
            {
                var maximum = mode == OceanBaseCompatibilityMode.MySql ? DateTime.MaxValue.AddTicks(-9) : DateTime.MaxValue;
                var durationMaximum = mode == OceanBaseCompatibilityMode.MySql ? new TimeSpan(34, 22, 59, 59) : TimeSpan.MaxValue;
                var durationMinimum = mode == OceanBaseCompatibilityMode.MySql ? durationMaximum.Negate() : TimeSpan.MinValue;
                var declarations = new (string Field, object[] Values, string[] Members)[]
                {
                    (nameof(Row.Stamp), [DateTime.MinValue, maximum], ["Year", "DayOfYear", "DayOfWeek", "Ticks", "TimeOfDay", "Nanosecond"]),
                    (nameof(Row.DateStamp), [DateTime.MinValue, new DateTime(9999, 12, 31, 23, 59, 59)], ["Ticks", "TimeOfDay", "Date", "Second"]),
                    (nameof(Row.Day), [DateOnly.MinValue, DateOnly.MaxValue], ["DayNumber", "DayOfYear", "DayOfWeek"]),
                    (nameof(Row.Clock), [TimeOnly.MinValue, TimeOnly.FromDateTime(maximum)], ["Ticks", "Second", "Microsecond", "Nanosecond"]),
                    (nameof(Row.Duration), [durationMinimum, durationMaximum], ["Days", "Seconds", "Nanoseconds", "Ticks", "TotalDays", "TotalNanoseconds"]),
                    (nameof(Row.Zoned), [DateTimeOffset.MinValue, new DateTimeOffset(maximum, TimeSpan.Zero)], ["UtcTicks", "UtcDateTime", "TimeOfDay"])
                };
                var helper = context.GetService<ISqlGenerationHelper>();
                var seeds = Seed(mode);
                foreach (var (field, values, members) in declarations)
                foreach (var value in values)
                {
                    stage("endpoint " + field + "/" + value);
                    var mapping = context.Model.FindEntityType(typeof(Row))!.FindProperty(field)!.GetRelationalTypeMapping();
                    var sql = "UPDATE " + helper.DelimitIdentifier(context.Table) + " SET " + helper.DelimitIdentifier(field)
                        + " = " + mapping.GenerateSqlLiteral(value) + " WHERE " + helper.DelimitIdentifier(nameof(Row.Id)) + " = 1";
                    Assert.Equal(1, async ? await context.Database.ExecuteSqlRawAsync(sql) : context.Database.ExecuteSqlRaw(sql));
                    typeof(Row).GetProperty(field)!.SetValue(seeds[0], value);
                    foreach (var member in members) await CheckMember(context, async, field, member, seeds);
                }
            });

        private static Task CheckMember(Context context, bool async, string field, string member, Row[]? seeds = null)
        {
            var receiver = Nullable.GetUnderlyingType(typeof(Row).GetProperty(field)!.PropertyType)!;
            var property = receiver.GetProperty(member)!;
            return (Task)typeof(LiveTemporalQueryTests).GetMethod(nameof(CheckMemberResult), BindingFlags.Static | BindingFlags.NonPublic)!
                .MakeGenericMethod(property.PropertyType).Invoke(null, [context, async, field, property, seeds])!;
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public Task Nullable_temporal_converters_preserve_stored_space_and_result_ownership(
            OceanBaseCompatibilityMode mode, bool async, bool nulls)
            => WithTable(mode, async, nulls, async (context, stage) =>
            {
                stage("nullable DateTime converter components and mapped results");
                await Check(context, async, row => (int?)row.ConvertedStamp!.Value.Year, row => row.ConvertedStamp?.AddDays(7).Year);
                await Check(context, async, row => (long?)row.ConvertedStamp!.Value.Ticks, row => row.ConvertedStamp?.AddDays(7).Ticks);
                await Check(context, async, row => (DateTime?)row.ConvertedStamp!.Value.Date, row => row.ConvertedStamp?.AddDays(7).Date.AddDays(-7));
                await Check(context, async, row => (TimeSpan?)row.ConvertedStamp!.Value.TimeOfDay, row => row.ConvertedStamp?.AddDays(7).TimeOfDay);
                await Check(context, async, row => (DateTime?)row.ConvertedStamp!.Value.AddMonths(row.Amount), row => row.ConvertedStamp?.AddDays(7).AddMonths(row.Amount).AddDays(-7));
                stage("nullable DateOnly converter calendar and day number");
                await Check(context, async, row => (int?)row.ConvertedDay!.Value.DayNumber, row => row.ConvertedDay?.AddDays(2).DayNumber);
                await Check(context, async, row => (DateOnly?)row.ConvertedDay!.Value.AddMonths(row.Amount), row => row.ConvertedDay?.AddDays(2).AddMonths(row.Amount).AddDays(-2));
                stage("nullable TimeSpan converter components ticks and totals");
                await Check(context, async, row => (int?)row.ConvertedDuration!.Value.Seconds, row => row.ConvertedDuration?.Negate().Seconds);
                await Check(context, async, row => (long?)row.ConvertedDuration!.Value.Ticks, row => row.ConvertedDuration?.Negate().Ticks);
                await Check(context, async, row => (double?)row.ConvertedDuration!.Value.TotalDays, row => row.ConvertedDuration?.Negate().TotalDays);
                stage("nullable TimeOnly and DateTimeOffset converter carriers");
                await Check(context, async, row => (int?)row.ConvertedClock!.Value.Hour, row => row.ConvertedClock?.AddHours(1).Hour);
                await Check(context, async, row => (long?)row.ConvertedClock!.Value.Ticks, row => row.ConvertedClock?.AddHours(1).Ticks);
                await Check(context, async, row => (int?)row.ConvertedZoned!.Value.Hour, row => row.ConvertedZoned?.AddMinutes(30).Hour);
                await Check(context, async, row => (long?)row.ConvertedZoned!.Value.UtcTicks, row => row.ConvertedZoned?.AddMinutes(30).UtcTicks);
                await Check(context, async, row => (TimeSpan?)row.ConvertedZoned!.Value.TimeOfDay, row => row.ConvertedZoned?.AddMinutes(30).TimeOfDay);
                if (mode == OceanBaseCompatibilityMode.Oracle)
                {
                    await Check(context, async, row => (int?)row.ConvertedZoned!.Value.TotalOffsetMinutes, row => row.ConvertedZoned?.AddMinutes(30).TotalOffsetMinutes);
                    await Check(context, async, row => (TimeSpan?)row.ConvertedZoned!.Value.Offset, row => row.ConvertedZoned?.AddMinutes(30).Offset);
                }
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public Task Numeric_session_offsets_keep_local_and_UTC_members_separate(
            OceanBaseCompatibilityMode mode, bool async, bool nulls)
            => WithTable(mode, async, nulls, async (context, stage) =>
            {
                var original = Assert.Single(await Read(context.Database.SqlQueryRaw<string>(mode == OceanBaseCompatibilityMode.MySql
                    ? "SELECT @@session.time_zone AS `Value`" : "SELECT SESSIONTIMEZONE AS \"Value\" FROM DUAL"), async));
                try
                {
                    foreach (var zone in new[] { "+05:45", "-03:30" })
                    {
                        stage("session offset=" + zone);
                        await SetZone(context, async, zone);
                        foreach (var member in new[] { "Year", "Month", "Day", "Hour", "Minute", "Second", "Date", "TimeOfDay", "UtcDateTime", "UtcTicks", "DayOfYear", "DayOfWeek" })
                            await CheckMember(context, async, nameof(Row.Zoned), member);
                        if (mode == OceanBaseCompatibilityMode.Oracle)
                            foreach (var member in new[] { "Offset", "TotalOffsetMinutes", "Ticks", "DateTime" })
                            {
                                stage("session offset=" + zone + "/Zoned." + member);
                                await CheckMember(context, async, nameof(Row.Zoned), member);
                            }
                    }
                }
                finally { await SetZone(context, async, original); }
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public Task Server_clocks_stay_inside_same_session_server_brackets(
            OceanBaseCompatibilityMode mode, bool async, bool nulls)
            => WithTable(mode, async, nulls, async (context, stage) =>
            {
                var original = Assert.Single(await Read(context.Database.SqlQueryRaw<string>(mode == OceanBaseCompatibilityMode.MySql
                    ? "SELECT @@session.time_zone AS `Value`" : "SELECT SESSIONTIMEZONE AS \"Value\" FROM DUAL"), async));
                try
                {
                    foreach (var zone in new[] { "+05:45", "-03:30" })
                    {
                        stage("server clocks session offset=" + zone);
                        await SetZone(context, async, zone);
                        var beforeLocal = await Clock(false);
                        var beforeUtc = await Clock(true);
                        var query = context.Set<Row>().Where(row => row.Id == 1 && DateTime.UtcNow > new DateTime(1900, 1, 1))
                            .Select(row => new { Now = DateTime.Now, Utc = DateTime.UtcNow, Today = DateTime.Today, OffsetUtc = DateTimeOffset.UtcNow });
                        var sql = query.ToQueryString();
                        Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "CURRENT_TIMESTAMP(6)" : "CURRENT_TIMESTAMP AS TIMESTAMP", sql);
                        var value = Assert.Single(await Read(query, async));
                        var afterUtc = await Clock(true);
                        var afterLocal = await Clock(false);
                        Assert.InRange(value.Now, beforeLocal, afterLocal);
                        Assert.InRange(value.Utc, beforeUtc, afterUtc);
                        Assert.InRange(value.OffsetUtc.UtcDateTime.Ticks, beforeUtc.Ticks, afterUtc.Ticks);
                        Assert.Equal(TimeSpan.Zero, value.OffsetUtc.Offset);
                        Assert.InRange(value.Today, beforeLocal.Date, afterLocal.Date);
                        Assert.Equal(TimeSpan.Zero, value.Today.TimeOfDay);
                        Assert.Equal(DateTimeKind.Unspecified, value.Now.Kind);
                        Assert.Equal(DateTimeKind.Unspecified, value.Utc.Kind);
                    }
                }
                finally { await SetZone(context, async, original); }

                async Task<DateTime> Clock(bool utc)
                {
                    var expression = mode == OceanBaseCompatibilityMode.MySql ? utc ? "UTC_TIMESTAMP(6)" : "CURRENT_TIMESTAMP(6)"
                        : utc ? "CAST(SYS_EXTRACT_UTC(SYSTIMESTAMP) AS TIMESTAMP(7))" : "CAST(CURRENT_TIMESTAMP AS TIMESTAMP(7))";
                    var sql = "SELECT " + expression + (mode == OceanBaseCompatibilityMode.MySql ? " AS `Value`" : " AS \"Value\" FROM DUAL");
                    return Assert.Single(await Read(context.Database.SqlQueryRaw<DateTime>(sql), async));
                }
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public Task Conditional_coalesce_and_unsupported_overloads_keep_server_and_recovery_boundaries(
            OceanBaseCompatibilityMode mode, bool async, bool nulls)
            => WithTable(mode, async, nulls, async (context, stage) =>
            {
                stage("temporal conditional and coalesce results");
                await Check(context, async, row => (int?)(row.Stamp.HasValue ? row.Stamp.Value.Year : -1), row => row.Stamp?.Year ?? -1);
                await Check(context, async, row => (long?)((row.Duration ?? TimeSpan.Zero).Ticks), row => (row.Duration ?? TimeSpan.Zero).Ticks);
                await Check(context, async, row => (DateOnly?)(row.Day ?? new DateOnly(2024, 1, 1)).AddMonths(1), row => (row.Day ?? new DateOnly(2024, 1, 1)).AddMonths(1));
                stage("unsupported temporal predicates reject before ADO");
                var rows = context.Set<Row>();
                IQueryable[] rejected = [rows.Where(row => row.Stamp!.Value.AddDays(1) > new DateTime(2000, 1, 1)),
                    rows.Where(row => row.Clock!.Value.AddHours(1) > TimeOnly.MinValue),
                    rows.Where(row => DateTimeOffset.Now > row.Zoned)];
                if (mode == OceanBaseCompatibilityMode.MySql)
                    rejected = rejected.Concat(new IQueryable[] { rows.Where(row => row.Zoned!.Value.Offset > TimeSpan.Zero),
                        rows.Where(row => row.Zoned!.Value.TotalOffsetMinutes > 0), rows.Where(row => row.Zoned!.Value.Ticks > 0),
                        rows.Where(row => row.Zoned!.Value.DateTime > new DateTime(2000, 1, 1)) }).ToArray();
                foreach (var query in rejected) Assert.Throws<InvalidOperationException>(() => query.ToQueryString());
                Assert.Equal(Seed(mode).Length, async ? await rows.CountAsync() : rows.Count());
            });

        private static Task SetZone(Context context, bool async, string zone)
        {
            var sql = (context.Mode == OceanBaseCompatibilityMode.MySql ? "SET SESSION time_zone = '" : "ALTER SESSION SET TIME_ZONE = '")
                + zone.Replace("'", "''", StringComparison.Ordinal) + "'";
            return async ? context.Database.ExecuteSqlRawAsync(sql) : Task.FromResult(context.Database.ExecuteSqlRaw(sql));
        }

        private static Task CheckMemberResult<T>(Context context, bool async, string field, PropertyInfo member, Row[]? seeds) where T : struct
        {
            var row = Expression.Parameter(typeof(Row), "row");
            var body = Expression.Property(Expression.Property(Expression.Property(row, field), "Value"), member);
            var projection = Expression.Lambda<Func<Row, T?>>(Expression.Convert(body, typeof(T?)), row);
            return Check(context, async, projection, value => typeof(Row).GetProperty(field)!.GetValue(value) is { } input
                ? (T?)member.GetValue(input) : null, seeds);
        }

        private static async Task Check<T>(Context context, bool async, Expression<Func<Row, T?>> projection, Func<Row, T?> expected, Row[]? expectedRows = null) where T : struct
        {
            var seeds = expectedRows ?? Seed(context.Mode);
            var wanted = seeds.Select(expected).ToArray();
            var needle = wanted.First(value => value.HasValue);
            // Database DateTime literals are unzoned, including UTC-valued results.
            if (needle is DateTime date) needle = (T)(object)DateTime.SpecifyKind(date, DateTimeKind.Unspecified);
            // A predicate requires translation even where EF permits client evaluation of the final projection.
            var comparison = typeof(T) == typeof(double)
                ? Expression.GreaterThan(projection.Body, Expression.Constant((T?)Activator.CreateInstance(typeof(T)), typeof(T?)))
                : Expression.Equal(projection.Body, Expression.Constant(needle, typeof(T?)));
            var predicate = Expression.Lambda<Func<Row, bool>>(comparison, projection.Parameters);
            var matching = seeds.Where(row => typeof(T) == typeof(double)
                ? expected(row) is { } number && Convert.ToDouble(number) > 0 : Nullable.Equals(expected(row), needle)).Select(row => row.Id);
            var filtered = context.Set<Row>().Where(predicate).OrderBy(row => row.Id).Select(row => row.Id);
            var ids = await Read(filtered, async);
            Assert.True(matching.SequenceEqual(ids), $"{projection}: expected ids=[{string.Join(',', matching)}], actual=[{string.Join(',', ids)}]");
            var actual = await Read(context.Set<Row>().OrderBy(row => row.Id).Select(projection), async);
            Assert.Equal(wanted.Length, actual.Length);
            for (var index = 0; index < wanted.Length; index++)
            {
                if (typeof(T) == typeof(double) && wanted[index].HasValue && actual[index].HasValue)
                {
                    var target = Convert.ToDouble(wanted[index]!.Value);
                    var observed = Convert.ToDouble(actual[index]!.Value);
                    Assert.True(double.IsFinite(observed) && Math.Abs(target - observed) <= Math.Max(Math.Abs(target) * 3e-15, 1e-15),
                        $"{projection} row={index + 1}: expected={target:R},actual={observed:R}");
                }
                else Assert.True(Nullable.Equals(wanted[index], actual[index]),
                    $"{projection} row={index + 1}: expected={wanted[index]},actual={actual[index]}");
                if (actual[index] is DateTime stamp) Assert.Equal(DateTimeKind.Unspecified, stamp.Kind);
            }
        }

        private static Task<T[]> Read<T>(IQueryable<T> query, bool async) => async ? query.ToArrayAsync() : Task.FromResult(query.ToArray());

        private static Task WithTable(OceanBaseCompatibilityMode mode, bool async, bool nulls, Func<Context, Action<string>, Task> run)
            => LiveStorageTable.WithTable<Context>(mode, async, false, (options, table) =>
            {
                var builder = new DbContextOptionsBuilder<Context>(options).UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
                    .EnableServiceProviderCaching(false).ReplaceService<IModelCacheKeyFactory, CacheKey>();
                ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(
                    builder.Options.Extensions.OfType<RelationalOptionsExtension>().Single().WithUseRelationalNulls(nulls));
                return new Context(builder.Options, mode, table);
            }, async (context, _, stage) =>
            {
                if (async) await context.Database.OpenConnectionAsync();
                else context.Database.OpenConnection();
                try
                {
                    stage("verify temporal seeds");
                    Assert.Equal(Seed(mode), await Read(context.Set<Row>().OrderBy(row => row.Id), async));
                    if (mode == OceanBaseCompatibilityMode.Oracle)
                        Assert.Equal("GREGORIAN", Assert.Single(await Read(context.Database.SqlQueryRaw<string>(
                            "SELECT VALUE AS \"Value\" FROM NLS_SESSION_PARAMETERS WHERE PARAMETER = 'NLS_CALENDAR'"), async)));
                    await run(context, stage);
                }
                finally
                {
                    if (async) await context.Database.CloseConnectionAsync();
                    else context.Database.CloseConnection();
                }
            });

        private static Row[] Seed(OceanBaseCompatibilityMode mode)
        {
            DateTime?[] stamps = [null, new(1900, 2, 28, 0, 0, 0), new(2000, 2, 29, 0, 0, 0),
                new(2024, 1, 31, 23, 59, 59), new(2024, 3, 31, 0, 0, 0), new(2025, 1, 1, 0, 0, 0),
                new(2024, 7, 1, 0, 0, 0), new(2024, 7, 1, 0, 0, 0)];
            long[] fractions = [0, 0, 1234560, 9999990, 10, 7654320, 2345670, 3456780];
            if (mode == OceanBaseCompatibilityMode.Oracle) { fractions[2] += 7; fractions[3] += 9; fractions[5] += 1; }
            var durations = new[] { (TimeSpan?)null, TimeSpan.Zero, TimeSpan.FromTicks(mode == OceanBaseCompatibilityMode.Oracle ? -1 : -10),
                -new TimeSpan(2, 3, 4, 5).Add(TimeSpan.FromTicks(1234560)),
                new TimeSpan(34, 22, 59, 59), new TimeSpan(2, 3, 4, 5).Add(TimeSpan.FromTicks(9999990)),
                TimeSpan.FromTicks(10), -TimeSpan.FromHours(1).Add(TimeSpan.FromTicks(2345670)) };
            var offsets = new[] { 0, 0, 345, -210, -720, 840, -30, 30 };
            return stamps.Select((stamp, index) =>
            {
                stamp = stamp?.AddTicks(fractions[index]);
                DateTimeOffset? zoned = stamp.HasValue ? new DateTimeOffset(stamp.Value, TimeSpan.FromMinutes(offsets[index])) : null;
                if (mode == OceanBaseCompatibilityMode.MySql) zoned = zoned?.ToUniversalTime();
                return new Row { Id = index + 1, Stamp = stamp, Day = stamp.HasValue ? DateOnly.FromDateTime(stamp.Value) : null,
                    Clock = stamp.HasValue ? TimeOnly.FromDateTime(stamp.Value) : null, Duration = durations[index], Zoned = zoned,
                    DateStamp = stamp.HasValue ? new DateTime(stamp.Value.Ticks - stamp.Value.Ticks % TimeSpan.TicksPerSecond) : null,
                    ConvertedStamp = stamp, ConvertedDay = stamp.HasValue ? DateOnly.FromDateTime(stamp.Value) : null,
                    ConvertedDuration = durations[index],
                    ConvertedClock = stamp.HasValue ? TimeOnly.FromDateTime(stamp.Value) : null, ConvertedZoned = zoned,
                    Amount = index % 3 - 1 };
            }).ToArray();
        }

        private sealed class Context(DbContextOptions<Context> options, OceanBaseCompatibilityMode mode, string table) : DbContext(options)
        {
            public string Table => table;
            public OceanBaseCompatibilityMode Mode => mode;
            protected override void OnModelCreating(ModelBuilder model)
            {
                var row = model.Entity<Row>();
                row.ToTable(table);
                row.Property(value => value.Id).ValueGeneratedNever();
                if (mode == OceanBaseCompatibilityMode.Oracle) row.Property(value => value.DateStamp).HasColumnType("DATE");
                row.Property(value => value.ConvertedStamp).HasConversion(value => value.HasValue ? value.Value.AddDays(7) : value,
                    value => value.HasValue ? value.Value.AddDays(-7) : value);
                row.Property(value => value.ConvertedDay).HasConversion(value => value.HasValue ? value.Value.AddDays(2) : value,
                    value => value.HasValue ? value.Value.AddDays(-2) : value);
                row.Property(value => value.ConvertedDuration).HasConversion(value => value.HasValue ? value.Value.Negate() : value,
                    value => value.HasValue ? value.Value.Negate() : value);
                row.Property(value => value.ConvertedClock).HasConversion(value => value.HasValue ? value.Value.AddHours(1) : value,
                    value => value.HasValue ? value.Value.AddHours(-1) : value);
                row.Property(value => value.ConvertedZoned).HasConversion(value => value.HasValue ? value.Value.AddMinutes(30) : value,
                    value => value.HasValue ? value.Value.AddMinutes(-30) : value);
                row.HasData(Seed(mode));
            }
        }

        private sealed class CacheKey : IModelCacheKeyFactory
        {
            public object Create(DbContext context, bool designTime) => (context.GetType(), ((Context)context).Mode, ((Context)context).Table, designTime);
        }

        private sealed record Row
        {
            public int Id { get; set; }
            public int Amount { get; set; }
            public DateTime? Stamp { get; set; }
            public DateTime? DateStamp { get; set; }
            public DateOnly? Day { get; set; }
            public TimeOnly? Clock { get; set; }
            public TimeSpan? Duration { get; set; }
            public DateTimeOffset? Zoned { get; set; }
            public DateTime? ConvertedStamp { get; set; }
            public DateOnly? ConvertedDay { get; set; }
            public TimeSpan? ConvertedDuration { get; set; }
            public TimeOnly? ConvertedClock { get; set; }
            public DateTimeOffset? ConvertedZoned { get; set; }
        }
    }
}
