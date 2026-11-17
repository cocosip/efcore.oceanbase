# Oracle ordinary text newline literals

Scope: MAP-03, MAP-06 and MAP-07 in the existing feature matrix. Oracle CHAR/VARCHAR2/CLOB literals
must preserve CR, LF and CRLF when EF constructs indented SQL commands, without switching ordinary
text to national storage or bypassing the configured client character set. No Connector change.

Design: split only at CR/LF, quote ordinary text fragments using the existing base mapping, and pair
adjacent fragments with `CAST(CHR(13/10) AS CHAR(1))` operands into a balanced concatenation tree.
Keep ordinary text without CR/LF, national literals, MySQL literals, NULL and parameter binding on
their current paths. Validate UTF-16 before rendering and preserve clones/converters.

Raw multiline literals are changed by EF indentation/newline construction. Reusing national `u'...'`
would change the ordinary character-set contract. Bare CHR concatenation changes blank-padded CHAR
comparison semantics: a read-only OceanBase 4.4.2.2 comparison against CHAR(20) returns 0 with bare CHR,
but 1 with CHR explicitly cast to CHAR(1). Preserve CHAR operand types instead. Test long VARCHAR2
query values without introducing column-size restrictions on query literals. Flat concatenation of
4000 newline fragments returns server error 600; a balanced equivalent returns the exact 4000 characters.

## Implementation and checks

- [x] Add failing unit tests for ordinary CHAR/VARCHAR2/CLOB newline literals, quote/backslash injection,
  CR/LF/CRLF at edges/repeated, empty/NULL, Unicode mixed text, clone/converter and invalid UTF-16.
- [x] Add failing real EF constant-query/default/HasData SQL tests with ordinary string properties.
- [x] Add live sync/async read-only value and padded CHAR-comparison tests; prove current LF corruption.
- [x] Add the minimum shared mapping fix and turn tests green. Preserve the configured client encoding.
- [x] Full Release solution, isolated EF10.0.14, live cases on both patches and matching packed consumers.
- [x] Independent code walkthrough, three status-document updates and local commit.

Review focus: split only validated value line breaks and double quotes on each ordinary fragment; no injection through quotes
or backslashes; no raw CR/LF inside the emitted literal expression; CHAR comparisons retain padding;
converters/clones share the same implementation; no column-capacity restriction on query operands;
ordinary text unencodable by the selected Connector client encoding must still fail clearly.
Native DDL/storage capacity/padding and LOB acceptance beyond these read-only expressions stay open.

## Results (2026-11-17)

- Unit RED: initially 19 failures / 9 passes from multiline literals; balanced-fragment expectations add
  odd/consecutive fragment cases and produce 25 failures / 9 passes before the final fix. Focused GREEN
  for StringMappingTests, StringParameterMappingTests and quote/backslash controls: 108 passes.
- Functional RED: 3 failures / 9 passes; new Oracle constant-query/default/HasData SQL assertions fail.
  GREEN: 12 passes across both modes and sync/async query paths. Generated Schema SQL only.
- Live RED: both new ordinary Oracle UTF8MB4/latin1 cases read LF as CRLF; the 3 existing national/MySQL
  cases pass. GREEN: all 5 expanded literal cases pass, including LF/CR/CRLF, control/quote/injection-shaped
  values, mixed Unicode, 4000-character ordinary values (one LF, all LF or repeated CRLF) and padded CHAR(20).
  The latter protects the CHAR(1) cast: bare CHR concatenation produces the wrong comparison result.
  Ordinary latin1 Unicode text outside the client encoding still throws EncoderFallbackException in both
  sync/async paths. National latin1 Unicode continues to succeed. NULL/empty semantics are unchanged.
- Full Release EF10.0.0 and isolated EF10.0.14 each pass 6628 tests: Unit 2607, Functional 2481,
  Specification 1538, Consumer 2. Sixteen existing specification skips remain.
- LiveTests project at both patches: each 19 passes, including 9 live cases and 10 offline query checks.
- Package `10.0.0-ordinary-literals-r2-20261117`: content/dependency checks pass; public consumers at both
  EF patches pass both modes and compare provider DLL identity with the tested baseline.
- Commands: `dotnet test OceanBase.EntityFrameworkCore.slnx -c Release --no-restore -m:1 -nr:false`;
  live project additionally requires `OCEANBASE_RUN_LIVE_TESTS=1` and the existing User Secrets.
  Logs, isolated patch source/results and package consumers: ignored `artifacts/ordinary-literals/`.
  No credential/configuration changes, new objects, DDL or DML. Current server remains 4.4.2.2 ordinary
  direct protocol via public Connector 0.3.0; storage/LOB/native defaults/HasData and other deployments
  remain outside these read-only results. Required parent rows retain In progress.
- Final independent walkthrough found no Critical/Important/Minor issue in the initial implementation,
  but set extreme newline density aside. Executor review classified the normal 4000-character density
  boundary as required: new live cases failed with server code 600 on a flat expression. A direct
  Connector flat/balanced comparison isolated expression shape. The balanced fix passes exact-value,
  CHAR-padding and odd-fragment cases; the full suite, live patches and unique r2 package were rerun.
  Final executor walkthrough checked quoting, validated UTF-16 boundaries, fragment order, odd carry,
  balanced depth, unchanged parameters and the two unaffected mode/facet paths. Native defaults/storage,
  other versions/character sets/transports and larger server expression/packet limits remain unclaimed.
