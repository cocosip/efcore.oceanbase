# EF10 Model-Managed Schema Data Implementation Plan

**Goal:** Enable HasData in create scripts, CreateTables and EnsureCreated for both modes.
**Spec:** [design](efcore-10-model-data-design.md). **Base:** fcd20a2. **Execution:** inline, one module.
**Architecture:** Existing DDL plus EF model-difference/provider migration seed SQL; owned seed transaction.
**Tech stack:** .NET/EF10, Connector 0.3.0, existing xUnit/native doubles and package/tooling consumers.

## Steps

1. [x] Add ModelDataTests.cs: script and actual lifecycle tests for ordered/conversion/owned/excluded data,
   callbacks, existing schemas and explicit CreateTables. Record RED at the current HasData guard.
2. [x] Remove only the HasData DDL guard. Build a schema/data plan in DatabaseCreator using EF's ordered
   InsertDataOperations and provider migration SQL. Execute data after DDL in an owned transaction with
   scope recheck, explicit rollback on failure and primary-error-preserving disposal; async cleanup is
   uncancelled. Retain callback semantics. Add failure/cancellation/lease/negative regressions and GREEN.
3. [x] Add package-only public model-data smoke; independently review the whole module and fix Important
   findings. Run full Debug/Release/isolated patch, content/runtime/affected tooling gates and exact graphs.
4. [x] Update usage/progress/checklist/roadmap and evidence; inspect diff, commit locally, verify clean tree.

## Review focus

- Principals and shared owned rows must use EF ordering/conversion, not entity/table alphabetical order.
- No model data reaches excluded/cross-scope tables or existing populated schemas; all validation precedes DDL.
- DDL is outside seed transactions; callback/model-data phases and failure recovery must be explicit.
- Cancelled/failed inserts must roll back with no replay; rollback/disposal failures preserve the cause.
- Script/native terminators, timeout/tokens and caller-open leases must retain both-mode behavior.

## Evidence

- Clean base and owning source inspected. At fcd20a2, SchemaSqlGenerator rejected HasData while migration seed
  SQL already existed. EF10 model differ supplies dependency-ordered inserts; creation-table filtering excludes
  externally managed tables.

- RED: artifacts/model-data-red.log records ten failures at the prior HasData guard. Initial GREEN is
  artifacts/model-data-initial-green.log; excluded-table filtering fixes the initial over-inclusive seed
  plan. Expanded lifecycle GREEN is artifacts/model-data-expanded.log (22 focused cases).
- Independent whole-module review reproduced combined rollback/disposal failure leaving CurrentTransaction
  and a transaction-owned connection lease attached. Four caller-open regression cases fail in
  artifacts/model-data-cleanup-red.log. Detaching the abandoned transaction and balancing only its lease
  preserves the original failure and caller ownership; artifacts/model-data-review-green.log passes all
  22 cases. Final review has no outstanding actionable findings.
- Full-suite checks exposed two obsolete negative cases in DatabaseManagementTests. Replace the old
  HasData profile with unsupported sequences, retaining pre-open rejection assertions. SchemaScriptTests
  drops one obsolete seed rejection case; the new positive/failure coverage replaces that limitation.
- Final baseline Debug/Release and isolated EF10.0.14 Release each pass **5028 tests**: 2313 unit,
  1845 functional, 868 metadata specification and two consumers; zero failures/skips, all commands exit 0.
  Logs: artifacts/model-data-final-debug.log, model-data-final-release.log, model-data-final-patch.log.

  ```powershell
  dotnet test OceanBase.EntityFrameworkCore.slnx -c Debug --no-restore -m:1 -nr:false
  dotnet test OceanBase.EntityFrameworkCore.slnx -c Release --no-restore -m:1 -nr:false
  dotnet test OceanBase.EntityFrameworkCore.slnx -c Release --no-restore -m:1 -nr:false -p:DirectoryPackagesPropsPath=D:/Code/dotnet/efcore.oceanbase/artifacts/lazy-loading-ef10.0.14/Directory.Packages.props -p:UseArtifactsOutput=true -p:ArtifactsPath=D:/Code/dotnet/efcore.oceanbase/artifacts/lazy-loading-ef10.0.14/build
  ```

- Baseline-built artifacts/model-data-package/OceanBase.EntityFrameworkCore.10.0.0-model-data-20261113.nupkg
  passes Verify-Package.ps1 content/identity/net10.0/README/range/three-dependency checks. Both-mode public
  runtime consumers at EF/Proxies10.0.0 and 10.0.14 pass all previous smokes plus HasData script/transaction/
  existing-schema tests. Command: artifacts/verify-model-data-consumers.ps1; log: artifacts/model-data-consumers.log.
  No runtime Design or project references; Connector 0.3.0 and selected EF/Proxies patches verified.
- Full Verify-Tooling.ps1 runs with -ProviderVersion 10.0.0-model-data-20261113 -PackageFeed artifacts/model-data-package
  and -EfCoreVersion 10.0.0 / 10.0.14 both pass generated catalogs/contexts, optimize/compiled use, migration
  discovery/snapshots/evolution/pending-model/scripts/list/removal and four Windows bundle build/help checks.
  Logs: artifacts/model-data-tooling-10.0.0.log and model-data-tooling-10.0.14.log. Scratch roots:
  artifacts/tooling-10.0.0-34f479eff8464c92b3fb68b50a4786a2 and
  artifacts/tooling-10.0.14-5f9bcbac7a7f420fac293eb39be673ed. Tooling CLI is 10.0.15; Design/runtime use
  the selected patches. Both tooling graphs contain packages only, with Connector 0.3.0.
- Source Release DLL, nupkg DLL and all four runtime/tooling consumer DLLs match SHA256
  AC5BB9E13F9DCAC0BED16C3BA4998E1E6BF22BFE4F61A45C82D07E4590B4164D.

## Paused handoff

This module is committed locally on efcore/10.0 after fcd20a2; locate its commit by this plan and the
subject `feat(schema): apply model-managed data during schema creation`. At the user's request, stop
after wrap-up. The overall goal remains paused and incomplete. Resume one coherent remaining module
from the checklist/local plan only when requested. Native DDL/seed transactions, server identity behavior,
wider local/specification coverage and release acceptance remain open. No new live probe, push or publication.
