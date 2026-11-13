# EF10 TPC Implementation Plan

**Goal:** Implement the both-mode TPC schema/migrations/query/write workflow with safe hierarchy keys.
**Base:** 3118cea. **Spec:** [TPC design](efcore-10-tpc-design.md). **Execution:** Inline.

## Files and steps

1. [x] Add UnitTests/TpcSchemaTests.cs for concrete tables, assigned/composite/client Guid keys, inherited
   columns/indexes and initial/evolved/reverse model differences. Watch current blanket rejections fail.
2. [x] Replace schema/migration blanket rejection with shared TPC key-generation validation in
   Metadata/OceanBaseRelationalAnnotationProvider.cs; use it before the first modification batch is created too.
   Preserve negative identity tests and add actionable existing-schema write failure cases.
3. [x] Add FunctionalTests/TpcRuntimeTests.cs for UNION/OfType/filters/Includes, null/empty/type/identity
   shaping and concrete-table CRUD/concurrency/automatic transaction failure. Fix owning shared code for
   any reproduced defect; preserve inherited metadata specification assertions.
4. [x] Add PackageConsumer/TpcSmoke.cs and ToolingConsumer TPC models to prove public packed schema/query/
   CRUD and real CLI snapshot evolution/compilation. Verify old-package failure and one baseline nupkg on
   both selected patches/modes, including existing full CLI/bundle compatibility.
5. [x] Run full baseline Debug/Release and isolated EF10.0.14, review module, fix findings with regressions,
   update current progress/checklist/roadmap/support notes and commit locally.

## Rulings and evidence

- Upstream ManyToManyTrackingRelationalTestBase inherits a shared initialized store; it cannot establish
  runtime SQL/results without the native connection. No fake SQL evaluator or inherited assertion override
  will replace that acceptance. Decompilations are cached in artifacts/runtime-specification-source.
- TPC is existing locally feasible DEF-07 work. The total EF10 goal remains unchanged; completing this module
  does not close runtime specifications, native validation, sequence strategies or release gates.
- RED: artifacts/tpc-schema-red.log reproduces the blanket rejection. EF's TPC integer convention is
  ValueGeneratedNever; negative identity/default fixtures now explicitly configure OnAdd rather than assuming
  the ordinary single-table convention. Earlier schema/migration negatives were corrected in the same way.
- Root cause: GetDefaultValue returns Guid.Empty even without a configured default. TryGetDefaultValue
  distinguishes client Guid generation from configured default values, including Guid.Empty itself.
- RED: artifacts/tpc-mixed-red.log shows a mixed save executing AAAOrdinary before rejecting a later TPC
  identity. Ruling: validate the complete model once in the scoped batch factory, before any batch/connection.
  Unsupported key strategies consequently reject tracked writes for unrelated entities in that model too.
- GREEN: artifacts/tpc-focused-green.log passes 14 schema and 54 runtime cases, including concrete FK/index
  targets, composite CRUD, current filters, three tracking modes, original NULL/non-NULL concurrency binds,
  mixed-save pre-execution rejection, Guid generation and automatic transaction failure cleanup.
- Package RED: artifacts/tpc-old-package-red.log runs the expanded public consumer against the preceding
  scaffolding r2 package and fails specifically on its blanket TPC create-script rejection.
- Independent module review found two real guard bypasses: computed keys explicitly marked Never, and
  OnAdd Guid keys converted to int/long (physical identity detection uses provider type). Both reproduced
  independently in both modes; artifacts/tpc-review-red.log records eight failing regressions before fixes.
  Reject computed expressions outside both allowed paths and reject integer-converted client Guids.
  artifacts/tpc-reviewed-green.log passes 18 schema and 58 runtime cases; follow-up review finds no blockers.
- Final Debug/Release at EF10.0.0 and isolated EF10.0.14 Release each pass 4704 tests: 2215 unit, 1619
  functional, 868 inherited metadata specifications and two consumers. Zero failures/skips. Logs:
  artifacts/tpc-final-debug.log, artifacts/tpc-final-release.log, artifacts/tpc-final-patch.log.
- Final package: artifacts/tpc-package/OceanBase.EntityFrameworkCore.10.0.0-tpc-r2-20261113.nupkg, packed
  without rebuilding from the verified EF10.0.0 Release output. Verify-Package.ps1 passes net10.0, README,
  EF [10.0.0,11.0.0) ranges and Connector 0.3.0. No source/project/driver references enter consumers.
- artifacts/verify-tpc-r2-consumers.ps1 and artifacts/tpc-r2-consumers.log verify the same nupkg on EF10.0.0
  and EF10.0.14 in both modes: all public runtime smokes, TPC schema/model differences, UNION materialization,
  concrete CRUD and unsafe-identity rejection. Package-only graphs and provider DLL hashes match.
- Full Verify-Tooling.ps1 passes both modes on both patches, including public-GetSchema generated contexts,
  compiled initial/evolved snapshots, pending-model checks/listing, forward/reverse/idempotent scripts,
  guarded removal and four Windows bundle build/help checks. TPC adds no identities (four existing physical
  identities remain), snapshots retain UseTpcMappingStrategy, and inherited TpcRevision evolves all three tables.
  Logs: artifacts/tpc-r2-tooling-10.0.0.log and artifacts/tpc-r2-tooling-10.0.14.log. Workspaces:
  artifacts/tooling-10.0.0-19dcbc858b55413f92732f8bfd1024b2 and
  artifacts/tooling-10.0.14-2133767a59fd46bfbf1efc5cabbe4815.
- Source Release/archive/runtime/tooling provider SHA256:
  96994067E0D81654855ABE964FC181562E37AF3F88A5C2955392B1707B1FEE2E.
  R1 evidence is superseded by this reviewed r2 package. Baseline source assets remain EF10.0.0; selected
  patch builds stay isolated. No live probe repeat, SQL/DDL, push, tag or publication. No additional driver
  capability is required by TPC. Native validation and the overall EF10 goal remain open.
