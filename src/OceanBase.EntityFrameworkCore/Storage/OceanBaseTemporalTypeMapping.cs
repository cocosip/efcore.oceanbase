using System;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Json;
using OceanBaseConnector;

namespace OceanBase.EntityFrameworkCore.Storage
{
    internal sealed class OceanBaseTemporalTypeMapping<T> : RelationalTypeMapping, IOceanBaseWriteValidation where T : struct
    {
        private const long MaximumMySqlTimeTicks = (838 * 3600L + 59 * 60 + 59) * TimeSpan.TicksPerSecond + 9999990;
        private readonly OceanBaseCompatibilityMode _mode;
        public OceanBaseCompatibilityMode CompatibilityMode => _mode;
        private readonly byte _leadingPrecision;

        private bool DateOnlyTransport => DbType == System.Data.DbType.Date
            && (_mode == OceanBaseCompatibilityMode.MySql || Converter?.ModelClrType == typeof(DateOnly));

        private int TransportPrecision => DateOnlyTransport ? 0 : Math.Max(Precision ?? 0,
            _mode == OceanBaseCompatibilityMode.MySql ? 6 : 7);

        public OceanBaseTemporalTypeMapping(string storeType, DbType dbType, int precision,
            OceanBaseCompatibilityMode mode, byte leadingPrecision = 0)
            : base(new RelationalTypeMappingParameters(new CoreTypeMappingParameters(typeof(T),
                jsonValueReaderWriter: JsonReaderWriter), storeType,
                StoreTypePostfix.None, dbType, precision: precision))
        {
            _mode = mode;
            _leadingPrecision = leadingPrecision;
        }

        private static JsonValueReaderWriter? JsonReaderWriter
            => typeof(T) == typeof(DateTime) ? JsonDateTimeReaderWriter.Instance
                : typeof(T) == typeof(DateTimeOffset) ? JsonDateTimeOffsetReaderWriter.Instance
                : typeof(T) == typeof(TimeSpan) ? JsonTimeSpanReaderWriter.Instance
                : typeof(T) == typeof(OceanBaseIntervalDayToSecond) ? OceanBaseIntervalJsonReaderWriter.Instance
                : null;

        private OceanBaseTemporalTypeMapping(RelationalTypeMappingParameters parameters,
            OceanBaseCompatibilityMode mode, byte leadingPrecision) : base(parameters)
        {
            _mode = mode;
            _leadingPrecision = leadingPrecision;
        }

        protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        {
            if (parameters.DbType != DbType || parameters.StoreTypePostfix != StoreTypePostfix.None)
            {
                throw new ArgumentException("Temporal clones must preserve their DbType and complete store declaration.");
            }

            if (parameters.Size is not null || parameters.FixedLength)
            {
                throw new ArgumentException("Temporal mappings do not support size or fixed-length facets.");
            }

            var date = DbType == System.Data.DbType.Date;
            var duration = typeof(T) == typeof(TimeSpan) || typeof(T) == typeof(OceanBaseIntervalDayToSecond);
            var precision = parameters.Precision ?? Precision ?? (date ? 0 : _mode == OceanBaseCompatibilityMode.Oracle ? 7 : 6);
            if (precision < 0 || precision > (date ? 0 : _mode == OceanBaseCompatibilityMode.Oracle ? 9 : 6))
            {
                throw new ArgumentOutOfRangeException(nameof(parameters), "The temporal precision is outside the supported store range.");
            }

            if (parameters.Scale is not null)
            {
                throw new ArgumentException("Temporal fractional seconds use Precision, not a separate Scale facet.");
            }

            parameters = parameters.WithPrecisionAndScale(precision, null);
            if (precision != Precision && parameters.StoreType == StoreType)
            {
                var storeType = date ? "DATE" : typeof(T) == typeof(OceanBaseIntervalDayToSecond)
                    ? $"INTERVAL DAY({_leadingPrecision}) TO SECOND({precision})"
                    : _mode == OceanBaseCompatibilityMode.Oracle ? $"TIMESTAMP({precision})" + (typeof(T) == typeof(DateTimeOffset) ? " WITH TIME ZONE" : string.Empty)
                    : typeof(T) == typeof(TimeSpan) ? $"TIME({precision})" : $"DATETIME({precision})";
                parameters = parameters.WithStoreTypeAndSize(storeType, parameters.Size);
            }

            var match = OceanBaseTemporalTypeMapping.MatchDeclaration(parameters.StoreType, _mode, date, duration,
                typeof(T) == typeof(DateTimeOffset));
            if (!match.Success)
            {
                throw new NotSupportedException("The store declaration is incompatible with the current temporal transport.");
            }

            var declaredPrecision = OceanBaseTemporalTypeMapping.ReadFacet(match, "precision",
                date ? 0 : _mode == OceanBaseCompatibilityMode.Oracle ? 6 : 0);
            if (declaredPrecision != precision || duration && _mode == OceanBaseCompatibilityMode.Oracle
                && OceanBaseTemporalTypeMapping.ReadFacet(match, "days", 2) != _leadingPrecision)
            {
                throw new ArgumentException("The temporal declaration conflicts with its fractional or leading precision.");
            }

            return new OceanBaseTemporalTypeMapping<T>(parameters, _mode, _leadingPrecision);
        }

        public override MethodInfo GetDataReaderMethod()
            => typeof(DbDataReader).GetMethod(nameof(DbDataReader.GetValue))!;

        public override Expression CustomizeDataReaderExpression(Expression expression)
            => Expression.Call(typeof(OceanBaseTemporalTypeMapping<T>).GetMethod(nameof(ReadValue))!, expression);

        public static T ReadValue(object value)
            => (T)OceanBaseTemporalConversions.ConvertValue(value, typeof(T));

        protected override void ConfigureParameter(DbParameter parameter)
        {
            var input = parameter.Direction is ParameterDirection.Input or ParameterDirection.InputOutput;
            parameter.Scale = checked((byte)(input ? TransportPrecision : Precision ?? 0));
            if (input && DbType == System.Data.DbType.Date && !DateOnlyTransport)
            {
                parameter.DbType = System.Data.DbType.DateTime2;
            }
            if (typeof(T) == typeof(OceanBaseIntervalDayToSecond))
            {
                ((OceanBaseParameter)parameter).OceanBaseDbType = OceanBaseDbType.IntervalDayToSecond;
                parameter.Precision = input ? (byte)9 : _leadingPrecision;
            }

            if (input && parameter.Value is { } value && value is not DBNull)
            {
                Validate(value, write: false);
            }
        }

        public void ValidateWriteValue(object? value)
        {
            if (Converter is { } converter) value = converter.ConvertToProvider(value);
            if (value is not null and not DBNull) Validate(value, write: true);
        }

        private void Validate(object value, bool write)
        {
            var precision = write ? Precision ?? 0 : TransportPrecision;
            long ticks;
            switch (value)
            {
                case DateTime date:
                    if (date.Kind != DateTimeKind.Unspecified)
                    {
                        throw new ArgumentException("Unzoned date/time values require DateTimeKind.Unspecified.");
                    }

                    if (DateOnlyTransport && date.TimeOfDay != TimeSpan.Zero)
                    {
                        throw new ArgumentException("A DATE cannot retain a time component.");
                    }

                    ticks = date.Ticks;
                    break;
                case DateTimeOffset date:
                    ticks = date.Ticks;
                    break;
                case TimeSpan time:
                    if (_mode == OceanBaseCompatibilityMode.MySql && (time.Ticks < -MaximumMySqlTimeTicks || time.Ticks > MaximumMySqlTimeTicks))
                    {
                        throw new ArgumentOutOfRangeException(nameof(value), "TIME exceeds 838:59:59.999999.");
                    }

                    ticks = time.Ticks;
                    break;
                case OceanBaseIntervalDayToSecond interval:
                    _ = new OceanBaseIntervalDayToSecond(interval.IsNegative, interval.Days, interval.Hours, interval.Minutes,
                        interval.Seconds, interval.Nanosecond, write ? _leadingPrecision : (byte)9, checked((byte)precision));
                    return;
                default:
                    throw new ArgumentException("The temporal parameter has an incompatible provider value.");
            }

            if (precision < 7 && ticks % (long)Math.Pow(10, 7 - precision) != 0)
            {
                throw new ArgumentException("The temporal precision would require truncation.");
            }
        }

        private string Fraction(long ticks)
        {
            var precision = TransportPrecision;
            return precision == 0 ? string.Empty
                : "." + Math.Abs(ticks % TimeSpan.TicksPerSecond).ToString("D7", CultureInfo.InvariantCulture)
                    .PadRight(precision, '0')[..precision];
        }

        public override Expression GenerateCodeLiteral(object value)
        {
            if (value is not OceanBaseIntervalDayToSecond interval) return base.GenerateCodeLiteral(value);
            return Expression.New(typeof(OceanBaseIntervalDayToSecond).GetConstructor(
                [typeof(bool), typeof(int), typeof(int), typeof(int), typeof(int), typeof(int), typeof(byte), typeof(byte)])!,
                Expression.Constant(interval.IsNegative), Expression.Constant(interval.Days), Expression.Constant(interval.Hours),
                Expression.Constant(interval.Minutes), Expression.Constant(interval.Seconds), Expression.Constant(interval.Nanosecond),
                Expression.Constant(interval.LeadingPrecision), Expression.Constant(interval.Scale));
        }

        protected override string GenerateNonNullSqlLiteral(object value)
        {
            Validate(value, write: false);
            switch (value)
            {
                case DateTime date:
                    if (DateOnlyTransport)
                    {
                        return _mode == OceanBaseCompatibilityMode.Oracle
                            ? $"TO_DATE('{date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}', 'YYYY-MM-DD HH24:MI:SS')"
                            : $"DATE '{date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}'";
                    }

                    return $"TIMESTAMP '{date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}{Fraction(date.Ticks)}'";
                case DateTimeOffset date:
                    var format = "YYYY-MM-DD HH24:MI:SS" + (TransportPrecision > 0 ? $".FF{TransportPrecision}" : string.Empty) + " TZH:TZM";
                    return $"TO_TIMESTAMP_TZ('{date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}{Fraction(date.Ticks)} {date.ToString("zzz", CultureInfo.InvariantCulture)}', '{format}')";
                case TimeSpan time:
                    var sign = time < TimeSpan.Zero ? "-" : string.Empty;
                    return string.Create(CultureInfo.InvariantCulture,
                        $"TIME '{sign}{Math.Abs(time.Days) * 24 + Math.Abs(time.Hours):00}:{Math.Abs(time.Minutes):00}:{Math.Abs(time.Seconds):00}{Fraction(time.Ticks)}'");
                case OceanBaseIntervalDayToSecond interval:
                    var exact = new OceanBaseIntervalDayToSecond(interval.IsNegative, interval.Days, interval.Hours, interval.Minutes,
                        interval.Seconds, interval.Nanosecond, 9, checked((byte)TransportPrecision));
                    return $"INTERVAL '{exact}' DAY(9) TO SECOND({TransportPrecision})";
                default:
                    throw new ArgumentException("The temporal literal has an incompatible provider value.");
            }
        }
    }

    internal static class OceanBaseTemporalTypeMapping
    {
        internal static Match MatchDeclaration(string storeType, OceanBaseCompatibilityMode mode, bool date, bool duration, bool zoned)
        {
            var oracle = mode == OceanBaseCompatibilityMode.Oracle;
            var pattern = date ? @"\ADATE\z"
                : duration && oracle ? @"\AINTERVAL\s+DAY(?:\((?<days>\d+)\))?\s+TO\s+SECOND(?:\((?<precision>\d+)\))?\z"
                : oracle ? zoned ? @"\ATIMESTAMP(?:\((?<precision>\d+)\))?\s+WITH\s+TIME\s+ZONE\z"
                    : @"\ATIMESTAMP(?:\((?<precision>\d+)\))?\z"
                : duration ? @"\ATIME(?:\((?<precision>\d+)\))?\z" : @"\ADATETIME(?:\((?<precision>\d+)\))?\z";
            return Regex.Match(storeType.Trim(), pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        internal static int ReadFacet(Match match, string name, int fallback)
        {
            if (!match.Groups[name].Success) return fallback;
            if (!int.TryParse(match.Groups[name].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var precision))
            {
                throw new ArgumentOutOfRangeException(nameof(match), "The temporal declaration exceeds supported precision.");
            }

            return precision;
        }
    }
}
