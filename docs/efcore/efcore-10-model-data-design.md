# EF10 Model-Managed Data During Schema Creation

**Date:** 2026-11-13. **IDs:** SCH-02, DEF-01/09, MAP-07, PKG-03/04.

## Intent and approach

Before this module, migrations rendered HasData operations but create scripts and EnsureCreated rejected
every seeded model. Complete that initialization path as one module. Reuse EF10 IMigrationsModelDiffer from an
empty model for dependency-ordered InsertDataOperation values and existing provider migration INSERT
generation. A separate handwritten seed sorter/converter is unnecessary and would disagree with migrations.
Keep the existing schema DDL generator, which has its own validated creation profile.

Work inline from clean fcd20a2 under the user's standing development authorization. Preserve schema scope,
caller/ambient/enlisted transaction and retry-strategy rejection before opening any connection.

## Contracts

- GenerateCreateScript appends model-managed INSERTs after all DDL, using design-time model metadata,
  store conversions, shared owned rows and EF dependency ordering. Excluded tables contribute no data.
- CreateTables/EnsureCreated prebuild both DDL and INSERT plans before opening. After successful DDL, run
  nonempty model data in one owned transaction, rechecking the actual scope inside that transaction.
  Roll back model data explicitly on failure/cancellation with an uncancelled cleanup token. Preserve the
  primary exception if rollback/disposal also fails. DDL commits implicitly and cannot be rolled back.
- Existing populated schemas skip HasData completely. Existing UseSeeding/UseAsyncSeeding callback behavior
  remains: called once after schema work (including HasData), in its own existing transaction. These two
  data phases are not one atomic transaction; a later callback failure does not erase initial model data.
- Explicit CreateTables also applies HasData, without invoking EnsureCreated-only callbacks. Script
  generation requires no database or management opt-in. Commands retain caller timeout and async tokens.
- HasData keys are explicit/stable; computed/default columns and conversions follow EF migration semantics.
  Manual SQL expressions and application model configuration remain trusted. No generated-key reservation,
  script-wide transaction promise or automatic repair of a partially created schema is introduced.

## Verification and boundaries

Write failing tests against current rejection first. Cover scripts, principal-before-dependent inserts,
enum/text/Boolean conversions and escaping, owned/shared rows, excluded tables; both modes and sync/async
schema creation, existing-table skip, callback ordering, scope/ownership, cancellation/failure/cleanup and
explicit CreateTables. Verify package-only creation through public APIs. Review the whole module before
commit; run full baseline Debug/Release, isolated EF10.0.14 and package/runtime/tooling gates as affected.

Installed EF10.0.0 MigrationsModelDiffer source is decompiled under artifacts/model-data-contract. Its
GetDataOperations sorts modification commands; initial data operations still require filtering to tables
included in this creation plan, even when their DDL is excluded. Confirm selected-patch behavior
with the same public tests. References: [model-managed data](https://learn.microsoft.com/ef/core/modeling/data-seeding#model-managed-data),
[EnsureCreated](https://learn.microsoft.com/ef/core/managing-schemas/ensure-created).

Native DDL/seed transactions, explicit identity behavior and server coercion remain unverified. Recorded
pre-auth endpoint failure is not a driver defect. No unchanged live probes, EF8/9, push or publication.
