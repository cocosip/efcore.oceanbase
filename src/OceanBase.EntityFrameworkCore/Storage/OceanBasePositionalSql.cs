using System;
using System.Collections.Generic;
using System.Text;

namespace OceanBase.EntityFrameworkCore.Storage
{
    internal static class OceanBasePositionalSql
    {
        internal static (string Sql, List<string> Names, int Positions) Rewrite(string sql, IReadOnlyDictionary<string, int> parameters)
        {
            var output = new StringBuilder(sql.Length);
            var names = new List<string>();
            var positions = 0;
            for (var i = 0; i < sql.Length;)
            {
                var start = i;
                var c = sql[i++];
                if (c is '\'' or '"' or '`')
                {
                    var closed = false;
                    while (i < sql.Length)
                    {
                        var next = sql[i++];
                        if (next == '\\' && c != '`')
                            throw new NotSupportedException("MySQL backslash quoting depends on SQL_MODE; use parameters for such literals.");
                        if (next != c) continue;
                        if (i < sql.Length && sql[i] == c) { i++; continue; }
                        closed = true;
                        break;
                    }
                    if (!closed) throw new NotSupportedException("Unterminated MySQL quoted token.");
                }
                else if (c == '#' || c == '-' && i + 1 < sql.Length && sql[i] == '-' && (sql[i + 1] <= ' ' || char.IsWhiteSpace(sql[i + 1])))
                {
                    while (i < sql.Length && sql[i] is not ('\r' or '\n')) i++;
                }
                else if (c == '/' && i < sql.Length && sql[i] == '*')
                {
                    if (i + 1 < sql.Length && (sql[i + 1] == '!' || sql[i + 1] == 'M' && i + 2 < sql.Length && sql[i + 2] == '!'))
                        throw new NotSupportedException("MySQL executable comments cannot contain provider parameter bindings.");
                    var end = sql.IndexOf("*/", i + 1, StringComparison.Ordinal);
                    if (end < 0) throw new NotSupportedException("Unterminated MySQL comment.");
                    i = end + 2;
                }
                else if (c == '@')
                {
                    if (i < sql.Length && sql[i] == '@')
                    {
                        i++;
                        while (i < sql.Length && IsName(sql[i])) i++;
                    }
                    else
                    {
                        while (i < sql.Length && IsName(sql[i])) i++;
                        var name = sql[start..i];
                        if (parameters.ContainsKey(name))
                        {
                            names.Add(name);
                            output.Append('?');
                            continue;
                        }
                    }
                }
                else if (c == '?') positions++;
                output.Append(sql, start, i - start);
            }
            if (positions > 0 && names.Count > 0)
                throw new NotSupportedException("Do not mix positional and named MySQL parameters.");
            if (positions > 0 ? positions != parameters.Count : new HashSet<string>(names, StringComparer.OrdinalIgnoreCase).Count != parameters.Count)
                throw new NotSupportedException("MySQL command contains missing or unused parameter bindings.");
            return (output.ToString(), names, positions);
        }

        private static bool IsName(char c) => char.IsLetterOrDigit(c) || c is '_' or '$' or '.' || c > 127;
    }
}
