using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBaseConnector;

namespace OceanBase.EntityFrameworkCore.Query
{
    internal sealed partial class OceanBaseQuerySqlGenerator : QuerySqlGenerator
    {
        private readonly OceanBaseCompatibilityMode _mode;

        public OceanBaseQuerySqlGenerator(QuerySqlGeneratorDependencies dependencies, OceanBaseCompatibilityMode mode)
            : base(dependencies)
        {
            _mode = mode;
        }

        protected override string AliasSeparator => _mode == OceanBaseCompatibilityMode.Oracle ? " " : " AS ";

        protected override void GenerateValues(ValuesExpression valuesExpression)
        {
            var rows = valuesExpression.RowValues
                ?? throw new InvalidOperationException("Collection parameters must be expanded before SQL generation.");
            var empty = rows.Count == 0;
            var prototypes = empty ? valuesExpression.FindAnnotation(OceanBaseCollectionParameterExpandingVisitor.EmptyColumnsAnnotation)
                ?.Value as IReadOnlyList<SqlExpression> : null;
            if (empty && (prototypes is null || prototypes.Count != valuesExpression.ColumnNames.Count))
                throw new NotSupportedException("An empty collection root requires retained element mapping metadata.");
            for (var row = 0; row < (empty ? 1 : rows.Count); row++)
            {
                if (row > 0) Sql.AppendLine().Append("UNION ALL ");
                Sql.Append("SELECT ");
                var columns = empty ? prototypes! : rows[row].Values;
                for (var column = 0; column < columns.Count; column++)
                {
                    if (column > 0) Sql.Append(", ");
                    var expression = columns[column];
                    if (empty) GenerateCollectionCast(expression);
                    else if (row == 0 && expression is SqlUnaryExpression { OperatorType: ExpressionType.Convert } conversion
                        && conversion.Type == conversion.Operand.Type
                        && ReferenceEquals(conversion.TypeMapping, conversion.Operand.TypeMapping))
                        GenerateCollectionCast(conversion.Operand);
                    else Visit(expression);
                    if (row == 0)
                        Sql.Append(AliasSeparator).Append(Dependencies.SqlGenerationHelper.DelimitIdentifier(valuesExpression.ColumnNames[column]));
                }
                GeneratePseudoFromClause();
                if (empty) Sql.Append(" WHERE 1 = 0");
            }
        }

        private void GenerateCollectionCast(SqlExpression expression)
        {
            if (expression.TypeMapping is not { } mapping
                || !OceanBaseCollectionParameterExpandingVisitor.IsSupportedCollectionElement(mapping))
                throw new NotSupportedException("Collection typing casts require supported numeric, Boolean, text, binary or temporal elements with non-null-converting storage carriers.");
            Sql.Append("CAST(");
            Visit(expression);
            if (OceanBaseCollectionParameterExpandingVisitor.IsSupportedBoolean(mapping))
            {
                var providerType = mapping.Converter?.ProviderClrType ?? mapping.ClrType;
                var providerCode = IntegerCode(providerType);
                var booleanTarget = _mode == OceanBaseCompatibilityMode.Oracle ? "NUMBER"
                    : providerCode is { } code ? UnsignedMaximum(code) is null ? "SIGNED" : "UNSIGNED"
                    : providerType == typeof(decimal) ? "DECIMAL(65,30)" : "SIGNED";
                Sql.Append(" AS ").Append(booleanTarget).Append(")");
                return;
            }
            if (OceanBaseCollectionParameterExpandingVisitor.IsSupportedText(mapping))
            {
                Sql.Append(" AS ").Append(_mode == OceanBaseCompatibilityMode.Oracle ? mapping.StoreType : "CHAR").Append(")");
                return;
            }
            if (OceanBaseCollectionParameterExpandingVisitor.IsSupportedBinary(mapping))
            {
                Sql.Append(" AS ").Append(_mode == OceanBaseCompatibilityMode.Oracle ? mapping.StoreType : "BINARY").Append(")");
                return;
            }
            if (OceanBaseCollectionParameterExpandingVisitor.IsSupportedTemporal(mapping))
            {
                Sql.Append(" AS ").Append(mapping.StoreType).Append(")");
                return;
            }

            // Use the lossless query carrier, not a property's narrower physical write declaration.
            var oracle = _mode == OceanBaseCompatibilityMode.Oracle;
            var target = mapping switch
            {
                Storage.OceanBaseDecimalTypeMapping => oracle ? "NUMBER" : "DECIMAL(65,30)",
                Storage.OceanBaseFloatingPointTypeMapping<float> => oracle ? "BINARY_FLOAT" : "FLOAT",
                Storage.OceanBaseFloatingPointTypeMapping<double> => oracle ? "BINARY_DOUBLE" : "DOUBLE",
                _ => oracle ? "NUMBER" : UnsignedMaximum(IntegerCode(mapping.Converter?.ProviderClrType ?? mapping.ClrType)!.Value) is null
                    ? "SIGNED" : "UNSIGNED"
            };
            Sql.Append(" AS ").Append(target).Append(")");
        }

        protected override Expression VisitSqlBinary(SqlBinaryExpression sqlBinaryExpression)
        {
            if (sqlBinaryExpression.OperatorType == ExpressionType.ExclusiveOr && sqlBinaryExpression.Type == typeof(bool))
            {
                Sql.Append("(");
                GenerateBooleanValue(sqlBinaryExpression.Left);
                Sql.Append(" <> ");
                GenerateBooleanValue(sqlBinaryExpression.Right);
                Sql.Append(")");
                return sqlBinaryExpression;
            }
            if (sqlBinaryExpression.OperatorType is ExpressionType.And or ExpressionType.Or or ExpressionType.ExclusiveOr
                && IntegerCode(sqlBinaryExpression.Type) is { } code)
            {
                ValidateInteger(sqlBinaryExpression);
                ValidateInteger(sqlBinaryExpression.Left);
                ValidateInteger(sqlBinaryExpression.Right);
                if (_mode == OceanBaseCompatibilityMode.MySql)
                {
                    var signed = UnsignedMaximum(code) is null;
                    Sql.Append(signed ? "CAST(" : "(");
                    BitwiseOperand(sqlBinaryExpression.Left);
                    Sql.Append(sqlBinaryExpression.OperatorType == ExpressionType.And ? " & "
                        : sqlBinaryExpression.OperatorType == ExpressionType.Or ? " | " : " ^ ");
                    BitwiseOperand(sqlBinaryExpression.Right);
                    Sql.Append(signed ? " AS SIGNED)" : ")");
                }
                else if (sqlBinaryExpression.OperatorType == ExpressionType.And)
                    OracleBitAnd(sqlBinaryExpression, code);
                else
                {
                    Sql.Append("(");
                    BitwiseOperand(sqlBinaryExpression.Left);
                    Sql.Append(" + ");
                    BitwiseOperand(sqlBinaryExpression.Right);
                    Sql.Append(sqlBinaryExpression.OperatorType == ExpressionType.Or ? " - " : " - 2 * ");
                    OracleBitAnd(sqlBinaryExpression, code);
                    Sql.Append(")");
                }
                return sqlBinaryExpression;
            }

            if (sqlBinaryExpression.OperatorType == ExpressionType.Add && sqlBinaryExpression.Type == typeof(string))
            {
                Sql.Append(_mode == OceanBaseCompatibilityMode.Oracle ? "(" : "CONCAT(");
                Visit(sqlBinaryExpression.Left);
                Sql.Append(_mode == OceanBaseCompatibilityMode.Oracle ? " || " : ", ");
                Visit(sqlBinaryExpression.Right);
                Sql.Append(")");
                return sqlBinaryExpression;
            }

            if (_mode == OceanBaseCompatibilityMode.Oracle)
            {
                if (IsIntegerDivision(sqlBinaryExpression))
                {
                    Sql.Append("TRUNC(");
                    base.VisitSqlBinary(sqlBinaryExpression);
                    Sql.Append(")");
                    return sqlBinaryExpression;
                }

                if (sqlBinaryExpression.OperatorType == ExpressionType.Modulo)
                {
                    Sql.Append("MOD(");
                    Visit(sqlBinaryExpression.Left);
                    Sql.Append(", ");
                    Visit(sqlBinaryExpression.Right);
                    Sql.Append(")");
                    return sqlBinaryExpression;
                }
            }

            return base.VisitSqlBinary(sqlBinaryExpression);
        }

        protected override string GetOperator(SqlBinaryExpression binaryExpression)
            => _mode == OceanBaseCompatibilityMode.MySql && IsIntegerDivision(binaryExpression)
                ? " DIV " : base.GetOperator(binaryExpression);

        protected override Expression VisitSqlUnary(SqlUnaryExpression expression)
        {
            // C# lowers enum complement to underlying conversions, promoting byte/short to Int32 first.
            if (expression is { OperatorType: ExpressionType.Convert, Operand: SqlUnaryExpression
                { OperatorType: ExpressionType.Not, Operand: SqlUnaryExpression
                    { OperatorType: ExpressionType.Convert } promotion } }
                && IntegerCode(expression.Type) is { } enumCode && IntegerCode(promotion.Operand.Type) == enumCode)
                return VisitSqlUnary(new SqlUnaryExpression(ExpressionType.Not, promotion.Operand,
                    expression.Type, expression.TypeMapping));

            if (expression.OperatorType is ExpressionType.Not or ExpressionType.OnesComplement
                && IntegerCode(expression.Type) is { } code)
            {
                ValidateInteger(expression);
                ValidateInteger(expression.Operand);
                var maximum = UnsignedMaximum(code);
                if (_mode == OceanBaseCompatibilityMode.Oracle)
                {
                    Sql.Append("(").Append(maximum ?? "-1").Append(" - ");
                    BitwiseOperand(expression.Operand);
                    Sql.Append(")");
                }
                else
                {
                    Sql.Append(maximum is null ? "CAST(~" : "(~");
                    BitwiseOperand(expression.Operand);
                    if (maximum is not null && code != TypeCode.UInt64)
                        Sql.Append(" & ").Append(maximum);
                    Sql.Append(maximum is null ? " AS SIGNED)" : ")");
                }
                return expression;
            }

            if (_mode == OceanBaseCompatibilityMode.MySql && expression.OperatorType == ExpressionType.Convert
                && IntegerCode(expression.Type) is { } target && IntegerCode(expression.Operand.Type) is not null
                && expression.TypeMapping is { } mapping && IntegerCode(mapping.Converter?.ProviderClrType ?? mapping.ClrType) is not null)
            {
                Sql.Append("CAST(");
                Visit(expression.Operand);
                Sql.Append(UnsignedMaximum(target) is null ? " AS SIGNED)" : " AS UNSIGNED)");
                return expression;
            }
            return base.VisitSqlUnary(expression);
        }

        private void GenerateBooleanValue(SqlExpression operand)
        {
            var mapping = operand.TypeMapping;
            var carrier = mapping?.Converter?.ProviderClrType ?? mapping?.ClrType;
            var code = Type.GetTypeCode(carrier is null ? null : Nullable.GetUnderlyingType(carrier) ?? carrier);
            if (operand.Type != typeof(bool) || mapping is null || mapping.Converter?.ConvertsNulls == true
                || code is not (TypeCode.Boolean or TypeCode.SByte or TypeCode.Byte or TypeCode.Int16 or TypeCode.UInt16
                    or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Single
                    or TypeCode.Double or TypeCode.Decimal or TypeCode.String))
                throw new NotSupportedException("Boolean operations require non-null-converting numeric or text Boolean storage.");
            var trueValue = mapping.Converter is { } converter ? converter.ConvertToProvider(true) : true;
            var falseValue = mapping.Converter is { } falseConverter ? falseConverter.ConvertToProvider(false) : false;
            if (trueValue is null || falseValue is null || Equals(trueValue, falseValue)
                || _mode == OceanBaseCompatibilityMode.Oracle && (trueValue is "" || falseValue is ""))
                throw new NotSupportedException("Boolean operations require distinct non-null true and false storage values.");
            Sql.Append("CASE WHEN ");
            BitwiseOperand(operand);
            Sql.Append(" = ").Append(mapping.GenerateSqlLiteral(true)).Append(" THEN 1 WHEN ");
            BitwiseOperand(operand);
            Sql.Append(" = ").Append(mapping.GenerateSqlLiteral(false)).Append(" THEN 0 ELSE NULL END");
        }

        private void BitwiseOperand(SqlExpression operand)
        {
            Sql.Append("(");
            Visit(operand);
            Sql.Append(")");
        }

        private void OracleBitAnd(SqlBinaryExpression expression, TypeCode code)
        {
            if (code == TypeCode.UInt64)
            {
                // OceanBase's NUMBER BITAND path converts to Int64; each half must stay below 2^32.
                Sql.Append("(BITAND(TRUNC(");
                BitwiseOperand(expression.Left);
                Sql.Append(" / 4294967296), TRUNC(");
                BitwiseOperand(expression.Right);
                Sql.Append(" / 4294967296)) * 4294967296 + BITAND(MOD(");
                Visit(expression.Left);
                Sql.Append(", 4294967296), MOD(");
                Visit(expression.Right);
                Sql.Append(", 4294967296)))");
            }
            else
            {
                Sql.Append("BITAND(");
                Visit(expression.Left);
                Sql.Append(", ");
                Visit(expression.Right);
                Sql.Append(")");
            }
        }

        private static void ValidateInteger(SqlExpression expression)
        {
            var mapping = expression.TypeMapping;
            if (mapping is null || mapping.Converter?.ConvertsNulls == true
                || IntegerCode(mapping.Converter?.ProviderClrType ?? mapping.ClrType) is null)
                throw new NotSupportedException("Integer bitwise operations require integer numeric storage carriers.");
            if (expression is SqlUnaryExpression { OperatorType: ExpressionType.Convert } conversion
                && IntegerCode(conversion.Operand.Type) is not null)
                ValidateInteger(conversion.Operand);
        }

        private static TypeCode? IntegerCode(Type type)
        {
            var code = Type.GetTypeCode(Nullable.GetUnderlyingType(type) ?? type);
            return code is TypeCode.SByte or TypeCode.Byte or TypeCode.Int16 or TypeCode.UInt16
                or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64 ? code : null;
        }

        private static string? UnsignedMaximum(TypeCode code)
            => code switch
            {
                TypeCode.Byte => "255", TypeCode.UInt16 => "65535", TypeCode.UInt32 => "4294967295",
                TypeCode.UInt64 => "18446744073709551615", _ => null
            };

        private static bool IsIntegerDivision(SqlBinaryExpression expression)
            => expression.OperatorType == ExpressionType.Divide
                && Type.GetTypeCode(Nullable.GetUnderlyingType(expression.Type) ?? expression.Type)
                    is TypeCode.SByte or TypeCode.Byte or TypeCode.Int16 or TypeCode.UInt16
                        or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64;

        protected override Expression VisitSqlFunction(SqlFunctionExpression sqlFunctionExpression)
        {
            if (sqlFunctionExpression.Name == OceanBaseSqlTranslatingExpressionVisitorFactory.BooleanValueMarker)
            {
                if (sqlFunctionExpression is not { IsBuiltIn: true, Instance: null, Schema: null, Arguments.Count: 1,
                        IsNullable: true, ArgumentsPropagateNullability: [true] }
                    || sqlFunctionExpression.Type != typeof(bool)
                    || !OceanBaseSqlTranslatingExpressionVisitorFactory.IsCanonicalBoolean(sqlFunctionExpression.TypeMapping))
                    throw new InvalidOperationException("Invalid internal Boolean value expression.");
                Sql.Append("(");
                GenerateBooleanValue(sqlFunctionExpression.Arguments[0]);
                Sql.Append(")");
                return sqlFunctionExpression;
            }
            if (sqlFunctionExpression.Name is OceanBaseSqlTranslatingExpressionVisitorFactory.LeftShiftMarker
                or OceanBaseSqlTranslatingExpressionVisitorFactory.RightShiftMarker)
                return GenerateShift(sqlFunctionExpression);

            if (sqlFunctionExpression.Name == OceanBaseMathTranslator.SingleResultMarker)
            {
                var mapping = sqlFunctionExpression.TypeMapping;
                var carrier = mapping?.Converter?.ProviderClrType ?? mapping?.ClrType;
                if (sqlFunctionExpression is not { IsBuiltIn: true, Instance: null, Schema: null, Arguments.Count: 1,
                        IsNullable: true, ArgumentsPropagateNullability: [false] }
                    || mapping is null || mapping.Converter?.ConvertsNulls == true
                    || sqlFunctionExpression.Type != mapping.ClrType
                    || !OceanBaseMathTranslator.IsNumeric(sqlFunctionExpression.Type)
                    || (Nullable.GetUnderlyingType(carrier!) ?? carrier) != typeof(float)
                    || sqlFunctionExpression.Arguments[0].Type != typeof(double)
                        && sqlFunctionExpression.Arguments[0].Type != sqlFunctionExpression.Type
                        // EF's SUM compensation derives COALESCE.Type from its Int32 zero constant.
                        && !IsCompensatedSingleSum(sqlFunctionExpression.Arguments[0], sqlFunctionExpression.Type, mapping))
                    throw new InvalidOperationException("Invalid internal Single result expression.");
                Sql.Append("CAST(");
                Visit(sqlFunctionExpression.Arguments[0]);
                Sql.Append(_mode == OceanBaseCompatibilityMode.MySql ? " AS FLOAT)" : " AS BINARY_FLOAT)");
                return sqlFunctionExpression;
            }

            if (sqlFunctionExpression.Name == OceanBaseTemporalMemberTranslator.ExtractMarker)
            {
                if (sqlFunctionExpression is not { IsBuiltIn: true, IsNullable: true, Arguments.Count: 2,
                        ArgumentsPropagateNullability: [false, false] }
                    || sqlFunctionExpression.Arguments[0] is not SqlFragmentExpression { Sql: var unit }
                    || unit is not ("YEAR" or "MONTH" or "DAY" or "HOUR" or "MINUTE" or "SECOND"
                        or "MILLISECOND" or "MICROSECOND" or "NANOSECOND"))
                    throw new InvalidOperationException("Invalid internal EXTRACT expression.");

                if (_mode == OceanBaseCompatibilityMode.MySql
                    && sqlFunctionExpression.Arguments[1].Type == typeof(TimeSpan))
                {
                    AppendMySqlTimeSpanSign(sqlFunctionExpression.Arguments[1]);
                    Sql.Append(" * ");
                    if (unit == "DAY")
                    {
                        Sql.Append("(ABS(TIME_TO_SEC(");
                        Visit(sqlFunctionExpression.Arguments[1]);
                        Sql.Append(")) DIV 86400)");
                    }
                    else if (unit == "HOUR")
                    {
                        Sql.Append("(MOD(ABS(TIME_TO_SEC(");
                        Visit(sqlFunctionExpression.Arguments[1]);
                        Sql.Append(")), 86400) DIV 3600)");
                    }
                    else if (unit == "MINUTE")
                    {
                        Sql.Append("(MOD(ABS(TIME_TO_SEC(");
                        Visit(sqlFunctionExpression.Arguments[1]);
                        Sql.Append(")), 3600) DIV 60)");
                    }
                    else if (unit == "SECOND")
                    {
                        Sql.Append("MOD(ABS(TIME_TO_SEC(");
                        Visit(sqlFunctionExpression.Arguments[1]);
                        Sql.Append(")), 60)");
                    }
                    else if (unit == "MILLISECOND")
                    {
                        Sql.Append("(ABS(MICROSECOND(");
                        Visit(sqlFunctionExpression.Arguments[1]);
                        Sql.Append(")) DIV 1000)");
                    }
                    else if (unit is "MICROSECOND" or "NANOSECOND")
                    {
                        Sql.Append("MOD(ABS(MICROSECOND(");
                        Visit(sqlFunctionExpression.Arguments[1]);
                        Sql.Append(")), 1000)");
                        if (unit == "NANOSECOND")
                            Sql.Append(" * 100");
                    }
                    else
                        throw new InvalidOperationException("Invalid MySQL duration extraction expression.");
                }
                else if (_mode == OceanBaseCompatibilityMode.MySql
                    && (sqlFunctionExpression.Arguments[1].Type == typeof(DateTime)
                        || sqlFunctionExpression.Arguments[1].Type == typeof(DateTimeOffset)
                        || sqlFunctionExpression.Arguments[1].Type == typeof(TimeOnly))
                    && unit is "MILLISECOND" or "MICROSECOND" or "NANOSECOND")
                {
                    if (unit == "MILLISECOND")
                    {
                        Sql.Append("(MICROSECOND(");
                        Visit(sqlFunctionExpression.Arguments[1]);
                        Sql.Append(") DIV 1000)");
                    }
                    else
                    {
                        Sql.Append("MOD(MICROSECOND(");
                        Visit(sqlFunctionExpression.Arguments[1]);
                        Sql.Append("), 1000)");
                        if (unit == "NANOSECOND")
                            Sql.Append(" * 100");
                    }
                }
                else if (_mode == OceanBaseCompatibilityMode.Oracle)
                {
                    if (unit == "MILLISECOND")
                    {
                        Sql.Append("MOD(TRUNC(EXTRACT(SECOND FROM ");
                        Visit(sqlFunctionExpression.Arguments[1]);
                        Sql.Append(") * 1000), 1000)");
                    }
                    else if (unit is "MICROSECOND" or "NANOSECOND")
                    {
                        Sql.Append("MOD(TRUNC(EXTRACT(SECOND FROM ");
                        Visit(sqlFunctionExpression.Arguments[1]);
                        Sql.Append(") * ").Append(unit == "MICROSECOND" ? "1000000" : "1000000000")
                            .Append("), 1000)");
                    }
                    else
                    {
                        Sql.Append("EXTRACT(").Append(unit).Append(" FROM ");
                        Visit(sqlFunctionExpression.Arguments[1]);
                        Sql.Append(")");
                    }
                }
                else
                    throw new InvalidOperationException("Invalid internal temporal extraction mode.");
                return sqlFunctionExpression;
            }

            if (sqlFunctionExpression.Name == OceanBaseTemporalMemberTranslator.TimeOfDayMarker)
            {
                if (sqlFunctionExpression is not { IsBuiltIn: true, Schema: null, Instance: null, Arguments.Count: 1,
                        IsNullable: true, ArgumentsPropagateNullability: [false], Type: var timeOfDayType,
                        TypeMapping: { } resultMapping }
                    || timeOfDayType != typeof(TimeSpan)
                    || !IsTimeSpanMapping(resultMapping)
                    || sqlFunctionExpression.Arguments[0] is not { Type: var inputType, TypeMapping: { } inputMapping }
                    || (inputType != typeof(DateTime) && inputType != typeof(DateTimeOffset))
                    || !IsDateTimeMapping(inputMapping))
                    throw new InvalidOperationException("Invalid internal TimeOfDay expression.");

                if (_mode == OceanBaseCompatibilityMode.MySql)
                {
                    Sql.Append("TIME(");
                    Visit(sqlFunctionExpression.Arguments[0]);
                    Sql.Append(")");
                }
                else if (inputMapping.StoreTypeNameBase.Equals("DATE", StringComparison.OrdinalIgnoreCase))
                {
                    Sql.Append("NUMTODSINTERVAL((");
                    Visit(sqlFunctionExpression.Arguments[0]);
                    Sql.Append(" - TRUNC(");
                    Visit(sqlFunctionExpression.Arguments[0]);
                    Sql.Append(")), 'DAY')");
                }
                else
                {
                    Sql.Append("(");
                    Visit(sqlFunctionExpression.Arguments[0]);
                    Sql.Append(" - CAST(TRUNC(");
                    Visit(sqlFunctionExpression.Arguments[0]);
                    Sql.Append(") AS TIMESTAMP))");
                }

                return sqlFunctionExpression;
            }

            if (sqlFunctionExpression.Name == OceanBaseTemporalMemberTranslator.DateTimeOffsetUtcDateTimeMarker)
            {
                if (sqlFunctionExpression is not { IsBuiltIn: true, Schema: null, Instance: null, Arguments.Count: 1,
                        IsNullable: true, ArgumentsPropagateNullability: [false], Type: var resultType,
                        TypeMapping: { } resultMapping }
                    || resultType != typeof(DateTime)
                    || !IsDateTimeMapping(resultMapping)
                    || sqlFunctionExpression.Arguments[0] is not { Type: var inputType, TypeMapping: { } inputMapping }
                    || inputType != typeof(DateTimeOffset)
                    || !IsDateTimeMapping(inputMapping))
                    throw new InvalidOperationException("Invalid internal DateTimeOffset.UtcDateTime expression.");

                if (_mode == OceanBaseCompatibilityMode.MySql)
                {
                    Visit(sqlFunctionExpression.Arguments[0]);
                }
                else
                {
                    Sql.Append("CAST(SYS_EXTRACT_UTC(");
                    Visit(sqlFunctionExpression.Arguments[0]);
                    Sql.Append(") AS TIMESTAMP)");
                }

                return sqlFunctionExpression;
            }

            if (sqlFunctionExpression.Name == OceanBaseTemporalMemberTranslator.DateTimeOffsetUtcNowMarker)
            {
                if (sqlFunctionExpression is not { IsBuiltIn: true, Schema: null, Instance: null, Arguments.Count: 0,
                        IsNullable: false, Type: var resultType, TypeMapping: { } resultMapping }
                    || resultType != typeof(DateTimeOffset)
                    || !IsDateTimeOffsetMapping(resultMapping))
                    throw new InvalidOperationException("Invalid internal DateTimeOffset.UtcNow expression.");

                Sql.Append(_mode == OceanBaseCompatibilityMode.MySql
                    ? "UTC_TIMESTAMP(6)"
                    : "CAST(SYS_EXTRACT_UTC(SYSTIMESTAMP) AS TIMESTAMP)");
                return sqlFunctionExpression;
            }

            if (sqlFunctionExpression.Name is OceanBaseTemporalMethodTranslator.DateTimeAddMonthsMarker
                or OceanBaseTemporalMethodTranslator.DateTimeAddYearsMarker)
                return GenerateDateTimeCalendarAdd(sqlFunctionExpression);

            if (sqlFunctionExpression.Name == OceanBaseTemporalMethodTranslator.DateOnlyAddDaysMarker)
            {
                if (sqlFunctionExpression is not { IsBuiltIn: true, Schema: null, Instance: null, Arguments.Count: 2,
                        IsNullable: true, ArgumentsPropagateNullability: [false, false], Type: var resultType,
                        TypeMapping: { } resultMapping }
                    || resultType != typeof(DateOnly)
                    || !IsDateOnlyMapping(resultMapping)
                    || sqlFunctionExpression.Arguments[0] is not { Type: var inputType, TypeMapping: { } inputMapping }
                    || inputType != typeof(DateOnly)
                    || !IsDateOnlyMapping(inputMapping)
                    || sqlFunctionExpression.Arguments[1] is not { Type: var daysType, TypeMapping: { } daysMapping }
                    || daysType != typeof(int)
                    || daysMapping.Converter?.ConvertsNulls == true
                    || (daysMapping.Converter?.ProviderClrType ?? daysMapping.ClrType) != typeof(int))
                    throw new InvalidOperationException("Invalid internal DateOnly.AddDays expression.");

                if (_mode == OceanBaseCompatibilityMode.MySql)
                {
                    Sql.Append("DATE_ADD(");
                    Visit(sqlFunctionExpression.Arguments[0]);
                    Sql.Append(", INTERVAL ");
                    Visit(sqlFunctionExpression.Arguments[1]);
                    Sql.Append(" DAY)");
                }
                else
                {
                    Sql.Append("(");
                    Visit(sqlFunctionExpression.Arguments[0]);
                    Sql.Append(" + ");
                    Visit(sqlFunctionExpression.Arguments[1]);
                    Sql.Append(")");
                }

                return sqlFunctionExpression;
            }

            if (sqlFunctionExpression.Name == OceanBaseTemporalMethodTranslator.DateOnlyAddMonthsMarker)
            {
                if (sqlFunctionExpression is not { IsBuiltIn: true, Schema: null, Instance: null, Arguments.Count: 2,
                        IsNullable: true, ArgumentsPropagateNullability: [false, false], Type: var resultType,
                        TypeMapping: { } resultMapping }
                    || resultType != typeof(DateOnly)
                    || !IsDateOnlyMapping(resultMapping)
                    || sqlFunctionExpression.Arguments[0] is not { Type: var inputType, TypeMapping: { } inputMapping }
                    || inputType != typeof(DateOnly)
                    || !IsDateOnlyMapping(inputMapping)
                    || sqlFunctionExpression.Arguments[1] is not { Type: var monthsType, TypeMapping: { } monthsMapping }
                    || monthsType != typeof(int)
                    || monthsMapping.Converter?.ConvertsNulls == true
                    || (monthsMapping.Converter?.ProviderClrType ?? monthsMapping.ClrType) != typeof(int))
                    throw new InvalidOperationException("Invalid internal DateOnly.AddMonths expression.");

                if (_mode == OceanBaseCompatibilityMode.MySql)
                {
                    Sql.Append("DATE_ADD(");
                    Visit(sqlFunctionExpression.Arguments[0]);
                    Sql.Append(", INTERVAL ");
                    Visit(sqlFunctionExpression.Arguments[1]);
                    Sql.Append(" MONTH)");
                }
                else
                {
                    Sql.Append("(");
                    AppendOracleCalendarDate(sqlFunctionExpression.Arguments[0], sqlFunctionExpression.Arguments[1], years: false);
                    Sql.Append(")");
                }

                return sqlFunctionExpression;
            }

            if (sqlFunctionExpression.Name == OceanBaseTemporalMethodTranslator.DateOnlyAddYearsMarker)
            {
                if (sqlFunctionExpression is not { IsBuiltIn: true, Schema: null, Instance: null, Arguments.Count: 2,
                        IsNullable: true, ArgumentsPropagateNullability: [false, false], Type: var resultType,
                        TypeMapping: { } resultMapping }
                    || resultType != typeof(DateOnly)
                    || !IsDateOnlyMapping(resultMapping)
                    || sqlFunctionExpression.Arguments[0] is not { Type: var inputType, TypeMapping: { } inputMapping }
                    || inputType != typeof(DateOnly)
                    || !IsDateOnlyMapping(inputMapping)
                    || sqlFunctionExpression.Arguments[1] is not { Type: var yearsType, TypeMapping: { } yearsMapping }
                    || yearsType != typeof(int)
                    || yearsMapping.Converter?.ConvertsNulls == true
                    || (yearsMapping.Converter?.ProviderClrType ?? yearsMapping.ClrType) != typeof(int))
                    throw new InvalidOperationException("Invalid internal DateOnly.AddYears expression.");

                if (_mode == OceanBaseCompatibilityMode.MySql)
                {
                    Sql.Append("DATE_ADD(");
                    Visit(sqlFunctionExpression.Arguments[0]);
                    Sql.Append(", INTERVAL ");
                    Visit(sqlFunctionExpression.Arguments[1]);
                    Sql.Append(" YEAR)");
                }
                else
                {
                    Sql.Append("(");
                    AppendOracleCalendarDate(sqlFunctionExpression.Arguments[0], sqlFunctionExpression.Arguments[1], years: true);
                    Sql.Append(")");
                }

                return sqlFunctionExpression;
            }

            if (sqlFunctionExpression.Name == OceanBaseTemporalMemberTranslator.DayNumberMarker)
            {
                if (sqlFunctionExpression is not { IsBuiltIn: true, Schema: null, Instance: null, Arguments.Count: 1,
                        IsNullable: true, ArgumentsPropagateNullability: [false], Type: var resultType,
                        TypeMapping: { } resultMapping }
                    || resultMapping.Converter?.ConvertsNulls == true
                    || resultType != typeof(int)
                    || sqlFunctionExpression.Arguments[0] is not { Type: var inputType, TypeMapping: { } inputMapping }
                    || inputType != typeof(DateOnly)
                    || !IsDateOnlyMapping(inputMapping))
                    throw new InvalidOperationException("Invalid internal DateOnly.DayNumber expression.");

                if (_mode == OceanBaseCompatibilityMode.MySql)
                {
                    Sql.Append("(DATEDIFF(");
                    Visit(sqlFunctionExpression.Arguments[0]);
                    Sql.Append(", '0001-01-01'))");
                }
                else
                {
                    Sql.Append("(TRUNC(");
                    Visit(sqlFunctionExpression.Arguments[0]);
                    Sql.Append(") - DATE '0001-01-01')");
                }

                return sqlFunctionExpression;
            }

            if (sqlFunctionExpression.Name == OceanBaseTemporalMemberTranslator.TicksMarker)
            {
                if (sqlFunctionExpression is not { IsBuiltIn: true, Schema: null, Instance: null, Arguments.Count: 1,
                        IsNullable: true, ArgumentsPropagateNullability: [false], Type: var resultType,
                        TypeMapping: { } resultMapping }
                    || resultMapping.Converter?.ConvertsNulls == true
                    || resultType != typeof(long)
                    || (resultMapping.Converter?.ProviderClrType ?? resultMapping.ClrType) != typeof(long)
                    || sqlFunctionExpression.Arguments[0] is not { Type: var inputType, TypeMapping: { } inputMapping }
                    || (inputType != typeof(TimeSpan) && inputType != typeof(TimeOnly))
                    || !IsTimeSpanMapping(inputMapping))
                    throw new InvalidOperationException("Invalid internal TimeSpan.Ticks expression.");

                if (_mode == OceanBaseCompatibilityMode.MySql)
                {
                    Sql.Append("((TIME_TO_SEC(");
                    Visit(sqlFunctionExpression.Arguments[0]);
                    Sql.Append(") * 10000000 + ");
                    if (inputType == typeof(TimeSpan))
                        AppendMySqlTimeSpanSign(sqlFunctionExpression.Arguments[0]);
                    else
                        Sql.Append("1");
                    Sql.Append(" * ABS(MICROSECOND(");
                    Visit(sqlFunctionExpression.Arguments[0]);
                    Sql.Append(") * 10))");
                }
                else
                {
                    Sql.Append("((EXTRACT(DAY FROM ");
                    Visit(sqlFunctionExpression.Arguments[0]);
                    Sql.Append(") * 864000000000 + EXTRACT(HOUR FROM ");
                    Visit(sqlFunctionExpression.Arguments[0]);
                    Sql.Append(") * 36000000000 + EXTRACT(MINUTE FROM ");
                    Visit(sqlFunctionExpression.Arguments[0]);
                    Sql.Append(") * 600000000 + TRUNC(EXTRACT(SECOND FROM ");
                    Visit(sqlFunctionExpression.Arguments[0]);
                    Sql.Append(") * 10000000))");
                }

                return sqlFunctionExpression;
            }

            if (sqlFunctionExpression.Name == OceanBaseTemporalMemberTranslator.DateTimeTicksMarker)
            {
                if (sqlFunctionExpression is not { IsBuiltIn: true, Schema: null, Instance: null, Arguments.Count: 1,
                        IsNullable: true, ArgumentsPropagateNullability: [false], Type: var resultType,
                        TypeMapping: { } resultMapping }
                    || resultMapping.Converter?.ConvertsNulls == true
                    || resultType != typeof(long)
                    || (resultMapping.Converter?.ProviderClrType ?? resultMapping.ClrType) != typeof(long)
                    || sqlFunctionExpression.Arguments[0] is not { Type: var inputType, TypeMapping: { } inputMapping }
                    || inputType != typeof(DateTime)
                    || !IsDateTimeMapping(inputMapping))
                    throw new InvalidOperationException("Invalid internal DateTime.Ticks expression.");

                if (_mode == OceanBaseCompatibilityMode.MySql)
                {
                    Sql.Append("((DATEDIFF(");
                    Visit(sqlFunctionExpression.Arguments[0]);
                    Sql.Append(", '0001-01-01') * 864000000000) + (TIME_TO_SEC(TIME(");
                    Visit(sqlFunctionExpression.Arguments[0]);
                    Sql.Append(")) * 10000000) + (ABS(MICROSECOND(TIME(");
                    Visit(sqlFunctionExpression.Arguments[0]);
                    Sql.Append(")) * 10))");
                }
                else if (inputMapping.StoreTypeNameBase.Equals("DATE", StringComparison.OrdinalIgnoreCase))
                {
                    Sql.Append("((TRUNC(");
                    Visit(sqlFunctionExpression.Arguments[0]);
                    Sql.Append(") - DATE '0001-01-01') * 864000000000 + TRUNC((");
                    Visit(sqlFunctionExpression.Arguments[0]);
                    Sql.Append(" - TRUNC(");
                    Visit(sqlFunctionExpression.Arguments[0]);
                    Sql.Append(")) * 864000000000)");
                }
                else
                {
                    Sql.Append("((TRUNC(");
                    Visit(sqlFunctionExpression.Arguments[0]);
                    Sql.Append(") - DATE '0001-01-01') * 864000000000 + EXTRACT(HOUR FROM (");
                    Visit(sqlFunctionExpression.Arguments[0]);
                    Sql.Append(" - CAST(TRUNC(");
                    Visit(sqlFunctionExpression.Arguments[0]);
                    Sql.Append(") AS TIMESTAMP))) * 36000000000 + EXTRACT(MINUTE FROM (");
                    Visit(sqlFunctionExpression.Arguments[0]);
                    Sql.Append(" - CAST(TRUNC(");
                    Visit(sqlFunctionExpression.Arguments[0]);
                    Sql.Append(") AS TIMESTAMP))) * 600000000 + TRUNC(EXTRACT(SECOND FROM (");
                    Visit(sqlFunctionExpression.Arguments[0]);
                    Sql.Append(" - CAST(TRUNC(");
                    Visit(sqlFunctionExpression.Arguments[0]);
                    Sql.Append(") AS TIMESTAMP))) * 10000000)");
                }

                return sqlFunctionExpression;
            }

            if (sqlFunctionExpression.Name is OceanBaseTemporalMemberTranslator.DateTimeNowMarker
                or OceanBaseTemporalMemberTranslator.DateTimeUtcNowMarker
                or OceanBaseTemporalMemberTranslator.DateTimeTodayMarker)
            {
                if (sqlFunctionExpression is not { IsBuiltIn: true, Schema: null, Instance: null, Arguments.Count: 0,
                        IsNullable: false, Type: var resultType, TypeMapping: { } resultMapping }
                    || resultType != typeof(DateTime)
                    || resultMapping.Converter?.ConvertsNulls == true
                    || !IsDateTimeMapping(resultMapping))
                    throw new InvalidOperationException("Invalid internal DateTime clock expression.");

                if (_mode == OceanBaseCompatibilityMode.MySql)
                {
                    Sql.Append(sqlFunctionExpression.Name switch
                    {
                        OceanBaseTemporalMemberTranslator.DateTimeNowMarker => "CURRENT_TIMESTAMP(6)",
                        OceanBaseTemporalMemberTranslator.DateTimeUtcNowMarker => "UTC_TIMESTAMP(6)",
                        _ => "CURDATE()"
                    });
                }
                else
                {
                    Sql.Append(sqlFunctionExpression.Name switch
                    {
                        OceanBaseTemporalMemberTranslator.DateTimeNowMarker => "CAST(CURRENT_TIMESTAMP AS TIMESTAMP)",
                        OceanBaseTemporalMemberTranslator.DateTimeUtcNowMarker => "CAST(SYS_EXTRACT_UTC(SYSTIMESTAMP) AS TIMESTAMP)",
                        _ => "TRUNC(CURRENT_DATE)"
                    });
                }

                return sqlFunctionExpression;
            }

            if (sqlFunctionExpression.Name == OceanBaseTemporalMemberTranslator.TotalMarker)
            {
                if (sqlFunctionExpression is not { IsBuiltIn: true, Schema: null, Instance: null, Arguments.Count: 2,
                        IsNullable: true, ArgumentsPropagateNullability: [false, false], TypeMapping: { } resultMapping }
                    || sqlFunctionExpression.Arguments[0] is not SqlFragmentExpression { Sql: var unit }
                    || unit is not ("DAY" or "HOUR" or "MINUTE" or "SECOND" or "MILLISECOND" or "MICROSECOND" or "NANOSECOND")
                    || sqlFunctionExpression.Type != typeof(double)
                    || resultMapping.Converter?.ConvertsNulls == true
                    || (resultMapping.Converter?.ProviderClrType ?? resultMapping.ClrType) != typeof(double)
                    || sqlFunctionExpression.Arguments[1].Type != typeof(TimeSpan)
                    || sqlFunctionExpression.Arguments[1].TypeMapping is not { } inputMapping
                    || !IsTimeSpanMapping(inputMapping))
                    throw new InvalidOperationException("Invalid internal TimeSpan total expression.");

                if (_mode == OceanBaseCompatibilityMode.MySql)
                {
                    Sql.Append("((TIME_TO_SEC(");
                    Visit(sqlFunctionExpression.Arguments[1]);
                    Sql.Append(") + ");
                    AppendMySqlTimeSpanSign(sqlFunctionExpression.Arguments[1]);
                    Sql.Append(" * (ABS(MICROSECOND(");
                    Visit(sqlFunctionExpression.Arguments[1]);
                    Sql.Append(") / 1000000.0))");
                    switch (unit)
                    {
                        case "DAY": Sql.Append(" / 86400.0"); break;
                        case "HOUR": Sql.Append(" / 3600.0"); break;
                        case "MINUTE": Sql.Append(" / 60.0"); break;
                        case "MILLISECOND": Sql.Append(" * 1000.0"); break;
                        case "MICROSECOND": Sql.Append(" * 1000000.0"); break;
                        case "NANOSECOND": Sql.Append(" * 1000000000.0"); break;
                    }
                    Sql.Append("))");
                }
                else
                {
                    Sql.Append("((EXTRACT(DAY FROM ");
                    Visit(sqlFunctionExpression.Arguments[1]);
                    Sql.Append(") * 86400.0 + EXTRACT(HOUR FROM ");
                    Visit(sqlFunctionExpression.Arguments[1]);
                    Sql.Append(") * 3600.0 + EXTRACT(MINUTE FROM ");
                    Visit(sqlFunctionExpression.Arguments[1]);
                    Sql.Append(") * 60.0 + EXTRACT(SECOND FROM ");
                    Visit(sqlFunctionExpression.Arguments[1]);
                    Sql.Append(")");
                    switch (unit)
                    {
                        case "DAY": Sql.Append(" / 86400.0"); break;
                        case "HOUR": Sql.Append(" / 3600.0"); break;
                        case "MINUTE": Sql.Append(" / 60.0"); break;
                        case "MILLISECOND": Sql.Append(" * 1000.0"); break;
                        case "MICROSECOND": Sql.Append(" * 1000000.0"); break;
                        case "NANOSECOND": Sql.Append(" * 1000000000.0"); break;
                    }
                    Sql.Append("))");
                }
                return sqlFunctionExpression;
            }

            if (_mode == OceanBaseCompatibilityMode.Oracle
                && sqlFunctionExpression is { IsBuiltIn: true, Name: "CONCAT", Arguments.Count: 2, Type: var type }
                && type == typeof(string))
            {
                Sql.Append("(");
                Visit(sqlFunctionExpression.Arguments[0]);
                Sql.Append(" || ");
                Visit(sqlFunctionExpression.Arguments[1]);
                Sql.Append(")");
                return sqlFunctionExpression;
            }

            if (sqlFunctionExpression.Name == OceanBaseTemporalMemberTranslator.DateTimeOffsetTotalOffsetMinutesMarker)
            {
                if (sqlFunctionExpression is not { IsBuiltIn: true, Schema: null, Instance: null, Arguments.Count: 1,
                        IsNullable: true, ArgumentsPropagateNullability: [false], TypeMapping: { } resultMapping }
                    || _mode != OceanBaseCompatibilityMode.Oracle
                    || sqlFunctionExpression.Type != typeof(int)
                    || sqlFunctionExpression.Arguments[0].Type != typeof(DateTimeOffset)
                    || sqlFunctionExpression.Arguments[0].TypeMapping is not { } inputMapping
                    || inputMapping.Converter?.ConvertsNulls == true
                    || !IsDateTimeOffsetMapping(inputMapping)
                    || resultMapping.Converter?.ConvertsNulls == true
                    || (resultMapping.Converter?.ProviderClrType ?? resultMapping.ClrType) != typeof(int))
                    throw new InvalidOperationException("Invalid internal DateTimeOffset total-offset-minutes expression.");

                Sql.Append("(EXTRACT(TIMEZONE_HOUR FROM ");
                Visit(sqlFunctionExpression.Arguments[0]);
                Sql.Append(") * 60 + EXTRACT(TIMEZONE_MINUTE FROM ");
                Visit(sqlFunctionExpression.Arguments[0]);
                Sql.Append(")");
                return sqlFunctionExpression;
            }

            if (sqlFunctionExpression.Name == OceanBaseTemporalMemberTranslator.DateTimeOffsetOffsetMarker)
            {
                if (sqlFunctionExpression is not { IsBuiltIn: true, Schema: null, Instance: null, Arguments.Count: 1,
                        IsNullable: true, ArgumentsPropagateNullability: [false], Type: var resultType,
                        TypeMapping: { } resultMapping }
                    || _mode != OceanBaseCompatibilityMode.Oracle
                    || resultType != typeof(TimeSpan)
                    || !IsTimeSpanMapping(resultMapping)
                    || sqlFunctionExpression.Arguments[0] is not { Type: var inputType, TypeMapping: { } inputMapping }
                    || inputType != typeof(DateTimeOffset)
                    || inputMapping.Converter?.ConvertsNulls == true
                    || !IsDateTimeOffsetMapping(inputMapping))
                    throw new InvalidOperationException("Invalid internal DateTimeOffset offset expression.");

                Sql.Append("NUMTODSINTERVAL(EXTRACT(TIMEZONE_HOUR FROM ");
                Visit(sqlFunctionExpression.Arguments[0]);
                Sql.Append(") * 60 + EXTRACT(TIMEZONE_MINUTE FROM ");
                Visit(sqlFunctionExpression.Arguments[0]);
                Sql.Append("), 'MINUTE')");
                return sqlFunctionExpression;
            }

            return base.VisitSqlFunction(sqlFunctionExpression);
        }

        private static bool IsCompensatedSingleSum(SqlExpression body, Type modelType, RelationalTypeMapping mapping)
            => body is SqlFunctionExpression { IsBuiltIn: true, Name: "COALESCE", Arguments:
                    [SqlFunctionExpression { IsBuiltIn: true, Name: "SUM" } sum, SqlConstantExpression { Value: 0 } zero] }
                && sum.Type == modelType && ReferenceEquals(sum.TypeMapping, mapping)
                && ReferenceEquals(body.TypeMapping, mapping) && ReferenceEquals(zero.TypeMapping, mapping);

        private bool IsTimeSpanMapping(RelationalTypeMapping mapping)
        {
            if (mapping.Converter?.ConvertsNulls == true) return false;
            var providerType = mapping.Converter?.ProviderClrType ?? mapping.ClrType;
            var store = mapping.StoreTypeNameBase.Trim().ToUpperInvariant();
            return _mode == OceanBaseCompatibilityMode.MySql
                ? providerType == typeof(TimeSpan) && store == "TIME"
                : providerType == typeof(OceanBaseIntervalDayToSecond) && store.StartsWith("INTERVAL DAY", StringComparison.Ordinal);
        }

        private bool IsDateTimeMapping(RelationalTypeMapping mapping)
        {
            if (mapping.Converter?.ConvertsNulls == true) return false;
            var providerType = mapping.Converter?.ProviderClrType ?? mapping.ClrType;
            var store = mapping.StoreTypeNameBase.Trim().ToUpperInvariant();
            return _mode == OceanBaseCompatibilityMode.MySql
                ? providerType == typeof(DateTime) && store == "DATETIME"
                : (providerType == typeof(DateTime) || providerType == typeof(DateTimeOffset))
                && (store is "DATE" or "TIMESTAMP");
        }

        private bool IsDateTimeOffsetMapping(RelationalTypeMapping mapping)
        {
            if (mapping.Converter?.ConvertsNulls == true || mapping.ClrType != typeof(DateTimeOffset)) return false;
            var providerType = mapping.Converter?.ProviderClrType ?? mapping.ClrType;
            var store = mapping.StoreTypeNameBase.Trim().ToUpperInvariant();
            return _mode == OceanBaseCompatibilityMode.MySql
                ? providerType == typeof(DateTime) && store == "DATETIME"
                : providerType == typeof(DateTimeOffset) && store == "TIMESTAMP";
        }

        private bool IsDateOnlyMapping(RelationalTypeMapping mapping)
        {
            if (mapping.Converter?.ConvertsNulls == true) return false;
            var providerType = mapping.Converter?.ProviderClrType ?? mapping.ClrType;
            var store = mapping.StoreTypeNameBase.Trim().ToUpperInvariant();
            return mapping.ClrType == typeof(DateOnly) && providerType == typeof(DateTime) && store == "DATE";
        }

        private void AppendMySqlTimeSpanSign(SqlExpression input)
        {
            Sql.Append("(CASE WHEN CAST(");
            Visit(input);
            Sql.Append(" AS CHAR) LIKE '-%' THEN -1 ELSE 1 END)");
        }

        protected override void GeneratePseudoFromClause()
        {
            if (_mode == OceanBaseCompatibilityMode.Oracle)
            {
                Sql.AppendLine().Append("FROM DUAL");
            }
        }

        protected override void GenerateLimitOffset(SelectExpression selectExpression)
        {
            if (_mode == OceanBaseCompatibilityMode.Oracle)
            {
                base.GenerateLimitOffset(selectExpression);
                return;
            }

            if (selectExpression.Limit is not null || selectExpression.Offset is not null)
            {
                Sql.AppendLine().Append("LIMIT ");
                if (selectExpression.Limit is not null)
                {
                    Visit(selectExpression.Limit);
                }
                else
                {
                    Sql.Append("18446744073709551615");
                }

                if (selectExpression.Offset is not null)
                {
                    Sql.Append(" OFFSET ");
                    Visit(selectExpression.Offset);
                }
            }
        }
    }
}
