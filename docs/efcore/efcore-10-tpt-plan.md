# EF Core 10 TPT Implementation Plan

**Goal:** Complete the provider's local TPT schema/query/write/tooling workflow in both modes.
**Spec:** [efcore-10-tpt-design.md](efcore-10-tpt-design.md).
**Baseline:** efcore/10.0, 0766d38, clean worktree. Continue inline; no EF8/9, push or publication.

## Review focus

Physical table vs property generation; copied/default/client keys; inherited-key FK order and scoping; model
evolution snapshots; actual query column order/discriminator shaping; native transaction attachment and failure
cleanup; fixtures that could hide a broken generator or bypass automatic transactions.

## Tasks

1. [x] Add failing TPT create/model-difference tests; observe blanket rejection. Cover root/derived identities,
   int/long/Guid/composite keys, hierarchy depth/siblings and remaining unsupported generation/model shapes.
2. [x] Reuse one physical-table identity classifier; admit TPT and copied generated keys in both schema paths.
   Run focused schema/migration regressions.
3. [x] Verify complete hierarchy query/materialization and multi-table tracked CRUD/failure/transaction behavior
   through registered provider services in both modes, sync/async. Fix exposed provider gaps at their owner.
4. [x] Extend packed runtime and CLI fixtures with TPT; verify old package fails, then dual-patch/mode consumers,
   snapshots/scripts/bundles, full Debug/Release/isolated patch suites and unchanged package content gate.
5. [x] Fresh independent read-only whole-module review; resolve material findings. Update acceptance/progress/
   roadmap/README and commit the complete module once.

## Execution record

- Official metadata inheritance tests already exist; runtime evidence cannot be replaced by metadata passes.
- Reuse existing EF TPT services and execute inline in this checkout. Native schema writes remain outside the
  authorized live probe scope.
- RED: all 10 original TptSchemaTests fail against baseline, either at blanket TPT rejection or at derived
  identity annotations (`artifacts/tpt-schema-red.log`). Final 14 focused schema cases pass; additional cases
  cover same table names in distinct schemas and unbacked derived generated scalars. The existing negative
  flat TPT test was replaced by this positive workflow; TPC and split rejections remain.
- Runtime: 40 cases pass across both modes, sync/async and three tracking modes. They cover concrete hierarchy
  shaping/null/empty results, OfType/type predicates, current inherited filters and disabling, collection Include,
  key propagation, owning-table updates, reverse deletion, default automatic transaction attachment/commit and
  uncommitted disposal for derived write/concurrency failures. They reuse TransactionTests' native doubles;
  connector startup and physical atomicity are not claimed. The initial fixture corrected its reader column
  order and EF expectations: OfType uses LEFT JOIN plus key tests; SaveChanges counts physical commands.
- Full Debug/Release on EF10.0.0 and isolated EF10.0.14 Release each pass **4441 tests**: 2124 unit, 1447
  intercepted functional, 868 metadata specification and 2 project consumers, zero failures/skips. The patch
  override includes every Microsoft.EntityFrameworkCore* package (including Design and Specification.Tests)
  and separate assets; baseline provider assets remain EF10.0.0.
- The previous `10.0.0-tooling-r2-20261113` package fails the new runtime smoke at explicit TPT rejection
  (`artifacts/tpt-package-red.log`). Final `10.0.0-tpt-r2-20261113` passes content checks, runtime consumers and
  full CLI consumers in both modes on EF10.0.0/10.0.14. Provider runtime dependencies remain Core/Relational/
  Connector only; runtime consumers are package-only and do not reference Design.
- CLI: installed dotnet-ef 10.0.15, selected runtime/Design 10.0.0/10.0.14. Both modes scaffold a three-level
  TPT hierarchy, retain its strategy in compiled snapshots, preserve one root identity plus the ordinary fixture
  identity, add/drop a nullable leaf column, check pending models, list/remove migrations and emit forward/
  reverse/idempotent scripts. All four framework-dependent Windows bundles build/start with --help.
  MySQL multiline idempotent DDL is hex encoded; the verifier decodes literal text only for inspection.
- Source/archive/runtime/both tooling consumer provider DLL SHA256:
  `63AB5433B3F8E7E038B6B909908185FD0E733D20764CFC244B09FC93ED68B233`.
- Independent read-only whole-module review and final evidence/docs follow-up found no actionable defects;
  final verdict: ready to commit. Native DDL/queries/keys/FKs/rollback, connector transaction startup, TPC and entity splitting
  remain outside this local acceptance claim; the overall EF10 goal is still incomplete.
- Evidence: `artifacts/tpt-schema-{red,final}.log`, `tpt-runtime-green-final.log`,
  `tpt-{debug,release,ef10.0.14}-final.log`, `tpt-package-final.log`,
  `tpt-runtime-{10.0.0,10.0.14}-final.log`, `tpt-cli-{10.0.0,10.0.14}-final.log`. Generated CLI artifacts:
  `artifacts/tooling-10.0.0-80dc180136744ae6b118fea26ce0213f/` and
  `artifacts/tooling-10.0.14-6a2b875ab22c42c1bec805d16bbfb265/`.

## Repeatable verification

Use the preceding tooling plan's full source, isolated patch, package-content and package-consumer commands,
with provider version `10.0.0-tpt-r2-20261113` and feed `artifacts/tpt-package`. Restore cached dependencies and
SDK runtime packs as needed; do not skip bundles for final acceptance. The updated tooling verifier covers
both the ordinary migration model and TPT. No database update or bundle application is part of these commands.
