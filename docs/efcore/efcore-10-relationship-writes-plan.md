# EF10 relationship-change write workflows

**Base:** `12b44e7`; **IDs:** `MOD-02`, `UPD-01/03/04/05`, `TXN-02`, `TST-01`.
**Goal:** Verify relationship mutations as one complete tracked-write module, extending the existing
new-graph/dependent-first deletion profile. Continue inline in `efcore/10.0` under the authorized
module/test/review/local-commit workflow.
**Architecture:** Reuse EF10 change tracking, cascade/orphan timing, relational command preparation and
the provider's existing modification batches and transaction executor. Use existing transaction doubles
at the ADO boundary and independently assert SQL and input relationships. No new public API or dependency.
**Spec:** Required MOD-02/UPD-05 and the tracked graph contract in efcore-10-query-update-design.md.
**Status:** Local profile verified and reviewed; native and broader official runtime acceptance remain open.

## Design and scope

The selected approach verifies and, only on demonstrated defects, corrects the existing EF/provider
pipeline. A provider relationship scheduler would duplicate EF. Native store-backed specifications need
an initialized OceanBase store, so local deterministic workflows retain explicit evidence limits.

| Workflow | Acceptance |
| --- | --- |
| Optional severing | Shadow FK writes NULL without deleting the dependent; original nullable concurrency is retained in the predicate; fixup and acceptance agree. |
| Optional principal deletion | Tracked ClientSetNull dependents update before principal deletion, under one automatic transaction. |
| Required reparenting | Moving between tracked principals preserves the dependent, original concurrency and fixup. Direct navigation changes and delayed orphan handling write only the FK; immediate orphan deletion followed by revival may mark other scalar fields modified under EF's state rules. |
| Required orphan deletion | Immediate/OnSaveChanges orphan timing produces the expected dependent DELETE. Never rejects before opening; explicit CascadeChanges then allows the save. |
| Required one-to-one replacement | Delete the old dependent before inserting the new one with the same unique FK; one automatic transaction; retain exact key/token/column/value relationships. |
| Acceptance control | SaveChanges(false) keeps pending old/new states until explicit acceptance. |
| Failure and recovery | Conflict or cancellation on the later replacement command keeps both pending entries and original values, cleans up the owned transaction, and allows a later save. |
| Caller transaction | Replacement failure rolls back to EF's savepoint, retains caller ownership and pending entries; successful retry remains in the caller transaction. |

All workflows run both modes and sync/async. Assigned keys keep this module focused on relationship
mutation; generated graph keys have their existing independent profile. Fixed ADO outcomes do not prove
native unique/FK constraints, cascade actions, persistence, rollback or concurrent isolation.

## Execution plan

- [x] Add `RelationshipWriteTests.cs`; run the focused profile before any production edit. Distinguish
  fixture assumptions and already-working EF behavior from meaningful production RED.
- [x] Correct only demonstrated production defects, if any, with failing-before/passing-after evidence;
  run graph/generated-value/TPH/TPT/TPC/many-to-many/transaction/interception connected regressions.
- [x] Add package-only public relationship replacement/severing smoke; reuse the reviewed production
  package if `src/` stays unchanged, otherwise build and verify a new baseline package.
- [x] Run Debug/Release, isolated EF10.0.14, package content and both-patch/mode consumers; independently
  review the whole module, update owning documents and diff-check. Local module commit follows these gates.

## Review focus

Assert outgoing DML and bindings separately from the ADO outcomes. Distinguish client cascade scheduling
from native referential actions. A successful first command followed by a failing second must not accept
either entry. Verify nullable original predicates, the shadow FK's current/original values, ordering at
the unique-FK boundary, owned versus caller transaction cleanup, cancellation identity and retry state.
Tests must not turn inherited EF behavior into a new provider feature or claim official runtime admission.

## Evidence

Tested revision: base `12b44e7` plus this module's final diff. Artifacts:
`artifacts/relationship-writes/` (local verification outputs, not release assets).

| Gate | Command/result and record |
| --- | --- |
| Initial focused profile | 32 passed / 4 failed (`focused-initial.log`). All four failures were fixture expectations about immediate orphan revival, not production RED. |
| Corrected focused | 36 passed (`focused-check.log`); subsequent direct-navigation controls and the final review assertion pass in the complete Functional runs. |
| Connected | 460 passed (`connected.log`): RelationshipWriteTests, GraphUpdateTests, GeneratedValuesTests, TPH/TPT/TPC, ManyToManyRuntimeTests, TransactionTests and SaveChangesInterceptionTests. |
| Full source | `dotnet test OceanBase.EntityFrameworkCore.slnx -c Debug/Release --no-restore -m:1 -nr:false`: each Unit 2421, Functional 2121, Specification 1538, ConsumerTests 2 = 6082 passed; 16 existing mapped skips (`full-debug.log`, `full-release.log`). |
| Isolated patch | EF10.0.14 Release, same 6082 passes and 16 skips (`ef10.0.14-release.log`). Restore/test use `--artifacts-path artifacts/relationship-writes/compat-build` and `-p:DirectoryPackagesPropsPath=D:/Code/dotnet/efcore.oceanbase/artifacts/relationship-writes/compat/Directory.Packages.props`; Connector remains 0.3.0 and baseline assets remain separate. |
| Final review assertion | Complete Functional reruns on Debug/Release and isolated patch: each 2121/2121 (`review-functional-debug.log`, `review-functional-release.log`, `review-functional-ef10.0.14.log`). |
| Package content | Existing `tests/OceanBase.EntityFrameworkCore.PackageConsumer/Verify-Package.ps1` passes the reused archive (`package-content.log`). |
| Public consumers | `verify-consumers.ps1` passes EF10.0.0/10.0.14, both modes/sync/async; new public smoke checks exact replacement/severing DML, input order and explicit acceptance, with automatic transactions disabled (`consumers.log`). Package-only graphs, no project references and provider binaries verified. |
| Independent review | No production correctness findings. One pre-open assertion gap corrected by checking the existing native double's close count; final code/docs/log re-review found no remaining findings. |

EF10.0.0's exact `NavigationFixer.UndeleteDependent` was inspected from the installed assembly using
ilspycmd (`ef-source/NavigationFixer.cs`). When a deleted dependent is reattached to a tracked unchanged/
modified principal, it calls SetEntityState(Modified), which explains the broader scalar modification
after immediate collection orphan deletion. Tests pin the state/Version modification flag and independent
DML for both that case and direct-navigation/delayed-timing controls. No provider code is changed to
override EF's state semantics. The final pre-open assertion proves rejection did not open and balance a
connection before failing. No `src/` diff exists relative to base `12b44e7`.

### Reused package provenance

Archive:
`artifacts/save-concurrency-interception/package/OceanBase.EntityFrameworkCore.10.0.0-save-concurrency-interception-20261116.nupkg`.
The provider DLL archive and both patch consumers freshly match SHA256
`CAF82CED2B5CD9E7D967FA81F1587B82F588EE1E38590CCE4476A47F7B2A7C68`
(`archive-hash.log`, `binary-provenance.log`, `consumers.log`). The verifier pins this recorded hash.

The archive was built on `2deb9ee` plus the concurrency-interception fix, then committed as `8b7fa79`;
TPH and this module add no production changes. Its informational version remains
`10.0.0+2deb9eeb243d36c09b899625423fa2b30a440697`. The current source rebuild is stamped
`10.0.0+12b44e776312d467e0f0d60328d43da513b0a3a6` and has a different hash. Current-source/archive
binary equality is not claimed; reuse preserves the reviewed production artifact.

Native FK/unique constraints, referential actions, actual persistence/rollback/concurrent isolation,
untracked dependents, broader relationship/model combinations and official store-backed runtime families
remain open. No fresh CLI/bundle, Linux, hosted CI or native SQL/DDL run. Required stays 53 rows per mode,
9 Done / 44 In progress. No push or publication.
