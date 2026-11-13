# EF Core 10 Migrations Core Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans inline; perform an independent whole-module review before committing.

**Goal:** Implement the complete locally testable migrations core in both OceanBase modes as one reviewed feature commit.
**Architecture:** Extend EF10's migration services at SQL, history/lock and namespace validation seams. Reuse provider mappings.
**Tech Stack:** C#, net10.0, EF Core [10.0.0,11.0.0), connector 0.2.0, xUnit.
**Spec:** efcore-10-migrations-design.md; DEF-01, SCH-01/02, MOD-03, REL-02.

## Global constraints

Keep the current efcore/10.0 checkout; no EF8/9, push, tag or publication. No database DDL without an
authorized isolated cleanup scope. Never print User Secrets. Preserve all Required acceptance rows.
Commit the whole module after review and verification, rather than each operation/type.

## Review focus

Cross-schema foreign keys/history; implicit DDL commits and lock ownership; converted defaults and identity
metadata from real model differences; repeat/failed migrations and cancellation cleanup; hostile quoted
identifiers and script literals containing delimiters/backslashes.

## Task 1: Migration operations and model differences

Files: Migrations/OceanBaseMigrationsSqlGenerator.cs, Metadata/OceanBaseRelationalAnnotationProvider.cs,
Internal/OceanBaseOptionsExtension.cs, UnitTests/MigrationsSqlGeneratorTests.cs.

- [x] Add failing both-mode tests resolving IMigrationsSqlGenerator and generating a real model difference.
- [x] Implement dialect operations, identity annotations and explicit unsupported boundaries; test the
  complete create/evolve/drop flow, defaults, seed DML, constraints/indexes and altered-column safety.
- [x] Run focused tests through RED/GREEN and retain actual evidence.

## Task 2: History, locking, migration execution and scripts

Files: Migrations/OceanBaseHistoryRepository.cs, Migrations/OceanBaseMigrator.cs,
FunctionalTests/MigrationsTests.cs; extend unit tests for history/scripts.

- [x] Add failing tests for two migrations, forward/reverse/idempotent scripts and sync/async execution.
- [x] Implement the scoped history repository, owner-row lock and guarded target execution on EF's migrator.
- [x] Verify lock contention/release, repeat no-op, DDL/history ordering, failures/cancellation, ownership,
  no opt-in/cross-scope/ambient rejection, quoted names and script parser boundaries.

## Task 3: Package, docs, review and feature commit

Files: PackageConsumer/MigrationsSmoke.cs and Program.cs; provider README and EF10 progress/checklist/roadmap.

- [x] Add package-only migration service and forward/reverse/idempotent-script smoke checks in both modes.
- [x] Run full Debug/Release, isolated EF10.0.14 and same-package dual-mode/dual-patch consumers; verify package.
- [x] Run the existing opt-in read-only live probes with redacted evidence, without printing secrets or DDL.
- [x] Review the entire module independently; fix material findings and reverify affected checks.
- [x] Update docs with exact evidence and unresolved native/tooling boundaries, git diff --check, commit.

## Execution record

- Baseline: d2a2ecd; clean checkout; prior turn reran Debug/Release, 4301 tests each, zero failures/skips.
- Ruling: implement inline in the existing efcore/10.0 checkout as required by the repository's active plan.
- Ruling: use a durable owner-row migration lock; native advisory-lock availability has not been established,
  and a transaction-held row lock on the DDL session would be released by implicit commits.
- RED evidence: initial operation tests failed 10/12; runtime tests failed 14/18. Added regressions separately
  exposed unsupported models/default-schema seed scope, bootstrap/claim cleanup, native history terminators,
  retry strategies, literal defaults, annotation handling, commented raw SQL and cross-scope DropColumn.
- Final new coverage: 25 unit and 41 intercepted functional cases, including matching/no-op/failing seeders.
- Full source verification after restoring the committed central dependencies: EF10.0.0 Debug and Release,
  plus isolated EF10.0.14 Release, each pass **4367 tests**: 2090 unit, 1407 functional, 868 specification,
  two project consumers; zero failures/skips. All EF packages including Specification.Tests resolve to the
  selected isolated patch. Baseline assets remain EF10.0.0. No dependency file changed.
- Full-suite fixture correction: new migrations contexts disable service-provider caching, matching existing
  test fixtures; this prevents synthetic service combinations from exhausting EF's global cache warning limit.
  The initial failing full run was not accepted as a pass; all three final suites above passed afterward.
- Package `10.0.0-migrations-r2-20261113` passes Verify-Package.ps1 and standalone consumers in both modes
  on EF10.0.0/10.0.14. Assets contain package dependencies only; source Release, nupkg and consumer provider
  DLL SHA256 hashes match. The same baseline-built package is used for both patches.
- Independent read-only whole-module review and final re-review found no remaining actionable source blockers.
  Review fixes cover scope, history, ownership/cleanup, SQL/default/model and script/client boundaries.
- Existing User Secrets were loaded by the opt-in read-only live project. Both probes failed during the
  handshake before authentication/SQL with `Connection closed while reading the packet`; no SQL/DDL ran.
  This is an unresolved environment/transport boundary, not a demonstrated client defect.
- Final logs (ignored): `artifacts/migrations-debug-final.log`, `migrations-release-final.log`,
  `migrations-ef10.0.14.log`, `migrations-consumer-10.0.0.log`, `migrations-consumer-10.0.14.log`,
  `migrations-live-redacted.log`. Native DDL/concurrency/script clients and CLI scaffolding/bundles remain open.

### Repeatable commands

```powershell
dotnet restore OceanBase.EntityFrameworkCore.slnx --disable-parallel
dotnet test OceanBase.EntityFrameworkCore.slnx --no-restore -m:1 -nr:false
dotnet test OceanBase.EntityFrameworkCore.slnx -c Release --no-restore -m:1 -nr:false
```

For the isolated patch matrix, copy Directory.Packages.props under artifacts and set every
Microsoft.EntityFrameworkCore* PackageVersion (including the specification package) to `[10.0.14]`; pass
DirectoryPackagesPropsPath and a separate `--artifacts-path`. Do not reuse baseline obj assets for the patch.
Follow the package-consumer commands in efcore-10-query-update-design.md with the version/feed above; run
Verify-Package.ps1, inspect package-only assets and compare DLL hashes. Use a new package version after changes.
Native probes require OCEANBASE_RUN_LIVE_TESTS=1; never echo connection strings or unredacted error output.
