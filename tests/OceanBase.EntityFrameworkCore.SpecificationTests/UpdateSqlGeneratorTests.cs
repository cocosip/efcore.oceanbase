using System;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.EntityFrameworkCore.Update;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace OceanBase.EntityFrameworkCore.SpecificationTests;

public abstract class OceanBaseUpdateSqlGeneratorTests(OceanBaseCompatibilityMode mode) : UpdateSqlGeneratorTestBase
{
    protected override TestHelpers TestHelpers { get; } = new OceanBaseModelTestHelpers(mode);
    protected override string OpenDelimiter => mode == OceanBaseCompatibilityMode.MySql ? "`" : "\"";
    protected override string CloseDelimiter => OpenDelimiter;
    protected override string RowsAffected => string.Empty; // The custom batch owns affected-row reporting.
    protected override IUpdateSqlGenerator CreateSqlGenerator()
        => TestHelpers.CreateContextServices().GetRequiredService<IUpdateSqlGenerator>();

    protected override void AppendDeleteOperation_creates_full_delete_command_text_verification(StringBuilder builder)
        => AssertSql(builder, "DELETE FROM \"dbo\".\"Ducks\"\nWHERE \"Id\" = @p0;");

    protected override void AppendDeleteOperation_creates_full_delete_command_text_with_concurrency_check_verification(StringBuilder builder)
        => AssertSql(builder, "DELETE FROM \"dbo\".\"Ducks\"\nWHERE \"Id\" = @p0 AND \"ConcurrencyToken\" IS NULL;");

    protected override void AppendInsertOperation_insert_if_store_generated_columns_exist_verification(StringBuilder builder)
        => AssertSql(builder,
            "INSERT INTO \"dbo\".\"Ducks\" (\"Name\", \"Quacks\", \"ConcurrencyToken\")\nVALUES (@p0, @p1, @p2)",
            " RETURNING \"Id\", \"Computed\" INTO @ob_out_0, @ob_out_1");

    [ConditionalFact]
    public override void AppendInsertOperation_appends_insert_and_select_rowcount_if_no_store_generated_columns_exist_or_conditions_exist()
    {
        var builder = new StringBuilder();
        CreateSqlGenerator().AppendInsertOperation(builder, CreateInsertCommand(identityKey: false, isComputed: false), 0);
        AssertSql(builder, "INSERT INTO \"dbo\".\"Ducks\" (\"Id\", \"Name\", \"Quacks\", \"ConcurrencyToken\")\nVALUES (@p0, @p1, @p2, @p3);");
    }

    protected override void AppendInsertOperation_for_store_generated_columns_but_no_identity_verification(StringBuilder builder)
        => AssertSql(builder,
            "INSERT INTO \"dbo\".\"Ducks\" (\"Id\", \"Name\", \"Quacks\", \"ConcurrencyToken\")\nVALUES (@p0, @p1, @p2, @p3)",
            " RETURNING \"Computed\" INTO @ob_out_0");

    protected override void AppendInsertOperation_for_only_identity_verification(StringBuilder builder)
        => AssertSql(builder,
            "INSERT INTO \"dbo\".\"Ducks\" (\"Name\", \"Quacks\", \"ConcurrencyToken\")\nVALUES (@p0, @p1, @p2)",
            " RETURNING \"Id\" INTO @ob_out_0");

    protected override void AppendInsertOperation_for_all_store_generated_columns_verification(StringBuilder builder)
        => AssertSql(builder,
            mode == OceanBaseCompatibilityMode.MySql ? "INSERT INTO \"dbo\".\"Ducks\" () VALUES ()"
                : "INSERT INTO \"dbo\".\"Ducks\" (\"Id\") VALUES (DEFAULT)",
            " RETURNING \"Id\", \"Computed\" INTO @ob_out_0, @ob_out_1");

    protected override void AppendInsertOperation_for_only_single_identity_columns_verification(StringBuilder builder)
        => AssertSql(builder,
            mode == OceanBaseCompatibilityMode.MySql ? "INSERT INTO \"dbo\".\"Ducks\" () VALUES ()"
                : "INSERT INTO \"dbo\".\"Ducks\" (\"Id\") VALUES (DEFAULT)",
            " RETURNING \"Id\" INTO @ob_out_0");

    protected override void AppendUpdateOperation_if_store_generated_columns_exist_verification(StringBuilder builder)
        => AssertSql(builder,
            "UPDATE \"dbo\".\"Ducks\" SET \"Name\" = @p0, \"Quacks\" = @p1, \"ConcurrencyToken\" = @p2\nWHERE \"Id\" = @p3 AND \"ConcurrencyToken\" IS NULL",
            " RETURNING \"Computed\" INTO @ob_out_0");

    protected override void AppendUpdateOperation_if_store_generated_columns_dont_exist_verification(StringBuilder builder)
        => AssertSql(builder, "UPDATE \"dbo\".\"Ducks\" SET \"Name\" = @p0, \"Quacks\" = @p1, \"ConcurrencyToken\" = @p2\nWHERE \"Id\" = @p3;");

    protected override void AppendUpdateOperation_appends_where_for_concurrency_token_verification(StringBuilder builder)
        => AssertSql(builder, "UPDATE \"dbo\".\"Ducks\" SET \"Name\" = @p0, \"Quacks\" = @p1, \"ConcurrencyToken\" = @p2\nWHERE \"Id\" = @p3 AND \"ConcurrencyToken\" IS NULL;");

    protected override void AppendUpdateOperation_for_computed_property_verification(StringBuilder builder)
        => AssertSql(builder,
            "UPDATE \"dbo\".\"Ducks\" SET \"Name\" = @p0, \"Quacks\" = @p1, \"ConcurrencyToken\" = @p2\nWHERE \"Id\" = @p3",
            " RETURNING \"Computed\" INTO @ob_out_0");

    [ConditionalFact]
    public override void GenerateNextSequenceValueOperation_returns_statement_with_sanitized_sequence()
        => AssertSequenceSql("sequence" + CloseDelimiter + "; --", null,
            $"{OpenDelimiter}sequence{CloseDelimiter}{CloseDelimiter}; --{CloseDelimiter}.NEXTVAL");

    [ConditionalFact]
    public override void GenerateNextSequenceValueOperation_correctly_handles_schemas()
        => AssertSequenceSql("mysequence", "dbo",
            $"{OpenDelimiter}dbo{CloseDelimiter}.{OpenDelimiter}mysequence{CloseDelimiter}.NEXTVAL");

    [ConditionalFact]
    public void Append_operations_report_batch_mapping_and_transaction_requirements()
    {
        var generator = CreateSqlGenerator();
        Assert.Equal(ResultSetMapping.NoResults, generator.AppendInsertOperation(new StringBuilder(),
            CreateInsertCommand(), 0, out var insertReads));
        Assert.Equal(mode == OceanBaseCompatibilityMode.MySql, insertReads);
        Assert.Equal(ResultSetMapping.NoResults, generator.AppendInsertOperation(new StringBuilder(),
            CreateInsertCommand(identityKey: false, isComputed: false), 0, out var insertWrites));
        Assert.False(insertWrites);
        Assert.Equal(ResultSetMapping.NoResults, generator.AppendUpdateOperation(new StringBuilder(),
            CreateUpdateCommand(), 0, out var updateReads));
        Assert.Equal(mode == OceanBaseCompatibilityMode.MySql, updateReads);
        Assert.Equal(ResultSetMapping.NoResults, generator.AppendUpdateOperation(new StringBuilder(),
            CreateUpdateCommand(isComputed: false), 0, out var updateWrites));
        Assert.False(updateWrites);
        Assert.Equal(ResultSetMapping.NoResults, generator.AppendDeleteOperation(new StringBuilder(),
            CreateDeleteCommand(), 0, out var delete));
        Assert.False(delete);
    }

    private void AssertSequenceSql(string name, string? schema, string expression)
    {
        var generator = CreateSqlGenerator();
        Assert.Equal($"SELECT {expression} FROM DUAL", generator.GenerateNextSequenceValueOperation(name, schema));
        Assert.Equal(expression, generator.GenerateObtainNextSequenceValueOperation(name, schema));
    }

    private void AssertSql(StringBuilder builder, string expected, string? returning = null)
    {
        if (returning is not null) expected += (mode == OceanBaseCompatibilityMode.Oracle ? returning : string.Empty) + ";";
        expected = mode == OceanBaseCompatibilityMode.MySql ? expected.Replace('"', '`') : expected.Replace('@', ':');
        Assert.Equal(expected.ReplaceLineEndings(Environment.NewLine) + Environment.NewLine, builder.ToString());
    }
}

public sealed class MySqlUpdateSqlGeneratorTests() : OceanBaseUpdateSqlGeneratorTests(OceanBaseCompatibilityMode.MySql);
public sealed class OracleUpdateSqlGeneratorTests() : OceanBaseUpdateSqlGeneratorTests(OceanBaseCompatibilityMode.Oracle);
