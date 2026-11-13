# EF Core 10 Table-per-Type Inheritance

## Outcome and scope

Complete the locally developable TPT workflow in both compatibility modes: relational model, offline creation,
model-difference migrations, hierarchy query/shaping, tracked insert/update/delete and package consumption.
This partially promotes DEF-07 and advances MOD-01/03, QRY-01/05/08/09, UPD-01..05 and SCH-01/02. The broader
EF10 goal and native acceptance remain open. TPC, entity splitting and sequence/HiLo strategies stay separate.

The current provider inherits EF's relational inheritance query/write services, and the inherited metadata
specification family already runs in both modes. At baseline 0766d38, both schema generators reject all TPT models.
The two existing identity classifiers also treat a generated inherited key as an identity in every table.
Removing the blanket rejection alone would create independent derived-table generators and misdescribe the
schema. A key copied from the base row must be an ordinary required PK/FK column in each derived table.

## Implementation decision

Reuse EF's TPT relational model, join/discriminator shaping and modification-command dependency graph.
Share the existing provider's identity classification between create scripts and migration annotations, and
make it physical-table aware: a TPT primary-key property generates only in its declaring entity's table;
the inherited column receives that value. Allow this propagated OnAdd key through generated-column checks.
Keep unbacked generated scalars, TPC, entity splitting and all existing migration scope checks rejected.
No public API or new dependency is needed.

An alternative custom TPT query/update pipeline would duplicate EF and is unnecessary. Continuing the schema
rejection would leave an advertised EF workflow unusable. The chosen approach closes the existing schema seam
and verifies the whole workflow; a test-only promotion would not close that seam.

EF10 contracts: `IColumn.PropertyMappings`, property `DeclaringType`/`ValueGenerated`, entity mapping strategy,
`StoreObjectIdentifier`, `IMigrationsModelDiffer`, and EF's relational query/modification services. Verify on
10.0.0 and 10.0.14; no claim about arbitrary later private-contract compatibility.

References: EF [TPT documentation](https://learn.microsoft.com/ef/core/modeling/inheritance#table-per-type-configuration),
[relational model construction](https://github.com/dotnet/efcore/blob/v10.0.0/src/EFCore.Relational/Metadata/Internal/RelationalModel.cs),
[batch executor](https://github.com/dotnet/efcore/blob/v10.0.0/src/EFCore.Relational/Update/Internal/BatchExecutor.cs)
and [modification commands](https://github.com/dotnet/efcore/blob/v10.0.0/src/EFCore.Relational/Update/ModificationCommand.cs).
The existing MySQL readback contract rejects server-default/generated-expression keys; this module does not
add another key strategy. SaveChanges counts physical modification commands, following the relational executor.

## Acceptance

- Both modes create root/intermediate/leaf/sibling tables with one root identity, required inherited keys,
  correct PK/FK edges, quoted/custom schemas and no synthesized derived generators. Check int/long and
  assigned/composite/client Guid keys; generated nonkeys and TPC/splitting still reject.
- Real initial/evolved/reverse model differences preserve physical identity annotations, derived nullability,
  indexes and constraints. CLI snapshots retain the TPT strategy and regenerate the same model.
- Base/derived/OfType/type-test queries materialize the right concrete classes under sync/async and all three
  tracking modes, including null derived values, missing sibling rows, empty results and global filters.
- Inserting a leaf saves base then intermediate then leaf with the same generated key. Updating inherited and
  derived properties targets their owning tables; deletion reverses dependencies. A failing command/conflict
  retains the failing entry and state, and default automatic transaction control encloses multi-table work.
- Package-only runtime and tooling consumers execute the complete local workflow on both EF patches/modes.
  Full suites, independent whole-module review, docs and one feature commit follow implementation.

Interceptors/readers/native doubles prove provider/client behavior, not server join semantics, generation,
foreign-key enforcement or physical rollback. Native TPT DDL/read/write/transaction validation requires an
authorized isolated schema; no DDL will run against the currently supplied application namespaces.
The runtime transaction fixture replaces IRelationalConnection with the existing native double; it retains the
registered provider batch executor, transaction factory and command generation, but does not prove connector
transaction startup. The package smoke disables transactions because its commands are fully intercepted;
the source runtime fixture independently keeps the default automatic-transaction behavior enabled.
