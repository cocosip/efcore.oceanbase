using System;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Json;

namespace OceanBase.EntityFrameworkCore.Storage
{
    internal sealed class OceanBaseIntegerTypeMapping<T> : RelationalTypeMapping, IOceanBaseWriteValidation where T : struct, IConvertible
    {
        private readonly OceanBaseCompatibilityMode _mode;
        private readonly decimal _minimum;
        private readonly decimal _maximum;

        public OceanBaseIntegerTypeMapping(string storeType, DbType dbType, OceanBaseCompatibilityMode mode,
            int? precision = null, int? scale = null, int? size = null, bool fixedLength = false)
            : this(new RelationalTypeMappingParameters(new CoreTypeMappingParameters(typeof(T),
                jsonValueReaderWriter: JsonReaderWriter),
                storeType, StoreTypePostfix.None, dbType, size: size, fixedLength: fixedLength,
                precision: precision, scale: scale), mode)
        {
        }

        // EF also needs element JSON metadata when expanding a captured collection into scalar parameters.
        private static JsonValueReaderWriter JsonReaderWriter
            => typeof(T) == typeof(sbyte) ? JsonSByteReaderWriter.Instance
                : typeof(T) == typeof(byte) ? JsonByteReaderWriter.Instance
                : typeof(T) == typeof(short) ? JsonInt16ReaderWriter.Instance
                : typeof(T) == typeof(ushort) ? JsonUInt16ReaderWriter.Instance
                : typeof(T) == typeof(int) ? JsonInt32ReaderWriter.Instance
                : typeof(T) == typeof(uint) ? JsonUInt32ReaderWriter.Instance
                : typeof(T) == typeof(long) ? JsonInt64ReaderWriter.Instance
                : typeof(T) == typeof(ulong) ? JsonUInt64ReaderWriter.Instance
                : typeof(T) == typeof(bool) ? JsonBoolReaderWriter.Instance
                : typeof(T) == typeof(decimal) ? JsonDecimalReaderWriter.Instance
                : throw new InvalidOperationException($"Unsupported integer carrier {typeof(T)}.");

        private OceanBaseIntegerTypeMapping(RelationalTypeMappingParameters parameters, OceanBaseCompatibilityMode mode,
            bool preserveBareDeclaration = false)
            : base(ValidateParameters(parameters, mode, preserveBareDeclaration))
        {
            _mode = mode;
            (_minimum, _maximum) = GetRange(StoreType, Precision, mode);
        }

        protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        {
            if (_mode == OceanBaseCompatibilityMode.Oracle && parameters.Precision != Precision
                && parameters.StoreType == StoreType)
            {
                var alias = Regex.Match(StoreType, @"\A(?:DECIMAL|NUMERIC)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                parameters = parameters.WithStoreTypeAndSize(alias.Success ? alias.Value : "NUMBER", null);
            }

            return new OceanBaseIntegerTypeMapping<T>(parameters, _mode,
                parameters.StoreType == StoreType && parameters.Precision == Precision && parameters.Scale == Scale);
        }

        public override MethodInfo GetDataReaderMethod()
            => typeof(DbDataReader).GetMethod(nameof(DbDataReader.GetValue))!;

        public override Expression CustomizeDataReaderExpression(Expression expression)
            => Expression.Call(typeof(OceanBaseIntegerTypeMapping<T>).GetMethod(nameof(ReadValue))!, expression);

        public static T ReadValue(object value)
            => (T)OceanBaseNumericConversion.ConvertValue(value, typeof(T));

        protected override void ConfigureParameter(DbParameter parameter)
        {
            if (Precision is { } precision) parameter.Precision = checked((byte)precision);
            if (Scale is { } scale) parameter.Scale = checked((byte)scale);
        }

        protected override string GenerateNonNullSqlLiteral(object value)
        {
            return value is bool boolean ? boolean ? "1" : "0" : base.GenerateNonNullSqlLiteral(value);
        }

        public void ValidateWriteValue(object? value)
        {
            if (Converter is { } converter) value = converter.ConvertToProvider(value);
            if (value is null or DBNull) return;
            var number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            if (decimal.Truncate(number) != number || number < _minimum || number > _maximum)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "The integer value is outside its declared store range.");
            }
        }

        private static Match ParseMySql(string storeType)
            => Regex.Match(storeType,
                @"\A(?<type>TINYINT|BOOL|BOOLEAN|SMALLINT|MEDIUMINT|INT|INTEGER|BIGINT)(?:\s*\(\s*(?<width>\d+)\s*\))?(?:\s+(?<unsigned>UNSIGNED))?\z",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static RelationalTypeMappingParameters ValidateParameters(RelationalTypeMappingParameters parameters,
            OceanBaseCompatibilityMode mode, bool preserveBareDeclaration)
        {
            var storeType = parameters.StoreType.Trim();
            if (parameters.FixedLength) throw new ArgumentException("Integer mappings do not support fixed-length facets.");
            int? precision = null;
            int? scale = null;
            if (mode == OceanBaseCompatibilityMode.MySql)
            {
                var match = ParseMySql(storeType);
                if (!match.Success || parameters.Precision is not null || parameters.Scale is not null)
                {
                    throw new ArgumentException("The MySQL integer declaration or numeric facets are not supported.");
                }

                int? width = null;
                if (match.Groups["width"].Success)
                {
                    if (!int.TryParse(match.Groups["width"].Value, out var display) || display is < 1 or > 255)
                    {
                        throw new ArgumentOutOfRangeException(nameof(parameters), "Integer display width must be between 1 and 255.");
                    }

                    width = display;
                }

                if (parameters.Size is not null && parameters.Size != width)
                {
                    throw new ArgumentException("Integer display width is not an EF length facet.");
                }
            }
            else
            {
                var match = Regex.Match(storeType,
                    @"\A(?<type>NUMBER|DECIMAL|NUMERIC|INT|INTEGER|SMALLINT)(?:\s*\(\s*(?<precision>\d+)(?:\s*,\s*(?<scale>[+-]?\d+))?\s*\))?\z",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (!match.Success) throw new ArgumentException("The Oracle integer store declaration is not supported.");
                var type = match.Groups["type"].Value.ToUpperInvariant();
                var alias = type is "INT" or "INTEGER" or "SMALLINT";
                int? declaredPrecision = null;
                int? declaredScale = null;
                if (match.Groups["precision"].Success)
                {
                    if (alias || !int.TryParse(match.Groups["precision"].Value, out var digits) || digits is < 1 or > 38)
                    {
                        throw new ArgumentOutOfRangeException(nameof(parameters), "Oracle integer precision must be between 1 and 38.");
                    }

                    declaredPrecision = digits;
                }

                if (match.Groups["scale"].Success)
                {
                    if (!int.TryParse(match.Groups["scale"].Value, out var places) || places != 0)
                    {
                        throw new ArgumentException("Oracle integer storage requires zero scale.");
                    }

                    declaredScale = places;
                }

                precision = declaredPrecision ?? (alias ? 38 : parameters.Precision ?? (type is "DECIMAL" or "NUMERIC" ? 38 : null));
                scale = 0;
                if (parameters.Precision is < 1 or > 38 || parameters.Scale is not null and not 0
                    || parameters.Precision is not null && precision != parameters.Precision
                    || declaredScale is not null && parameters.Scale is not null && declaredScale != parameters.Scale
                    || parameters.Size is not null && parameters.Size != declaredPrecision)
                {
                    throw new ArgumentException("The Oracle integer declaration conflicts with its configured facets.");
                }

                if (declaredPrecision is null && !alias && parameters.Precision is not null && !preserveBareDeclaration)
                {
                    storeType = $"{match.Groups["type"].Value}({precision},0)";
                }
            }

            return new RelationalTypeMappingParameters(parameters.CoreParameters, storeType, StoreTypePostfix.None,
                parameters.DbType, precision: precision, scale: scale);
        }

        private static (decimal Minimum, decimal Maximum) GetRange(string storeType, int? precision, OceanBaseCompatibilityMode mode)
        {
            if (mode == OceanBaseCompatibilityMode.Oracle)
            {
                if (precision is null or >= 20) return (decimal.MinValue, decimal.MaxValue);
                var bound = 1m;
                for (var digit = 0; digit < precision; digit++) bound *= 10;
                return (-bound + 1, bound - 1);
            }

            var match = ParseMySql(storeType);
            var unsigned = match.Groups["unsigned"].Success;
            return (match.Groups["type"].Value.ToUpperInvariant(), unsigned) switch
            {
                ("TINYINT" or "BOOL" or "BOOLEAN", false) => (sbyte.MinValue, sbyte.MaxValue),
                ("TINYINT" or "BOOL" or "BOOLEAN", true) => (byte.MinValue, byte.MaxValue),
                ("SMALLINT", false) => (short.MinValue, short.MaxValue),
                ("SMALLINT", true) => (ushort.MinValue, ushort.MaxValue),
                ("MEDIUMINT", false) => (-8388608, 8388607),
                ("MEDIUMINT", true) => (0, 16777215),
                ("INT" or "INTEGER", false) => (int.MinValue, int.MaxValue),
                ("INT" or "INTEGER", true) => (uint.MinValue, uint.MaxValue),
                ("BIGINT", false) => (long.MinValue, long.MaxValue),
                ("BIGINT", true) => (ulong.MinValue, ulong.MaxValue),
                _ => throw new ArgumentException("The MySQL integer store type is not supported.")
            };
        }
    }
}
