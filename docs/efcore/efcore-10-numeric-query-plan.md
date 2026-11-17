# Numeric-query native acceptance

Baseline be0b795; QRY-07 numeric functions and CASE/coalesce. Execute inline
under the established test/review/local-commit authorization. Temporal query
acceptance remains in QRY-07; this module cannot close that entire requirement.

## Scope and ownership

Reuse LiveStorageTable and public Connector0.3.0 input/reader APIs. Test the
implemented Math/MathF/decimal overloads against ordinary OceanBase4.4.2.2
MySQL/Oracle modes, sync/async and both EF NULL configurations. No protocol,
precision-narrowing or text-transport workaround. Credentials stay in User Secrets.

Native arithmetic/transcendental results have a documented finite-domain
contract; compare independent CLR results with a justified floating tolerance.
Round has a stronger CLR rounding contract for supported digits/rules and needs
exact result assertions. Converter functions operate in stored numeric space.
Oracle Double input extremes and MySQL default Decimal aggregate metadata are
separate previously recorded boundaries, not reasons to weaken these tests.

## Tasks

1. Add LiveNumericQueryTests using nullable signed integer, Single, Double,
   default/faceted Decimal and converter columns. Reuse one owned table per
   test, verify seeds, and run expressions through EF rather than a client
   projection. Test Abs/Sign/Truncate/Floor/Ceiling aliases, transcendental
   unary/binary functions in valid domains, and exact method selection.
2. Verify all five Round rules and supported captured digits/current rules,
   constant overloads, NULLs, negative/midpoint/neighbor values and native
   precision boundaries. Invalid/row-dependent digits/rules and unsupported
   signatures must reject in predicates with usable connection recovery.
3. Cover finite Min/Max/MinNumber/MaxNumber, EF Least/Greatest, Clamp/CopySign,
   conditional/coalesce/negation and converter-owned results. Document native
   NULL and zero semantics rather than assuming CLR semantics for SQL operators.
4. Diagnose native failures with same-SQL direct Connector controls. Production
   correction requires a meaningful local RED before the smallest shared fix.
5. Both-patch full local/native/package gates, one independent read-only review,
   executor fixes for material findings, docs and credential/LF/snapshot audits,
   residual-table probe and exact-file local commit. No push or publication.

## Review focus

- Final projections must translate the method under test, not silently use CLR.
- Floating tolerance must not hide wrong branch/sign/rounding or large errors.
- Decimal metadata and query results must retain scale without silent narrowing.
- Captured Round options must refresh and invalid values reject before commands.
- NULL operands and converter inverses must remain correct in predicates and
  results; native domain errors are not positive support evidence.

## Results

The 64 native cases cover eight families in both modes, sync/async and default/
relational NULL semantics. Initial48 cases produced20 passes and28 failures.
One test-only reflection error selected Math.Floor(Double) for a Single input;
exact method lookup was corrected. Direct public Connector controls establish:

- Oracle SIGN(0D/0F) returns1 while NUMBER zero returns0. A carrier-zero CASE
  restores CLR Sign(0), without passing syntax zero through a model converter.
- MySQL lacks all five tested hyperbolic functions(1305); Oracle ASINH/ATANH
  return600 on the tested4.4.2.2 server. The provider composes EXP/LN/SQRT and
  Double arithmetic, promotes operands before ABS/multiplication, uses a
  small-input polynomial below1e-3, and guards large inputs against intermediate overflow. Strong
  relative-result tests include both sides of the branch, zero, tiny signed
  values, Asinh at CLR maxima and Sinh/Cosh near their finite result limit.
- MySQL text FLOAT results such as SQRT(1.5) lose significant digits. Normalizing
  to FLOAT then casting to DOUBLE retains the Single value for numeric transport.
  This shared marker also serves Round, aggregates and scalar extrema. Oracle
  retains BINARY_FLOAT normalization; no default decimal narrowing was added.
- Default MySQL DECIMAL(65,28) scalar LEAST/GREATEST fail public reader metadata
  validation. Same-SQL GetValue/GetProviderSpecificValue/GetFieldValue of
  OceanBaseDecimal reproduce it; explicit(18,6) succeeds only for that domain.
  Default Clamp has positive coverage. This gap is distinct from existing
  default Decimal SUM/AVG metadata rejection and aggregate MIN/MAX acceptance.

Nullable decimal/floating same-carrier converters had strict nullable-type
comparisons that rejected server translation and could permit final client
projection to throw on NULL. Local RED cases precede normalized CLR/provider
type checks; Single marker validation accepts a nullable numeric operand.
Emulated hyperbolic final results retain the original converter/type mapping
and Decimal query-result reader, with unmapped syntax constants and no SQL CAST
used merely to restore result ownership. Existing converter-identity tests cover
all implemented function names and accepted numeric carriers.

The earlier targeted native run passed56/56, including nullable floating
converter Round with refreshed captured digits and emulated Asinh inverse-once
results. The final fixture also tests EF Least/Greatest and adds8 Round boundary
cases: Single/Double midpoint bit neighbors,2^23/2^52 and1e8/1e16 cutoffs,
epsilon/maxima, Decimal scale28/neighbors/maxima, signed inputs, all five rules
and digits0/2/max. Its first run stopped at an incorrect test assertion requiring
CASE for Decimal ToZero; TRUNC/TRUNCATE is the correct native operation.

Independent review reported0 Critical/1 Important/0 Minor. Accepted integer
converters could enter emulated Asinh with BIGINT multiplication before floating
promotion. A stored5e9 reproduces MySQL1690 (4 failing MySQL cases/4 passing Oracle
cases);2 local RED cases also pin promotion and syntax-constant ownership.
Multiplication by native Double syntax-one now promotes every stored operand
before ABS/squaring, including physically narrow declarations. Final converter
ownership is retained; the native integer regression uses predicates, avoiding
an unrelated fractional result decoded through an integer converter.

The reviewer declines Connector protocol/Decimal internals, existing Oracle
Double binding, nonfinite/signed-zero/domain equivalence, broader converter/
deployment/temporal acceptance and final cleanup evidence. These remain in
owning rows; no second review was requested. Both-patch full Release suites
pass6665 with16 existing specification skips; focused numeric units pass725/725.
Package10.0.0-numeric-query-r2-20261117 passes archive/dependency checks and
both-mode consumers on both patches. Source/archive/consumer identity audit
passes, including the final source snapshot check.

An initial native invocation omitted the schema/live opt-in flags and executed
no SQL. A first full live invocation omitted the existing serial runsettings:
246/387 passed,16 service-cache warning failures and125 connection/resource
failures followed; cleanup connections also failed for some owned tables.
These failed artifacts are retained. The serial387-case rerun passed373;
all14 remaining failures were EF's service-provider cache warning, caused by
the extra independent fixture configurations. Only the new numeric fixture now
disables service-provider caching; provider cache behavior is unchanged. An
over-parallelized MSBuild invocation was interrupted before
suite output; serial -m:1 -nr:false completed the reported full local gates.

A subsequent reviewed64-case run passed43 with21 socket-resource failures.
The many expressions with Pooling disabled exhausted Windows temporary ports
(10047 TIME_WAIT sockets observed). The numeric fixture now holds one explicit
physical connection through its expressions, balances close in finally and
keeps the existing independent cleanup connection. No pooling/protocol policy
was changed. Logs identify9 CREATE-confirmed cleanup failures across these
runs; their exact catalog names were checked against that allowlist and removed.
MySQL's catalog folds the generated names to lower case. No unrelated object
was dropped; the final read-only catalog probe finds zero tagged diagnostic
tables in both modes.

Final serial LiveTests runs on EF10.0.0 and isolated EF10.0.14 each pass395/395
(385 live plus10 offline profiles), including all64 numeric cases and the
reviewer regression. Both TRX files confirm those totals. Credential, LF,
whitespace, source snapshot and archive/consumer identity checks pass. Failed-run
diagnostics retain their errors with private connection identifiers redacted.

QRY-07
stays In progress in both modes: temporal query acceptance, default Decimal
result boundary, wider converters/declarations and deployments remain open.
No Connector source/protocol change; no push/publication.
