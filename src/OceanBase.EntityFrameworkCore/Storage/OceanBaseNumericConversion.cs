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
                value = number.ToDecimal();
            }

            var integer = Type.GetTypeCode(targetType) is TypeCode.SByte or TypeCode.Byte or TypeCode.Int16
                or TypeCode.UInt16 or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64;
            if (integer && value is decimal fraction && decimal.Truncate(fraction) != fraction)
            {
                throw new InvalidCastException("A fractional database value cannot be read as an integer.");
            }

            return targetType.IsInstanceOfType(value) ? value : Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
        }
    }
}
