using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ModelBuilding;
using static Microsoft.EntityFrameworkCore.ModelBuilding.ModelBuilderTest;

namespace OceanBase.EntityFrameworkCore.SpecificationTests
{
    public abstract class OceanBaseModelBuilderTests
    {
        public abstract class OneToMany(OceanBaseCompatibilityMode mode)
            : RelationalModelBuilderTest.RelationalOneToManyTestBase(new OceanBaseModelBuilderFixture(mode))
        {
            protected override TestModelBuilder CreateModelBuilder(Action<ModelConfigurationBuilder>? configure = null)
                => new GenericTestModelBuilder(Fixture, configure);
        }

        public abstract class ManyToOne(OceanBaseCompatibilityMode mode)
            : RelationalModelBuilderTest.RelationalManyToOneTestBase(new OceanBaseModelBuilderFixture(mode))
        {
            protected override TestModelBuilder CreateModelBuilder(Action<ModelConfigurationBuilder>? configure = null)
                => new GenericTestModelBuilder(Fixture, configure);
        }

        public abstract class OneToOne(OceanBaseCompatibilityMode mode)
            : RelationalModelBuilderTest.RelationalOneToOneTestBase(new OceanBaseModelBuilderFixture(mode))
        {
            protected override TestModelBuilder CreateModelBuilder(Action<ModelConfigurationBuilder>? configure = null)
                => new GenericTestModelBuilder(Fixture, configure);
        }

        public abstract class ManyToMany(OceanBaseCompatibilityMode mode)
            : RelationalModelBuilderTest.RelationalManyToManyTestBase(new OceanBaseModelBuilderFixture(mode))
        {
            protected override TestModelBuilder CreateModelBuilder(Action<ModelConfigurationBuilder>? configure = null)
                => new GenericTestModelBuilder(Fixture, configure);
        }

        public abstract class Inheritance(OceanBaseCompatibilityMode mode)
            : RelationalModelBuilderTest.RelationalInheritanceTestBase(new OceanBaseModelBuilderFixture(mode))
        {
            protected override TestModelBuilder CreateModelBuilder(Action<ModelConfigurationBuilder>? configure = null)
                => new GenericTestModelBuilder(Fixture, configure);
        }

        public abstract class OwnedTypes(OceanBaseCompatibilityMode mode)
            : RelationalModelBuilderTest.RelationalOwnedTypesTestBase(new OceanBaseModelBuilderFixture(mode))
        {
            protected override TestModelBuilder CreateModelBuilder(Action<ModelConfigurationBuilder>? configure = null)
                => new GenericTestModelBuilder(Fixture, configure);
        }
    }

    public sealed class MySqlOneToManyTests() : OceanBaseModelBuilderTests.OneToMany(OceanBaseCompatibilityMode.MySql);
    public sealed class OracleOneToManyTests() : OceanBaseModelBuilderTests.OneToMany(OceanBaseCompatibilityMode.Oracle);
    public sealed class MySqlManyToOneTests() : OceanBaseModelBuilderTests.ManyToOne(OceanBaseCompatibilityMode.MySql);
    public sealed class OracleManyToOneTests() : OceanBaseModelBuilderTests.ManyToOne(OceanBaseCompatibilityMode.Oracle);
    public sealed class MySqlOneToOneTests() : OceanBaseModelBuilderTests.OneToOne(OceanBaseCompatibilityMode.MySql);
    public sealed class OracleOneToOneTests() : OceanBaseModelBuilderTests.OneToOne(OceanBaseCompatibilityMode.Oracle);
    public sealed class MySqlManyToManyTests() : OceanBaseModelBuilderTests.ManyToMany(OceanBaseCompatibilityMode.MySql);
    public sealed class OracleManyToManyTests() : OceanBaseModelBuilderTests.ManyToMany(OceanBaseCompatibilityMode.Oracle);
    public sealed class MySqlInheritanceTests() : OceanBaseModelBuilderTests.Inheritance(OceanBaseCompatibilityMode.MySql);
    public sealed class OracleInheritanceTests() : OceanBaseModelBuilderTests.Inheritance(OceanBaseCompatibilityMode.Oracle);
    public sealed class MySqlOwnedTypesTests() : OceanBaseModelBuilderTests.OwnedTypes(OceanBaseCompatibilityMode.MySql);
    public sealed class OracleOwnedTypesTests() : OceanBaseModelBuilderTests.OwnedTypes(OceanBaseCompatibilityMode.Oracle);
}
