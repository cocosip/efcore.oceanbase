using System;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Storage;

namespace OceanBase.EntityFrameworkCore.Storage
{
    internal sealed class OceanBaseByteArrayTypeMapping : ByteArrayTypeMapping
    {
        private readonly OceanBaseCompatibilityMode _mode;

        public OceanBaseByteArrayTypeMapping(string storeType, int? size, OceanBaseCompatibilityMode mode)
            : base(storeType, System.Data.DbType.Binary, size)
        {
            _mode = mode;
        }

        private OceanBaseByteArrayTypeMapping(RelationalTypeMappingParameters parameters, OceanBaseCompatibilityMode mode)
            : base(parameters)
        {
            _mode = mode;
        }

        protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
            => new OceanBaseByteArrayTypeMapping(parameters, _mode);

        protected override void ConfigureParameter(DbParameter parameter)
        {
            if (Size is { } size)
            {
                parameter.Size = size;
            }
        }

        protected override string GenerateNonNullSqlLiteral(object value)
            => _mode == OceanBaseCompatibilityMode.Oracle
                ? $"HEXTORAW('{Convert.ToHexString((byte[])value)}')"
                : base.GenerateNonNullSqlLiteral(value);
    }
}
