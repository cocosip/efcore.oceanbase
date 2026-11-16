# EF10 DbContext concurrency-protection module

**Base:** `f339d08`

**IDs:** `TST-01`, `CFG-04`, `QRY-01`, `QRY-11`, `UPD-01`, `DEF-03`

**Status:** Local characterization, verification and independent review complete; native/release gates open

## Contract and design

Admit the EF10 enabled concurrency-detector specification family for both modes through the real
OceanBase provider services. The seven core query methods (Find, Count, First, Last, Single, Any,
ToList) retain their inherited bodies and the enabled base's cross-flow critical-section assertion.
The relational FromSql method uses literal dialect delimiters instead of an initialized TestStore.
SaveChanges retains the inherited concurrent-write attempt and exact exception assertion, replacing
its subsequent database absence query with an explicit zero-open assertion. These two adaptations
must be recorded; this is not a native store fixture or proof of database state after a rejected write.

The fixture deliberately does not create or seed a store. Its context applies the official assigned-key
Product model and installs a per-context connection-opening guard that throws a distinct exception.
No physical connection/command is replaced and no result rows are fabricated. Any attempt to open
the database in an inherited rejection case therefore fails its exact concurrency-message assertion.
Fixture tests verify provider services, the configured mode, the model and the guard in sync/async.

Add bulk update/delete rejection cases, context recovery after the competing critical section exits,
and an explicit EnableThreadSafetyChecks(false) control. Recovery and disabled-check controls may
reach the guard, never a physical database. This only validates entry to the execution path, not query
results or successful writes. Default enabled checks must reject before connection opening, including
bulk operations. Do not add a provider concurrency implementation unless a failing test exposes a defect.

Owning contracts are the installed EF10.0.0 core/relational specification and relational runtime
assemblies; verify selected 10.0.14 compatibility separately. Command/transaction interception families
require physical/store behavior and remain unadmitted. Disabled concurrency's full result-bearing family
also requires a store and is not represented by the single local disabled-check control.

## Execution plan

1. [x] Add both-mode fixtures and enabled specification subclasses with exactly two documented method
   adaptations. Add fixture integrity, bulk rejection, recovery and disabled-check controls.
2. [x] Run focused cases. Verify assertion sensitivity by temporarily disabling fixture thread-safety
   checks for representative inherited query/write tests, observing failures and restoring the fixture.
   Record existing behavior accurately; do not invent a production fix to obtain a RED cycle.
3. [x] Run full baseline Debug/Release and isolated EF10.0.14 Release. This test-only module reuses
   the prior production package evidence; no redundant package, CLI or bundle rebuild is required.
4. [x] Request independent review, address material findings, synchronize owning evidence documents,
   run diff checks and commit locally. Keep broader TST-01 and live/release gates open.

## Review focus

Ensure the inherited enabled cross-flow assertion really executes; no guard exception or fabricated
database result is accepted as concurrency rejection. Verify the local model matches the upstream
fixture, no store/seed setup escapes the guard, per-context counts do not leak across cases, bulk paths
exercise actual provider execution, and recovery/disabled controls prove reaching the guard. Check
that documentation accurately labels both adaptations and leaves native/store/runtime acceptance open.

## Evidence

Tested source: module diff on `f339d08`; final local revision is recorded in git log.

| Gate | Result |
| --- | --- |
| Focused enabled family and controls | 56 passed, 0 failed/skipped: 36 family cases and 20 controls across both modes |
| Sensitivity control | Temporarily configure default contexts with false: all 20 selected Count/SaveChanges/FromSql/bulk cases fail at opening attempt 1; restore untouched defaults: 56 pass |
| Baseline EF10.0.0 Debug and Release | Unit 2,421; Functional 1,955; Specification 1,538; ConsumerTests 2 passed per configuration; 16 existing mapped skips |
| Isolated EF10.0.14 Release | Same totals/skips; exact source graphs confirm EF10.0.14 and Connector 0.3.0 |
| Final review correction | Complete Specification project rerun in baseline Debug/Release and isolated EF10.0.14 Release: 1,538 passed and 16 existing mapped skips in each |
| Production/package/tooling | No production, public API, dependency or consumer changes; no new package, CLI/bundle or live run |

Independent read-only review checked upstream assertions, fixture integrity, bulk execution and local
evidence boundaries. One test-gap finding is corrected: setting true explicitly masked possible default
regressions. The final fixture leaves the default option untouched, configuring false only for the
explicit disabled-check control. Sensitivity and focused green probes were repeated after this correction,
then the entire Specification project was rerun in all three configurations. No other actionable finding
was reported. The reviewer did not rerun builds concurrently; parent-side commands above supply evidence.
The upstream detector implementation and full result-bearing disabled-check family remain outside this
module; the native/store boundaries are recorded rather than inferred from these local controls.

Initial discovery exposed inherited MemberData duplication. Overrides now inherit both theory and data
attributes, avoiding duplicate IDs without suppressing xUnit analyzers. An intermediate compile-only
failure while adjusting those attributes is not counted as the sensitivity RED evidence.

Commands from the repository root:

```powershell
dotnet test tests/OceanBase.EntityFrameworkCore.SpecificationTests/OceanBase.EntityFrameworkCore.SpecificationTests.csproj -c Release --no-restore --filter FullyQualifiedName~ConcurrencyDetectorTests -m:1 -nr:false
dotnet test OceanBase.EntityFrameworkCore.slnx -c Release --no-restore -m:1 -nr:false
dotnet test OceanBase.EntityFrameworkCore.slnx -c Debug --no-restore -m:1 -nr:false
dotnet test OceanBase.EntityFrameworkCore.slnx -c Release --no-restore --artifacts-path artifacts/concurrency-detector/compat-build -p:DirectoryPackagesPropsPath=D:/Code/dotnet/efcore.oceanbase/artifacts/concurrency-detector/compat/Directory.Packages.props -m:1 -nr:false
```

Logs live under `artifacts/concurrency-detector/`: sensitivity-red.log, focused-green.log, release.log,
debug.log, ef10.0.14-release.log and review-{release,debug,ef10.0.14-release}.log. Compatibility props
derive from current root props with EF pinned to 10.0.14; isolated restore uses the existing
artifacts/datetime-calendar-add/compat-packages cache and artifacts/min-number-feed, retaining Connector
0.3.0 and baseline assets. Exact-source diagnostic decompilations are under artifacts/spec-research/source/.
TST-01 and the overall goal remain open. No push, publication or live SQL is part of this module.
