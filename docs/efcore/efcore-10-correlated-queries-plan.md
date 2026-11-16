# EF10 correlated lateral-query module

**Base:** `8f810ec`. **IDs:** `QRY-03/05/08/11`, `CON-04/05`, `TST-01`, `PKG-03/04`.
**Goal:** Generate OceanBase lateral joins for correlated LINQ shapes that remain EF APPLY nodes,
with local SQL/materialization/cleanup evidence in both modes.
**Architecture:** Override the two SQL-generator seams. Reuse EF's correlation rewriting, projection
mapping, nullable shaper and pagination; reuse existing test connection/interception facilities.
**Spec:** `efcore-10-feature-checklist.md` QRY-03/11 and `efcore-10-query-update-design.md`.
**Stack:** net10.0, EF `[10.0.0,11.0.0)`, OceanBaseConnector 0.3.0. Both targets are OceanBase modes.
**Status:** Local module complete; native semantics/version acceptance remain unverified.

## Contract and approach

**Owning layer:** EF Provider SQL translation. Existing EF relational execution uses the Connector's
public DbConnection/CreateCommand, DbCommand.Parameters/ExecuteReader[Async] and DbDataReader APIs.
No new Connector API, transport, parameter format, native reader conversion, routing or protocol work.
OceanBase's lateral SQL/version rules are server prerequisites, not driver features implemented here.

The current EF base generator emits CROSS APPLY/OUTER APPLY without a provider override. OceanBase's
documented lateral-derived-table path provides correlation to preceding FROM sources. Emit
`JOIN LATERAL (<select>) alias ON 1 = 1` for CrossApply and
`LEFT JOIN LATERAL (<select>) alias ON 1 = 1` for OuterApply. The left form preserves absent inner rows.
Keep nested aliases, outer references, ordering/limits, mapping and current input occurrence order.
Ordinary joins/scalar subqueries and EF's decorrelation optimizations stay with existing services.
When EF has lifted the projection and leaves a plain TableExpression, generate CROSS JOIN or
LEFT JOIN ON 1 = 1 instead: OceanBase LATERAL syntax requires a derived source, not a bare table name.
This branch changes no projection or EF scalar/anonymous DefaultIfEmpty COALESCE/default decisions;
those syntax controls do not establish their native nullable result semantics.

Rewriting to scalar subqueries would lose multi-column/collection semantics. Rejecting all APPLY nodes
would omit an available OceanBase capability. Use the lateral SQL seam without a new translator,
configuration switch, dependency, SQL evaluator or connector change. Public TVF/procedure mappings
remain outside the initial scope; arbitrary unsupported LINQ still rejects through EF.

OceanBase V4.3.5 is the reference profile, not proof of the unknown deployed endpoint version. The
common resolver permits lateral derived tables only for cluster versions >=4.3.1, or >=4.2.2 and <4.3.0.
This module does not detect server versions or promise earlier-version execution. SQL-generation and
supplied-reader tests do not prove native lateral evaluation or NULL/limit/result semantics.

## Owning sources

- OceanBase docs revision `2a5ae3a23a51729d065cec716508eb26cb064106`, Oracle
  [lateral SQL](https://github.com/oceanbase/oceanbase-doc/blob/2a5ae3a23a51729d065cec716508eb26cb064106/en-US/700.reference/500.sql-reference/100.sql-syntax/300.common-tenant-of-oracle-mode/800.queries-and-subqueries-of-oracle-mode/650.lateral-derived-tables-of-oracle-sql-suntax.md)
  and [application examples](https://github.com/oceanbase/oceanbase-doc/blob/2a5ae3a23a51729d065cec716508eb26cb064106/en-US/300.develop/200.application-development-of-oracle-mode/500.read-data-of-oracle-mode/200.multi-table-join-query-of-oracle-mode/600.lateral-derived-tables-of-oracle-develop.md).
- OceanBase `v4.3.5_CE_BP5`
  [MySQL parser](https://github.com/oceanbase/oceanbase/blob/v4.3.5_CE_BP5/src/sql/parser/sql_parser_mysql_mode.y):
  `table_factor` accepts LATERAL derived SELECTs; joined_table accepts inner/left joins.
- Same source's [DML resolver](https://github.com/oceanbase/oceanbase/blob/v4.3.5_CE_BP5/src/sql/resolver/dml/ob_dml_resolver.cpp):
  `resolve_lateral_generated_table`, common SELECT path, namespace and cluster-version restriction;
  `resolve_joined_table_item` handles lateral operands. No upstream Oracle/MySQL feature assumption.
- EF10.0.0 `QuerySqlGenerator.VisitCrossApply/VisitOuterApply` and SelectExpression join conversion.
  Existing exact sources are cached under `artifacts/set-operations/source/`; OceanBase sources under
  `artifacts/correlated-queries/source/`. Oracle parser source is not publicly available at this tag;
  no claim is made to have inspected it.

## Files and execution

- [x] Add `CorrelatedQueryTests.cs` in UnitTests: both apply kinds, outer-reference projection,
  correlated paging/nesting and ordinary decorrelated join control. Observe meaningful SQL RED.
- [x] Add the two generator overrides in a focused `OceanBaseQuerySqlGenerator.Joins.cs` partial.
  Pass the unit profile without altering EF's query tree or source provider.
- [x] Add `CorrelatedQueryRuntimeTests.cs` in FunctionalTests: both-mode sync/async SQL/binding
  assertions independent of supplied nullable rows; repeated input values, empty results, failures,
  cancellation, lease/reader cleanup and recovery. Include unsupported predicate rejection control.
- [x] Add public package-only `CorrelatedQuerySmoke.cs` and Program call. Build a new baseline package;
  check contents, both EF patches/modes, dependency graphs and source/archive/consumer DLL identity.
- [x] Extend the existing read-only live profile with inner/outer per-parent first-row queries and
  exact literal pair expectations; verify SQL construction at both EF patches. Do not repeat an
  unchanged failed endpoint handshake or claim native lateral results from fixture construction.
- [x] Run full Debug/Release/isolated EF10.0.14, connected join/collection/include/split/bulk regressions,
  independent review, fix findings, update owning docs/matrix, diff-check and commit locally.

Review outer-join NULL extension, predicate placement, lateral source grouping, multiple/nested alias
scopes, per-parent versus global paging, parameter occurrences, exact selected columns and row shaping.
Expected reader values are supplied by a double; never call them native join evaluation evidence.

Ruling: follow the user's standing inline module workflow and existing plan location; no new worktree
or design approval cycle. Only the correlated query module is active. Lifted base-table handling is
part of the same generator seam, discovered by the unpaged shape tests, not a new feature family.

## Evidence

Artifacts: `artifacts/correlated-queries/`. Tested revision is `8f810ec` plus this module's source/test/
consumer/docs changes. Local commit is recorded in Git history. No live retry, DDL/data writes, push
or publication. Existing native opening failures remain unresolved; no new native result is claimed.

### Regression and fixture evidence

Initial unit RED: ten lateral syntax failures, two ordinary join/scalar controls pass (`unit-red.log`).
Unpaged tests then expose lifted TableExpression operands; emitting LATERAL before those bare names
fails four meaningful syntax cases (`base-table-red.log`). The final generator handles both branches.
With the final fixtures and the entire new generator temporarily removed, unit tests fail 14/pass 2
and functional tests fail 32/pass 4 (`unit-red-final-fixture.log`, `functional-red-final-fixture.log`).
Restoration passes 16 unit and 36 functional tests. Unsupported local predicates reject before opening.

Initial functional compile errors were fixture nullable annotations; initial eight SQL assertion failures
were missing parentheses in the expected arithmetic predicate. The native fixture's first record-constructor
projection could not be ordered by EF; ordering the anonymous server projection before the final record
construction fixes the fixture. These are not production RED evidence or extra provider defects.

The native profile now asserts pairs (1,2)/(2,3)/(3,4), plus (4,NULL) for the outer form, from the four
literal rows. Six offline tests pass at each EF patch (`live-offline.log`, `live-offline-compat.log`).
They validate generated SQL without reading secrets/connecting or supplying fake native pair results.

### Final source and package gates

| Gate | Result |
| --- | --- |
| EF10.0.0 Debug | Unit 2479, Functional 2255, Specification 1538, ConsumerTests 2: 6274 pass; 16 existing mapped skips |
| EF10.0.0 Release | Same; zero failures |
| Isolated EF10.0.14 Release | Same; zero failures |
| Excluded LiveTests offline checks | Six pass per patch, zero skips; separate from solution totals |
| New package content | `10.0.0-correlated-queries-20261116`, net10.0/bounded EF dependencies/Connector 0.3.0/README pass |
| Public package runtime | Both modes at both EF patches pass, including the new lateral smoke; package-only graphs |
| Binary identity | Source/archive/both consumer copies: SHA256 `5DA5F8EA0E4337A93E35655399AB0E3DFCA862B6067FEB4C286529D7C691DA28` |

Run `dotnet test OceanBase.EntityFrameworkCore.slnx -c Debug --no-restore -m:1 -nr:false`, then the
same command at Release. Logs: `full-debug.log`, `full-release.log`, `full-compat.log`. These complete
runs include connected ordinary joins/scalar subqueries, collection roots, Include/split/TPC and bulk-DML
regressions; no separate connected total is claimed.

The isolated patch restore/test adds `--artifacts-path artifacts/correlated-queries/compat-build` and
`-p:DirectoryPackagesPropsPath=D:/Code/dotnet/efcore.oceanbase/artifacts/correlated-queries/compat/Directory.Packages.props`.
Restore from `--source artifacts/min-number-feed --packages artifacts/datetime-calendar-add/compat-packages`;
restore the excluded LiveTests project separately before its offline filter
`FullyQualifiedName~ReadOnlyQueryProfileTests`. Baseline assets remain EF10.0.0.

Pack baseline Release with `dotnet pack src/OceanBase.EntityFrameworkCore -c Release --no-build
--no-restore -p:PackageVersion=10.0.0-correlated-queries-20261116 -o artifacts/correlated-queries/package`.
Verify-Package.ps1 receives that PackagePath/ExpectedVersion; `artifacts/correlated-queries/verify-consumers.ps1`
checks both-patch public runtime consumers and package-only dependency graphs. Logs: `pack.log`,
`package-content.log`, `consumers.log`. The package was built from the tested precommit source above;
the recorded hash describes that revision stamp, not an arbitrary rebuild after committing.

### Review and remaining work

Independent read-only review checks both generator branches, EF join conversion, cached OceanBase
parser/resolver sources, nested scopes, nullable entity/collection shaping, per-parent paging, current
bindings, cleanup, public smoke/native fixture and driver boundaries. No actionable findings. Reviewer
did not run tests, connect or read secrets; command results above are execution evidence.
The final documentation pass found stale current package/test/hash summaries in PKG-03/04 and REL-02;
these and PKG-02 now name the new package and 6274-pass gates, retaining historical CLI/bundle limits.

Required status remains 9 Done / 44 In progress per mode. Native lateral syntax/evaluation, nullable
scalar/anonymous DefaultIfEmpty default semantics, deployed cluster-version acceptance and broader
official store-backed runtime families remain open. No fresh tooling/bundle/hosted/Linux result. This
module adds no driver feature, new Connector API, transport routing or failover implementation. The
overall goal remains active.
