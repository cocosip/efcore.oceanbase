using System;
using System.Buffers;
using System.Data;
using System.Data.Common;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Json;
using OceanBaseConnector;

namespace OceanBase.EntityFrameworkCore.Storage
{
    internal sealed class OceanBaseStringTypeMapping : StringTypeMapping, IOceanBaseWriteValidation
    {
        private readonly OceanBaseCompatibilityMode _mode;
        public OceanBaseCompatibilityMode CompatibilityMode => _mode;

        public OceanBaseStringTypeMapping(string storeType, DbType dbType, bool unicode, int? size, bool fixedLength,
            OceanBaseCompatibilityMode mode)
            : this(new RelationalTypeMappingParameters(new CoreTypeMappingParameters(typeof(string),
                jsonValueReaderWriter: JsonStringReaderWriter.Instance),
                storeType, StoreTypePostfix.None, dbType, unicode, size, fixedLength), mode)
        {
        }

        private OceanBaseStringTypeMapping(RelationalTypeMappingParameters parameters, OceanBaseCompatibilityMode mode)
            : base(ValidateParameters(parameters, mode))
        {
            _mode = mode;
        }

        protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
            => new OceanBaseStringTypeMapping(parameters, _mode);

        protected override string GenerateNonNullSqlLiteral(object value)
        {
            var text = (string)value;
            CountCharacters(text);
            if (_mode == OceanBaseCompatibilityMode.MySql)
            {
                foreach (var character in text)
                {
                    if (character == '\\' || char.IsControl(character))
                    {
                        return $"CONVERT(X'{Convert.ToHexString(Encoding.UTF8.GetBytes(text))}' USING utf8mb4)";
                    }
                }
            }

            return (_mode == OceanBaseCompatibilityMode.Oracle && IsUnicode ? "N" : string.Empty)
                + base.GenerateNonNullSqlLiteral(value);
        }

        protected override void ConfigureParameter(DbParameter parameter)
        {
            if (_mode == OceanBaseCompatibilityMode.Oracle)
            {
                var type = StoreTypeNameBase.Trim().ToUpperInvariant();
                if (type is "NCHAR" or "NVARCHAR2")
                {
                    ((OceanBaseParameter)parameter).OceanBaseDbType = type == "NCHAR"
                        ? OceanBaseDbType.NChar : OceanBaseDbType.NVarchar2;
                }
            }

            if (Size is { } size)
            {
                parameter.Size = size;
            }

            if (parameter.Direction is ParameterDirection.Input or ParameterDirection.InputOutput
                && parameter.Value is string text)
            {
                CountCharacters(text);
                // A column mapping also binds query operands that can exceed the column length.
                parameter.Size = Math.Max(parameter.Size, text.Length);
            }
        }

        public void ValidateWriteValue(object? value)
        {
            if (Converter is { } converter) value = converter.ConvertToProvider(value);
            if (value is null or DBNull) return;
            var length = CountCharacters((string)value);
            if (Size is { } size && length > size)
            {
                throw new ArgumentException("The text value exceeds its declared character length.", nameof(value));
            }
        }

        private static int CountCharacters(string text)
        {
            var remaining = text.AsSpan();
            var count = 0;
            while (!remaining.IsEmpty)
            {
                if (Rune.DecodeFromUtf16(remaining, out _, out var consumed) != OperationStatus.Done)
                {
                    throw new ArgumentException("Text inputs must contain well-formed UTF-16.", nameof(text));
                }

                remaining = remaining[consumed..];
                count++;
            }

            return count;
        }

        private static RelationalTypeMappingParameters ValidateParameters(RelationalTypeMappingParameters parameters,
            OceanBaseCompatibilityMode mode)
        {
            var storeType = parameters.StoreType.Trim();
            var match = Regex.Match(storeType,
                @"\A(?<type>CHAR|VARCHAR|VARCHAR2|NCHAR|NVARCHAR2|TINYTEXT|TEXT|MEDIUMTEXT|LONGTEXT|CLOB|NCLOB)(?:\s*\(\s*(?<size>\d+)\s*(?<unit>CHAR|BYTE)?\s*\))?\z",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success || parameters.Precision is not null || parameters.Scale is not null)
            {
                throw new ArgumentException("The text store declaration or numeric facets are not supported.");
            }

            var type = match.Groups["type"].Value.ToUpperInvariant();
            var oracle = mode == OceanBaseCompatibilityMode.Oracle;
            if (oracle ? type is not ("CHAR" or "VARCHAR2" or "NCHAR" or "NVARCHAR2" or "CLOB" or "NCLOB")
                : type is not ("CHAR" or "VARCHAR" or "TINYTEXT" or "TEXT" or "MEDIUMTEXT" or "LONGTEXT"))
            {
                throw new ArgumentException("The text store type is not supported in this compatibility mode.");
            }

            var fixedLength = type is "CHAR" or "NCHAR";
            var bounded = fixedLength || type is "VARCHAR" or "VARCHAR2" or "NVARCHAR2";
            if (parameters.FixedLength != fixedLength
                || oracle && parameters.Unicode != (type is "NCHAR" or "NVARCHAR2" or "NCLOB")
                || match.Groups["unit"].Success && (!oracle || type is not ("CHAR" or "VARCHAR2"))
                || !bounded && match.Groups["size"].Success)
            {
                throw new ArgumentException("The text declaration conflicts with its configured facets.");
            }

            var size = parameters.Size;
            if (bounded)
            {
                if (!match.Groups["size"].Success && !fixedLength
                    || !int.TryParse(match.Groups["size"].Success ? match.Groups["size"].Value : "1", out var length))
                {
                    throw new ArgumentException("The text store type requires a supported explicit length.");
                }

                var maximum = oracle ? type == "CHAR" ? 2000 : type == "NCHAR" ? 1000
                    : type == "VARCHAR2" ? 4000 : 2000 : fixedLength ? 255 : 16383;
                if (length < 1 || length > maximum)
                {
                    throw new ArgumentOutOfRangeException(nameof(parameters), "The text length is outside the supported store profile.");
                }

                if (size is not null && size != length)
                {
                    throw new ArgumentException("The explicit text length conflicts with its configured size.");
                }

                size = length;
            }

            if (size is < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(parameters), "The text length must be positive.");
            }

            return new RelationalTypeMappingParameters(parameters.CoreParameters, storeType, StoreTypePostfix.None,
                parameters.DbType, parameters.Unicode, size, fixedLength);
        }
    }
}
