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

internal sealed class OceanBaseRoundTranslator(
    ISqlExpressionFactory factory, IRelationalTypeMappingSource mappingSource, OceanBaseCompatibilityMode mode)
    : IMethodCallTranslator
{
    private static readonly Dictionary<MethodInfo, (int Digits, int Rule)> Signatures = CreateSignatures();

    private static Dictionary<MethodInfo, (int, int)> CreateSignatures()
    {
        var signatures = new Dictionary<MethodInfo, (int, int)>();
        foreach (var (owner, type) in new[] { (typeof(Math), typeof(decimal)), (typeof(decimal), typeof(decimal)),
            (typeof(Math), typeof(double)), (typeof(double), typeof(double)),
            (typeof(MathF), typeof(float)), (typeof(float), typeof(float)) })
        {
            signatures.Add(owner.GetMethod("Round", [type])!, (-1, -1));
            signatures.Add(owner.GetMethod("Round", [type, typeof(int)])!, (1, -1));
            signatures.Add(owner.GetMethod("Round", [type, typeof(MidpointRounding)])!, (-1, 1));
            signatures.Add(owner.GetMethod("Round", [type, typeof(int), typeof(MidpointRounding)])!, (1, 2));
        }
        return signatures;
    }

    public SqlExpression? Translate(SqlExpression? instance, MethodInfo method, IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (instance is not null || !Signatures.TryGetValue(method, out var signature)
            || arguments.Count != method.GetParameters().Length || arguments[0].Type != method.ReturnType)
            return null;

        var type = method.ReturnType;
        var maxDigits = type == typeof(float) ? 6 : type == typeof(double) ? 15 : 28;
        var digits = 0;
        if (signature.Digits >= 0)
        {
            if (arguments[signature.Digits] is not SqlConstantExpression { Value: int count } || count < 0 || count > maxDigits)
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
        if (type != typeof(decimal))
            return TranslateBinary(input, type, digits, rule);

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

    private SqlExpression? TranslateBinary(SqlExpression input, Type type, int digits, MidpointRounding rule)
    {
        var single = type == typeof(float);
        var mapping = input.TypeMapping;
        if ((single ? mapping is not OceanBaseFloatingPointTypeMapping<float>
                : mapping is not OceanBaseFloatingPointTypeMapping<double>)
            || mapping!.ClrType != type || (mapping.Converter?.ProviderClrType ?? type) != type
            || !OceanBaseMathTranslator.IsValidNumeric(mapping, mappingSource))
            return null;

        var syntax = mappingSource.FindMapping(type)!;
        var zero = Literal(0);
        var one = Literal(1);
        // Promote physically narrower Double operands without using their declaration as a cast target.
        var value = single ? Single(input, syntax) : factory.Multiply(input, one, mapping);
        var scale = Literal(Math.Pow(10, digits));
        var scaled = digits == 0 ? value : Normalize(factory.Multiply(value, scale, syntax));
        SqlExpression integral;
        if (rule == MidpointRounding.AwayFromZero)
        {
            // CLR truncates after adding the signed predecessor of one half.
            var bias = single ? MathF.BitDecrement(0.5f) : Math.BitDecrement(0.5d);
            var signedBias = Choose(factory.LessThan(scaled, zero), Literal(-bias), Literal(bias));
            integral = Truncate(Normalize(factory.Add(scaled, signedBias, syntax)));
        }
        else
        {
            var truncated = Truncate(scaled);
            integral = rule switch
            {
                MidpointRounding.ToZero => truncated,
                MidpointRounding.ToPositiveInfinity => Choose(factory.GreaterThan(scaled, truncated),
                    factory.Add(truncated, one, syntax), truncated),
                MidpointRounding.ToNegativeInfinity => Choose(factory.LessThan(scaled, truncated),
                    factory.Subtract(truncated, one, syntax), truncated),
                _ => ToEven(truncated)
            };
        }

        // The integral guard also applies inside a scaled calculation, before dividing back.
        var rounded = Choose(factory.GreaterThanOrEqual(Abs(scaled), Literal(single ? 8388608d : 4503599627370496d)),
            scaled, integral);
        var completed = digits == 0 ? rounded : Choose(
            factory.LessThan(Abs(value), Literal(single ? 1e8 : 1e16)),
            factory.Divide(rounded, scale, mapping), value);
        return single ? Single(completed, mapping) : completed;

        SqlExpression Literal(double number) => factory.Constant(single ? (object)(float)number : number, syntax);
        SqlExpression Abs(SqlExpression operand) => Unary("ABS", operand, syntax);
        SqlExpression Single(SqlExpression operand, RelationalTypeMapping result)
            => Unary(OceanBaseMathTranslator.SingleResultMarker, operand, result);
        SqlExpression Normalize(SqlExpression operand) => single ? Single(operand, syntax) : operand;
        SqlExpression Unary(string name, SqlExpression operand, RelationalTypeMapping result)
            => factory.Function(name, [operand], nullable: true, argumentsPropagateNullability: [false], type, result);
        SqlExpression Truncate(SqlExpression operand)
            => mode == OceanBaseCompatibilityMode.MySql
                ? factory.Function("TRUNCATE", [operand, factory.Constant(0, mappingSource.FindMapping(typeof(int)))],
                    nullable: true, argumentsPropagateNullability: [false, false], type, syntax)
                : Unary("TRUNC", operand, syntax);
        SqlExpression Choose(SqlExpression condition, SqlExpression yes, SqlExpression no)
            => factory.Case([new CaseWhenClause(condition, yes)], no);

        SqlExpression ToEven(SqlExpression truncated)
        {
            var residue = Abs(factory.Subtract(scaled, truncated, syntax));
            var half = Literal(0.5d);
            var parity = new SqlBinaryExpression(ExpressionType.Modulo, truncated, Literal(2), type, syntax);
            var adjust = factory.OrElse(factory.GreaterThan(residue, half),
                factory.AndAlso(factory.Equal(residue, half), factory.NotEqual(parity, zero)));
            return Choose(adjust, Choose(factory.LessThan(scaled, zero),
                factory.Subtract(truncated, one, syntax), factory.Add(truncated, one, syntax)), truncated);
        }
    }
}
