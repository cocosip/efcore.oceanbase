using System;
using System.Text;

namespace OceanBase.EntityFrameworkCore.Storage;

internal static class OceanBaseCommentSql
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal static string Literal(string? value, bool oracle)
    {
        value ??= string.Empty;
        if (value.Contains('\0')) throw new NotSupportedException("OceanBase schema comments cannot contain NUL.");
        if (!oracle && value.Contains('\\'))
            throw new NotSupportedException("OceanBase MySQL schema comments cannot contain backslashes without an explicit SQL mode contract.");

        Utf8.GetByteCount(value);
        return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    internal static string OracleStatement(string table, string? column, string? value)
        => (column is null ? "COMMENT ON TABLE " + table : "COMMENT ON COLUMN " + table + "." + column)
            + " IS " + Literal(value, oracle: true);
}
