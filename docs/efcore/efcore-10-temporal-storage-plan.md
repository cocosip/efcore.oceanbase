# Temporal storage acceptance implementation plan

> Execute inline with superpowers:executing-plans; one independent module review
> after implementation and verification. Continue the authorized mapping queue.

**Goal:** Verify the existing MAP-05/06/07 temporal contract against OceanBase
4.4.2.2 in both modes through packaged Connector 0.3.0 public ADO.NET APIs.

**Spec:** The acceptance criteria and next mapping cases in
efcore-10-feature-matrix.md, current temporal mappings/conversions and
docs/repository-guidelines.md. No new deployment, protocol or Connector work.

**Design:** Reuse LiveStorageTable, separate TemporalContext/cache key and owned
tables. DateTime is unspecified wall clock; Oracle DateTimeOffset retains its
offset and MySQL normalizes to UTC. DateOnly uses DATE; TimeOnly/TimeSpan use
TIME or Oracle intervals. Preserve NULL, exact accepted fractional values,
default/seed/returned values and converters. Clear tracking before reads and
assert DateTimeKind/DateTimeOffset.Offset separately from value equality.

## Tasks

- [x] Add both-mode sync/async five-type storage, defaults/HasData/generated
  readback, converters, endpoints and tracked updates. Diagnose failures with
  direct public Connector controls; add a local RED before any production fix.
- [x] Add explicit fractional/leading precision and invalid-write entry/attempt
  preservation; lossless query comparands/CASE/original-value predicates,
  mapping-owned literal storage and timezone session controls. Distinguish
  Connector/server limits from Provider defects and keep unsupported domains open.
- [x] Run appropriate targeted/full/both-patch/live/package gates, update matrix
  and progress evidence, independently review once, fix findings once if needed,
  audit credentials and owned-table cleanup, and commit locally without pushing.

## Review focus

- DateTimeKind and numeric offsets must not be hidden by CLR value equality.
- Native carriers must retain fractions/signs near day and CLR/store boundaries.
- Query inputs/original values must not inherit restrictive column write facets.
- Defaults/seed and returned values must use the same documented semantics.
- Failures must retain entity entries, avoid unintended writes and clean up only
  CREATE-confirmed tables through an independent connection.

Broader calendar/LINQ overloads, named timezone regions/DST, TIMESTAMP WITH LOCAL
TIME ZONE, non-Gregorian query arithmetic and other versions/deployments remain
separate existing inventory work; no full-row Done claim follows from this slice.

## Findings

The initial eight cases pass in Oracle and expose MySQL TIME endpoint loss in two
cases: signed 838:59:59.999999 parameters read back as the signed whole-second
endpoint. Direct public Connector controls with positional parameters verify the
same stored CAST-to-CHAR text and MICROSECOND=0. Signed 838:59:58.999999 and exact
838:59:59 preserve their values; 838:59:59.000001 and .999999 typed TIME literals
return 1525. This is not a reader-only discrepancy or an Oracle/MySQL upstream
inference. The Provider's previous allowed magnitude was too large.

A local unit regression first fails because the mapping accepts those extra
fractions. The shared temporal bound now ends at exactly 838:59:59, preventing
loss for parameters/literals/writes and converted durations. No transport or
Connector changes. Updated endpoint tests preserve the actual supported boundary;
separate versioned raw-parameter/literal tests retain the server behavior instead
of describing it as valid full-range storage. Raw caller-owned parameters bypass
the mapping guard and are explicitly used only by that control.

The final targeted model has 26 passing sync/async cases. Five CLR types preserve
NULL, endpoints, signed fractions, converters, defaults/seed/generated returns
and tracked updates. DateTime/DateOnly years1..9999 were actually exercised on
this target; this does not establish other server versions' range semantics.
MySQL timestamps/clocks keep microseconds and Oracle keeps ticks. Oracle TimeSpan
CLR Min/Max pass; MySQL exact signed TIME endpoints and the preceding microsecond
pass. Explicit milliseconds reject lossy writes, keep exception entries and make
no DML attempt; a subsequent valid write succeeds.

Fractional parameter/constant comparands and CASE preserve inputs beyond column
write precision. A mismatching fractional original timestamp raises a concurrency
conflict without changing the row, followed by a valid update. Oracle interval
comparands retain a larger leading-day precision than the column, and DATE
comparands preserve fractional timestamp inputs while DATE writes keep seconds.

Two numeric session timezones (+05:45/-03:30) preserve wall-clock DateTime values;
DateTimeOffset offsets0/+05:45/-03:30/-12/+14 retain offsets in Oracle and read
as UTC in MySQL. Offsets and DateTimeKind are checked explicitly. Oracle native
nanosecond results reject subtick loss for DateTime/DateTimeOffset/TimeSpan/TimeOnly,
oversized durations and invalid clock values reject, and DateOnly rejects a raw
non-midnight DATE before a valid read recovers. Named zones/DST remain outside
this numeric-offset storage profile.

Evidence: ignored artifacts/temporal-storage (initial/expanded/boundaries,
unit-red/unit-green, direct probe, final suite/live/package/review/cleanup).

## Verification and review

EF10.0.0 and isolated10.0.14 Release suites each pass 6645 cases with 16 existing
specification skips. Serial final LiveTests profiles each pass 119 cases
(109 live, 10 offline), including the 26 new temporal cases. The targeted temporal
mapping/query unit gate passes131. Package10.0.0-temporal-storage-r1-20261117
archive/dependencies, both-mode/both-patch consumers and source/package DLL/README
identity pass; the consumer includes signed TIME endpoint/overrange smoke checks.
Compatibility source hashes and actual EF10.0.14 assets are verified.

Fresh independent review found no Critical/Important issues; no fix pass.
Minor deferred: immediate generated DefaultStamp DateTimeKind is not separately
asserted before clearing tracking; the fresh queried Kind is checked. This is
a coverage gap, not an observed wrong Kind. Named zones/DST, non-Gregorian/full
temporal LINQ, other versions/deployments, transport internals and wider EF
modules remain excluded from this verdict. No feature row is promoted to Done.

The executor retains the approved design/current mapping contract rather than
inventing a new temporal policy: unsupported domains remain explicit gaps.
Final read-only catalog cleanup finds zero tagged diagnostic tables in both
namespaces. Exact-module credential and whitespace audits pass. The checked
module is committed locally; no push or publication.
