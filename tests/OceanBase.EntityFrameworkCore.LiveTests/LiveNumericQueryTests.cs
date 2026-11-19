using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBaseConnector;
using Xunit;

namespace OceanBase.EntityFrameworkCore.LiveTests
{
    public sealed class LiveNumericQueryTests
    {
        public static IEnumerable<object[]> Cases()
        {
            foreach (var mode in Enum.GetValues<OceanBaseCompatibilityMode>())
            foreach (var async in new[] { false, true })
            foreach (var nulls in new[] { false, true }) yield return [mode, async, nulls];
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public Task Unary_numeric_functions_preserve_signed_fractional_and_NULL_results(
            OceanBaseCompatibilityMode mode, bool async, bool nulls)
            => WithTable(mode, async, nulls, async (context, stage) =>
            {
                foreach (var (type, property) in new[] { (typeof(sbyte), nameof(Row.SByte)), (typeof(short), nameof(Row.Int16)),
                    (typeof(int), nameof(Row.Int32)), (typeof(long), nameof(Row.Int64)), (typeof(float), nameof(Row.Single)),
                    (typeof(double), nameof(Row.Double)), (typeof(decimal), nameof(Row.Decimal)), (typeof(decimal), nameof(Row.Faceted)) })
                foreach (var name in type == typeof(float) || type == typeof(double) || type == typeof(decimal)
                    ? new[] { "Abs", "Sign", "Floor", "Ceiling", "Truncate" } : new[] { "Abs", "Sign" })
                {
                    stage(property + "/" + name);
                    var method = typeof(Math).GetMethod(name, [type]);
                    if (method?.GetParameters()[0].ParameterType == type)
                        await CheckCall(context, async, method, property, [], true);
                    if (type == typeof(float)) await CheckCall(context, async, typeof(MathF).GetMethod(name, [type])!, property, [], true);
                    if (type == typeof(decimal)) await CheckCall(context, async, typeof(decimal).GetMethod(name, [type])!, property, [], true);
                }
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public Task Transcendental_functions_evaluate_at_native_precision_with_Single_normalization(
            OceanBaseCompatibilityMode mode, bool async, bool nulls)
            => WithTable(mode, async, nulls, async (context, stage) =>
            {
                foreach (var (owner, type, positive, unit) in new[]
                {
                    (typeof(Math), typeof(double), nameof(Row.Positive), nameof(Row.Unit)),
                    (typeof(MathF), typeof(float), nameof(Row.PositiveSingle), nameof(Row.UnitSingle))
                })
                {
                    foreach (var name in new[] { "Sqrt", "Exp", "Log", "Log2", "Log10", "Sin", "Cos", "Tan", "Asin", "Acos", "Atan", "Sinh", "Cosh", "Tanh", "Asinh", "Atanh" })
                    {
                        var property = name is "Sqrt" or "Log" or "Log2" or "Log10" ? positive : unit;
                        stage(owner.Name + "/" + name);
                        await CheckCall(context, async, owner.GetMethod(name, [type])!, property, [], false);
                    }
                    foreach (var name in new[] { "Log", "Pow", "Atan2" })
                    {
                        stage(owner.Name + "/" + name + "/binary");
                        await CheckCall(context, async, owner.GetMethod(name, [type, type])!, positive,
                            [Convert.ChangeType(2, type)], false);
                    }
                }
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public Task Hyperbolic_small_large_and_transition_values_avoid_cancellation_and_intermediate_overflow(
            OceanBaseCompatibilityMode mode, bool async, bool nulls)
            => WithTable(mode, async, nulls, async (context, stage) =>
            {
                await HyperbolicBoundaries<double>(context, async, nameof(Row.Double), typeof(Math), stage);
                await HyperbolicBoundaries<float>(context, async, nameof(Row.Single), typeof(MathF), stage);
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public Task Acosh_preserves_domain_nulls_and_precision_across_finite_inputs(
            OceanBaseCompatibilityMode mode, bool async, bool nulls)
            => WithTable(mode, async, nulls, async (context, stage) =>
            {
                await Check(nameof(Row.Double), row => Math.Acosh(row.Double!.Value), Math.Acosh,
                    [double.MinValue, 0d, Math.BitDecrement(1d), 1d, Math.BitIncrement(1d), 1.0000019, 1.0000021, 2d, 1e150, double.MaxValue], 5e-12);
                await Check(nameof(Row.Single), row => MathF.Acosh(row.Single!.Value), MathF.Acosh,
                    [float.MinValue, 0f, MathF.BitDecrement(1f), 1f, MathF.BitIncrement(1f), 1.0000019f, 1.0000021f, 2f, 1e30f, float.MaxValue], 3e-6);

                async Task Check<T>(string property, Expression<Func<Row, T?>> projection, Func<T, T> expected,
                    T[] inputs, double relativeTolerance) where T : struct
                {
                    var mapping = context.Model.FindEntityType(typeof(Row))!.FindProperty(property)!.GetRelationalTypeMapping();
                    var helper = context.GetService<ISqlGenerationHelper>();
                    var valueQuery = context.Set<Row>().Where(row => row.Id == 1).Select(projection);
                    var selectDomain = valueQuery.Where(value => value != null);
                    foreach (var input in inputs.Select(value => (T?)value).Append(null))
                    {
                        stage($"{typeof(T).Name}.Acosh finite boundary={input}");
                        var sql = "UPDATE " + helper.DelimitIdentifier(context.Table) + " SET " + helper.DelimitIdentifier(property)
                            + " = " + (input.HasValue ? mapping.GenerateSqlLiteral(input.Value) : "NULL")
                            + " WHERE " + helper.DelimitIdentifier(nameof(Row.Id)) + " = 1";
                        Assert.Equal(1, async ? await context.Database.ExecuteSqlRawAsync(sql) : context.Database.ExecuteSqlRaw(sql));
                        if (!input.HasValue || Convert.ToDouble(input.Value) < 1)
                        {
                            Assert.Null(Assert.Single(await Read(valueQuery, async)));
                            Assert.Empty(await Read(selectDomain, async));
                            continue;
                        }
                        var actual = Convert.ToDouble(Assert.IsType<T>(Assert.Single(await Read(selectDomain, async))));
                        var wanted = Convert.ToDouble(expected(input.Value));
                        Assert.True(double.IsFinite(actual) && Math.Abs(actual - wanted) <= relativeTolerance * Math.Abs(wanted),
                            $"Acosh({input}): expected={wanted:R},actual={actual:R}");
                    }
                }
            });

        private static async Task HyperbolicBoundaries<T>(Context context, bool async, string property, Type owner, Action<string> stage) where T : struct
        {
            var single = typeof(T) == typeof(float);
            var maximum = single ? float.MaxValue : double.MaxValue;
            var helper = context.GetService<ISqlGenerationHelper>();
            var mapping = context.Model.FindEntityType(typeof(Row))!.FindProperty(property)!.GetRelationalTypeMapping();
            foreach (var name in new[] { "Sinh", "Cosh", "Tanh", "Asinh", "Atanh" })
            {
                var method = owner.GetMethod(name, [typeof(T)])!;
                var row = Expression.Parameter(typeof(Row), "row");
                var call = Expression.Call(method, Expression.Property(Expression.Property(row, property), "Value"));
                var query = context.Set<Row>().Where(value => value.Id == 1).Select(Expression.Lambda<Func<Row, T?>>(Expression.Convert(call, typeof(T?)), row));
                var magnitudes = name switch
                {
                    "Atanh" => new[] { 0d, 1e-12, 1e-5, 9.999e-4, 1e-3, 1.001e-3, 0.5, 0.999999 },
                    "Asinh" => [0d, 1e-12, 1e-5, 9.999e-4, 1e-3, 1.001e-3, 0.5, 1d, 1e10, single ? 1e20 : 1e150, maximum],
                    "Tanh" => [0d, 1e-12, 1e-5, 9.999e-4, 1e-3, 1.001e-3, 0.5, 20d, 21d, maximum],
                    _ => [0d, 1e-12, 1e-5, 9.999e-4, 1e-3, 1.001e-3, 0.5, 20d, 80d, single ? 89.4 : 710.47]
                };
                foreach (var magnitude in magnitudes)
                foreach (var sign in new[] { 1, -1 })
                {
                    var input = (T)Convert.ChangeType(magnitude * sign, typeof(T));
                    stage($"{owner.Name}.{name} finite boundary={input}");
                    var sql = "UPDATE " + helper.DelimitIdentifier(context.Table) + " SET " + helper.DelimitIdentifier(property)
                        + " = " + mapping.GenerateSqlLiteral(input) + " WHERE " + helper.DelimitIdentifier(nameof(Row.Id)) + " = 1";
                    Assert.Equal(1, async ? await context.Database.ExecuteSqlRawAsync(sql) : context.Database.ExecuteSqlRaw(sql));
                    var actual = Convert.ToDouble(Assert.Single(await Read(query, async))!.Value);
                    var expected = Convert.ToDouble(method.Invoke(null, [input]));
                    var tolerance = (single ? 3e-6 : 5e-12) * Math.Abs(expected);
                    Assert.True(double.IsFinite(actual) && Math.Abs(actual - expected) <= tolerance,
                        $"{owner.Name}.{name}({input}): expected={expected:R},actual={actual:R},tolerance={tolerance:R}");
                }
            }
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public Task Round_all_rules_and_exact_overloads_keep_midpoints_and_NULLs(
            OceanBaseCompatibilityMode mode, bool async, bool nulls)
            => WithTable(mode, async, nulls, async (context, stage) =>
            {
                foreach (var (owner, type, property, maximum) in new[]
                {
                    (typeof(Math), typeof(decimal), nameof(Row.Decimal), 28),
                    (typeof(decimal), typeof(decimal), nameof(Row.Faceted), 28),
                    (typeof(Math), typeof(double), nameof(Row.Double), 15),
                    (typeof(double), typeof(double), nameof(Row.Double), 15),
                    (typeof(MathF), typeof(float), nameof(Row.Single), 6),
                    (typeof(float), typeof(float), nameof(Row.Single), 6)
                })
                {
                    stage(owner.Name + "/Round/defaults");
                    await CheckCall(context, async, owner.GetMethod("Round", [type])!, property, [], true);
                    await CheckCall(context, async, owner.GetMethod("Round", [type, typeof(int)])!, property, [2], true);
                    foreach (var rule in Enum.GetValues<MidpointRounding>())
                    {
                        stage(owner.Name + "/Round/" + rule);
                        await CheckCall(context, async, owner.GetMethod("Round", [type, typeof(MidpointRounding)])!, property, [rule], true);
                        foreach (var digits in new[] { 0, 2, maximum })
                            await CheckCall(context, async, owner.GetMethod("Round", [type, typeof(int), typeof(MidpointRounding)])!,
                                property, [digits, rule], true);
                    }
                }
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public Task Round_midpoint_neighbors_precision_cutoffs_and_finite_extremes_match_CLR(
            OceanBaseCompatibilityMode mode, bool async, bool nulls)
            => WithTable(mode, async, nulls, async (context, stage) =>
            {
                await RoundBoundaries<float>(context, async, nameof(Row.Single), typeof(MathF), 6,
                    [MathF.BitDecrement(2.5f), 2.5f, MathF.BitIncrement(2.5f), MathF.BitDecrement(1.005f), 1.005f,
                        MathF.BitIncrement(1.005f), MathF.BitDecrement(8388608f), 8388608f, MathF.BitIncrement(8388608f),
                        MathF.BitDecrement(1e8f), 1e8f, MathF.BitIncrement(1e8f), float.Epsilon, float.MaxValue], stage);
                await RoundBoundaries<double>(context, async, nameof(Row.Double), typeof(Math), 15,
                    [Math.BitDecrement(2.5d), 2.5d, Math.BitIncrement(2.5d), Math.BitDecrement(1.005d), 1.005d,
                        Math.BitIncrement(1.005d), Math.BitDecrement(4503599627370496d), 4503599627370496d,
                        Math.BitIncrement(4503599627370496d), Math.BitDecrement(1e16d), 1e16d, Math.BitIncrement(1e16d),
                        double.Epsilon, double.MaxValue], stage);
                await RoundBoundaries<decimal>(context, async, nameof(Row.Decimal), typeof(decimal), 28,
                    [2.4999999999999999999999999999m, 2.5m, 2.5000000000000000000000000001m,
                        1.0049999999999999999999999999m, 1.005m, 1.0050000000000000000000000001m,
                        0.0000000000000000000000000001m, decimal.MaxValue], stage);
            });

        private static async Task RoundBoundaries<T>(Context context, bool async, string property, Type owner,
            int maximumDigits, T[] positiveValues, Action<string> stage) where T : struct, INumber<T>
        {
            var helper = context.GetService<ISqlGenerationHelper>();
            var mapping = context.Model.FindEntityType(typeof(Row))!.FindProperty(property)!.GetRelationalTypeMapping();
            var method = owner.GetMethod("Round", [typeof(T), typeof(int), typeof(MidpointRounding)])!;
            foreach (var positive in positiveValues)
            foreach (var sign in new[] { 1, -1 })
            {
                var input = sign == 1 ? positive : -positive;
                stage($"{owner.Name}.Round boundary={input}");
                var sql = "UPDATE " + helper.DelimitIdentifier(context.Table) + " SET " + helper.DelimitIdentifier(property)
                    + " = " + mapping.GenerateSqlLiteral(input) + " WHERE " + helper.DelimitIdentifier(nameof(Row.Id)) + " = 1";
                Assert.Equal(1, async ? await context.Database.ExecuteSqlRawAsync(sql) : context.Database.ExecuteSqlRaw(sql));
                foreach (var rule in Enum.GetValues<MidpointRounding>())
                {
                    var digits = new[] { 0, 2, maximumDigits };
                    var row = Expression.Parameter(typeof(Row), "row");
                    var field = Expression.Property(Expression.Property(row, property), "Value");
                    var calls = digits.Select(count => Expression.Convert(Expression.Call(method, field,
                        Expression.Constant(count), Expression.Constant(rule)), typeof(T?)));
                    var query = context.Set<Row>().Where(value => value.Id == 1)
                        .Select(Expression.Lambda<Func<Row, T?[]>>(Expression.NewArrayInit(typeof(T?), calls), row));
                    Assert.Contains(context.Mode == OceanBaseCompatibilityMode.MySql ? "TRUNCATE(" : "TRUNC(", query.ToQueryString());
                    var actual = Assert.Single(await Read(query, async));
                    for (var index = 0; index < digits.Length; index++)
                    {
                        var expected = (T)method.Invoke(null, [input, digits[index], rule])!;
                        Assert.True(actual[index].HasValue && expected.Equals(actual[index]!.Value),
                            $"{owner.Name}.Round({input}, {digits[index]}, {rule}): expected={expected},actual={actual[index]}");
                    }
                }
            }
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public Task Captured_round_options_refresh_and_invalid_options_reject_before_execution(
            OceanBaseCompatibilityMode mode, bool async, bool nulls)
            => WithTable(mode, async, nulls, async (context, stage) =>
            {
                var digits = 0;
                var rule = MidpointRounding.ToEven;
                var rows = context.Set<Row>();
                var query = rows.OrderBy(row => row.Id).Select(row => new
                {
                    D = (decimal?)Math.Round(row.Decimal!.Value, digits, rule),
                    F = (float?)MathF.Round(row.Single!.Value, digits, rule),
                    B = (double?)Math.Round(row.Double!.Value, digits, rule)
                });
                foreach (var next in new[] { (0, MidpointRounding.ToEven), (2, MidpointRounding.AwayFromZero),
                    (1, MidpointRounding.ToNegativeInfinity), (6, MidpointRounding.ToPositiveInfinity), (0, MidpointRounding.ToZero) })
                {
                    (digits, rule) = next;
                    stage($"captured round digits={digits},rule={rule}");
                    Assert.Contains("CASE", query.ToQueryString());
                    Assert.Equal(Seed().Select(row => new
                    {
                        D = row.Decimal.HasValue ? (decimal?)Math.Round(row.Decimal.Value, digits, rule) : null,
                        F = row.Single.HasValue ? (float?)MathF.Round(row.Single.Value, digits, rule) : null,
                        B = row.Double.HasValue ? (double?)Math.Round(row.Double.Value, digits, rule) : null
                    }), await Read(query, async));
                }
                stage("invalid current Round arguments");
                digits = -1;
                Assert.Throws<InvalidOperationException>(() => query.ToQueryString());
                digits = 7;
                Assert.Throws<InvalidOperationException>(() => query.ToQueryString());
                digits = 0;
                rule = (MidpointRounding)99;
                Assert.Throws<InvalidOperationException>(() => query.ToQueryString());
                rule = MidpointRounding.ToEven;
                Assert.Equal(Seed().Length, (await Read(query, async)).Length);
                IQueryable[] rejected =
                [
                    rows.Where(row => Math.Round(row.Double!.Value, row.Id) > 0),
                    rows.Where(row => Math.Round(row.Decimal!.Value, 29) > 0),
                    rows.Where(row => MathF.Round(row.Single!.Value, 7) > 0),
                    rows.Where(row => Math.IEEERemainder(row.Double!.Value, 2) > 0)
                ];
                foreach (var candidate in rejected)
                    Assert.Contains("could not be translated", Assert.Throws<InvalidOperationException>(() => candidate.ToQueryString()).Message);
                Assert.Equal(Seed().Length, async ? await rows.CountAsync() : rows.Count());
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public Task Extrema_clamp_and_copy_sign_keep_finite_native_results(
            OceanBaseCompatibilityMode mode, bool async, bool nulls)
            => WithTable(mode, async, nulls, async (context, stage) =>
            {
                stage("min/max/minnumber/maxnumber/clamp/copysign");
                foreach (var (type, property) in new[] { (typeof(int), nameof(Row.Int32)), (typeof(long), nameof(Row.Int64)),
                    (typeof(float), nameof(Row.Single)), (typeof(double), nameof(Row.Double)), (typeof(decimal), nameof(Row.Decimal)),
                    (typeof(decimal), nameof(Row.Faceted)) })
                {
                    foreach (var name in new[] { "Min", "Max" })
                    {
                        stage(property + "/" + name);
                        var method = typeof(Math).GetMethod(name, [type, type])!;
                        await CheckCall(context, async, method, property, [Convert.ChangeType(1, type)], true);
                    }
                    stage(property + "/Clamp");
                    await CheckCall(context, async, typeof(Math).GetMethod("Clamp", [type, type, type])!, property,
                        [Convert.ChangeType(-1, type), Convert.ChangeType(1, type)], true);
                    if (type == typeof(float) || type == typeof(double))
                    {
                        foreach (var name in new[] { "MinNumber", "MaxNumber" })
                            await CheckCall(context, async, type.GetMethod(name, [type, type])!, property, [Convert.ChangeType(1, type)], true);
                        await CheckCall(context, async, (type == typeof(float) ? typeof(MathF) : typeof(Math)).GetMethod("CopySign", [type, type])!,
                            property, [Convert.ChangeType(-1, type)], true);
                    }
                }
                stage("EF Least/Greatest scalar result and NULL propagation");
                var extrema = context.Set<Row>().OrderBy(row => row.Id).Select(row => new
                {
                    I = EF.Functions.Least(row.Int32, 1), L = EF.Functions.Greatest(row.Int64, 1L),
                    F = EF.Functions.Least(row.Single, 1f), B = EF.Functions.Greatest(row.Double, 1d),
                    D = EF.Functions.Least(row.Decimal, 1m), DMax = EF.Functions.Greatest(row.Decimal, 1m)
                });
                Assert.Contains("LEAST(", extrema.ToQueryString());
                Assert.Contains("GREATEST(", extrema.ToQueryString());
                Assert.Equal(Seed().Select(row => new
                {
                    I = row.Int32.HasValue ? (int?)Math.Min(row.Int32.Value, 1) : null,
                    L = row.Int64.HasValue ? (long?)Math.Max(row.Int64.Value, 1L) : null,
                    F = row.Single.HasValue ? (float?)Math.Min(row.Single.Value, 1f) : null,
                    B = row.Double.HasValue ? (double?)Math.Max(row.Double.Value, 1d) : null,
                    D = row.Decimal.HasValue ? (decimal?)Math.Min(row.Decimal.Value, 1m) : null,
                    DMax = row.Decimal.HasValue ? (decimal?)Math.Max(row.Decimal.Value, 1m) : null
                }), await Read(extrema, async));
                if (mode == OceanBaseCompatibilityMode.MySql)
                {
                    stage("direct default Decimal extrema metadata and exact readers");
                    if (async) await context.Database.OpenConnectionAsync();
                    else context.Database.OpenConnection();
                    try
                    {
                        var table = context.GetService<ISqlGenerationHelper>().DelimitIdentifier(context.Table);
                        foreach (var function in new[] { "LEAST", "GREATEST" })
                        {
                            await using var command = context.Database.GetDbConnection().CreateCommand();
                            command.CommandText = "SELECT " + function + "(`Decimal`, 1) FROM " + table + " WHERE `Decimal` IS NOT NULL";
                            await using var reader = async ? await command.ExecuteReaderAsync() : command.ExecuteReader();
                            while (async ? await reader.ReadAsync() : reader.Read())
                            {
                                var exact = reader.GetFieldValue<OceanBaseDecimal>(0);
                                Assert.Equal(exact, Assert.IsType<OceanBaseDecimal>(reader.GetValue(0)));
                                Assert.Equal(exact, Assert.IsType<OceanBaseDecimal>(reader.GetProviderSpecificValue(0)));
                                Assert.Equal(exact.ToDecimal(), reader.GetDecimal(0));
                            }
                            while (async ? await reader.NextResultAsync() : reader.NextResult())
                            {
                                while (async ? await reader.ReadAsync() : reader.Read()) { }
                            }
                        }
                    }
                    finally
                    {
                        if (async) await context.Database.CloseConnectionAsync();
                        else context.Database.CloseConnection();
                    }
                }
                var query = context.Set<Row>().OrderBy(row => row.Id).Select(row => new
                {
                    Low = EF.Functions.Least(row.Int32, (int?)1, (int?)-1),
                    High = EF.Functions.Greatest(row.Int32, (int?)1, (int?)-1)
                });
                Assert.Equal(Seed().Select(row => new
                {
                    Low = row.Int32.HasValue ? (int?)Math.Min(row.Int32.Value, -1) : null,
                    High = row.Int32.HasValue ? (int?)Math.Max(row.Int32.Value, 1) : null
                }), await Read(query, async));
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public Task Conditional_coalesce_and_converters_preserve_store_space_and_inverse_once(
            OceanBaseCompatibilityMode mode, bool async, bool nulls)
            => WithTable(mode, async, nulls, async (context, stage) =>
            {
                stage("conditional/coalesce/nullable negation");
                var rows = context.Set<Row>();
                var query = rows.OrderBy(row => row.Id).Select(row => new
                {
                    A = (decimal?)Math.Abs(row.Decimal!.Value) ?? -99m,
                    Sign = (int?)Math.Sign(row.Decimal!.Value),
                    Branch = row.Id % 2 == 0 ? (double?)Math.Floor(row.Double!.Value) : Math.Ceiling(row.Double!.Value),
                    IsNull = ((decimal?)Math.Truncate(row.Decimal!.Value)).HasValue == false
                });
                Assert.Equal(Seed().Select(row => new
                {
                    A = row.Decimal.HasValue ? Math.Abs(row.Decimal.Value) : -99m,
                    Sign = row.Decimal.HasValue ? (int?)Math.Sign(row.Decimal.Value) : null,
                    Branch = row.Double.HasValue ? (double?)(row.Id % 2 == 0 ? Math.Floor(row.Double.Value) : Math.Ceiling(row.Double.Value)) : null,
                    IsNull = !row.Decimal.HasValue
                }), await Read(query, async));
                var filtered = rows.Where(row => !((int?)Math.Sign(row.Decimal!.Value) == 1)).OrderBy(row => row.Id).Select(row => row.Id);
                Assert.Equal(Seed().Where(row => row.Decimal.HasValue ? Math.Sign(row.Decimal.Value) != 1 : !nulls).Select(row => row.Id), await Read(filtered, async));
                stage("converter stored numeric space");
                var converted = rows.OrderBy(row => row.Id).Select(row => new
                {
                    Abs = (decimal?)Math.Abs(row.Converted!.Value), Floor = (decimal?)Math.Floor(row.Converted!.Value),
                    Sign = (int?)Math.Sign(row.Converted!.Value), Round = (decimal?)Math.Round(row.Converted!.Value, 2)
                });
                Assert.Equal(Seed().Select(row => new
                {
                    Abs = row.Converted.HasValue ? (decimal?)(Math.Abs(row.Converted.Value * -2m) / -2m) : null,
                    Floor = row.Converted.HasValue ? (decimal?)(Math.Floor(row.Converted.Value * -2m) / -2m) : null,
                    Sign = row.Converted.HasValue ? (int?)Math.Sign(row.Converted.Value * -2m) : null,
                    Round = row.Converted.HasValue ? (decimal?)(Math.Round(row.Converted.Value * -2m, 2) / -2m) : null
                }), await Read(converted, async));
                stage("nullable floating converter Round and captured options");
                var digits = 2;
                var floating = rows.Where(row => row.ConvertedSingle == null
                    || MathF.Round(row.ConvertedSingle!.Value, digits) == MathF.Round(row.ConvertedSingle.Value, digits))
                    .OrderBy(row => row.Id).Select(row => new
                    {
                        F = (float?)MathF.Round(row.ConvertedSingle!.Value, digits),
                        B = (double?)Math.Round(row.ConvertedDouble!.Value, digits),
                        ConstantF = (float?)MathF.Round(row.ConvertedSingle!.Value, 2),
                        ConstantB = (double?)Math.Round(row.ConvertedDouble!.Value, 2)
                    });
                foreach (var current in new[] { 2, 1 })
                {
                    digits = current;
                    Assert.Equal(Seed().Select(row => new
                    {
                        F = row.ConvertedSingle.HasValue ? (float?)(MathF.Round(row.ConvertedSingle.Value * -2f, digits) / -2f) : null,
                        B = row.ConvertedDouble.HasValue ? (double?)(Math.Round(row.ConvertedDouble.Value * -2d, digits) / -2d) : null,
                        ConstantF = row.ConvertedSingle.HasValue ? (float?)(MathF.Round(row.ConvertedSingle.Value * -2f, 2) / -2f) : null,
                        ConstantB = row.ConvertedDouble.HasValue ? (double?)(Math.Round(row.ConvertedDouble.Value * -2d, 2) / -2d) : null
                    }), await Read(floating, async));
                }
                stage("emulated hyperbolic converter result mapping");
                var hyperbolic = rows.OrderBy(row => row.Id).Select(row => new
                {
                    F = (float?)MathF.Asinh(row.ConvertedSingle!.Value),
                    B = (double?)Math.Asinh(row.ConvertedDouble!.Value)
                });
                Assert.Contains("LN(", hyperbolic.ToQueryString());
                var actual = await Read(hyperbolic, async);
                var seed = Seed();
                Assert.Equal(seed.Length, actual.Length);
                for (var index = 0; index < seed.Length; index++)
                {
                    if (seed[index].ConvertedSingle is not { } value)
                    {
                        Assert.Null(actual[index].F);
                        Assert.Null(actual[index].B);
                        continue;
                    }
                    Assert.NotNull(actual[index].F);
                    Assert.NotNull(actual[index].B);
                    Assert.InRange(Math.Abs(actual[index].F!.Value - MathF.Asinh(value * -2f) / -2f), 0, 3e-6);
                    Assert.InRange(Math.Abs(actual[index].B!.Value - Math.Asinh(seed[index].ConvertedDouble!.Value * -2d) / -2d), 0, 5e-12);
                }
                stage("integer converter Asinh promotes before ABS and multiplication");
                var helper = context.GetService<ISqlGenerationHelper>();
                var integerMapping = context.Model.FindEntityType(typeof(Row))!.FindProperty(nameof(Row.IntegerCarrier))!.GetRelationalTypeMapping();
                foreach (var value in new[] { 5e9d, -5e9d, (double)long.MinValue })
                {
                    var update = "UPDATE " + helper.DelimitIdentifier(context.Table) + " SET " + helper.DelimitIdentifier(nameof(Row.IntegerCarrier))
                        + " = " + integerMapping.GenerateSqlLiteral(value) + " WHERE " + helper.DelimitIdentifier(nameof(Row.Id)) + " = 1";
                    Assert.Equal(1, async ? await context.Database.ExecuteSqlRawAsync(update) : context.Database.ExecuteSqlRaw(update));
                    var predicate = rows.Where(row => row.Id == 1 && (value > 0
                        ? Math.Asinh(row.IntegerCarrier!.Value) > 0 : Math.Asinh(row.IntegerCarrier!.Value) < 0)).Select(row => row.Id);
                    Assert.Contains("LN(", predicate.ToQueryString());
                    Assert.Equal(1, Assert.Single(await Read(predicate, async)));
                }
            });

        private static Task CheckCall(Context context, bool async, MethodInfo method, string property, object[] arguments, bool exact)
            => (Task)typeof(LiveNumericQueryTests).GetMethod(nameof(CheckResult), BindingFlags.Static | BindingFlags.NonPublic)!
                .MakeGenericMethod(method.ReturnType).Invoke(null, [context, async, method, property, arguments, exact])!;

        private static async Task CheckResult<T>(Context context, bool async, MethodInfo method, string property, object[] arguments, bool exact) where T : struct
        {
            var row = Expression.Parameter(typeof(Row), "row");
            var field = Expression.Property(row, property);
            var call = Expression.Call(method, new Expression[] { Expression.Property(field, "Value") }.Concat(arguments.Select(value => Expression.Constant(value))));
            var projection = Expression.Lambda<Func<Row, T?>>(Expression.Convert(call, typeof(T?)), row);
            // Force server translation even when EF could evaluate a final projection on the client.
            var predicate = Expression.Lambda<Func<Row, bool>>(Expression.OrElse(Expression.Equal(field, Expression.Constant(null, field.Type)), Expression.Equal(call, call)), row);
            var query = context.Set<Row>().Where(predicate).OrderBy(value => value.Id).Select(projection);
            var actual = await Read(query, async);
            var seed = Seed();
            Assert.Equal(seed.Length, actual.Length);
            var source = typeof(Row).GetProperty(property)!;
            for (var index = 0; index < seed.Length; index++)
            {
                var input = source.GetValue(seed[index]);
                var expected = input is null ? (T?)null : (T)method.Invoke(null, new[] { input }.Concat(arguments).ToArray())!;
                if (exact || expected is null || actual[index] is null)
                    Assert.True(Nullable.Equals(expected, actual[index]),
                        $"{property}/{method.DeclaringType!.Name}.{method.Name}({input}): expected={expected},actual={actual[index]}");
                else
                {
                    var wanted = Convert.ToDouble(expected.Value);
                    var observed = Convert.ToDouble(actual[index]!.Value);
                    var tolerance = (typeof(T) == typeof(float) ? 3e-6 : 5e-12) * Math.Max(1d, Math.Abs(wanted));
                    Assert.True(double.IsFinite(observed) && Math.Abs(observed - wanted) <= tolerance,
                        $"{method.DeclaringType!.Name}.{method.Name}({input}): expected={wanted:R},actual={observed:R},tolerance={tolerance:R}");
                }
            }
        }

        private static Task<T[]> Read<T>(IQueryable<T> query, bool async) => async ? query.ToArrayAsync() : Task.FromResult(query.ToArray());

        private static Task WithTable(OceanBaseCompatibilityMode mode, bool async, bool nulls, Func<Context, Action<string>, Task> run)
            => LiveStorageTable.WithTable<Context>(mode, async, false, (options, table) =>
            {
                var builder = new DbContextOptionsBuilder<Context>(options).UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
                    .EnableServiceProviderCaching(false)
                    .ReplaceService<IModelCacheKeyFactory, CacheKey>();
                ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(
                    builder.Options.Extensions.OfType<RelationalOptionsExtension>().Single().WithUseRelationalNulls(nulls));
                return new Context(builder.Options, mode, table);
            }, async (context, _, stage) =>
            {
                // Reuse one physical session for the many numeric expressions with pooling disabled.
                if (async) await context.Database.OpenConnectionAsync();
                else context.Database.OpenConnection();
                try
                {
                    stage("verify numeric seeds");
                    Assert.Equal(Seed(), await Read(context.Set<Row>().OrderBy(row => row.Id), async));
                    await run(context, stage);
                }
                finally
                {
                    if (async) await context.Database.CloseConnectionAsync();
                    else context.Database.CloseConnection();
                }
            });

        private static Row[] Seed()
        {
            decimal?[] values = [null, -2.5m, -1.25m, 0m, 1.25m, 2.5m, 2.675m, -2.675m];
            return values.Select((value, index) => new Row
            {
                Id = index + 1, SByte = (sbyte?)value, Int16 = (short?)value, Int32 = (int?)value, Int64 = (long?)value,
                Single = (float?)value, Double = (double?)value, Decimal = value, Faceted = value, Converted = value,
                ConvertedSingle = (float?)value, ConvertedDouble = (double?)value,
                IntegerCarrier = (double?)(long?)value,
                Unit = value.HasValue ? (double?)(value.Value / 4) : null, UnitSingle = value.HasValue ? (float?)(value.Value / 4) : null,
                Positive = value.HasValue ? (double?)(Math.Abs(value.Value) + 0.25m) : null,
                PositiveSingle = value.HasValue ? (float?)(Math.Abs(value.Value) + 0.25m) : null
            }).ToArray();
        }

        private sealed class Context(DbContextOptions<Context> options, OceanBaseCompatibilityMode mode, string table) : DbContext(options)
        {
            public string Table => table;
            public OceanBaseCompatibilityMode Mode => mode;
            protected override void OnModelCreating(ModelBuilder model)
            {
                var row = model.Entity<Row>();
                row.ToTable(table);
                row.Property(value => value.Id).ValueGeneratedNever();
                row.Property(value => value.Faceted).HasPrecision(18, 6);
                row.Property(value => value.Converted).HasConversion(value => value * -2m, value => value / -2m).HasPrecision(18, 6);
                row.Property(value => value.ConvertedSingle).HasConversion(value => value * -2f, value => value / -2f);
                row.Property(value => value.ConvertedDouble).HasConversion(value => value * -2d, value => value / -2d);
                row.Property(value => value.IntegerCarrier).HasConversion<long>();
                row.HasData(Seed());
            }
        }

        private sealed class CacheKey : IModelCacheKeyFactory
        {
            public object Create(DbContext context, bool designTime) => (context.GetType(), ((Context)context).Mode, ((Context)context).Table, designTime);
        }

        private sealed record Row
        {
            public int Id { get; set; }
            public sbyte? SByte { get; set; }
            public short? Int16 { get; set; }
            public int? Int32 { get; set; }
            public long? Int64 { get; set; }
            public float? Single { get; set; }
            public double? Double { get; set; }
            public decimal? Decimal { get; set; }
            public decimal? Faceted { get; set; }
            public decimal? Converted { get; set; }
            public float? ConvertedSingle { get; set; }
            public double? ConvertedDouble { get; set; }
            public double? IntegerCarrier { get; set; }
            public double? Unit { get; set; }
            public float? UnitSingle { get; set; }
            public double? Positive { get; set; }
            public float? PositiveSingle { get; set; }
        }
    }
}
