# Raw SQL and diagnostics native acceptance plan

Spec: efcore-10-feature-matrix.md (QRY-10/11, SQL-01, DIA-01, TST-02).
Baseline: `8e2f269`, existing `efcore/10.0` branch.

This adds native evidence for implemented APIs, not a new raw-SQL dialect or
Connector feature. Reuse LiveStorageTable's opt-in, exact CREATE ownership and
cleanup. Only uniquely named EFT_ tables in the authorized current namespace
may be written; no business objects, credentials or namespace changes.

## Scope and tasks

1. Add LiveRawQueryTests with assigned-key, nullable/converter rows and quoted
   mapped column names. Cover raw/interpolated entity and scalar SQL, composed
   predicates/paging, tracking/identity, NULL/hostile parameter values and raw
   UPDATE counts/values in both modes sync/async.
2. Compare each actual command's SQL and bindings across refreshed values.
   Verify default command logs redact runtime parameter values. Capture native
   command-error identity without printing logs or arbitrary exception messages.
3. Cover missing columns on empty/nonempty readers, converter/required-NULL
   failures, unsupported composition, repeated native parameter mode boundaries
   and a successful query after each failure. Use no malformed protocol mocks.
4. Run focused native suites on EF10.0.0 and isolated10.0.14. A production failure
   requires direct Connector comparison and local RED before a minimal fix.
   Full local/package checks repeat only if affected source changes.
5. Obtain independent read-only review, resolve material findings, update matrices
   with exact supported profile and remaining gates, then commit locally.

## Review focus

- Generated identifiers must delimit mapped names and escape MySQL backticks;
  raw identifiers are trusted fixture names, while values must remain parameters.
- A mapped nullable converter must follow real reader metadata and NULL semantics.
- Entity/scalar/ExecuteSql API coverage must not be inferred from each other.
- Error tests must prove their intended failure, then verify connection reuse.
- Default logging must exercise actual commands with a unique runtime value;
  sensitive-data opt-in and external Connector/server sinks remain separate.

Ignored evidence: artifacts/raw-diagnostics/. Connector/public driver changes
require separate user confirmation. TLS Disabled is the already authorized
test-process profile; no change to User Secrets or defaults.

Ruling: the initial column name contained both a backtick and a double quote.
MySQL passed12/12; all12 Oracle cases failed during CREATE with600 before their
test bodies. Direct read-only Connector controls on4.4.2.2 reproduce600 for
``SELECT 1 AS "Row`""Key" FROM DUAL``, while ``"Row`Key"`` and `"RowKey"` pass.
The fixture therefore retains the embedded backtick in both modes; Oracle embedded
double quotes remain a separate unaccepted SQL-01 server boundary. No Provider
escaping change or silent assertion removal is used. Final read-only catalog counts
after this failed run are0 in both modes.

Ruling: native Oracle repeats the same named parameter twice in SQL with one
public command parameter; the returned row proves both references execute. The
first expected two-value assertion was based on an intercepted reader profile,
not the actual native contract. Assert one unchanged caller parameter and two
named SQL occurrences; MySQL's documented no-clone rejection remains explicit.

Independent review: no production/safety defect. One Important evidence gap before
closing DIA-01: success-command redaction plus native failure interceptor identity
does not prove native failure-log redaction. The invalid-column test now includes
a bound hostile value and asserts both Failed executing DbCommand and value absence
from the captured command logs before a successful recovery query.

## Verification checkpoint

EF10.0.0 targeted28/28 passed before the last failure-log assertion. Final
EF10.0.14 targeted28/28 includes that assertion, scalar positive/NULL values,
UPDATE SQL invariance and Oracle named-parameter refresh with both references
affecting results. The first full EF10.0.0 run returned545pass/73fail: all73 failures
are ManyServiceProvidersCreatedWarning, with no distinct error. Adding the raw
fixture's detailed-error/logging configuration exhausted EF's process-wide internal
service-provider cache and affected later test profiles. The raw fixture now uses
EnableServiceProviderCaching(false), following the existing ownership-test isolation
pattern. This changes only its test options; no warning is suppressed or Provider
behavior altered. Both-mode diagnostic table counts after that run are0. Final
EF10.0.0 and isolated EF10.0.14 full projects each pass618/618 (605 native,
13 offline), with no failures or skips. The final read-only catalog audit finds
zero EFT_ tables in both modes. This completes this test-only batch.

No production or shared fixture source changed. The exact production sources
still match the connection-lifecycle package snapshot. Its both-patch local
Release6690+16 existing skips, full native590 and package consumers are retained
as baseline evidence, separate from the new618-case two-patch full run. Package
restore/repack and the unaffected offline solution need no repeat for a test-only
file in the separately opted-in LiveTests project.

QRY-10 acceptance is the declared ordinary direct4.4.2.2 raw entity/scalar/DML
profile. DIA-01 covers ordinary EF command/failure logs and standard interceptors;
sensitive-data opt-in remains existing offline evidence. External Connector/server
sinks, native diagnostic correlation, write-exception/entry and cancellation
contracts remain separate. SQL-01 keeps the Oracle embedded-double-quote boundary;
broader QRY-11/runtime specifications and other deployments are not completed here.
