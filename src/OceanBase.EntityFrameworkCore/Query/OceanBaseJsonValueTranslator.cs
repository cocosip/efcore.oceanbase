using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace OceanBase.EntityFrameworkCore.Query
{
    internal sealed class OceanBaseJsonValueTranslator(
        ISqlExpressionFactory sql, IRelationalTypeMappingSource mappings, OceanBaseCompatibilityMode mode)
        : IMethodCallTranslator
    {
        private static readonly MethodInfo Method = typeof(OceanBaseDbFunctionsExtensions)
            .GetMethod(nameof(OceanBaseDbFunctionsExtensions.OceanBaseJsonValue))!;

        public SqlExpression? Translate(SqlExpression? instance, MethodInfo method, IReadOnlyList<SqlExpression> arguments,
            IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        {
            if (method != Method || arguments[2] is not SqlConstantExpression { Value: string path }) return null;
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            var resultMapping = mappings.FindMapping(typeof(string),
                mode == OceanBaseCompatibilityMode.MySql ? "varchar(512)" : "VARCHAR2(4000 BYTE)")!;
            return sql.Function("JSON_VALUE", [sql.ApplyDefaultTypeMapping(arguments[1]), sql.Constant(path, resultMapping)],
                nullable: true, argumentsPropagateNullability: [false, false], typeof(string), resultMapping);
        }
    }
}
