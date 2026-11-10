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
}
