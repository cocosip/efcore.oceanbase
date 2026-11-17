# Temporal-query native acceptance implementation plan

> **For agentic workers:** Use superpowers:executing-plans inline under the
> established test/review/local-commit authorization.

**Goal:** Verify and correct the implemented temporal QRY-07 profile against
ordinary OceanBase MySQL/Oracle modes without expanding the public function set.

**Architecture:** Reuse LiveStorageTable ownership, ordinary User Secrets and
Connector 0.3.0 public APIs. Hold one physical connection per test body. Compare
server-translated expressions with independent CLR results from verified seeds;
use direct SQL controls to classify any observed differences.

**Tech Stack:** EF 10.0.0/10.0.14, .NET 10, Connector 0.3.0, xUnit.

**Spec:** efcore-10-feature-matrix.md (MAP-05, QRY-07, QRY-11).

## Global constraints

- Both modes, sync/async, both EF NULL configurations; no credentials in files.
- Current efcore/10.0 branch; no push/publication or driver source changes.
- Oracle calendar arithmetic requires a Gregorian session. Numeric session
  offsets are in scope; named zones/DST/non-Gregorian calendars remain open.
- No silent precision narrowing, text carrier substitution or client predicate
  fallback. Existing unsupported overloads retain explicit rejection.
- QRY-07 cannot close while default MySQL Decimal scalar metadata remains open.

## Review focus

- Fractions must be counted once, including negative subsecond durations.
- Gregorian epoch arithmetic and month clamping must retain time and fractions.
- Offset-local and UTC properties must not accidentally use session-local time.
- Nullable converters must translate while retaining the correct result mapping.
- Final projections must exercise server translation; failures must clean owned
  tables and leave unrelated objects intact.

## Task 1: Native temporal expression matrix

Files: create tests/OceanBase.EntityFrameworkCore.LiveTests/LiveTemporalQueryTests.cs.

- [x] Verify seeded nullable DateTime/DateOnly/TimeOnly/TimeSpan/DateTimeOffset,
  Oracle DATE, and same-carrier converter columns before querying.
- [x] Assert calendar/fractional members, Date/TimeOfDay/Ticks/DayNumber,
  signed duration components/totals, and offset/UTC members. Put each expression
  in a predicate as well as the result, including NULL branches.
- [x] Assert DateOnly AddDays/AddMonths/AddYears and DateTime AddMonths/AddYears
  with constant/captured/column amounts, leap years/month ends, DATE composition,
  NULL propagation, converter results and execution-time rebindings.
- [x] Verify clocks against same-session direct server brackets under numeric
  offsets; reject unsupported offset-local MySQL members and unsupported methods.

## Task 2: Evidence-driven corrections

Files: temporal translators/generator and focused Unit/Functional tests only as needed.

- [x] Reproduce native differences through direct public Connector controls.
- [x] Add a meaningful local failing regression before each production change.
- [x] Apply the smallest shared fix, then rerun local and native regressions.

## Task 3: Module acceptance

- [x] Run full Release suites on both EF patches, serial full LiveTests, package
  verification and two-mode/two-patch isolated consumers on the final revision.
- [x] One fresh independent read-only module review; fix material findings with
  RED/GREEN and appropriate reruns.
- [x] Update progress/matrices/README with actual supported profiles and gaps;
  audit credentials/LF/whitespace and owned-table cleanup, then commit locally.

Ignored execution ledger and logs: artifacts/temporal-query/.

## Findings and corrections

The first32 native cases failed across all four initial families. Direct public
Connector controls separated SQL/result defects from transport limitations:

- Tick expressions lacked closing arithmetic delimiters in both dialects;
  Oracle TotalOffsetMinutes had the same defect. Local delimiter regressions
  and native execution now cover projection, predicate and composed methods.
- MySQL nanosecond components scaled the microsecond remainder incorrectly.
  Scale to nanoseconds before modulo1000; the six-digit carrier yields zero
  while retaining SQL NULL. Oracle DATE fractional extraction now first casts
  to TIMESTAMP, and signed interval Seconds truncate toward zero in SQL.
- TimeSpan totals used decimal division, losing tiny MySQL values and producing
  Oracle NUMBER metadata rejected by public readers. Unit scaling also applied
  to the final fractional term rather than the complete duration. Native Double
  constants now promote before division and group the complete seconds sum.
  Direct negative-one-microsecond controls show old zero/overflow versus correct
  Double results; native tests include100ns Oracle values and full endpoints.
- Oracle EXTRACT on zoned timestamps uses UTC; direct extraction of a stored
  midnight/-12:00 value returns hour12. Local DateTimeOffset members now drop
  the zone explicitly before extraction/truncation/subtraction; UTC members
  retain SYS_EXTRACT_UTC. Subtraction previously inherited session timezone,
  producing8 hours for a stored UTC midnight in the tested default session.
- An unspecified TIMESTAMP cast defaults to six fractional digits. Explicit
  TIMESTAMP(7) preserves100ns UTC extraction and clock results. Oracle DATE
  arithmetic now explicitly casts the DATE result, avoiding unspecified prepared
  result precision while retaining declared temporal storage semantics.
- Nullable same-carrier converters were rejected by strict CLR/provider-type
  comparisons. Normalize nullable types for temporal ownership checks; retain
  actual store-family and null-converting rejection rules. All five CLR types
  now have native converter cases. Temporal-valued results apply inverse once;
  independent component/tick/total mappings operate in stored space.

Seven local RED batches preceded production corrections. The72-case native
matrix covers both modes, sync/async and both EF NULL configurations across
members, Date/TimeOfDay/UTC, durations, calendar methods, converters, numeric
session offsets, server clocks, rejection/recovery and endpoints. Fractions
include Oracle seventh digits; offsets include +05:45/-03:30/-12/+14 and
positive/negative subhour offsets. Sessions are restored in finally blocks;
ordinary owned tables use CREATE-confirmed cleanup and one physical session.

SQL DateTime results retain Kind.Unspecified, including UTC-valued projections;
their numeric UTC value is verified separately from Kind. This module does not
promise new temporal methods, named zones/DST, non-Gregorian sessions, arbitrary
converter families or other deployments. QRY-07 remains In progress because
default MySQL Decimal scalar LEAST/GREATEST metadata and wider combinations
remain separate open requirements.

Independent read-only review found0 Critical/0 Important/1 Minor: an existing
synthetic DateTimeOffset fixture used total fractions as Microsecond/Nanosecond
components. CLR-derived expected values first failed all4 cases; consistent
supplied-reader data fixes that test oracle without production changes.

Final validation and commit evidence are recorded in efcore-10-progress.md.

Both EF patches pass6679 local Release tests with16 existing specification
skips. After the synthetic fixture correction, both patches pass16 targeted
functional cases. Full serial LiveTests pass467/467 on each patch (457 live,
10 offline, including72 temporal). The root run is followed by8 latest
endpoint/Kind cases; the compatibility full run includes those final assertions.
Package10.0.0-temporal-query-r1-20261117 passes content/dependency checks and
both-mode consumers on both patches. Source/archive/consumer DLL and README
identities, source snapshot, credential/LF/whitespace checks pass. Final
read-only tagged-table counts are zero in both modes. No push/publication.
