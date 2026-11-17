# Connection lifecycle native acceptance plan

Spec: efcore-10-feature-matrix.md (CON-02..05, DIA-01/02, CFG-04, TST-02).
Baseline: `5f73ef9`, existing `efcore/10.0` development branch.

The user authorized continued implementation, native tests, module review and local commits.
Keep the documented release scope and deferred inventory; do not implement Connector internals.
Existing design/acceptance contracts govern this module. Use the current clean checkout as
required by repository-guidelines.md rather than moving the active development branch.

## Design

Exercise public EF APIs with the published Connector 0.3.0 in both modes, sync/async.
Use only User Secrets and read-only SQL; create no objects and change no credentials.
Test provider-created, supplied closed and caller-opened connection ownership; nested leases;
real server errors; partial reader disposal; mode rejection; and pre/in-flight cancellation
and command timeout. Slow commands must have a bounded delay and no business side effects.
If a proposed delay SQL is unsupported, establish the server boundary before selecting
an equivalent supported command; do not reinterpret a syntax error as cancellation evidence.
After interruption, a broken native session may need close/reopen; prove fresh EF use rather
than claiming transparent session recovery. Keep pooling and other deployments separate.

## Tasks

1. Add `LiveConnectionLifecycleTests.cs` using the existing opt-in and redacted-failure
   conventions. Assert actual connection states and subsequent native SELECT results.
   No production change unless a reproducible Provider failure requires one.
2. For any failure, compare the same public Connector path and add an offline RED test
   before a minimal Provider fix. Connector changes require the user's confirmation.
3. Run focused native tests, existing connection tests and the Release solution; verify
   affected package consumers/patches when production or compatibility contracts change.
4. Obtain a fresh read-only module review, resolve material findings, update the feature,
   gap and progress records with exact evidence, then commit locally. Do not push/publish.

## Review focus

- Native Open state must not be inferred from an intercepted opening callback.
- Caller-opened connections remain usable after context disposal; provider-owned ones dispose.
- Failed/mismatched opens must not leak EF lease counts or dispose caller-owned connections.
- Timeout/cancellation must come from an executing command, with correct exception/token facts.
- Read-only errors and interruptions must leave an explicit, tested recovery path.
- No connection strings, credential-bearing messages or inner exceptions enter artifacts.

Run focused tests with `OCEANBASE_RUN_LIVE_TESTS=1` and the
`FullyQualifiedName~LiveConnectionLifecycleTests` filter. Schema opt-in is unnecessary.
Ignored evidence: `artifacts/connection-lifecycle/`.

## Findings and corrections

- The current servers do not advertise TLS. The user explicitly authorized non-TLS
  tests; `OCEANBASE_LIVE_DISABLE_TLS=1` changes test strings only in memory. Without
  that opt-in, configured TLS policy is preserved. Secrets and defaults are unchanged.
- Direct Connector controls establish that disposing an unfinished native response
  breaks the session, while consuming all rows/results preserves it. EF now consumes
  healthy remaining responses through public reader APIs before close/dispose. Failed
  readers and non-open connections are not drained. Existing read failure wins over
  cleanup errors; cleanup failures veto retry. Early disposal can wait for the remaining
  response, with the original command timeout (zero means no timeout bound).
- A caller-opened connection reproduced authentication error1045 in all four mode/API
  cases after external close and EF reopen. ValidateConnection had assigned the public,
  password-hidden connection string back to Connector to add ExpectedMode. It now only
  validates supplied configuration and detected mode; it never rewrites supplied strings.
  Provider-created connections still receive ExpectedMode. No credential extraction,
  PersistSecurityInfo opt-in or Connector change is needed.
- Native in-flight interruption retains OceanBaseException/unknown execution outcome
  with its cancellation or timeout cause. Tests preserve that contract, including the
  caller token on the immediate OperationCanceledException, and prove explicit close/
  reopen recovery rather than transparent retry.
- Independent review found one Important issue: wrapping native readers unconditionally
  must preserve GetProviderSpecificFieldType/GetProviderSpecificValue/GetProviderSpecificValues.
  Divergent ordinary/provider values fail before the three delegations and pass afterward.
  Follow-up review of the supplied-connection fix found no Critical/Important findings.

## Validation checkpoint

Final native lifecycle tests:56/56 on EF10.0.0, both modes, sync/async. This includes
24 ownership/error cases,4 direct-open controls,4 direct-reader controls,4 partial
enumeration cases,2 pre/post-yield cancellation cases,4 mode-conflict cases,4 reopened
caller-connection cases,4 post-open callback failures,4 timeouts and2 in-flight cancellations.
The new reader cleanup unit family passes11/11. Two configuration-preservation unit
cases replace the former supplied-string mutation expectation and were observed RED.

Final local Release suites on isolated EF10.0.0 and10.0.14 each pass6690 with16 existing
specification skips (Consumer2, Functional2487, Specification1538+16skip, Unit2663).
Connection-lifecycle r1 package content and package-only consumers pass on both patches
and both modes, including supplied-connection configuration preservation.
Final serial full native suites each pass590/590 (577 native,13 offline). Both servers
report4.4.2.2; .NET runtime10.0.14, TLS Disabled, Pooling/OB20/compression off,
UseAffectedRows=false. Read-only final catalog audits report zero EFT_ tables in each mode.
Tested source snapshots, package/consumer DLLs, README and dependency identities match.
Credential-value, Markdown-link and Git whitespace/LF audits pass; no push/publication.

SaveChanges cancellation/atomicity, transaction handles/savepoints, broader facade/schema,
diagnostic logging/correlation and other deployment profiles retain their own acceptance
requirements. Pooling, TLS, proxy/HA/OB20 and cross-platform evidence are not established here.
