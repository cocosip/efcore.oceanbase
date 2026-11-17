# Native numeric storage acceptance

Scope: existing MAP-01/02/06/07, based on a31cbf9. Continue the approved mapping
queue using public Connector 0.3.0, ordinary direct connections and User Secrets.
No Connector changes, deployment/protocol expansion, or release publication.

Design: extract the existing owned-table lifecycle into one test helper, keeping
CREATE-success ownership, dynamic model keys, safe diagnostics and independent
cleanup. Keep text and numeric models separate. Test both modes synchronously and
asynchronously through actual EF tracked writes and database materialization.

- [x] Extract lifecycle and verify all existing text cases unchanged.
- [x] Add eight signed/unsigned integer nullable endpoints, Boolean false/true/null,
  integer enum conversion, default and explicit decimal precision/scale, native
  finite float/double extremes/subnormals and numeric converters. Clear tracking
  before reads; verify predicates and updates. Reject lossy decimal writes and
  nonfinite floating inputs before DML, retaining entries and table usability.
- [x] Investigate each unexpected result with a direct Connector control where
  needed. Add local RED regression before any minimal production fix.
- [x] Run serial LiveTests on EF10.0.0 and isolated 10.0.14. Run broader suites and
  package checks if production changes require them; distinguish historical evidence.
- [x] Independent final review, one fix pass if needed; update matrices/progress,
  inspect credentials/diff/cleanup and commit locally.

Review focus: exact values rather than tolerances hiding data loss, actual reader
carriers, signed/unsigned extremes, decimal scale versus query binding, converter
hooks, false/null SQL semantics, rejected writes before DML and cleanup on failures.
Parent matrix rows remain In progress: this bounded storage slice does not establish
all operators, numeric declarations, decimal-backed floating/REAL, versions or modes.

## Native findings and decisions

The original 14 text storage cases pass after extracting the owned-table helper,
before adding the separate numeric model. Each new round-trip clears tracking and
compares complete records using exact CLR equality. Both modes cover nullable
endpoints for all eight integer types, long-enum conversion/predicates, nine pairs
of Boolean false/true/null with lifted NOT/AND/OR/XOR, CASE and correlated EXISTS,
default decimal MinValue/MaxValue/scale-28 and explicit (5,2) converter storage.
Captured decimal predicates retain their own scale independently of column facets.
Tracked updates/deletes and pre-DML rejection preserve entries and usable tables.

Single parameter storage succeeds at extrema, signed minimum subnormals and minimum
normal values. Oracle Single SQL literals initially return 1426: shortest round-trip
text such as 3.4028235E+38F and 1E-45F crosses the server's accepted numeric boundary
before binary rounding. Direct Connector DDL/SELECT controls prove that exact
Double-expanded Single text succeeds. The shared Provider formatter now emits that
text with F suffix. Five unit cases and two Oracle live cases were observed RED;
the formatter fix makes them GREEN. The existing Round guard SQL assertion changes
to the same exact Single representation; no translator logic changes.

MySQL Double parameter extrema/subnormals pass. Oracle ordinary Double values and
numeric converters pass, but MinValue/MaxValue parameter writes return 1426 and
tested tiny values (signed Epsilon and minimum positive normal) store/read as zero.
Direct Connector 0.3.0 reproduces extrema failure and Epsilon loss with PrepareExecute
off/on and explicit Prepare; its parameter SELECT returns OceanBaseDecimal, while
D-suffixed literals return Double and preserve the same extrema/subnormal values.
The exact codec/server cause remains unresolved. Version-pinned negative tests retain
this boundary, failure entries/attempt counts and observed data loss; their passing
status is **not acceptance of full Oracle Double parameter support**. No Connector
source, transport option policy, implicit string carrier or literal substitution was
added to bypass it. The package README and matrices disclose the risk.

Ruling: proceed with the approved mapping slice and record the unresolved direct
Double boundary separately. A wrong attribution would delay resolution of potential
data loss, so neither a Connector defect nor complete Oracle Double support is claimed.

## Verification

Serial LiveTests on EF10.0.0 and isolated EF10.0.14 each pass 59 cases (49 live,
10 offline profiles), including 26 new numeric cases. Public package consumers
pass both modes on both patches and verify the package graph and source/package DLL
identity. Read-only catalog checks find zero EFT_/EFD_/EFL_ diagnostic tables in
both namespaces. Final full Release suites on both patches each pass 6644 cases
(Unit 2617, Functional 2487, Specification 1538, Consumer 2), with 16 existing
specification skips. Package version: 10.0.0-numeric-storage-r1-20261117.
Independent fresh-context review found no actionable Critical/Important/Minor findings.
No fix pass was needed. The reviewer excluded underlying Double codec/server resolution,
wider declarations/deployments and preceding modules; those remain explicit gaps. Final
source/test snapshot (246 files), package DLL/README identity and credential audits pass.

Executor closeout also restored PKG-02/03/04 and TST-01 requirements/status cells from
pre-consolidation f398c42^:docs/efcore/efcore-10-feature-checklist.md; consolidation had
truncated/merged rows. Their criteria/statuses are unchanged, evidence is refreshed, and
both modes now recompute to 53 Required rows: 9 Done/44 In progress. No feature status
was promoted by this repair. The next-case table and TST-02 reflect the new narrow evidence.
These documentation corrections were checked by the executor after the source review;
no second review or duplicate test runs were requested for prose-only edits. Local commit
is the final closeout step; no push or publication.

Evidence: ignored artifacts/numeric-storage, including fixture/initial/isolate,
unit-red.log, boundaries-red, direct probe-paths.log, live-final/compat-live-final,
release-final/compat-release-final, package/consumers.log and cleanup-verification.log.

## Declaration/precision follow-up

The user's proposed explicit-type/precision control now has a separate
[regression record](efcore-10-oracle-double-declarations-plan.md). Catalog and
actual writes distinguish intentional NUMBER(18,6) rounding from native
BINARY_DOUBLE parameter loss. Default/explicit native columns both retain the
tested extremes/tiny D-literal inserts, but fail/lose the corresponding parameters;
parameter Precision17 does not change the measured boundary. The same ordinary
native value keeps its fractional digits. Deliberate decimal conversion is tested
only in its restricted representable domain, with exact-write scale validation.
No Connector/server root-cause ownership or full-range Double acceptance is claimed.
