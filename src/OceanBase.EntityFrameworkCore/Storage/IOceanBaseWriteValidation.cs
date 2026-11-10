namespace OceanBase.EntityFrameworkCore.Storage
{
    internal interface IOceanBaseWriteValidation
    {
        void ValidateWriteValue(object? value);
    }
}
