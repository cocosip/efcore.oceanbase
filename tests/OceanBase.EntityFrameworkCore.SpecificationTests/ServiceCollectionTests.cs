using Microsoft.EntityFrameworkCore;
using Xunit;

namespace OceanBase.EntityFrameworkCore.SpecificationTests;

public abstract class OceanBaseServiceCollectionTests(OceanBaseCompatibilityMode mode)
    : RelationalServiceCollectionExtensionsTestBase(new OceanBaseModelTestHelpers(mode));

public sealed class MySqlServiceCollectionTests()
    : OceanBaseServiceCollectionTests(OceanBaseCompatibilityMode.MySql);

public sealed class OracleServiceCollectionTests()
    : OceanBaseServiceCollectionTests(OceanBaseCompatibilityMode.Oracle);
