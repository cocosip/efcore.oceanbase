# EF10 TPH runtime verification

**Base:** `8b7fa79`; **IDs:** `MOD-02`, `MAP-07`, `QRY-01/03/05/08/09/11`,
`UPD-01..05`, `TXN-02`, `DEF-07`, `TST-01`.

**Goal:** Close the missing local TPH query/write workflow evidence as one inheritance module.
**Architecture:** Reuse EF10 discriminator/model/shaper/update services, provider SQL/mappings/batches,
and existing deterministic transaction doubles. Add no hierarchy service, public option or dependency.
**Spec:** efcore-10-feature-checklist.md and the inheritance/query/tracked-write contracts in
efcore-10-query-update-design.md. Continue inline on efcore/10.0 under the user's module workflow.
**Status:** Local profile verified and reviewed; native and broader official runtime acceptance remain open.

## Profile and approach

TPH maps a concrete root, intermediate, leaf and sibling to one physical table. Use a bounded,
enum-to-string converted shadow discriminator, custom key column, nullable inherited concurrency
token, tenant filter and a leaf collection relationship. Query the real provider with fixed reader
rows at the ADO boundary; independently assert SQL, discriminator literals and parameter values.
No fixture evaluates SQL or claims native filtering, discriminator enforcement or persistence.

EF's existing TPH pipeline is the implementation candidate. A new custom hierarchy pipeline would
duplicate EF responsibilities. Admit existing behavior only after the complete local profile passes;
fix a production defect only after a meaningful failing regression demonstrates it.

| Area | Required local behavior |
| --- | --- |
| Model/schema | One hierarchy table; discriminator conversion/facets and derived-column nullability retained; leaf collection has the correct FK; create script uses the same profile. |
| Materialization | Root/intermediate/leaf/sibling types, nullable fields and empty results work sync/async under tracking, no tracking and identity resolution. Unknown mapped-enum discriminator values reject and readers/connections are cleaned up. |
| Query composition | OfType and type tests constrain discriminator values; scalar predicates/order/paging retain the single-table shape; inherited filters refresh context values and IgnoreQueryFilters removes them. Incomplete discriminator mappings add a known-type predicate even for root queries. |
| Derived graph | Leaf collection Include materializes multiple/missing children, preserves type/fixup and identity semantics; SQL independently contains the discriminator filter, join and ordering. |
| Writes | Every concrete type writes its converted discriminator once to the shared table, uses only its applicable columns and retrieves an identity through the existing mode-specific path. Leaf+child save propagates the key in dependency order and uses one automatic transaction. |
| Update/delete | Original nullable/non-null concurrency predicates and physical key column retained; no discriminator or sibling-only column is rewritten. Zero-row conflicts retain entries/state; successful retry updates snapshots. |
| Failure/cancellation | A later child failure discards generated parent sidecars, keeps pending entries and disposes the uncommitted transaction. Async cancellation does not become a concurrency conflict; a subsequent operation can run. |

## Execution

- [x] Add TphRuntimeTests.cs with the full both-mode sync/async profile above. Run focused tests;
  distinguish production regressions from fixture mistakes and already-working inherited behavior.
- [x] Review any failure against exact EF10 contracts, add RED before a production correction,
  and run connected TPT/TPC/graph/generated-value/transaction/interception regressions.
- [x] Verify a public package-only TPH smoke against the already-reviewed baseline production nupkg
  on EF10.0.0/10.0.14 and both modes. A new provider build is needed only if production changes.
- [x] Run full Debug/Release and isolated EF10.0.14 Release, obtain independent read-only review,
  and update the owning matrix/progress/workboard/design. Local module commit follows these gates.

## Review focus and limits

Reader rows must not stand in for SQL correctness: pin discriminator predicates/literals, tenant/key
binds, selected columns and outgoing DML separately. Distinguish identity resolution inside one query
from cross-query tracking. Verify unrelated sibling columns are never written, nullable concurrency
uses original values, and cleanup never turns a failed graph into accepted state. The model cache
must distinguish complete/incomplete discriminator profiles. Tests replacing the connection service
prove provider query/update/transaction integration, not native mode detection or driver execution.

Native TPH constraints, identity, discriminator/collation, isolation, filtering, rollback and live results
remain unverified. Broader official store-backed inheritance specifications are not replaced by these
local tests. Required statuses remain open; no push, publication or destructive SQL.

## Evidence

Artifacts: `artifacts/tph-runtime/` (local verification outputs, not release assets).

| Gate | Result | Record |
| --- | --- | --- |
| Focused TPH | 78 passed | `focused.log`; final strengthened assertions also pass in the full suites below. |
| Connected TPH/TPT/TPC/graph/generated-values/transaction/interception | 362 passed | `connected.log`; run before final assertion strengthening. |
| Full source Debug and Release, EF10.0.0 | Each: Unit 2421, Functional 2085, Specification 1538, ConsumerTests 2; 6046 passed, 16 existing mapped skips | `full-debug.log`, `full-release.log`; no new skips. |
| Isolated EF10.0.14 Release | Same 6046 passes and 16 mapped skips | `ef10.0.14-release.log`; final assertion changes rechecked by complete Functional 2085/2085 in `review-ef10.0.14-functional.log`. |
| Reused package content | Passed | `package-content.log`; `tests/OceanBase.EntityFrameworkCore.PackageConsumer/Verify-Package.ps1`. |
| Public package-only runtime consumers | Passed on EF10.0.0/10.0.14, both modes and sync/async | `consumers.log`; includes TPH materialization/OfType/assigned-key CRUD and graph/binary verification. |
| Independent read-only review | No remaining actionable code findings | Initial predicate/join/paging and INSERT relationship evidence gaps fixed and re-reviewed. |

Commands: `dotnet test OceanBase.EntityFrameworkCore.slnx -c Debug --no-restore -m:1 -nr:false`
and the same command with `-c Release`. The patch run uses the isolated
`artifacts/tph-runtime/compat/Directory.Packages.props` (EF `[10.0.14]`, Connector 0.3.0) and
`--artifacts-path artifacts/tph-runtime/compat-build`; baseline restore assets remain separate.
The final patch Functional rerun uses that same props/artifacts selection. Public package verification
is recorded in `artifacts/tph-runtime/verify-consumers.ps1` and `consumers.log`.

Initial focused failures (40 failed / 22 passed in the initial 62-case fixture) exposed fixture
assumptions: positional MySQL placeholders, Oracle Unicode discriminator literals, table alias,
physical DML column order and whitespace. Repeated positional inputs must be matched by parameter
object identity, and Oracle named inputs by SQL occurrence rather than collection order. These were
fixture corrections, not production RED. No `src/` change exists relative to base `8b7fa79`.

Review strengthened tenant equality, join keys, key ordering, paging bindings, complete/incomplete
discriminator metadata, every concrete INSERT and parent/child graph INSERT column/value relationships.
Fixed reader rows still do not execute SQL; these independent assertions only establish the local
provider contract. Re-review found no additional actionable code issues.

### Reused package provenance

The unchanged reviewed archive is
`artifacts/save-concurrency-interception/package/OceanBase.EntityFrameworkCore.10.0.0-save-concurrency-interception-20261116.nupkg`.
Its provider DLL SHA256, freshly checked against the archive and both patch consumers, is
`CAF82CED2B5CD9E7D967FA81F1587B82F588EE1E38590CCE4476A47F7B2A7C68`
(`archive-hash.log`). The consumer verifier pins this recorded archive hash and checks package-only
provider/EF/Connector graphs with no project references.

That archive was built on `2deb9ee` plus the concurrency-interception implementation, subsequently
committed as `8b7fa79`. Its informational version is
`10.0.0+2deb9eeb243d36c09b899625423fa2b30a440697`; a current rebuild has
`10.0.0+8b7fa79abd92d0a2270ec1038e6b62ddf3437ba9` and a different DLL hash because the revision stamp
changed. Current rebuilt-source/archive binary equality is not claimed. This module changes tests and
documentation only, so it reuses the reviewed production package rather than producing another artifact.

No fresh tooling/bundle, Linux, hosted CI or native SQL/DDL result is claimed. Required remains
53 rows per mode, 9 Done / 44 In progress. No push or publication.
