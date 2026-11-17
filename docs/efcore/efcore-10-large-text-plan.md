# Large text and Oracle BYTE storage

Scope: existing MAP-03/06/07 acceptance, with narrow native UPD evidence. Base: 234387f.
Use public Connector 0.3.0, existing User Secrets, ordinary accounts, unique owned tables
and the preceding storage fixture's CREATE-success/independent-cleanup contract.

Design: reuse that fixture for separate sync/async LOB and BYTE cases. Attempt ordinary
CLOB/national NCLOB in Oracle and LONGTEXT in MySQL as CLR strings; verify NULL/empty,
quotes/newlines/supplementary Unicode, values beyond inline limits, converter materialization
and tracked updates. Do not expose locator APIs or develop transport protocols.

Oracle explicit VARCHAR2/CHAR BYTE capacity belongs to the database character set. Record
the actual server charset and test exact byte boundaries/padding and server overflow, along
with conservative Provider scalar-length rejection. Do not infer database bytes from client
encoding or weaken the requested storage test to a read-only expression.

Approaches: reuse owned CreateTables and tracked writes; raw SQL alone would bypass mapping,
while a separate generic fixture duplicates existing ownership logic. First extract the
existing fixture body without changing its acceptance; prove the six existing cases green.

- [x] Reuse owned fixture; retain six bounded cases and add LOB/BYTE live tests.
- [x] Observe failures; add local RED cases for any Provider defect, then minimal fixes.
- [x] Verify both modes/paths, relevant full/patch/package gates and independent review.
- [x] Update progress/feature/gap records and commit locally.

Review focus: ordinary/national transport and reader carriers, NULL versus empty, values
beyond 4000/32767 boundaries, converters, server charset versus client charset, BYTE bounds,
cleanup on partial failure, no added Connector capability or credential persistence.

## Native findings

The initial six-case extension had two MySQL passes and four Oracle CREATE failures with
902. Direct Connector DDL isolated the declarations: CLOB, VARCHAR2 BYTE and CHAR BYTE
succeed; NCLOB returns 902. This is a 4.4.2.2 server limit. It is preserved in separate
sync/async ALTER rejection tests that also verify the owned table remains usable; national
LOB storage is not silently redirected to CLOB or claimed supported. Existing NCLOB local
mapping/literal tests are SQL-generation evidence only, and native NCLOB remains open.

Ordinary CLOB and MySQL LONGTEXT pass NULL/empty, 4001/32768 ASCII characters, 8192 repeats
of quote/backslash/CRLF/supplementary text, a 65537-character update plus mixed suffix,
and string-converter materialization. These tests add acceptance, with no production change.
Oracle BYTE tests verify AL32UTF8 from the server; 8-byte boundaries/padding, conservative
9-scalar rejection before DML, and 9-byte multibyte rejection (12899) from the server all pass.
The server remains authoritative for bytes; no client-charset inference is added.

## Verification and review

- Six original storage cases passed after lifecycle extraction and before model expansion.
  Initial new cases: 2 pass/4 fail due NCLOB DDL; direct declaration controls isolated
  the server rejection. The final 8 added cases pass, including the explicit negatives.
- Exact final source on EF10.0.0 and an isolated EF10.0.14 snapshot each passes all
  33 LiveTests (23 live, 10 offline query-profile checks). Both use serial collections,
  existing User Secrets and both LIVE_TESTS/LIVE_SCHEMA_TESTS environment opt-ins.
- Read-only catalog checks confirm zero EFT_/EFD_/EFL_ diagnostic tables in either
  namespace after the runs. CREATE ownership and independent cleanup are unchanged.
- No production or dependency files changed. The preceding 234387f module's two-patch
  6640-pass/16-skip full suites and r1 package consumers remain historical evidence;
  they were not rerun/repacked for this test-only module. Current rebuilt binaries carry
  234387f information, while the r1 package carries a47602c information, so their hashes
  differ. This is not new package validation and does not close release gates.
- Independent final source review found no actionable findings and requested no fixes.
  Executor compared the final test/snapshot hashes, read the final TRX results and checked
  production source identity and cleanup. `git diff --check` passes.
- Evidence: ignored `artifacts/large-text/`, with initial/boundaries/live-final/
  compat-live-final results, direct DDL controls and cleanup verification. No credentials
  or connection settings were added to Git; Oracle PrepareExecute remains test opt-in.

Review boundaries: successful NCLOB transport, other versions/charsets/collations,
streaming/locator APIs/theoretical capacity/performance, large seed/default literals,
explicit transaction/cancellation/retry/multirow recovery and broader schema/migration/index
behavior remain unverified. Those existing matrix requirements are not closed by CLR-string
storage acceptance. Exact-source verification and document closeout are executor evidence,
not additional reviewer execution. No deferred minor findings.
