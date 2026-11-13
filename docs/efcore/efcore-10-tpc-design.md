# EF10 Table-per-Concrete-Type Inheritance

**Date:** 2026-11-13. **Base:** 3118cea. **IDs:** DEF-07, MOD-01/03, QRY-01/05/08/09,
UPD-01..05, TXN-02, SCH-01/02, PKG-03/04.

## Approach and contract

Promote TPC from a blanket schema/migrations rejection to a complete local workflow using EF10's existing
concrete-table mappings, UNION ALL query expansion and tracked writes. Each concrete table contains its
inherited and declared properties; abstract types have no table and there are no TPT-style hierarchy FKs.
Reuse provider scalar mappings, model differ, SQL generators and batch/transaction services. Do not add a
parallel TPC translator. Fix concrete mapping/SQL defects only after reproducing them.

Keys must be unique across the entire hierarchy. Support application-assigned keys configured with
ValueGeneratedNever, including composite keys, and the existing client-generated Guid key strategy.
Client Guid keys converted to int/long would be recognized as identities by existing physical column rules
and are outside this profile. Computed keys reject even when ValueGeneratedNever is explicitly configured.
Independent per-table int/long identity counters do not guarantee that invariant. Reject unsupported
store-generated TPC keys with actionable advice in both schema generation and migration generation, and
before the first batch is created for any tracked write against that model, including unrelated entities.
The scoped batch factory validates the immutable context model once; it must not wait until a later unsafe
entity's batch after earlier commands may have executed. Sequence/HiLo/distributed key allocators are
separate DEF-06 work; do not silently fabricate offsets or reserve key ranges. Applications own uniqueness
of assigned keys; client Guid generation retains the provider's ordinary conversion/storage contracts.

Cover concrete roots, abstract intermediate types, leaf/sibling concrete tables, renamed inherited columns,
inherited indexes/concurrency/filters, concrete-table relationships and payload evolution. Base queries use
UNION ALL with correct typed NULL/discriminator projections; OfType and type predicates must retain
filters and materialize CLR types. Test tracking, no tracking and identity resolution, sync/async, empty
and nullable results, related Includes and generated client keys. A single entity insert/update/delete
targets one concrete table; multiple entities retain automatic transaction and concurrency behavior.

## Verification and evidence limits

Add focused schema/model-difference and intercepted runtime tests before implementation. Pin SQL projections,
table targets and bound values independently of fixture results. Include negative integer/default generation
and a pre-execution guard for existing schemas. Add a package-only public consumer, migration snapshot/CLI
coverage, full Debug/Release and isolated EF10.0.14 checks, then independently review and commit the module.

EF10's inherited relational model specifications remain unchanged. Upstream runtime query/tracking fixtures
require a real initialized store and cannot be claimed from deterministic readers. Existing read-only probes
with Connector 0.3.0 still fail during OpenAsync; native joins, NULL typing, identity, FKs and rollback remain
acceptance gates. Cross-table polymorphic FK enforcement, database-wide assigned-key uniqueness and
sequence strategies are not supplied by this module. EF8/9, push and publication remain postponed.

References: EF [inheritance and TPC key generation](https://learn.microsoft.com/ef/core/modeling/inheritance#key-generation),
the exact EF10 relational model/query contracts, and existing TPT/splitting designs. TPC supports Guid or
application-generated keys without requiring a common database sequence; independent identity counters
are deliberately insufficient.
