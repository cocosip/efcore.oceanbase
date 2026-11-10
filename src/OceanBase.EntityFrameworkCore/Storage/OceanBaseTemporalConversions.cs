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
