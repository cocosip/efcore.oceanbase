# Native bounded text storage acceptance

Scope: MAP-03/MAP-06/MAP-07, with narrow SCH-02 and UPD-01/02 evidence. Continue the
approved matrix order using Connector 0.3.0 public ADO.NET APIs and normal accounts.

Design: an opt-in live profile creates one uniquely named table per mode and sync/async
case in the configured existing namespace. Use provider CreateTables, model defaults
and HasData, assigned integer keys, bounded ordinary/national/fixed text and a string
value converter. Verify exact parameter round trips, NULL/empty semantics, padding,
length limits, tracked update/delete and generated defaults. Drop only a table whose
CREATE command this fixture observed succeeding, using a fresh connection, including
when seeding or later assertions fail. Include the unique table in the model cache key.

Approaches: read-only expressions cannot prove column storage or DML; hand-written
DDL would bypass the provider under test. Use the existing schema creation service,
with its explicit current-namespace opt-in. Do not create/drop databases/users or use
administrator credentials. LOB capacity, BYTE declarations, wider charset/index and
server-version coverage remain separate gaps.

Oracle generated defaults use the existing Connector `Use Oracle Prepare Execute=true`
opt-in, set explicitly by this write fixture without editing User Secrets or Provider
connection policy. The default-off negative case fails before execution as documented
by Connector. MySQL also runs with NO_BACKSLASH_ESCAPES on an owned session.

Live findings: MySQL rejects CONVERT-based literal defaults with 5023; the same UTF-8
bytes in `_utf8mb4 X'...'` are accepted and preserve text. Use that character literal
in the shared mapping. MySQL generated-value SELECT must be consumed through the final
result before releasing the reader: an early close breaks the Connector session and
prevents commit. Drain the provider-owned readback in sync/async paths, preserving
cancellation and write errors. No Connector source changes or broader protocol work.

## Tasks

- [x] Add the owned-table fixture and live acceptance cases; run against current code.
- [x] If failures reveal a provider defect, reproduce it locally first, make the smallest
  shared fix and verify RED/GREEN. Do not change production code for passing acceptance.
- [x] Run applicable live sync/async and two-patch checks; full/package checks if code changes.
- [x] Independent walkthrough, update the progress/feature/gap records and commit locally.

Review focus: unique names and model caching, CREATE ownership and partial failure cleanup,
scope from configured connection identity, no credential output, genuine parameter writes
and materialization rather than ChangeTracker reuse, server empty/padding semantics,
converted-value length enforcement, and unchanged public Connector/deployment boundaries.

## Results (2026-11-17)

- Initial native profile: MySQL sync/async failed CREATE/defaults with 5023; Oracle
  created/seeded/read successfully but generated-default writes rejected default-off
  PrepareExecute. Explicit Connector opt-in made Oracle pass without a Provider change.
- Direct Connector DDL variants prove CONVERT fails while `_utf8mb4 X'...'` defaults
  preserve the same UTF-8 bytes. Six new unit cases and six functional SQL cases were
  RED before the shared literal change. Existing query/collection/package expectations
  now assert the character-literal form; malformed UTF-16 still rejects first.
- MySQL then exposed an unconsumed generated-value response: first-row read/dispose
  breaks the direct Connector session before commit; complete drain/commit succeeds.
  Six new functional cases were RED before the sync/async readback drain. They cover
  success, later-result IO failure/cancellation, disposal and pending-entry recovery.
- Independent review found no production defect and one Important test gap: the initial
  reader had only one row/result. The fix supplies four rows across three results
  (including an empty result), enforces consumption before advancing, and injects
  failures/cancellation in the later result. Temporarily removing the drain produces
  six failures; restoring it produces six passes. No second independent review was run.
- EF10.0.0 and isolated EF10.0.14 each pass 6640 Release tests: Unit 2613,
  Functional 2487, Specification 1538, Consumer 2; 16 existing mapping skips remain.
- Both patches pass 25 serial LiveTests each: 15 live cases plus 10 offline profile
  checks. Six storage cases cover two modes, sync/async, and MySQL NO_BACKSLASH_ESCAPES.
  Variable columns round-trip NULL/empty, quote/backslash/CR/LF, supplementary Unicode
  and 32 scalar characters. Fixed columns verify Oracle padding and MySQL trimming.
  Generated ordinary/national defaults, HasData, converter bounds and tracked DML pass.
- First concurrent native run had two handshake interruptions (23/25), including a
  cleanup connection. The exact owned table was independently removed; serial native
  runs then pass. A read-only catalog check in both namespaces confirms zero remaining
  EFT_/EFD_ diagnostic tables. Test errors identify owned names and stages without
  attaching credential-bearing exceptions. No User Secrets edits or admin use.
- Package `10.0.0-text-storage-r1-20261117` passes content/dependency checks and both-mode
  public consumers at EF10.0.0/10.0.14. Consumer and package DLL identity is checked
  against the tested source binary. Logs/TRX/snapshots are ignored under
  `artifacts/text-storage/`; the production dependency range remains unchanged.

Run the solution with `dotnet test OceanBase.EntityFrameworkCore.slnx -c Release
--no-restore -m:1 -nr:false`. The separate live project requires existing User Secrets,
`OCEANBASE_RUN_LIVE_TESTS=1` and the additional `OCEANBASE_RUN_LIVE_SCHEMA_TESTS=1`
DDL/DML opt-in. The acceptance run uses serial xUnit collections.

LOB/BYTE/full-capacity/index and additional charset/server-version acceptance remain
matrix gaps. Connector protocol and early-close implementation were not changed;
the Provider consumes its owned response through public reader APIs. Earlier literal
modules were regression-tested, without repeating their independent reviews. Package
evidence applies to this artifact, not future releases. Broad parent rows stay In progress.
