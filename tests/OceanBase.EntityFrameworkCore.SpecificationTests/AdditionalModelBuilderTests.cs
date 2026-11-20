using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ModelBuilding;
using static Microsoft.EntityFrameworkCore.ModelBuilding.ModelBuilderTest;
using Xunit;

namespace OceanBase.EntityFrameworkCore.SpecificationTests;

public abstract class OceanBaseAdditionalModelBuilderTests
{
    public abstract class NonRelationship(OceanBaseCompatibilityMode mode)
        : RelationalModelBuilderTest.RelationalNonRelationshipTestBase(new OceanBaseModelBuilderFixture(mode))
    {
        protected override TestModelBuilder CreateModelBuilder(Action<ModelConfigurationBuilder>? configure = null)
            => new GenericTestModelBuilder(Fixture, configure);

        [ConditionalFact(Skip = "OceanBase profile rejects MaxLength on numeric or unsupported unbounded carriers; MAP-01/MAP-03.")]
        public override void Can_set_max_length_for_properties() { }

        [ConditionalFact(Skip = "OceanBase profile rejects MaxLength on numeric or unsupported unbounded carriers; MAP-01/MAP-03.")]
        public override void Can_set_max_length_for_property_type() { }

        [ConditionalFact(Skip = "OceanBase profile rejects unbounded MaxLength where the store family requires a bounded declaration; MAP-03.")]
        public override void Can_set_unbounded_max_length_for_property_type() { }

        [ConditionalFact(Skip = "OceanBase profile rejects MaxLength on primitive collection elements without a supported store length; MAP-03/MAP-06.")]
        public override void Can_set_max_length_for_primitive_collections() { }

        [ConditionalFact(Skip = "OceanBase profile rejects unsupported MaxLength on primitive collection elements; MAP-03/MAP-06.")]
        public override void Element_types_can_have_max_length() { }

        [ConditionalFact(Skip = "OceanBase profile rejects precision/scale on text and integer mappings; MAP-01/MAP-03/MAP-05.")]
        public override void Can_set_precision_and_scale_for_properties() { }

        [ConditionalFact(Skip = "OceanBase profile rejects precision/scale on text and integer mappings; MAP-01/MAP-03/MAP-05.")]
        public override void Can_set_precision_and_scale_for_property_type() { }

        [ConditionalFact(Skip = "OceanBase profile rejects unsupported precision/scale on primitive collection elements; MAP-01/MAP-03/MAP-05/MAP-06.")]
        public override void Element_types_can_have_precision_and_scale() { }
    }
}

public sealed class MySqlNonRelationshipTests()
    : OceanBaseAdditionalModelBuilderTests.NonRelationship(OceanBaseCompatibilityMode.MySql);

public sealed class OracleNonRelationshipTests()
    : OceanBaseAdditionalModelBuilderTests.NonRelationship(OceanBaseCompatibilityMode.Oracle);
