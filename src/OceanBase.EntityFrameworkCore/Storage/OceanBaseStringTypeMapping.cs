using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Storage;

namespace OceanBase.EntityFrameworkCore.Storage
{
    internal sealed class OceanBaseStringTypeMapping : StringTypeMapping
    {
        public OceanBaseStringTypeMapping(string storeType, DbType dbType, bool unicode, int? size, bool fixedLength)
            : base(new RelationalTypeMappingParameters(new CoreTypeMappingParameters(typeof(string)),
                storeType, StoreTypePostfix.None, dbType, unicode, size, fixedLength))
        {
        }

        private OceanBaseStringTypeMapping(RelationalTypeMappingParameters parameters)
            : base(parameters)
        {
        }

        protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
            => new OceanBaseStringTypeMapping(parameters);

        protected override void ConfigureParameter(DbParameter parameter)
        {
            if (Size is { } size)
            {
                parameter.Size = size;
            }
        }
    }
}
