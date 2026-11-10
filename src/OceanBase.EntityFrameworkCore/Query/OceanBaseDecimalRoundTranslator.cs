using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBase.EntityFrameworkCore.Storage;

namespace OceanBase.EntityFrameworkCore.Query;

internal sealed class OceanBaseDecimalRoundTranslator(
    ISqlExpressionFactory factory, IRelationalTypeMappingSource mappingSource, OceanBaseCompatibilityMode mode)
    : IMethodCallTranslator
{
    private static readonly Dictionary<MethodInfo, (int Digits, int Rule)> Signatures = CreateSignatures();

    private static Dictionary<MethodInfo, (int, int)> CreateSignatures()
    {
        var signatures = new Dictionary<MethodInfo, (int, int)>();
        foreach (var owner in new[] { typeof(Math), typeof(decimal) })
        {
            signatures.Add(owner.GetMethod("Round", [typeof(decimal)])!, (-1, -1));
            signatures.Add(owner.GetMethod("Round", [typeof(decimal), typeof(int)])!, (1, -1));
            signatures.Add(owner.GetMethod("Round", [typeof(decimal), typeof(MidpointRounding)])!, (-1, 1));
            signatures.Add(owner.GetMethod("Round", [typeof(decimal), typeof(int), typeof(MidpointRounding)])!, (1, 2));
        }
        return signatures;
    }

    public SqlExpression? Translate(SqlExpression? instance, MethodInfo method, IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (instance is not null || !Signatures.TryGetValue(method, out var signature)
            || arguments.Count != method.GetParameters().Length || arguments[0].Type != typeof(decimal))
            return null;

        var digits = 0;
        if (signature.Digits >= 0)
        {
            if (arguments[signature.Digits] is not SqlConstantExpression { Value: int count } || count is < 0 or > 28)
                return null;
            digits = count;
        }

        var rule = MidpointRounding.ToEven;
        if (signature.Rule >= 0)
        {
            if (arguments[signature.Rule] is not SqlConstantExpression { Value: MidpointRounding midpoint }
                || !Enum.IsDefined(midpoint))
                return null;
            rule = midpoint;
        }

        var input = factory.ApplyDefaultTypeMapping(arguments[0]);
        if (input.TypeMapping is not OceanBaseDecimalTypeMapping mapping || mapping.ClrType != typeof(decimal)
            || mapping.Converter is { ProviderClrType: var carrier } && carrier != typeof(decimal)
            || !OceanBaseMathTranslator.IsValidNumeric(mapping, mappingSource))
            return null;

        var result = mapping.AsQueryResult();
        var syntax = mappingSource.FindMapping(typeof(decimal))!;
        var truncated = factory.Function(mode == OceanBaseCompatibilityMode.MySql ? "TRUNCATE" : "TRUNC",
            [input, factory.Constant(digits, mappingSource.FindMapping(typeof(int)))], nullable: true,
            argumentsPropagateNullability: [false, false], typeof(decimal), result);
        if (rule == MidpointRounding.ToZero)
            return truncated;

        var quantum = new decimal(1, 0, 0, false, (byte)digits);
        var q = Literal(quantum);
        var zero = Literal(0m);
        var positive = factory.GreaterThan(input, zero);
        var up = factory.Add(truncated, q, result);
        var down = factory.Subtract(truncated, q, result);
        if (rule is MidpointRounding.ToPositiveInfinity or MidpointRounding.ToNegativeInfinity)
        {
            var ceiling = rule == MidpointRounding.ToPositiveInfinity;
            var hasRemainder = factory.NotEqual(Remainder(input, q), zero);
            return Choose(factory.AndAlso(ceiling ? positive : factory.LessThan(input, zero), hasRemainder),
                ceiling ? up : down, truncated);
        }

        // Doubling the residue avoids constructing a 29-scale half quantum in CLR decimal.
        var absoluteRemainder = factory.Function("ABS", [Remainder(input, q)], nullable: true,
            argumentsPropagateNullability: [false], typeof(decimal), syntax);
        var twiceRemainder = factory.Multiply(Literal(2m), absoluteRemainder, syntax);
        var adjust = rule == MidpointRounding.AwayFromZero
            ? factory.GreaterThanOrEqual(twiceRemainder, q)
            : factory.OrElse(factory.GreaterThan(twiceRemainder, q),
                factory.AndAlso(factory.Equal(twiceRemainder, q),
                    factory.NotEqual(Remainder(truncated, Literal(2m * quantum)), zero)));
        return Choose(adjust, Choose(positive, up, down), truncated);

        SqlExpression Literal(decimal value) => factory.Constant(value, syntax);
        SqlExpression Remainder(SqlExpression value, SqlExpression divisor)
            => new SqlBinaryExpression(ExpressionType.Modulo, value, divisor, typeof(decimal), syntax);
        SqlExpression Choose(SqlExpression condition, SqlExpression yes, SqlExpression no)
            => factory.Case([new CaseWhenClause(condition, yes)], no);
    }
}
