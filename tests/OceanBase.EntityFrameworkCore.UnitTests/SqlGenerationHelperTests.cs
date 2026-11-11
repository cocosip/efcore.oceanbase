using System.Text;
using Microsoft.EntityFrameworkCore.Storage;
using OceanBase.EntityFrameworkCore.Storage;
using Xunit;

namespace OceanBase.EntityFrameworkCore.UnitTests;

public sealed class SqlGenerationHelperTests
{
    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, "`order`", "@p0")]
    [InlineData(OceanBaseCompatibilityMode.Oracle, "\"order\"", ":p0")]
    public void Generates_mode_specific_identifiers_and_parameters(
        OceanBaseCompatibilityMode mode,
        string identifier,
        string parameter)
    {
        var helper = new OceanBaseSqlGenerationHelper(mode);

        Assert.Equal(identifier, helper.DelimitIdentifier("order"));
        Assert.Equal(parameter, helper.GenerateParameterName("p0"));
        Assert.Equal(parameter, helper.GenerateParameterNamePlaceholder("p0"));
        Assert.Equal(";", helper.StatementTerminator);
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, "a`b", "a``b", "`a``b`", "s`s", "`s``s`.`a``b`")]
    [InlineData(OceanBaseCompatibilityMode.Oracle, "a\"b", "a\"\"b", "\"a\"\"b\"", "s\"s", "\"s\"\"s\".\"a\"\"b\"")]
    [InlineData(OceanBaseCompatibilityMode.MySql, "海🌊", "海🌊", "`海🌊`", "库", "`库`.`海🌊`")]
    [InlineData(OceanBaseCompatibilityMode.Oracle, "海🌊", "海🌊", "\"海🌊\"", "库", "\"库\".\"海🌊\"")]
    [InlineData(OceanBaseCompatibilityMode.MySql, "", "", "``", "", "``.``")]
    [InlineData(OceanBaseCompatibilityMode.Oracle, "", "", "\"\"", "", "\"\".\"\"")]
    [InlineData(OceanBaseCompatibilityMode.MySql, "a\"b", "a\"b", "`a\"b`", "order", "`order`.`a\"b`")]
    [InlineData(OceanBaseCompatibilityMode.Oracle, "a`b", "a`b", "\"a`b\"", "order", "\"order\".\"a`b\"")]
    public void Identifier_escaping_and_schema_qualification_match_all_builder_overloads(
        OceanBaseCompatibilityMode mode, string name, string escaped, string delimited, string schema, string qualified)
    {
        var helper = new OceanBaseSqlGenerationHelper(mode);
        Assert.Equal(escaped, helper.EscapeIdentifier(name));
        Assert.Equal(delimited, helper.DelimitIdentifier(name));
        Assert.Equal(delimited, helper.DelimitIdentifier(name, null));
        Assert.Equal(qualified, helper.DelimitIdentifier(name, schema));
        var builder = new StringBuilder("prefix:");
        helper.EscapeIdentifier(builder, name);
        Assert.Equal("prefix:" + escaped, builder.ToString());
        builder.Clear().Append("prefix:");
        helper.DelimitIdentifier(builder, name);
        Assert.Equal("prefix:" + delimited, builder.ToString());
        builder.Clear().Append("prefix:");
        helper.DelimitIdentifier(builder, name, null);
        Assert.Equal("prefix:" + delimited, builder.ToString());
        builder.Clear().Append("prefix:");
        helper.DelimitIdentifier(builder, name, schema);
        Assert.Equal("prefix:" + qualified, builder.ToString());
    }

    [Theory]
    [InlineData(OceanBaseCompatibilityMode.MySql, "@")]
    [InlineData(OceanBaseCompatibilityMode.Oracle, ":")]
    public void Parameter_builder_overloads_append_the_same_placeholders(OceanBaseCompatibilityMode mode, string prefix)
    {
        var helper = new OceanBaseSqlGenerationHelper(mode);
        foreach (var name in new[] { "p0", "海🌊", "" })
        {
            Assert.Equal(prefix + name, helper.GenerateParameterName(name));
            Assert.Equal(prefix + name, helper.GenerateParameterNamePlaceholder(name));
            var builder = new StringBuilder("prefix:");
            helper.GenerateParameterName(builder, name);
            Assert.Equal("prefix:" + prefix + name, builder.ToString());
            builder.Clear().Append("prefix:");
            helper.GenerateParameterNamePlaceholder(builder, name);
            Assert.Equal("prefix:" + prefix + name, builder.ToString());
        }

        var prefixed = prefix + "named";
        Assert.Equal(prefixed, helper.GenerateParameterName(prefixed));
        Assert.Equal(prefixed, helper.GenerateParameterNamePlaceholder(prefixed));
        var parameterBuilder = new StringBuilder("existing:");
        helper.GenerateParameterName(parameterBuilder, prefixed);
        Assert.Equal("existing:" + prefixed, parameterBuilder.ToString());
        parameterBuilder.Clear().Append("existing:");
        helper.GenerateParameterNamePlaceholder(parameterBuilder, prefixed);
        Assert.Equal("existing:" + prefixed, parameterBuilder.ToString());
    }
}
