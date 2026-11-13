using System;
using System.Data;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBaseConnector;

namespace OceanBase.EntityFrameworkCore.Storage;

/// <summary>Construction helpers used by generated EF compiled models.</summary>
/// <remarks>These infrastructure methods preserve provider mappings without exposing their internal types.</remarks>
public static class OceanBaseCompiledModelTypeMappings
{
    /// <summary>Converts an exact duration for generated provider converter expressions.</summary>
    public static OceanBaseIntervalDayToSecond ToInterval(TimeSpan value) => OceanBaseTemporalConversions.ToInterval(value);

    /// <summary>Reads a date for generated provider converter expressions.</summary>
    public static DateOnly ReadDateOnly(DateTime value) => OceanBaseTemporalConversions.ReadDateOnly(value);

    /// <summary>Normalizes a timestamp for generated provider converter expressions.</summary>
    public static DateTime ToUtcDateTime(DateTimeOffset value) => OceanBaseTemporalConversions.ToUtcDateTime(value);

    /// <summary>Reads a UTC timestamp for generated provider converter expressions.</summary>
    public static DateTimeOffset ReadUtcDateTime(DateTime value) => OceanBaseTemporalConversions.ReadUtcDateTime(value);

    /// <summary>Creates the provider integer mapping used by generated code.</summary>
    public static RelationalTypeMapping Integer<T>(string storeType, DbType dbType, OceanBaseCompatibilityMode mode,
        int? precision, int? scale, int? size, bool fixedLength) where T : struct, IConvertible
        => new OceanBaseIntegerTypeMapping<T>(storeType, dbType, mode, precision, scale, size, fixedLength);

    /// <summary>Creates the provider floating-point mapping used by generated code.</summary>
    public static RelationalTypeMapping FloatingPoint<T>(string storeType, OceanBaseCompatibilityMode mode,
        int? precision, int? scale, int? size, bool fixedLength) where T : struct, IConvertible
        => new OceanBaseFloatingPointTypeMapping<T>(storeType, mode, precision, scale, size, fixedLength);

    /// <summary>Creates the provider decimal mapping used by generated code.</summary>
    public static RelationalTypeMapping Decimal(string storeType, int? precision, int? scale,
        int? size, bool fixedLength, OceanBaseCompatibilityMode mode)
        => new OceanBaseDecimalTypeMapping(storeType, precision, scale, size, fixedLength, mode);

    /// <summary>Creates the provider text mapping used by generated code.</summary>
    public static RelationalTypeMapping Text(string storeType, DbType dbType, bool unicode, int? size,
        bool fixedLength, OceanBaseCompatibilityMode mode)
        => new OceanBaseStringTypeMapping(storeType, dbType, unicode, size, fixedLength, mode);

    /// <summary>Creates the provider binary mapping used by generated code.</summary>
    public static RelationalTypeMapping Binary(string storeType, int? size, OceanBaseCompatibilityMode mode, bool fixedLength)
        => new OceanBaseByteArrayTypeMapping(storeType, size, mode, fixedLength);

    /// <summary>Creates the provider temporal mapping used by generated code.</summary>
    public static RelationalTypeMapping Temporal<T>(string storeType, DbType dbType, int precision,
        OceanBaseCompatibilityMode mode, byte leadingPrecision) where T : struct
        => new OceanBaseTemporalTypeMapping<T>(storeType, dbType, precision, mode, leadingPrecision);
}
