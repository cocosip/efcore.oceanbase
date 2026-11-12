using System;
using OceanBaseConnector;

namespace OceanBase.EntityFrameworkCore.Storage
{
    internal static class OceanBaseTemporalConversions
    {
        public static object ConvertValue(object value, Type targetType)
        {
            if (targetType == typeof(DateTime) && value is OceanBaseTimestamp timestamp)
            {
                return timestamp.ToDateTime();
            }

            if (targetType == typeof(DateTimeOffset) && value is OceanBaseTimestampWithTimeZone zoned)
            {
                return zoned.ToDateTimeOffset();
            }

            if (targetType == typeof(DateTimeOffset) && value is DateTime dateTime)
            {
                return ReadUtcDateTime(dateTime);
            }

            if (targetType == typeof(DateTime) && value is DateTimeOffset zonedDateTime)
            {
                return DateTime.SpecifyKind(zonedDateTime.DateTime, DateTimeKind.Unspecified);
            }

            if (targetType == typeof(DateOnly) && value is DateTime date)
            {
                return ReadDateOnly(date);
            }

            if (targetType == typeof(DateOnly) && value is DateTimeOffset zonedDate)
            {
                return ReadDateOnly(zonedDate.DateTime);
            }

            if (targetType == typeof(TimeOnly) && value is TimeSpan time)
            {
                return TimeOnly.FromTimeSpan(time);
            }

            if (targetType == typeof(TimeOnly) && value is OceanBaseIntervalDayToSecond intervalForClock)
            {
                return TimeOnly.FromTimeSpan(intervalForClock.ToTimeSpan());
            }

            if (targetType == typeof(TimeSpan) && value is OceanBaseIntervalDayToSecond interval)
            {
                return interval.ToTimeSpan();
            }

            return OceanBaseNumericConversion.ConvertValue(value, targetType);
        }

        public static DateOnly ReadDateOnly(DateTime value)
        {
            if (value.TimeOfDay != TimeSpan.Zero)
            {
                throw new InvalidCastException("A DateOnly column contains a non-midnight time component.");
            }

            return DateOnly.FromDateTime(value);
        }

        public static DateTime ToUtcDateTime(DateTimeOffset value)
            => DateTime.SpecifyKind(value.UtcDateTime, DateTimeKind.Unspecified);

        public static DateTimeOffset ReadUtcDateTime(DateTime value)
            => new(DateTime.SpecifyKind(value, DateTimeKind.Unspecified), TimeSpan.Zero);

        public static OceanBaseIntervalDayToSecond ToInterval(TimeSpan value)
            => new(value < TimeSpan.Zero, Math.Abs(value.Days), Math.Abs(value.Hours), Math.Abs(value.Minutes),
                Math.Abs(value.Seconds), checked((int)Math.Abs(value.Ticks % TimeSpan.TicksPerSecond) * 100), 9, 9);
    }
}
