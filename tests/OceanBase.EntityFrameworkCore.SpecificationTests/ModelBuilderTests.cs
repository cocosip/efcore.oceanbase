using Microsoft.EntityFrameworkCore;

namespace OceanBase.EntityFrameworkCore.SpecificationTests;

public abstract class OceanBaseModelBuilderTests(OceanBaseCompatibilityMode mode) : ModelBuilding101RelationalTestBase
{
    private readonly OceanBaseModelTestHelpers _helpers = new(mode);

    protected override DbContextOptionsBuilder ConfigureContext(DbContextOptionsBuilder optionsBuilder)
        => _helpers.UseProviderOptions(optionsBuilder);
}

public sealed class MySqlModelBuilderTests() : OceanBaseModelBuilderTests(OceanBaseCompatibilityMode.MySql);

public sealed class OracleModelBuilderTests() : OceanBaseModelBuilderTests(OceanBaseCompatibilityMode.Oracle);
