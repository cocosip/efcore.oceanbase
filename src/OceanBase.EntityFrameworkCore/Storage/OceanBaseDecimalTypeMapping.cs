using System.Data.Common;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBaseConnector;

namespace OceanBase.EntityFrameworkCore.Storage
{
    internal sealed class OceanBaseDecimalTypeMapping : DecimalTypeMapping
    {
        public OceanBaseDecimalTypeMapping(string storeType, int? precision, int? scale)
            : base(storeType, System.Data.DbType.Decimal, precision, scale)
        {
        }

        private OceanBaseDecimalTypeMapping(RelationalTypeMappingParameters parameters)
            : base(parameters)
        {
        }

        protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
            => new OceanBaseDecimalTypeMapping(parameters);

        protected override void ConfigureParameter(DbParameter parameter)
        {
            if (Precision is { } precision)
            {
                parameter.Precision = checked((byte)precision);
            }

            if (Scale is { } scale)
            {
                ((OceanBaseParameter)parameter).DecimalScale = scale;
            }
        }
    }
}
