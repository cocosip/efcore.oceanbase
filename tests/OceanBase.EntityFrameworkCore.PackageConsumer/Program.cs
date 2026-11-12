using System;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using System.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OceanBase.EntityFrameworkCore;
using OceanBaseCommand = OceanBaseConnector.OceanBaseCommand;
using OceanBaseDecimal = OceanBaseConnector.OceanBaseDecimal;
using OceanBaseParameter = OceanBaseConnector.OceanBaseParameter;

namespace PackageConsumer
{
    internal static class Program
    {
        private static int Main()
        {
            try
            {
                VerifyPackage();
                return 0;
            }
            catch (Exception error)
            {
                // Expected negative package probes must fail normally, without Windows crash reporting.
                Console.Error.WriteLine($"Package consumer failed ({error.GetType().Name}): {error.Message}");
                return 1;
            }
        }

        private static void VerifyPackage()
        {
            foreach (var mode in new[] { OceanBaseCompatibilityMode.MySql, OceanBaseCompatibilityMode.Oracle })
            {
                VerifyFailedOpen(mode);
                VerifyNullableConcurrency(mode);
                TransactionSmoke.Run(mode);
                SchemaSmoke.Run(mode);
                SeedingSmoke.Run(mode);
                BulkDmlSmoke.Run(mode);
                var options = new DbContextOptionsBuilder<ConsumerContext>()
                    .UseOceanBase("Server=localhost;Database=package_smoke", mode).Options;
                using var context = new ConsumerContext(options, mode);
                if (!context.Database.IsOceanBase()) throw new Exception("Provider registration failed.");
                if (context.Database.GetDbConnection() is not OceanBaseConnector.OceanBaseConnection)
                    throw new Exception("Database facade did not resolve the packed provider connection.");
                var explicitName = mode == OceanBaseCompatibilityMode.MySql ? "@named" : ":named";
                var nameParameter = new OceanBaseParameter { ParameterName = explicitName, Value = "a" };
                var rawSql = context.Set<Row>().FromSqlRaw("SELECT Id, Name FROM Rows WHERE Name = {0}", nameParameter)
                    .Select(row => row.Id).ToQueryString();
                if (!rawSql.Contains("WHERE Name = " + (mode == OceanBaseCompatibilityMode.MySql ? "?" : explicitName), StringComparison.Ordinal))
                    throw new Exception("Packed provider did not preserve the named raw SQL placeholder.");
                var sql = context.Set<Row>().Where(row => row.Enabled).OrderBy(row => row.Id).Skip(1).Take(2)
                    .Select(row => new { row.Name, row.Token, row.ExplicitToken, row.OccurredAt, row.OffsetAt, row.Day,
                        row.Clock, row.Duration, row.Payload, row.LargePayload, row.Count, row.Amplitude, row.Wide,
                        Positive = row.Id > 0 }).ToQueryString();
                var paging = mode == OceanBaseCompatibilityMode.MySql ? "LIMIT" : "FETCH NEXT";
                if (!sql.Contains(paging, StringComparison.Ordinal)) throw new Exception("Wrong query dialect.");
                if (mode == OceanBaseCompatibilityMode.Oracle && !sql.Contains("CASE", StringComparison.Ordinal))
                {
                    throw new Exception("Oracle Boolean projection was not converted to a scalar value.");
                }

                var suffix = "包'_%\\";
                var concatSql = context.Set<Row>().Select(row => string.Concat(row.Name, suffix)).ToQueryString();
                if (!concatSql.Contains(mode == OceanBaseCompatibilityMode.MySql ? "CONCAT(" : " || ", StringComparison.Ordinal)
                    || concatSql.Contains(" + ", StringComparison.Ordinal))
                {
                    throw new Exception("Packed provider used the wrong string concatenation syntax.");
                }

                if (mode == OceanBaseCompatibilityMode.Oracle)
                {
                    string? fallback = "present";
                    var nullQuery = context.Set<Row>().Where(row => (row.Name + suffix ?? fallback) == null);
                    if (!nullQuery.ToQueryString().Contains("WHERE 0 = 1", StringComparison.Ordinal))
                    {
                        throw new Exception("A nonempty Oracle coalesce fallback was treated as nullable.");
                    }
                    foreach (var value in new string?[] { "", null, "again", "" })
                    {
                        fallback = value;
                        var nullSql = nullQuery.ToQueryString();
                        if (string.IsNullOrEmpty(value)
                            ? !nullSql.Contains(" || ", StringComparison.Ordinal)
                                || !nullSql.Contains("IS NULL", StringComparison.Ordinal)
                                || nullSql.Contains("WHERE 0 = 1", StringComparison.Ordinal)
                            : !nullSql.Contains("WHERE 0 = 1", StringComparison.Ordinal))
                        {
                            throw new Exception("Oracle empty/null parameter processing reused stale command SQL.");
                        }
                    }
                }

                var arithmeticSql = context.Set<Row>().Select(row => new { Quotient = row.Id / 2, Remainder = row.Id % 2 })
                    .ToQueryString();
                if (!arithmeticSql.Contains(mode == OceanBaseCompatibilityMode.MySql ? " DIV " : "TRUNC(", StringComparison.Ordinal)
                    || !arithmeticSql.Contains(mode == OceanBaseCompatibilityMode.MySql ? " % " : "MOD(", StringComparison.Ordinal))
                {
                    throw new Exception("Packed provider used the wrong integer division or remainder dialect.");
                }
                var fractionalSql = context.Set<Row>().Select(row => row.Amount / 2m).ToQueryString();
                if (!fractionalSql.Contains(" / ", StringComparison.Ordinal)
                    || fractionalSql.Contains("TRUNC(", StringComparison.Ordinal)
                    || fractionalSql.Contains(" DIV ", StringComparison.Ordinal))
                {
                    throw new Exception("Packed provider truncated fractional division.");
                }

                var clampSql = context.Set<Row>().Where(row => Math.Clamp(row.Id, -1, 1) > 0).ToQueryString();
                if (!clampSql.Contains("CASE", StringComparison.Ordinal)
                    || !clampSql.Contains("WHEN", StringComparison.Ordinal))
                {
                    throw new Exception("Packed provider did not translate Math.Clamp.");
                }

                var bitwiseSql = context.Set<Row>().Where(row => (row.Id & -3) != 0).Select(row => new
                {
                    Or = row.Id | -3, Xor = row.Id ^ -3, Not = ~row.Id,
                    Unsigned = ~row.Unsigned, Wide = row.Bits & 9223372036854775808UL,
                    Enum = ~row.Flags, Promoted = row.Narrow | 256
                }).ToQueryString();
                var bitwiseFragments = mode == OceanBaseCompatibilityMode.MySql
                    ? new[] { " AS SIGNED)", " ^ ", " | ", " & 4294967295", " & 255", " AS SIGNED)) | (256)", "9223372036854775808" }
                    : new[] { "BITAND(", " - 2 * BITAND(", "(-1 - (", "4294967295 -", "255 -", "TRUNC(", "MOD(", "* 4294967296", " + (256)" };
                foreach (var fragment in bitwiseFragments)
                    if (!bitwiseSql.Contains(fragment, StringComparison.Ordinal))
                        throw new Exception($"Packed provider did not preserve integer bitwise semantics: {fragment}");

                bool? xorFlag = true;
                var xorQuery = context.Set<Row>().Where(row => row.BooleanText ^ row.BooleanNumber)
                    .Select(row => row.OptionalBoolean ^ xorFlag);
                foreach (var value in new bool?[] { true, false, null, true, null, false })
                {
                    xorFlag = value;
                    using var xorCommand = xorQuery.CreateDbCommand();
                    var xorSql = xorCommand.CommandText;
                    foreach (var fragment in new[] { " <> ", "CASE WHEN", "ELSE NULL", " = 2 THEN 1", " = -1 THEN 0",
                        mode == OceanBaseCompatibilityMode.MySql ? " = 'Y' THEN 1" : " = N'Y' THEN 1" })
                        if (!xorSql.Contains(fragment, StringComparison.Ordinal))
                            throw new Exception($"Packed provider lost Boolean XOR normalization: {fragment}");
                    if (xorSql.Contains(" ^ ", StringComparison.Ordinal)
                        || xorSql.Contains("THEN 'Y'", StringComparison.Ordinal))
                        throw new Exception("Packed provider used storage-space Boolean XOR or a converted result.");
                    if (value is null)
                    {
                        if (xorCommand.Parameters.Count != 0 || !xorSql.Contains("(NULL) = 1 THEN 1", StringComparison.Ordinal))
                            throw new Exception("Packed provider retained a stale nullable Boolean capture.");
                    }
                    else
                    {
                        if (xorCommand.Parameters.Count != (mode == OceanBaseCompatibilityMode.MySql ? 2 : 1))
                            throw new Exception("Packed provider lost Boolean XOR parameter ordering/reuse.");
                        foreach (DbParameter parameter in xorCommand.Parameters)
                            if (Convert.ToDecimal(parameter.Value) != (value.Value ? 1m : 0m)
                                || parameter.DbType != (mode == OceanBaseCompatibilityMode.MySql ? DbType.Boolean : DbType.Decimal))
                                throw new Exception("Packed provider used a converted operand mapping for a Boolean capture.");
                    }
                }

                bool? logicalFlag = true;
                var logicalQuery = context.Set<Row>().Where(row => row.BooleanText == row.BooleanNumber)
                    .Select(row => new { Not = !row.BooleanText, And = row.BooleanText && row.BooleanNumber,
                        Or = row.BooleanText || row.BooleanNumber, Equal = row.BooleanText == row.BooleanNumber,
                        Different = row.BooleanText != row.Enabled, Optional = row.OptionalBoolean & logicalFlag,
                        NullableOr = row.OptionalBoolean | logicalFlag, NullableEqual = row.OptionalBoolean == logicalFlag,
                        Conditional = row.BooleanText ? row.OptionalBoolean : logicalFlag,
                        Fallback = row.OptionalBoolean ?? logicalFlag });
                foreach (var value in new bool?[] { true, false, null, true, null, false })
                {
                    logicalFlag = value;
                    using var logicalCommand = logicalQuery.CreateDbCommand();
                    var logicalSql = logicalCommand.CommandText;
                    foreach (var fragment in new[] { "CASE WHEN", "ELSE NULL", " = 2 THEN 1", " = -1 THEN 0", "COALESCE(",
                        mode == OceanBaseCompatibilityMode.MySql ? " = 'Y' THEN 1" : " = N'Y' THEN 1" })
                        if (!logicalSql.Contains(fragment, StringComparison.Ordinal))
                            throw new Exception($"Packed provider lost converted Boolean logical normalization: {fragment}");
                    if (logicalSql.Contains("__oceanbase_internal_", StringComparison.Ordinal))
                        throw new Exception("Packed provider emitted an internal Boolean function.");
                    if (value is null)
                    {
                        if (logicalCommand.Parameters.Count != 0)
                            throw new Exception("Packed provider retained a stale logical Boolean capture.");
                    }
                    else
                    {
                        if (logicalCommand.Parameters.Count == 0)
                            throw new Exception("Packed provider omitted a non-null logical Boolean capture.");
                        foreach (DbParameter parameter in logicalCommand.Parameters)
                            if (Convert.ToDecimal(parameter.Value) != (value.Value ? 1m : 0m)
                                || parameter.DbType != (mode == OceanBaseCompatibilityMode.MySql ? DbType.Boolean : DbType.Decimal))
                                throw new Exception("Packed provider changed a logical Boolean capture's value or mapping.");
                    }
                }

                bool? methodFlag = true;
                var methodFallback = true;
                var methodsQuery = context.Set<Row>().Where(row => row.BooleanText.Equals(row.BooleanNumber))
                    .Select(row => new { Equal = row.BooleanText.Equals((object)row.BooleanNumber),
                        NullableEqual = Nullable.Equals(row.OptionalBoolean, methodFlag),
                        Default = row.OptionalBoolean.GetValueOrDefault(),
                        Fallback = row.OptionalBoolean.GetValueOrDefault(methodFallback),
                        ConvertedFallback = row.OptionalBoolean.GetValueOrDefault(row.BooleanText) });
                foreach (var value in new bool?[] { true, false, null, true, null, false })
                {
                    methodFlag = value;
                    methodFallback = value == true;
                    using var methodsCommand = methodsQuery.CreateDbCommand();
                    var methodsSql = methodsCommand.CommandText;
                    foreach (var fragment in new[] { " = 2 THEN 1", " = -1 THEN 0", "COALESCE(",
                        mode == OceanBaseCompatibilityMode.MySql ? " = 'Y' THEN 1" : " = N'Y' THEN 1" })
                        if (!methodsSql.Contains(fragment, StringComparison.Ordinal))
                            throw new Exception($"Packed provider lost Boolean method translation: {fragment}");
                    if (methodsSql.Contains("__oceanbase_internal_", StringComparison.Ordinal)
                        || methodsCommand.Parameters.Count == 0)
                        throw new Exception("Packed provider lost Boolean method captures or emitted an internal function.");
                    foreach (DbParameter parameter in methodsCommand.Parameters)
                        if (Convert.ToDecimal(parameter.Value) != (methodFallback ? 1m : 0m)
                            || parameter.DbType != (mode == OceanBaseCompatibilityMode.MySql ? DbType.Boolean : DbType.Decimal))
                            throw new Exception("Packed provider changed Boolean method parameter values or mappings.");
                }
                if (!context.Set<Row>().Where(row => row.OptionalBoolean.HasValue).ToQueryString()
                    .Contains("IS NOT NULL", StringComparison.Ordinal))
                    throw new Exception("Packed provider lost the nullable Boolean HasValue predicate.");

                var orderFlag = true;
                var orderTextQuery = context.Set<Row>()
                    .Where(row => row.BooleanText.CompareTo(row.BooleanNumber) > 0 && row.BooleanText.ToString() == "True")
                    .Select(row => new { Typed = row.BooleanNumber.CompareTo(orderFlag),
                        Boxed = row.BooleanText.CompareTo((object)row.OptionalBoolean!), Text = row.BooleanText.ToString(),
                        Formatted = row.BooleanNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        Optional = row.OptionalBoolean.ToString() });
                foreach (var value in new[] { true, false, true, false })
                {
                    orderFlag = value;
                    using var orderCommand = orderTextQuery.CreateDbCommand();
                    foreach (var fragment in new[] { "'True'", "'False'", "ELSE -1", " = 2 THEN 1", " = -1 THEN 0",
                        mode == OceanBaseCompatibilityMode.MySql ? " = 'Y' THEN 1" : " = N'Y' THEN 1" })
                        if (!orderCommand.CommandText.Contains(fragment, StringComparison.Ordinal))
                            throw new Exception($"Packed provider lost Boolean ordering/string translation: {fragment}");
                    if (orderCommand.CommandText.Contains("__oceanbase_internal_", StringComparison.Ordinal)
                        || orderCommand.Parameters.Count == 0)
                        throw new Exception("Packed provider lost Boolean ordering captures or emitted an internal function.");
                    foreach (DbParameter parameter in orderCommand.Parameters)
                        if (Convert.ToDecimal(parameter.Value) != (value ? 1m : 0m))
                            throw new Exception("Packed provider changed a Boolean ordering parameter value.");
                }
                bool? boxedFlag = true;
                var boxedOrder = context.Set<Row>().Select(row => row.BooleanText.CompareTo((object?)boxedFlag));
                foreach (var value in new bool?[] { true, false, null, true, null })
                {
                    boxedFlag = value;
                    using var boxedCommand = boxedOrder.CreateDbCommand();
                    if (value is null && (!boxedCommand.CommandText.Contains("SELECT 1", StringComparison.Ordinal)
                        || boxedCommand.Parameters.Count != 0))
                        throw new Exception("Packed provider lost the boxed nullable-null CompareTo result.");
                    if (value is not null && boxedCommand.Parameters.Count == 0)
                        throw new Exception("Packed provider lost a current boxed ordering capture.");
                }

                var shiftCount = -1;
                var shiftQuery = context.Set<Row>().Where(row => (row.Id << shiftCount) != 0).Select(row => new
                {
                    Left = row.Id << shiftCount, Right = row.Id >> shiftCount, WideLeft = row.Count << shiftCount,
                    WideRight = row.Count >> shiftCount, Unsigned = row.Unsigned << shiftCount,
                    Bits = row.Bits >> shiftCount, Promoted = row.Narrow << shiftCount
                });
                foreach (var value in new[] { -1, 0, 32, 64, int.MaxValue })
                {
                    shiftCount = value;
                    using var shiftCommand = shiftQuery.CreateDbCommand();
                    var shiftSql = shiftCommand.CommandText;
                    var fragments = mode == OceanBaseCompatibilityMode.MySql
                        ? new[] { " << ", " >> ", " & 31)", " & 63)", "CASE WHEN", "THEN CAST(~", " ^ 2147483648) AS SIGNED)" }
                        : new[] { "MOD(MOD(", "FLOOR(", "POWER(2, ", ", 31)", ", 63)", "18446744073709551616" };
                    foreach (var fragment in fragments)
                        if (!shiftSql.Contains(fragment, StringComparison.Ordinal))
                            throw new Exception($"Packed provider did not preserve integer shift semantics: {fragment}");
                    if (shiftCommand.Parameters.Count != (mode == OceanBaseCompatibilityMode.MySql ? 10 : 1)
                        || shiftCommand.Parameters.Cast<DbParameter>().Any(parameter => parameter.DbType != DbType.Int32
                            || parameter.Value is not int count || count != value)
                        || shiftSql.Contains("__oceanbase_internal_", StringComparison.Ordinal))
                        throw new Exception("Packed shift count mapping or captured-value refresh failed.");
                }

                var stringFunctionsSql = context.Set<Row>().Where(row => row.Name.Trim().ToLower() == "pkg")
                    .Select(row => new { Upper = row.Name.ToUpper(), Start = row.Name.TrimStart(),
                        End = row.Name.TrimEnd(), Length = row.Name.Length }).ToQueryString();
                foreach (var function in new[] { "LOWER(", "UPPER(", "TRIM(", "LTRIM(", "RTRIM(",
                    mode == OceanBaseCompatibilityMode.MySql ? "CHAR_LENGTH(" : "LENGTH(" })
                {
                    if (!stringFunctionsSql.Contains(function, StringComparison.Ordinal))
                    {
                        throw new Exception($"Packed provider did not translate {function}");
                    }
                }

                var searchSql = context.Set<Row>().Where(row => row.Name.Contains(suffix)
                    && row.Name.StartsWith(suffix) && row.Name.EndsWith(suffix)).ToQueryString();
                if (!searchSql.Contains("INSTR(", StringComparison.Ordinal)
                    || !searchSql.Contains(mode == OceanBaseCompatibilityMode.MySql ? "RIGHT(" : "SUBSTR(", StringComparison.Ordinal)
                    || searchSql.Contains(" LIKE ", StringComparison.Ordinal))
                {
                    throw new Exception("Packed provider did not translate literal-data string search.");
                }
                string? searchNeedle = "needle";
                var searchQuery = context.Set<Row>().Where(row => row.SearchText!.EndsWith(searchNeedle!));
                foreach (var value in new string?[] { "needle", "", null, "again", "", null })
                {
                    searchNeedle = value;
                    var searchCommand = searchQuery.ToQueryString();
                    var expected = value is null ? (mode == OceanBaseCompatibilityMode.MySql ? "WHERE 0" : "WHERE 0 = 1")
                        : value.Length == 0 ? "IS NOT NULL"
                        : mode == OceanBaseCompatibilityMode.MySql ? "RIGHT(" : "SUBSTR(";
                    if (!searchCommand.Contains(expected, StringComparison.Ordinal)
                        || mode == OceanBaseCompatibilityMode.Oracle && string.IsNullOrEmpty(value)
                            && searchCommand.Contains("SUBSTR(", StringComparison.Ordinal))
                    {
                        throw new Exception("String search reused stale empty/null/nonempty parameter SQL.");
                    }
                }

                var substringSql = context.Set<Row>().Where(row => row.Name.Substring(1, 2) == "pk")
                    .Select(row => row.Name.Substring(1)).ToQueryString();
                var substringFunction = mode == OceanBaseCompatibilityMode.MySql ? "SUBSTRING(" : "SUBSTR(";
                if (!substringSql.Contains(substringFunction, StringComparison.Ordinal))
                {
                    throw new Exception("Packed provider did not translate both Substring overloads.");
                }
                var maxStartSql = context.Set<Row>().Where(row => row.Name.Substring(int.MaxValue) == "x").ToQueryString();
                if (!maxStartSql.Contains("2147483648", StringComparison.Ordinal)
                    && !maxStartSql.Contains("2147483647 + 1", StringComparison.Ordinal))
                {
                    throw new Exception("Substring origin adjustment overflowed before SQL generation.");
                }

                var temporalPartsSql = context.Set<Row>().Where(row => row.OccurredAt.Year == 2024
                    && row.Day.Month == 2 && row.Clock.Second == 59 && row.LegacyStamp.Hour == 12)
                    .Select(row => new { row.OccurredAt.Month, Day = row.Day.Day, row.Clock.Minute }).ToQueryString();
                var temporalParts = mode == OceanBaseCompatibilityMode.MySql
                    ? new[] { "YEAR(", "MONTH(", "DAYOFMONTH(", "HOUR(", "MINUTE(", "SECOND(" }
                    : new[] { "EXTRACT(YEAR FROM", "EXTRACT(MONTH FROM", "EXTRACT(DAY FROM",
                        "EXTRACT(HOUR FROM", "EXTRACT(MINUTE FROM", "TRUNC(EXTRACT(SECOND FROM" };
                foreach (var part in temporalParts)
                {
                    if (!temporalPartsSql.Contains(part, StringComparison.Ordinal))
                        throw new Exception($"Packed provider did not translate temporal part {part}");
                }
                if (mode == OceanBaseCompatibilityMode.Oracle && !temporalPartsSql.Contains("AS TIMESTAMP(7)", StringComparison.Ordinal))
                    throw new Exception("Oracle DATE clock extraction lacked a timestamp cast.");

                var calendarSql = context.Set<Row>().Where(row => row.OccurredAt.DayOfWeek == DayOfWeek.Sunday
                    && row.Day.DayOfYear == 60).ToQueryString();
                var calendarParts = mode == OceanBaseCompatibilityMode.MySql
                    ? new[] { "DAYOFWEEK(", "DAYOFYEAR(" } : new[] { "'IW'", "'YYYY'" };
                if (calendarParts.Any(part => !calendarSql.Contains(part, StringComparison.Ordinal)))
                    throw new Exception("Packed provider did not translate Gregorian calendar parts.");

                var numericFunctionsSql = context.Set<Row>().Where(row => Math.Abs(row.Id) == 1
                    && Math.Sign(row.Amount) == -1 && Math.Truncate(row.Amplitude) == 1d
                    && MathF.Abs(row.Wide) > 0f && decimal.Truncate(row.Amount) == 1m)
                    .Select(row => new { Sign = MathF.Sign(row.Wide), Absolute = decimal.Abs(row.Amount) })
                    .ToQueryString();
                foreach (var function in new[] { "ABS(", "SIGN(",
                    mode == OceanBaseCompatibilityMode.MySql ? "TRUNCATE(" : "TRUNC(" })
                {
                    if (!numericFunctionsSql.Contains(function, StringComparison.Ordinal))
                        throw new Exception($"Packed provider did not translate numeric function {function}");
                }

                int[] membershipValues = [1, 2];
                var membershipQuery = context.Set<Row>().Where(row => membershipValues.Contains(row.Id));
                foreach (var values in new[] { new[] { 1, 2 }, Array.Empty<int>(), new[] { 1, 2, 2, 3, 4, 5 }, Array.Empty<int>() })
                {
                    membershipValues = values;
                    var membershipSql = membershipQuery.ToQueryString();
                    if (!membershipSql.Contains(values.Length == 0 ? "WHERE 0" : " IN (", StringComparison.Ordinal))
                        throw new Exception("Packed integer membership failed or reused stale collection SQL.");
                }
                foreach (var collectionMode in new ParameterTranslationMode?[]
                    { null, ParameterTranslationMode.Constant, null, ParameterTranslationMode.Constant })
                {
                    using var membershipContext = CreateMembershipContext(mode, collectionMode);
                    var sqlWithMode = MembershipSql(membershipContext, [41, 42]);
                    var hasParameters = sqlWithMode.Contains(mode == OceanBaseCompatibilityMode.MySql ? "@" : ":", StringComparison.Ordinal);
                    if (hasParameters != (collectionMode != ParameterTranslationMode.Constant))
                        throw new Exception("Compiled membership query reused another context's collection mode.");
                }

                string?[] textMembershipValues = ["one", "two"];
                var textMembershipQuery = context.Set<Row>()
                    .Where(row => Enumerable.Contains(textMembershipValues, row.SearchText));
                foreach (var values in new string?[][]
                    { ["one", "two"], [], ["a", "b", "c", "d", "e", "f"], [null, ""], ["", "again"], [] })
                {
                    textMembershipValues = values;
                    var textMembershipSql = textMembershipQuery.ToQueryString();
                    var predicate = textMembershipSql[textMembershipSql.IndexOf("WHERE ", StringComparison.Ordinal)..];
                    if (values.Length == 0 && !textMembershipSql.Contains("WHERE 0", StringComparison.Ordinal))
                        throw new Exception("Packed text membership reused a nonempty collection shape.");
                    if (values.Any(value => mode == OceanBaseCompatibilityMode.MySql ? value is not null : !string.IsNullOrEmpty(value))
                        && !predicate.Contains(" IN (", StringComparison.Ordinal)
                        && !predicate.Contains(mode == OceanBaseCompatibilityMode.MySql ? "`SearchText` = " : "\"SearchText\" = ", StringComparison.Ordinal))
                        throw new Exception("Packed text membership reused an empty or all-null collection shape.");
                    if (mode == OceanBaseCompatibilityMode.Oracle && values.Length == 2 && values.All(string.IsNullOrEmpty))
                    {
                        if (!predicate.Contains("IS NULL", StringComparison.Ordinal)
                            || predicate.Contains(" IN (", StringComparison.Ordinal)
                            || predicate.Contains(" = ", StringComparison.Ordinal))
                            throw new Exception("Oracle text membership did not normalize empty elements before null compensation.");
                    }
                }

                int[] rootValues = [3, 1, 3];
                var rootQuery = context.Set<Row>()
                    .Where(row => rootValues.Where(value => value > row.Id).Any());
                foreach (var values in new[] { new[] { 3, 1, 3 }, Array.Empty<int>(), new[] { 1, 2, 3, 4, 5, 6 }, Array.Empty<int>() })
                {
                    rootValues = values;
                    var rootSql = rootQuery.ToQueryString();
                    if (rootSql.Contains("UNION ALL VALUES", StringComparison.Ordinal)
                        || (values.Length > 0 && rootSql.Split("UNION ALL SELECT", StringSplitOptions.None).Length - 1 != values.Length - 1)
                        || (values.Length == 0 && !rootSql.Contains("WHERE 0", StringComparison.Ordinal)
                            && !rootSql.Contains("WHERE 1 = 0", StringComparison.Ordinal)))
                        throw new Exception("Packed general collection root has invalid or stale row SQL: " + rootSql);
                    if (mode == OceanBaseCompatibilityMode.Oracle && values.Length > 0
                        && rootSql.Split("FROM DUAL", StringSplitOptions.None).Length - 1 != values.Length)
                        throw new Exception("Oracle collection SELECT arms do not each have FROM DUAL.");
                }

                VerifyIntegerRoots(context, mode, sbyte.MinValue, sbyte.MaxValue, false);
                VerifyIntegerRoots(context, mode, byte.MinValue, byte.MaxValue, true);
                VerifyIntegerRoots(context, mode, short.MinValue, short.MaxValue, false);
                VerifyIntegerRoots(context, mode, ushort.MinValue, ushort.MaxValue, true);
                VerifyIntegerRoots(context, mode, int.MinValue, int.MaxValue, false);
                VerifyIntegerRoots(context, mode, uint.MinValue, uint.MaxValue, true);
                VerifyIntegerRoots(context, mode, long.MinValue, long.MaxValue, false);
                VerifyIntegerRoots(context, mode, ulong.MinValue, ulong.MaxValue, true);
                VerifyNumericRoots(context, mode, decimal.MinValue, decimal.MaxValue, 0.0000000000000000000000000001m, "DECIMAL(65,30)", "NUMBER");
                VerifyNumericRoots(context, mode, float.MinValue, float.MaxValue, float.Epsilon, "FLOAT", "BINARY_FLOAT");
                VerifyNumericRoots(context, mode, double.MinValue, double.MaxValue, double.Epsilon, "DOUBLE", "BINARY_DOUBLE");
                VerifyNonnumericExtrema(context, mode);
                VerifyTextRoots(context, mode);
                VerifyBooleanRoots(context, mode);
                VerifyBinaryRoots(context, mode);
                VerifyTemporalRoots(context, mode);
                int?[] nullableConvertedValues = [null, 17, 17];
                var nullableConverted = context.Set<Row>().Where(row => nullableConvertedValues
                    .Where(value => value > row.NullableInteger).Contains(row.NullableInteger)).Select(row => row.NullableInteger);
                foreach (var input in new int?[][] { nullableConvertedValues, [], [null, null], [] })
                {
                    nullableConvertedValues = input;
                    var convertedSql = nullableConverted.ToQueryString();
                    if (!convertedSql.Contains(mode == OceanBaseCompatibilityMode.MySql ? " AS UNSIGNED)" : " AS NUMBER)", StringComparison.Ordinal)
                        || convertedSql.Contains("WHERE 1 = 0", StringComparison.Ordinal) != (input.Length == 0))
                        throw new Exception("Packed nullable integer converter lost its unsigned carrier or current root values.");
                    foreach (var projected in new IQueryable[]
                    {
                        context.Set<Row>().SelectMany(row => nullableConvertedValues.Where(value => value > row.NullableInteger)),
                        context.Set<Row>().Select(row => nullableConvertedValues.Where(value => value > row.NullableInteger).FirstOrDefault()),
                        context.Set<Row>().Join(nullableConvertedValues, row => row.NullableInteger, value => value, (row, value) => value),
                        context.Set<Row>().SelectMany(row => nullableConvertedValues.Where(value => value > row.NullableInteger).Distinct().Take(2))
                    })
                    {
                        var projectedSql = projected.ToQueryString();
                        if (!projectedSql.Contains(mode == OceanBaseCompatibilityMode.MySql ? " AS UNSIGNED)" : " AS NUMBER)", StringComparison.Ordinal)
                            || projectedSql.Contains("WHERE 1 = 0", StringComparison.Ordinal) != (input.Length == 0)
                            || projectedSql.Split("UNION ALL SELECT", StringSplitOptions.None).Length - 1 != Math.Max(0, input.Length - 1))
                            throw new Exception("Packed converted element projection lost its mapping, current values or row count.");
                    }
                }
                var projectedEmpty = context.Set<Row>().Join(new int?[] { }, row => row.NullableInteger, value => value,
                    (row, value) => value).ToQueryString();
                if (!projectedEmpty.Contains("WHERE 1 = 0", StringComparison.Ordinal)
                    || !projectedEmpty.Contains(mode == OceanBaseCompatibilityMode.MySql ? "CAST(NULL AS UNSIGNED)" : "CAST(NULL AS NUMBER)", StringComparison.Ordinal))
                    throw new Exception("Packed converted empty-inline Join lost its mapping or zero rows.");
                var nullableEmpty = context.Set<Row>().Select(row => new int?[] { }
                    .Count(value => value > row.NullableInteger)).ToQueryString();
                if (!nullableEmpty.Contains(mode == OceanBaseCompatibilityMode.MySql ? "CAST(NULL AS UNSIGNED)" : "CAST(NULL AS NUMBER)", StringComparison.Ordinal))
                    throw new Exception("Packed nullable converter lost its original empty-inline type.");

                foreach (var emptyRoot in new IQueryable[]
                {
                    context.Set<Row>().Select(row => new int[] { }.Count(value => value > row.Id)),
                    context.Set<Row>().Select(row => new int?[] { }.Any(value => value > row.Id)),
                    context.Set<Row>().SelectMany(row => new int[] { }.Where(value => value > row.Id)
                        .Join(context.Set<Row>(), value => value, item => item.Id, (value, item) => value))
                })
                {
                    var emptySql = emptyRoot.ToQueryString();
                    if (emptySql.Contains("UNION ALL VALUES", StringComparison.Ordinal)
                        || (!emptySql.Contains("WHERE 1 = 0", StringComparison.Ordinal)
                            && !emptySql.Contains("WHERE 0", StringComparison.Ordinal)))
                        throw new Exception("Packed inline-empty integral root lost its zero-row relation: " + emptySql);
                }

                var emptyOrdinalSql = context.Set<Row>().Select(row => new int[] { }
                    .Select(value => row.Id + 1).ElementAt(row.Id)).ToQueryString();
                var ordinalDeclaration = mode == OceanBaseCompatibilityMode.MySql
                    ? "CAST(NULL AS SIGNED) AS `_ord`" : "CAST(NULL AS NUMBER) \"_ord\"";
                if (!emptyOrdinalSql.Contains(ordinalDeclaration, StringComparison.Ordinal)
                    || !emptyOrdinalSql.Contains("ORDER BY", StringComparison.Ordinal)
                    || !emptyOrdinalSql.Contains("WHERE 1 = 0", StringComparison.Ordinal))
                    throw new Exception("Packed inline-empty root lost its retained ordinal identity: " + emptyOrdinalSql);

                foreach (var (roundingQuery, floor) in new (IQueryable Query, bool Floor)[]
                {
                    (context.Set<Row>().Where(row => Math.Floor(row.Amplitude) > 0d), true),
                    (context.Set<Row>().Where(row => Math.Floor(row.Amount) > 0m), true),
                    (context.Set<Row>().Where(row => MathF.Floor(row.SingleValue) > 0f), true),
                    (context.Set<Row>().Where(row => decimal.Floor(row.Amount) > 0m), true),
                    (context.Set<Row>().Where(row => Math.Ceiling(row.Amplitude) > 0d), false),
                    (context.Set<Row>().Where(row => Math.Ceiling(row.Amount) > 0m), false),
                    (context.Set<Row>().Where(row => MathF.Ceiling(row.SingleValue) > 0f), false),
                    (context.Set<Row>().Where(row => decimal.Ceiling(row.Amount) > 0m), false)
                })
                {
                    var roundingSql = roundingQuery.ToQueryString();
                    if (mode == OceanBaseCompatibilityMode.MySql
                        ? !roundingSql.Contains("CASE", StringComparison.Ordinal)
                            || !roundingSql.Contains("TRUNCATE(", StringComparison.Ordinal)
                            || !roundingSql.Contains(floor ? " - " : " + ", StringComparison.Ordinal)
                            || roundingSql.Contains("FLOOR(", StringComparison.Ordinal)
                            || roundingSql.Contains("CEIL(", StringComparison.Ordinal)
                        : !roundingSql.Contains(floor ? "FLOOR(" : "CEIL(", StringComparison.Ordinal))
                        throw new Exception("Packed Floor/Ceiling lost its range-preserving dialect: " + roundingSql);
                }

                double logarithmBase = 0.5d;
                float singleLogarithmBase = 0.5f;
                foreach (var (numericQuery, operation, single) in new (IQueryable Query, string Operation, bool Single)[]
                {
                    (context.Set<Row>().Where(row => Math.Sqrt(row.DoubleValue) > 0d), "SQRT", false),
                    (context.Set<Row>().Where(row => MathF.Sqrt(row.SingleValue) > 0f), "SQRT", true),
                    (context.Set<Row>().Where(row => Math.Exp(row.DoubleValue) > 0d), "EXP", false),
                    (context.Set<Row>().Where(row => MathF.Exp(row.SingleValue) > 0f), "EXP", true),
                    (context.Set<Row>().Where(row => Math.Log(row.DoubleValue) > 0d), "LN", false),
                    (context.Set<Row>().Where(row => MathF.Log(row.SingleValue) > 0f), "LN", true),
                    (context.Set<Row>().Where(row => Math.Log(row.DoubleValue, logarithmBase) > 0d), "LOG_BASE", false),
                    (context.Set<Row>().Where(row => MathF.Log(row.SingleValue, singleLogarithmBase) > 0f), "LOG_BASE", true),
                    (context.Set<Row>().Where(row => Math.Log2(row.DoubleValue) > 0d), "LOG2", false),
                    (context.Set<Row>().Where(row => MathF.Log2(row.SingleValue) > 0f), "LOG2", true),
                    (context.Set<Row>().Where(row => Math.Log10(row.DoubleValue) > 0d), "LOG10", false),
                    (context.Set<Row>().Where(row => MathF.Log10(row.SingleValue) > 0f), "LOG10", true),
                    (context.Set<Row>().Where(row => Math.Pow(row.DoubleValue, 2d) > 0d), "POWER", false),
                    (context.Set<Row>().Where(row => MathF.Pow(row.SingleValue, 2f) > 0f), "POWER", true),
                    (context.Set<Row>().Where(row => Math.Sin(row.DoubleValue) > 0d), "SIN", false),
                    (context.Set<Row>().Where(row => MathF.Sin(row.SingleValue) > 0f), "SIN", true),
                    (context.Set<Row>().Where(row => Math.Cos(row.DoubleValue) > 0d), "COS", false),
                    (context.Set<Row>().Where(row => MathF.Cos(row.SingleValue) > 0f), "COS", true),
                    (context.Set<Row>().Where(row => Math.Tan(row.DoubleValue) > 0d), "TAN", false),
                    (context.Set<Row>().Where(row => MathF.Tan(row.SingleValue) > 0f), "TAN", true)
                })
                {
                    var numericSql = numericQuery.ToQueryString();
                    var function = operation switch
                    {
                        "LOG_BASE" => mode == OceanBaseCompatibilityMode.MySql ? "LN(" : "LOG(",
                        "LOG2" => mode == OceanBaseCompatibilityMode.MySql ? "LOG2(" : "LOG(2",
                        "LOG10" => mode == OceanBaseCompatibilityMode.MySql ? "LOG10(" : "LOG(",
                        _ => operation + "("
                    };
                    if (!numericSql.Contains(function, StringComparison.Ordinal)
                        || (mode == OceanBaseCompatibilityMode.MySql && operation == "LOG_BASE"
                            && (!numericSql.Contains(" / ", StringComparison.Ordinal)
                                || numericSql.Split("LN(", StringSplitOptions.None).Length - 1 != 2
                                || numericSql.Contains("LOG(", StringComparison.Ordinal)))
                        || (!single && (numericSql.Contains(" AS FLOAT)", StringComparison.Ordinal)
                            || numericSql.Contains(" AS BINARY_FLOAT)", StringComparison.Ordinal)))
                        || (single && (mode == OceanBaseCompatibilityMode.MySql || operation != "SQRT")
                            && !numericSql.Contains(mode == OceanBaseCompatibilityMode.MySql
                                ? " AS FLOAT)" : " AS BINARY_FLOAT)", StringComparison.Ordinal)))
                        throw new Exception("Packed numeric function lost its dialect or Single normalization: " + numericSql);
                }

                var aggregateSql = context.Set<Row>().GroupBy(row => row.Id).Select(group => new
                {
                    group.Key,
                    SumCount = group.Sum(row => row.Count), AverageCount = group.Average(row => row.Count),
                    SumSingle = group.Sum(row => row.SingleValue), AverageSingle = group.Average(row => row.SingleValue),
                    SumWide = group.Sum(row => row.Wide), AverageWide = group.Average(row => row.Wide),
                    MinimumWide = group.Min(row => row.Wide), MaximumWide = group.Max(row => row.Wide),
                    Minimum = group.Min(row => row.Amount), Maximum = group.Max(row => row.Amount),
                    Filtered = group.Where(row => row.Id > 0).Select(row => row.Count).Distinct().Sum()
                }).Where(group => group.AverageCount > 0d).OrderBy(group => group.SumWide).ToQueryString();
                if (!aggregateSql.Contains("SUM(", StringComparison.Ordinal)
                    || !aggregateSql.Contains("AVG(", StringComparison.Ordinal)
                    || !aggregateSql.Contains("MIN(", StringComparison.Ordinal)
                    || !aggregateSql.Contains("MAX(", StringComparison.Ordinal)
                    || !aggregateSql.Contains("HAVING", StringComparison.Ordinal)
                    || !aggregateSql.Contains("ORDER BY", StringComparison.Ordinal)
                    || !aggregateSql.Contains("SUM(DISTINCT (CASE", StringComparison.Ordinal)
                    || aggregateSql.Contains("AVG(CAST(", StringComparison.Ordinal)
                    || aggregateSql.Contains(" AS DOUBLE", StringComparison.Ordinal)
                    || aggregateSql.Contains(" AS BINARY_DOUBLE)", StringComparison.Ordinal))
                    throw new Exception("Packed aggregates lost native input precision or Single result normalization: " + aggregateSql);
                var singleAggregateTarget = mode == OceanBaseCompatibilityMode.MySql ? "FLOAT" : "BINARY_FLOAT";
                var aggregateAliasSeparator = mode == OceanBaseCompatibilityMode.MySql ? " AS " : " ";
                var aggregateIdentifiers = context.GetService<ISqlGenerationHelper>();
                foreach (var projection in new[] { "SumSingle", "AverageSingle", "SumWide", "AverageWide", "MinimumWide", "MaximumWide" })
                {
                    var normalizedProjection = " AS " + singleAggregateTarget + ")" + aggregateAliasSeparator
                        + aggregateIdentifiers.DelimitIdentifier(projection);
                    if (!aggregateSql.Contains(normalizedProjection, StringComparison.Ordinal))
                        throw new Exception("Packed aggregate projection lost Single normalization: " + projection + ": " + aggregateSql);
                }

                var extrema = context.Set<Row>().Where(row => Math.Min(row.Count, 5L) > 0
                    && Math.Max(row.DoubleValue, 3d) > 1d
                    && MathF.Max(row.Wide, 2f) > 1f
                    && decimal.Min(row.Amount, 5m) > 0m
                    && EF.Functions.Greatest(row.Id, 2, 3) > 1).ToQueryString();
                var wideColumn = aggregateIdentifiers.DelimitIdentifier(nameof(Row.Wide), "r");
                var singleStoreType = mode == OceanBaseCompatibilityMode.MySql ? "FLOAT" : "BINARY_FLOAT";
                var wideExtremumPattern = System.Text.RegularExpressions.Regex.Escape($"CAST(GREATEST({wideColumn}, ")
                    + @"[^(),]+" + System.Text.RegularExpressions.Regex.Escape($") AS {singleStoreType})");
                if (extrema.Split("LEAST(", StringSplitOptions.None).Length != 3
                    || extrema.Split("GREATEST(", StringSplitOptions.None).Length != 4
                    || !System.Text.RegularExpressions.Regex.IsMatch(extrema, wideExtremumPattern))
                    throw new Exception("Packed scalar extrema lost native translation or Single normalization: " + extrema);

                int[] extremaValues = [1, 2, 3, 4, 5, 6, 7, 8, 9];
                var capturedExtremum = context.Set<Row>().Where(row => EF.Functions.Least(extremaValues) < row.Id);
                foreach (var current in new[] { 1, 11, 1 })
                {
                    extremaValues[0] = current;
                    var capturedSql = capturedExtremum.ToQueryString();
                    var expected = "LEAST(" + current.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", 2, 3,";
                    if (!capturedSql.Contains(expected, StringComparison.Ordinal))
                        throw new Exception("Packed extrema reused stale captured values: " + capturedSql);
                }

                var roundEven = context.Set<Row>().Where(row => Math.Round(row.Amount, 2) > 0m).ToQueryString();
                var roundAway = context.Set<Row>().Where(row =>
                    decimal.Round(row.Amount, 28, MidpointRounding.AwayFromZero) > 0m).ToQueryString();
                var roundZero = context.Set<Row>().Where(row =>
                    decimal.Round(row.Amount, 3, MidpointRounding.ToZero) > 0m).ToQueryString();
                var truncate = mode == OceanBaseCompatibilityMode.MySql ? "TRUNCATE(" : "TRUNC(";
                if (!roundEven.Contains(truncate, StringComparison.Ordinal) || !roundEven.Contains(", 2)", StringComparison.Ordinal)
                    || !roundEven.Contains(" OR ", StringComparison.Ordinal) || !roundEven.Contains("0.02", StringComparison.Ordinal))
                    throw new Exception("Packed decimal Round lost the even midpoint check: " + roundEven);
                if (!roundAway.Contains(truncate, StringComparison.Ordinal) || !roundAway.Contains(", 28)", StringComparison.Ordinal)
                    || !roundAway.Contains(" >= ", StringComparison.Ordinal))
                    throw new Exception("Packed decimal Round lost the away-from-zero midpoint check: " + roundAway);
                if (!roundZero.Contains(truncate, StringComparison.Ordinal) || !roundZero.Contains(", 3)", StringComparison.Ordinal)
                    || roundZero.Contains("CASE", StringComparison.Ordinal))
                    throw new Exception("Packed decimal Round lost direct truncation toward zero: " + roundZero);

                var roundDouble = context.Set<Row>().Where(row => Math.Round(row.DoubleValue, 15) > 0d).ToQueryString();
                var roundSingle = context.Set<Row>().Where(row =>
                    MathF.Round(row.Wide, 6, MidpointRounding.AwayFromZero) > 0f).ToQueryString();
                if (!roundDouble.Contains(truncate, StringComparison.Ordinal) || !roundDouble.Contains(" OR ", StringComparison.Ordinal)
                    || !roundDouble.Contains("4503599627370496", StringComparison.Ordinal)
                    || !roundDouble.Contains("10000000000000000", StringComparison.Ordinal))
                    throw new Exception("Packed Double Round lost the guards or even midpoint check: " + roundDouble);
                var singleCast = mode == OceanBaseCompatibilityMode.MySql ? " AS FLOAT)" : " AS BINARY_FLOAT)";
                if (!roundSingle.Contains(truncate, StringComparison.Ordinal)
                    || roundSingle.Split(singleCast, StringSplitOptions.None).Length < 4
                    || !roundSingle.Contains("8388608", StringComparison.Ordinal)
                    || !roundSingle.Contains("100000000", StringComparison.Ordinal))
                    throw new Exception("Packed Single Round lost the guards or Single precision casts: " + roundSingle);

                var guidType = context.Model.FindEntityType(typeof(Row))!.FindProperty(nameof(Row.Token))!.GetColumnType();
                if (guidType != (mode == OceanBaseCompatibilityMode.Oracle ? "RAW(16)" : "CHAR(36)"))
                {
                    throw new Exception("Wrong Guid storage type.");
                }

                var temporalTypes = mode == OceanBaseCompatibilityMode.Oracle
                    ? new[] { "TIMESTAMP(7)", "TIMESTAMP(7) WITH TIME ZONE", "DATE",
                        "INTERVAL DAY(1) TO SECOND(7)", "INTERVAL DAY(9) TO SECOND(7)" }
                    : new[] { "DATETIME(6)", "DATETIME(6)", "DATE", "TIME(6)", "TIME(6)" };
                var temporalProperties = new[] { nameof(Row.OccurredAt), nameof(Row.OffsetAt), nameof(Row.Day),
                    nameof(Row.Clock), nameof(Row.Duration) };
                for (var index = 0; index < temporalProperties.Length; index++)
                {
                    if (context.Model.FindEntityType(typeof(Row))!.FindProperty(temporalProperties[index])!.GetColumnType()
                        != temporalTypes[index])
                    {
                        throw new Exception($"Wrong temporal storage type for {temporalProperties[index]}.");
                    }
                }

                var rowType = context.Model.FindEntityType(typeof(Row))!;
                var explicitGuid = rowType.FindProperty(nameof(Row.ExplicitToken))!.GetRelationalTypeMapping();
                if (explicitGuid.StoreType != (mode == OceanBaseCompatibilityMode.Oracle ? "VARCHAR2(36)" : "BINARY(16)"))
                {
                    throw new Exception("Explicit Guid storage was not preserved by its mapping.");
                }

                var token = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
                var guidParameterSql = context.Set<Row>().Where(row => row.ExplicitToken == token).ToQueryString();
                if (!guidParameterSql.Contains(mode == OceanBaseCompatibilityMode.Oracle
                        ? "00112233-4455-6677-8899-aabbccddeeff" : "33221100554477668899AABBCCDDEEFF", StringComparison.Ordinal))
                {
                    throw new Exception("Explicit Guid query operand used the wrong representation.");
                }

                if (rowType.FindProperty(nameof(Row.Payload))!.GetColumnType()
                        != (mode == OceanBaseCompatibilityMode.Oracle ? "RAW(8)" : "VARBINARY(8)")
                    || rowType.FindProperty(nameof(Row.LargePayload))!.GetColumnType()
                        != (mode == OceanBaseCompatibilityMode.Oracle ? "BLOB" : "LONGBLOB"))
                {
                    throw new Exception("Wrong bounded/unbounded binary storage type.");
                }

                if (rowType.FindProperty(nameof(Row.Count))!.GetRelationalTypeMapping().StoreType
                        != (mode == OceanBaseCompatibilityMode.Oracle ? "NUMBER(3)" : "MEDIUMINT")
                    || rowType.FindProperty(nameof(Row.Enabled))!.GetRelationalTypeMapping().StoreType
                        != (mode == OceanBaseCompatibilityMode.Oracle ? "NUMBER(3)" : "BOOLEAN"))
                {
                    throw new Exception("Explicit integer/Boolean store types were not preserved by their mappings.");
                }

                if (rowType.FindProperty(nameof(Row.Amplitude))!.GetRelationalTypeMapping().StoreType
                        != (mode == OceanBaseCompatibilityMode.Oracle ? "BINARY_FLOAT" : "FLOAT(24)")
                    || rowType.FindProperty(nameof(Row.Wide))!.GetRelationalTypeMapping().StoreType
                        != (mode == OceanBaseCompatibilityMode.Oracle ? "BINARY_DOUBLE" : "DOUBLE PRECISION"))
                {
                    throw new Exception("Explicit floating-point store types were not preserved by their mappings.");
                }

                var floatingSql = context.Set<Row>().Select(row => row.Amplitude + 0.1d).ToQueryString();
                if (!floatingSql.Contains(mode == OceanBaseCompatibilityMode.Oracle ? "0.1D" : "0.1E0", StringComparison.Ordinal))
                {
                    throw new Exception("Floating-point literal used the wrong representation.");
                }

                var temporalBoundary = new DateTime(2026, 11, 10, 12, 0, 0).AddTicks(1234560);
                var durationBoundary = TimeSpan.FromDays(mode == OceanBaseCompatibilityMode.Oracle ? 100 : 1)
                    .Add(TimeSpan.FromTicks(1234560));
                var temporalSql = context.Set<Row>().Where(row => row.NarrowStamp < temporalBoundary
                    && row.NarrowDuration < durationBoundary).Select(row => row.Id).ToQueryString();
                if (!temporalSql.Contains(".123456", StringComparison.Ordinal))
                {
                    throw new Exception("Temporal query comparands lost fractional precision.");
                }

                var amountMapping = rowType.FindProperty(nameof(Row.Amount))!.GetRelationalTypeMapping();
                using var command = new OceanBaseCommand();
                var amountParameter = (OceanBaseParameter)amountMapping.CreateParameter(command, "boundary", decimal.MaxValue);
                if (amountMapping.Precision != 5 || amountMapping.Scale != 2
                    || amountParameter.Precision != 0 || amountParameter.DecimalScale is not null
                    || !Equals(amountParameter.Value, decimal.MaxValue))
                {
                    throw new Exception("Decimal query transport inherited physical column precision.");
                }

                var captionMapping = rowType.FindProperty(nameof(Row.Name))!.GetRelationalTypeMapping();
                var captionParameter = (OceanBaseParameter)captionMapping.CreateParameter(command, "caption", "海洋🌊数据库");
                if (captionParameter.Size != "海洋🌊数据库".Length || captionMapping.Size != 4
                    || captionParameter.OceanBaseDbType != (mode == OceanBaseCompatibilityMode.Oracle
                        ? OceanBaseConnector.OceanBaseDbType.NVarchar2 : (OceanBaseConnector.OceanBaseDbType?)null))
                {
                    throw new Exception("National text query binding lost its type or transport length.");
                }

                var source = context.GetService<IRelationalTypeMappingSource>();
                var integerMapping = source.FindMapping(typeof(int))!;
                foreach (var fraction in new object[] { 1.5f, 1.5d })
                {
                    var fractionalReadRejected = false;
                    var read = integerMapping.CustomizeDataReaderExpression(Expression.Constant(fraction, typeof(object)));
                    try { Expression.Lambda<Func<int>>(read).Compile()(); }
                    catch (InvalidCastException) { fractionalReadRejected = true; }
                    if (!fractionalReadRejected) throw new Exception("A floating database fraction was rounded to an integer.");
                }

                var midpoint = new OceanBaseDecimal(1.000000059604644775390625m);
                var aboveMidpoint = new OceanBaseDecimal(midpoint.Coefficient * BigInteger.Pow(10, 36) + 1, 60);
                var singleRead = source.FindMapping(typeof(float))!.CustomizeDataReaderExpression(
                    Expression.Constant(aboveMidpoint, typeof(object)));
                if (BitConverter.SingleToInt32Bits(Expression.Lambda<Func<float>>(singleRead).Compile()()) != 0x3f800001)
                {
                    throw new Exception("Wide decimal normalization lost direct Single rounding.");
                }
                var doubleRead = source.FindMapping(typeof(double))!.CustomizeDataReaderExpression(
                    Expression.Constant(new OceanBaseDecimal(BigInteger.One, -30), typeof(object)));
                if (Expression.Lambda<Func<double>>(doubleRead).Compile()() != 1E30d)
                {
                    throw new Exception("Wide decimal normalization overflowed before Double conversion.");
                }

                var temporalFacetRejected = false;
                try { source.FindMapping(typeof(DateTime), null, size: 3); }
                catch (ArgumentException) { temporalFacetRejected = true; }
                if (!temporalFacetRejected) throw new Exception("A temporal length facet was silently discarded.");

                var temporalCloneRejected = false;
                try { source.FindMapping(typeof(DateTime))!.WithStoreTypeAndSize("VARCHAR(4)", null); }
                catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
                {
                    temporalCloneRejected = true;
                }
                if (!temporalCloneRejected) throw new Exception("An incompatible temporal clone was silently accepted.");

                var temporalTransportRejected = false;
                try { source.FindMapping(typeof(DateTime))!.Clone(new RelationalTypeMappingInfo(typeof(DateTime), dbType: DbType.String)); }
                catch (ArgumentException) { temporalTransportRejected = true; }
                if (!temporalTransportRejected) throw new Exception("A temporal clone changed its parameter transport.");

                if (mode == OceanBaseCompatibilityMode.Oracle)
                {
                    foreach (var declaration in new[] { "DECIMAL", "NUMERIC" })
                    {
                        foreach (var type in new[] { typeof(decimal), typeof(int), typeof(bool) })
                        {
                            var aliasMapping = source.FindMapping(type, declaration)!;
                            if (aliasMapping.Precision != 38 || aliasMapping.Scale != 0)
                            {
                                throw new Exception("Oracle fixed-point alias defaults were not preserved.");
                            }
                        }

                        var alias = source.FindMapping(typeof(decimal), declaration)!;
                        var input = (OceanBaseParameter)alias.CreateParameter(command, "aliasInput", 1.25m);
                        var output = (OceanBaseParameter)alias.CreateParameter(command, "aliasOutput", null,
                            direction: ParameterDirection.Output);
                        if (!Equals(input.Value, 1.25m) || input.Precision != 0 || input.DecimalScale is not null
                            || output.Precision != 38 || output.DecimalScale != 0)
                        {
                            throw new Exception("Oracle fixed-point aliases changed query inputs or lost output facets.");
                        }
                    }

                    var number = source.FindMapping(typeof(decimal), "NUMBER")!;
                    if (number.Precision is not null || number.Scale is not null)
                    {
                        throw new Exception("Unqualified Oracle NUMBER lost value inference.");
                    }
                }

                foreach (var declaration in mode == OceanBaseCompatibilityMode.Oracle
                    ? new[] { "NVARCHAR2(4) trailing", "NCLOB(4)", "VARCHAR2" }
                    : new[] { "VARCHAR(4) trailing", "TEXT(4)", "VARCHAR" })
                {
                    var rejected = false;
                    try { source.FindMapping(typeof(string), declaration); }
                    catch (ArgumentException) { rejected = true; }
                    if (!rejected) throw new Exception("An invalid text declaration was silently accepted.");
                }

                var invalidUnicodeRejected = false;
                try { captionMapping.CreateParameter(command, "caption", "\ud800"); }
                catch (ArgumentException) { invalidUnicodeRejected = true; }
                if (!invalidUnicodeRejected) throw new Exception("Invalid UTF-16 was silently accepted.");

                Console.WriteLine($"{mode}: packed provider registration, scalar/text/temporal/binary/decimal model, string/null/Boolean SQL and paging passed; EF {typeof(DbContext).Assembly.GetName().Version}.");
            }
        }

        private static void VerifyNonnumericExtrema(ConsumerContext context, OceanBaseCompatibilityMode mode)
        {
            var sql = context.Set<Row>().GroupBy(row => row.Id).Select(group => new
            {
                MinText = group.Min(row => row.Name), MaxText = group.Max(row => row.Name),
                MinStamp = group.Min(row => row.OccurredAt), MaxStamp = group.Max(row => row.OccurredAt),
                MinZoned = group.Min(row => row.OffsetAt), MaxZoned = group.Max(row => row.OffsetAt),
                MinDay = group.Min(row => row.Day), MaxDay = group.Max(row => row.Day),
                MinClock = group.Min(row => row.Clock), MaxClock = group.Max(row => row.Clock),
                MinDuration = group.Min(row => row.Duration), MaxDuration = group.Max(row => row.Duration)
            }).ToQueryString();
            if (sql.Split("MIN(", StringSplitOptions.None).Length - 1 != 6
                || sql.Split("MAX(", StringSplitOptions.None).Length - 1 != 6
                || !sql.Contains("GROUP BY", StringComparison.Ordinal) || sql.Contains("CAST(", StringComparison.Ordinal))
                throw new Exception("Packed text/temporal MIN/MAX lost its native shape.");
            var rejected = false;
            try { _ = context.Set<AggregateBoundaryRow>().GroupBy(row => row.Id).Select(group => group.Min(row => row.Text)).ToQueryString(); }
            catch (InvalidOperationException error) when (error.Message.Contains("OceanBase MIN", StringComparison.Ordinal)) { rejected = true; }
            if (!rejected) throw new Exception("Packed MIN accepted a null-converting string mapping.");
            Console.WriteLine($"{mode}: packed text/temporal MIN/MAX and null-converter boundary passed.");
        }

        private static void VerifyTextRoots(ConsumerContext context, OceanBaseCompatibilityMode mode)
        {
            string?[] values = ["one", null, "two"];
            var query = context.Set<Row>().SelectMany(row => values.Where(value => value != row.Name));
            foreach (var input in new string?[][] { values, [], ["again", "again"] })
            {
                values = input;
                var sql = query.ToQueryString();
                if (sql.Contains("UNION ALL VALUES", StringComparison.Ordinal)
                    || sql.Split("UNION ALL SELECT", StringSplitOptions.None).Length - 1 != Math.Max(0, input.Length - 1)
                    || sql.Contains("WHERE 1 = 0", StringComparison.Ordinal) != (input.Length == 0))
                    throw new Exception("Packed text collection root lost its current values or row shape: " + sql);
                if (mode == OceanBaseCompatibilityMode.Oracle && input.Length > 0
                    && sql.Split("FROM DUAL", StringSplitOptions.None).Length - 1 != input.Length)
                    throw new Exception("Oracle text collection SELECT arms do not each have FROM DUAL.");
            }

            var empty = context.Set<Row>().Select(row => new string[] { }.Count(value => row.Id > 0)).ToQueryString();
            if (!empty.Contains("WHERE 1 = 0", StringComparison.Ordinal)
                || !empty.Contains(mode == OceanBaseCompatibilityMode.MySql ? "CAST(NULL AS CHAR)" : "CAST(NULL AS NVARCHAR2",
                    StringComparison.Ordinal))
                throw new Exception("Packed text collection root lost its typed empty relation: " + empty);
        }

        private static void VerifyBooleanRoots(ConsumerContext context, OceanBaseCompatibilityMode mode)
        {
            bool?[] values = [true, null, false, true];
            var query = context.Set<Row>().SelectMany(row => values.Where(value => value != row.OptionalBoolean));
            foreach (var input in new bool?[][] { values, [], [false, false], [] })
            {
                values = input;
                var sql = query.ToQueryString();
                if (!sql.Contains(mode == OceanBaseCompatibilityMode.MySql ? " AS SIGNED)" : " AS NUMBER)", StringComparison.Ordinal)
                    || sql.Split("UNION ALL SELECT", StringSplitOptions.None).Length - 1 != Math.Max(0, input.Length - 1)
                    || sql.Contains("WHERE 1 = 0", StringComparison.Ordinal) != (input.Length == 0))
                    throw new Exception("Packed Boolean collection root lost its current values, carrier or row shape: " + sql);
                if (mode == OceanBaseCompatibilityMode.Oracle && input.Length > 0
                    && sql.Split("FROM DUAL", StringSplitOptions.None).Length - 1 != input.Length)
                    throw new Exception("Oracle Boolean collection SELECT arms do not each have FROM DUAL.");
            }

            var empty = context.Set<Row>().Select(row => new bool[] { }.Count(value => row.Id > 0)).ToQueryString();
            if (!empty.Contains("WHERE 1 = 0", StringComparison.Ordinal)
                || !empty.Contains(mode == OceanBaseCompatibilityMode.MySql ? "CAST(NULL AS SIGNED)" : "CAST(NULL AS NUMBER)",
                    StringComparison.Ordinal))
                throw new Exception("Packed Boolean collection root lost its typed empty relation: " + empty);
        }

        private static void VerifyBinaryRoots(ConsumerContext context, OceanBaseCompatibilityMode mode)
        {
            byte[][] values = [];
            var query = context.Set<Row>().SelectMany(row => values.Where(value => row.Id > 0));
            var target = mode == OceanBaseCompatibilityMode.MySql ? "BINARY" : "BLOB";
            foreach (var input in new[]
            {
                Array.Empty<byte[]>(),
                new[] { new byte[] { 1, 2 } },
                Array.Empty<byte[]>(),
                new[] { new byte[] { 3 } },
                Array.Empty<byte[]>()
            })
            {
                values = input;
                var sql = query.ToQueryString();
                if (!sql.Contains(" AS " + target + ")", StringComparison.Ordinal))
                    throw new Exception("Packed binary collection root lost its provider carrier: " + sql);
                if (sql.Split("UNION ALL SELECT", StringSplitOptions.None).Length - 1 != Math.Max(0, input.Length - 1)
                    || sql.Contains("WHERE 1 = 0", StringComparison.Ordinal) != (input.Length == 0))
                    throw new Exception("Packed binary collection root lost its current values or row shape.");
            }
            var empty = context.Set<Row>().Select(row => new byte[][] { }.Count(value => row.Id > 0)).ToQueryString();
            if (!empty.Contains("CAST(NULL AS " + target + ")", StringComparison.Ordinal)
                || !empty.Contains("WHERE 1 = 0", StringComparison.Ordinal))
                throw new Exception("Packed binary collection root lost its typed empty relation: " + empty);
        }

        private static void VerifyTemporalRoots(ConsumerContext context, OceanBaseCompatibilityMode mode)
        {
            TimeSpan?[] durations = [new TimeSpan(1, 2, 3), null];
            var durationQuery = context.Set<Row>().SelectMany(row => durations.Where(value => value != row.Duration));
            TimeOnly?[] clocks = [new TimeOnly(1, 2, 3), null];
            var clockQuery = context.Set<Row>().SelectMany(row => clocks.Where(value => value != row.Clock));
            var target = mode == OceanBaseCompatibilityMode.MySql ? "TIME" : "INTERVAL DAY";
            foreach (var sql in new[] { durationQuery.ToQueryString(), clockQuery.ToQueryString() })
            {
                if (!sql.Contains(target, StringComparison.OrdinalIgnoreCase))
                    throw new Exception("Packed temporal collection root lost its carrier: " + sql);
            }

            var empty = context.Set<Row>().Select(row => new TimeSpan[] { }.Count(value => row.Id > 0)).ToQueryString();
            if (!empty.Contains("WHERE 1 = 0", StringComparison.Ordinal)
                || !empty.Contains("CAST(NULL AS " + target, StringComparison.OrdinalIgnoreCase))
                throw new Exception("Packed temporal collection root lost its typed empty relation: " + empty);
        }

        private static void VerifyNumericRoots<T>(ConsumerContext context, OceanBaseCompatibilityMode mode,
            T minimum, T maximum, T tiny, string mysql, string oracle) where T : struct
        {
            T?[] values = [];
            var query = context.Set<Row>().SelectMany(row => values.Where(value => row.Id > 0));
            var target = mode == OceanBaseCompatibilityMode.MySql ? mysql : oracle;
            foreach (var input in new T?[][] { [], [minimum, maximum, tiny, minimum, null], [null, null], [tiny], [] })
            {
                values = input;
                var sql = query.ToQueryString();
                if (!sql.Contains(" AS " + target + ")", StringComparison.Ordinal)
                    || sql.Split("UNION ALL SELECT", StringSplitOptions.None).Length - 1 != Math.Max(0, input.Length - 1)
                    || sql.Contains("WHERE 1 = 0", StringComparison.Ordinal) != (input.Length == 0))
                    throw new Exception("Packed numeric root lost its provider carrier, row count or empty transition.");
            }
            var empty = context.Set<Row>().Select(row => new T[] { }.Count(value => row.Id > 0)).ToQueryString();
            if (!empty.Contains("CAST(NULL AS " + target + ")", StringComparison.Ordinal)
                || !empty.Contains("WHERE 1 = 0", StringComparison.Ordinal))
                throw new Exception("Packed numeric root lost its original empty-inline type.");
        }

        private static void VerifyIntegerRoots<T>(ConsumerContext context, OceanBaseCompatibilityMode mode,
            T minimum, T maximum, bool unsigned) where T : struct
        {
            T?[] values = [];
            var query = context.Set<Row>().SelectMany(row => values.Where(value => row.Id > 0));
            foreach (var input in new T?[][] { [], [minimum, maximum, minimum, null], [null, null], [maximum], [] })
            {
                values = input;
                var sql = query.ToQueryString();
                var target = mode == OceanBaseCompatibilityMode.Oracle ? "NUMBER" : unsigned ? "UNSIGNED" : "SIGNED";
                if (!sql.Contains(" AS " + target + ")", StringComparison.Ordinal)
                    || sql.Split("UNION ALL SELECT", StringSplitOptions.None).Length - 1 != Math.Max(0, input.Length - 1)
                    || sql.Contains("WHERE 1 = 0", StringComparison.Ordinal) != (input.Length == 0))
                    throw new Exception("Packed integer root lost its carrier, current values or zero-row SQL.");
            }
            var empty = context.Set<Row>().Select(row => new T[] { }.Count(value => row.Id > 0)).ToQueryString();
            if (!empty.Contains("WHERE 1 = 0", StringComparison.Ordinal)
                || !empty.Contains(mode == OceanBaseCompatibilityMode.Oracle ? "CAST(NULL AS NUMBER)"
                    : unsigned ? "CAST(NULL AS UNSIGNED)" : "CAST(NULL AS SIGNED)", StringComparison.Ordinal))
                throw new Exception("Packed inline-empty root used the Count result type instead of its integer element type.");
        }

        private static void VerifyNullableConcurrency(OceanBaseCompatibilityMode mode)
        {
            var commands = new CaptureWrite();
            using var context = new WriteContext(new DbContextOptionsBuilder<WriteContext>()
                .UseOceanBase("Server=localhost;Database=package_smoke", mode)
                .AddInterceptors(new SuppressNetwork(), commands).Options);
            context.Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
            var row = new VersionedRow { TenantId = 2, Id = 17, Name = "before" };
            context.Attach(row);
            row.Name = "after";
            row.Version = 9;
            if (context.SaveChanges() != 1) throw new Exception("Packed nullable-token update failed.");
            commands.Verify("UPDATE VersionedRows SET Name = ?, Version = ? WHERE Id = ? AND TenantId = ? AND Version IS NULL;",
                ["after", 9, 17, 2]);
            row.Version = null;
            if (context.SaveChanges() != 1) throw new Exception("Packed NULL write failed.");
            commands.Verify("UPDATE VersionedRows SET Version = ? WHERE Id = ? AND TenantId = ? AND Version = ?;",
                [DBNull.Value, 17, 2, 9]);
            context.Remove(row);
            if (context.SaveChanges() != 1) throw new Exception("Packed nullable-token delete failed.");
            commands.Verify("DELETE FROM VersionedRows WHERE Id = ? AND TenantId = ? AND Version IS NULL;", [17, 2]);
            Console.WriteLine($"{mode}: packed provider composite nullable concurrency and NULL writes passed.");
        }

        private sealed class CaptureWrite : DbCommandInterceptor
        {
            private string _sql = "";
            private object?[] _values = [];

            public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
            {
                var parameters = command.Parameters.Cast<DbParameter>().ToArray();
                var binds = System.Text.RegularExpressions.Regex.Matches(command.CommandText, @"[:@]\w+|\?");
                if (binds.Count != parameters.Length) throw new Exception("Packed write has missing or unused bindings.");
                _values = binds.Select((bind, index) => bind.Value == "?" ? parameters[index].Value
                    : parameters.Single(parameter => parameter.ParameterName.TrimStart(':', '@') == bind.Value.TrimStart(':', '@')).Value).ToArray();
                _sql = System.Text.RegularExpressions.Regex.Replace(command.CommandText
                    .Replace("`", "", StringComparison.Ordinal).Replace("\"", "", StringComparison.Ordinal), @"[:@]\w+|\?", "?");
                _sql = System.Text.RegularExpressions.Regex.Replace(_sql, @"\s+", " ").Trim();
                return InterceptionResult<int>.SuppressWithResult(1);
            }

            public void Verify(string sql, object?[] values)
            {
                if (_sql != sql || !_values.SequenceEqual(values))
                    throw new Exception("Packed nullable concurrency SQL or bindings changed: " + _sql);
            }
        }

        private sealed class WriteContext(DbContextOptions<WriteContext> options) : DbContext(options)
        {
            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity<VersionedRow>().ToTable("VersionedRows");
                modelBuilder.Entity<VersionedRow>().HasKey(row => new { row.TenantId, row.Id });
                modelBuilder.Entity<VersionedRow>().Property(row => row.Id).ValueGeneratedNever();
                modelBuilder.Entity<VersionedRow>().Property(row => row.Name).HasMaxLength(80);
                modelBuilder.Entity<VersionedRow>().Property(row => row.Version).IsConcurrencyToken();
            }
        }

        private sealed class VersionedRow
        {
            public int TenantId { get; set; }
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public int? Version { get; set; }
        }

        private static void VerifyFailedOpen(OceanBaseCompatibilityMode mode)
        {
            using var context = new DbContext(new DbContextOptionsBuilder()
                .UseOceanBase("Server=localhost;Database=package_smoke", mode)
                .AddInterceptors(new SuppressNetwork())
                .ConfigureWarnings(warnings => warnings.Throw(RelationalEventId.AmbientTransactionWarning)).Options);
            var connection = context.GetService<IRelationalConnection>();
            using (var ambient = new TransactionScope())
            {
                try
                {
                    connection.Open();
                    throw new Exception("Packed provider did not reject the unsupported ambient transaction.");
                }
                catch (InvalidOperationException error) when (error.Message.Contains("AmbientTransactionWarning", StringComparison.Ordinal))
                {
                }
            }
            // Package-only runtime probe: EF has no public open-count API.
            var openedCount = typeof(RelationalConnection).GetField("_openedCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
            if ((int)openedCount.GetValue(connection)! != 0)
                throw new Exception("Packed provider retained a failed open lease.");
            connection.Open();
            connection.Close();
            if ((int)openedCount.GetValue(connection)! != 0)
                throw new Exception("Packed provider could not balance a retry after failed open.");
            Console.WriteLine($"{mode}: packed provider failed-open accounting and retry passed.");
        }

        private sealed class SuppressNetwork : DbConnectionInterceptor
        {
            public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
                => InterceptionResult.Suppress();
        }

        private static string MembershipSql(ConsumerContext context, int[] values)
            => context.Set<Row>().Where(row => values.Contains(row.Id)).ToQueryString();

        private static ConsumerContext CreateMembershipContext(OceanBaseCompatibilityMode mode, ParameterTranslationMode? collectionMode)
        {
            var builder = new DbContextOptionsBuilder<ConsumerContext>().UseOceanBase("Server=localhost;Database=package_smoke", mode);
            if (collectionMode.HasValue)
            {
                var extension = builder.Options.Extensions.OfType<RelationalOptionsExtension>().Single()
                    .WithUseParameterizedCollectionMode(collectionMode.Value);
                ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(extension);
            }
            return new ConsumerContext(builder.Options, mode);
        }

        private sealed class ConsumerContext(DbContextOptions<ConsumerContext> options, OceanBaseCompatibilityMode mode) : DbContext(options)
        {
            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
#pragma warning disable EF1001
                modelBuilder.Entity<AggregateBoundaryRow>().Property(row => row.Text).HasConversion(
                    new ValueConverter<string, string>(value => value ?? "missing", value => value, convertsNulls: true));
#pragma warning restore EF1001
                modelBuilder.Entity<Row>().Property(row => row.Name).HasMaxLength(4);
                modelBuilder.Entity<Row>().Property(row => row.Payload).HasMaxLength(8);
                modelBuilder.Entity<Row>().Property(row => row.Count).HasColumnType(mode == OceanBaseCompatibilityMode.Oracle ? "NUMBER(3)" : "MEDIUMINT");
                modelBuilder.Entity<Row>().Property(row => row.Enabled).HasColumnType(mode == OceanBaseCompatibilityMode.Oracle ? "NUMBER(3)" : "BOOLEAN");
                modelBuilder.Entity<Row>().Property(row => row.BooleanText)
                    .HasConversion(value => value ? "Y" : "N", value => value == "Y");
                modelBuilder.Entity<Row>().Property(row => row.BooleanNumber)
                    .HasConversion(value => value ? 2 : -1, value => value == 2);
                modelBuilder.Entity<Row>().Property(row => row.NullableInteger).HasConversion(
                    new ValueConverter<int?, ulong?>(value => (ulong?)value, value => (int?)value));
                modelBuilder.Entity<Row>().Property(row => row.ExplicitToken).HasColumnType(mode == OceanBaseCompatibilityMode.Oracle ? "VARCHAR2(36)" : "BINARY(16)");
                modelBuilder.Entity<Row>().Property(row => row.Amplitude).HasColumnType(mode == OceanBaseCompatibilityMode.Oracle ? "BINARY_FLOAT" : "FLOAT(24)");
                modelBuilder.Entity<Row>().Property(row => row.Wide).HasColumnType(mode == OceanBaseCompatibilityMode.Oracle ? "BINARY_DOUBLE" : "DOUBLE PRECISION");
                modelBuilder.Entity<Row>().Property(row => row.NarrowStamp).HasPrecision(3);
                modelBuilder.Entity<Row>().Property(row => row.LegacyStamp)
                    .HasColumnType(mode == OceanBaseCompatibilityMode.Oracle ? "DATE" : "DATETIME(6)");
                modelBuilder.Entity<Row>().Property(row => row.Amount).HasPrecision(5, 2);
                modelBuilder.Entity<Row>().Property(row => row.NarrowDuration)
                    .HasColumnType(mode == OceanBaseCompatibilityMode.Oracle ? "INTERVAL DAY(1) TO SECOND(3)" : "TIME(3)");
            }
        }

        private sealed class Row
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public string? SearchText { get; set; }
            public bool Enabled { get; set; }
            public bool BooleanText { get; set; }
            public bool BooleanNumber { get; set; }
            public bool? OptionalBoolean { get; set; }
            public int? NullableInteger { get; set; }
            public Guid Token { get; set; }
            public Guid ExplicitToken { get; set; }
            public DateTime OccurredAt { get; set; }
            public DateTimeOffset OffsetAt { get; set; }
            public DateOnly Day { get; set; }
            public TimeOnly Clock { get; set; }
            public TimeSpan Duration { get; set; }
            public byte[] Payload { get; set; } = [];
            public byte[]? LargePayload { get; set; }
            public long Count { get; set; }
            public double Amplitude { get; set; }
            public double DoubleValue { get; set; }
            public float SingleValue { get; set; }
            public float Wide { get; set; }
            public DateTime NarrowStamp { get; set; }
            public DateTime LegacyStamp { get; set; }
            public decimal Amount { get; set; }
            public TimeSpan NarrowDuration { get; set; }
            public uint Unsigned { get; set; }
            public ulong Bits { get; set; }
            public ByteFlags Flags { get; set; }
            public byte Narrow { get; set; }
        }

        private sealed class AggregateBoundaryRow
        {
            public int Id { get; set; }
            public string Text { get; set; } = "";
        }

        [Flags]
        private enum ByteFlags : byte { Low = 1 }
    }
}
