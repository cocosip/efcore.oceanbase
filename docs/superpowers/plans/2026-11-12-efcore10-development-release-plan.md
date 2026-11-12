# EF Core 10 Development and Release Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** Finish every locally developable required EF Core 10 feature, validate the provider against real OceanBase MySQL and Oracle tenants, and produce a release candidate with complete package and support evidence.

**Architecture:** Continue on efcore/10.0 with explicit MySQL/Oracle compatibility modes. Reuse the existing EF Core relational services, shared VALUES collection pipeline, provider mappings, query/update generators and namespace lifecycle services. Keep local implementation evidence separate from native OceanBase acceptance.

**Tech Stack:** .NET 10, EF Core/Relational [10.0.0,11.0.0), OceanBaseConnector 0.2.0, xUnit, relational specification tests, packed consumer tests.

**Spec:** docs/efcore/efcore-10-feature-checklist.md, docs/efcore/efcore-10-technical-baseline.md, docs/efcore/efcore-10-query-update-design.md, docs/efcore/efcore-10-progress.md.

## Global Constraints

- Both MySQL and Oracle modes are required and must be tested independently.
- Generated SQL, intercepted commands and supplied readers do not prove native OceanBase execution.
- Keep EF Core dependencies in [10.0.0,11.0.0) and the runtime connector at 0.2.0.
- Preserve every Required checklist row; do not turn an open requirement into a passing row by weakening its acceptance criteria.
- Keep credentials outside the repository and do not commit live connection strings.
- Run warnings-as-errors, full Debug/Release tests, selected later EF10 patch tests and packed consumer checks for each release checkpoint.
- Do not push, publish, tag or synchronize other branches implicitly.
- Keep EF8/EF9 and Deferred families outside the initial EF10 release unless the scope is explicitly changed.

## Review Focus

- Native server coercion, collation, temporal semantics, transaction atomicity and DDL privileges must be tested separately from intercepted SQL.
- Mode-specific quoting, casts, Boolean/null behavior, table-free Oracle SQL and model-cache identity must remain independent.
- Value converters must preserve model/provider mappings, null conversion rules, parameter values and reader hooks.
- Caller-owned versus provider-owned connections, cancellation cleanup, savepoints and namespace ownership must preserve the documented lifecycle.
- Packed consumers must resolve only published package dependencies and must run against both the baseline and selected later EF10 patch.

## Current Baseline

The feature implementation baseline is 5c42125 (Batch3ah, temporal collection-root reader alignment). Debug and Release source suites each pass 3984 tests: 1896 unit, 1218 intercepted functional, 868 specification and 2 project consumers, with zero failures or skips. Release packaging and Verify-Package.ps1 pass for 10.0.0. This is local evidence only; the provider is not release-ready.

The checklist currently has 9 Done rows, 44 In progress rows, 2 Planned rows and 8 Deferred rows. The status split below is the working boundary for automatic progression.

## Development Work Still Required

| Workstream | Checklist IDs | Current state | Work required before local closure | Local exit evidence |
| --- | --- | --- | --- | --- |
| Query, mappings and conversion | SQL-01, MAP-01..07, QRY-01..07, QRY-11 | Broad local coverage, aggregate rows still open | Complete the remaining binary/interval/other collection-root feasibility work; finish converter/operator compositions, function and aggregate overloads, Boolean/cast/overflow boundaries, and unsupported-shape diagnostics. | Focused RED/GREEN unit tests, both-mode intercepted sync/async tests, full Debug/Release and selected-patch suites, package smoke. |
| Model, materialization and filters | MOD-01..03, QRY-02, QRY-05, QRY-08..09, TST-01 | Core inherited model families pass; wider runtime/specification families remain | Add the remaining model, graph, Include, filter, cardinality and query/write specification families that are locally feasible. Keep inherited official assertions intact. | Named specification fixtures pass in both modes with no hidden skips/exclusions; corresponding functional tests pass. |
| Tracked writes and bulk DML | UPD-01..05, DEF-03 | Ordinary writes and current MySQL/Oracle bulk rewrites have local coverage | Close remaining generated-value, converter, composite-key, advanced bulk and unsupported-shape cases. Keep arbitrary converter/JSON/wider model variants explicit until designed. | Sync/async functional tests, failure/entry identity checks, transaction-owner checks, package consumer smoke. |
| Connection, diagnostics and lifecycle | CFG-04, CON-02..05, DIA-01..02, TXN-01..03, SCH-01..03, DEF-09 | Deterministic doubles cover many boundaries; native ownership, cleanup, rollback and DDL remain open | Finish local service/facade gaps and deterministic failure/cleanup cases; add the live fixture harness required by TST-02. Preserve redaction and ownership boundaries. | Full local lifecycle suite plus a documented live fixture that can run the core matrix without repository credentials. |
| Package and release automation | PKG-01..04, REL-02 | Package gates and selected patch consumers exist; each new release revision needs fresh evidence | Make the release command sequence repeatable: restore, warnings-as-errors build, full tests, pack, Verify-Package.ps1, package-only graphs and consumers. Record the exact revision and hashes. | Release candidate package passes contents, dependency, identity, baseline/later-patch consumer and archive checks. |

The next implementation batch is the remaining collection-root boundary audit under QRY-03/QRY-11 and MAP-06/MAP-07. The current visitor supports numeric, Boolean, text and DateTime-family mappings when EF10 collection metadata is available; binary and Oracle interval carriers remain explicit boundaries. First characterize each remaining carrier with RED tests and the EF10 mapping contract, then either implement one complete family through the existing VALUES pipeline or record a deliberate unsupported boundary. Do not add a second collection translator.

## Validation Work Still Required

These areas have substantial local code but cannot be marked Done from intercepted tests:

- **Mappings:** native integer/Boolean ranges, Decimal/OceanBaseDecimal, floating promotion, Unicode/text padding/collation, Oracle empty BLOB, Guid representation, temporal precision/timezone/range and converter behavior.
- **Queries:** native Boolean/null semantics, collection UNION coercion, string collation/LOB behavior, ordering/paging, aggregate ordering/results, joins/subqueries and unsupported overload behavior.
- **Writes:** generated identities/default/computed readback, concurrency predicates, affected-row semantics, bulk update/delete results and duplicate source matches.
- **Transactions and connections:** open/close ownership, cancellation, savepoints, rollback atomicity, connection reuse, caller transactions and connector/server timeout behavior.
- **Schema lifecycle:** database/user existence classification, CREATE/GRANT/DROP privileges, partial DDL repair, seed transaction behavior, pool clearing and concurrent-session effects.

Each live result must be classified as pass, provider defect, connector defect, server limitation, unsupported contract or environment blocker. A skipped or unavailable live case is not acceptance.

## Release Evidence Still Required

### Phase 1: Close local implementation

- [ ] Select one checklist slice and write its concrete positive, boundary and unsupported cases into the relevant design document.
- [ ] Add RED tests before production changes.
- [ ] Implement the smallest change in the existing service/pipeline extension point.
- [ ] Run focused unit and intercepted functional tests in both modes and both sync/async paths where applicable.
- [ ] Run the complete Debug and Release solution suites with warnings-as-errors.
- [ ] Run the isolated later-EF10 patch suite and the packed consumer on EF10.0.0 and the selected later patch.
- [ ] Perform source review, repair confirmed findings with regressions, update checklist/progress/design, and commit the batch.

### Phase 2: Complete local specification and package gates

- [ ] Finish the remaining locally feasible TST-01 specification families and document every intentional exclusion.
- [ ] Build the provider and package consumer from a clean checkout with no project reference to the provider.
- [ ] Run Verify-Package.ps1, dependency-range checks, package-only graph checks and provider/archive/consumer identity checks.
- [ ] Record the exact revision, test counts, package version and hashes in the progress handoff.

### Phase 3: Establish live validation

- [ ] Add a configuration-only live fixture using external secrets for one declared MySQL tenant and one declared Oracle tenant.
- [ ] Predeclare cleanup scope and never let a live test infer tenant or namespace ownership from a username alone.
- [ ] Run the core vertical slice in each mode: open, scalar query, mapping round trips, generated insert, update/delete, transaction rollback/savepoint, EnsureCreated/EnsureDeleted within the documented ownership boundary and diagnostics/redaction.
- [ ] Expand the matrix for the open MAP/QRY/UPD/TXN/SCH rows, including null/empty/boundary cases and both sync/async paths.
- [ ] Reproduce the first failing operation and classify the failure before changing provider code.
- [ ] Update TST-02 only from fresh live evidence; keep historical or intercepted results separate.

### Phase 4: Produce a release candidate

- [ ] Complete REL-01 with supported server, connector, runtime, EF, tenant mode, endpoint and transport details without credentials.
- [ ] Re-run the full source, specification, package and selected-patch suites on the release revision.
- [ ] Run hosted Windows/Linux CI for the release revision, not only for historical commits.
- [ ] Review public README/API/support claims against Required and Deferred rows.
- [ ] Mark REL-02 only after the reviewed release revision, package and live evidence agree.

### Phase 5: Publish only after explicit release approval

- [ ] Tag and publish the package only after the release candidate gates pass and the user explicitly requests publication.
- [ ] Preserve the source revision, package hash, support record and live acceptance record with the release.

## Deferred Scope Gate

The following remain outside the initial release unless scope is explicitly changed: DEF-01 migrations/history and idempotent scripts; DEF-02 reverse engineering/design-time tooling; DEF-04 JSON/primitive collections/complex types; DEF-05 spatial/XML/native advanced types; DEF-06 stored-procedure mappings and provider-specific sequence/HiLo; DEF-07 advanced inheritance/splitting/skip navigations; DEF-08 split queries/lazy loading/compiled-model tooling; and DEF-10 full-text/vector/search and advanced batching. DEF-03 bulk DML and DEF-09 seeding/retry/transaction behavior remain tracked as In progress because their local partial implementations exist.

## Automatic Progression Rules

Every batch must name its checklist IDs, files, tests and exit evidence before editing. The agent then completes the RED/GREEN cycle, full local validation, package smoke, source review, documentation update and local commit in one batch. It must not repeat a completed checkpoint, silently convert a live requirement into a local-only claim, or publish/push without an explicit request.
