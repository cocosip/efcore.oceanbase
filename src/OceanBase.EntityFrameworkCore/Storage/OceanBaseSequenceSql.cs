using System;
using System.Globalization;

namespace OceanBase.EntityFrameworkCore.Storage;

internal static class OceanBaseSequenceSql
{
    internal static string Create(string identifier, Type type, long start, int increment,
        long? min, long? max, bool cyclic)
    {
        if (type != typeof(long))
            throw new NotSupportedException("OceanBase model sequences require the Int64 CLR type.");
        ValidateStart(start, increment, min, max);
        return "CREATE SEQUENCE " + identifier + " START WITH " + Number(start)
            + Options(increment, min, max, cyclic) + " NOCACHE";
    }

    internal static void ValidateStart(long start, int increment, long? min, long? max)
    {
        if (start < EffectiveMin(min, increment) || start > EffectiveMax(max, increment))
            throw new ArgumentOutOfRangeException(nameof(start), "The sequence start must lie within its bounds.");
    }

    internal static string Options(int increment, long? min, long? max, bool cyclic)
    {
        var lower = EffectiveMin(min, increment);
        var upper = EffectiveMax(max, increment);
        if (increment == 0 || (decimal)upper - lower < Math.Abs((long)increment))
            throw new ArgumentException("Sequence bounds must be separated by at least one nonzero increment.");
        return " INCREMENT BY " + Number(increment) + " MINVALUE " + Number(lower) + " MAXVALUE " + Number(upper)
            + (cyclic ? " CYCLE" : " NOCYCLE");
    }

    private static long EffectiveMin(long? min, int increment) => min ?? (increment < 0 ? long.MinValue : 1);
    private static long EffectiveMax(long? max, int increment) => max ?? (increment < 0 ? -1 : long.MaxValue);
    internal static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
}
