# EF Core 10 Migrations Tooling Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans inline and a fresh independent final module review.

**Goal:** Deliver the complete packed-provider migrations authoring/script/bundle workflow in both modes.
**Architecture:** Public relational configuration facade plus EF's existing design-service builder/generators;
package-only CLI verification. Keep the existing runtime package dependency graph.
**Tech Stack:** C#, net10.0, EF Core/Relational [10.0.0,11.0.0), EF Design in test consumer, PowerShell7, xUnit.
**Spec:** efcore-10-migrations-tooling-design.md; DEF-01/02, CFG-04, PKG-03/04.

## Global constraints

Continue in the existing efcore/10.0 checkout from a8cbccf. Preserve old UseOceanBase overloads. No runtime
dependency, global tool mutation, real database writes, EF8/EF9, push or publication. English repository docs.
Commit the whole reviewed module, not each overload or registration.

## Review focus

Binary/API compatibility; cloning that preserves ownership/mode/scope; design-service lifetimes and discovery;
generated snapshots retaining identities/converters; offline test guard and force-remove semantics; package
consumers that accidentally use source references or a different DLL; bundles that silently capture secrets.

## Task 1: Configuration and design-time scaffolding

Files: Infrastructure/OceanBaseDbContextOptionsBuilder.cs, OceanBaseDbContextOptionsBuilderExtensions.cs,
Design/OceanBaseDesignTimeServices.cs, UnitTests/MigrationsToolingTests.cs, Directory.Packages.props/test csproj.

- [x] Write failing tests for actual design-service discovery/scaffolding and callback configuration contracts.
- [x] Observe RED, add the facade/action overloads and EF relational design-service registration.
- [x] Verify real initial/evolved model scaffolding and options/history boundaries through GREEN.

## Task 2: Packed CLI workflow

Files: tests/OceanBase.EntityFrameworkCore.ToolingConsumer/{csproj,Program.cs,Verify-Tooling.ps1}.

- [x] Add a packed-only, offline-guarded two-mode design-time context factory and deterministic model stages.
- [x] Verify the old package fails the feature, then pack a unique version and run both-mode/patch CLI matrix.
- [x] Compile initial/evolution snapshots; check unchanged/evolved pending states; generate all scripts; list,
  remove with guarded EF --force semantics; build bundles and run help only.

## Task 3: Validation, review and commit

- [x] Full Debug/Release and isolated EF10.0.14 source suites, package-content and runtime/tooling consumers.
- [x] Independent fresh-context whole-module source review; fix material findings and reverify.
- [x] Update checklist, progress, roadmap/design/README; diff checks and one feature commit.

## Execution record

- Baseline: a8cbccf, clean checkout; migrations core's preceding matrix passes 4367 per configuration.
- Ruling: finish the missing CLI caller/configuration seam before attempting database-backed runtime
  specifications; the current live connection blocker does not prevent local authoring/bundle work.
- Ruling: preserve all three-argument UseOceanBase overloads and use a small facade that hides the internal
  options extension instead of exposing it publicly through a generic relational builder base.
- RED: all 11 original discovery/scaffolder/options tests failed (`artifacts/tooling-red.log`); the old
  `10.0.0-migrations-r2-20261113` package fails the new consumer at the missing callback overload
  (`artifacts/tooling-package-red.log`). Final focused coverage passes 21 cases, including resets, invalid
  history/assembly names, separate migrations assembly resolution, all overloads and namespace factories.
- EF10.0.0 Debug/Release and isolated EF10.0.14 Release each pass **4388 tests**: 2111 unit, 1407 intercepted
  functional, 868 metadata specification and 2 project consumers; zero failures/skips, warnings as errors.
  The selected-patch override includes all Microsoft.EntityFrameworkCore* packages, including Design and
  Specification.Tests, and has separate artifacts. Baseline assets remain EF10.0.0.
- Final package `10.0.0-tooling-r2-20261113` passes content checks and ordinary runtime consumers in both modes
  on both patches. The provider still has exactly three runtime dependencies; EF Design is consumer/test only.
  Source/package/runtime/tooling DLL identity and package-only graphs are checked.
  The shared provider DLL SHA256 is `8228A4F747C39ADB20D0FF06B88901D4E4A34A6B088E3CCC7C14E7568BA3E482`.
- The installed **dotnet-ef 10.0.15** passes the full CLI workflow with runtime/Design **10.0.0 and 10.0.14**,
  both modes: initial/evolved migrations and compiled snapshots, unchanged/pending states, listing,
  upgrade/reverse/idempotent scripts, guarded force-removal, restored snapshot, and four win-x64
  framework-dependent bundles that start with `--help`. No bundle was applied and no database was opened.
- Bundle prerequisite diagnosis: EF's temporary project did not discover the fixture NuGet.Config; the
  verifier now passes RestoreConfigFile and the local package cache to child builds and restores their prior
  environment afterward. Three missing official 10.0.14 Windows runtime packs were downloaded from NuGet's
  HTTPS flat-container into the ignored local feed. No global SDK/tool/package configuration changed.
- Independent read-only whole-module review found no source blockers. Its pending CLI/suite boundaries are
  closed by the final runs above; native execution, reverse engineering and the overall EF10 goal remain open.
  A final follow-up reviewed the updated verifier/docs and read all passing logs; verdict: ready to commit.
- Evidence: `artifacts/tooling-{debug,release,ef10.0.14}.log`, `tooling-boundaries.log`,
  `tooling-package-final.log`, `tooling-runtime-{10.0.0,10.0.14}-final.log`,
  `tooling-cli-{10.0.0,10.0.14}-final.log`. Detailed generated artifacts and per-command logs:
  `artifacts/tooling-10.0.0-dd42776beab8425bb92512c9b0456d81/` and
  `artifacts/tooling-10.0.14-60891a4ddf0f4a9cbc8e9bb82b7d465b/`.

### Repeatable commands

```powershell
dotnet test OceanBase.EntityFrameworkCore.slnx --no-restore -m:1 -nr:false
dotnet test OceanBase.EntityFrameworkCore.slnx -c Release --no-restore -m:1 -nr:false
dotnet pack src/OceanBase.EntityFrameworkCore -c Release --no-restore `
    -p:PackageVersion=10.0.0-tooling-r2-20261113 -o artifacts/tooling-package -m:1 -nr:false
pwsh -NoProfile -File tests/OceanBase.EntityFrameworkCore.PackageConsumer/Verify-Package.ps1 `
    -PackagePath artifacts/tooling-package/OceanBase.EntityFrameworkCore.10.0.0-tooling-r2-20261113.nupkg `
    -ExpectedVersion 10.0.0-tooling-r2-20261113
foreach ($toolingPatch in @('10.0.0', '10.0.14')) {
    pwsh -NoProfile -File tests/OceanBase.EntityFrameworkCore.ToolingConsumer/Verify-Tooling.ps1 `
        -ProviderVersion 10.0.0-tooling-r2-20261113 -EfCoreVersion $toolingPatch `
        -PackageFeed artifacts/tooling-package
}
```

The verifier uses the supplied local feed and the user's existing NuGet cache as sources. They must contain
the selected EF/Design dependencies and SDK-required runtime packs. It fails on missing prerequisites;
`-SkipBundle` is a troubleshooting aid and is not the module acceptance command. For isolated patch and runtime
consumer commands, follow the preceding migrations plan, including Design in the central override.
