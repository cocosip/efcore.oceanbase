using System;
using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

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
            if (clrType == typeof(byte[])) return new OceanBaseByteArrayTypeMapping(storeTypes.Binary, mappingInfo.Size, _compatibilityMode);
            if (clrType == typeof(DateTime)) return new DateTimeTypeMapping(storeTypes.DateTime, DbType.DateTime2);
            if (clrType == typeof(DateTimeOffset)) return new DateTimeOffsetTypeMapping(storeTypes.DateTimeOffset, DbType.DateTimeOffset);
            if (clrType == typeof(DateOnly)) return new DateOnlyTypeMapping(storeTypes.Date, DbType.Date);
            if (clrType == typeof(TimeOnly)) return new TimeOnlyTypeMapping(storeTypes.Time, DbType.Time);
            if (clrType == typeof(TimeSpan)) return new TimeSpanTypeMapping(storeTypes.Time, DbType.Time);
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
            var name = base.ParseStoreTypeName(storeTypeName, ref unicode, ref size, ref precision, ref scale);
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
