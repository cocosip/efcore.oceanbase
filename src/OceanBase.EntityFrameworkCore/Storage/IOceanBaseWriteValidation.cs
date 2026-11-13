namespace OceanBase.EntityFrameworkCore.Storage
{
    internal interface IOceanBaseWriteValidation
    {
        OceanBaseCompatibilityMode CompatibilityMode { get; }
        void ValidateWriteValue(object? value);
    }
}
