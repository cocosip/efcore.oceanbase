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
    internal sealed class OceanBaseFloatingPointTypeMapping<T> : RelationalTypeMapping, IOceanBaseWriteValidation
        where T : struct, IConvertible
    {
        private readonly OceanBaseCompatibilityMode _mode;
        private readonly bool _singleStorage;
        private readonly bool _unsigned;

        public OceanBaseFloatingPointTypeMapping(string storeType, OceanBaseCompatibilityMode mode,
            int? precision, int? scale, int? size, bool fixedLength)
            : this(new RelationalTypeMappingParameters(
                new CoreTypeMappingParameters(typeof(T), jsonValueReaderWriter: typeof(T) == typeof(float)
                    ? JsonFloatReaderWriter.Instance : JsonDoubleReaderWriter.Instance),
                storeType, StoreTypePostfix.None, typeof(T) == typeof(float) ? System.Data.DbType.Single : System.Data.DbType.Double,
                size: size, fixedLength: fixedLength, precision: precision, scale: scale), mode)
        {
        }

        private OceanBaseFloatingPointTypeMapping(RelationalTypeMappingParameters parameters, OceanBaseCompatibilityMode mode)
            : base(ValidateParameters(parameters, mode))
        {
            _mode = mode;
            _singleStorage = mode == OceanBaseCompatibilityMode.Oracle
                ? StoreType.Equals("BINARY_FLOAT", StringComparison.OrdinalIgnoreCase)
                : StoreType.StartsWith("FLOAT", StringComparison.OrdinalIgnoreCase) && (Precision ?? 24) <= 24;
            _unsigned = StoreType.EndsWith("UNSIGNED", StringComparison.OrdinalIgnoreCase);
        }

        protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        {
            if (_mode == OceanBaseCompatibilityMode.MySql && parameters.Precision != Precision
                && parameters.StoreType == StoreType && StoreType.StartsWith("FLOAT", StringComparison.OrdinalIgnoreCase))
            {
                parameters = parameters.WithStoreTypeAndSize("FLOAT" + (parameters.Precision is { } precision
                    ? $"({precision})" : string.Empty) + (_unsigned ? " UNSIGNED" : string.Empty), parameters.Size);
            }

            return new OceanBaseFloatingPointTypeMapping<T>(parameters, _mode);
        }

        public override MethodInfo GetDataReaderMethod()
            => typeof(DbDataReader).GetMethod(nameof(DbDataReader.GetValue))!;

        public override Expression CustomizeDataReaderExpression(Expression expression)
            => Expression.Call(typeof(OceanBaseFloatingPointTypeMapping<T>).GetMethod(nameof(ReadValue))!, expression);

        public static T ReadValue(object value)
            => (T)OceanBaseNumericConversion.ConvertValue(value, typeof(T));

        protected override void ConfigureParameter(DbParameter parameter)
        {
            if (parameter.Direction is ParameterDirection.Input or ParameterDirection.InputOutput
                && parameter.Value is { } value && value is not DBNull)
            {
                ValidateFinite(value);
            }
        }

        protected override string GenerateNonNullSqlLiteral(object value)
        {
            var number = ValidateFinite(value);
            if (_mode == OceanBaseCompatibilityMode.Oracle)
            {
                return typeof(T) == typeof(float) ? ((float)value).ToString("R", CultureInfo.InvariantCulture) + "F"
                    : number.ToString("R", CultureInfo.InvariantCulture) + "D";
            }

            var literal = number.ToString("R", CultureInfo.InvariantCulture);
            return literal.Contains('E') || literal.Contains('e') ? literal : literal + "E0";
        }

        public void ValidateWriteValue(object? value)
        {
            if (Converter is { } converter) value = converter.ConvertToProvider(value);
            if (value is null or DBNull) return;
            var number = ValidateFinite(value);
            if (_singleStorage && (number > float.MaxValue || number < -float.MaxValue) || _unsigned && number < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "The floating-point value is outside its declared store range.");
            }
        }

        private static double ValidateFinite(object value)
        {
            var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            if (!double.IsFinite(number))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Non-finite floating-point inputs are not supported.");
            }

            return number;
        }

        private static RelationalTypeMappingParameters ValidateParameters(RelationalTypeMappingParameters parameters,
            OceanBaseCompatibilityMode mode)
        {
            var storeType = parameters.StoreType.Trim();
            if (parameters.Size is not null || parameters.FixedLength || parameters.Scale is not null)
            {
                throw new ArgumentException("Floating-point mappings do not support size, fixed-length or scale facets.");
            }

            int? precision = null;
            if (mode == OceanBaseCompatibilityMode.Oracle)
            {
                if (parameters.Precision is not null || !storeType.Equals("BINARY_FLOAT", StringComparison.OrdinalIgnoreCase)
                    && !storeType.Equals("BINARY_DOUBLE", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException("Oracle binary floating-point storage requires BINARY_FLOAT or BINARY_DOUBLE without precision.");
                }
            }
            else
            {
                var match = Regex.Match(storeType,
                    @"\A(?<type>FLOAT|DOUBLE(?:\s+PRECISION)?)(?:\s*\(\s*(?<precision>\d+)\s*\))?(?:\s+UNSIGNED)?\z",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (!match.Success) throw new ArgumentException("The MySQL binary floating-point declaration is not supported.");
                var single = match.Groups["type"].Value.Equals("FLOAT", StringComparison.OrdinalIgnoreCase);
                if (match.Groups["precision"].Success)
                {
                    if (!single || !int.TryParse(match.Groups["precision"].Value, out var bits) || bits is < 0 or > 53)
                    {
                        throw new ArgumentOutOfRangeException(nameof(parameters), "FLOAT binary precision must be between 0 and 53.");
                    }

                    precision = bits;
                }
                else if (single && parameters.Precision is { } configured)
                {
                    if (configured is < 0 or > 53)
                    {
                        throw new ArgumentOutOfRangeException(nameof(parameters), "FLOAT binary precision must be between 0 and 53.");
                    }

                    precision = configured;
                    storeType = $"{match.Groups["type"].Value}({precision})"
                        + (storeType.EndsWith("UNSIGNED", StringComparison.OrdinalIgnoreCase) ? " UNSIGNED" : string.Empty);
                }

                if (parameters.Precision is not null && (parameters.Precision != precision || !single))
                {
                    throw new ArgumentException("The floating-point declaration conflicts with its configured binary precision.");
                }
            }

            return new RelationalTypeMappingParameters(parameters.CoreParameters, storeType, StoreTypePostfix.None,
                parameters.DbType, precision: precision);
        }
    }
}
