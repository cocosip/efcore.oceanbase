using System;
using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OceanBaseConnector;

namespace OceanBase.EntityFrameworkCore.Storage
{
    internal sealed class OceanBaseTypeMappingSource : RelationalTypeMappingSource
    {
        private readonly OceanBaseCompatibilityMode _compatibilityMode;

        internal OceanBaseTypeMappingSource(
            TypeMappingSourceDependencies dependencies,
            RelationalTypeMappingSourceDependencies relationalDependencies,
            OceanBaseCompatibilityMode compatibilityMode)
            : base(dependencies, relationalDependencies)
        {
            _compatibilityMode = compatibilityMode;
        }

        protected override RelationalTypeMapping? FindMapping(in RelationalTypeMappingInfo mappingInfo)
        {
            var clrType = mappingInfo.ClrType is { } declaredType
                ? Nullable.GetUnderlyingType(declaredType) ?? declaredType
                : null;
            var storeTypes = OceanBaseTypeMappings.For(_compatibilityMode);
            var oracle = _compatibilityMode == OceanBaseCompatibilityMode.Oracle;

            if (clrType == typeof(sbyte)) return new OceanBaseIntegerTypeMapping<sbyte>(oracle ? "NUMBER(3)" : "TINYINT", DbType.SByte);
            if (clrType == typeof(byte)) return new OceanBaseIntegerTypeMapping<byte>(oracle ? "NUMBER(3)" : "TINYINT UNSIGNED", DbType.Byte);
            if (clrType == typeof(short)) return new OceanBaseIntegerTypeMapping<short>(oracle ? "NUMBER(5)" : "SMALLINT", DbType.Int16);
            if (clrType == typeof(ushort)) return new OceanBaseIntegerTypeMapping<ushort>(oracle ? "NUMBER(5)" : "SMALLINT UNSIGNED", DbType.UInt16);
            if (clrType == typeof(int)) return new OceanBaseIntegerTypeMapping<int>(storeTypes.Integer, DbType.Int32);
            if (clrType == typeof(uint)) return new OceanBaseIntegerTypeMapping<uint>(oracle ? "NUMBER(10)" : "INT UNSIGNED", DbType.UInt32);
            if (clrType == typeof(long)) return new OceanBaseIntegerTypeMapping<long>(storeTypes.Long, DbType.Int64);
            if (clrType == typeof(ulong)) return new OceanBaseIntegerTypeMapping<ulong>(oracle ? "NUMBER(20)" : "BIGINT UNSIGNED", DbType.UInt64);
            if (clrType == typeof(decimal)) return FindDecimalMapping(mappingInfo);
            if (clrType == typeof(string)) return FindStringMapping(mappingInfo);
            if (clrType == typeof(byte[])) return FindBinaryMapping(mappingInfo);
            if (clrType == typeof(DateTime) || clrType == typeof(DateTimeOffset) || clrType == typeof(DateOnly)
                || clrType == typeof(TimeOnly) || clrType == typeof(TimeSpan))
            {
                return FindTemporalMapping(clrType, mappingInfo);
            }
            if (clrType == typeof(Guid)) return (RelationalTypeMapping)(oracle
                ? new OceanBaseByteArrayTypeMapping(storeTypes.Guid, 16, _compatibilityMode)
                    .WithComposedConverter(new GuidToBytesConverter())
                : new OceanBaseStringTypeMapping(storeTypes.Guid, DbType.AnsiStringFixedLength, false, 36, true, _compatibilityMode)
                    .WithComposedConverter(new GuidToStringConverter()));
            if (clrType == typeof(double)) return new DoubleTypeMapping(storeTypes.Double, DbType.Double);
            if (clrType == typeof(float)) return new FloatTypeMapping(storeTypes.Float, DbType.Single);
            if (clrType == typeof(bool)) return (RelationalTypeMapping)(oracle
                ? new OceanBaseDecimalTypeMapping(storeTypes.Boolean, 1, 0)
                    .WithComposedConverter(new BoolToZeroOneConverter<decimal>())
                : new OceanBaseIntegerTypeMapping<bool>(storeTypes.Boolean, DbType.Boolean));

            return base.FindMapping(mappingInfo);
        }

        private RelationalTypeMapping FindBinaryMapping(in RelationalTypeMappingInfo info)
        {
            if (info.Precision is not null || info.Scale is not null)
            {
                throw new ArgumentException("Binary mappings do not support precision or scale.");
            }

            var oracle = _compatibilityMode == OceanBaseCompatibilityMode.Oracle;
            var size = info.Size ?? (info.IsKeyOrIndex && info.StoreTypeName is null ? 255 : (int?)null);
            var storeType = info.StoreTypeName ?? (info.IsFixedLength == true
                ? $"BINARY({size})"
                : size is { } length && length <= (oracle ? 2000 : 262144)
                    ? $"{(oracle ? "RAW" : "VARBINARY")}({size})"
                    : oracle ? "BLOB" : "LONGBLOB");
            var mapping = new OceanBaseByteArrayTypeMapping(storeType, size, _compatibilityMode, info.IsFixedLength);
            if (info.IsKeyOrIndex && mapping.StoreTypeNameBase.EndsWith("BLOB", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Binary keys and indexes require bounded storage in this provider profile.");
            }

            return mapping;
        }

        private RelationalTypeMapping FindTemporalMapping(Type clrType, in RelationalTypeMappingInfo info)
        {
            var oracle = _compatibilityMode == OceanBaseCompatibilityMode.Oracle;
            var dateOnly = clrType == typeof(DateOnly);
            var clock = clrType == typeof(TimeOnly);
            var duration = clock || clrType == typeof(TimeSpan);
            var zoned = clrType == typeof(DateTimeOffset);
            var leading = clock ? 1 : 9;
            var precision = info.Precision ?? (dateOnly ? 0 : oracle ? 7 : 6);
            var typeName = dateOnly ? "DATE" : duration ? oracle ? "INTERVAL" : "TIME"
                : oracle ? "TIMESTAMP" : "DATETIME";
            var storeType = info.StoreTypeName;
            if (storeType is not null)
            {
                var pattern = dateOnly ? @"\ADATE\z"
                    : duration && oracle ? @"\AINTERVAL\s+DAY(?:\((?<days>\d+)\))?\s+TO\s+SECOND(?:\((?<precision>\d+)\))?\z"
                    : oracle ? zoned ? @"\ATIMESTAMP(?:\((?<precision>\d+)\))?\s+WITH\s+TIME\s+ZONE\z"
                        : @"\A(?<type>TIMESTAMP(?:\((?<precision>\d+)\))?|DATE)\z"
                    : duration ? @"\ATIME(?:\((?<precision>\d+)\))?\z" : @"\ADATETIME(?:\((?<precision>\d+)\))?\z";
                var match = Regex.Match(storeType.Trim(), pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (!match.Success)
                {
                    throw new NotSupportedException($"The store type '{storeType}' is not supported for {clrType.Name} in {_compatibilityMode} mode.");
                }

                var date = storeType.Trim().Equals("DATE", StringComparison.OrdinalIgnoreCase);
                if (date) typeName = "DATE";
                var declaredPrecision = match.Groups["precision"].Success
                    ? int.Parse(match.Groups["precision"].Value, CultureInfo.InvariantCulture) : (int?)null;
                if (declaredPrecision is not null && info.Precision is not null && declaredPrecision != info.Precision)
                {
                    throw new ArgumentException("The explicit and configured temporal precisions conflict.");
                }

                precision = declaredPrecision ?? info.Precision ?? (date ? 0 : oracle ? 6 : 0);
                if (duration && oracle)
                {
                    leading = match.Groups["days"].Success ? int.Parse(match.Groups["days"].Value, CultureInfo.InvariantCulture) : 2;
                }

                if (declaredPrecision is null && info.Precision is not null && !date)
                {
                    storeType = null;
                }
            }

            if (precision < 0 || precision > (typeName == "DATE" ? 0 : oracle ? 9 : 6) || leading is < 0 or > 9)
            {
                throw new ArgumentOutOfRangeException(nameof(info), "The temporal precision is outside the supported store range.");
            }

            if (info.Scale is not null)
            {
                throw new ArgumentException("Temporal fractional seconds use Precision, not a separate Scale facet.");
            }

            storeType ??= typeName == "DATE" ? "DATE" : duration && oracle ? $"INTERVAL DAY({leading}) TO SECOND({precision})"
                : $"{typeName}({precision})" + (oracle && zoned ? " WITH TIME ZONE" : string.Empty);
            if (duration && oracle)
            {
                var dayPrecision = (byte)leading;
                var mapping = new OceanBaseTemporalTypeMapping<OceanBaseIntervalDayToSecond>(storeType, DbType.Object,
                    precision, _compatibilityMode, dayPrecision);
                return (RelationalTypeMapping)(clock
                    ? mapping.WithComposedConverter(new ValueConverter<TimeOnly, OceanBaseIntervalDayToSecond>(
                        value => OceanBaseTemporalConversions.ToInterval(value.ToTimeSpan()),
                        value => TimeOnly.FromTimeSpan(value.ToTimeSpan())))
                    : mapping.WithComposedConverter(new ValueConverter<TimeSpan, OceanBaseIntervalDayToSecond>(
                        value => OceanBaseTemporalConversions.ToInterval(value), value => value.ToTimeSpan())));
            }

            if (duration)
            {
                var mapping = new OceanBaseTemporalTypeMapping<TimeSpan>(storeType, DbType.Time, precision, _compatibilityMode);
                return clock ? (RelationalTypeMapping)mapping.WithComposedConverter(new ValueConverter<TimeOnly, TimeSpan>(
                    value => value.ToTimeSpan(), value => TimeOnly.FromTimeSpan(value))) : mapping;
            }

            if (oracle && zoned)
            {
                return new OceanBaseTemporalTypeMapping<DateTimeOffset>(storeType, DbType.DateTimeOffset, precision, _compatibilityMode);
            }

            var dateMapping = new OceanBaseTemporalTypeMapping<DateTime>(storeType,
                typeName == "DATE" ? DbType.Date : DbType.DateTime2, precision, _compatibilityMode);
            return dateOnly ? (RelationalTypeMapping)dateMapping.WithComposedConverter(new ValueConverter<DateOnly, DateTime>(
                    value => value.ToDateTime(TimeOnly.MinValue), value => OceanBaseTemporalConversions.ReadDateOnly(value)))
                : zoned ? (RelationalTypeMapping)dateMapping.WithComposedConverter(new ValueConverter<DateTimeOffset, DateTime>(
                    value => OceanBaseTemporalConversions.ToUtcDateTime(value), value => OceanBaseTemporalConversions.ReadUtcDateTime(value)))
                : dateMapping;
        }

        private RelationalTypeMapping? FindDecimalMapping(in RelationalTypeMappingInfo info)
        {
            var oracle = _compatibilityMode == OceanBaseCompatibilityMode.Oracle;
            var storeType = info.StoreTypeName;
            var typeName = info.StoreTypeNameBase?.ToUpperInvariant();
            if (typeName is not null && (oracle ? typeName != "NUMBER" && typeName != "DECIMAL" && typeName != "NUMERIC"
                    : typeName != "DECIMAL" && typeName != "NUMERIC"))
            {
                return null;
            }

            var precision = info.Precision;
            var scale = info.Scale;
            if (!oracle)
            {
                precision ??= storeType is not null ? 10 : 65;
                scale ??= info.Precision is not null || storeType is not null ? 0 : 30;
            }
            else if (precision is not null || scale is not null)
            {
                precision ??= 38;
                scale ??= 0;
            }

            if (precision is < 1 || precision > (oracle ? 38 : 65))
            {
                throw new ArgumentOutOfRangeException(nameof(info), "Decimal precision is outside the supported store range.");
            }

            if (scale < (oracle ? -84 : 0) || scale > (oracle ? 127 : 30) || !oracle && scale > precision)
            {
                throw new ArgumentOutOfRangeException(nameof(info), "Decimal scale is outside the supported store range.");
            }

            storeType ??= precision is { } digits
                ? string.Create(CultureInfo.InvariantCulture, $"{(oracle ? "NUMBER" : "DECIMAL")}({digits},{scale})")
                : "NUMBER";
            return new OceanBaseDecimalTypeMapping(storeType, precision, scale);
        }

        protected override string? ParseStoreTypeName(
            string? storeTypeName, ref bool? unicode, ref int? size, ref int? precision, ref int? scale)
        {
            var configuredSize = size;
            var name = base.ParseStoreTypeName(storeTypeName, ref unicode, ref size, ref precision, ref scale);
            if (configuredSize is not null && size != configuredSize
                && name?.ToUpperInvariant() is "BINARY" or "VARBINARY" or "RAW")
            {
                throw new ArgumentException("The explicit binary store length conflicts with its configured size.");
            }

            if (_compatibilityMode == OceanBaseCompatibilityMode.Oracle && storeTypeName is not null
                && (string.Equals(name, "CHAR", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "VARCHAR2", StringComparison.OrdinalIgnoreCase)))
            {
                var opening = storeTypeName.IndexOf('(');
                var closing = storeTypeName.IndexOf(')', opening + 1);
                if (opening >= 0 && closing > opening)
                {
                    var parts = storeTypeName[(opening + 1)..closing].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2 && (parts[1].Equals("CHAR", StringComparison.OrdinalIgnoreCase)
                            || parts[1].Equals("BYTE", StringComparison.OrdinalIgnoreCase))
                        && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var length))
                    {
                        size = length;
                    }
                }
            }

            return name;
        }

        private RelationalTypeMapping? FindStringMapping(in RelationalTypeMappingInfo info)
        {
            var oracle = _compatibilityMode == OceanBaseCompatibilityMode.Oracle;
            var unicode = info.IsUnicode ?? true;
            var fixedLength = info.IsFixedLength ?? false;
            var size = info.Size;
            var storeType = info.StoreTypeName;
            if (storeType is not null)
            {
                var typeName = info.StoreTypeNameBase?.ToUpperInvariant();
                if (oracle)
                {
                    if (typeName is not "CHAR" and not "VARCHAR2" and not "NCHAR" and not "NVARCHAR2" and not "CLOB" and not "NCLOB")
                    {
                        return null;
                    }

                    unicode = typeName is "NCHAR" or "NVARCHAR2" or "NCLOB";
                }
                else if (typeName is not "CHAR" and not "VARCHAR" and not "TINYTEXT" and not "TEXT" and not "MEDIUMTEXT" and not "LONGTEXT")
                {
                    return null;
                }

                fixedLength = typeName is "CHAR" or "NCHAR";
            }
            else
            {
                size ??= info.IsKeyOrIndex ? 255 : oracle ? unicode ? 2000 : 4000 : null;
                if (size is < 1 || fixedLength && size is null)
                {
                    throw new ArgumentOutOfRangeException(nameof(info), "A string length must be positive; fixed strings require a length.");
                }

                if (fixedLength)
                {
                    var limit = oracle ? unicode ? 1000 : 2000 : 255;
                    if (size > limit)
                    {
                        throw new ArgumentOutOfRangeException(nameof(info), "The fixed string length exceeds the store limit.");
                    }

                    storeType = oracle && unicode ? $"NCHAR({size})" : oracle ? $"CHAR({size} CHAR)" : $"CHAR({size})";
                }
                else if (oracle)
                {
                    storeType = size > (unicode ? 2000 : 4000) ? unicode ? "NCLOB" : "CLOB"
                        : unicode ? $"NVARCHAR2({size})" : $"VARCHAR2({size} CHAR)";
                }
                else
                {
                    storeType = size is null or > 16383 ? "LONGTEXT" : $"VARCHAR({size})";
                }
            }

            var dbType = unicode ? fixedLength ? DbType.StringFixedLength : DbType.String
                : fixedLength ? DbType.AnsiStringFixedLength : DbType.AnsiString;
            return new OceanBaseStringTypeMapping(storeType, dbType, unicode, size, fixedLength, _compatibilityMode);
        }
    }
}
