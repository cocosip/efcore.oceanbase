# EF10 read-only native query acceptance fixture

**Base:** `53f2c4b`. **IDs:** `TST-02`, `REL-01`, `CON-04/05`, `MAP-01/02/03/07`, `QRY-01..05/10`.
**Goal:** Add an executable, opt-in OceanBase read-only core query profile through the real EF pipeline,
without requiring table creation or modifying tenant objects.
**Architecture:** Keep the existing excluded LiveTests project and UserSecretsId. A keyless raw inline
four-row source supplies known integer/nullable/text/Boolean/decimal values; ordinary LINQ exercises
the actual provider. No intercepted rows in native tests and no SQL evaluator in local fixture tests.
**Spec:** `efcore-10-feature-checklist.md` TST-02 and `efcore-10-release-support.md`.
**Status:** Local fixture complete; native acceptance remains unverified. Final gates/local commit below.

## Scope and evidence boundaries

Both targets are OceanBase Database compatibility modes. UNION ALL/DUAL and typed inline NULLs follow
the project's pinned OceanBase V4.3.5 SQL references, not upstream Oracle/MySQL capabilities. The live
server version must be observed on a successful connection; pinned syntax is not server acceptance.

The profile verifies sync/async mapped reads, changing numeric/text/NULL parameters, Boolean predicates
and projections, deterministic paging, nullable aggregates/GroupBy/HAVING, Any/All and four set operators
including nested difference. Expected results derive from the four literal source rows, not supplied ADO
results. It does not promise all scalar ranges, native collation behavior, writes, transactions or DDL.

Opt-in is still OCEANBASE_RUN_LIVE_TESTS=1; connection strings remain in the existing two User Secrets.
No secrets are read by ordinary local fixture checks. The profile only sends reader SELECT commands;
it never calls EnsureCreated/Migrate/SaveChanges/ExecuteSql. No target objects or cleanup scope are needed.
Failures record a safe stage/type/server code, without forwarding arbitrary connector exception text or
inner exceptions into test artifacts. No authentication or transport setting is invented or changed.

## Files and steps

- [x] Add `ReadOnlyQueryProfile.cs`, `LiveReadOnlyQueryTests.cs`, `ReadOnlyQueryProfileTests.cs` to the
  existing LiveTests project. Use a four-row keyless source, real EF operators and exact expected results.
- [x] Run explicit offline fixture checks before native execution: seed-only SELECT/UNION ALL shape,
  quoted mapped columns, typed NULLs, both-mode paging/set SQL and no secret-dependent setup.
- [x] Build/test the offline profile at EF10.0.0 and isolated EF10.0.14. Reuse existing secrets for one
  explicit non-destructive native attempt per mode; record authentication/SQL boundaries truthfully.
- [x] Run source Debug/Release/isolated-patch gates and unchanged package content/consumers as appropriate.
  Obtain independent review, fix findings, update owning evidence/matrix, diff-check and commit locally.

No production behavior change is planned. If native queries expose a provider defect, isolate meaningful
RED evidence before fixing its owning service. A failed handshake is not a client SQL defect or pass.

## Review focus

Check expected rows independently; NULL ordering must be explicit. Ensure parameter values are not
concatenated into SQL, source/aliases/types match the mapped model, aggregate/set expectations include
NULL/duplicates, default execution cannot load credentials/connect, and exception/cleanup paths cannot
expose connection strings. Source fixtures do not substitute for a successful native run.

Ruling: use the existing efcore/10.0 workspace and module plan location, without a new design approval
cycle, under the user's standing instruction to continue complete modules. Live actions are SELECT-only.

## Evidence

Tested revision: `53f2c4b` plus this module's test/docs changes. Artifacts:
`artifacts/read-only-live-query/`. No production diff, push, publication, DDL or data writes.

### OceanBase sources

OceanBase documentation revision `2a5ae3a23a51729d065cec716508eb26cb064106`:

- [MySQL CAST](https://github.com/oceanbase/oceanbase-doc/blob/2a5ae3a23a51729d065cec716508eb26cb064106/en-US/700.reference/500.sql-reference/100.sql-syntax/200.common-tenant-of-mysql-mode/400.functions-of-mysql-mode/200.single-row-functions-of-mysql-mode/300.conversion-functions-of-mysql-mode/100.cast-of-mysql-mode.md): SIGNED and CHAR(n) targets.
- [Oracle CAST](https://github.com/oceanbase/oceanbase-doc/blob/2a5ae3a23a51729d065cec716508eb26cb064106/en-US/700.reference/500.sql-reference/100.sql-syntax/300.common-tenant-of-oracle-mode/500.functions-of-oracle-mode/200.single-row-functions-of-oracle-mode/600.conversion-functions-of-oracle-mode/200.cast-of-oracle-mode.md): NUMBER/VARCHAR2 targets and SELECT FROM DUAL examples.
- [Set-operation module sources](efcore-10-set-operations-plan.md#owning-sources-and-review): both-mode UNION ALL, Oracle MINUS and operand grouping. The existing collection-root contract in [query/update design](efcore-10-query-update-design.md) uses the same table-free source pattern.

CAST pages are cached under `source/` in this module's artifacts; set pages remain under
`artifacts/set-operations/source/`. These references guide fixture syntax; they do not identify the
deployed server version or prove its metadata/type coercion and result behavior.

### Fixture and native results

Initial implicit restore failed with Windows NuGet TLS authentication errors; local-feed restore then
hit a read-only global package cache. Both solution and excluded LiveTests assets were restored with
`--source artifacts/min-number-feed --packages artifacts/datetime-calendar-add/compat-packages`.
No transport/security setting or dependency version was changed to work around those local failures.

The initial compiled fixture passes 4/4 locally. Exact ordered set-token assertions distinguish the six
source UNION ALL tokens from the actual operator. Final offline runs pass 4/4 at EF10.0.0 and isolated
EF10.0.14, no skips (`offline.log`, `offline-compat.log`). These are SQL/model construction checks only.

Explicit sanitized native attempt, once per mode, using existing User Secrets:

| Mode | Result | Established boundary |
| --- | --- | --- |
| OceanBase MySQL | Failed at opening; OceanBaseException, server code 0, EF readers 0 | Authentication/server version/SQL not established |
| OceanBase Oracle | Same | Authentication/server version/SQL not established |

See `native.log`. The sanitized output does not retain the original connector message, so this attempt
does not establish a more specific transport/authentication cause. No test objects were created or
written. It is not a pass, skip or provider SQL defect. TST-02/REL-01 remain In progress.

### Review

Independent read-only review covers the literal expected rows/aggregates, explicit NULL ordering,
set duplicates and nested difference, captured parameters, opt-in before secret access, context-owned
cleanup within the sanitized exception boundary, offline assertions and evidence separation. No
actionable findings. Reviewer did not execute tests, connect or read secrets; execution evidence is
provided by the recorded commands. No new package/API or connector change is needed.

The final documentation pass identified one evidence wording issue: zero EF readers does not rule out
connector-internal initialization SQL. The workboard now states that no EF query ran, server SQL is
unverified and no DDL was attempted, without claiming all connection-internal SQL was absent.

### Reproduction and final gates

Offline fixture command:

```powershell
dotnet test tests/OceanBase.EntityFrameworkCore.LiveTests -c Release --no-restore `
  --filter FullyQualifiedName~ReadOnlyQueryProfileTests -m:1 -nr:false
```

The isolated patch command adds `--artifacts-path artifacts/read-only-live-query/compat-build` and
`-p:DirectoryPackagesPropsPath=D:/Code/dotnet/efcore.oceanbase/artifacts/read-only-live-query/compat/Directory.Packages.props`.
Restore solution and LiveTests separately with those flags and the local feed/cache above. The module
props copy the existing EF10.0.14 central versions; normal project assets remain baseline EF10.0.0.
The native opt-in command and restoration of the prior environment value are documented in
[release support](efcore-10-release-support.md#reproduction); its filter is `LiveReadOnlyQueryTests`.

The live project stays outside the solution: report its four offline cases and two failing native
attempts separately.

| Final gate | Result |
| --- | --- |
| EF10.0.0 Debug | Unit 2463, Functional 2219, Specification 1538, ConsumerTests 2: 6222 pass; 16 existing mapped skips |
| EF10.0.0 Release | Same; zero failures |
| Isolated EF10.0.14 Release | Same; zero failures |
| Excluded LiveTests offline profile | 4/4 pass at each EF patch, zero skips; not included in the solution totals |
| Native profile | 0/2 pass, two opening failures; not included in offline totals |
| Reused package | Content and both-patch/mode public consumers pass; no new binary |

Full solution commands: `dotnet test OceanBase.EntityFrameworkCore.slnx -c Debug --no-restore -m:1
-nr:false`, the same at Release, then Release with the isolated patch flags above. Logs:
`full-debug.log`, `full-release.log`, `full-compat.log`. Final code was unchanged throughout those runs.

Package checks reuse
`artifacts/set-operations/package/OceanBase.EntityFrameworkCore.10.0.0-set-operations-20261116.nupkg`.
Run PackageConsumer/Verify-Package.ps1 with that PackagePath/ExpectedVersion, then
`artifacts/read-only-live-query/verify-consumers.ps1`; logs: `package-content.log`, `consumers.log`.
The artifact contains the production implementation built from `4b8fed2` plus the change committed as
`6e31783`, unchanged in this module. Package-only dependency graphs and archive/consumer DLL SHA256
`19F828983A14ADD2905F9C81A8FE57F98F38F995E08C246159B3DB55B269AE0C` match. No claim equates a
current revision-stamped source rebuild with that earlier archive.

Required status remains 9 Done / 44 In progress per mode (53 rows). Native mapping/query results,
write/transaction/schema/transport acceptance, the classified support environment, broader official
store-backed specifications and release gates remain open. No fresh tooling/bundle/hosted/Linux result.
Local module commit is recorded in Git history; the overall goal remains active.
