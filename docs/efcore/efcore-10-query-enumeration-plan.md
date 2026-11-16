# EF10 query enumeration lifecycle

**Base:** `b3b1c4f`. **IDs:** CON-03/04/05, QRY-05, DEF-09.
**Status:** Local profile verified/reviewed; native acceptance remains open.

## Contract and ownership

EF owns query enumeration, materialization, buffering for an execution strategy, disposal of its
readers and release of its connection leases. Connector 0.3.0 owns the physical connection, native
reader and cancellation/transport implementation. This module uses only existing public EF and
ADO.NET reader APIs. Both modes are OceanBase modes; no new SQL or server capability is assumed.

The missing evidence is after reader creation: existing command-cancellation cases throw before
a reader exists. Extend `CommandExecutionBoundaryTests` and its existing tracking reader, using
the established `TransactionTests.TestConnection` for observable local EF lease state. Do not add
a production reader implementation unless a failing regression requires a change in EF orchestration.

| Profile | Required behavior |
| --- | --- |
| Early disposal | Sync/async enumerators do no work before first MoveNext; first result can be consumed then disposed; readers/EF leases are released and a later query works |
| Retry buffering | Enabled execution strategy buffers before exposing rows; ordinary queries stream; early disposal respects both paths |
| Read failure/cancellation | Inject failure on the second reader read; preserve error/token, expose no buffered partial rows, avoid replay of cancellation/nontransient errors, dispose and recover |
| Consumer cancellation | Cancel an async enumeration after its first yielded row; preserve cancellation and release resources on disposal |
| Caller lease | An explicit EF OpenConnection lease survives enumeration success/early stop/failure/cancellation; explicit CloseConnection balances it |
| Tracking | Partial entity enumeration keeps the requested identity scope and already materialized tracked state; later queries retain only context tracking identity |

## Execution

- [x] Add meaningful supplied-reader tests for both modes and sync/async before production edits.
  Keep actual query compilation/materialization/enumeration real; inject only native connection/reader
  results. Assert real EF outputs, read counts/tokens, cleanup, lease state and later reuse.
- [x] Fix only reproduced defects, after tracing the owning EF seam. Do not duplicate passing inherited
  behavior. Retain focused failures as evidence if a production fix is required.
- [x] Obtain independent read-only review. Run focused and full Debug/Release/isolated EF10.0.14 gates;
  package content and existing public consumers. Production changes require a unique package; a
  test-only continuation may replay the original package with its original DLL provenance.
- [x] Update checklist/progress/workboard and local-commit after all checks pass.

Native wire cancellation, server timeout, connection-pool return, native read/cleanup failures,
LOB streams, arbitrary split/lazy-loading combinations and real deployment versions remain open.
No native attempt, live DDL/writes, driver changes, push/publication, EF8/9 or Deferred expansion.

## Implementation and focused evidence

No production change was needed. The existing registered EF/provider path passes 34 new cases;
the full `CommandExecutionBoundaryTests` profile passes 48/48 and the connected execution-strategy
profile passes 112/112. Both modes have sync/async streaming, buffering, failure and lease coverage;
external consumer cancellation after a yield is async-only, matching the public enumeration API.

Removing `base.IsBuffering` temporarily from the existing provider query compilation context makes
8/48 cases fail: buffered first-row/disposal and partial-result assertions detect the regression.
Restoring it returns the connected profile to green. No mutation remains in source.
Logs: `artifacts/query-enumeration/profile-green.log`, `buffering-mutation.log`, `connected-green.log`.

Initial fixture failures were a read counter that incremented only when a callback existed and an
incorrect assumption that the native reader stays open until a buffered result is yielded. EF closes
the native reader after filling its buffer. These were fixture corrections, not provider defects.
The external-cancellation probe initially assumed the DataTableReader exception token must equal the
input token. Diagnostic `cancel-boundary.log` confirms the correct caller token reaches the second
ReadAsync, while that reader supplies its own cancelled token. The final assertion checks exact
reader-exception identity and the caller token at the read boundary separately. Injected cancellation
also asserts exact exception identity and correct input tokens. Do not rewrite a driver's error to
manufacture a provider pass or classify this supplied-reader behavior as a Connector API gap.

Independent read-only review of tests, connection fixture, buffering, retry interceptor and strategy
registration found no material issue. The reviewer ran no builds, tests or native queries.

## Final verification

| Gate | Result |
| --- | --- |
| EF10.0.0 Debug and Release | Each 6442 pass: Unit 2509, Functional 2393, Specification 1538, ConsumerTests 2; 16 old mapped skips |
| Isolated EF10.0.14 Release | Same counts; runtime deps explicitly resolve EF/Relational10.0.14 and Connector0.3.0 |
| Original archive content | `10.0.0-default-reference-20261116.1` passes; no new package |
| Original public consumers | EF/Proxies10.0.0 and10.0.14, both modes pass; package-only graphs and original archive/consumer DLL hashes match |

Source commands are in `artifacts/query-enumeration/run-gates.ps1`: normal solution tests at Debug
and Release with `--no-restore -m:1 -nr:false`, then Release with the existing
`artifacts/default-if-empty/compat-build` and exact
`artifacts/default-if-empty/compat/Directory.Packages.props`. Source logs: `full-debug.log`,
`full-release.log`, `full-compat.log` under `artifacts/query-enumeration/`.

Replay `tests/OceanBase.EntityFrameworkCore.PackageConsumer/Verify-Package.ps1` with the original
archive in `artifacts/default-if-empty/reference/package/` and its expected version, then
`artifacts/default-if-empty/verify-collection-consumers.ps1`. Logs: `package-content.log`,
`consumers.log` under this module's artifact directory. The original archive's provider DLL and both
consumers retain SHA256 `703D578BF6FB00AF88501FFE1F180A8CC6B382F68DAD61AC227AB2815C0F716C`.
This archive remains the preceding precommit build. New enumeration cases run against rebuilt source
on `b3b1c4f` plus tests; they are not newly added packed-consumer smokes, and current source DLL hash
equality with the old archive is not claimed. No production/dependency diff, new CLI/bundle/hosted/
Linux/native evidence or Required-row promotion; both modes remain 9 Done / 44 In progress out of53.
