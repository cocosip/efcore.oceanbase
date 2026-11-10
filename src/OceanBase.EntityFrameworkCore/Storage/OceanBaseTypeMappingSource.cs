using System;
using System.Data;
using Microsoft.EntityFrameworkCore.Storage;

namespace OceanBase.EntityFrameworkCore.Storage;

internal sealed class OceanBaseTypeMappingSource : RelationalTypeMappingSource
{
    private readonly OceanBaseCompatibilityMode _compatibilityMode;

    internal OceanBaseTypeMappingSource(
        TypeMappingSourceDependencies dependencies,
        RelationalTypeMappingSourceDependencies relationalDependencies,
        OceanBaseCompatibilityMode compatibilityMode)
        : base(dependencies, relationalDependencies)
    {
        _compatibilityMode = compatibilityMode;
    }

    protected override RelationalTypeMapping? FindMapping(in RelationalTypeMappingInfo mappingInfo)
    {
        var clrType = mappingInfo.ClrType is { } declaredType
            ? Nullable.GetUnderlyingType(declaredType) ?? declaredType
            : null;
        var storeTypes = OceanBaseTypeMappings.For(_compatibilityMode);

        if (clrType == typeof(int)) return new IntTypeMapping(storeTypes.Integer, DbType.Int32);
        if (clrType == typeof(long)) return new LongTypeMapping(storeTypes.Long, DbType.Int64);
        if (clrType == typeof(decimal)) return new DecimalTypeMapping(storeTypes.Decimal, DbType.Decimal, mappingInfo.Precision, mappingInfo.Scale);
        if (clrType == typeof(string)) return new StringTypeMapping(storeTypes.Text, DbType.String, unicode: true, size: mappingInfo.Size);
        if (clrType == typeof(byte[])) return new ByteArrayTypeMapping(storeTypes.Binary, DbType.Binary, mappingInfo.Size);
        if (clrType == typeof(DateTime)) return new DateTimeTypeMapping(storeTypes.DateTime, DbType.DateTime2);
        if (clrType == typeof(DateTimeOffset)) return new DateTimeOffsetTypeMapping(storeTypes.DateTimeOffset, DbType.DateTimeOffset);
        if (clrType == typeof(DateOnly)) return new DateOnlyTypeMapping(storeTypes.Date, DbType.Date);
        if (clrType == typeof(TimeOnly)) return new TimeOnlyTypeMapping(storeTypes.Time, DbType.Time);
        if (clrType == typeof(TimeSpan)) return new TimeSpanTypeMapping(storeTypes.Time, DbType.Time);
        if (clrType == typeof(Guid)) return new GuidTypeMapping(storeTypes.Guid, DbType.Guid);
        if (clrType == typeof(double)) return new DoubleTypeMapping(storeTypes.Double, DbType.Double);
        if (clrType == typeof(float)) return new FloatTypeMapping(storeTypes.Float, DbType.Single);
        if (clrType == typeof(bool)) return new BoolTypeMapping(storeTypes.Boolean, DbType.Boolean);

        return base.FindMapping(mappingInfo);
    }
}
