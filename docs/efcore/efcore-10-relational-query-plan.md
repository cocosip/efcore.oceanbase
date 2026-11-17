# Relational query composition implementation plan

Execute inline with superpowers:executing-plans against base037df34, followed by
one fresh independent review. Spec: unchanged QRY-03 acceptance in the feature
matrix and docs/repository-guidelines.md. Goal: establish actual OceanBase
results for existing joins/correlations/quantifiers/Contains/set operators,
fixing only measured Provider differences. Tech: EF10, Connector0.3.0 public
ADO.NET, existing LiveStorageTable and User Secrets.

## Design and interfaces

Reuse CREATE-confirmed single-table ownership and independent cleanup. A six-row
self-related dataset has missing parents, duplicate nullable keys/values,
nullable bounded labels, Boolean values and a string-converted enum. Model-cache
keys include context type, mode, unique table and designTime. Both modes run
sync/async with default/relational NULL options. Compare native results against
independent in-memory expectations, explicitly accounting for SQL three-valued
logic rather than treating relational NULL mode as CLR equality.

Approach: add native acceptance to the existing implementation. Reuse local SQL
and supplied-reader tests for exhaustive shape/error contracts; they cannot
replace live results. A new query abstraction, driver implementation or separate
test database is unnecessary. No additional deployments, graph Include, grouped
results, new operator families or mapping policy are part of this module.

## Tasks

- [x] Add LiveRelationalQueryTests with inner/left/cross/nullable/composite joins,
  empty operands, true correlation versus joins, correlated scalar/collection
  results, per-parent paging/current captures and ordinary entity identity.
- [x] Add root/correlated Any/All, empty-source vacuity, queryable/captured/inline
  Contains/negation with NULLs/duplicates/rebinding, and native typed collection
  roots for the existing numeric/text/Boolean/binary/Guid/temporal carrier profile.
  Ordinary finite Oracle Double samples do not close its known range boundary.
- [x] Add Union/Concat/Intersect/Except, nullable/text/converted/entity results,
  nesting, operand/outer paging and comparer rejection followed by recovery.
- [x] Run serial module acceptance. Diagnose failures against owning source and,
  where relevant, direct public Connector controls. Before a product fix, capture
  a meaningful local RED, implement the shared correction, then GREEN.
- [x] Run both-patch live and applicable full/package gates, one independent
  review and one fix pass for material findings. Audit cleanup/credentials,
  update matrices by evidence and commit locally; do not push or publish.

## Review focus

- Ordinary scalar-key joins must not acquire CLR null-equality compensation;
  nullable/composite predicates must honor their configured NULL semantics.
- LEFT JOIN must distinguish a missing entity from a present row with NULL data.
- All(empty) is true; Contains/negation may be UNKNOWN in relational NULL mode.
- Nested correlated Skip/Take captures must refresh without stale parent scopes,
  and collection shaping must retain parents with no matching children.
- Set operations treat NULLs as equal, retain Concat multiplicity and grouping,
  and converters use stored-value semantics; paging requires explicit ordering.

## Evidence boundary

QRY-03 closes only if its existing criterion is met; missing evidence remains
open. String collation beyond the bounded ASCII sample, arbitrary CLR shapes,
other server versions/deployments, full official runtime families and physical
wire cancellation keep their existing separate requirements. No test may accept
an unrelated exception as evidence of an advertised rejection.

## Findings

LiveRelationalQueryTests adds 60 cases: 56 across both modes, sync/async and default/
relational NULL semantics, plus four Oracle text-query carrier cases. The existing owned-table fixture and public Connector
APIs remain unchanged. Only the bounded binary test updates two seed rows to
verify membership against distinct non-null stored payloads; other seed payloads
remain NULL. It independently verifies RAW/VARBINARY root enumeration and property
membership. Mixed bounded/default-BLOB requirements for one root retain the existing
mapping-conflict rejection; this module does not relax mapping ownership.

Ordinary scalar-key Join and an equality SelectMany decorrelate to uncompensated
SQL equality. Anonymous composite Join retains EF nullable-key compensation by
default and removes it with UseRelationalNulls. Quantifiers preserve empty-source
vacuity; relational Contains can produce UNKNOWN, including a required bool read
failure. Tests pin that specific error and use bool? for projected UNKNOWN.

Direct packaged Connector controls reproduce 1790 for SByte/Byte parameters when
only the first UNION branch is cast, and 12704 for mixed NVARCHAR2/native string
branches. Casting all parameter/constant branches through the existing carrier
policy resolves both. MySQL retains first-branch typing, and ordinary function
expressions keep their original rendering and validation. No parameter codec,
reader or connection policy is changed. Local common-carrier RED precedes GREEN;
full-suite function regressions also fail before the scalar-only correction.

Further direct controls prove narrow Oracle VARCHAR2/NVARCHAR2/CHAR/NCHAR casts
silently truncate a five-character query input to four characters. TO_CHAR/TO_NCHAR
preserve five/2001/4001-character ASCII values in all UNION branches. The shared
variable non-LOB text collection renderer now uses those charset-preserving functions,
independently of contextual column length. A native RED first reproduced lost
values; four Oracle sync/async/NULL-mode cases then verify exact values, NULL,
duplicates, empty/reversed captures and ordinary/national variable facets through
2001 characters. Fixed contextual roots explicitly reject after the review correction
described below. Local SQL/carrier tests also go RED to GREEN. This changes no
tracked-write length validation or physical column declaration.

Ruling: the attempted analogous MySQL contextual-property root returned 1267
(collation conflict), distinct from the Oracle truncation. Keep that wider MySQL
collation composition unaccepted and QRY-03 In progress for MySQL; ordinary
default text roots and bounded binary membership still have positive acceptance.
No speculative charset/collation policy is introduced to make a test pass.

Direct CAST(binary AS BLOB) returns 932; TO_BLOB succeeds for scalar/UNION/NULL
roots. The generator now uses TO_BLOB for Oracle BLOB collections. Direct ordinary
BLOB equality and IN also return 932, while RAW membership and DBMS_LOB.COMPARE
controls succeed. The Provider rejects ordinary BLOB comparisons/membership before
ADO, preserving enumeration and IS NULL/IS NOT NULL. Converted BLOB values follow
the same restriction. Existing Queryable LOB set-operation exclusions remain.

Ruling: keep a clear BLOB rejection and bounded RAW alternative rather than add
a new LOB comparison API. Misstating this boundary would send unsupported SQL or
advertise unverified LOB semantics. No general LOB comparison support is claimed.

Ruling: preserve existing unsupported comparer/unconfigured EF.Parameter and
mapping-conflict boundaries. The initial record-constructor ordering and mixed
binary-root failures were fixture composition errors, not evidence to rewrite
translation or mapping policy. Wider compositions remain unverified.

## Verification

Pre-review r2 Release solution runs on EF10.0.0 and isolated EF10.0.14 each pass 6650 tests
with 16 existing specification skips. This includes the new carrier/BLOB guards
and unchanged ordinary-function contracts. Pre-review serial full LiveTests each pass
195 cases (185 live and 10 offline profile checks), zero failures/skips, including
all 60 added composition cases. The isolated source matches all 254 source/test
files and its assets resolve EF10.0.14 and Connector0.3.0.

The pre-review package 10.0.0-relational-query-r2-20261117 passes content/range checks and
both-mode package-only consumers on both EF patches. Dependency graphs and
source/archive/consumer Provider DLL plus packaged README identities match.
No push/publication or Connector source changes. Final post-review gates are
recorded below once complete.

## Independent review

One fresh reviewer found no Critical/Minor issues and two Important boundaries.
The single executor fix pass adds subquery-projection/explicit-value checks to
the BLOB IN guard. A mixed RAW item/BLOB subquery was already caught by default
NULL compensation's EXISTS equality, but bypassed the guard with relational
NULLs; that actual query first failed the expected-rejection assertion.

TO_CHAR/TO_NCHAR yields variable text and cannot establish CHAR/NCHAR blank-padding
comparison semantics. The original fixed-facet test used NULL properties and
an OR-NULL predicate, proving value preservation only. Oracle contextual fixed-text
roots now reject explicitly before execution; variable roots retain the lossless
carrier. Four native rejection assertions first failed with no exception.
Ordinary scalar fixed-text comparisons and physical mappings are unchanged.

Ruling: explicitly exclude contextual Oracle fixed-text roots rather than invent
a padded comparison policy. Otherwise a user could silently receive different
rows. QRY-03 remains In progress for both modes; no criterion is weakened.

The review set aside general collation/non-ASCII, LOB and wider configured-carrier
compositions; other server versions/deployments, physical cancellation and
performance; and deeper captured Skip/Take rebinding beyond the tested one-level
captures/constant nested pages. These remain unaccepted, because the current
evidence cannot guarantee their results. No second review or deferred Minor fix.

## Post-review verification

Final r3 Release solution runs on EF10.0.0 and isolated EF10.0.14 each pass
6650 tests with 16 existing skips. BLOB unit tests pass 11/11 and the four
Oracle variable-value/fixed-root-rejection native cases pass after their REDs.

The EF10.0.0 full live run passes 189/195; six existing text/temporal cases hit
physical handshake packet closure (ProtocolException, native error code 0),
before executing SQL. All 60 composition cases pass. Targeted repetition of the
three affected method families passes 10/10 unchanged; exact TRX names confirm
all six failed cases are among those passes. This is not a single-run 195/195
claim. No new connection setting, Connector implementation or retry policy is
introduced.

EF10.0.14 post-review live passes 194/195, with one existing temporal case failing
in physical handshake, including its independent cleanup connection. Repeating
that complete method family passes 4/4 unchanged; the exact failed case passes.
The CREATE-confirmed table is no longer present, and a final independent read-only
catalog check finds zero tagged diagnostic tables in either namespace. Both
post-review full runs pass all 60 composition cases. These recorded recovery
checks do not prove reliable physical connection behavior or erase first-run errors.

Final package 10.0.0-relational-query-r3-20261117 passes content/range checks,
both-mode/both-patch consumers, dependency graphs and source/archive/consumer DLL
plus README identity. Final snapshot source/test identities, credential audit and
LF/whitespace checks pass. Local commit closes this bounded module; no push or
publication. QRY-03 remains In progress in both modes (11 Done/42 In progress per
mode), with MySQL contextual text1267 and wider compositions still open.

Ruling: preserve the initial transport failures and their targeted recovery
evidence. Successful later connections do not establish the cause of an earlier
handshake interruption; no Provider or Connector defect is inferred from it.
