# EF Core 10 DateTime calendar-add module

**Date:** 2026-11-16

**Base:** `aff738e`

**IDs:** `MAP-05`, `QRY-07`, `QRY-11`

**Status:** local implementation, verification and independent review complete; native/release gates open

## Contract

Translate the exact `DateTime.AddMonths(int)` and `DateTime.AddYears(int)` identities in
predicates, ordering and projections. Reuse MySQL `DATETIME` and Oracle unzoned `DATE`/`TIMESTAMP`
carriers, preserving mapping/converter metadata and nullable results. Reject unrelated and
null-converting input/amount carriers; do not add a service, dependency or public API.

MySQL uses `DATE_ADD(input, INTERVAL amount MONTH|YEAR)`. Oracle must preserve the complete
time of day and fractional seconds, and must not inherit `ADD_MONTHS`' source-month-end rule.
In an Oracle Gregorian calendar session:

1. Compute target month start using `ADD_MONTHS(TRUNC(input, 'MM'), months)`.
2. Add `LEAST(original day - 1, target last day - 1)` days.
3. Cast that midnight to `TIMESTAMP(7)` and add the original timestamp minus its truncated day.
4. Cast the final result back to `DATE` for DATE-mapped inputs, retaining truthful metadata for
   subsequent `.Ticks` and `.TimeOfDay` translation. DATE inputs contain no fractional seconds.

Years become months by multiplying the parenthesized integer amount by 12 in SQL. The Oracle expression uses
`LAST_DAY` to determine target month length and requires `NLS_CALENDAR=GREGORIAN`; the provider
does not change session state. NULL flows through native functions/arithmetic. Operations use stored
values under converters, with no general model-space equivalence promise. Stable input expressions
are the supported domain; repeated SQL occurrences do not promise single evaluation of volatile functions.
Server range errors are not a CLR exception or overflow-equivalence claim.

`DateTimeOffset`, TimeOnly wrapping, duration arithmetic and fractional date addition remain separate
contracts. Both-mode native calendar, result precision, session settings and SQL execution remain live gates.

## Execution plan

- [x] Observe RED for both-mode method predicates and mapping admission tests.
- [x] Observe RED for both-mode sync/async intercepted materialization tests.
- [x] Implement exact identities, guarded internal markers and dialect SQL.
- [x] Verify SQL shape, mapping preservation, current parameter values, NULL and negative carriers.
- [x] Run Debug/Release source suites, both-patch packed consumers and package checks.
- [x] Review the complete module and record evidence.
- [x] Commit locally with the final documentation/diff check; see `git log` for the revision.

## References and evidence boundary

The EF10 `IMethodCallTranslator` contract is the existing provider seam used by the DateOnly add
module. CLR month/year adjustment clamps the day and retains the original time ticks. Oracle's
[ADD_MONTHS](https://docs.oracle.com/en/database/oracle/oracle-database/19/sqlrf/ADD_MONTHS.html)
returns DATE and applies source-month-end rules; [LAST_DAY](https://docs.oracle.com/en/database/oracle/oracle-database/19/sqlrf/LAST_DAY.html)
uses the session calendar. The reconstruction above handles the DATE/precision and day-adjustment
differences explicitly. Fresh official-source downloads failed with local TLS errors in this session.
No live compatibility is established by those references or by intercepted reader fixtures.

Initial RED: Unit 4 failed/2 passed, with translation failures for the missing DateTime methods.
The first Functional run exposed a generic InterceptionResult fixture typo; after correcting it, all
8 Functional cases failed on missing method translation (the actual Functional RED). The final intercepted
tests pass 8/8, covering both methods, modes, sync/async, parameter rebinding, NULL and fractional readers.
The values are supplied by the reader fixture and do not establish native calendar calculations.

Independent review found compound year-amount precedence and an Oracle DATE/TIMESTAMP metadata mismatch.
Three regression cases failed before the fixes. Parenthesized year multiplication then passed its focused
test; the outer DATE cast closes both methods' `.Ticks`/`.TimeOfDay` composition cases. The complete
`TemporalMemberQueryTests` suite passes 96/96, including 11 new module cases. Read-only re-review found
no further blockers. Logs are under `artifacts/datetime-calendar-add/`.

## Final local verification

Tested source: this module diff on base `aff738e`; final local revision is recorded by `git log`.

| Gate | Result |
| --- | --- |
| Baseline EF10.0.0 Debug and Release | Unit 2,414; Functional 1,947; Specification 1,452; ConsumerTests 2 passed per configuration; 16 existing mapped Specification skips |
| Isolated EF10.0.14 Release | Same passing totals and mapped skips; graph confirms EF10.0.14 and Connector 0.3.0 |
| Package content | `10.0.0-date-time-calendar-add-20261116` passes Verify-Package.ps1 |
| Packed runtime consumer | EF10.0.0/10.0.14, MySQL/Oracle pass; dependency graphs and provider DLL hashes match the baseline-built package |

Commands (from the repository root):

```powershell
dotnet test OceanBase.EntityFrameworkCore.slnx -c Release --no-restore -m:1 -nr:false
dotnet test OceanBase.EntityFrameworkCore.slnx -c Debug --no-restore -m:1 -nr:false
dotnet test OceanBase.EntityFrameworkCore.slnx -c Release --no-restore --artifacts-path artifacts/datetime-calendar-add/compat-build -p:DirectoryPackagesPropsPath=D:/Code/dotnet/efcore.oceanbase/artifacts/datetime-calendar-add/compat/Directory.Packages.props -m:1 -nr:false
pwsh -File tests/OceanBase.EntityFrameworkCore.PackageConsumer/Verify-Package.ps1 -PackagePath artifacts/datetime-calendar-add/package/OceanBase.EntityFrameworkCore.10.0.0-date-time-calendar-add-20261116.nupkg -ExpectedVersion 10.0.0-date-time-calendar-add-20261116
pwsh -File artifacts/datetime-calendar-add/verify-consumers.ps1
```

The compatibility props are generated from the current root props, pinning EF packages to 10.0.14 while
retaining Connector 0.3.0; restore uses isolated outputs/cache and the local offline feed. The consumer
script copies the committed PackageConsumer sources into isolated scratch projects with explicit patch
references and no ProjectReference. The script and logs are scratch artifacts, not public tooling.

No live query, CLI/bundle rerun, push or publication was performed for this translation-only module.
MAP-05/QRY-07/QRY-11 remain `In progress`: native calendar/session, precision, range/error behavior and
the wider required function matrix still need acceptance.

## Adjacent existing gap for the next module

Final connected-code inspection confirms the older DateOnly Oracle paths still call ADD_MONTHS on the
original date and render year amounts without grouping. They therefore retain source-month-end behavior
and the same compound-year precedence defect fixed here for DateTime. DateOnly constant-amount tests
do not cover those cases. Reconcile DateOnly calendar arithmetic as the next coherent module before
adding further temporal APIs; this module does not change the older DateOnly implementation.
