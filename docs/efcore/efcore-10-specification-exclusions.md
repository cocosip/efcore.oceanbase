# EF10 Specification Exclusions

This record owns the explicit exclusions and profile adaptations added while extending `TST-01` with
official relational specification families. It records unsupported profile combinations; it does not
turn a provider mapping failure into a pass.

## Non-relationship model family

`tests/OceanBase.EntityFrameworkCore.SpecificationTests/AdditionalModelBuilderTests.cs` runs the official
family for both MySQL and Oracle through the real OceanBase provider services.

Initial RED result: 346 cases, 330 passed, 16 failed, 0 skipped. The 16 failures were the same eight
inherited tests in each mode:

| Inherited test | Provider boundary | Checklist IDs |
| --- | --- | --- |
| `Can_set_max_length_for_properties` | EF applies a length facet to numeric properties; integer display width is not an EF length facet. | `MAP-01`, `MAP-03` |
| `Can_set_max_length_for_property_type` | Same numeric/unbounded carrier boundary for property-level type configuration. | `MAP-01`, `MAP-03` |
| `Can_set_unbounded_max_length_for_property_type` | An unbounded fixed or otherwise unsupported store declaration cannot be emitted safely. | `MAP-03` |
| `Can_set_max_length_for_primitive_collections` | Primitive collection elements require a supported bounded text/binary carrier before parameter expansion. | `MAP-03`, `MAP-06` |
| `Element_types_can_have_max_length` | The same element mapping boundary applies to model element metadata. | `MAP-03`, `MAP-06` |
| `Can_set_precision_and_scale_for_properties` | Text, integer and unsupported temporal mappings do not accept arbitrary precision/scale facets. | `MAP-01`, `MAP-03`, `MAP-05` |
| `Can_set_precision_and_scale_for_property_type` | Property-type configuration must retain the same provider facet contract. | `MAP-01`, `MAP-03`, `MAP-05` |
| `Element_types_can_have_precision_and_scale` | Collection element precision/scale is constrained by the selected numeric/temporal store family. | `MAP-01`, `MAP-03`, `MAP-05`, `MAP-06` |

The derived test classes mark only these inherited methods with explicit `ConditionalFact(Skip=...)`
reasons. The remaining 330 official assertions execute unchanged. The skips are visible in test output and
must remain mapped to the checklist IDs above; they are not release completion evidence for those mapping rows.

## Acceptance evidence

Focused Release command:

```powershell
dotnet test tests/OceanBase.EntityFrameworkCore.SpecificationTests/OceanBase.EntityFrameworkCore.SpecificationTests.csproj `
  -c Release --no-restore --filter FullyQualifiedName~NonRelationship
```

Result after explicit exclusions: the focused family passes 330 cases with 16 mapped skips and 0 failures;
the complete `SpecificationTests` project passes 1,198 cases with the same 16 mapped skips (1,214 total).
This is local EF/provider
metadata evidence only. It does not establish native SQL, server coercion, collection transport or live
OceanBase behavior.

The exclusions can be removed only after the corresponding mapping contract is implemented and covered by
positive, boundary and negative tests without weakening the provider's range, transport or unsupported-shape
validation.

## Model-building 101 relational family

`tests/OceanBase.EntityFrameworkCore.SpecificationTests/ModelBuilding101Tests.cs` adds the official
`ModelBuilding101RelationalTestBase` for both MySQL and Oracle. This family only builds and inspects the
relational model; the fixture installs a connection-opening guard so it cannot be mistaken for live CRUD or
schema evidence.

Focused Release result: 248 passed, 0 failed, 0 skipped. The family requires no provider exclusion.

The next reflected candidate, `Types.RelationalTypeTestBase<T,TFixture>`, contains only JSON-property query,
`SaveChanges` and `ExecuteUpdate` cases. It is therefore an explicit `DEF-04` candidate and is not admitted to
the Required `TST-01` suite until JSON/complex/primitive-collection support is scheduled. This audit prevents
the deferred JSON family from being counted as ordinary relational type coverage.

## Write SQL generator family

`UpdateSqlGeneratorTests.cs` adds the complete fourteen-method EF10 `UpdateSqlGeneratorTestBase` family
for MySQL and Oracle. Eleven inherited DML test bodies execute unchanged with literal provider SQL
assertions in the required verification hooks. The remaining three method adaptations are explicit:

| Inherited method | Adaptation | Checklist IDs |
| --- | --- | --- |
| `AppendInsertOperation_appends_insert_and_select_rowcount_if_no_store_generated_columns_exist_or_conditions_exist` | Preserve the fixture/command, adapt the hardcoded @ placeholders to Oracle colons. The expected insert still has no extra result set. | `SQL-02`, `UPD-01` |
| `GenerateNextSequenceValueOperation_returns_statement_with_sanitized_sequence` | Require NotSupportedException for delimiter-bearing names through both sequence Generate/Append pairs; append rejection preserves the caller buffer. | `DEF-06` |
| `GenerateNextSequenceValueOperation_correctly_handles_schemas` | Apply the same explicit rejection to a schema-qualified name. No sequence/HiLo support is claimed. | `DEF-06` |

Each mode also has one provider-specific fact checking NoResults and generated-read transaction flags
for insert/update/delete operations. Focused result: 30 passed, 0 failed, 0 skipped. The initial run had
26 passes and four failures because the inherited generator emitted unsupported sequence SQL. The
two append overrides now reject before writing SQL, including through inherited Generate wrappers.
No new skip was introduced and no failed native query was reclassified as a test pass.

This family proves local command construction, NULL concurrency predicates, default-only inserts,
RETURNING placeholders and batch flags through registered provider services. The helper blocks connection
opening. It does not prove native affected rows, key allocation, generated-value readback, transactions or
server SQL acceptance. TST-01 and the parent UPD/SQL rows retain their wider acceptance boundary.
See [the module plan](efcore-10-update-sql-specification-plan.md) for current full/patch/package evidence.
