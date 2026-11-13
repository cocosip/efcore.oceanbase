# EF Core 10 Development and Release Roadmap

**Updated:** 2026-11-13
**Branch:** `efcore/10.0`  
**Acceptance authority:** [efcore-10-feature-checklist.md](efcore-10-feature-checklist.md)

This is the execution board for the first OceanBase EF Core 10 provider. It separates provider work from
evidence work so an intercepted test cannot be mistaken for native OceanBase acceptance.

## Current baseline

The following required rows have complete local/package evidence for both modes:

- `CFG-01..03`, `CON-01`, `SQL-02`
- `PKG-01..04`
- Migrations core now supplies both-mode model-difference/operation SQL, scoped history and owner-row locking,
  Migrate/MigrateAsync and forward/reverse/idempotent scripts; see the [core plan](efcore-10-migrations-plan.md).
- Migrations tooling supplies public assembly/history options and discoverable design services. Package-only
  CLI migrations/snapshots/model checks/scripts/list/removal and four Windows bundle build/help checks pass
  both modes on EF10.0.0/10.0.14 using dotnet-ef 10.0.15; see the [tooling plan](efcore-10-migrations-tooling-plan.md).
  DEF-01/02 stay In progress pending native acceptance and reverse engineering.
- TPT inheritance now has physical-table-aware generation plus local schema/migration/query/write and packed
  CLI evidence. Debug/Release/selected-patch suites each pass 4441; see the [TPT plan](efcore-10-tpt-plan.md).
  DEF-07 is partial; native TPT acceptance and TPC remain open. Entity/table splitting now also has local
  schema/migration/runtime evidence; see the [splitting plan](efcore-10-splitting-plan.md).
- Batch3as scalar `Math.Round`/`decimal.Round` digits and midpoint parameters now refresh local SQL per
  execution in both modes; invalid runtime and row-dependent arguments remain rejected.
- Numeric and nonnumeric query slices, collection roots for integer/numeric/text/Boolean/temporal/binary/Guid
  carriers, and selected model/materialization, write, transaction, schema and diagnostics paths have local
  coverage, but their required rows remain open until their remaining boundaries and native evidence are closed.
- `DateTime.Date` now has both-mode SQL-shape, mapping and intercepted reader coverage; native date semantics remain
  a live gate.

The provider is still a development checkpoint. It is not a release candidate.

## Work that still needs provider development and local evidence

| Workstream | Required IDs | What must be finished | Exit evidence |
| --- | --- | --- | --- |
| Connection, services and diagnostics | `CFG-04`, `CON-02..05`, `SQL-01`, `DIA-01..02` | Complete mode-conflict detection, opened/caller-owned connection lifetime, timeout/cancellation cleanup, service-facade coverage, and protected diagnostics. | Focused RED/green tests in both modes and sync/async; full Debug/Release suites; source review. |
| Model and scalar mappings | `MOD-01..03`, `MAP-01..07` | Close remaining explicit numeric/decimal, text length/Unicode/LOB, binary/Guid, temporal and converter/null/clone boundaries. Reject unsupported combinations before execution. | Mapping metadata, parameter, reader, DML and negative tests in both modes; package consumer smoke for public behavior. |
| Query translation and materialization | `QRY-01..11` | Finish the supported overload matrix, remaining temporal/math cases, collection/operator compositions, cardinality, Include/ThenInclude, split/graph filter boundaries, and unsupported translation errors. | RED/green unit tests, intercepted sync/async functional tests, selected EF specification coverage, and explicit unsupported-case records. |
| Tracked writes and transactions | `UPD-01..05`, `TXN-01..03` | Finish generated/default/computed readback, composite and nullable concurrency, graph save ordering, atomic failure/cancellation recovery, enlistment and savepoint boundaries. | Deterministic command/transaction tests in both modes plus native readback/rollback evidence. |
| Schema and database lifecycle | `SCH-01..03` | Finish existence/error classification, owned namespace boundaries, table/database/user creation and deletion behavior, privilege checks, and cleanup rules. | Offline SQL/double tests plus controlled live DDL/privilege/deletion matrix with an isolated owned target. |
| EF relational specification coverage | `TST-01` | Add the next required relational model/query/write/runtime families and map each exclusion to a checklist ID. | Selected specification classes pass for both modes with zero unexplained skips. |
| Migrations and reverse engineering | `DEF-01/02` | Core runtime/scripts and the package-only migration-tooling workflow pass locally. Implement reverse engineering separately; close native migration/script/bundle/concurrency acceptance. | Live schema fixtures and an authorized isolated upgrade/revert/lock/script matrix; retain packed tooling matrix. |

## Work that is implemented locally but still needs native validation

`TST-02` is a release blocker. It must cover scalar mapping, query, write/readback, transaction and schema
operations in both MySQL and Oracle modes on a declared supported environment. Local intercepted tests do not
close this gate.

The current supplied endpoint is reachable at TCP level, but both mode probes fail before authentication with
`Connection closed while reading the packet`. No SQL or DDL ran. The next action for this gate is to classify the
endpoint/protocol/TLS or proxy configuration with the server owner, then rerun the existing non-destructive
probes before adding write or schema cases.

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

`DEF-04/05/06/08/10` remain deferred. `DEF-01/02/03/07/09` are partial: migrations core/tooling and local TPT,
ordinary bulk operations, EnsureCreated/Migrate callbacks and runtime migration locking have local evidence.
Native migrations/locks/bundle application, reverse engineering, wider JSON/converter/model variants, execution-strategy retries
and ambient/distributed transactions remain outside the accepted support claim. Deferred
features stay visible and must not be advertised as supported.

## Execution order from this checkpoint

1. **Completed locally:** migrations core, tooling, TPT inheritance and entity/table splitting are reviewed/tested
   as complete modules. Splitting source suites each pass 4504; the same package passes both-mode runtime/CLI
   consumers on EF10.0.0/10.0.14 and all four Windows bundle build/help checks.
2. Build the next complete relational query/write specification fixture module (`TST-01`), then close the
   exposed graph materialization, tracked graph writes and transaction/connection recovery gaps together.
   Record positive/null/empty/failure boundaries by feature ID; avoid a new sequence of one-overload commits.
3. Resolve the pre-auth live transport blocker and run the two-mode native matrix.
4. Write `REL-01`, review the release revision, and run the final package/consumer matrix.

Each completed feature module gets its own documented evidence and local commit. No push, tag or publication is implied by
this roadmap.
