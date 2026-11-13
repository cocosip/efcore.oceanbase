using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBase.EntityFrameworkCore.Storage;
using OceanBaseConnector;

namespace OceanBase.EntityFrameworkCore.Design
{
    internal static class OceanBaseScaffoldingTypes
    {
        internal static OceanBaseCompatibilityMode Mode(string connectionString)
            => new OceanBaseConnectionStringBuilder(connectionString).ExpectedMode switch
            {
                OceanBaseConnector.OceanBaseCompatibilityMode.MySql => OceanBaseCompatibilityMode.MySql,
                OceanBaseConnector.OceanBaseCompatibilityMode.Oracle => OceanBaseCompatibilityMode.Oracle,
                _ => throw new ArgumentException("Reverse engineering requires ExpectedMode=MySql or ExpectedMode=Oracle in the connection string.")
            };

        internal static Type ClrType(string storeType, OceanBaseCompatibilityMode mode)
        {
            var type = Regex.Replace(storeType.ToUpperInvariant(), @"\s*\([^)]*\)", "").Trim();
            var unsigned = type.EndsWith(" UNSIGNED", StringComparison.Ordinal);
            if (unsigned) type = type[..^9].TrimEnd();
            if (mode == OceanBaseCompatibilityMode.MySql)
            {
                switch (type)
                {
                    case "TINYINT": return unsigned ? typeof(byte) : typeof(sbyte);
                    case "SMALLINT": return unsigned ? typeof(ushort) : typeof(short);
                    case "MEDIUMINT": case "INT": case "INTEGER": return unsigned ? typeof(uint) : typeof(int);
                    case "BIGINT": return unsigned ? typeof(ulong) : typeof(long);
                    case "DECIMAL": case "NUMERIC": return typeof(decimal);
                    case "FLOAT": return typeof(float);
                    case "DOUBLE": case "DOUBLE PRECISION": case "REAL": return typeof(double);
                    case "CHAR": case "VARCHAR": case "TINYTEXT": case "TEXT": case "MEDIUMTEXT": case "LONGTEXT": return typeof(string);
                    case "BINARY": case "VARBINARY": case "TINYBLOB": case "BLOB": case "MEDIUMBLOB": case "LONGBLOB": return typeof(byte[]);
                    case "DATE": return typeof(DateOnly);
                    case "DATETIME": case "TIMESTAMP": return typeof(DateTime);
                    case "TIME": return typeof(TimeSpan);
                }
            }
            else
            {
                switch (type)
                {
                    case "NUMBER": case "DECIMAL": case "NUMERIC":
                        var number = Regex.Match(storeType, @"\(\s*(\d+)\s*(?:,\s*0\s*)?\)");
                        if (number.Success && int.Parse(number.Groups[1].Value, CultureInfo.InvariantCulture) is var precision)
                        {
                            if (precision <= 9) return typeof(int);
                            if (precision <= 18) return typeof(long);
                        }
                        return typeof(decimal);
                    case "BINARY_FLOAT": return typeof(float);
                    case "BINARY_DOUBLE": return typeof(double);
                    case "CHAR": case "VARCHAR": case "VARCHAR2": case "NCHAR": case "NVARCHAR2": case "CLOB": case "NCLOB": return typeof(string);
                    case "RAW": case "BLOB": return typeof(byte[]);
                    case "DATE": case "TIMESTAMP": return typeof(DateTime);
                    case "TIMESTAMP WITH TIME ZONE": return typeof(DateTimeOffset);
                    case "INTERVAL DAY TO SECOND": return typeof(TimeSpan);
                }
            }
            throw new NotSupportedException("Reverse engineering does not support store type '" + storeType + "'. Select supported tables or map that type explicitly.");
        }
    }

    // Context-free EF design services must resolve mappings without keeping the last tenant's mode in a singleton.
    internal sealed class OceanBaseScaffoldingTypeMappingSource : RelationalTypeMappingSource
    {
        private readonly OceanBaseTypeMappingSource _mysql;
        private readonly OceanBaseTypeMappingSource _oracle;

        public OceanBaseScaffoldingTypeMappingSource(TypeMappingSourceDependencies dependencies,
            RelationalTypeMappingSourceDependencies relationalDependencies) : base(dependencies, relationalDependencies)
        {
            _mysql = new(dependencies, relationalDependencies, OceanBaseCompatibilityMode.MySql);
            _oracle = new(dependencies, relationalDependencies, OceanBaseCompatibilityMode.Oracle);
        }

        internal IRelationalTypeMappingSource ForMode(OceanBaseCompatibilityMode mode)
            => mode == OceanBaseCompatibilityMode.Oracle ? _oracle : _mysql;

        protected override RelationalTypeMapping? FindMapping(in RelationalTypeMappingInfo mappingInfo)
        {
            // EF compiled models serialize Oracle TimeOnly/TimeSpan sentinels in their provider representation.
            if (mappingInfo.ClrType == typeof(OceanBaseIntervalDayToSecond))
                return new OceanBaseTemporalTypeMapping<OceanBaseIntervalDayToSecond>("INTERVAL DAY(9) TO SECOND(9)",
                    System.Data.DbType.Object, 9, OceanBaseCompatibilityMode.Oracle, 9);
            var store = mappingInfo.StoreTypeName;
            var oracle = store is not null && (Regex.IsMatch(store,
                @"\A(?:NUMBER|NVARCHAR2|VARCHAR2|NCHAR|CLOB|NCLOB|RAW|BINARY_FLOAT|BINARY_DOUBLE|INTERVAL|TIMESTAMP)\b|\b(?:BYTE|CHAR)\s*\)\z",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                || store.Equals("DATE", StringComparison.OrdinalIgnoreCase) && mappingInfo.ClrType == typeof(DateTime)
                || Regex.IsMatch(store, @"\A(?:NUMERIC|DECIMAL)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                    && mappingInfo.ClrType is { } clr && clr != typeof(decimal));
            var mode = oracle ? OceanBaseCompatibilityMode.Oracle : OceanBaseCompatibilityMode.MySql;
            var type = mappingInfo.ClrType ?? (store is null ? null : OceanBaseScaffoldingTypes.ClrType(store, mode));
            return type is null ? null : (oracle ? _oracle : _mysql).FindMapping(type, store);
        }
    }
}
