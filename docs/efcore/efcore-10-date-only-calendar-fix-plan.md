# DateOnly calendar arithmetic correction

**Base:** `4a51610`

**Feature IDs:** `MAP-05`, `QRY-07`, `QRY-11`

**Status:** Local implementation, verification and independent review complete; native/release gates open

## Scope and contract

Correct the existing Oracle `DateOnly.AddMonths(int)` and `AddYears(int)` translations. Gregorian
2024-02-29 plus one month must target March 29, not March 31; 2023-02-28 plus one year must target
2024-02-28, not February 29. Compound years such as `Id + 1` must become `(Id + 1) * 12` months.
NULL, negative and captured amounts, nullable DateOnly readers and DATE member composition retain
the existing EF10/Connector 0.3.0 contract. Reject unrelated/null-converting input and amount carriers.

Reuse the DateTime module's Oracle target-date construction: anchor ADD_MONTHS at TRUNC(input, 'MM'),
then add LEAST(original day - 1, target last day - 1). Return DATE directly for DateOnly and keep
DateTime's existing time restoration. A private shared renderer owns this algorithm; no new service,
dependency or public API. MySQL continues using DATE_ADD.

Oracle requires NLS_CALENDAR=GREGORIAN; the provider does not set session state. Arithmetic acts on
stored values, with no arbitrary converter model-space equivalence promise. Repeated expressions
support stable inputs; volatile single-evaluation parity and CLR range/exception parity are not promised.
Local tests cannot prove native calendar arithmetic, SQL acceptance, range/error behavior or session settings.

## Implementation and verification

1. [x] Add failing unit regressions in TemporalMemberQueryTests for both Oracle methods' month-start,
   day clamp and DATE composition; separately reproduce compound constant/captured year precedence.
2. [x] Expand the existing DateOnlyDayNumberFunctionalTests fixture with nullable input, parameter
   rebinding and explicit clamping SQL assertions. Observe the Oracle cases fail before production edits.
3. [x] Share Oracle calendar-date rendering between DateTime and DateOnly in the query SQL generator.
   Keep every existing marker guard and mapping/parameter contract.
4. [x] Verify mapping/converter identity, amount rejection and malformed-marker boundaries, and extend
   the packed consumer's DateOnly SQL assertions for the corrected algorithm and grouped years.
5. [x] Run focused tests, full Debug/Release suites, isolated EF10.0.14 Release, package content and
   packed consumers on EF10.0.0/10.0.14 in both modes. Review the complete module independently.
6. [x] Update owning documents, run final diff checks and commit locally. See git log for the final revision.

## Evidence

Tested source: module diff on `4a51610`; final revision is in git log. The implementation renames
`OceanBaseQuerySqlGenerator.DateTimeCalendarAdd.cs` to `OceanBaseQuerySqlGenerator.CalendarAdd.cs`
and extracts `AppendOracleCalendarDate(SqlExpression input, SqlExpression amount, bool years)`.
Both DateOnly branches call it; the existing DateTime SQL text/time restoration remains intact.

Observed RED: three Oracle unit regressions failed on missing month-start/day clamp or grouped years.
The eight new functional cases produced four Oracle failures on missing clamp SQL and four MySQL passes.
No fixture error was counted as RED. Mapping/amount and malformed-marker tests characterize the retained
guards; they are not claims of newly failing behavior. GREEN: TemporalMemberQueryTests 103/103 and
combined DateOnly/DateTime calendar functional suites 32/32, including seven new unit and eight new
functional cases. The reader supplies DateTime/DBNull values; these establish DateOnly materialization,
NULL and parameter rebinding, not computed server calendar results. Independent read-only review found
no actionable findings in the final production diff and connected tests.

| Gate | Result |
| --- | --- |
| Baseline EF10.0.0 Debug and Release | Unit 2,421; Functional 1,955; Specification 1,452; ConsumerTests 2 passed per configuration; 16 existing mapped Specification skips |
| Isolated EF10.0.14 Release | Same passing totals/skips; source graph confirms EF10.0.14 and Connector 0.3.0 |
| Package content | `10.0.0-date-only-calendar-fix-20261116` passes Verify-Package.ps1 |
| Packed runtime consumers | EF10.0.0/10.0.14, MySQL/Oracle pass; graphs contain the exact provider/EF/Connector versions, no ProjectReference, and matching provider DLL hashes |

Commands from the repository root:

```powershell
dotnet test OceanBase.EntityFrameworkCore.slnx -c Release --no-restore -m:1 -nr:false
dotnet test OceanBase.EntityFrameworkCore.slnx -c Debug --no-restore -m:1 -nr:false
dotnet test OceanBase.EntityFrameworkCore.slnx -c Release --no-restore --artifacts-path artifacts/date-only-calendar-fix/compat-build -p:DirectoryPackagesPropsPath=D:/Code/dotnet/efcore.oceanbase/artifacts/date-only-calendar-fix/compat/Directory.Packages.props -m:1 -nr:false
pwsh -File tests/OceanBase.EntityFrameworkCore.PackageConsumer/Verify-Package.ps1 -PackagePath artifacts/date-only-calendar-fix/package/OceanBase.EntityFrameworkCore.10.0.0-date-only-calendar-fix-20261116.nupkg -ExpectedVersion 10.0.0-date-only-calendar-fix-20261116
pwsh -File artifacts/date-only-calendar-fix/verify-consumers.ps1
```

Logs/scripts are scratch artifacts under `artifacts/date-only-calendar-fix/`. Compatibility props are
generated from root props with EF pinned to 10.0.14 and Connector retained at 0.3.0; separate outputs and
the existing isolated Connector 0.3.0 package cache avoid contaminating baseline assets. Consumers use
isolated copies of the committed sources and the baseline-built package, with explicit EF patch references.

No native SQL, design-time CLI/bundle rerun, push or publication was performed. This correction changes
private query rendering only. MAP-05/QRY-07/QRY-11 remain In progress until broader required coverage
and live acceptance pass; the overall development goal remains incomplete.

References: [DateTime calendar contract](efcore-10-date-time-add-plan.md),
[DateOnly.AddMonths](efcore-10-date-only-add-months-plan.md),
[DateOnly.AddYears](efcore-10-date-only-add-years-plan.md). Oracle ADD_MONTHS returns DATE and uses
the session calendar/source-month-end rules; LAST_DAY also uses the session calendar. The target-date
reconstruction deliberately avoids starting ADD_MONTHS on the source day. These upstream contracts
do not establish OceanBase native acceptance without a live result.
