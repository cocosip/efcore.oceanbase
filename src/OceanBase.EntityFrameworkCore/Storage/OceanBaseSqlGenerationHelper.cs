using System;
using System.Text;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace OceanBase.EntityFrameworkCore.Storage;

internal sealed class OceanBaseSqlGenerationHelper : RelationalSqlGenerationHelper
{
    private readonly string _parameterPrefix;
    private readonly char _identifierDelimiter;

    internal OceanBaseSqlGenerationHelper(OceanBaseCompatibilityMode mode)
#pragma warning disable EF1001
        : this(new RelationalSqlGenerationHelperDependencies(), mode)
#pragma warning restore EF1001
    {
    }

    internal OceanBaseSqlGenerationHelper(
        RelationalSqlGenerationHelperDependencies dependencies,
        OceanBaseCompatibilityMode mode)
        : base(dependencies)
    {
        _parameterPrefix = mode == OceanBaseCompatibilityMode.Oracle ? ":" : "@";
        _identifierDelimiter = mode == OceanBaseCompatibilityMode.Oracle ? '"' : '`';
    }

    public override string GenerateParameterName(string name) => _parameterPrefix + name;

    public override void GenerateParameterName(StringBuilder builder, string name)
        => builder.Append(_parameterPrefix).Append(name);

    public override string GenerateParameterNamePlaceholder(string name)
        => GenerateParameterName(name);

    public override void GenerateParameterNamePlaceholder(StringBuilder builder, string name)
        => GenerateParameterName(builder, name);

    public override string EscapeIdentifier(string identifier)
        => identifier.Replace(_identifierDelimiter.ToString(), new string(_identifierDelimiter, 2), StringComparison.Ordinal);

    public override void EscapeIdentifier(StringBuilder builder, string identifier)
        => builder.Append(EscapeIdentifier(identifier));

    public override string DelimitIdentifier(string name)
        => _identifierDelimiter + EscapeIdentifier(name) + _identifierDelimiter;

    public override void DelimitIdentifier(StringBuilder builder, string name)
        => builder.Append(_identifierDelimiter).Append(EscapeIdentifier(name)).Append(_identifierDelimiter);

    public override string DelimitIdentifier(string name, string? schema)
        => schema is null ? DelimitIdentifier(name) : DelimitIdentifier(schema) + "." + DelimitIdentifier(name);

    public override void DelimitIdentifier(StringBuilder builder, string name, string? schema)
    {
        if (schema is null)
        {
            DelimitIdentifier(builder, name);
            return;
        }

        builder.Append(DelimitIdentifier(schema)).Append('.').Append(DelimitIdentifier(name));
    }

}
