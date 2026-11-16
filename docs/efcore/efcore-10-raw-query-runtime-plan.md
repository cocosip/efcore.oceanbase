# EF10 raw SQL query materialization and recovery

**Base:** `6e31783`. **IDs:** `QRY-10`, `QRY-05/08/11`, `MAP-07`, `CON-04/05`, `TST-01`, `PKG-03/04`.
**Goal:** Complete the existing FromSqlRaw/FromSqlInterpolated query workflow's local result-schema,
tracking, composition and failure-recovery coverage as one module.
**Architecture:** Reuse EF10 raw query translation, FromSqlQueryingEnumerable column indexing and the
provider's existing binding/mapping/connection services. Use the existing native connection doubles;
reader rows verify shaping and lifecycle, not execution of caller-supplied SQL.
**Spec:** `efcore-10-query-update-design.md`, QRY-10 in `efcore-10-feature-checklist.md`.
**Status:** Local tests/package checks complete; independent final review/local commit recorded below.
Native acceptance and the overall EF10 goal remain open.

## Contract and scope

- Noncomposed raw queries for the tested multi-column entity bind result columns by mapped column name, independently of column
  order/case, and decode nullable/converted properties. Extra columns do not alter the model projection.
- Tracking, no tracking and identity resolution handle repeated key rows and repeated enumeration through
  the ordinary EF shaper. No new identity map or raw-SQL parser.
- Composed queries retain the quoted derived table, current inputs, predicates/order/paging and scalar
  projection. Interpolated values stay parameters, including hostile strings and NULL.
- Missing mapped columns fail clearly even for an empty reader; invalid provider values/required NULLs
  preserve EF diagnostics. Readers/owned leases close after errors and allow later queries.
- Noncomposable SQL used in a composed query rejects before opening. Command failures/cancellation
  preserve exception/token and cleanup; cancellation does not become an update exception.
- Retain the documented MySQL repeated caller-owned native parameter restriction. Do not invent a
  connector clone contract or add Database.SqlQuery APIs to this module.

Both modes are OceanBase modes. This module adds no server syntax or upstream Oracle/MySQL capability
assumptions. The application owns raw SQL syntax and identifiers; provider-owned composition uses the
existing OceanBase dialect. Native SQL/results/collation/metadata remain unverified.

## Files and execution

- [x] Add `RawQueryRuntimeTests.cs` in FunctionalTests using `TransactionTests.TestConnection` and named
  DataTable readers. Run the focused profile before any production change; distinguish fixture errors
  and existing correct EF behavior from genuine production RED.
- [x] If a defect appears, fix it at its shared owning service, with a meaningful regression and connected
  raw binding/materialization/connection checks. Otherwise leave production behavior unchanged.
- [x] Add a small public package-only smoke and invoke it from PackageConsumer Program. Verify both modes
  at EF10.0.0 and 10.0.14. Repack only if production changes; record reused package provenance otherwise.
- [x] Run full Debug/Release and isolated EF10.0.14, independent read-only review, document results and
  boundaries in owning ledgers/matrix, diff-check and commit locally.

Review result-column mapping independently of the supplied rows, tracking identity, input occurrence
order, pre-open rejection, reader/lease cleanup and later success. No test double evaluates the raw SQL.

## Sources and evidence

Published EF10.0.0 relational query pipeline defines FromSqlQueryingEnumerable.BuildIndexMap, tracking
and cardinality. Pin/cache exact source under `artifacts/raw-query-runtime/source/` when examining a defect.
Current owning provider services are OceanBaseRelationalCommandBuilderFactory, OceanBaseQuerySqlGenerator
and the existing type mappings. Artifacts: `artifacts/raw-query-runtime/`.

The initial 32-case profile passes unchanged production code. Adding the repeated-parameter boundary
gives 36 focused passes (`focused.log`); no production RED or production fix is claimed. Initial full
runs fail with ManyServiceProvidersCreatedWarning: this fixture's detailed-error option adds cached
provider combinations to the process-wide cache. The fixture now disables provider caching locally,
without suppressing diagnostics. Failed full logs are retained as `full-*-before-fixture-fix.log`.
This is a test-isolation correction, not an OceanBase failure.

Independent review identifies one assertion gap: canned scalar rows could hide an incorrect outer
projection or ordering. The test now explicitly requires SELECT alias.Maybe and ORDER BY alias.RowKey
with the same alias. Those final assertions and the isolated fixture are included in all three full runs.

| Final gate | Result |
| --- | --- |
| EF10.0.0 Debug | Unit 2463, Functional 2219, Specification 1538, ConsumerTests 2 pass; 16 existing mapped skips |
| EF10.0.0 Release | Same: 6222 pass, zero failures, 16 existing mapped skips |
| Isolated EF10.0.14 Release | Same: 6222 pass, zero failures, 16 existing mapped skips |
| Package content/runtime | Unchanged set-operation nupkg passes content and fresh both-patch/mode public raw-query smokes |

Run `dotnet test OceanBase.EntityFrameworkCore.slnx -c Debug --no-restore -m:1 -nr:false` and the same
command at Release. Logs: `full-debug.log`, `full-release.log`, `full-compat.log`. The isolated patch
command adds `--artifacts-path artifacts/raw-query-runtime/compat-build` and
`-p:DirectoryPackagesPropsPath=D:/Code/dotnet/efcore.oceanbase/artifacts/raw-query-runtime/compat/Directory.Packages.props`.
It uses the module-local patch props/restore and existing local feed; the baseline stays EF10.0.0.
These full runs include connected BasicRelational raw commands/queries, materialization and connection
boundary regressions. No standalone connected-test total is claimed. The focused command is
`dotnet test tests/OceanBase.EntityFrameworkCore.FunctionalTests -c Release --no-restore --filter
FullyQualifiedName~RawQueryRuntimeTests -m:1 -nr:false`.

Final independent read-only re-review covers the corrected SQL assertions, fixture isolation and owning
documentation and finds no further actionable issues. The reviewer does not rerun tests or establish
native evidence; the command results above are the execution evidence.

Both-patch/mode package-only consumers pass using the unchanged reviewed set-operation package:
`artifacts/set-operations/package/OceanBase.EntityFrameworkCore.10.0.0-set-operations-20261116.nupkg`.
Content checks and package-only graphs pass. Archive/consumer SHA256 is
`19F828983A14ADD2905F9C81A8FE57F98F38F995E08C246159B3DB55B269AE0C`. This artifact was built from
`4b8fed2` plus the set-operation implementation committed as `6e31783`. This module has no production
diff from that implementation. Current source rebuild has a different informational revision stamp and
SHA256 `70D1A7D046893773D996ADAAD90B5C825564BE511B5754C6D874C97468D130FC`; do not claim equality
between that rebuilt file and the reused nupkg. The original set-operation plan records its build-time
source/archive identity. No repack is needed for these test/consumer/docs changes.

Reproduce the package checks with Verify-Package.ps1 against that archive/version and
`artifacts/raw-query-runtime/verify-consumers.ps1`; see `package-content.log` and `consumers.log`.
Both isolated consumer asset graphs contain provider/Connector/EF packages, no project dependency.
No fresh generated-context/optimize/migration CLI/bundle, hosted CI/Linux or native run is claimed.
The final commit is recorded in Git history; tested revision is `6e31783` plus this module's test/consumer/
documentation changes. Required status stays 9 Done / 44 In progress per mode. Native raw SQL, column
metadata, cancellation/transport and broader official store-backed query families remain open.

Ruling: keep this module on the existing efcore/10.0 worktree and use the user's already authorized inline
module workflow; no new worktree or design approval cycle. Public scope is the existing QRY-10 APIs.
