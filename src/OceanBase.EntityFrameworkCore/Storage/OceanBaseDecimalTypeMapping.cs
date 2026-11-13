using System;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Json;
using OceanBaseConnector;

namespace OceanBase.EntityFrameworkCore.Storage
{
    internal sealed class OceanBaseDecimalTypeMapping : DecimalTypeMapping, IOceanBaseWriteValidation
    {
        private readonly OceanBaseCompatibilityMode _mode;
        public OceanBaseCompatibilityMode CompatibilityMode => _mode;
        private readonly bool _queryResult;

        public OceanBaseDecimalTypeMapping(string storeType, int? precision, int? scale,
            int? size, bool fixedLength, OceanBaseCompatibilityMode mode)
            : this(new RelationalTypeMappingParameters(
                new CoreTypeMappingParameters(typeof(decimal), jsonValueReaderWriter: JsonDecimalReaderWriter.Instance),
                storeType, StoreTypePostfix.None, System.Data.DbType.Decimal,
                size: size, fixedLength: fixedLength, precision: precision, scale: scale), mode)
        {
        }

        private OceanBaseDecimalTypeMapping(RelationalTypeMappingParameters parameters, OceanBaseCompatibilityMode mode,
            bool preserveBareDeclaration = false, bool queryResult = false)
            : base(ValidateParameters(parameters, mode, preserveBareDeclaration))
        {
            _mode = mode;
            _queryResult = queryResult;
        }

        protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        {
            if (parameters.StoreType == StoreType && (parameters.Precision != Precision || parameters.Scale != Scale))
            {
                var name = Regex.Match(StoreType, @"\A\w+").Value;
                parameters = parameters.WithStoreTypeAndSize(name
                    + (StoreType.EndsWith("UNSIGNED", StringComparison.OrdinalIgnoreCase) ? " UNSIGNED" : string.Empty), parameters.Size);
            }

            return new OceanBaseDecimalTypeMapping(parameters, _mode,
                _mode == OceanBaseCompatibilityMode.Oracle && parameters.StoreType == StoreType
                && parameters.Precision == Precision && parameters.Scale == Scale, _queryResult);
        }

        internal OceanBaseDecimalTypeMapping AsQueryResult()
            => _queryResult ? this : new OceanBaseDecimalTypeMapping(Parameters, _mode, preserveBareDeclaration: true, queryResult: true);

        public override MethodInfo GetDataReaderMethod()
            => _queryResult ? typeof(DbDataReader).GetMethod(nameof(DbDataReader.GetValue))! : base.GetDataReaderMethod();

        public override Expression CustomizeDataReaderExpression(Expression expression)
            => _queryResult ? Expression.Call(typeof(OceanBaseDecimalTypeMapping).GetMethod(nameof(ReadQueryValue))!, expression)
                : base.CustomizeDataReaderExpression(expression);

        public static decimal ReadQueryValue(object value)
            => (decimal)OceanBaseNumericConversion.ConvertValue(value, typeof(decimal));

        protected override void ConfigureParameter(DbParameter parameter)
        {
            // Query operands inherit column mappings, but must retain their own precision and scale.
            if (parameter.Direction is ParameterDirection.Input or ParameterDirection.InputOutput) return;
            if (Precision is { } precision)
            {
                parameter.Precision = checked((byte)precision);
            }

            if (Scale is { } scale)
            {
                ((OceanBaseParameter)parameter).DecimalScale = scale;
            }
        }

        public void ValidateWriteValue(object? value)
        {
            if (Converter is { } converter) value = converter.ConvertToProvider(value);
            if (value is null or DBNull) return;
            var number = new OceanBaseDecimal((decimal)value);
            var coefficient = number.Coefficient;
            if (StoreType.EndsWith("UNSIGNED", StringComparison.OrdinalIgnoreCase) && coefficient.Sign < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "A negative decimal cannot be written to unsigned storage.");
            }

            if (Scale is { } scale)
            {
                var difference = scale - number.Scale;
                if (difference >= 0) coefficient *= BigInteger.Pow(10, difference);
                else
                {
                    coefficient = BigInteger.DivRem(coefficient, BigInteger.Pow(10, -difference), out var remainder);
                    if (!remainder.IsZero)
                    {
                        throw new ArgumentException("The decimal value would require rounding to its declared scale.", nameof(value));
                    }
                }
            }

            if (Precision is { } precision && BigInteger.Abs(coefficient) >= BigInteger.Pow(10, precision))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "The decimal value exceeds its declared precision.");
            }
        }

        private static RelationalTypeMappingParameters ValidateParameters(RelationalTypeMappingParameters parameters,
            OceanBaseCompatibilityMode mode, bool preserveBareDeclaration)
        {
            if (parameters.Size is not null || parameters.FixedLength)
            {
                throw new ArgumentException("Decimal mappings do not support size or fixed-length facets.");
            }

            var oracle = mode == OceanBaseCompatibilityMode.Oracle;
            var storeType = parameters.StoreType.Trim();
            var match = Regex.Match(storeType,
                @"\A(?<type>NUMBER|DECIMAL|NUMERIC)(?:\s*\(\s*(?<precision>\d+)\s*(?:,\s*(?<scale>-?\d+)\s*)?\))?(?<unsigned>\s+UNSIGNED)?\z",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success || !oracle && match.Groups["type"].Value.Equals("NUMBER", StringComparison.OrdinalIgnoreCase)
                || oracle && match.Groups["unsigned"].Success)
            {
                throw new ArgumentException("The decimal store declaration is not supported in this compatibility mode.");
            }

            var precision = parameters.Precision;
            var scale = parameters.Scale;
            if (match.Groups["precision"].Success)
            {
                if (!int.TryParse(match.Groups["precision"].Value, out var declaredPrecision)
                    || !int.TryParse(match.Groups["scale"].Success ? match.Groups["scale"].Value : "0", out var declaredScale))
                {
                    throw new ArgumentOutOfRangeException(nameof(parameters), "The decimal declaration exceeds supported numeric facets.");
                }

                if (precision is not null && precision != declaredPrecision || scale is not null && scale != declaredScale)
                {
                    throw new ArgumentException("The explicit decimal declaration conflicts with its configured precision or scale.");
                }

                precision = declaredPrecision;
                scale = declaredScale;
            }
            else if (precision is not null || scale is not null)
            {
                precision ??= oracle ? 38 : 65;
                scale ??= 0;
                if (!preserveBareDeclaration)
                {
                    storeType = string.Create(CultureInfo.InvariantCulture,
                        $"{match.Groups["type"].Value}({precision},{scale})")
                        + (match.Groups["unsigned"].Success ? " UNSIGNED" : string.Empty);
                }
            }
            else if (!oracle || !match.Groups["type"].Value.Equals("NUMBER", StringComparison.OrdinalIgnoreCase))
            {
                precision = oracle ? 38 : 10;
                scale = 0;
            }

            if (precision is < 1 || precision > (oracle ? 38 : 65)
                || scale < (oracle ? -84 : 0) || scale > (oracle ? 127 : 30) || !oracle && scale > precision)
            {
                throw new ArgumentOutOfRangeException(nameof(parameters), "Decimal precision or scale is outside the supported store range.");
            }

            return new RelationalTypeMappingParameters(parameters.CoreParameters, storeType, StoreTypePostfix.None,
                parameters.DbType, precision: precision, scale: scale);
        }
    }
}
