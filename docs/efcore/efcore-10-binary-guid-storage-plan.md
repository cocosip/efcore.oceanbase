# Native binary and Guid storage acceptance

Scope: existing MAP-04/06/07, with narrow UPD-01/02 and SCH-02 evidence, based on
b9eb987. Continue the approved mapping queue using Connector 0.3.0 public APIs,
User Secrets, ordinary direct accounts and the existing LiveStorageTable helper.
No Connector source, new protocol/deployment support, or locator API development.

Use a separate binary/Guid model with unique owned tables. Compare byte sequences,
clear tracking before reads, and inspect server hexadecimal output independently
of Guid conversion so symmetric writer/reader mistakes cannot conceal byte order.
Retain CREATE-success ownership, independent cleanup and safe diagnostics.

- [x] Add both-mode sync/async bounded binary NULL/bytes/length/padding cases,
  binary defaults/HasData, converters and tracked updates.
- [x] Add large binary storage beyond 2000/65535 bytes, NULL/empty boundaries,
  full byte-pattern/converter readback and updates. Investigate failures against
  direct Connector; do not silently lower accepted sizes or conflate empty with NULL.
- [x] Add Guid.Empty/known UUID/NULL through default and explicit text/binary
  mappings, Guid converters, parameters/literals/default readback and raw server
  bytes/text. Add pre-DML capacity/transport rejection with entry preservation.
- [x] Add a local RED regression before any production fix; run relevant/full,
  both-patch/live/package gates appropriate to changed source.
- [x] Independent final module review, one fix pass if needed, document evidence
  and remaining gaps, audit credentials/cleanup and commit locally.

Review focus: Guid mixed-endian byte contract versus RFC order, exact binary
content/trailing zeros/padding, NULL versus empty, large-value reader/parameter
carriers, defaults/generated readback, converter validation and cleanup on failures.
Wider indexed/rowversion/concurrency/generated-value combinations, theoretical LOB
capacity/streaming, other versions/deployments and unrelated mappings stay open.

## Findings

Initial 16 cases pass, then maximum RAW(2000)/VARBINARY(262144) cases raise that to
20 passing cases. Full-byte patterns and one-extra-byte rejection verify actual
capacity. A query comparand extends an existing exact-length value by one byte,
so accidental parameter truncation would produce a wrong match. MySQL BINARY(8)
retains zero padding; Guid server hex independently confirms the mixed-endian
Guid.ToByteArray contract. NULL/Guid.Empty/known/all-ones UUIDs, explicit text/binary
declarations, converters, default readback, HasData, predicates/literals and tracked
updates pass. Large values include 2000/2001/65536 bytes and a 131073-byte update.

Direct Oracle Connector controls show generic DbType.Binary empty arrays store
non-NULL, zero-length BLOBs, with PrepareExecute disabled/enabled and explicit
Prepare. Typed OceanBaseDbType.Blob plus byte[] is a rejected locator API and is
not needed. RAW empty literals become NULL; that path still rejects empty arrays.
The Provider previously rejected even BLOB empty values. A new unit case fails
before the minimal shared guard/literal fix. Oracle native default/seed generation
also fails before DML under the old guard; afterward parameters, converters,
EMPTY_BLOB() literals/defaults/HasData and NULL-to-empty-to-NULL updates pass.

MySQL 4.4.2.2 rejects BLOB/LONGBLOB defaults with 1067 for empty/nonempty hex and
empty plain/binary string alternatives; CAST returns 5023. Keep a separate versioned
negative ALTER test and use a bounded VARBINARY(8) empty default in that model.
This is not a silent Provider store substitution: explicit MySQL LOB defaults remain
unaccepted. Oracle empty BLOB defaults and returned empty arrays are genuinely tested.

The final targeted model has 26 passing cases. Remaining broad mapping, LOB API,
index/concurrency and deployment requirements are not closed by this acceptance.
Production changes are restricted to the byte-array mapping's empty Oracle BLOB
guard/literal; no Connector or dependency changes. Evidence lives in ignored
artifacts/binary-guid-storage (initial/boundaries/unit-red/empty-red/binary-green,
direct controls and final suite/package/cleanup results).

## Verification and review

EF10.0.0 and isolated10.0.14 final Release suites each pass 6645 cases with 16
existing specification skips; both serial LiveTests profiles pass 85 cases
(75 live, 10 offline). The 26-case binary model passes separately. Initial full
runs exposed two obsolete text-collection assertions: captured collections render
parameters, while EF.Constant renders EMPTY_BLOB(). Both forms now have the
appropriate SQL assertion. This is SQL-generation evidence only; native BLOB
comparison acceptance remains open.

Package 10.0.0-binary-guid-r1-20261117 content/dependencies, both-mode consumers
on both patches and source/package DLL identity pass. Read-only cleanup checks
find zero tagged diagnostic tables in both namespaces. No publication or push.
Fresh independent review found no Critical/Important/Minor findings, so no fix
pass was required. Wider LOB comparisons, indexes/concurrency/generated-value
combinations, theoretical capacity/streaming/locator APIs and other deployments
were excluded from that verdict and remain open. Oracle Double is a separate
declaration/precision follow-up, not resolved by this change.
