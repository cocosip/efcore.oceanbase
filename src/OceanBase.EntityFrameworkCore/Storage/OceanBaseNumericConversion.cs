using System;
using System.Globalization;
using OceanBaseConnector;

namespace OceanBase.EntityFrameworkCore.Storage
{
    internal static class OceanBaseNumericConversion
    {
        public static object ConvertValue(object value, Type targetType)
        {
            if (value is OceanBaseDecimal number)
            {
                // Parse at the target precision: decimal is bounded and Single via Double can round twice.
                if (targetType == typeof(float))
                {
                    var single = float.Parse(number.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
                    return float.IsFinite(single) ? single
                        : throw new OverflowException("The database value is outside the CLR Single range.");
                }

                if (targetType == typeof(double))
                {
                    var converted = double.Parse(number.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
                    return double.IsFinite(converted) ? converted
                        : throw new OverflowException("The database value is outside the CLR Double range.");
                }

                value = number.ToDecimal();
            }

            var integer = Type.GetTypeCode(targetType) is TypeCode.SByte or TypeCode.Byte or TypeCode.Int16
                or TypeCode.UInt16 or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64;
            if (integer && (value switch
                {
                    decimal fraction => decimal.Truncate(fraction) != fraction,
                    double fraction => double.IsFinite(fraction) && Math.Truncate(fraction) != fraction,
                    float fraction => float.IsFinite(fraction) && MathF.Truncate(fraction) != fraction,
                    _ => false
                }))
            {
                throw new InvalidCastException("A fractional database value cannot be read as an integer.");
            }

            if (targetType == typeof(float) && value is double wide)
            {
                if (double.IsFinite(wide) && (wide > float.MaxValue || wide < -float.MaxValue))
                {
                    throw new OverflowException("The database value is outside the CLR Single range.");
                }

                return (float)wide;
            }

            return targetType.IsInstanceOfType(value) ? value : Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
        }
    }
}
