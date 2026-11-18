# EF Core 10 Release Acceptance Checklist

Updated: 2026-11-18. This checklist records only tests, evidence and release gates; **do not use it to count unimplemented features**.
See the [feature inventory](efcore-10-feature-matrix.md) for code scope and the [gap inventory](efcore-10-gap-matrix.md) for individual development states.
The current feature work adds DateTime.AddDays through AddMicroseconds(double), AddTicks(long) and Subtract(DateTime)
after `f119f51` (DateTime.Add/Subtract(TimeSpan)); no final release revision has been selected.
Develop missing features first; the full pre-release acceptance campaign follows the selected development queue.

## Acceptance states and completion criteria

- `Passed` applies only to the recorded revision, package identity, mode and environment; `Historical pass` does not establish acceptance of the current revision.
  `Partially passed`/`Pending acceptance`/`Blocked` leave the gate open; unexecuted tests, skips and fake readers must not be reported as live-database passes.
- For every declared runtime profile, check MySQL/Oracle, sync/async, positive/NULL/boundary/rejection/recovery cases; check SQL, parameters, readers,
  ownership, cancellation and error contracts separately. Missing test coverage updates only this checklist; it does not make an implemented feature unimplemented.
- The final release must align source, package, consumer and Connector artifact identities; independent code review must leave no unresolved Critical/Important issues.
  State remaining Minor issues, skips and scope exclusions explicitly. Missing-feature development continues while this checklist awaits infrastructure.

## Release gates

| Gate | State | Current evidence / remaining conditions |
| --- | --- | --- |
| R-01 Dependency and source identity | Partially passed | Latest numeric/date-difference feature checks use the aligned Connector 0.3.0 identity recorded below; it differs from the duration checkpoint. Historical results do not carry forward across artifacts with the same package version; final release identities remain pending |
| R-02 Local / TST-01 | Partially passed | Numeric-add feature regression is recorded below; historical duration totals across projects for each patch were 6699 passed/16 existing skips. Complete runtime families remain uncovered, and the final release revision requires a rerun |
| R-03 Live database / TST-02 | Partially passed | Targeted numeric-add evidence and historical duration/ordinary profiles are listed below with separate dependency identities. Broader live evidence for writes/concurrency, transaction atomicity/cancellation, Schema, inheritance/lazy/compiled and other areas remains incomplete |
| R-04 PKG-01..04 | Partially passed | duration-r2 package contents/EF range, package-only consumers for both patches and modes, and DLL/README/Connector identity passed; the final release package, consumer, EF11 negative dependency check and version consistency still require execution |
| R-05 Tooling / DEF-01/02/08 | Partially passed | Existing migration CLI, generated context/compiled-model C#, and Windows bundle build/help have local evidence; native catalog checks with the current identity, bundle application, script-client execution/locking and broader generated combinations await acceptance |
| R-06 Platforms | Pending acceptance | Current evidence is local Windows only; repository CI previously passed Windows/Linux for e41981e, which does not cover the current revision; release requires CI on both platforms and portable-path/line-ending checks |
| R-07 Environment / REL-01 | Partially passed | Ordinary direct connections in both modes use OceanBase 4.4.2.2 with TLS Disabled; the complete supported environment/options and runtime/EF/Connector identities must be fixed alongside final results; additional deployments extend only the declared scope |
| R-08 Review / REL-02 | Partially passed | Independent numeric-add review has no remaining findings. Date-difference review findings on converters, duration operators, Compare/CompareTo and mixed set/membership carriers are addressed with regression tests; NULL compensation and fractional scalar mapping also have regressions. Historical Minor items below and final-release revision review remain outstanding |
| R-09 Sensitive information/cleanup | Partially passed | The latest checks found 0 EFT_ leftovers in both modes; the final run must recheck object cleanup, log redaction and exclusion of credentials/connection strings from Git and artifacts |
| R-10 Release operations | Pending acceptance | Version, README support claims, dependency ranges, release notes, tag and package revision must agree; a local commit does not authorize push/publish; nothing has been released |

## Acceptance requirements by feature

Preserve the original 53 Required acceptance IDs and conditions. The mode columns below record only historical closure results from the original checklist;
they are neither code-implementation states nor pass conclusions for the current dependency/final revision. Update only the relevant evidence row each time; do not add module documents.
Existing DEF-01/02/03/07/08/09 functionality still requires R-03/R-05 checks; deferred DEF-04/05/06/10 capabilities are excluded from positive-support acceptance, while their rejection contracts remain required.

| ID | Acceptance condition | MySQL history | Oracle history | Current evidence gates |
| --- | --- | --- | --- | --- |
| CFG-01 | Both UseOceanBase overloads preserve options; invalid mode/null/invalid connection inputs fail clearly in both modes. | Historical pass | Historical pass | R-01/R-02/R-03 |
| CFG-02 | Database.IsOceanBase() and invariant identify this provider through EF's real service graph. | Historical pass | Historical pass | R-01/R-02/R-03 |
| CFG-03 | Model/service-provider and compiled-query caches separate compatibility/translation modes and design-time/runtime models; compatible options reuse services. | Historical pass | Historical pass | R-01/R-02/R-03 |
| CFG-04 | Required relational services resolve with EF10 lifetimes; normal DatabaseFacade APIs have no missing services. | Pending acceptance | Pending acceptance | R-01/R-02/R-03 |
| CON-01 | Create OceanBaseConnection through public OceanBaseConnector 0.3.0 APIs and supply ExpectedMode. | Historical pass | Historical pass | R-03/R-09 |
| CON-02 | Reject configured/detected mode conflicts before execution for supplied/created connections; cleanup remains safe after rejection. | Historical pass | Historical pass | R-03/R-09 |
| CON-03 | Caller-owned connections survive context disposal; provider-owned connections close/dispose in sync/async success/error paths. | Historical pass | Historical pass | R-03/R-09 |
| CON-04 | Sync/async open, execute, read, close and connection reuse honor mode and connector settings. | Historical pass | Historical pass | R-03/R-09 |
| CON-05 | Timeout/cancellation reach connector; pre-cancelled/in-flight query/save operations release resources. | Pending acceptance | Pending acceptance | R-03/R-09 |
| DIA-01 | Logging/interceptors report commands and failures; parameter values stay protected unless sensitive logging is explicitly enabled. | Historical pass | Historical pass | R-03/R-09 |
| DIA-02 | Update failures retain inner connector errors and entries; cancellation is not wrapped as DbUpdateException. | Pending acceptance | Pending acceptance | R-03/R-09 |
| MOD-01 | Relational conventions initialize tables, primary/composite/alternate keys, foreign keys and indexes. | Pending acceptance | Pending acceptance | R-02/R-03 |
| MOD-02 | Required/optional relationships, owned references/collections and shadow properties preserve model and CRUD behavior. | Pending acceptance | Pending acceptance | R-02/R-03 |
| MOD-03 | Generated/default/computed metadata, explicit names and schema settings produce supported SQL or validation errors. | Pending acceptance | Pending acceptance | R-02/R-03 |
| MAP-01 | Boolean and signed/unsigned integers bind/round-trip in documented ranges; predicates use valid Boolean SQL. | Pending acceptance | Pending acceptance | R-03; also see B dependencies |
| MAP-02 | Decimal precision/scale and float/double round-trip; default decimal storage preserves documented fractional precision. | Pending acceptance | Pending acceptance | R-03; also see B dependencies |
| MAP-03 | Unicode/non-Unicode, fixed/variable and bounded/unbounded text honor length and key/index constraints. | Pending acceptance | Pending acceptance | R-03; also see B dependencies |
| MAP-04 | Binary/Guid storage and byte order are explicit and round-trip through parameters, literals and generated values. | Pending acceptance | Pending acceptance | R-03; also see B dependencies |
| MAP-05 | DateTime/DateTimeOffset/DateOnly/TimeOnly/TimeSpan have documented precision, timezone and range semantics. | Pending acceptance | Pending acceptance | R-03; also see B dependencies |
| MAP-06 | Explicit store type, size, precision/scale, Unicode/fixed facets and null/DBNull parameter values are honored or rejected clearly. | Pending acceptance | Pending acceptance | R-03; also see B dependencies |
| MAP-07 | EF value converters, including ordinary enum-to-integer/string conversions, work in query/update paths. | Pending acceptance | Pending acceptance | R-03; also see B dependencies |
| SQL-01 | Escape identifiers, schema qualification and embedded delimiters; Oracle table aliases omit AS and table-free SELECT uses DUAL. | Pending acceptance | Pending acceptance | R-03; also see B dependencies |
| SQL-02 | Parameter names/placeholders follow each dialect. | Historical pass | Historical pass | R-03; also see B dependencies |
| QRY-01 | SELECT/projection, arithmetic/comparison/Boolean predicates and null semantics bind values and materialize nullable/non-null results. | Pending acceptance | Pending acceptance | R-02/R-03 |
| QRY-02 | OrderBy/ThenBy, Distinct, Skip/Take and offset-only queries return correct results, including deterministic paging/empty pages. | Historical pass | Historical pass | R-02/R-03 |
| QRY-03 | Inner/left joins, correlated subqueries, Any/All, scalar/collection Contains and Queryable Union/Concat/Intersect/Except have verified results or explicit exclusions. | Pending acceptance | Pending acceptance | R-02/R-03 |
| QRY-04 | GroupBy aggregates, Count/LongCount, Sum/Average/Min/Max handle empty and nullable inputs correctly. | Pending acceptance | Historical pass | R-02/R-03 |
| QRY-05 | First/Single/default variants enforce cardinality; entity/scalar async materialization matches sync. | Historical pass | Historical pass | R-02/R-03 |
| QRY-06 | String translations have a supported-overload matrix: equality, Contains, StartsWith/EndsWith, length, substring, case and trim. | Historical pass | Historical pass | R-02/R-03 |
| QRY-07 | Date/time members, numeric functions, conditional/coalesce expressions have a supported-overload matrix. | Pending acceptance | Pending acceptance | R-02/R-03 |
| QRY-08 | Include/ThenInclude for reference/collection navigations, tracking/no-tracking and identity resolution preserve related entities. | Historical pass | Historical pass | R-02/R-03 |
| QRY-09 | Global query filters compose with parameterized predicates and can be disabled using supported EF APIs. | Historical pass | Historical pass | R-02/R-03 |
| QRY-10 | FromSqlRaw/FromSqlInterpolated and ExecuteSqlRaw/ExecuteSqlInterpolated bind values; hostile parameter values cannot change SQL shape. | Historical pass | Historical pass | R-02/R-03 |
| QRY-11 | Unsupported predicates fail with actionable translation errors; preserve EF's allowed top-level client projection evaluation. | Pending acceptance | Pending acceptance | R-02/R-03 |
| UPD-01 | Sync/async INSERT binds values, supports explicit keys/default-only inserts and propagates generated keys. | Pending acceptance | Pending acceptance | R-03 |
| UPD-02 | Generated/default/computed readback uses same connection; MySQL LAST_INSERT_ID/Oracle RETURNING handle null/converted outputs. | Pending acceptance | Pending acceptance | R-03 |
| UPD-03 | UPDATE/DELETE bind original keys/tokens, including composite keys and nullable concurrency tokens. | Pending acceptance | Pending acceptance | R-03 |
| UPD-04 | Stale update/delete throws DbUpdateConcurrencyException with entries unless explicitly suppressed through EF interception; unchanged matched rows succeed in MySQL. | Pending acceptance | Pending acceptance | R-03 |
| UPD-05 | Multi-entity saves respect dependency order and propagate parent/child keys; unsupported readback shapes reject before DML. | Pending acceptance | Pending acceptance | R-03 |
| TXN-01 | Begin/commit/rollback and existing transaction enlistment work sync/async; incompatible connections/transactions reject. | Pending acceptance | Pending acceptance | R-03 |
| TXN-02 | SaveChanges is atomic across commands/readback; later failure rolls back earlier writes; cancellation leaves no unintended partial commit. | Pending acceptance | Pending acceptance | R-03 |
| TXN-03 | Savepoints and SaveChanges inside an existing transaction preserve caller transaction after recoverable failure. | Pending acceptance | Pending acceptance | R-03 |
| SCH-01 | DatabaseFacade services resolve; existence checks distinguish absence from authentication, permission and network failures. | Pending acceptance | Pending acceptance | R-03/R-05 |
| SCH-02 | EnsureCreated/GenerateCreateScript implement schema profile and key generation; existing populated schemas are handled explicitly. | Pending acceptance | Pending acceptance | R-03/R-05 |
| SCH-03 | EnsureDeleted has an explicit ownership/deletion boundary, preserves tenant objects outside that boundary and fails clearly outside supported profile. | Pending acceptance | Pending acceptance | R-03/R-05 |
| PKG-01 | Build net10.0 assets against OceanBaseConnector 0.3.0 with warnings as errors. | Historical pass | Historical pass | R-01/R-04 |
| PKG-02 | Packed nuspec uses OceanBase.EntityFrameworkCore and EF ranges [10.0.0,11.0.0). | Historical pass | Historical pass | R-01/R-04 |
| PKG-03 | Isolated packed consumer restores/executes provider API without project/local connector references. | Historical pass | Historical pass | R-01/R-04 |
| PKG-04 | Minimum EF10 and selected later available EF10 patch restore/build/test without pinning the EF patch in the provider package; EF11 cannot satisfy dependencies. | Historical pass | Historical pass | R-01/R-04 |
| TST-01 | Selected EF relational specification classes pass; exclusions map to checklist IDs and limitations. | Pending acceptance | Pending acceptance | R-02/R-03 |
| TST-02 | Live core query/write/transaction/schema/scalar suites pass in at least one declared supported environment per mode. | Pending acceptance | Pending acceptance | R-02/R-03 |
| REL-01 | Support notes record server/connector/runtime/EF versions, tenant mode, ordinary direct endpoint, transport/options without credentials; extended deployments follow Connector support boundaries. | Pending acceptance | Pending acceptance | R-07/R-08/R-10 |
| REL-02 | Tested efcore/10.0 development checkpoint is reviewed; packaging/release checks run on release revision. | Pending acceptance | Pending acceptance | R-07/R-08/R-10 |

## Currently referenceable evidence

| Layer | Latest result | Boundary / entry point |
| --- | --- | --- |
| Numeric date additions and date differences, EF10.0.0 | Unit 2708/2708 and Functional 2500/2500 passed | `artifacts/date-difference/unit-final-aligned/`, `functional-final-aligned/`; covers all eight signatures, fractional-unit SQL, exact Int64 ticks/TimeSpan shaping, duration members/composition, NULL/precision/converters, mixed-carrier normalization/rejection, parameter refresh/ownership and Oracle naming/collisions. These are development regressions, not final release acceptance or EF10.0.14 evidence |
| Numeric day/hour native feature checks, EF10.0.0 | 8/8 native, 0 failures/skips | `artifacts/numeric-date-add/native-closed/`; final serial run covers both modes, sync/async and two NULL settings with constants/refreshed parameters/native Double columns, large signed fractional offsets, signed 1/9/10/11-tick boundaries and invalid-unit NULL guards. Uses the ordinary direct fixture with TLS Disabled; it is not a full native-suite or TLS acceptance run |
| Numeric minute through tick native feature checks, EF10.0.0 | 8/8 native, 0 failures/skips | `artifacts/numeric-date-add/small-units-native/`; both modes, sync/async and two NULL settings cover signed fractional parameters, predicates/composition/converters, DATE promotion, nullable native Double/Int64 columns, Int64 offsets above 2^53, signed 1/9/10/11-tick boundaries, unit-range guards and Int64.MinValue/MaxValue. Ordinary direct profile with TLS Disabled; not full native-suite or TLS acceptance |
| Date-difference native feature checks, EF10.0.0 | 8/8 native, 0 failures/skips | `artifacts/date-difference/native-final-aligned/`; both modes, sync/async and two NULL settings cover signed long/subsecond differences, DATE operands with fractional scalars, all duration components/totals, scalar converter ownership, mixed ordinary duration comparisons/Compare/CompareTo/CASE/COALESCE, timestamp composition and date-range endpoints. Ordinary direct profile with TLS Disabled; not full native-suite or TLS acceptance |
| Duration local EF10.0.0/10.0.14 | Historical totals across projects for each patch: 6699 passed/16 skips | `artifacts/datetime-duration/root-final/`, `compat-final/`; the new Oracle null-converter fixture initially had 1 failure; after correction, both patches passed the complete Unit suite at 2672/2672 in `unit-final/`, `compat-unit-final/`; source for the other three projects did not change afterward |
| Documented-capability check | For each mode, 37 unsupported predicate/sequence probes passed, along with supported Add/Subtract and ordinary-string positive controls | `artifacts/efcore-doc-cleanup/probe/`; invokes only the real Provider's offline ToQueryString, opens no connection, and is not live release acceptance |
| Duration native checks with its fixed dependency | Each patch: 8/8, 0 failures/skips | `artifacts/datetime-duration/root-final-live/`, `compat-final-live/`; covers only the duration family, sync/async in both modes and two NULL configurations; it does not establish a full 626-case pass |
| Duration package-only checks | `10.0.0-datetime-duration-r2-20261117` contents and consumers for both patches and modes passed | `artifacts/datetime-duration/feed/`, `consumer-10.0.0-final.log`, `consumer-10.0.14-final.log`; DLL/README/dependency identity matches, with no local project references |
| Historical full native suite | After raw/diagnostics, each patch: 618/618 (605 native, 13 offline) | `artifacts/raw-diagnostics/`; same-version Connector cache/old assets differed, so these historical results cannot establish complete acceptance with the current fixed dependency |
| Historical module profiles | Connection lifecycle 56; raw/diagnostics 28; graph 56+ownership 10; temporal 72 plus endpoint/Kind; numeric 64; string 64; relational/aggregate/storage and others | Test source remains; historical TRX files are in the corresponding `artifacts/`; module plans have been merged and deleted; do not add these counts as feature counts or use them to claim a current full-suite pass |
| Specification tests | 6 complete relationship/inheritance/owned model families, plus non-relational/service/partial write-SQL profiles | [SpecificationTests](../../tests/OceanBase.EntityFrameworkCore.SpecificationTests/); 16 skips retain the reasons in source. Metadata tests are not official store-backed query/write/runtime acceptance |
| Historical cross-platform checks | All four Windows/Linux gates passed for e41981e | [Historical CI](https://github.com/cocosip/efcore.oceanbase/actions/runs/42865290840); the current revision requires a rerun |

Different cached Connector 0.3.0 artifacts were found locally (including an old assembly 0.2.0); some old assets even still referenced package 0.2.0.
After alignment, the current duration module uses nuspec revision `6de85dee1849b04fc937f4419c4ff7e17fce64b4`,
assembly 0.3.0.0, and net10 DLL SHA256
`aa1a7b3dafb0a3b543a3cfbcde0bdefff5dff3a7ff81dfeac0c7edbd5b01b19f`.
The later numeric-add development run instead resolves the available Connector 0.3.0 package with nuspec revision
`7f0cf7e544bf8b944d584ddf9116bea03ec4c5df`, assembly 0.3.0.0, and net10 DLL SHA256
`f4105bdc9d8b33a77f7223ac09265faa497267add542812fc1763c9bf8ab0c55` in Unit, Functional and Live test outputs.
Its scoped results do not replace the earlier duration artifact's evidence; no Connector source or package configuration was changed.
Minute-through-tick and early date-difference local builds resolved the earlier artifact from the shared cache;
the latest local results above supersede them after aligning the package folder with native checks and verifying all three output DLL hashes.
Release records must state the actual package hash/revision, not just the version.
NuGet network/vulnerability sources previously failed; restore completed using existing packages plus a local feed. This documentation cleanup changes neither dependencies nor NuGet configuration.

## Outstanding coverage and independent review items

| Item | Remaining evidence |
| --- | --- |
| Writes/errors/cancellation | Inner errors/entries for real constraint failures, generated keys/defaults/computed values, complete graphs/concurrency/transaction recovery; interrupted SaveChanges, rollback atomicity and connection-lease recovery |
| Transactions | Commit/rollback/savepoint in both modes, failure and cancellation recovery inside existing transactions; driver handles do not establish EF atomicity acceptance; do not claim that implicitly committed DDL is rollbackable |
| Schema/migrations/scaffolding | Permission-versus-absence classification, creation/deletion ownership, existing schemas, real catalog/DDL/default/identity/index behavior, history locking, script-client/bundle application and cleanup after failure |
| Mapping and queries | Broader declarations/capacity/charset/collation/converter coverage, refresh of deeply captured Skip/Take values, raw/inheritance/lazy/compiled combinations, complete official runtime families; review B-01..09 at their owning layers |
| Aggregate Minor | The native fixture's Tag depends on Bucket and cannot independently detect omission of the second composite-key component; strengthen seed discrimination and result assertions |
| Graph Minor 1 | Per-command SQL-shape/binding-refresh checks for subsequent split commands are not strict enough; existing result evidence cannot replace proof of invariants for each command |
| Graph Minor 2 | With an optional FK and the Tenant filter retained, the fixture cannot independently detect omission of the TenantId join condition; add result-sensitivity checks with the Tenant filter selectively disabled |
| Temporal storage Minor | The immediately generated DefaultStamp lacks an independent DateTimeKind assertion before Clear tracking; Kind was checked on a fresh query, which does not establish a pass for the immediate value |
| Other deployments/performance | Named zones/DST, other server versions, memory/isolation/optimizer performance, Proxy/TLS/HA and similar environments extend coverage only when support is claimed; TLS Disabled must not be reported as TLS acceptance |

These are evidence gaps, not confirmed new production defects; assign responsibility only after a real failure and comparison through the public Connector API.

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

Run the temporal feature regressions serially with focused filters and aligned restore assets; the native
command uses the live/schema opt-ins above and creates only fixture-owned tables:

```powershell
dotnet test tests/OceanBase.EntityFrameworkCore.UnitTests -c Release --no-restore --filter FullyQualifiedName~DateTimeNumericAddQueryTests
dotnet test tests/OceanBase.EntityFrameworkCore.FunctionalTests -c Release --no-restore --filter FullyQualifiedName~DateTimeCalendarAddFunctionalTests
dotnet test tests/OceanBase.EntityFrameworkCore.LiveTests -c Release --no-restore --filter 'FullyQualifiedName~DateTime_numeric|FullyQualifiedName~DateTime_date_differences'
```

For each result, update only the corresponding gate/evidence row, retaining the revision, environment, pass/fail/skip counts, open items and artifact entry points.
Do not create another test report, plan or progress document for a small feature.
