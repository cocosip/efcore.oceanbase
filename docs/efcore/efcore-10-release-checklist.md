# EF Core 10 Release Acceptance Checklist

Updated 2026-11-20. This checklist records tests, evidence and release gates.
See the [feature inventory](efcore-10-feature-matrix.md) for code scope and the [gap inventory](efcore-10-gap-matrix.md) for individual development states.
The implementation candidate is `e25988a`, using published OceanBaseConnector 0.4.0. The current
evidence below supersedes earlier checkpoints for this bounded release. Implementation status remains
in the feature/gap inventories; test counts are not feature-completion counts.

The current release target is Windows x64 with the existing OceanBase 4.4.2.2 MySQL/Oracle ordinary-direct
profile, TLS Disabled. On 2026-11-20 the user explicitly excluded Linux publication, TLS and HA acceptance
because the required environments are unavailable. Record these as unverified deployment extensions, not
blocking gates for this bounded release and not successful acceptance. Do not generalize the direct profile
to proxy routing/failover, certificate verification or distributed ambient transactions.

## Acceptance states and completion criteria

- `Passed` applies only to the recorded revision, package identity, mode and environment; `Historical pass` does not establish acceptance of the current revision.
  `Partially passed`/`Pending acceptance`/`Blocked` leave the gate open; unexecuted tests, skips and fake readers must not be reported as live-database passes.
- For every declared runtime profile, check MySQL/Oracle, sync/async, positive/NULL/boundary/rejection/recovery cases; check SQL, parameters, readers,
  ownership, cancellation and error contracts separately. Missing test coverage updates only this checklist; it does not make an implemented feature unimplemented.
- The final release must align source, package, consumer and Connector artifact identities; independent code review must leave no unresolved Critical/Important issues.
  State remaining Minor issues, skips and scope exclusions explicitly.

## Release gates

| Gate | State | Current evidence / remaining conditions |
| --- | --- | --- |
| R-01 Dependency and source identity | Passed | Implementation `e25988a`, published Connector 0.4.0 and package/DLL hashes are recorded below. README repacks `.2`/`.3` preserve every runtime payload entry and the implementation revision; final `.3` package-only consumers pass on both EF patches |
| R-02 Local / TST-01 | Passed | Windows Release passed 7,016 tests per EF patch, with 16 documented specification skips each. Selected official store-backed runtime families are tracked separately under R-05; this does not claim the entire upstream specification suite |
| R-03 Live database / TST-02 | Passed for declared scope | Both patches passed 921 baseline cases each (908 native, 13 offline), plus 18 added native cases and eight repeated package smoke cases each. The 939 distinct cases per patch contain 926 native and 13 offline checks; these counts do not measure feature completion |
| R-04 PKG-01..04 | Passed for declared scope | Final `.3` passed archive/source checks and isolated consumers on both EF patches; every runtime payload entry matches tested `.1`/`.2`. Strict synthetic EF11 dependency rejection passed. Native/tooling gates are complete within the recorded scope |
| R-05 Tooling / DEF-01/02/08 | Passed for declared scope | Package-only catalog/model/migration/Windows-bundle gates pass on both patches, including 12 native migration applications, four mode/patch lock checks and 624 selected official runtime cases. C04 native replay passes eight model/proxy combinations and four generated-query paths per patch. Windows x64 NativeAOT passes its bounded EF 10.0.14 profile with retained warnings |
| R-06 Platforms | Passed for declared scope | Current Windows source/package checks pass. Linux final-revision publication/CI is explicitly excluded by the user. Historical cross-platform CI is not evidence for this revision |
| R-07 Environment / REL-01 | Passed for declared scope | OceanBase 4.4.2.2, both compatibility modes, ordinary direct endpoint, TLS Disabled, SDK 10.0.403/runtime 10.0.14 and Connector identity are recorded below. No Proxy/TLS/HA or distributed ambient-transaction acceptance is claimed |
| R-08 Review / REL-02 | Passed for declared scope | Independent reviews cover the computed-column fix, equivalent EF-patch mappings, connection probes, added native contracts, README and final evidence/scope. Initial findings were corrected and re-reviewed; no unresolved Critical/Important issue remains |
| R-09 Sensitive information/cleanup | Passed for candidate-owned scope | Tracked text and 488 completed log/TRX/SQL files have no configured connection-detail matches or locked files. Candidate-owned CREATE/cleanup/absence checks passed. The read-only global catalog still retains ten tables and four sequences of historical or unidentified ownership; this is not a zero-object global pass and does not authorize prefix-based deletion |
| R-10 Release operations | Held by user instruction | No tag, push or publication is authorized. Package major/dependency ranges and README scope are aligned. Local acceptance commits do not authorize release operations; a validation package is not a published release |

## Remaining publication preparation

The acceptance checkpoint is `4fab40c`; the tested implementation remains `e25988a`.
The user reconfirmed on 2026-11-20 that Linux, TLS and HA acceptance may remain documented exclusions.
They do not block this declared release scope. No further feature implementation is currently identified
as a blocker within that scope; the deferred and unaccepted extensions below remain explicit limitations.

- R-01/R-04: Produce the final stable package, currently configured as `10.0.0`, from the selected release
  commit. The accepted `.1`/`.2`/`.3` artifacts are validation versions. Verify the final archive, dependencies,
  README, source revision and hashes, then run isolated consumers on EF 10.0.0 and 10.0.14 and focused native
  connection/CRUD/generated-value/migration smoke checks against that exact package. A documentation-only
  checkpoint does not require repeating the full native suite; any runtime change requires affected regression.
- R-04/R-10: Confirm that the selected version is available on the publication feed and complete an online
  direct/transitive dependency vulnerability audit. The 2026-11-20 NuGet version lookup could not connect;
  cached restores and prior package checks establish neither version availability nor a current online audit.
- R-10: Tag, push and publish only after explicit user authorization. No release operation has been performed.

## Acceptance requirements by feature

The 53 Required acceptance IDs below define contracts and map them to evidence gates; they are not a second status inventory.
Current revision results and their per-mode scope are recorded in the release gates and evidence below. Administrative extensions remain explicitly unaccepted; a scoped gate pass does not accept every wider condition.
Existing DEF-01/02/03/06/07/08/09 functionality still requires R-03/R-05 checks. D-04/05/06/10 define the deferred capabilities and their rejection contracts.

| ID | Acceptance condition | Current evidence gates |
| --- | --- | --- |
| CFG-01 | Both UseOceanBase overloads preserve options; invalid mode/null/invalid connection inputs fail clearly in both modes. | R-01/R-02/R-03 |
| CFG-02 | Database.IsOceanBase() and invariant identify this provider through EF's real service graph. | R-01/R-02/R-03 |
| CFG-03 | Model/service-provider and compiled-query caches separate compatibility/translation modes and design-time/runtime models; compatible options reuse services. | R-01/R-02/R-03 |
| CFG-04 | Required relational services resolve with EF10 lifetimes; normal DatabaseFacade APIs have no missing services. | R-01/R-02/R-03 |
| CON-01 | Create OceanBaseConnection through public OceanBaseConnector APIs and supply ExpectedMode. | R-03/R-09 |
| CON-02 | Reject configured/detected mode conflicts before execution for supplied/created connections; cleanup remains safe after rejection. | R-03/R-09 |
| CON-03 | Caller-owned connections survive context disposal; provider-owned connections close/dispose in sync/async success/error paths. | R-03/R-09 |
| CON-04 | Sync/async open, execute, read, close and connection reuse honor mode and connector settings. | R-03/R-09 |
| CON-05 | Timeout/cancellation reach connector; pre-cancelled/in-flight query/save operations release resources. | R-03/R-09 |
| DIA-01 | Logging/interceptors report commands and failures; parameter values stay protected unless sensitive logging is explicitly enabled. | R-03/R-09 |
| DIA-02 | Update failures retain inner connector errors and entries. Direct/pre-send OperationCanceledException remains unwrapped; in-flight native unknown-outcome errors retain their explicit native flag and cancellation cause. | R-03/R-09 |
| MOD-01 | Relational conventions initialize tables, primary/composite/alternate keys, foreign keys and indexes. | R-02/R-03 |
| MOD-02 | Required/optional relationships, owned references/collections and shadow properties preserve model and CRUD behavior. | R-02/R-03 |
| MOD-03 | Generated/default/computed metadata, explicit names and schema settings produce supported SQL or validation errors. | R-02/R-03 |
| MAP-01 | Boolean and signed/unsigned integers bind/round-trip in documented ranges; predicates use valid Boolean SQL. | R-03; also see B dependencies |
| MAP-02 | Decimal precision/scale and float/double round-trip; default decimal storage preserves documented fractional precision. | R-03; also see B dependencies |
| MAP-03 | Unicode/non-Unicode, fixed/variable and bounded/unbounded text honor length and key/index constraints. | R-03; also see B dependencies |
| MAP-04 | Binary/Guid storage and byte order are explicit and round-trip through parameters, literals and generated values. | R-03; also see B dependencies |
| MAP-05 | DateTime/DateTimeOffset/DateOnly/TimeOnly/TimeSpan have documented precision, timezone and range semantics. | R-03; also see B dependencies |
| MAP-06 | Explicit store type, size, precision/scale, Unicode/fixed facets and null/DBNull parameter values are honored or rejected clearly. | R-03; also see B dependencies |
| MAP-07 | EF value converters, including ordinary enum-to-integer/string conversions, work in query/update paths. | R-03; also see B dependencies |
| SQL-01 | Escape identifiers, schema qualification and embedded delimiters; Oracle table aliases omit AS and table-free SELECT uses DUAL. | R-03; also see B dependencies |
| SQL-02 | Parameter names/placeholders follow each dialect. | R-03; also see B dependencies |
| QRY-01 | SELECT/projection, arithmetic/comparison/Boolean predicates and null semantics bind values and materialize nullable/non-null results. | R-02/R-03 |
| QRY-02 | OrderBy/ThenBy, Distinct, Skip/Take and offset-only queries return correct results, including deterministic paging/empty pages. | R-02/R-03 |
| QRY-03 | Inner/left joins, correlated subqueries, Any/All, scalar/collection Contains and Queryable Union/Concat/Intersect/Except have verified results or explicit exclusions. | R-02/R-03 |
| QRY-04 | GroupBy aggregates, Count/LongCount, Sum/Average/Min/Max handle empty and nullable inputs correctly. | R-02/R-03 |
| QRY-05 | First/Single/default variants enforce cardinality; entity/scalar async materialization matches sync. | R-02/R-03 |
| QRY-06 | Verify the supported string overloads against the feature inventory. | R-02/R-03 and string feature checks below |
| QRY-07 | Date/time members, numeric functions, conditional/coalesce expressions have a supported-overload matrix. | R-02/R-03 |
| QRY-08 | Include/ThenInclude for reference/collection navigations, tracking/no-tracking and identity resolution preserve related entities. | R-02/R-03 |
| QRY-09 | Global query filters compose with parameterized predicates and can be disabled using supported EF APIs. | R-02/R-03 |
| QRY-10 | FromSqlRaw/FromSqlInterpolated and ExecuteSqlRaw/ExecuteSqlInterpolated bind values; hostile parameter values cannot change SQL shape. | R-02/R-03 |
| QRY-11 | Unsupported predicates fail with actionable translation errors; preserve EF's allowed top-level client projection evaluation. | R-02/R-03 |
| UPD-01 | Sync/async INSERT binds values, supports explicit keys/default-only inserts and propagates generated keys. | R-03 |
| UPD-02 | Generated/default/computed readback uses same connection; MySQL LAST_INSERT_ID/Oracle RETURNING handle null/converted outputs. | R-03 |
| UPD-03 | UPDATE/DELETE bind original keys/tokens, including composite keys and nullable concurrency tokens. | R-03 |
| UPD-04 | Stale update/delete throws DbUpdateConcurrencyException with entries unless explicitly suppressed through EF interception; unchanged matched rows succeed in MySQL. | R-03 |
| UPD-05 | Multi-entity saves respect dependency order and propagate parent/child keys; unsupported readback shapes reject before DML. | R-03 |
| TXN-01 | Begin/commit/rollback and existing transaction enlistment work sync/async; incompatible connections/transactions reject. | R-03 |
| TXN-02 | SaveChanges is atomic across commands/readback; later failure rolls back earlier writes; cancellation leaves no unintended partial commit. | R-03 |
| TXN-03 | Savepoints and SaveChanges inside an existing transaction preserve caller transaction after recoverable failure. | R-03 |
| SCH-01 | DatabaseFacade services resolve; existence checks distinguish absence from authentication, permission and network failures. | R-03/R-05 |
| SCH-02 | EnsureCreated/GenerateCreateScript implement schema profile and key generation. For G-31, confirm the owned MySQL database's initial, changed and restored catalog defaults, inheritance by newly created columns, unchanged existing columns and populated EnsureCreated behavior. Apply forward and reverse migration scripts and check permissions and cleanup. Verify Oracle's explicit rejection offline. | R-03/R-05 |
| SCH-03 | EnsureDeleted has an explicit ownership/deletion boundary, preserves tenant objects outside that boundary and fails clearly outside supported profile. | R-03/R-05 |
| PKG-01 | Build net10.0 assets against OceanBaseConnector 0.4.0 with warnings as errors. | R-01/R-04 |
| PKG-02 | Packed nuspec uses OceanBase.EntityFrameworkCore and EF ranges [10.0.0,11.0.0). | R-01/R-04 |
| PKG-03 | Isolated packed consumer restores/executes provider API without project/local connector references. | R-01/R-04 |
| PKG-04 | Minimum EF10 and selected later EF10 patch restore, build and test without pinning the EF patch in the provider package. Check an EF11 restore against the upper bound. | R-01/R-04 |
| TST-01 | Selected EF relational specification classes pass; exclusions map to checklist IDs and limitations. | R-02/R-03 |
| TST-02 | Live core query/write/transaction/schema/scalar suites pass in at least one declared supported environment per mode. | R-02/R-03 |
| REL-01 | Support notes record server/connector/runtime/EF versions, tenant mode, ordinary direct endpoint, transport/options without credentials; extended deployments follow Connector support boundaries. | R-07/R-08/R-10 |
| REL-02 | Tested efcore/10.0 development checkpoint is reviewed; packaging/release checks run on release revision. | R-07/R-08/R-10 |

## Current candidate evidence

All paths below are ignored local evidence under `artifacts/release-e25988a/`; they are not published
release artifacts. Source history and older acceptance evidence remain available through Git and their
original ignored artifact directories. Do not combine old test totals with these results.

| Layer | Result | Evidence and boundary |
| --- | --- | --- |
| Source and Connector | Provider implementation `e25988ac4b7decd3b0147aa489714b5355214f8d`; public Connector 0.4.0 only | Connector nuspec revision `45c73b6b14eb528355fdbf6aa60009a4314ca3a0`, nupkg SHA-256 `95ae0ec33cd4908cb92189c1a2e7a19c5f76552bf596e791d8195e6b92e7fdae`, net10 DLL SHA-256 `ac9d35b3de1e85b96822fb2c633c3043558cb07f50e7933d2634bdb5bc40391b`. No driver source changes. |
| Windows local solution | EF 10.0.0 and 10.0.14 each passed 7,016 tests: Unit 2,908, Functional 2,568, Specification 1,538, Consumer 2; 16 existing specification skips each | `solution-10.0.0.log`, `solution-10.0.14.log` and `solution-results-*/`. Skips reject unsupported length/precision facets and primitive collection declarations; see `AdditionalModelBuilderTests.cs`. SDK 10.0.403, runtime 10.0.14. Warnings are errors. Restores used cached packages with process-local NuGet audit disabled because the service index was unavailable; this is not a current online vulnerability audit. |
| Package identity | Candidate `.1` passed archive/revision checks and independent offline consumers on both EF patches | `feed/OceanBase.EntityFrameworkCore.10.0.0-validation.releasee25988a.1.nupkg`, SHA-256 `8566682392d8b88baf205f99287f07ca1da27682f1a3a2a0e84e235fb4743204`; `package-verification.log`, `consumer-*.log`. Runtime DLL SHA-256 `71fa7805f431fd5982f2d97d4598eb72d76b114055b9a28334eafdc001709f71`. |
| README repacks and final package | Final `.3` updates support notes; all `lib/` entries remain byte-identical to tested `.1`/`.2`, including implementation `e25988a` | `feed/OceanBase.EntityFrameworkCore.10.0.0-validation.releasee25988a.3.nupkg`, SHA-256 `dbaa855ea9d2e4c0bc6d8742bf116de99b51ab371ae0b40f85b2c7e6dd6a62d7`; `final-readme-repack-identities.log`, `final-package-verification.log`, `final-consumer-*.log`. Final README matches saved source. Full baseline/tooling/AOT consumed `.1`; the 26-case-per-patch native addition/smoke consumed intermediate `.2`. Final `.3` has archive and isolated consumer checks plus exact runtime-payload identity; no third full native run is claimed. |
| EF major bound | Strict restore rejected synthetic EF/Relational 11.0.0 metadata with NU1608 for both `[10.0.0,11.0.0)` dependencies | `upper-bound/restore.log`. This tests NuGet dependency constraints, not an actual EF11 runtime. |
| Native core regression | Both EF patches passed all 921 baseline cases, with zero failures or skips in the completed serial results | Discovery contains 921 cases: 908 native and 13 offline checks, across 34 classes. `results-10.0.0/`, `results-10.0.14/`, `native-run-*.log`, `native-continued-*.log`. Initial parallel runs each passed 55/56 Graph cases and encountered remote connection resets at the same second during schema creation. Originals remain in `initial-interruption-*/`; an isolated Include rerun passed 8/8 in `diagnostics/graph-diagnostic.trx`. Continuation runs classes and patches serially without adding automatic retries. No server/driver defect is inferred from the socket resets alone. |
| Added ordinary native contracts | Each patch passed 16 bulk-DML/TPT/splitting/seeding cases, two blocked-save cancellation/recovery cases and eight repeated connection/computed/read-only smoke cases; zero failures/skips | `repack-results-*/`, `repack-native-run-*.log`. Four shared-token removal cases initially failed per patch before SQL generation; `initial-shared-token-model-*/` preserves them. A no-network EF command-preparation probe in `diagnostics/shared-token/` identifies upstream shared-column propagation: explicitly update both application-managed token values before deleting the optional dependent. Corrected tests assert fresh retained-owner version, absent dependents and physical row counts. No Provider/Connector implementation change was needed. |
| Computed columns and test portability | Two model-diff regressions first failed and then passed for string/binary synthetic defaults; 13/13 migration unit cases and 4/4 native cases passed | The final native cases cover numeric/string/binary unchanged virtual computed columns, data preservation, required/comment changes, rejection with existing NULLs, reverse DDL and recomputation after writes. EF 10.0.14's equivalent parameter mapping reuse is checked by mapping facets and binding semantics, rather than reference identity. Connection probes now read the configured scope without embedding a tenant name; two cases passed on each patch in `probe-results-*/`. |
| Offline tooling and generated models | Both patches passed catalog generation, compiled C#/metadata/DDL, eight model/proxy combinations each, migration add/list/remove/pending and forward/reverse/idempotent SQL, Windows bundle build/help | `tooling/`; all 12 consumers are package-only and match the candidate DLL/Connector identity in `tooling/package-identities.txt`. Native migration applications passed 12/12, lock mode/patch checks 4/4 and selected official runtime cases 624/624 with zero skips. `tooling/native-results.txt`, `official-results.txt` and scoped TRX/phase logs retain the evidence. An ignored duplicate client-delimiter adapter initially failed before client execution; only that affected phase was rerun after fixing the harness. Separate SQL*Plus 21.23 control failed ORA-12569 before script execution and remains unaccepted; MySQL CLI 8.4.13 applied Oracle scripts on both patches. |
| Actual NativeAOT | EF 10.0.14 Windows x64 EXEs passed in both modes: 12 numeric/temporal sync/async query executions with NULL/rebound parameters and 12 built-in mapping JSON roundtrips | `aot/identities.log`, `aot/aot-*/`; dynamic code is disabled and runtime query/model compilation guards remain enabled. Raw publish logs retain 21 warning lines per mode: CS8669 x4, CS9270 x6, IL2026 x2, IL3050 x2, IL2104 x4, IL3053 x3. No new suppressions. C04 offline re-generation and native replay passed eight model/proxy combinations plus four generated-query paths per patch (16 and eight total), with owned-object catalog cleanup confirmed. `aot/c04-*/15-live-compiled-proxies.log`. CLI 10.0.15 with resolved EF Design/runtime packages asserted at requested patches. |
| MySQL sequence metadata integration (D-06) | Ordinary-tenant direct probes confirm a stable declaration start in `DBA_OB_SEQUENCE_OBJECTS.START_WITH`; the public Connector sequence catalog omits it | `artifacts/release-331ff0c/connector/catalog-probe.log` and `owned-sequence.log`. START_WITH stayed 41 while LAST_NUMBER advanced to 56; NEXTVAL was 41/44/47/50 and catalog reads consumed no value. Same-name cross-database identity mapping is not established: the observed `DBA_OB_DATABASES` projection lacks DATABASE_ID. The Connector already executes this catalog SQL; this is an omitted GetSchema field/convenience contract, not missing sequence or SQL-execution support. Compared Connector/J 2.4.14/2.4.19 public APIs also exposed no dedicated declaration-start method; standard Java catalog SQL execution was not tested. Static comparison evidence is in `connector-java/`. The gap inventory records the optional API/catalog-extension proposal and closure tests. No name-only Provider workaround or driver change was made. |
## Explicit coverage boundaries and final review

| Item | Remaining evidence or explicit boundary |
| --- | --- |
| Ordinary candidate replays | Complete for the declared scope: baseline, added native contracts, package smoke, official runtime, C04, migration application and locking. Preserve the initial transport, ignored-harness and shared-token test-model failures separately from successful reruns |
| Administrative extensions | SCH-02 owned-database default collation (G-31) and namespace creation, and SCH-03 namespace deletion remain unaccepted administrative extensions. Owned-table and existing-namespace seeding tests do not pass these entire Required rows. No privileged namespace changes were made. Wider inheritance/splitting/bulk shapes retain their documented scope boundaries |
| C-01 extensions | Actual NativeAOT is accepted only for the recorded Windows x64 EF 10.0.14 generated numeric/temporal queries and built-in converted mapping profile. Publish warnings remain visible. Arbitrary converters, raw FromSqlRaw precompilation, broader query shapes, other platforms and performance are outside that profile |
| D-06 metadata integration | OceanBase and Connector ordinary commands support declared-start catalog reads. The existing public sequence catalog omits this field. Exact cross-database identity must be solved before the deferred EF reverse-engineering integration; this is not a database sequence or transport defect |
| Other deployments/performance | Linux, TLS and HA are explicitly excluded for this release. SQL*Plus endpoint transport, named zones/DST, other server versions, Proxy/OB20, wider declaration/converter/collation combinations, optimizer/isolation/performance and additional upstream runtime families need their own acceptance when support is claimed |
| Cleanup/review | Candidate-owned cleanup/absence checks and independent review passed. `catalog-closure.log` and `catalog-created-evidence.log` explicitly retain the nonzero global result: ten tables/four sequences. All ten tables and the Oracle sequence predate candidate implementation `e25988a`; three MySQL sequence dates are unavailable, and only one has a positive older generated-model reference. Unidentified ownership is not inferred from the prefix. No global deletion was performed. Final review leaves no unresolved Critical/Important finding |

These are acceptance boundaries, not confirmed production defects. Assign layer responsibility only
after a real failure and comparison through the public Connector API. Deferred/unsupported functionality
remains in the feature/gap inventories and is not expanded merely to raise a passing-test count.
## Safe reproduction and recording

- Connection configuration loads only from User Secrets for `OceanBase.EntityFrameworkCore.LiveTests`, using
  `ConnectionStrings:OceanBaseMySql` and `ConnectionStrings:OceanBaseOracle`; management connections have separate public key names, and no values enter the repository.
- The ordinary profile uses server 4.4.2.2 in both modes, TLS Disabled, Pooling/OB20/compression disabled, and UseAffectedRows=false;
  record query connection/command timeouts from configuration. Oracle generated-value writes explicitly enable `UseOraclePrepareExecute` on the test connection.
- The user authorized test processes against servers without TLS to set `OCEANBASE_LIVE_DISABLE_TLS=1`; do not change User Secrets or Provider defaults.
- Read-only queries create no tables. Schema tests require both live/schema opt-ins and create unique `EFT_` tables within the currently authorized database/schema;
  clean up only objects whose CREATE was observed to succeed, in reverse dependency order, then independently check leftovers read-only. Do not touch business objects or create/delete namespaces without authorization.
- Record only safe error stages/types/server codes; do not output configuration values, credentials, arbitrary native inner messages or sensitive SQL parameters.
- Use isolated source/output/restore directories for the minimum and later EF patches, and record the actual assets and DLLs; never mix two different cached artifacts.

Ordinary local regression (excluding the live project):

```powershell
dotnet test OceanBase.EntityFrameworkCore.slnx -c Release --logger 'trx;LogFilePrefix=release' --results-directory artifacts/release/local
```

For read-only profiles, check local shape first, then explicitly run native cases (enable schema opt-in only when DDL is needed):

```powershell
dotnet test tests/OceanBase.EntityFrameworkCore.LiveTests -c Release --filter FullyQualifiedName~ReadOnlyQueryProfileTests
$env:OCEANBASE_RUN_LIVE_TESTS='1'
$env:OCEANBASE_LIVE_DISABLE_TLS='1'
dotnet test tests/OceanBase.EntityFrameworkCore.LiveTests -c Release --filter FullyQualifiedName~LiveReadOnlyQueryTests
$env:OCEANBASE_RUN_LIVE_SCHEMA_TESTS='1'
dotnet test tests/OceanBase.EntityFrameworkCore.LiveTests -c Release --logger 'trx;LogFilePrefix=release-live' --results-directory artifacts/release/live
```

Package-content gates use the existing [Verify-Package.ps1](../../tests/OceanBase.EntityFrameworkCore.PackageConsumer/Verify-Package.ps1),
package consumers use [PackageConsumer](../../tests/OceanBase.EntityFrameworkCore.PackageConsumer/), and tooling uses
[ToolingConsumer](../../tests/OceanBase.EntityFrameworkCore.ToolingConsumer/). Explicitly select the package version awaiting acceptance,
the EF version and isolated restore/cache directories at run time; existing bin/obj directories or the global cache cannot prove that the new package was used.
Pass `-ExpectedSourceRevision` with the full commit SHA to check the assembly revision inside the archive. CI supplies the checked-out commit.

Run the temporal feature regressions serially with focused filters and aligned restore assets; the native
command uses the live/schema opt-ins above and creates only fixture-owned tables:

```powershell
dotnet test tests/OceanBase.EntityFrameworkCore.UnitTests -c Release --no-restore --filter FullyQualifiedName~DateTimeNumericAddQueryTests
dotnet test tests/OceanBase.EntityFrameworkCore.FunctionalTests -c Release --no-restore --filter FullyQualifiedName~DateTimeCalendarAddFunctionalTests
dotnet test tests/OceanBase.EntityFrameworkCore.LiveTests -c Release --no-restore --filter 'FullyQualifiedName~DateTime_numeric|FullyQualifiedName~DateTime_date_differences'
```

For each result, update only the corresponding gate/evidence row, retaining the revision, environment, pass/fail/skip counts, open items and artifact entry points.
Do not create another test report, plan or progress document for a small feature.
