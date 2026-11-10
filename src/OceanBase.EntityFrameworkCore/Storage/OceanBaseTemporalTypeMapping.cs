using System;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBaseConnector;

namespace OceanBase.EntityFrameworkCore.Storage
{
    internal sealed class OceanBaseTemporalTypeMapping<T> : RelationalTypeMapping where T : struct
    {
        private const long MaximumMySqlTimeTicks = (838 * 3600L + 59 * 60 + 59) * TimeSpan.TicksPerSecond + 9999990;
        private readonly OceanBaseCompatibilityMode _mode;
        private readonly byte _leadingPrecision;

        public OceanBaseTemporalTypeMapping(string storeType, DbType dbType, int precision,
            OceanBaseCompatibilityMode mode, byte leadingPrecision = 0)
            : base(new RelationalTypeMappingParameters(new CoreTypeMappingParameters(typeof(T)), storeType,
                StoreTypePostfix.None, dbType, precision: precision))
        {
            _mode = mode;
            _leadingPrecision = leadingPrecision;
        }

        private OceanBaseTemporalTypeMapping(RelationalTypeMappingParameters parameters,
            OceanBaseCompatibilityMode mode, byte leadingPrecision) : base(parameters)
        {
            _mode = mode;
            _leadingPrecision = leadingPrecision;
        }

        protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        {
            var date = parameters.DbType == System.Data.DbType.Date;
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
            if (precision != Precision)
            {
                var storeType = date ? "DATE" : typeof(T) == typeof(OceanBaseIntervalDayToSecond)
                    ? $"INTERVAL DAY({_leadingPrecision}) TO SECOND({precision})"
                    : _mode == OceanBaseCompatibilityMode.Oracle ? $"TIMESTAMP({precision})" + (typeof(T) == typeof(DateTimeOffset) ? " WITH TIME ZONE" : string.Empty)
                    : typeof(T) == typeof(TimeSpan) ? $"TIME({precision})" : $"DATETIME({precision})";
                parameters = parameters.WithStoreTypeAndSize(storeType, parameters.Size);
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
            parameter.Scale = checked((byte)(Precision ?? 0));
            if (typeof(T) == typeof(OceanBaseIntervalDayToSecond))
            {
                ((OceanBaseParameter)parameter).OceanBaseDbType = OceanBaseDbType.IntervalDayToSecond;
                parameter.Precision = _leadingPrecision;
            }

            if (parameter.Direction is ParameterDirection.Input or ParameterDirection.InputOutput
                && parameter.Value is { } value && value is not DBNull)
            {
                Validate(value);
            }
        }

        private void Validate(object value)
        {
            long ticks;
            switch (value)
            {
                case DateTime date:
                    if (date.Kind != DateTimeKind.Unspecified)
                    {
                        throw new ArgumentException("Unzoned date/time values require DateTimeKind.Unspecified.");
                    }

                    if (_mode == OceanBaseCompatibilityMode.MySql && DbType == System.Data.DbType.Date && date.TimeOfDay != TimeSpan.Zero)
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
                        interval.Seconds, interval.Nanosecond, _leadingPrecision, checked((byte)(Precision ?? 0)));
                    return;
                default:
                    throw new ArgumentException("The temporal parameter has an incompatible provider value.");
            }

            if (Precision is < 7 && ticks % (long)Math.Pow(10, 7 - Precision.Value) != 0)
            {
                throw new ArgumentException("The temporal precision would require truncation.");
            }
        }

        private string Fraction(long ticks)
        {
            var precision = Precision ?? 0;
            return precision == 0 ? string.Empty
                : "." + Math.Abs(ticks % TimeSpan.TicksPerSecond).ToString("D7", CultureInfo.InvariantCulture)
                    .PadRight(precision, '0')[..precision];
        }

        protected override string GenerateNonNullSqlLiteral(object value)
        {
            Validate(value);
            switch (value)
            {
                case DateTime date:
                    if (DbType == System.Data.DbType.Date)
                    {
                        return _mode == OceanBaseCompatibilityMode.Oracle
                            ? $"TO_DATE('{date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}', 'YYYY-MM-DD HH24:MI:SS')"
                            : $"DATE '{date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}'";
                    }

                    return $"TIMESTAMP '{date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}{Fraction(date.Ticks)}'";
                case DateTimeOffset date:
                    var format = "YYYY-MM-DD HH24:MI:SS" + (Precision > 0 ? $".FF{Precision}" : string.Empty) + " TZH:TZM";
                    return $"TO_TIMESTAMP_TZ('{date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}{Fraction(date.Ticks)} {date.ToString("zzz", CultureInfo.InvariantCulture)}', '{format}')";
                case TimeSpan time:
                    var sign = time < TimeSpan.Zero ? "-" : string.Empty;
                    return string.Create(CultureInfo.InvariantCulture,
                        $"TIME '{sign}{Math.Abs(time.Days) * 24 + Math.Abs(time.Hours):00}:{Math.Abs(time.Minutes):00}:{Math.Abs(time.Seconds):00}{Fraction(time.Ticks)}'");
                case OceanBaseIntervalDayToSecond interval:
                    var exact = new OceanBaseIntervalDayToSecond(interval.IsNegative, interval.Days, interval.Hours, interval.Minutes,
                        interval.Seconds, interval.Nanosecond, _leadingPrecision, checked((byte)(Precision ?? 0)));
                    return $"INTERVAL '{exact}' DAY({_leadingPrecision}) TO SECOND({Precision})";
                default:
                    throw new ArgumentException("The temporal literal has an incompatible provider value.");
            }
        }
    }
}
