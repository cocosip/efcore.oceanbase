# EF10 Reverse Engineering Implementation Plan

**Goal:** Complete the both-mode local database-to-generated-context workflow as one reviewed feature.
**Base:** 102e6e8, efcore/10.0. **Spec:** [scaffolding design](efcore-10-scaffolding-design.md).
**Architecture:** Public Connector 0.3.0 GetSchema metadata APIs and EF10 relational scaffolding/code generation.
Consume typed declaration and tri-state generation/default/update fields; do not query the column dictionary
or reconstruct declarations/defaults in the provider. Keep the three runtime package dependencies.
Execute inline under the continuing development authorization; EF8/9, push and publication stay out of scope.

## Tasks

### Connector 0.3.0 adaptation checkpoint

- [x] Update the connector dependency and package-content expectation to 0.3.0; restore from the supplied
  release nupkg if public NuGet is unavailable, recording package provenance.
- [x] RED: typed 23-column GetSchema fixtures, unknown/present-but-incomplete/contradictory behavior states,
  legacy missing fields, exact declarations/default SQL and no provider column-directory commands.
- [x] GREEN: consume GetSchema("Columns") directly, preserve available behavior for selected columns,
  warn for unavailable behavior, reject known unsupported ON UPDATE, preserve diagnostics.
- [x] Run focused and complete source suites, selected EF patch and package-generated-context checks;
  document remaining driver/server evidence gaps without claiming complete reverse engineering.

The driver API now expresses the required facts and unknown states. Its current implementation still leaves
MySQL absent/NULL defaults and string/binary defaults incomplete, and Oracle identity/ordinary generation/
ON UPDATE facts incomplete on the inspected server baseline. These shapes receive explicit review warnings
rather than wholesale rejection. Fully populated fixtures test consumer
behavior only; they are not claims that 0.3.0 produces complete
metadata for every table. Oracle identity mode/options are not part of the 0.3.0 contract.

### Complete reverse-engineering module

1. [x] Add RED discovery/context-free services and metadata-to-model tests, including realistic per-mode
   catalog shapes, ordered composite keys/indexes/FKs, views, generation, filters and failure ownership.
2. [x] Implement Design/OceanBaseDatabaseModelFactory.cs with mode validation, bounded catalog reads and
   complete supported metadata assembly; preserve unsafe/unsupported boundaries explicitly.
3. [x] Implement provider code generation and context-free design mappings, reusing existing runtime scalar
   mappings. Prove real EF reverse-engineered C# output and named/explicit mode behavior.
4. [x] Extend packed tooling with database reverse-engineering fixtures and compiled generated contexts.
   Negative old package must fail at a missing feature, not an unrelated build error.
5. [x] Run full Debug/Release, isolated EF10.0.14, package/content/runtime/tooling and generated-code matrix;
   independently review the complete module, resolve findings, update current boards and commit.

## Review focus

No mutable global mode; no credentials in source/artifacts; caller ownership and failed-open cleanup; precise
Oracle NUMBER/DATE vs MySQL scalar carriers; unknown metadata stays visible with warnings and write/DDL review;
quoted table selection and schema binding; foreign-key column order and excluded principals; generated identity
does not become an assigned key; compile generated contexts through the actual package-only code path.

## Evidence

- Connector release source: master 6de85de, schema implementation 7f0cf7e. Used the supplied local release
  artifact `TestResults/packages-0.3.0/OceanBaseConnector.0.3.0.nupkg` from the driver checkout;
  SHA256 `4C6B6570D10D1DEB7386C1A39C3DF7FD1183E222D296DF7176F760BF48AF519A`.
  Public NuGet lookup was unavailable under the current Windows TLS execution context. No certificate
  checks were bypassed and no global SDK/tool/package-cache update was required; restore used repository artifacts.
- RED: the converted public Columns fixture rejects all provider directory-reader commands; 38/48 focused
  cases initially failed. After adaptation and real EF warning-pipeline checks, 52 cases passed. An empty-comment
  package failure added two cases; preserve nonempty text and treat an empty directory comment as no model comment.
- Independent whole-module review found Oracle unzoned TIMESTAMP and CHAR BYTE/CHAR declarations routed to
  MySQL in context-free mappings. Four new real EF model cases all failed, then passed after the shared
  dispatch correction. Focused suite now passes 58. No additional actionable review finding.
- Final source: EF10.0.0 Debug and Release and isolated EF10.0.14 Release each pass 4562 tests (2193 unit,
  1499 functional, 868 metadata specification, two consumers), zero failures/skips. Focused reverse engineering:
  58 pass. Logs: `artifacts/scaffolding030-final-debug.log`, `scaffolding030-final-release.log` and
  `scaffolding030-final-patch.log`. Isolated patch props/build live under `artifacts/scaffolding030-ef10.0.14`.
- Package: `10.0.0-scaffolding-ob030-r2-20261113`, under `artifacts/scaffolding030-package`, passes content
  verification and package-only runtime consumers on EF10.0.0/10.0.14 in both modes. Logs:
  `artifacts/scaffolding030-runtime-10.0.0.log` and `scaffolding030-runtime-10.0.14.log`.
- Full packed tooling passes both selected patches: complete and documented-driver metadata profiles generate
  C#, compile DbContexts and verify model/query behavior in both modes; complete profiles also verify DDL.
  Migrations/snapshots/evolution/model checks/scripts/list/removal and all four Windows bundle build/help checks
  pass. Logs: `artifacts/scaffolding030-tooling-base.log` and `scaffolding030-tooling-patch.log`.
- Package-only assets resolve Connector 0.3.0 and the selected EF patch. Source Release, archive, package cache,
  runtime and both CLI consumer Provider DLLs match SHA256
  `0FD5A9D83F87905AB7B21C97CBAEC0D1CF81BAA4EEBE7244BF3F2D54197D3492`.
  CLI workspaces: `artifacts/tooling-10.0.0-c381d64b42d84543860b9080c73d5d97` and
  `artifacts/tooling-10.0.14-f979e9254ca843c99d5f62674048dd24`.
- Previous splitting package fails the new smoke at missing IDatabaseModelFactory registration, proving the
  workflow is a new feature (`artifacts/scaffolding-old-package.log`). No live SQL/catalog, lossless DDL,
  EF8/9, push/publication or overall EF10 completion is claimed. Native catalogs and wider relational
  specification/graph/recovery modules remain follow-up work.

## Repeatable local checks

Use repository-local package caches when the global cache is not writable; restore Connector from
`artifacts/connector030-feed` and other dependencies from the existing NuGet cache. After restore, run:

```powershell
dotnet test OceanBase.EntityFrameworkCore.slnx -c Debug --no-restore -m:1 -nr:false
dotnet test OceanBase.EntityFrameworkCore.slnx -c Release --no-restore -m:1 -nr:false
dotnet test OceanBase.EntityFrameworkCore.slnx -c Release --no-restore -m:1 -nr:false `
  -p:DirectoryPackagesPropsPath=D:/Code/dotnet/efcore.oceanbase/artifacts/scaffolding030-ef10.0.14/Directory.Packages.props `
  -p:UseArtifactsOutput=true -p:ArtifactsPath=D:/Code/dotnet/efcore.oceanbase/artifacts/scaffolding030-ef10.0.14/build
pwsh -NoProfile -File tests/OceanBase.EntityFrameworkCore.PackageConsumer/Verify-Package.ps1 `
  -PackagePath artifacts/scaffolding030-package/OceanBase.EntityFrameworkCore.10.0.0-scaffolding-ob030-r2-20261113.nupkg `
  -ExpectedVersion 10.0.0-scaffolding-ob030-r2-20261113
foreach ($efPatch in @('10.0.0', '10.0.14')) {
  pwsh -NoProfile -File tests/OceanBase.EntityFrameworkCore.ToolingConsumer/Verify-Tooling.ps1 `
    -ProviderVersion 10.0.0-scaffolding-ob030-r2-20261113 -EfCoreVersion $efPatch `
    -PackageFeed D:/Code/dotnet/efcore.oceanbase/artifacts/scaffolding030-package
}
```

Restore the isolated patch with the same props/output arguments before its no-restore test command.
The recorded tooling runs did not use SkipBundle. Results apply to the feature source based on 102e6e8;
the feature commit contains this record and the updated current boards.
