# EF10 Table and Entity Splitting

**Date:** 2026-11-13. **IDs:** DEF-07, MOD-01..03, QRY-01/05/08, UPD-01..05, TXN-02, SCH-01/02.

## Contract

Support the complete local relational workflow in both modes for one entity mapped across a primary table
and required fragment tables (`SplitToTable`), and related entities sharing one physical row (`ToTable`).
Reuse EF10's relational model, query shaper, shared-row modification commands and dependency ordering.
Only the primary table generates an entity-split identity; fragment PK/FKs receive its value. Compare both
table name and schema, including store-specific key column names. Shared-table identity generation remains
owned by its physical column rather than by the number of entity mappings.

Cover int/long identities, assigned/composite/Guid keys, fragment schema evolution, references, nullable
properties, optional all-null dependents, shared concurrency fields, inserts/updates/deletes, key propagation,
automatic transactions and failures retaining entries. EF relational restrictions still apply: entity-split
fragments are required, and inheritance combined with entity splitting is not admitted by EF's validator.
An optional shared-row dependent with all nullable non-key values NULL materializes as absent.
Application-managed shared concurrency values must be changed consistently on both mapped entities;
database-generated shared tokens are read once and propagated to both entries by EF's column modifications.
EF10's `OnUpdateSometimes` convention describes conditional shared-column propagation and must not be
mistaken for an unbacked server-generated scalar by either schema validator.

## Approach

Generalize the existing TPT physical-key propagation classifier for mapping fragments, then remove only the
entity-splitting rejection from both schema generators. Merely removing the rejection would emit independent
identities; implementing another query/write pipeline would duplicate EF's existing services. No public API,
dependency or connector changes are needed. Preserve TPC and unbacked-generated-property rejection.

## Evidence boundaries

Source transaction tests use the registered provider batch executor/transaction factory with a replacement
physical connection. Package consumers intercept commands and disable automatic transactions explicitly.
Neither proves native joins, shared-row affected counts, identity generation or physical rollback. CLI fixtures
compile actual migrations/snapshots and build/help bundles without executing them. Native validation requires
the existing transport gate to be resolved and an isolated authorized DDL/cleanup scope.
