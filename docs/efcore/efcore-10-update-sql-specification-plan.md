# EF10 write SQL specification module

**Base:** `52e00a4`

**IDs:** `TST-01`, `UPD-01..04`, `SQL-01/02`, `DEF-06`

**Status:** Local implementation, verification and independent review complete; native/release gates open

## Contract and design

Run the complete EF10 `UpdateSqlGeneratorTestBase` family against registered OceanBase provider services
in both modes. This is a local SQL/metadata contract, not server DML or generated-value readback evidence.
Retain eleven official test bodies per mode. Supply literal SQL assertions in the abstract verification
hooks for qualified table/column quoting, current/original parameters, NULL concurrency, default-only
inserts and generated outputs. The inherited no-read insert assertion hardcodes @ parameters; adapt
that one method for Oracle's colon placeholders. The two inherited sequence cases become explicit
negative profile assertions, mapped to Deferred DEF-06, rather than skips or claims of sequence support.

MySQL emits DML without RETURNING and requests an owned transaction when generated reads need the
batch's separate SELECT/LAST_INSERT_ID path. Oracle emits RETURNING INTO :ob_out_N and does not request
a separate readback transaction from the SQL generator. All DML returns NoResults because the custom
batch owns affected-row checks/readback/output parameters. These flags must be checked separately from SQL.

The existing EF UpdateSqlGenerator emits generic NEXT VALUE FOR through both sequence entry pairs.
Override both AppendNextSequenceValueOperation and AppendObtainNextSequenceValueOperation to throw
NotSupportedException before appending anything. Their inherited Generate wrappers must propagate the
same rejection. Names, schemas and delimiter-bearing input must not change that boundary. Do not add
sequence/HiLo, stored-procedure modification mappings, dependencies, public options or live operations.
The existing custom batch already rejects stored-procedure mappings before DML.

Use the existing OceanBaseModelTestHelpers fixture so real provider mapping/convention/service registrations
apply and sync/async connection opening is blocked. Official source version: relational specification
package 10.0.0, with selected 10.0.14 source compatibility verified separately. Decompiled owning assembly
confirms all fourteen test methods and their construction/expectations; it is diagnostic scratch material.

## Execution plan

1. [x] Add MySqlUpdateSqlGeneratorTests/OracleUpdateSqlGeneratorTests in SpecificationTests. Implement
   literal dialect assertions, the three documented profile adaptations, and transaction/result flags.
2. [x] Run the focused family and observe rejection cases fail before the generator change. Investigate
   every additional failure against owning source; do not add skips to make the suite green.
3. [x] Add the two sequence append overrides in OceanBaseUpdateSqlGenerator; test both Generate and
   Append pairs and unchanged caller StringBuilder on rejection.
4. [x] Extend packed consumer sequence rejection checks; verify focused specification and connected
   generated-value/concurrency/runtime tests, then full Debug/Release and isolated EF10.0.14 Release.
5. [x] Pack a unique module version; verify package content and both-patch/both-mode consumers and graphs.
6. [x] Independently review the module, synchronize the owning docs, diff-check and commit locally.

## Evidence

Tested source: module diff on `52e00a4`; final local revision is recorded in git log.
The initial focused run produced 26 passes and four sequence rejection failures (no exception).
The two append overrides change that result to 30/30, zero skips. GeneratedValuesTests, GraphUpdateTests
and TransactionTests pass 134/134. Independent read-only review checked the owning EF10 source,
profile adaptations, registered fixture, custom batch and connected tests, finding no actionable issues.
The reviewer's stale no-build Debug probe found no tests and is not counted as validation; the fresh
full Debug build below includes all thirty cases.

| Gate | Result |
| --- | --- |
| Baseline EF10.0.0 Debug and Release | Unit 2,421; Functional 1,955; Specification 1,482; ConsumerTests 2 passed per configuration; 16 existing mapped Specification skips |
| Isolated EF10.0.14 Release | Same passing totals/skips; source graph confirms EF10.0.14 and Connector 0.3.0 |
| Package content | `10.0.0-update-sql-specification-20261116` passes Verify-Package.ps1 |
| Packed consumers | EF10.0.0/10.0.14, MySQL/Oracle pass, including all four sequence entrypoints; exact dependency graphs/no ProjectReference/matching provider DLL hashes verified |

Commands from the repository root:

```powershell
dotnet test tests/OceanBase.EntityFrameworkCore.SpecificationTests/OceanBase.EntityFrameworkCore.SpecificationTests.csproj -c Release --no-restore --filter FullyQualifiedName~UpdateSqlGeneratorTests -m:1 -nr:false
dotnet test tests/OceanBase.EntityFrameworkCore.FunctionalTests/OceanBase.EntityFrameworkCore.FunctionalTests.csproj -c Release --no-restore --filter 'FullyQualifiedName~GeneratedValuesTests|FullyQualifiedName~GraphUpdateTests|FullyQualifiedName~TransactionTests' -m:1 -nr:false
dotnet test OceanBase.EntityFrameworkCore.slnx -c Release --no-restore -m:1 -nr:false
dotnet test OceanBase.EntityFrameworkCore.slnx -c Debug --no-restore -m:1 -nr:false
dotnet test OceanBase.EntityFrameworkCore.slnx -c Release --no-restore --artifacts-path artifacts/update-sql-specification/compat-build -p:DirectoryPackagesPropsPath=D:/Code/dotnet/efcore.oceanbase/artifacts/update-sql-specification/compat/Directory.Packages.props -m:1 -nr:false
pwsh -File tests/OceanBase.EntityFrameworkCore.PackageConsumer/Verify-Package.ps1 -PackagePath artifacts/update-sql-specification/package/OceanBase.EntityFrameworkCore.10.0.0-update-sql-specification-20261116.nupkg -ExpectedVersion 10.0.0-update-sql-specification-20261116
pwsh -File artifacts/update-sql-specification/verify-consumers.ps1
```

Logs/scripts are scratch artifacts under `artifacts/update-sql-specification/`. The compatibility props
come from the current root props, with EF pinned to 10.0.14 and Connector kept at 0.3.0. Separate outputs
and the existing isolated 0.3.0 cache preserve baseline assets. Consumers use isolated copies of the committed
PackageConsumer sources and the baseline-built package with explicit EF patch references. Design-time
CLI/bundle checks were not rerun for this runtime SQL-generator boundary; no design service/API changed.

Native affected rows, RETURNING/SELECT readback, permissions, transactions and wire behavior remain
open under UPD/TXN/TST-02. TST-01 stays In progress until the selected broader runtime families and
documented exclusions meet their acceptance boundary. No push, publication or destructive SQL is authorized.
