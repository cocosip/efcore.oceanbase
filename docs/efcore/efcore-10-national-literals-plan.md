# Oracle national text literals

Scope: MAP-03, MAP-06 and converter composition under MAP-07. Existing Oracle NCHAR,
NVARCHAR2 and NCLOB mappings must preserve inline Unicode values when the client's ordinary
character set cannot encode them. The Connector 0.3.0 `ClientPreparedSql.NationalLiteral`
implementation at package revision `7f0cf7e` already uses OceanBase's `u'\hhhh'` syntax.
Provider SQL literal rendering is EF work; no Connector internal API, protocol or new option is added.

Use the same syntax for national strings containing non-ASCII, backslash or control characters.
Encode each validated UTF-16 code unit using invariant four-digit hexadecimal. Keep ordinary ASCII
`N'...'`, quote escaping, NULL, parameter binding and MySQL behavior. UTF-16 validation runs before
rendering. Ordinary CHAR/VARCHAR2/CLOB remain governed by the configured client character set.
Native acceptance is limited to the read-only expressions actually executed; column storage and DDL
remain separate acceptance work.

Alternatives: raw `N'...'` remains dependent on client encoding; adding session-aware encoders to
EF would duplicate Connector work. ASCII Unicode literals reuse an existing driver/server contract.

## Implementation and verification

- [x] Add failing mapping tests for national declarations, BMP/supplementary values, backslash,
  quote/control/injection-shaped input, NULL/empty, ordinary ASCII, invalid UTF-16 and converters/clones.
- [x] Run the new tests and observe failure from raw literal rendering.
- [x] Add minimal rendering to OceanBaseStringTypeMapping.GenerateNonNullSqlLiteral.
- [x] Verify real EF sync/async query constants and Schema/HasData literal generation, with MySQL controls.
- [x] Run the full offline Release solution, isolated EF10.0.14 and package consumers affected by literals.
- [x] Walk mapping callers, converter application, SQL quoting, surrogate validation and migration paths;
  update the three status documents and commit the reviewed change.

Review focus: backslashes must not be interpreted as escape prefixes from user input; quotes never end
the generated literal; surrogate pairs retain both UTF-16 units; invalid surrogates fail before SQL;
ordinary/parameter text is not silently converted to national storage. No new dependency or public API.

## Results (2026-11-17)

- Unit RED: 17 failures / 49 passes. Focused GREEN including parameter controls: 74 passes.
- Functional RED reproduced Oracle inline/default SQL failures. A MySQL default assertion was corrected
  to retain the existing expression parentheses. GREEN: 6 passes across both modes and sync/async.
- Live RED reproduced Oracle latin1 EncoderFallbackException and a raw control-literal newline change
  through EF command building. GREEN uses ASCII escapes and preserves the exact Unicode/control text.
  LiveStringLiteralTests has 3 cases: MySQL UTF8MB4, Oracle UTF8MB4 and Oracle latin1; each executes
  sync/async scalar raw queries plus nonempty LINQ EF.Constant comparisons. NULL, empty text, quotes,
  BMP/supplementary characters, backslash-looking escapes and injection-shaped/control text are covered.
  Oracle empty text returns NULL. No test objects are created and no DDL/DML is executed.
- Following the server repair, all six original Connector / EF-equivalent Connector / EF connection paths
  pass. The official scalar connection test now handles OceanBaseDecimal via its public ToDecimal API;
  direct GetInt32 is intentionally not used because Connector typed getters require matching carriers.
  Both identity probes and the 72-reader-per-mode existing query profile pass; both servers report 4.4.2.2.
- LiveTests project on EF10.0.0 and isolated EF10.0.14: each 17 passes (7 live cases, 10 offline
  profile checks); the compatibility snapshot matches every changed source/test file.
- Full Release at EF10.0.0 and isolated EF10.0.14: each 6588 passes (Unit 2573, Functional 2475,
  Specification 1538, Consumer 2), with 16 existing specification skips.
- Package 10.0.0-national-literals-20261117 passes content/range checks; its public consumers on
  EF10.0.0/10.0.14 pass both modes and verify dependency graph/provider DLL identity.
- Reproduce offline suites with `dotnet test OceanBase.EntityFrameworkCore.slnx -c Release --no-restore
  -m:1 -nr:false`. Live project requires `OCEANBASE_RUN_LIVE_TESTS=1` and existing User Secrets.
  Logs, compatibility snapshot and package consumers are in ignored `artifacts/national-literals/`.
  Offline restore used the existing local feed/cache after the remote source failed OS TLS authentication;
  no dependency versions were changed. DML, native defaults/HasData, storage capacity/padding/LOB and
  ordinary Oracle non-national text outside the selected client encoding remain unverified.
- Final independent read-only review: no Critical/Important/Minor findings. Mapping selection,
  clones/converters, query constants, defaults/HasData and migration callers were inspected.
  Review exclusions remain acceptance gaps: native defaults/HasData execution, storage capacity,
  padding/LOB, other server versions and extended deployment transports. No support is inferred for them.
