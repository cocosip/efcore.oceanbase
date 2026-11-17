# Ordered queries and cardinality native acceptance

Execute inline against base1a4d129 using the approved EF10 queue, then one fresh
independent module review. Scope: QRY-02/05, with narrow QRY-01/08 evidence only.
Spec: the existing feature matrix's ordered paging and First/Single criteria,
ModelMaterializationTests and repository-guidelines.md. No Connector, deployment,
schema namespace, graph Include, aggregate or new operator-family development.

## Design and interfaces

Reuse LiveStorageTable without altering ownership/cleanup. Add one QueryContext
with unique table/mode model-cache keys, six deterministic HasData rows including
duplicate sort values, NULLs and a decimal converter. Compare native results with
independently evaluated seed data and clear tracking between materialization modes.
All cases run sync/async on MySQL/Oracle through Connector0.3.0 public APIs.

Supported overloads here: ordinary OrderBy/Descending/ThenBy/Descending, Distinct,
Skip/Take (nonnegative counts), First/FirstOrDefault/Single/SingleOrDefault with
ordinary optional predicates. Explicit comparer and caller-supplied default-value
overloads are rejection probes, not advertised translated operations. Native NULL
placement follows the dialect; explicit null rank/coalesce provides portable order.

- [x] Add deterministic ordered/paged/entity/scalar/Distinct/offset-only and empty
  cases with captured bound refresh, native NULL ordering and comparer rejection.
- [x] Add empty/one/multiple entity/nonnullable/nullable/converted/reference-DTO
  First/Single/default results and errors, predicate overloads, recovery and ordinary
  tracking/no-tracking/identity-resolution behavior on actual duplicate rows.
- [x] Diagnose differences with direct public Connector where needed; add local
  RED before a product fix. Run appropriate both-patch/live/package/full gates,
  independently review, audit cleanup/secrets, update criteria/status by evidence
  and commit locally. No push/publication.

## Review focus

- Paging needs deterministic final keys when sort values duplicate.
- NULL elements and empty sources must remain distinguishable at materialization.
- Single must detect a second server row, while First returns the ordered first.
- Captured bounds/predicates must refresh and failures must leave the next query usable.
- Entity identity/tracking assertions must compare references, not record equality.

Named/string collations, arbitrary graphs and full official runtime families remain
their existing separate inventory. Do not change a criterion to fit a passing test.

## Findings and evidence boundary

Six seeded rows exercise duplicate values and NULLs. Recomposition with different
Skip/Take values verifies page bounds, first/last/out-of-range/zero-size pages,
offset-only, Take-only, reversed three-key ordering, Distinct before/after paging
and nullable projections. A fixed predicate query separately refreshes a captured
floor. The initial four failures were a test error: Queryable numeric arguments
are consumed when composing a query, so changing locals cannot mutate an existing
Skip(0).Take(0). No Provider defect or production RED/GREEN is claimed for that.

Native NULL positions match each dialect and explicit null rank returns the same
order in both modes. Comparer overloads reject. First/Single/default variants
exercise empty/one/two rows for entities, nonnullable/nullable numeric, strings,
decimal-converted reference values and constructed reference records. A one-row
NULL result is distinct from a cardinality error; standard predicate variants,
explicit-default overload rejection and subsequent query recovery are checked.
The verifier additionally checks the EF no-elements/more-than-one message so an
unrelated InvalidOperationException cannot satisfy the error assertion.

UNION ALL duplicates verify reference identity separately for tracking,
no-tracking and identity resolution, including across enumerations and tracker
entry counts. No graph Include or generic full runtime-family acceptance follows.
Current Provider code already satisfies these cases; this module adds native
acceptance and documentation, not a guessed production change.

## Verification

On the declared OceanBase4.4.2.2 ordinary direct environment, EF10.0.0 and
isolated10.0.14 serial full LiveTests each pass135 cases (125 live, 10 offline),
including the16 new ordered/cardinality cases. The final strengthened cardinality
message assertions then pass all16 targeted cases on each patch. Source hashes
match the compatibility snapshot and its assets resolve EF10.0.14/Connector0.3.0.

Production and offline tests are unchanged since1a4d129; that checkpoint's two
Release suites each passed6645 cases with16 existing specification skips. They
are prior evidence, not repeated runs for this test/documentation-only module.
Package10.0.0-ordered-cardinality-r1-20261117 passes archive/dependency checks and
both-mode consumers on both patches, with source/archive/consumer DLL identity
and packaged README identity verified. Read-only catalog checks find zero tagged
diagnostic tables in both namespaces. Evidence is retained under ignored
artifacts/ordered-cardinality.

## Review and closeout

One fresh independent reviewer found one Important test gap, no Critical/Minor:
SingleOrDefault(-99) used six rows, so even a supported overload could satisfy
the expected exception through ordinary multiplicity. Both explicit-default
probes now use an empty source. A sensitivity control temporarily substituted
supported SingleOrDefault(); all four cardinality cases failed specifically
because no exception was thrown. Restoring the excluded explicit-default
overload passes the full16-case module on each patch. This is test-sensitivity
RED/GREEN, not a Provider fix. No production/package change or second review.

The reviewer supports closing QRY-02/05 without changing their criteria after
that fix. Both modes are now Done for these rows:11 of53 Required rows each;
42 remain In progress. The broader EF10 goal is not complete.

Executor rulings retain the declared boundaries: negative paging counts are
outside the nonnegative contract; string/named collation behavior remains
unverified; graph Include/raw materialization/full runtime families retain
QRY-08/10/TST-01; other server versions/proxy/TLS/transport failures/cancellation
need separate evidence. Wrongly widening any of these claims could conceal
ordering/materialization/lifecycle defects in untested inputs or environments.
The reviewer did not run physical cleanup; executor read-only catalog checks
cover tagged diagnostic tables in both namespaces, without claiming ownership
of foreign resources. Initial paging failures remain a composition-test mistake,
with no Provider defect claimed. No deferred Minor findings in this module.

Final read-only cleanup finds zero tagged diagnostic tables in both namespaces;
exact-module credential and whitespace audits pass. The checked module is
committed locally. No push, publication, Connector changes or protocol bypass.
