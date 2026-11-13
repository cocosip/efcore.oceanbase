# EF10 Splitting Execution Record

**Base:** 7fde2f1. **Branch:** efcore/10.0. **Design:** [splitting design](efcore-10-splitting-design.md).
Execute inline under the continuing complete-feature development request. EF8/9 remain postponed.

1. [x] Add RED schema/model-difference tests for required fragments and shared rows, including key strategies,
   table-specific key names, schema identity, defaults/computed values and unsupported generation.
2. [x] Generalize physical key propagation and admit entity splitting in both schema paths; preserve TPT/TPC guards.
3. [x] Verify both-mode sync/async graph queries and CRUD, shared concurrency/optional dependents, transactions,
   failed fragment writes and concurrency failures using EF's existing pipeline.
4. [x] Extend package-only runtime and actual CLI fixtures. Verify the previous package rejects the new feature.
5. [x] Run Debug/Release, isolated EF10.0.14, unique package/content/runtime/CLI/bundle matrix and DLL identity checks.
6. [x] Update acceptance/current boards and independently review the complete module; resolve findings before commit.

## Execution evidence

- RED: eight of the first ten schema cases fail against 7fde2f1: four at incorrect fragment identity annotations
  and four at explicit entity-splitting rejection. The two unsupported-generation cases already pass
  (`artifacts/splitting-schema-red.log`). Shared-row tests separately reproduce create-script rejection of EF10
  `OnUpdateSometimes` (`splitting-shared-schema-red.log`); source tracing finds the same guard in migrations.
  EF's actual `ForUpdate()` tests the OnUpdate bit, which OnUpdateSometimes alone does not contain. Both actual
  unbacked generation and TPC remain rejected. Final new schema cases: 12 pass.
- Runtime: 52 cases pass for both modes and sync/async, with three tracking modes for fragment Include and
  optional shared dependents. Coverage includes parameter/filter refresh, NULL/empty rows, primary identity
  propagation to two renamed fragment keys, owning-table updates, reverse deletion, automatic transactions,
  fragment write/concurrency failure, shared inserts/merged updates/dependent removal/reintroduction/deletion,
  shared application tokens and generated token readback/snapshots for both entities. Fixtures retain the
  provider batch executor/transaction factory but replace the physical connection; native atomicity is unproven.
- Fixture corrections followed actual EF behavior: entity projection order follows property metadata rather
  than physical table order; application-managed shared tokens must be changed consistently on both entities.
  Generated shared tokens have separate readback/propagation cases. No custom query/update pipeline was added.
- Full EF10.0.0 Debug and Release and isolated EF10.0.14 Release each pass **4504** (2135 unit, 1499 functional,
  868 inherited metadata specification and two project consumers), zero failures/skips. The isolated central
  override selects every Microsoft.EntityFrameworkCore* package, with separate build assets.
- Negative package consumer: `10.0.0-tpt-r2-20261113` fails the new smoke at entity-splitting rejection with
  ordinary exit1, not a compilation failure. `10.0.0-splitting-r1-20261113` passes content checks and standalone
  runtime consumers for both modes on EF10.0.0/10.0.14. Graphs remain package-only, runtime has no EF Design.
- Actual CLI on installed dotnet-ef10.0.15, selected runtime/Design10.0.0/10.0.14: initial/evolved migrations,
  compiled snapshots retaining SplitToTable, exactly four identities (ordinary/TPT root/split root/shared row),
  pending model checks, list, forward/reverse/idempotent scripts, fragment column evolution, guarded removal
  and all four win-x64 framework-dependent bundle build/help checks pass. No bundle or script is applied.
- Source/archive/runtime/both tooling provider DLL SHA256:
  `8E96132C10778356B453E5F70103E1DD74CD69B106169302534561CB8BD33B14`.
- Fresh independent read-only whole-module review found no critical/important/minor findings. It inspected
  owning conventions/schema/migrations/update/transaction code and EF10's actual shared-column implementation.
  Final independent evidence/docs follow-up confirms all three suites, both runtime/CLI matrices, four bundle
  help artifacts, splitting snapshots, packaged README and matching DLL hashes. Verdict: ready to commit.
- Existing User Secrets were reused for both opt-in read-only probes. Both still fail during handshake with
  `Connection closed while reading the packet` before authentication/SQL. No DDL or test-owned namespace was
  created. Native splitting joins/identities/shared affected counts/rollback and the overall EF10 goal remain open.

## Repeatable verification and artifacts

Use the preceding TPT/tooling plans' source/patch/package commands with version
`10.0.0-splitting-r1-20261113`, feed `artifacts/splitting-package`, and isolated patch root
`artifacts/splitting-ef10.0.14`. Do not skip bundles or run database update/bundle application.

Logs: `artifacts/splitting-schema-{red,green}.log`, `splitting-shared-schema-red.log`, `splitting-focused.log`,
`splitting-{debug,release,ef10.0.14}.log`, `splitting-package-red.log`, `splitting-package-content.log`,
`splitting-runtime-{10.0.0,10.0.14}.log`, `splitting-cli-{10.0.0,10.0.14}.log`, `splitting-live-probes.log`.
CLI workspaces: `artifacts/tooling-10.0.0-7d12ccfe123c432b8a56227acc65b457/` and
`artifacts/tooling-10.0.14-a5238d0159c546488a440297d7dc3d26/`.
