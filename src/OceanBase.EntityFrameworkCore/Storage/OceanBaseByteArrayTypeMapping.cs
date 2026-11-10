using System;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Json;

namespace OceanBase.EntityFrameworkCore.Storage
{
    internal sealed class OceanBaseByteArrayTypeMapping : ByteArrayTypeMapping
    {
        private readonly OceanBaseCompatibilityMode _mode;

        public OceanBaseByteArrayTypeMapping(string storeType, int? size, OceanBaseCompatibilityMode mode, bool? fixedLength = null)
            : base(ValidateParameters(new RelationalTypeMappingParameters(
                new CoreTypeMappingParameters(typeof(byte[]), jsonValueReaderWriter: JsonByteArrayReaderWriter.Instance),
                storeType, StoreTypePostfix.None, System.Data.DbType.Binary, size: size), mode, fixedLength))
        {
            _mode = mode;
        }

        private OceanBaseByteArrayTypeMapping(RelationalTypeMappingParameters parameters, OceanBaseCompatibilityMode mode)
            : base(ValidateParameters(parameters, mode, parameters.FixedLength))
        {
            _mode = mode;
        }

        protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
            => new OceanBaseByteArrayTypeMapping(parameters, _mode);

        protected override void ConfigureParameter(DbParameter parameter)
        {
            if (Size is { } size)
            {
                parameter.Size = size;
            }

            if (parameter.Direction is ParameterDirection.Input or ParameterDirection.InputOutput
                && parameter.Value is byte[] bytes)
            {
                ValidateValue(bytes);
            }
        }

        protected override string GenerateNonNullSqlLiteral(object value)
        {
            var bytes = (byte[])value;
            ValidateValue(bytes);
            if (_mode == OceanBaseCompatibilityMode.Oracle && bytes.Length > 2000)
            {
                throw new NotSupportedException("Oracle binary literals above 2,000 bytes require a parameter.");
            }

            return _mode == OceanBaseCompatibilityMode.Oracle
                ? $"HEXTORAW('{Convert.ToHexString(bytes)}')" : base.GenerateNonNullSqlLiteral(value);
        }

        private void ValidateValue(byte[] value)
        {
            if (_mode == OceanBaseCompatibilityMode.Oracle && value.Length == 0)
            {
                throw new NotSupportedException("Non-null empty Oracle binary inputs require a tested empty-BLOB transport.");
            }

            if (value.Length > (Size ?? MaximumLength(StoreTypeNameBase.ToUpperInvariant(), _mode)))
            {
                throw new ArgumentException("The binary value exceeds its declared storage length.", nameof(value));
            }
        }

        private static RelationalTypeMappingParameters ValidateParameters(RelationalTypeMappingParameters parameters,
            OceanBaseCompatibilityMode mode, bool? fixedLength)
        {
            var storeType = parameters.StoreType.Trim();
            var match = Regex.Match(storeType,
                @"\A(?<type>BINARY|VARBINARY|RAW|TINYBLOB|BLOB|MEDIUMBLOB|LONGBLOB)(?:\s*\(\s*(?<size>\d+)\s*\))?\z",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success || parameters.Precision is not null || parameters.Scale is not null)
            {
                throw new ArgumentException("The binary store declaration or facets are not supported.");
            }

            var type = match.Groups["type"].Value.ToUpperInvariant();
            var maximum = MaximumLength(type, mode);
            var bounded = type is "BINARY" or "VARBINARY" or "RAW";
            int? declaredSize = null;
            if (match.Groups["size"].Success)
            {
                if (!bounded || !int.TryParse(match.Groups["size"].Value, NumberStyles.None,
                        CultureInfo.InvariantCulture, out var length))
                {
                    throw new ArgumentException("The binary store length is not supported.");
                }

                declaredSize = length;
            }
            else if (bounded)
            {
                if (type != "BINARY") throw new ArgumentException("The binary store type requires an explicit length.");
                declaredSize = 1;
            }

            var fixedStore = type == "BINARY";
            if (fixedLength is { } configured && configured != fixedStore
                || declaredSize is not null && parameters.Size is not null && declaredSize != parameters.Size)
            {
                throw new ArgumentException("The explicit binary store type conflicts with its configured facets.");
            }

            var size = declaredSize ?? parameters.Size;
            if (size is < 1 || size > maximum)
            {
                throw new ArgumentOutOfRangeException(nameof(parameters), "The binary length is outside the supported store range.");
            }

            return new RelationalTypeMappingParameters(parameters.CoreParameters, storeType,
                StoreTypePostfix.None, System.Data.DbType.Binary, unicode: false, size: size, fixedLength: fixedStore);
        }

        private static int MaximumLength(string type, OceanBaseCompatibilityMode mode)
            => (mode, type) switch
            {
                (OceanBaseCompatibilityMode.MySql, "BINARY") => 255,
                (OceanBaseCompatibilityMode.MySql, "VARBINARY") => 262144,
                (OceanBaseCompatibilityMode.MySql, "TINYBLOB") => 255,
                (OceanBaseCompatibilityMode.MySql, "BLOB") => 65535,
                (OceanBaseCompatibilityMode.MySql, "MEDIUMBLOB") => 16777215,
                (OceanBaseCompatibilityMode.MySql, "LONGBLOB") => 536870910,
                (OceanBaseCompatibilityMode.Oracle, "RAW") => 2000,
                (OceanBaseCompatibilityMode.Oracle, "BLOB") => int.MaxValue,
                _ => throw new ArgumentException("The binary store type is not supported in this compatibility mode.")
            };
    }
}
