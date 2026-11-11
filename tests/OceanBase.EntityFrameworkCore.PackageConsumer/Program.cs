using System;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Numerics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBase.EntityFrameworkCore;
using OceanBaseCommand = OceanBaseConnector.OceanBaseCommand;
using OceanBaseDecimal = OceanBaseConnector.OceanBaseDecimal;
using OceanBaseParameter = OceanBaseConnector.OceanBaseParameter;

namespace PackageConsumer
{
    internal static class Program
    {
        private static void Main()
        {
            foreach (var mode in new[] { OceanBaseCompatibilityMode.MySql, OceanBaseCompatibilityMode.Oracle })
            {
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
                if (!rawSql.Contains("WHERE Name = " + explicitName, StringComparison.Ordinal))
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
                    (context.Set<Row>().Where(row => Math.Log10(row.DoubleValue) > 0d), "LOG10", false),
                    (context.Set<Row>().Where(row => MathF.Log10(row.SingleValue) > 0f), "LOG10", true),
                    (context.Set<Row>().Where(row => Math.Pow(row.DoubleValue, 2d) > 0d), "POWER", false),
                    (context.Set<Row>().Where(row => MathF.Pow(row.SingleValue, 2f) > 0f), "POWER", true)
                })
                {
                    var numericSql = numericQuery.ToQueryString();
                    var function = operation switch
                    {
                        "LOG_BASE" => mode == OceanBaseCompatibilityMode.MySql ? "LN(" : "LOG(",
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
                modelBuilder.Entity<Row>().Property(row => row.Name).HasMaxLength(4);
                modelBuilder.Entity<Row>().Property(row => row.Payload).HasMaxLength(8);
                modelBuilder.Entity<Row>().Property(row => row.Count).HasColumnType(mode == OceanBaseCompatibilityMode.Oracle ? "NUMBER(3)" : "MEDIUMINT");
                modelBuilder.Entity<Row>().Property(row => row.Enabled).HasColumnType(mode == OceanBaseCompatibilityMode.Oracle ? "NUMBER(3)" : "BOOLEAN");
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
        }
    }
}
