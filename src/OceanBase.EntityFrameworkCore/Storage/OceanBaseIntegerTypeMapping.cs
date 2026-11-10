using System;
using System.Data;
using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Storage;

namespace OceanBase.EntityFrameworkCore.Storage
{
    internal sealed class OceanBaseIntegerTypeMapping<T> : RelationalTypeMapping where T : struct, IConvertible
    {
        public OceanBaseIntegerTypeMapping(string storeType, DbType dbType)
            : base(storeType, typeof(T), dbType)
        {
        }

        private OceanBaseIntegerTypeMapping(RelationalTypeMappingParameters parameters) : base(parameters)
        {
        }

        protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
            => new OceanBaseIntegerTypeMapping<T>(parameters);

        public override MethodInfo GetDataReaderMethod()
            => typeof(DbDataReader).GetMethod(nameof(DbDataReader.GetValue))!;

        public override Expression CustomizeDataReaderExpression(Expression expression)
            => Expression.Call(typeof(OceanBaseIntegerTypeMapping<T>).GetMethod(nameof(ReadValue))!, expression);

        public static T ReadValue(object value)
            => (T)OceanBaseNumericConversion.ConvertValue(value, typeof(T));

        protected override string GenerateNonNullSqlLiteral(object value)
            => value is bool boolean ? boolean ? "1" : "0" : base.GenerateNonNullSqlLiteral(value);
    }
}
