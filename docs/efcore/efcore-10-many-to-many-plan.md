# EF10 Many-to-Many Implementation Plan

**Goal:** Verify and complete both-mode skip-navigation schema/query/write behavior as one feature module.
**Base:** 1f35e12. **Spec:** [many-to-many design](efcore-10-many-to-many-design.md).
**Execution:** Inline, using existing EF/provider services and focused RED/GREEN for reproduced defects.

## Files and responsibilities

- FunctionalTests/ManyToManyRuntimeTests.cs: public query/write pipeline, deterministic readers, ordinary
  implicit joins and explicit payload/concurrency, transaction failure/recovery; reuse TransactionTests doubles.
- UnitTests/ManyToManySchemaTests.cs: creation and real model-difference contracts including generated keys
  and explicit join table/key/index/FK profiles. Reuse existing schema/migration helpers where possible.
- PackageConsumer/Program.cs: package-only public smoke, following the current consumer structure.
- Existing production services: edit only the owning shared implementation when a regression proves a defect.
- Current progress/checklist/roadmap/local plan and README if support changes: record exact evidence/limits.

## Tasks

1. [x] Verify implicit/explicit schema and migration differences; run focused characterization first.
2. [x] Verify predicates, Include/filtered Include, inverse fixup, shared identities, empty/NULL and filter
   refresh through real sync/async readers in both modes and all tracking modes.
3. [x] Verify generated key propagation, link-only addition/removal, payload/concurrency updates, principal
   deletion ordering and multi-command owned/caller transaction failure/recovery. Fix reproduced gaps with RED/GREEN.
4. [x] Add package-only smoke; run full Debug/Release and isolated EF10.0.14. When production changes, pack
   one baseline binary and run both patches/modes plus full CLI/bundle compatibility.
5. [x] Review module, resolve actionable findings with regressions, update boards and commit locally.

## Evidence

- Focused: 4 schema and 62 functional cases pass, including all tracking modes for implicit and explicit
  Includes, sync/async, both modes. Initial schema characterization passed without production changes.
  Fixture corrections addressed expected no-tracking projection ordering and Oracle named-bind registration
  order; these were test mistakes, not provider defects or feature RED evidence.
- Independent review found two P2 verification gaps. Pin the exact tenant/effective filtered-Include predicates
  and explicit join conditions; map nonconventional CourseRef/StudentRef join columns and assert each migration
  FK's target/columns/cascade plus supporting index and SQL. Both findings are fixed. Explicit join Include now
  covers all three tracking modes, resolving the review's narrower-evidence note. Native results, split queries,
  lazy loading, TPC, complex payloads, EF8/9, publication and retry after owned-transaction disposal were
  considered outside this module; no production implementation is needed for the verified ordinary profile.
- Expanded package-only consumers pass against existing `10.0.0-scaffolding-ob030-r2-20261113` on EF10.0.0
  and EF10.0.14, both modes, sync/async. Logs: `artifacts/many-to-many-consumers.log`; workspaces:
  `artifacts/many-to-many-runtime-10.0.0` and `artifacts/many-to-many-runtime-10.0.14`.
  Their package graphs select Connector 0.3.0 and the selected EF patch with no project dependency. Provider
  DLL SHA256 remains `0FD5A9D83F87905AB7B21C97CBAEC0D1CF81BAA4EEBE7244BF3F2D54197D3492`.
- No production file or package metadata changed. Repacking and repeating unchanged full CLI/bundle checks
  would not verify a new runtime binary; the scaffolding module's existing matrix remains its evidence.
- Final source Debug/Release at EF10.0.0 and isolated EF10.0.14 Release each pass 4628 tests (2197 unit,
  1561 functional, 868 upstream metadata specification, two project consumers), zero failures/skips.
  Logs: `artifacts/many-to-many-final-debug.log`, `many-to-many-final-release.log` and
  `many-to-many-final-patch.log`. No overall EF10/release acceptance is claimed.
- Refreshed the existing opt-in LiveTests restore to Connector 0.3.0 and ran both User Secrets-backed
  read-only probes. Both fail during OpenAsync with `Connection closed while reading the packet`; neither
  SELECT nor any DDL ran. Log: `artifacts/many-to-many-live-probe.log`. This does not identify a connector
  defect or establish native many-to-many acceptance; endpoint/protocol/transport diagnosis remains separate.

## Repeatable checks

```powershell
dotnet test OceanBase.EntityFrameworkCore.slnx -c Debug --no-restore -m:1 -nr:false
dotnet test OceanBase.EntityFrameworkCore.slnx -c Release --no-restore -m:1 -nr:false
```

For the isolated EF10.0.14 run, use the same props/output arguments and prepared package cache documented
in the [scaffolding plan](efcore-10-scaffolding-plan.md). Expanded packed consumers copy the PackageConsumer
project and all its C# files to each isolated workspace, restore against `artifacts/scaffolding030-package`
and the existing NuGet cache with OceanBaseProviderVersion set to the scaffolding r2 version and
OceanBaseConsumerEfCoreVersion set to the selected patch, then run the project. The exact local orchestration
is saved at `artifacts/verify-many-to-many-consumers.ps1`.
