using System;
using System.Data;
using System.Data.Common;
using System.Text;
using Microsoft.EntityFrameworkCore.Storage;

namespace OceanBase.EntityFrameworkCore.Storage
{
    internal sealed class OceanBaseStringTypeMapping : StringTypeMapping
    {
        private readonly OceanBaseCompatibilityMode _mode;

        public OceanBaseStringTypeMapping(string storeType, DbType dbType, bool unicode, int? size, bool fixedLength,
            OceanBaseCompatibilityMode mode)
            : base(new RelationalTypeMappingParameters(new CoreTypeMappingParameters(typeof(string)),
                storeType, StoreTypePostfix.None, dbType, unicode, size, fixedLength))
        {
            _mode = mode;
        }

        private OceanBaseStringTypeMapping(RelationalTypeMappingParameters parameters, OceanBaseCompatibilityMode mode)
            : base(parameters)
        {
            _mode = mode;
        }

        protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
            => new OceanBaseStringTypeMapping(parameters, _mode);

        protected override string GenerateNonNullSqlLiteral(object value)
        {
            var text = (string)value;
            if (_mode == OceanBaseCompatibilityMode.MySql)
            {
                foreach (var character in text)
                {
                    if (character == '\\' || char.IsControl(character))
                    {
                        return $"CONVERT(X'{Convert.ToHexString(Encoding.UTF8.GetBytes(text))}' USING utf8mb4)";
                    }
                }
            }

            return (_mode == OceanBaseCompatibilityMode.Oracle && IsUnicode ? "N" : string.Empty)
                + base.GenerateNonNullSqlLiteral(value);
        }

        protected override void ConfigureParameter(DbParameter parameter)
        {
            if (Size is { } size)
            {
                parameter.Size = size;
            }
        }
    }
}
