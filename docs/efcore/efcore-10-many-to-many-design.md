# EF10 Many-to-Many Skip Navigations

**Date:** 2026-11-13. **Base:** 1f35e12. **IDs:** DEF-07, MOD-01..03, QRY-03/08/09,
UPD-01..05, TXN-02/03, SCH-01/02, PKG-03/04, TST-01.

## Approach and scope

Complete the local schema/query/write workflow for ordinary many-to-many relationships in both modes.
The existing inherited EF relational ManyToMany metadata specifications are necessary but do not prove
runtime behavior. Reuse EF10 skip-navigation expansion, relationship fixup, dependency ordering and shared
join entries with the existing OceanBase mappings, SQL generators and batch/transaction services. A custom
many-to-many translator or write coordinator would duplicate these contracts; add one only for a reproduced
provider gap. This implementation choice follows the continuing authorization to finish complete modules.

Support implicit shared join dictionaries and explicit join entities with scalar payload/concurrency,
generated principal keys, composite join keys, explicit table/column names and ordinary cascade relationships.
Verify creation/model differences, navigation predicates, single-query Include and filtered Include, inverse
fixup and identity behavior, current global-filter values, link insertion/removal and principal deletion.
Test sync/async in both modes, with tracking/no-tracking/identity resolution for reads. Include empty results,
unrelated principals and one related entity shared by multiple principals.

Generated principal keys must reach join FKs before insertion; join keys are never independently identities.
Removing a link must delete only the join row. Explicit payload updates/deletes must use original concurrency
values and surface stale-row exceptions. Multi-command errors retain pending states and primary exceptions,
dispose uncommitted owned transactions, and recover caller-owned transactions through existing savepoints.
Do not claim physical rollback from transaction doubles.

## Verification and boundaries

Use real provider EF pipelines with deterministic readers and the existing transaction connection double.
Pin projections/joins and command parameters independently of supplied results; do not evaluate SQL in a fake
database. Keep upstream metadata assertions unchanged. Add package-only smoke for public model/schema/query/
tracked-link behavior and retain the existing migration CLI/bundle matrix if production changes are required.
Run full Debug/Release and isolated EF10.0.14, then review the entire module before committing.

Native catalogs/results/cascades/identity/rollback still need live acceptance. Split-query/lazy-loading support,
TPC, JSON/complex payloads, provider-specific generation, EF8/9 and publication are separate scope. Existing
required/deferred rows remain open where native evidence or wider shapes are missing. If inherited runtime
behavior already works, record that fact; do not manufacture provider changes to increase the diff.
