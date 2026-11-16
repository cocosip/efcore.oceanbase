# EF Core 10 Development and Release Roadmap

**Updated:** 2026-11-16
**Branch:** `efcore/10.0`  
**State:** Active local development; write SQL specifications are the latest local module, following
DateOnly/DateTime calendar arithmetic, `DateTimeOffset.UtcTicks`, scalar extrema and service lifetimes.
**Acceptance authority:** [efcore-10-feature-checklist.md](efcore-10-feature-checklist.md)

The `REL-01` support record now exists at [efcore-10-release-support.md](efcore-10-release-support.md),
but remains `In progress` while the supplied endpoint's direct/proxy routing, transport options and
pre-authentication failure are unresolved.

**Execution control:** Follow [efcore-10-development-acceptance-plan.md](efcore-10-development-acceptance-plan.md)
for the ordered waves, evidence levels and handoff format. The latest Release SpecificationTests result is 1,482
passed and 16 explicitly mapped skips (1,498 total); Release/Debug Unit/Functional are 2,421/1,955 and
ConsumerTests 2. Isolated EF10.0.14 Release has the same totals. The write SQL family adds thirty passing
cases with three explicit profile adaptations per mode and no new skips. Sequence SQL entrypoints now
reject consistently while DEF-06 stays Deferred. Independent review, package content and both-patch/both-mode
consumers pass. Native DML/readback/transaction and broader runtime acceptance remain open; see
[the module plan](efcore-10-update-sql-specification-plan.md).

This is the execution board for the first OceanBase EF Core 10 provider. It separates provider work from
evidence work so an intercepted test cannot be mistaken for native OceanBase acceptance.

## Current baseline

The following required rows have complete local/package evidence for both modes:

- `CFG-01..03`, `CON-01`, `SQL-02`
- `CFG-04` local service-graph/facade characterization and service-registration lifetime now have both-mode local
  evidence; native schema and lifecycle acceptance remain open.
- `PKG-01..04`
- HasData now participates in GenerateCreateScript, CreateTables and EnsureCreated for both modes. EF
  orders/converts seed operations; excluded tables are filtered. DDL precedes an owned model-data transaction,
  and EnsureCreated callbacks retain a separate phase. Twenty-two focused cases cover lifecycle, rollback,
  scope and ownership; independent review's combined rollback/disposal lease leak is fixed. See the
  [model-data plan](efcore-10-model-data-plan.md): full source suites each pass 5028 tests; the same baseline-
  built package passes both-patch runtime/generated-context/compiled-model/full CLI and four bundle build/help
  checks, with matching graphs/hashes. Native acceptance,
  identity behavior and application recovery after partial DDL remain open under SCH-02/DEF-09.
- Lazy-loading integration now verifies official optional EF Proxies and injected ILazyLoader through
  existing both-mode provider queries/writes. Sixty-four runtime cases cover references/collections,
  tracking/filters/state/lifecycle/recovery and writes. Independent review's injected-reference and empty-
  target gaps are fixed. Full source suites each pass 5007 tests; a baseline-built package passes content
  and both-mode public runtime/proxy consumers on both patches, with matching graphs/DLL hashes. See the
  [lazy-loading plan](efcore-10-lazy-loading-plan.md). No new loader or driver change was needed. Native
  behavior, streaming nested queries and proxy/compiled-model/change-tracking/AOT combinations remain open.
- Ordinary compiled models now support actual dbcontext optimize, generated-code compilation and UseModel
  in both modes. Six mapping families preserve converters/facets/sentinels and write validation; mode tags
  reject opposite-mode reuse. Independent review's unhinted-converter physical mapping regression is fixed.
  Full source suites each pass 4943 tests. One baseline-built r2 nupkg passes content, both-mode runtime,
  generated-context/optimized-model/full CLI consumers on both patches and four bundle build/help checks.
  Package graphs and DLL hashes match; see the [compiled-model plan](efcore-10-compiled-model-plan.md).
  EF filter restrictions remain; NativeAOT/precompiled optimization and native/performance evidence remain open.
- Split queries now buffer effective SplitQuery result sets before opening another native reader, using
  EF graph coordinators with public context-wide options and per-query overrides. Seven unit and 72
  runtime cases cover tracking, nested/sibling graphs, filters/paging, empty results and retry/error
  cleanup. Independent review's SQL verification gap is fixed; full source suites each pass 4930. The
  same baseline-built nupkg passes content, both-mode runtime/generated-context/full CLI consumers on
  both patches and four bundle build/help checks, with matching graphs/DLL hashes.
  See the [split-query plan](efcore-10-split-query-plan.md). DEF-08 is partial; native behavior,
  NativeAOT/precompiled optimization and upstream runtime specifications remain unfinished work.
- Opt-in execution retries now use EF's operation strategy with conservative known-outcome server codes,
  confirmed owned rollback and early migration/lifecycle DDL rejection. Independent review's startup,
  buffered-reader disposal and post-write close replay gaps are fixed. Seventy-nine unit and 68 runtime
  cases pass; full baseline Debug/Release/selected-patch suites each pass 4851. One baseline-built retry
  nupkg passes content/runtime consumers on both patches/modes. See the [retry plan](efcore-10-retries-plan.md).
  Generated-context/full CLI and four bundle build/help checks also pass for the same binary.
  Native outcome/error-code/failover acceptance and ambient/distributed support remain open under DEF-09.
- Migrations core now supplies both-mode model-difference/operation SQL, scoped history and owner-row locking,
  Migrate/MigrateAsync and forward/reverse/idempotent scripts; see the [core plan](efcore-10-migrations-plan.md).
- Migrations tooling supplies public assembly/history options and discoverable design services. Package-only
  CLI migrations/snapshots/model checks/scripts/list/removal and four Windows bundle build/help checks pass
  both modes on EF10.0.0/10.0.14 using dotnet-ef 10.0.15; see the [tooling plan](efcore-10-migrations-tooling-plan.md).
  DEF-01/02 stay In progress pending native acceptance.
- Reverse engineering now consumes public Connector 0.3.0 GetSchema metadata, warning on unknown behavior.
  Real generated contexts compile and produce query SQL in both modes for complete and documented-driver
  profiles. Full source suites each pass 4562; packed runtime/CLI/bundle consumers pass on both EF patches.
  See the [scaffolding plan](efcore-10-scaffolding-plan.md); native catalogs and lossless DDL are not claimed.
- TPT inheritance now has physical-table-aware generation plus local schema/migration/query/write and packed
  CLI evidence. Debug/Release/selected-patch suites each pass 4441; see the [TPT plan](efcore-10-tpt-plan.md).
  DEF-07 is partial; native TPT acceptance remains open. Entity/table splitting now also has local
  schema/migration/runtime evidence; see the [splitting plan](efcore-10-splitting-plan.md).
- TPC now supports assigned/composite/client Guid hierarchy keys with shared schema/migration and pre-write
  validation. Eighteen schema and 58 runtime cases cover concrete tables, UNION materialization, filters,
  Includes, CRUD/concurrency and transactions; see the [TPC plan](efcore-10-tpc-plan.md). Independent review's
  computed-key and converted-Guid bypasses are fixed. Native behavior and sequence allocation remain open.
  Final source suites each pass 4704 tests; the same TPC r2 nupkg passes content/runtime/generated-context/
  full CLI consumers on both patches/modes and four Windows bundle build/help checks.
- Ordinary many-to-many implicit/explicit joins now have local schema/query/write contract verification:
  four schema and 62 runtime cases plus expanded packed consumers. Existing EF/provider behavior works
  without a new algorithm; see the [many-to-many plan](efcore-10-many-to-many-plan.md). Native acceptance,
  wider variants and upstream runtime specifications remain separate work. Final source suites each pass
  4628 with no failures/skips after independent review's verification gaps were fixed.
- Batch3as scalar `Math.Round`/`decimal.Round` digits and midpoint parameters now refresh local SQL per
  execution in both modes; invalid runtime and row-dependent arguments remain rejected.
- Numeric and nonnumeric query slices, collection roots for integer/numeric/text/Boolean/temporal/binary/Guid
  carriers, and selected model/materialization, write, transaction, schema and diagnostics paths have local
  coverage, but their required rows remain open until their remaining boundaries and native evidence are closed.
- Exact `float`/`double` `MinNumber`/`MaxNumber` overloads now reuse the scalar `LEAST`/`GREATEST` path;
  47 unit and 4 intercepted sync/async cases cover both modes. Non-finite, native and live result semantics
  remain open; see the [MinNumber/MaxNumber plan](efcore-10-min-number-plan.md).
- `DateTimeOffset.UtcTicks` now composes UTC extraction with the existing Gregorian `DateTime.Ticks`
  arithmetic. Both modes have SQL-shape, unsupported-carrier and intercepted sync/async materialization
  coverage; native timezone/session behavior and live results remain open. See the
  [DateTimeOffset.UtcTicks plan](efcore-10-date-time-offset-utc-ticks-plan.md).
- `TimeSpan.TotalDays`, `TotalHours`, `TotalMinutes`, `TotalSeconds`, `TotalMilliseconds`, `TotalMicroseconds`
  and `TotalNanoseconds` now have both-mode translation and intercepted sync/async materialization coverage.
  Native interval precision/range and live results remain open.
- `TimeSpan.Days`, `Hours`, `Minutes`, `Seconds`, `Milliseconds`, `Microseconds` and `Nanoseconds` now have
  both-mode signed-component translation and intercepted sync/async materialization coverage. Native interval
  precision/range and live results remain open.
- `DateTime.Date` now has both-mode SQL-shape, mapping and intercepted reader coverage; native date semantics remain
  a live gate.
- `DateTime.TimeOfDay` now uses the existing MySQL `TIME` and Oracle `INTERVAL DAY TO SECOND` carriers, with
  both-mode SQL-shape and intercepted sync/async NULL/materialization coverage. Native interval semantics remain
  a live gate; see the [TimeOfDay plan](efcore-10-time-of-day-plan.md).
- `DateOnly.DayNumber` now translates the zero-based Gregorian day number through the existing `DATE` carrier:
  MySQL uses `DATEDIFF` and Oracle uses `TRUNC` date subtraction. Both-mode projection/predicate and intercepted
  sync/async NULL/materialization coverage pass; native date arithmetic remains a live gate. See the
  [DateOnly.DayNumber plan](efcore-10-date-only-day-number-plan.md).
- `TimeSpan.Ticks` now translates signed 100ns values through the existing `TIME`/`INTERVAL DAY TO SECOND`
  carriers. Both-mode projection/predicate/order and intercepted sync/async NULL/materialization coverage pass;
  native interval precision and sign behavior remain a live gate. See the
  [TimeSpan.Ticks plan](efcore-10-time-span-ticks-plan.md).
- `TimeOnly.Ticks` now translates non-negative midnight-based 100ns values through the same carriers. Both-mode
  projection/predicate and intercepted sync/async NULL/materialization coverage pass; native interval precision
  remains a live gate. See the [TimeOnly.Ticks plan](efcore-10-time-only-ticks-plan.md).
- `DateTime.Ticks` now translates Gregorian epoch-based 100ns values through MySQL `DATETIME` and Oracle
  `DATE`/`TIMESTAMP` carriers. Both-mode projection/predicate and intercepted sync/async NULL/materialization
  coverage pass; native date arithmetic and precision remain a live gate. See the
  [DateTime.Ticks plan](efcore-10-date-time-ticks-plan.md).
- `DateTime.Now`, `DateTime.UtcNow` and `DateTime.Today` now translate to explicit MySQL and Oracle server clock
  functions with both-mode sync/async intercepted coverage. Server clock accuracy, session timezone and UTC
  conversion remain live gates. Package `10.0.0-date-time-clocks-20261116` passes content verification and
  packed consumers on EF10.0.0/10.0.14 in both modes. See the [server clock plan](efcore-10-date-time-clocks-plan.md).
- `DateOnly.AddDays(int)` now translates through the existing `DATE` carrier: MySQL uses `DATE_ADD` and Oracle
  uses date-plus-integer. Both-mode projection/predicate, sync/async intercepted materialization and unsupported
  carrier boundaries pass locally. Package `10.0.0-date-only-add-days-20261116` passes content verification and
  packed consumers on EF10.0.0/10.0.14 in both modes. Native date arithmetic and live results remain open; see
  [the DateOnly.AddDays plan](efcore-10-date-only-add-days-plan.md).
- `DateOnly.AddMonths(int)` now translates through the existing `DATE` carrier: MySQL uses `DATE_ADD` and Oracle
  uses `ADD_MONTHS`. Both-mode projection/predicate, sync/async intercepted materialization and unsupported
  carrier boundaries pass locally. Package `10.0.0-date-only-add-months-20261116` passes content verification
  and packed consumers on EF10.0.0/10.0.14 in both modes. Native month arithmetic and live results remain open;
  see [the DateOnly.AddMonths plan](efcore-10-date-only-add-months-plan.md).
- `DateOnly.AddYears(int)` now translates through the existing `DATE` carrier: MySQL uses `DATE_ADD` and Oracle
  uses `ADD_MONTHS(input, years * 12)`. Both-mode projection/predicate, sync/async intercepted materialization
  and unsupported carrier boundaries pass locally. Package `10.0.0-date-only-add-years-20261116` passes content
  verification and packed consumers on EF10.0.0/10.0.14 in both modes. Native year arithmetic and live results
  remain open; see [the DateOnly.AddYears plan](efcore-10-date-only-add-years-plan.md).
- `CON-02` now rechecks the compatibility mode after sync/async open callbacks and restores EF's connection lease
  accounting when the post-open check rejects a conflict. Native handshake detection and live cleanup remain open.

The provider is still a development checkpoint. It is not a release candidate.

## Work that still needs provider development and local evidence

| Workstream | Required IDs | What must be finished | Exit evidence |
| --- | --- | --- | --- |
| Connection, services and diagnostics | `CFG-04`, `CON-02..05`, `SQL-01`, `DIA-01..02` | Complete mode-conflict detection, opened/caller-owned connection lifetime, timeout/cancellation cleanup, service-facade coverage, and protected diagnostics. | Focused RED/green tests in both modes and sync/async; full Debug/Release suites; source review. `CON-03..05` local characterizations are recorded; native/live cleanup remains open. |
| Model and scalar mappings | `MOD-01..03`, `MAP-01..07` | Close remaining explicit numeric/decimal, text length/Unicode/LOB, binary/Guid, temporal and converter/null/clone boundaries. Reject unsupported combinations before execution. | Mapping metadata, parameter, reader, DML and negative tests in both modes; package consumer smoke for public behavior. |
| Query translation and materialization | `QRY-01..11` | Finish the supported overload matrix, remaining temporal/math cases, collection/operator compositions, cardinality, Include/ThenInclude, split/graph filter boundaries, and unsupported translation errors. | RED/green unit tests, intercepted sync/async functional tests, selected EF specification coverage, and explicit unsupported-case records. |
| Tracked writes and transactions | `UPD-01..05`, `TXN-01..03` | Finish generated/default/computed readback, composite and nullable concurrency, graph save ordering, atomic failure/cancellation recovery, enlistment and savepoint boundaries. | Deterministic command/transaction tests in both modes plus native readback/rollback evidence. |
| Schema and database lifecycle | `SCH-01..03` | Finish existence/error classification, owned namespace boundaries, table/database/user creation and deletion behavior, privilege checks, and cleanup rules. | Offline SQL/double tests plus controlled live DDL/privilege/deletion matrix with an isolated owned target. |
| EF relational specification coverage | `TST-01` | Add the next required relational model/query/write/runtime families and map each exclusion to a checklist ID. JSON-only `RelationalTypeTestBase` remains under `DEF-04`. | Selected specification classes pass for both modes with zero unexplained skips. |
| Migrations and reverse engineering | `DEF-01/02` | Core runtime/scripts, package-only migration tooling and Connector 0.3.0 metadata-to-compiled-context workflow pass locally. Close native catalog/migration/script/bundle/concurrency acceptance; review unknown generated configuration before writes or DDL. | Live schema fixtures and an authorized isolated upgrade/revert/lock/script matrix; retain packed tooling matrix. |

## Work that is implemented locally but still needs native validation

`TST-02` is a release blocker. It must cover scalar mapping, query, write/readback, transaction and schema
operations in both MySQL and Oracle modes on a declared supported environment. Local intercepted tests do not
close this gate.

The current supplied endpoint is reachable at TCP level, but both mode probes fail before authentication with
`Connection closed while reading the packet`. No SQL or DDL ran. The next action for this gate is to classify the
endpoint/protocol/TLS or proxy configuration with the server owner, then rerun the existing non-destructive
probes before adding write or schema cases.
The two existing read-only probes were rerun on 2026-11-13 after restoring Connector 0.3.0; both still fail
during OpenAsync with the same packet closure. No SQL/DDL ran, and the cause is not yet assigned to the driver.

Native validation must also be recorded for the local workstreams above: successful SQL generation, parameter
metadata, or connector doubles prove provider behavior at that layer only.

## Release work

| Release item | Required IDs | Exit condition |
| --- | --- | --- |
| Support record | `REL-01` | Record server and tenant mode, connector/runtime/EF versions, direct or proxy endpoint, TLS and transport options, and known limitations without credentials. |
| Release revision review | `REL-02` | Review the exact `efcore/10.0` revision after all required rows are `Done`; rerun full Debug/Release, selected EF10 patch compatibility, package content, package consumers in both modes, and `git diff --check`. |
| Package publication decision | `PKG-01..04` | Recreate the package from the reviewed revision, verify the EF range `[10.0.0,11.0.0)`, net10.0 asset, dependency graph and standalone consumer. Tag/push/publication happen only after explicit user direction. |

Release is blocked while any required row is `In progress` or `Planned`, while `TST-02` is unavailable, or while
the exact release revision has not been reviewed. EF8/EF9 adaptation is outside this release sequence.

## Explicitly deferred after the first release

`DEF-04/05/06/10` remain deferred. `DEF-01/02/03/07/08/09` are partial: migrations core/tooling, local TPT/TPC,
ordinary bulk operations, split-query graphs, ordinary compiled models, lazy loading, EnsureCreated/Migrate callbacks, execution retries and runtime
migration locking have local evidence. Native migrations/locks/bundle application, reverse-engineering
catalogs, graph/retry behavior, wider JSON/converter/model variants, advanced proxy combinations, NativeAOT/precompiled optimization and
ambient/distributed transactions remain outside the accepted support claim. Deferred
features stay visible and must not be advertised as supported.

## Execution order from this checkpoint

Development continues after the CFG-04/TST-01 service-lifetime local commit. Keep one feature module active at a time and do not
repeat completed connection characterization without a reproduced defect.

1. **Completed locally:** migrations core, tooling, TPT/TPC inheritance, entity/table splitting and Connector
   0.3.0 reverse engineering form complete local modules; see each plan for reviewed source/package evidence.
   Unknown metadata remains visible through warnings. Native acceptance remains distinct from local results.
2. CFG-04 service-graph wiring and the DIA-01/02 diagnostics boundaries are locally characterized without a redundant
   provider implementation. Continue with the exposed graph materialization, tracked graph writes and
   transaction/connection recovery gaps together.
   Official runtime fixtures require an initialized store; intercepted canned results cannot replace them.
   Record positive/null/empty/failure boundaries by feature ID; avoid a new sequence of one-overload commits.
3. Resolve the pre-auth live transport blocker and run the two-mode native matrix.
4. Write `REL-01`, review the release revision, and run the final package/consumer matrix.

Each completed feature module gets its own documented evidence and local commit. No push, tag or publication is implied by
this roadmap.
Keep only one feature module active: finish implementation, tests, review fixes, evidence and its local
commit before starting the next. Status reports must distinguish new functionality, verified existing
behavior and missing native acceptance.
