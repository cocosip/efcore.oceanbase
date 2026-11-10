namespace OceanBase.EntityFrameworkCore.Storage
{
    internal sealed record OceanBaseTypeMappings(
        string Integer,
        string Long,
        string Guid,
        string Double,
        string Float,
        string Boolean)
    {
        public static OceanBaseTypeMappings For(OceanBaseCompatibilityMode mode)
            => mode == OceanBaseCompatibilityMode.Oracle
                ? new("NUMBER(10)", "NUMBER(19)", "RAW(16)", "BINARY_DOUBLE", "BINARY_FLOAT", "NUMBER(1)")
                : new("INT", "BIGINT", "CHAR(36)", "DOUBLE", "FLOAT", "TINYINT");
    }
}
