using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBase.EntityFrameworkCore.TestUtilities;
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
                            .Select(row => new { Now = DateTime.Now, Utc = DateTime.UtcNow, Today = DateTime.Today,
                                OffsetUtc = DateTimeOffset.UtcNow, AdjustedOffsetUtc = (DateTimeOffset?)DateTimeOffset.UtcNow.AddTicks(10) });
                        var sql = query.ToQueryString();
                        Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "CURRENT_TIMESTAMP(6)" : "CURRENT_TIMESTAMP AS TIMESTAMP", sql);
                        var value = Assert.Single(await Read(query, async));
                        var afterUtc = await Clock(true);
                        var afterLocal = await Clock(false);
                        Assert.InRange(value.Now, beforeLocal, afterLocal);
                        Assert.InRange(value.Utc, beforeUtc, afterUtc);
                        Assert.InRange(value.OffsetUtc.UtcDateTime.Ticks, beforeUtc.Ticks, afterUtc.Ticks);
                        Assert.Equal(TimeSpan.Zero, value.OffsetUtc.Offset);
                        Assert.NotNull(value.AdjustedOffsetUtc);
                        Assert.Equal(TimeSpan.Zero, value.AdjustedOffsetUtc.Value.Offset);
                        Assert.InRange(value.AdjustedOffsetUtc.Value.UtcTicks - 10, beforeUtc.Ticks, afterUtc.Ticks);
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
                var rows = context.Set<Row>();
                stage("supported temporal arithmetic in predicates");
                var day = new DateTime(2000, 1, 1);
                await CheckPredicate(rows.Where(row => row.Stamp!.Value.AddDays(1) > day),
                    Seed(mode).Where(row => row.Stamp?.AddDays(1) > day));
                await CheckPredicate(rows.Where(row => row.Clock!.Value.AddHours(1) > TimeOnly.MinValue),
                    Seed(mode).Where(row => row.Clock?.AddHours(1) > TimeOnly.MinValue));

                stage("unsupported temporal predicates reject before ADO");
                IQueryable[] rejected = [rows.Where(row => DateTimeOffset.Now > row.Zoned)];
                if (mode == OceanBaseCompatibilityMode.MySql)
                    rejected = rejected.Concat(new IQueryable[] { rows.Where(row => row.Zoned!.Value.Offset > TimeSpan.Zero),
                        rows.Where(row => row.Zoned!.Value.TotalOffsetMinutes > 0), rows.Where(row => row.Zoned!.Value.Ticks > 0),
                        rows.Where(row => row.Zoned!.Value.DateTime > new DateTime(2000, 1, 1)) }).ToArray();
                foreach (var query in rejected) Assert.Throws<InvalidOperationException>(() => query.ToQueryString());
                Assert.Equal(Seed(mode).Length, async ? await rows.CountAsync() : rows.Count());

                async Task CheckPredicate(IQueryable<Row> query, IEnumerable<Row> expected)
                {
                    var ids = query.OrderBy(row => row.Id).Select(row => row.Id);
                    Assert.Equal(expected.OrderBy(row => row.Id).Select(row => row.Id),
                        async ? await ids.ToListAsync() : ids.ToList());
                }
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public Task DateTime_duration_arithmetic_preserves_fractional_results_and_NULLs(
            OceanBaseCompatibilityMode mode, bool async, bool nulls)
            => WithTable(mode, async, nulls, async (context, stage) =>
            {
                stage("DateTime Add/Subtract duration column and DATE promotion");
                await Check(context, async, row => (DateTime?)row.Stamp!.Value.Add(row.Duration!.Value),
                    row => row.Stamp.HasValue && row.Duration.HasValue ? row.Stamp.Value.Add(row.Duration.Value) : null);
                await Check(context, async, row => (DateTime?)row.Stamp!.Value.Subtract(row.Duration!.Value),
                    row => row.Stamp.HasValue && row.Duration.HasValue ? row.Stamp.Value.Subtract(row.Duration.Value) : null);
                await Check(context, async, row => (DateTime?)row.DateStamp!.Value.Add(row.Duration!.Value),
                    row => row.DateStamp.HasValue && row.Duration.HasValue ? row.DateStamp.Value.Add(row.Duration.Value) : null);
                await Check(context, async, row => (DateTime?)row.DateStamp!.Value.Subtract(row.Duration!.Value),
                    row => row.DateStamp.HasValue && row.Duration.HasValue ? row.DateStamp.Value.Subtract(row.Duration.Value) : null);
                await Check(context, async, row => (DateTime?)row.Stamp!.Value.Add((row.Id == 2 ? (TimeSpan?)null : row.Duration)!.Value),
                    row => row.Id != 2 && row.Stamp.HasValue && row.Duration.HasValue ? row.Stamp.Value.Add(row.Duration.Value) : null);
                var tick = mode == OceanBaseCompatibilityMode.MySql ? 10 : 1;
                var amount = new TimeSpan(1, 2, 3, 4).Add(TimeSpan.FromTicks(tick));
                var query = context.Set<Row>().Where(row => row.Stamp!.Value.Add(amount) > new DateTime(2000, 1, 1))
                    .OrderBy(row => row.Id).Select(row => (DateTime?)row.Stamp!.Value.Add(amount));
                string? initialSql = null;
                foreach (var value in new[] { amount, -amount, TimeSpan.Zero, TimeSpan.FromTicks(-tick) })
                {
                    stage("DateTime duration parameter refresh");
                    amount = value;
                    var sql = query.ToQueryString();
                    // ToQueryString parameter declarations contain the current values.
                    var body = sql[sql.IndexOf("SELECT", StringComparison.Ordinal)..];
                    if (initialSql is not null) Assert.Equal(initialSql, body);
                    initialSql = body;
                    Assert.Equal(Seed(mode).Where(row => row.Stamp?.Add(amount) > new DateTime(2000, 1, 1))
                        .Select(row => row.Stamp?.Add(amount)), await Read(query, async));
                    await Check(context, async, row => (DateTime?)row.Stamp!.Value.Subtract(amount), row => row.Stamp?.Subtract(amount));
                    await Check(context, async, row => (int?)row.DateStamp!.Value.Add(amount).Nanosecond,
                        row => row.DateStamp?.Add(amount).Nanosecond);
                }
                stage("DateTime converted carrier result ownership");
                await Check(context, async, row => (DateTime?)row.ConvertedStamp!.Value.Add(amount), row => row.ConvertedStamp?.Add(amount));
                await Check(context, async, row => (DateTime?)row.ConvertedStamp!.Value.Subtract(amount), row => row.ConvertedStamp?.Subtract(amount));
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public Task DateTime_numeric_day_hour_addition_matches_CLR_without_TIME_range_limits(
            OceanBaseCompatibilityMode mode, bool async, bool nulls)
            => WithTable(mode, async, nulls, async (context, stage) =>
            {
                foreach (var amount in new[] { 0.0, 0.5, -1.25, 40.25, -40.25, 900.5 })
                {
                    stage("numeric days/hours parameter and promoted DATE");
                    await Check(context, async, row => (DateTime?)row.Stamp!.Value.AddDays(amount), row => row.Stamp?.AddDays(amount));
                    await Check(context, async, row => (DateTime?)row.DateStamp!.Value.AddHours(amount), row => row.DateStamp?.AddHours(amount));
                    await Check(context, async, row => (long?)row.Stamp!.Value.AddHours(amount).Ticks, row => row.Stamp?.AddHours(amount).Ticks);
                }
                stage("numeric days/hours column, constant, NULL and converter composition");
                await Check(context, async, row => (DateTime?)row.Stamp!.Value.AddDays(row.Amount + 0.25), row => row.Stamp?.AddDays(row.Amount + 0.25));
                await Check(context, async, row => (DateTime?)row.Stamp!.Value.AddHours(row.Amount + 0.5).AddDays(-0.25),
                    row => row.Stamp?.AddHours(row.Amount + 0.5).AddDays(-0.25));
                await Check(context, async, row => (DateTime?)row.ConvertedStamp!.Value.AddDays(0.25), row => row.ConvertedStamp?.AddDays(0.25));
                var tickHour = 1.0 / TimeSpan.TicksPerHour;
                var query = context.Set<Row>().Where(row => row.Id == 2)
                    .Select(row => (DateTime?)row.DateStamp!.Value.AddHours(tickHour));
                stage("numeric sub-microsecond precision policy");
                Assert.Equal(mode == OceanBaseCompatibilityMode.MySql ? null : Seed(mode)[1].DateStamp?.AddHours(tickHour),
                    Assert.Single(await Read(query, async)));
                var anchor = new DateTime(5000, 1, 1);
                stage("native Double column with fractional and large unit offsets");
                await Check(context, async, row => (DateTime?)anchor.AddDays(row.NumericAmount!.Value),
                    row => ExpectedNumeric(row.NumericAmount, hours: false));
                await Check(context, async, row => (DateTime?)anchor.AddHours(row.NumericAmount!.Value),
                    row => ExpectedNumeric(row.NumericAmount, hours: true));
                foreach (var offset in new[] { 1000000.0000000001, -1000000.0000000001 })
                {
                    stage("large day offset retains separate fractional ticks");
                    var large = context.Set<Row>().Where(row => row.Id == 2)
                        .Select(row => (DateTime?)anchor.AddDays(offset + row.Amount));
                    Assert.Contains(mode == OceanBaseCompatibilityMode.MySql ? "TIMESTAMPADD" : "NUMTODSINTERVAL", large.ToQueryString());
                    Assert.Equal(anchor.AddDays(offset), Assert.Single(await Read(large, async)));
                }
                foreach (var ticks in new[] { -11, -10, -9, -1, 1, 9, 10, 11 })
                {
                    stage("signed tick precision and parameter refresh");
                    var offset = ticks / (double)TimeSpan.TicksPerHour;
                    var wanted = anchor.AddHours(offset);
                    var subsecond = context.Set<Row>().Where(row => row.Id == 2)
                        .Select(row => (DateTime?)anchor.AddHours(offset + row.Amount));
                    Assert.Equal(mode == OceanBaseCompatibilityMode.MySql && (wanted - anchor).Ticks % 10 != 0 ? null : (DateTime?)wanted,
                        Assert.Single(await Read(subsecond, async)));
                }
                stage("offsets outside CLR unit range return NULL");
                var invalidDays = DateTime.MaxValue.Ticks / TimeSpan.TicksPerDay + 1.0;
                var invalidHours = DateTime.MaxValue.Ticks / TimeSpan.TicksPerHour + 1.0;
                foreach (var sign in new[] { -1, 1 })
                {
                    Assert.Null(Assert.Single(await Read(context.Set<Row>().Where(row => row.Id == 2)
                        .Select(row => (DateTime?)anchor.AddDays(sign * invalidDays + row.Amount)), async)));
                    Assert.Null(Assert.Single(await Read(context.Set<Row>().Where(row => row.Id == 2)
                        .Select(row => (DateTime?)anchor.AddHours(sign * invalidHours + row.Amount)), async)));
                }

                DateTime? ExpectedNumeric(double? value, bool hours)
                {
                    if (!value.HasValue) return null;
                    var result = hours ? anchor.AddHours(value.Value) : anchor.AddDays(value.Value);
                    return mode == OceanBaseCompatibilityMode.MySql && (result - anchor).Ticks % 10 != 0 ? null : result;
                }
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public Task DateTime_numeric_minute_through_tick_additions_match_CLR(
            OceanBaseCompatibilityMode mode, bool async, bool nulls)
            => WithTable(mode, async, nulls, async (context, stage) =>
            {
                var anchor = new DateTime(5000, 1, 1);
                foreach (var (name, scale) in new[] { (nameof(DateTime.AddMinutes), TimeSpan.TicksPerMinute),
                    (nameof(DateTime.AddSeconds), TimeSpan.TicksPerSecond),
                    (nameof(DateTime.AddMilliseconds), TimeSpan.TicksPerMillisecond),
                    (nameof(DateTime.AddMicroseconds), TimeSpan.TicksPerMicrosecond) })
                {
                    var method = typeof(DateTime).GetMethod(name, [typeof(double)])!;
                    Func<DateTime, double, DateTime> add = (value, units) => (DateTime)method.Invoke(value, [units])!;
                    var row = Expression.Parameter(typeof(Row), "row");
                    var stamp = Expression.Property(Expression.Property(row, nameof(Row.DateStamp)), "Value");
                    var amount = 0.0;
                    Expression<Func<double>> captured = () => amount;
                    var projection = Expression.Lambda<Func<Row, DateTime?>>(Expression.Convert(
                        Expression.Call(stamp, method, captured.Body), typeof(DateTime?)), row);
                    foreach (var value in new[] { 0.0, 0.5, -1.25, 1000000.0000000001, -1000000.0000000001,
                        -11.0 / scale, -10.0 / scale, -9.0 / scale, -1.0 / scale,
                        1.0 / scale, 9.0 / scale, 10.0 / scale, 11.0 / scale })
                    {
                        stage(name + " signed fractional parameters, predicates and DATE promotion");
                        amount = value;
                        await Check(context, async, projection, value => value.DateStamp.HasValue
                            ? Expected(value.DateStamp.Value, amount) : null);
                    }
                    stage(name + " nullable native Double column");
                    var columnCall = Expression.Call(Expression.Constant(anchor), method,
                        Expression.Property(Expression.Property(row, nameof(Row.NumericAmount)), "Value"));
                    await Check(context, async, Expression.Lambda<Func<Row, DateTime?>>(
                        Expression.Convert(columnCall, typeof(DateTime?)), row),
                        value => value.NumericAmount.HasValue ? Expected(anchor, value.NumericAmount.Value) : null);
                    stage(name + " constant and converted timestamp composition");
                    var converted = Expression.Property(Expression.Property(row, nameof(Row.ConvertedStamp)), "Value");
                    var composed = Expression.Call(Expression.Call(converted, method, Expression.Constant(0.5)),
                        typeof(DateTime).GetMethod(nameof(DateTime.AddTicks), [typeof(long)])!, Expression.Constant(-10L));
                    await Check(context, async, Expression.Lambda<Func<Row, DateTime?>>(
                        Expression.Convert(composed, typeof(DateTime?)), row), value => value.ConvertedStamp.HasValue
                            ? Expected(value.ConvertedStamp.Value, 0.5)?.AddTicks(-10) : null);
                    stage(name + " out-of-unit-range NULL guard");
                    foreach (var sign in new[] { -1, 1 })
                    {
                        amount = sign * (DateTime.MaxValue.Ticks / scale + 1024.0);
                        Assert.All(await Read(context.Set<Row>().OrderBy(value => value.Id).Select(projection), async), Assert.Null);
                    }

                    DateTime? Expected(DateTime stampValue, double units)
                    {
                        var result = add(stampValue, units);
                        return mode == OceanBaseCompatibilityMode.MySql && (result - stampValue).Ticks % 10 != 0 ? null : result;
                    }
                }
                stage("AddTicks exact Int64 columns, NULL, large values and extreme guards");
                await Check(context, async, row => (DateTime?)anchor.AddTicks(row.TickAmount!.Value),
                    row => ExpectedTicks(row.TickAmount));
                var ticks = 0L;
                var query = context.Set<Row>().Where(row => row.Id == 2)
                    .Select(row => (DateTime?)anchor.AddTicks(ticks + row.Amount));
                foreach (var value in new[] { -11L, -10, -9, -1, 0, 1, 9, 10, 11,
                    9007199254740993, -9007199254740993, 9007199254741010, -9007199254741010, long.MinValue, long.MaxValue })
                {
                    stage("AddTicks precision and parameter refresh");
                    ticks = value;
                    Assert.Equal(ExpectedTicks(ticks), Assert.Single(await Read(query, async)));
                }
                await Check(context, async, row => (DateTime?)row.DateStamp!.Value.AddTicks(10), row => row.DateStamp?.AddTicks(10));

                DateTime? ExpectedTicks(long? value)
                    => !value.HasValue || value < -DateTime.MaxValue.Ticks || value > DateTime.MaxValue.Ticks
                        || mode == OceanBaseCompatibilityMode.MySql && value % 10 != 0 ? null : anchor.AddTicks(value.Value);
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public Task DateTime_date_differences_retain_signed_long_durations_and_composition(
            OceanBaseCompatibilityMode mode, bool async, bool nulls)
            => WithTable(mode, async, nulls, async (context, stage) =>
            {
                var anchor = new DateTime(2000, 1, 1);
                foreach (var value in new[] { anchor, new DateTime(1900, 1, 1), new DateTime(5000, 1, 1) })
                {
                    anchor = value;
                    stage("date difference long signed values and refreshed timestamp");
                    await Check(context, async, row => (TimeSpan?)row.Stamp!.Value.Subtract(anchor), row => row.Stamp?.Subtract(anchor));
                }
                stage("date difference signed subsecond values and DATE operands");
                var fractionalAnchor = new DateTime(2000, 1, 1).AddTicks(mode == OceanBaseCompatibilityMode.MySql ? 10 : 1);
                await Check(context, async, row => (TimeSpan?)row.DateStamp!.Value.Subtract(fractionalAnchor),
                    row => row.DateStamp?.Subtract(fractionalAnchor));
                await Check(context, async, row => (TimeSpan?)row.Stamp!.Value.Subtract(row.DateStamp!.Value),
                    row => row.Stamp.HasValue && row.DateStamp.HasValue ? row.Stamp.Value.Subtract(row.DateStamp.Value) : null);
                await Check(context, async, row => (TimeSpan?)row.DateStamp!.Value.Subtract(row.Stamp!.Value),
                    row => row.Stamp.HasValue && row.DateStamp.HasValue ? row.DateStamp.Value.Subtract(row.Stamp.Value) : null);
                foreach (var name in new[] { "Days", "Hours", "Minutes", "Seconds", "Milliseconds", "Microseconds", "Nanoseconds", "Ticks",
                    "TotalDays", "TotalHours", "TotalMinutes", "TotalSeconds", "TotalMilliseconds", "TotalMicroseconds", "TotalNanoseconds" })
                {
                    stage("date difference member " + name);
                    var row = Expression.Parameter(typeof(Row), "row");
                    Expression<Func<Row, TimeSpan>> difference = value => value.Stamp!.Value.Subtract(anchor);
                    var body = Expression.Property(ReplacingExpressionVisitor.Replace(difference.Parameters[0], row, difference.Body), name);
                    var member = typeof(TimeSpan).GetProperty(name)!;
                    if (member.PropertyType == typeof(long))
                        await Check(context, async, Expression.Lambda<Func<Row, long?>>(Expression.Convert(body, typeof(long?)), row),
                            value => value.Stamp.HasValue ? (long?)member.GetValue(value.Stamp.Value.Subtract(anchor)) : null);
                    else if (member.PropertyType == typeof(double))
                        await Check(context, async, Expression.Lambda<Func<Row, double?>>(Expression.Convert(body, typeof(double?)), row),
                            value => value.Stamp.HasValue ? (double?)member.GetValue(value.Stamp.Value.Subtract(anchor)) : null);
                    else
                        await Check(context, async, Expression.Lambda<Func<Row, int?>>(Expression.Convert(body, typeof(int?)), row),
                            value => value.Stamp.HasValue ? (int?)member.GetValue(value.Stamp.Value.Subtract(anchor)) : null);
                }
                stage("date differences compose with timestamp addition and subtraction");
                await Check(context, async, row => (DateTime?)anchor.Add(row.Stamp!.Value.Subtract(anchor)), row => row.Stamp);
                await Check(context, async, row => (DateTime?)row.Stamp!.Value.Subtract(row.Stamp.Value.Subtract(anchor)),
                    row => row.Stamp.HasValue ? anchor : null);
                stage("date differences normalize mixed duration comparisons, CASE and COALESCE");
                var expected = Seed(mode).Where(row => row.Stamp.HasValue && row.Duration.HasValue
                    && row.Stamp.Value.Subtract(anchor) > row.Duration.Value).Select(row => row.Id);
                Assert.Equal(expected, await Read(context.Set<Row>().Where(row => row.Stamp!.Value.Subtract(anchor) > row.Duration!.Value)
                    .OrderBy(row => row.Id).Select(row => row.Id), async));
                await Check(context, async, row => (TimeSpan?)row.Stamp!.Value.Subtract(anchor) ?? row.Duration,
                    row => row.Stamp?.Subtract(anchor) ?? row.Duration);
                await Check(context, async, row => row.Id == 2 ? row.Duration : (TimeSpan?)row.Stamp!.Value.Subtract(anchor),
                    row => row.Id == 2 ? row.Duration : row.Stamp?.Subtract(anchor));
                stage("date differences preserve scalar DateTime converter ownership");
                await Check(context, async, row => (TimeSpan?)row.ConvertedStamp!.Value.Subtract(anchor),
                    row => row.ConvertedStamp?.Subtract(anchor));
                await Check(context, async, row => (TimeSpan?)anchor.Subtract(row.ConvertedStamp!.Value),
                    row => row.ConvertedStamp.HasValue ? anchor.Subtract(row.ConvertedStamp.Value) : null);
                stage("mixed duration Compare and CompareTo normalize units");
                await Check(context, async, row => (int?)row.Stamp!.Value.Subtract(anchor).CompareTo(row.Duration!.Value),
                    row => row.Stamp.HasValue && row.Duration.HasValue ? row.Stamp.Value.Subtract(anchor).CompareTo(row.Duration.Value) : null);
                await Check(context, async, row => (int?)TimeSpan.Compare(row.Duration!.Value, row.Stamp!.Value.Subtract(anchor)),
                    row => row.Stamp.HasValue && row.Duration.HasValue ? TimeSpan.Compare(row.Duration.Value, row.Stamp.Value.Subtract(anchor)) : null);
                stage("date difference endpoints retain all representable ticks");
                var minimum = new DateTime(mode == OceanBaseCompatibilityMode.MySql ? 1000 : 1, 1, 1);
                var maximum = new DateTime(DateTime.MaxValue.Ticks - (mode == OceanBaseCompatibilityMode.MySql ? 9 : 0));
                var endpoints = context.Set<Row>().Where(row => row.Id == 2)
                    .Select(row => maximum.AddTicks(row.Amount).Subtract(minimum));
                Assert.Equal(maximum - minimum, Assert.Single(await Read(endpoints, async)));
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public Task TimeOnly_additions_wrap_signed_numeric_and_full_duration_offsets(
            OceanBaseCompatibilityMode mode, bool async, bool nulls)
            => WithTable(mode, async, nulls, async (context, stage) =>
            {
                foreach (var name in new[] { nameof(TimeOnly.AddHours), nameof(TimeOnly.AddMinutes) })
                {
                    var method = typeof(TimeOnly).GetMethod(name, [typeof(double)])!;
                    var scale = name == nameof(TimeOnly.AddHours) ? TimeSpan.TicksPerHour : TimeSpan.TicksPerMinute;
                    var row = Expression.Parameter(typeof(Row), "row");
                    Expression<Func<Row, TimeOnly>> clock = value => value.Clock!.Value;
                    var source = ReplacingExpressionVisitor.Replace(clock.Parameters[0], row, clock.Body);
                    var amount = 0.0;
                    Expression<Func<double>> captured = () => amount;
                    var projection = Expression.Lambda<Func<Row, TimeOnly?>>(Expression.Convert(
                        Expression.Call(source, method, captured.Body), typeof(TimeOnly?)), row);
                    foreach (var value in new[] { 0.0, 0.5, -1.25, 25.5, -25.5, 1440.25, -1440.25,
                        1000000.0000000001, -1000000.0000000001, 9223372036854775808.0 / scale,
                        -9223372036854775808.0 / scale,
                        Math.BitDecrement(9223372036854775808.0 / scale), Math.BitIncrement(-9223372036854775808.0 / scale),
                        -11.0 / scale, -10.0 / scale, -1.75 / scale, -1.0 / scale, -0.75 / scale,
                        0.75 / scale, 1.0 / scale, 1.75 / scale, 10.0 / scale, 11.0 / scale })
                    {
                        stage(name + " signed/wrapped fractional parameters and Int64 overflow boundaries");
                        amount = value;
                        try
                        {
                            await Check(context, async, projection, value => value.Clock.HasValue ? Expected(value.Clock.Value, amount) : null);
                        }
                        catch (Xunit.Sdk.XunitException error)
                        {
                            Assert.Fail($"{name}({amount:R}): {error.Message}");
                        }
                    }
                    foreach (var value in new[] { double.MaxValue, -double.MaxValue })
                    {
                        stage(name + " extreme finite literals");
                        await Check(context, async, Expression.Lambda<Func<Row, TimeOnly?>>(Expression.Convert(
                            Expression.Call(source, method, Expression.Constant(value)), typeof(TimeOnly?)), row),
                            seed => seed.Clock.HasValue ? Expected(seed.Clock.Value, value) : null);
                    }
                    stage(name + " nullable native Double columns");
                    var column = Expression.Property(Expression.Property(row, nameof(Row.NumericAmount)), "Value");
                    await Check(context, async, Expression.Lambda<Func<Row, TimeOnly?>>(
                        Expression.Convert(Expression.Call(source, method, column), typeof(TimeOnly?)), row),
                        value => value.Clock.HasValue && value.NumericAmount.HasValue ? Expected(value.Clock.Value, value.NumericAmount.Value) : null);

                    TimeOnly? Expected(TimeOnly clockValue, double units)
                    {
                        var result = (TimeOnly)method.Invoke(clockValue, [units])!;
                        return Representable(result);
                    }
                }
                foreach (var amount in new[] { TimeSpan.Zero, TimeSpan.FromDays(1000).Add(TimeSpan.FromTicks(10)),
                    -TimeSpan.FromDays(1000).Add(TimeSpan.FromTicks(10)), TimeSpan.FromTicks(-1), TimeSpan.FromTicks(1),
                    TimeSpan.FromTicks(-10), TimeSpan.FromTicks(10), TimeSpan.MinValue, TimeSpan.MaxValue })
                {
                    stage("TimeOnly.Add full TimeSpan parameter range");
                    await Check(context, async, row => (TimeOnly?)row.Clock!.Value.Add(amount),
                        row => row.Clock.HasValue ? Representable(row.Clock.Value.Add(amount)) : null);
                    await Check(context, async, row => (TimeOnly?)row.ConvertedClock!.Value.Add(amount),
                        row => row.ConvertedClock.HasValue ? Representable(row.ConvertedClock.Value.Add(amount)) : null);
                }
                stage("TimeOnly duration columns, numeric constants and method composition");
                await Check(context, async, row => (TimeOnly?)row.Clock!.Value.Add(row.Duration!.Value),
                    row => row.Clock.HasValue && row.Duration.HasValue ? Representable(row.Clock.Value.Add(row.Duration.Value)) : null);
                await Check(context, async, row => (TimeOnly?)row.Clock!.Value.AddHours(0.5).AddMinutes(-90.5),
                    row => row.Clock.HasValue ? Representable(row.Clock.Value.AddHours(0.5).AddMinutes(-90.5)) : null);
                var anchor = new DateTime(5000, 1, 1);
                await Check(context, async, row => (TimeOnly?)row.Clock!.Value.Add(row.Stamp!.Value.Subtract(anchor)),
                    row => row.Clock.HasValue && row.Stamp.HasValue ? Representable(row.Clock.Value.Add(row.Stamp.Value.Subtract(anchor))) : null);

                TimeOnly? Representable(TimeOnly value)
                    => mode == OceanBaseCompatibilityMode.MySql && value.Ticks % 10 != 0 ? null : value;
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public Task DateOnly_TimeOnly_conversions_and_ranges_preserve_native_values(
            OceanBaseCompatibilityMode mode, bool async, bool nulls)
            => WithTable(mode, async, nulls, async (context, stage) =>
            {
                stage("date and clock extraction, roundtrip and stored-calendar converters");
                await Check(context, async, row => (DateOnly?)DateOnly.FromDateTime(row.Stamp!.Value),
                    row => row.Stamp.HasValue ? DateOnly.FromDateTime(row.Stamp.Value) : null);
                await Check(context, async, row => (TimeOnly?)TimeOnly.FromDateTime(row.Stamp!.Value),
                    row => row.Stamp.HasValue ? TimeOnly.FromDateTime(row.Stamp.Value) : null);
                await Check(context, async, row => (TimeOnly?)TimeOnly.FromDateTime(row.DateStamp!.Value),
                    row => row.DateStamp.HasValue ? TimeOnly.FromDateTime(row.DateStamp.Value) : null);
                await Check(context, async, row => (DateOnly?)DateOnly.FromDateTime(row.ConvertedStamp!.Value),
                    row => row.ConvertedStamp.HasValue ? DateOnly.FromDateTime(row.ConvertedStamp.Value.AddDays(7)) : null);
                await Check(context, async, row => (TimeSpan?)row.Clock!.Value.ToTimeSpan(),
                    row => row.Clock?.ToTimeSpan());
                await Check(context, async, row => (TimeSpan?)row.ConvertedClock!.Value.ToTimeSpan(),
                    row => row.ConvertedClock?.AddHours(1).ToTimeSpan());
                await Check(context, async, row => (DateTime?)row.Day!.Value.ToDateTime(row.Clock!.Value),
                    row => row.Day.HasValue && row.Clock.HasValue ? row.Day.Value.ToDateTime(row.Clock.Value) : null);
                await Check(context, async, row => (DateTime?)DateOnly.FromDateTime(row.Stamp!.Value)
                        .ToDateTime(TimeOnly.FromDateTime(row.Stamp.Value), DateTimeKind.Unspecified), row => row.Stamp);
                await Check(context, async, row => (DateTime?)row.ConvertedDay!.Value.ToDateTime(row.ConvertedClock!.Value),
                    row => row.ConvertedDay.HasValue && row.ConvertedClock.HasValue
                        ? row.ConvertedDay.Value.AddDays(2).ToDateTime(row.ConvertedClock.Value.AddHours(1)) : null);
                var fixedClock = new TimeOnly(12, 34, 56);
                var fixedDay = new DateOnly(2024, 2, 29);
                await Check(context, async, row => (DateTime?)row.Day!.Value.ToDateTime(fixedClock),
                    row => row.Day?.ToDateTime(fixedClock));
                await Check(context, async, row => (DateTime?)fixedDay.ToDateTime(row.Clock!.Value),
                    row => row.Clock.HasValue ? fixedDay.ToDateTime(row.Clock.Value) : null);
                foreach (var date in new[] { DateOnly.MinValue, DateOnly.MaxValue })
                {
                    var duration = date == DateOnly.MinValue ? TimeSpan.Zero
                        : TimeSpan.FromDays(1).Subtract(TimeSpan.FromTicks(mode == OceanBaseCompatibilityMode.MySql ? 10 : 1));
                    await Check(context, async, row => (DateTime?)date.ToDateTime(TimeOnly.FromTimeSpan(duration)),
                        _ => date.ToDateTime(TimeOnly.FromTimeSpan(duration)));
                }

                stage("duration conversions: NULL, invalid ranges, full scalar tick range and precision");
                await Check(context, async, row => (TimeOnly?)TimeOnly.FromTimeSpan(row.Duration!.Value),
                    row => Clock(row.Duration));
                await Check(context, async, row => (TimeOnly?)TimeOnly.FromTimeSpan(row.ConvertedDuration!.Value),
                    row => Clock(row.ConvertedDuration?.Negate()));
                await Check(context, async, row => (TimeOnly?)TimeOnly.FromTimeSpan(row.Clock!.Value.ToTimeSpan()), row => row.Clock);
                await Check(context, async, row => (TimeOnly?)TimeOnly.FromTimeSpan(row.Stamp!.Value.Subtract(row.DateStamp!.Value)),
                    row => row.Stamp.HasValue && row.DateStamp.HasValue ? Clock(row.Stamp.Value.Subtract(row.DateStamp.Value)) : null);
                foreach (var offset in new[] { TimeSpan.Zero, TimeSpan.FromTicks(-1), TimeSpan.FromTicks(1), TimeSpan.FromTicks(10),
                    TimeSpan.FromDays(1).Subtract(TimeSpan.FromTicks(1)), TimeSpan.FromDays(1).Subtract(TimeSpan.FromTicks(10)),
                    TimeSpan.FromDays(1), TimeSpan.MinValue, TimeSpan.MaxValue })
                {
                    await Check(context, async, row => (TimeOnly?)TimeOnly.FromTimeSpan(offset), _ => Clock(offset));
                }

                stage("normal, wrapping, equal and fractional IsBetween endpoints and refreshed parameters");
                foreach (var (start, end) in new[] { (TimeOnly.MinValue, new TimeOnly(1, 0)),
                    (new TimeOnly(23, 0), new TimeOnly(1, 0)), (TimeOnly.MinValue, TimeOnly.MinValue),
                    (new TimeOnly(1, 0), new TimeOnly(23, 0)), (TimeOnly.FromTimeSpan(TimeSpan.FromTicks(10)), new TimeOnly(0, 0, 1)),
                    (new TimeOnly(23, 59, 59), TimeOnly.FromTimeSpan(TimeSpan.FromTicks(10))) })
                {
                    await Check(context, async, row => (bool?)row.Clock!.Value.IsBetween(start, end),
                        row => row.Clock?.IsBetween(start, end));
                    await Check(context, async, row => (bool?)row.ConvertedClock!.Value.IsBetween(start, end),
                        row => row.ConvertedClock?.IsBetween(start, end));
                }
                await Check(context, async, row => (bool?)row.Clock!.Value.IsBetween(row.Clock.Value, row.Clock.Value),
                    row => row.Clock.HasValue ? false : null);
                await Check(context, async, row => (bool?)row.Clock!.Value.IsBetween(
                        TimeOnly.FromDateTime(row.Stamp!.Value), row.Clock.Value.AddMinutes(1)),
                    row => row.Clock.HasValue && row.Stamp.HasValue
                        ? row.Clock.Value.IsBetween(TimeOnly.FromDateTime(row.Stamp.Value), row.Clock.Value.AddMinutes(1)) : null);
                var upper = new TimeOnly(1, 0);
                await Check(context, async, row => (bool?)row.Clock!.Value.IsBetween(TimeOnly.FromTimeSpan(row.Duration!.Value), upper),
                    row => row.Clock.HasValue && Clock(row.Duration) is { } lower ? row.Clock.Value.IsBetween(lower, upper) : null);
                var lowerLimit = TimeOnly.MinValue;
                await Check(context, async, row => (bool?)row.Clock!.Value.IsBetween(lowerLimit, TimeOnly.FromTimeSpan(row.Duration!.Value)),
                    row => row.Clock.HasValue && Clock(row.Duration) is { } endLimit ? row.Clock.Value.IsBetween(lowerLimit, endLimit) : null);

                TimeOnly? Clock(TimeSpan? value)
                    => value.HasValue && value.Value >= TimeSpan.Zero && value.Value < TimeSpan.FromDays(1)
                        && (mode != OceanBaseCompatibilityMode.MySql || value.Value.Ticks % 10 == 0)
                        ? TimeOnly.FromTimeSpan(value.Value) : null;
            }, keepTemporalConversions: true);

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
            var needle = wanted.FirstOrDefault(value => value.HasValue);
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

        private static Task WithTable(OceanBaseCompatibilityMode mode, bool async, bool nulls, Func<Context, Action<string>, Task> run,
            bool keepTemporalConversions = false)
            => LiveStorageTable.WithTable<Context>(mode, async, false, (options, table) =>
            {
                var builder = new DbContextOptionsBuilder<Context>(options).UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
                    .EnableServiceProviderCaching(false).ReplaceService<IModelCacheKeyFactory, CacheKey>();
                if (keepTemporalConversions) builder.ReplaceService<IEvaluatableExpressionFilter, TemporalConversionFilter>();
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
            double?[] numericAmounts = [null, 0, 0.5, -1.25, 40.25, -40.25, 1000000.0000000001, -1000000.0000000001];
            long?[] tickAmounts = [null, 0, 10, -10, 9007199254741010, -9007199254741010, long.MinValue, long.MaxValue];
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
                    Amount = index % 3 - 1, NumericAmount = numericAmounts[index], TickAmount = tickAmounts[index] };
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
            public double? NumericAmount { get; set; }
            public long? TickAmount { get; set; }
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
