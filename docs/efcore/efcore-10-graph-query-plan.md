# Graph-query native acceptance implementation plan

> **For agentic workers:** Use superpowers:executing-plans inline under the
> established test/review/local-commit authorization.

**Goal:** Accept the existing Include/ThenInclude, identity and named-filter
profile against both OceanBase modes; fix only demonstrated Provider defects.

**Architecture:** Extend LiveStorageTable to observe each successful CREATE for
a fixed set of uniquely named tables and clean them in reverse declared dependency order.
LiveGraphQueryTests uses ordinary assigned-key relational entities and owned
objects. Independent seed arrays determine expected graphs; supplied readers
and SQL shape alone cannot establish native graph correctness.

**Tech Stack:** EF 10.0.0/10.0.14, .NET 10, Connector 0.3.0 public APIs, xUnit.

**Spec:** efcore-10-feature-matrix.md (QRY-08/09, MOD-01/02, DEF-07/08).

## Global constraints

- Existing efcore/10.0 branch; inline execution, local commit, no push/publication.
- Both modes, sync/async, default/relational NULL semantics and all three tracking modes.
- Ordinary User Secrets only; no credentials in source/artifacts or administrator operations.
- Hold one physical connection per test body; restore tracker/context state between scenarios.
- Only CREATE-confirmed test tables may be dropped; attempt every cleanup even after a failure.
- Inheritance, proxy/compiled-model combinations and distributed isolation stay in their existing modules.

## Review focus

- Composite/alternate keys overlap across tenants; dropping either join component must change expected graphs.
- Empty/filtered/missing dependents must not remove optional principals or create phantom entities.
- Identity resolution and navigation fixup differ across tracking modes and across enumerations.
- Named filters must refresh current context parameters and disable only the requested names on every affected source.
- Split graphs must buffer sequentially on the real Connector and keep ownership/cleanup correct on failure.

## Task 1: Multi-table owned fixture

Files: LiveStorageTable.cs, new LiveStorageTableOwnershipTests.cs in LiveTests.

- [x] Add offline observer regressions for success-only, exact allowlist CREATE confirmation,
  recorded creation order and unconfirmed/unrelated commands.
- [x] Accept an optional fixed suffix set; derive each table from the fixture's unique root.
  Record successful expected CREATE statements and drop confirmed tables in reverse declared dependency order.
  Keep ordinary one-table callers and parameter-write counting unchanged.
- [x] Verify partial CREATE cleanup and ownership on ordinary accounts before graph acceptance.

## Task 2: Native graph matrix

Files: new LiveGraphQueryTests.cs.

- [x] Seed composite-key Parent/Child/Detail/Optional, owned reference/collection,
  Tag and explicit many-to-many Link. Use overlapping tenant keys and independent
  alternate codes, NULL optional references, absent details, empty collections,
  soft-deleted principals/dependents and shared tag identities.
- [x] Compare Include/ThenInclude single/split results with independent seed graphs;
  assert inverse references, owned/shadow values, tracker counts and loaded flags.
- [x] Assert required/optional references, repeated entity identity within and across
  enumerations, filtered Include/order/Skip/Take and root paging/empty results.
- [x] Exercise EF10 Tenant/Active named filters, selective/all disable, captured
  hostile values and context tenant changes; verify exact rows and command binding.
- [x] Cover pretracked filtered-Include fixup, fresh no-tracking results and ordinary
  many-to-many split graphs. Keep inheritance/lazy/proxy/AOT acceptance separate.

## Task 3: Corrections and acceptance

- [x] Diagnose differences with actual command/reader and direct Connector controls.
  Add local RED before any production fix, then rerun appropriate local/native tests.
- [x] Full Release suites and serial native suites on both patches, package content
  and both-mode/two-patch consumers on the final source, one fresh read-only review.
- [x] Fix material findings, document exact profile and gaps, audit credentials/LF/
  whitespace/source/package identity, verify zero residual tables and commit locally.

Ignored ledger/logs: artifacts/graph-query/.

Ruling: SchemaSqlGenerator creates tables alphabetically and adds foreign keys afterward.
Reverse observed CREATE order is not a safe DROP order. The fixture therefore takes
the fixed suffix list in principal-before-dependent order and reverses that list,
dropping only success-confirmed entries. The first native graph run demonstrated
MySQL3730/Oracle2449 during cleanup; confirmed residual tables were cleaned separately.
An interrupted root was verified against the exact seven-table model and catalog creation
times before cleanup. The final read-only tagged-table probe reports zero in both modes.
The corrected path passes all66 root targeted cases and both-patch full533-case projects.


## Closeout

The ordinary QRY-08/09 profile is accepted on the declared direct4.4.2.2 environment.
This module changes test fixtures, tests and documentation; no Provider production C#
query change was required. HasData builds the seven-table model with assigned overlapping
tenant keys. Each test body holds one physical connection. Fixed suffixes declare principals
before dependents; cleanup reverses that declaration and only drops CREATE-confirmed names.

EF10.0.0 and isolated10.0.14 local Release each pass6679 with16 existing specification
skips. Root local execution reused unchanged production/local-test Release binaries;
compat rebuilt from the source snapshot. Both full serial LiveTests projects pass533/533:
521 live and12 offline, including56 graph,8 native ownership and2 offline ownership additions.
Root first ran66/66 targeted. Graph-query r1 pack, content/dependencies, both-mode/two-patch
consumers and source/archive/consumer DLL/README identities pass. Final diagnostic tables0
in both modes; credential/LF/whitespace/source snapshot audits pass. No push/publication.

Independent read-only review:0 Critical/0 Important/2 Minor (deferred):

- Captured predicates verify actual rows, raw hostile-value absence in SQL and at least
  one parameter binding. They do not compare each split command's SQL across executions
  or prove parameterization in every later command; correctly escaped literals could evade
  the current negative assertion. Do not claim SQL-text invariance for all graph commands.
- Optional-reference tests keep Tenant active on both sources. They do not independently
  detect omission of TenantId from that FK join. Add selective-Tenant-disable enumeration
  and full parent composite-key assertions when strengthening this combination.

These are evidence limitations, not demonstrated production defects. Inheritance, lazy/
compiled/AOT combinations, graph failure recovery, concurrent-change isolation, arbitrary
converter/model shapes and other deployments retain their independent requirements.

The implementation-only inventory is separate from this acceptance ledger. The user
requested stopping after this module's local commit; no next module is started.
