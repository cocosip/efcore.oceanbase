# EF10 relational set-operation queries

**Base:** `4b8fed2`; **IDs:** `QRY-01/02/03/05/11`, `MAP-07`, `TST-01`, `PKG-03/04`.
**Goal:** Complete one local module for Queryable Union, Concat, Intersect and Except, including Oracle
difference syntax, nesting, query composition, nullable results and parameter binding.
**Architecture:** Keep EF10 set-operation translation, projection/type inference and operand grouping.
Extend only the provider SQL generator where OceanBase differs. Existing ADO doubles verify the real
provider query pipeline; supplied reader rows do not evaluate set membership or duplicate elimination.
**Spec:** efcore-10-query-update-design.md and the existing QRY inventory, extended explicitly for this family.
**Status:** Local implementation, tests, package and independent review complete. Commit is recorded in
`git log`; native acceptance and the overall EF10 goal remain open.

## Design and contracts

The selected approach overrides SQL generation at EF's existing GenerateSetOperation seam. Rewriting LINQ
to joins/NOT EXISTS would add null/multicolumn/comparison risks and duplicate EF's query pipeline. A text
replacement of EXCEPT would corrupt literals/identifiers and bypass expression semantics.

The project's OceanBase V4.3.5 documentation revision `2a5ae3a23a51729d065cec716508eb26cb064106` explicitly
says Oracle supports MINUS and does not support EXCEPT. MySQL supports EXCEPT/MINUS and INTERSECT.
UNION is distinct; UNION ALL preserves duplicates; INTERSECT/MINUS are distinct. EF10 Queryable.Except
builds a distinct ExceptExpression. Preserve EF's explicit operand grouping for different operators,
different distinctness and nested Except; it avoids depending on native precedence differences.
OceanBase Oracle docs exclude BLOB/CLOB set projections. This provider conservatively includes NCLOB
in its LOB boundary; the pinned page does not independently establish native NCLOB set behavior. Reject
these projections before ADO execution. Use bounded text/scalar projections. Non-distinct MINUS and
INTERSECT expression trees also reject, because OceanBase documents only their distinct forms.

| Area | Acceptance |
| --- | --- |
| Operators | Union -> UNION; Concat -> UNION ALL; Intersect -> INTERSECT; Except -> MySQL EXCEPT / Oracle MINUS. No new public API or comparer overload support. |
| Grouping | Preserve left/right nested difference and mixed operators/distinctness with EF's parentheses and derived-table boundaries. |
| Composition | Operand predicates and mapped columns, result ordering/paging/filtering/aggregates and current captured binds survive. |
| Materialization | Nullable numeric and bounded text projections, entity tracking/no-tracking/identity resolution and duplicate rows supplied by the reader follow the existing shaper. |
| Errors | Oracle LOB projections and unsupported comparer overloads reject before commands; cancellation/error cleanup permits a subsequent query. |
| Package | One baseline-built provider package passes public set-operation SQL/materialization smokes on both modes and EF10.0.0/10.0.14, with exact graphs/hashes. |

Native set membership, duplicate elimination, null/collation/coercion/LOB semantics and row ordering remain
unverified. This module does not admit official store-backed query specifications or promote Required rows.

## Execution plan

- [x] Add unit translation/negative tests and observe meaningful Oracle syntax/LOB RED before production edits.
- [x] Correct the provider SQL seam, retain EF operand generation and pass both-mode operator/grouping tests.
- [x] Add both-mode sync/async functional tests and a public package-only smoke; run connected query,
  collection-root/TPC/bulk-DML regressions.
- [x] Run full Debug/Release, isolated EF10.0.14, new package content and both-patch/mode consumers; obtain
  independent review, fix findings, update owning documents, diff-check and commit locally.

## Owning sources and review

Exact EF10.0.0 QuerySqlGenerator.GenerateSetOperation/GenerateSetOperationOperand and SelectExpression
ApplyUnion/ApplyExcept/ApplyIntersect sources are cached in `artifacts/set-operations/source/`.
OceanBase references at the pinned revision:

- [Oracle compound queries](https://github.com/oceanbase/oceanbase-doc/blob/2a5ae3a23a51729d065cec716508eb26cb064106/en-US/700.reference/500.sql-reference/100.sql-syntax/300.common-tenant-of-oracle-mode/800.queries-and-subqueries-of-oracle-mode/400.collection-of-oracle-mode.md).
- [Oracle set operators](https://github.com/oceanbase/oceanbase-doc/blob/2a5ae3a23a51729d065cec716508eb26cb064106/en-US/700.reference/500.sql-reference/100.sql-syntax/300.common-tenant-of-oracle-mode/400.operator-of-oracle-mode/600.set-operators-of-oracle-mode.md).
- [MySQL set operators](https://github.com/oceanbase/oceanbase-doc/blob/2a5ae3a23a51729d065cec716508eb26cb064106/en-US/700.reference/500.sql-reference/100.sql-syntax/200.common-tenant-of-mysql-mode/600.sql-statement-of-mysql-mode/8100.select-of-mysql-mode/400.union-clause-of-mysql-mode.md).

Review noncommutative grouping, ALL preservation, LOB mapping facts on both operands, mode isolation,
null propagation, inputs in SQL occurrence order and reader/connection recovery. Compare emitted SQL
independently of canned rows. Do not infer native acceptance from matching SQL or local package success.

## Evidence

Artifacts: `artifacts/set-operations/`. Tested revision is base `4b8fed2` plus this module's working
changes; final commit is discoverable from this plan's Git history. No push, publication or native SQL/DDL.

### Regression and fixture evidence

- Removing the production fix with the corrected fixtures gives 16 meaningful failures / 22 passes:
  twelve Oracle LOB cases and four Oracle Except uses. See `red-final-fixture.log`.
- Two additional direct non-distinct Except/Intersect tree tests fail with no exception before the ALL
  guard, then pass after it. See `non-distinct-red.log`. These trees are not emitted by public Queryable.
- Final focused unit run: 42 pass (`green.log`); functional: 62 pass (`functional-green.log`). The full
  solution runs below include the connected query, collection-root, TPC and bulk-DML regressions; no
  additional standalone connected count is claimed.
- Initial fixture failures corrected explicit NCLOB mapping, captured Skip/Take construction values
  and the FETCH FIRST versus FETCH NEXT assertion. They are not production defects. Supplied rows
  only verify EF shaping/reader behavior; the test double does not compute SQL set results.

### Full solution and package gates

| Gate | Result |
| --- | --- |
| EF10.0.0 Debug | Unit 2463, Functional 2183, Specification 1538, ConsumerTests 2 pass; 16 existing mapped skips |
| EF10.0.0 Release | Same: 6186 pass, zero failures, 16 existing mapped skips |
| Isolated EF10.0.14 Release | Same: 6186 pass, zero failures, 16 existing mapped skips; separate props/restore/output paths |
| New baseline-built package | `10.0.0-set-operations-20261116`; package content, README, net10.0 and EF ranges/Connector 0.3.0 checks pass |
| Public runtime consumers | Both modes at EF/Proxies10.0.0 and 10.0.14 pass; no project or local connector reference |
| Binary identity | Baseline source, nupkg and both consumer copies match SHA256 `19F828983A14ADD2905F9C81A8FE57F98F38F995E08C246159B3DB55B269AE0C` |

Reproduce source gates with `dotnet test OceanBase.EntityFrameworkCore.slnx -c Debug --no-restore -m:1
-nr:false` and the same command at Release. Logs: `full-debug.log`, `full-release.log`, `full-compat.log`.
The isolated patch gate uses `--artifacts-path artifacts/set-operations/compat-build` and
`-p:DirectoryPackagesPropsPath=D:/Code/dotnet/efcore.oceanbase/artifacts/set-operations/compat/Directory.Packages.props`.
Its restore uses the existing local package feed and separate package cache.

Pack the baseline Release output with `dotnet pack src/OceanBase.EntityFrameworkCore -c Release
--no-build --no-restore -p:PackageVersion=10.0.0-set-operations-20261116 -o artifacts/set-operations/package`.
Run `tests/OceanBase.EntityFrameworkCore.PackageConsumer/Verify-Package.ps1` with PackagePath and
ExpectedVersion, then `artifacts/set-operations/verify-consumers.ps1`; logs are `pack.log` and
`consumers.log`. This package has changed production code and does not reuse the earlier artifact.
The hash describes the recorded precommit build; rebuilding after a commit may change revision stamps.

### Review and limits

Independent read-only review covered the generator, connected EF/mapping source, unit/functional tests,
package smoke and source references. No actionable code findings. Its NCLOB evidence wording correction
is incorporated: explicit native documentation covers BLOB/CLOB, while NCLOB is a conservative provider
restriction. The repository guidelines now explicitly require OceanBase version evidence for each mode;
Oracle Database/MySQL Server features are not OceanBase capability evidence.

No fresh CLI/bundle, hosted CI/Linux or native execution evidence. Native syntax, set membership,
deduplication, collation/coercion, NULL/LOB semantics and ordering remain open. Required status stays
9 Done / 44 In progress per mode, and broader official store-backed query specifications remain open.
