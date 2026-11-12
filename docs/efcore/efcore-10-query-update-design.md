# EF Core 10 Query and Update Pipeline

## Scope and evidence

This is the runtime query and tracked-update part of implementation Task 4. Database creation, live server
execution, transactions, complete scalar mappings and package validation remain required work in the
[feature checklist](efcore-10-feature-checklist.md). SQL-generation tests and intercepted commands do not prove
that a server accepts the SQL, generates identity values or commits and rolls back correctly.

## Text and temporal aggregate extrema contract (Batch3ad, QRY-04/11, MAP-06/07)

Own the exact Queryable Min/Max generic scalar/selector identities for string, DateTime,
DateTimeOffset, DateOnly, TimeOnly and TimeSpan, including nullable value types. Reuse EF10's
QueryableAggregateMethodTranslator for the already working native MIN/MAX, filter -> CASE -> DISTINCT,
nullable metadata and cardinality shaping. Add only the missing boundary validation in the existing
provider aggregate translator; preserve plugin priority, numeric aggregates and Count/LongCount.

The relational queryable visitor may elide a LINQ Distinct before MIN/MAX because duplicates cannot
change extrema. Direct aggregate-service tests retain explicit DISTINCT input; pipeline tests accept
that inherited optimization instead of forcing redundant DISTINCT into generated SQL.

String results retain OceanBaseStringTypeMapping and its converter/facets. MySQL character/text
storage participates; Oracle CHAR/VARCHAR2/NCHAR/NVARCHAR2 participates. Oracle CLOB/NCLOB extrema
are explicitly unsupported in this profile: do not introduce a truncating VARCHAR cast. Temporal
results retain the existing native date/timestamp/time/interval mappings, transport and reader hooks,
including DateOnly/TimeOnly/DateTimeOffset built-in conversions. Same-family custom converters
participate under stored-value semantics: decode(native MIN/MAX(encode(values))), not arbitrary CLR
ordering or converter equivalence. Null-converting converters and custom/cross-family carriers in
these owned model families fail explicitly before inherited fallback or a command.

Empty/all-null string and nullable extrema return null; nonnullable temporal empty results use EF's
no-elements exception. An empty GroupBy produces no row. Preserve current parameters in filters and
HAVING/order composition, typed NULL CASE arms and result mappings independently of column capacity.
Collation, Oracle empty strings, datetime/interval ordering and native aggregate metadata/results
remain live acceptance gates; intercepted rows prove only command bindings, shaping and decoding.
Boolean, enums, Guid, binary/custom scalar families and comparer overloads remain separate contracts.

Source contracts: published EF10.0.0 QueryableMethods/QueryableAggregateMethodTranslator and relational
aggregate-provider plugin order. Fresh baseline method/provider decompilations are cached under
`artifacts/non-numeric-extrema-source/`; the translator is in `artifacts/local-development/reference/query-shapes/`.
The older `query-aggregates/` method/provider manifest records EF10.0.14, not the baseline. OceanBase's pinned documentation revision
`2a5ae3a23a51729d065cec716508eb26cb064106` describes character/date MIN/MAX, same-expression result
types, ignored NULLs and DISTINCT/ALL; cached MIN/MAX pages and hashes are in the aggregate manifest.
[Oracle MIN](https://docs.oracle.com/en/database/oracle/oracle-database/19/sqlrf/MIN.html) also documents
character collation and date examples. These references establish contracts, not deployed acceptance.

Verify all eleven native/nullable type shapes with both identities/operators/modes, exact mapping
identity, native reader/converter ownership, filtered DISTINCT/group/HAVING/order SQL, sync/async
current bindings, empty/all-null/no-group results and negative carrier/null-converter boundaries.
Run full Debug/Release, fresh isolated EF10.0.14 and one baseline package at both patches/modes.

## General text collection-root contract (Batch3ae, QRY-03/06/11, MAP-06/07)

Support captured arrays/lists and originally empty inline arrays whose element mapping is an
`OceanBaseStringTypeMapping` with a string provider carrier and a converter that does not convert
nulls. The existing VALUES expansion keeps one raw model value per row, current capture snapshots,
duplicates/order and the optional `_ord` column. Constant and MultipleParameters policies remain
supported; an explicit `EF.Parameter(collection)` root remains unsupported without a primitive-
collection translator.

Use the same mapping for collection parameters, predicates and projected readers, including ordinary
model-to-string converters. MySQL synthetic typing uses `CAST(... AS CHAR)`; Oracle uses the mapping's
declared text type (for example `NVARCHAR2(...)`) so empty roots retain typed NULL metadata and
`WHERE 1 = 0`. Oracle's later string normalizer handles empty-string-as-NULL after raw model values
have been expanded. Null-converting converters, binary/temporal/other carriers and unknown column
layouts reject before command execution.

Cover SelectMany, joins/ordinal or other direct-root compositions as they are added, current values,
empty transitions, NULL/empty text, converter-owned parameter/read values, sync/async bindings and both
compatibility modes. Intercepted SQL and readers establish translation, mapping and shaping only;
native text collation, LOB behavior, UNION coercion and server results remain live gates.

## Boolean collection-root contract (Batch3af, QRY-03/06/11, MAP-06/07)

Support captured arrays/lists and originally empty inline `bool`/`bool?` roots in both compatibility modes by
reusing the general VALUES expansion. Preserve current captures, order, duplicates, NULL cells, empty roots,
`Constant`/`MultipleParameters` policies, provider-owned Boolean mappings and non-null-converting Boolean-to-
integer/decimal converters. `EF.Parameter(collection)` remains outside this translator boundary.

MySQL synthetic collection typing uses `CAST(... AS SIGNED)` or the carrier's unsigned/decimal target; Oracle
uses `CAST(... AS NUMBER)` and emits `FROM DUAL` for every SELECT arm. Oracle's decimal Boolean carrier and
reader continue to decode `OceanBaseDecimal` values to `bool?`. Unknown layouts, null-converting converters,
non-Boolean model carriers and binary/temporal/other mappings fail before command execution. Intercepted
bindings and SQL prove local translation, mapping and shaping only; native Boolean UNION coercion, Boolean
result semantics and live OceanBase execution remain open.

## Contextual numeric collection projection contract (Batch3ac, QRY-03/11, MAP-06/07)

Support direct collection-element projection in SelectMany, scalar FirstOrDefault and Join after a
numeric property comparison supplies a converter or explicit store mapping. Include captured/inline,
nullable, originally empty and composed subquery shapes in both modes. Retain the property's exact
mapping/converter for parameters, literals and projected readers; native collection defaults still work.
This supersedes Batch3ab's documented contextual projection exclusion for the cases covered here.

EF10 applies a default mapping to a projected untyped VALUES column before its final inference scanner.
The comparison can independently infer a different property mapping. Remap only that premature
default projection when an originally untyped numeric root has a contextual mapping; do not erase
typed inline values, explicit casts, property columns or genuine predicate requirements. Follow direct
column projections through subqueries. Scalar-subquery result mapping must follow its element mapping.
Do not catch or suppress EF's inference errors. Different store types or different converters required
for the same SQL root remain an explicit conflicting-mapping error, including same-store converters.
Separate roots over one capture may have different mappings and must retain independent conversions.

Collect actual operator/predicate requirements before rewriting: sharing one ColumnExpression object
between projection and predicate cannot weaken the predicate. Scalar-result consumers are requirements
too. A converted FirstOrDefault compared against an incompatible native property, converter or literal
rejects rather than comparing encoded provider values with unconverted model values. Arbitrary result
operator/converter composition remains unsupported. For direct nonnullable FirstOrDefault projections,
both COALESCE's fallback literal and result mapping follow the element mapping, so an offset converter
encodes the CLR default correctly. NULL tests impose no numeric carrier when null conversion is disabled.

Distinct needs an element KeyComparer before EF's final mapping pass. Normalize its eligible projected
columns early using contextual ownership, or the native default when no context exists. Final mapping
inference and all genuine-conflict checks still run. Exact contracts are the published EF10.0.0
RelationalTypeMappingPostprocessor, SelectExpression and ColumnExpression APIs; no private reflection.

Verify real EF translation trees, exact sync/async bind/read converters, order/duplicates/nulls/empty
transitions, carrier casts, clone ownership and native defaults. Include genuine multi-context conflicts,
explicit conversions and nonnumeric/null-converting boundaries. No native result/coercion claim follows
from intercepted commands. Computed converter arithmetic and other collection carriers keep separate
contracts; do not advertise arbitrary converter equivalence.

## Decimal and floating collection-root contract (Batch3ab, QRY-03/11, MAP-06/07)

Extend the existing integer VALUES pipeline to decimal, Single and Double, nullable forms and
non-null-converting numeric-to-numeric converters. Both model and provider carriers must be numeric;
the original mapping owns conversion, binding and reading. Preserve contextual mapping inference,
capture snapshots, exact duplicates/order/NULL cells, current values and empty-inline metadata.
Constant, MultipleParameters and the default policy participate; single JSON parameters remain separate.

Typing casts follow the provider carrier, never the physical column's narrower facets: MySQL uses
DECIMAL(65,30), FLOAT or DOUBLE, Oracle NUMBER, BINARY_FLOAT or BINARY_DOUBLE. Every CLR decimal
fits DECIMAL(65,30) without rounding (at most 29 integral and 28 fractional digits). Reject non-finite
floating inputs through the mapping's existing validation. The MySQL CAST documentation and public
[v4.3.5_CE_BP5 parser cast_data_type production](https://github.com/oceanbase/oceanbase/blob/v4.3.5_CE_BP5/src/sql/parser/sql_parser_mysql_mode.y#L6483)
confirm decimal/float/double syntax; Oracle CAST's
conversion table confirms NUMBER and binary floating targets. This is source evidence, not live proof.

Cover both modes/null settings/policies, arrays/lists, inline/captured empty roots, nullable values,
min/max, scale-28 decimals, floating subnormals, rebinding and converter provider ownership. Intercepted
sync/async commands must verify exact values/DbTypes and mapping readers independently of SQL shape.
Native UNION coercion/results and older-server syntax remain live acceptance gates. Text/other carriers
and contextual projections of converted collection elements remain separate work: SelectMany,
FirstOrDefault and Join can fail EF's earlier conflicting-mapping check. Nonprojected contextual
roots and ordinary native element projections participate; do not bypass EF's conflict validation.

## Wider integer collection-root contract (Batch3aa, QRY-03/11, MAP-06/07)

Extend the existing VALUES expansion/UNION ALL renderer and empty-inline metadata path to sbyte,
byte, short, ushort, int, uint, long and ulong, their nullable forms and integer-backed enums.
This supersedes the narrower int/int? carrier boundary in the earlier collection-slice records below.
The supported model and provider types must both be integral; preserve the mapping and converter
instance rather than converting values eagerly or stripping reader hooks. Null-converting and
noninteger storage mappings reject before opening. Both MySQL query casts use the provider carrier's
signedness (SIGNED or UNSIGNED), and Oracle uses NUMBER independently of column precision.
This prevents UInt64 values above Int64.MaxValue from becoming signed or narrowing to column facets.
Explicit nullable-model/provider converters retain nullable mapping CLR types; unwrap those types only
for validation, retaining the original converter, parameter binding and reader NULL handling.

EF10's base type-mapping postprocessor throws UnreachableException for a parameter VALUES root
without an inferred comparison mapping, including standalone projected roots. A derived mapping
postprocessor keeps TryGetInferredTypeMapping authoritative (and preserves conflicting-mapping
errors), then uses the parameter's element mapping or original sequence element CLR type as the
fallback for supported integer roots. It uses the provider's ordinary collection mapping and retains
the base processor for other families. No default mapping is forced before contextual inference.

Captured roots keep exact sequence length/order/duplicates, including NULL cells; do not pad UNION
rows as IN-list bucketing does. Empty roots retain both ordinal and element mapping metadata and
render a typed SELECT with WHERE 1 = 0. The original inline CLR element type survives when the
element column is pruned. Existing Constant/MultipleParameters/default policy, capture snapshots,
unique parameter names and command-cache invalidation remain unchanged. Explicit single-parameter
JSON-style roots and EF's direct converted-Join mapping conflicts are not promoted by this work.

Verify both modes, both null settings, all three supported collection policies, min/max and nullable
inputs, arrays/lists, inline/captured zero rows, joins/count/ordinal shapes and runtime rebinding.
Intercepted reader values establish shaping and exact native parameter values/types, not server
cardinality or UNION coercion. Native execution, text/other carriers and wider converter projections
remain separate work. The source contracts are the published EF10.0.0 VALUES/collection pipeline
already inspected for Batch3j/3k; selected EF10.0.14 and package consumers must cover this extension.

## EF Core 10 contracts

The implementation was checked against the EF Core Relational 10.0.0 assembly and these upstream sources:

- [Provider implementation guidance](https://learn.microsoft.com/ef/core/providers/writing-a-provider).
- [Relational service registration](https://github.com/dotnet/efcore/blob/v10.0.0/src/EFCore.Relational/Infrastructure/EntityFrameworkRelationalServicesBuilder.cs).
- [Query SQL generator](https://github.com/dotnet/efcore/blob/v10.0.0/src/EFCore.Relational/Query/QuerySqlGenerator.cs).
- [Update SQL generator](https://github.com/dotnet/efcore/blob/v10.0.0/src/EFCore.Relational/Update/UpdateSqlGenerator.cs).
- [Generated-value propagation](https://github.com/dotnet/efcore/blob/v10.0.0/src/EFCore.Relational/Update/ColumnModification.cs).
- [Relational command execution parameters](https://github.com/dotnet/efcore/blob/v10.0.0/src/EFCore.Relational/Storage/RelationalCommandParameterObject.cs).

| Contract | EF10 lifetime | Implementation responsibility |
| --- | --- | --- |
| `IProviderConventionSetBuilder` | Scoped | Extend `RelationalConventionSetBuilder` with its two dependency objects. |
| `IModelRuntimeInitializer` | Singleton | Use `RelationalModelRuntimeInitializer` to initialize relational model metadata. |
| `IModelCustomizer` | Singleton | Use `RelationalModelCustomizer` for relational model conventions and DbSet table naming. |
| `IModelValidator` | Singleton | Use `RelationalModelValidator` for the relational model boundary. |
| `IQuerySqlGeneratorFactory` | Singleton | Create a generator using `QuerySqlGeneratorDependencies` and the configured mode. |
| `IMethodCallTranslatorProvider` | Scoped | Add exact string method translations while retaining EF plugins and model DbFunctions. |
| `IMemberTranslatorProvider` | Scoped | Add the exact string Length member using per-mode character-count SQL. |
| `IUpdateSqlGenerator` | Singleton | Use `UpdateSqlGeneratorDependencies` and the configured mode to emit DML. |
| `IModificationCommandBatchFactory` | Scoped | Consume `ModificationCommandBatchFactoryDependencies` and context options. |
| `ModificationCommandBatch` | Per batch | Implement admission, completion, transaction requirements, sync/async execution and value propagation. |

All services are registered through `EntityFrameworkRelationalServicesBuilder.TryAdd` before
`TryAddCoreServices`. The builder maintains its own registration map; mixing direct service-collection mutations
with an already-created builder can cause core defaults to replace provider factories.

## Relational model and queries

Relational conventions and model initialization must run before translating a query. Table names on an otherwise
core-only model are insufficient: query translation requires the table mappings in the relational model.

The query generator retains EF10's relational handling of projection, predicates, joins and aggregates, and changes:

- MySQL paging to `LIMIT <count> OFFSET <offset>`. Offset without a limit uses MySQL's maximum unsigned 64-bit
  limit literal, `18446744073709551615`.
- Oracle table aliases to omit `AS`, and table-free scalar selects to include `FROM DUAL`.
- Oracle paging retains EF10's `OFFSET ... ROWS FETCH NEXT ... ROWS ONLY` syntax.

This does not claim complete function translation, Boolean translation, collection translation or compatibility of
every scalar mapping. Default Oracle strings now use NVARCHAR2(2000), with length/Unicode/fixed facets described
in the [technical baseline](efcore-10-technical-baseline.md). Larger Oracle strings still map to CLOB/NCLOB;
generating an order or group operation over LOB storage does not establish valid execution. Live mapping and query
evidence remains required before advertising support.

## Model and materialization local contract

Batch4a/4b/4c uses the existing EF10 relational conventions, initialized model and query materializers.
There is no new provider convention, tracking implementation or model-dependent service lifetime.
ModelMaterializationTests covers both modes:

- Composite primary and alternate keys; required composite alternate-key relationships and optional
  composite primary-key relationships; explicit table/column/index names, shadow concurrency metadata,
  owned table sharing and a separately mapped owned collection with a shadow ownership key.
- Single-command collection Include then optional reference ThenInclude, required/optional reference
  Include and automatic owned reference/collection loading. Supplied duplicate principal rows must group
  into one graph; equal Id/Code values in different tenants must remain distinct. SQL assertions pin
  composite joins and reader projection order. TrackAll, NoTracking and NoTrackingWithIdentityResolution
  check fixup, object identity and context tracker entries, synchronously and asynchronously.
- First/Single and default variants check empty/one/two supplied rows where appropriate and assert the
  dialect's one/two-row limit. First receives at most one row, reflecting its required server limit;
  the reader does not simulate the server applying that limit.
- EF10 named Tenant/Active filters compose with a captured Name predicate. Selectively disabling either
  filter and disabling all filters preserves the user predicate; changing TenantId and Name between
  executions must bind current values without inserting those values into SQL.

These are inherited-provider characterization tests, not a claim that the server executes joins, filters,
ordering or limits correctly. Graph writes, schema/generation validation, broader graph/query shapes and
live results remain open under MOD/QRY/UPD. Split queries and lazy loading retain their separate boundary.

## Raw SQL local contract

QRY-10 uses EF Core 10's relational raw-command builder and FromSql translation through the provider's
SQL helper, type mappings, connection and OceanBaseConnector 0.2.0 parameters. The four APIs accept
values, including hostile text and NULL, without inserting values into SQL text. Caller-created
OceanBaseParameter instances retain native metadata and names; generated placeholders must match
the tenant's prefix. Query composition uses the provider's identifier delimiters and alias syntax.
Sync/async intercepted commands prove SQL shape, parameters, materialization and affected-row
propagation; they do not execute SQL. Raw text/identifiers remain the caller's responsibility.

MySQL had a confirmed bind contract gap: OceanBaseConnector 0.2.0 sends MySQL statement text
unchanged and binds ordered values to `?` positions. Provider-generated `@name` is a MySQL user
variable, not a prepared statement parameter. This affects generated queries, tracked writes and
raw APIs with generated placeholders, even when intercepted commands pass. The provider now supplies
that positional binding path and offline connector-boundary tests. Oracle has a
separate named-bind path in the connector and is not affected by this mismatch.

MySQL SQL templates retain EF names and redacted log text. A final relational binding parameter rewrites
the materialized command before CommandInitialized, preserving that hook's final-command contract.
Repeated EF-mapped inputs are recreated through their mapping; caller-created native duplicates reject
because Connector 0.2.0 has no public clone that preserves optional scale declarations. Use distinct
native parameters or repeated scalar format arguments. Streams cannot be consumed twice. Quoted or
commented tokens are preserved; SQL_MODE-dependent backslash literals, executable comments with binds,
mixed markers and missing/unused inputs reject. Bind string values instead of embedding such literals.

The scoped database creator supplies the dependency EF's `DatabaseFacade` needs even for
non-DDL calls. `Exists` opens the configured connection and balances its successful open;
only MySQL error 1049 means an absent database. `CanConnect` returns false on failures and
propagates cancellation. `HasTables` probes the current MySQL database or Oracle `CURRENT_SCHEMA`
using metadata visible to the current account. Oracle counts can use the connector's exact
numeric carrier. Offline scripts and explicitly scoped existing-schema table creation are implemented below;
namespace creation/deletion remain guarded. Batch6a covers failed-open ownership/accounting with deterministic
connection doubles; native cleanup and server acceptance remain open under CON-03.

## String concatenation local contract

The QRY-01/QRY-06/QRY-11 concatenation slice fixes string addition and the fixed two/three/four-string Concat overloads.
EF10 StringMethodTranslator produces string SqlBinary Add; the base SQL generator emits `+`, which is not this
provider's concatenation syntax. Preserve numeric addition. MySQL emits `CONCAT(left, right)` and Oracle emits
`left || right`, with nested grouping and type mappings preserved. Keep EF's null-as-empty protection for MySQL;
rewriting before nullability processing must not accidentally discard that protection.

Sources: EF Core v10.0.0 StringMethodTranslator, QuerySqlGenerator and SqlNullabilityProcessor, and OceanBase
documentation revision `2a5ae3a23a51729d065cec716508eb26cb064106`:
[MySQL CONCAT](https://github.com/oceanbase/oceanbase-doc/blob/2a5ae3a23a51729d065cec716508eb26cb064106/en-US/700.reference/500.sql-reference/100.sql-syntax/200.common-tenant-of-mysql-mode/400.functions-of-mysql-mode/200.single-row-functions-of-mysql-mode/200.string-functions-of-mysql-mode/700.concat-of-mysql-mode.md),
[Oracle concatenation](https://github.com/oceanbase/oceanbase-doc/blob/2a5ae3a23a51729d065cec716508eb26cb064106/en-US/700.reference/500.sql-reference/100.sql-syntax/300.common-tenant-of-oracle-mode/400.operator-of-oracle-mode/400.concatenate-operators-of-oracle-mode.md).
These sources define generated SQL, not live acceptance.

Oracle treats zero-length strings as NULL; concatenating two NULL strings yields NULL, while a non-null operand
survives concatenation with NULL. Exact CLR empty-string result equivalence is not promised. Test nullable
projections and null-sensitive predicates explicitly: do not inherit a proven-wrong non-nullability assumption
from EF's generic Add processing. Oracle string Add is represented as a nullable, non-null-propagating
concatenation function before EF nullability processing, then rendered as `||`; this prevents incorrect operand-OR
null tests. Empty provider string constants/parameters become SQL NULL before that processing. Classify values
after normal value conversion, including enum/Guid/string converters, while preserving original caller values
for binding. Oracle query values using experimental null-converting string converters fail explicitly.
Any unsupported null-sensitive shape must fail clearly rather than silently folding a potentially-null database
result to a constant. Keep the Oracle Boolean condition conversion in place.

Oracle queries with provider-string SQL parameters conservatively disable command-SQL caching: EF's normal cache
key distinguishes null from non-null, while Oracle also needs to distinguish empty strings after conversion.
Model/query-compilation caching is unchanged. Repeated nonempty/empty/null executions must prove that command SQL
is regenerated correctly; no parameter dictionary or caller string is mutated. MySQL retains its existing caches.

Characterize existing string equality/inequality and Equals overloads through real EF SQL. Comparison uses
database collation semantics; StringComparison/culture overloads remain unsupported predicates. Preserve allowed
top-level client projections. Array/object/IEnumerable Concat overloads and the rest of QRY-06 remain separate
matrix work. Local tests cover constants/parameters/nullable columns, nesting, projection/predicate use,
opposite-mode contexts and sync/async intercepted commands; server collation, lengths, LOBs and result semantics
still require the postponed live phase.

## Unary string function local contract

The QRY-06 function slice adds exact parameterless `ToLower()`, `ToUpper()`, `Trim()`, `TrimStart()`
and `TrimEnd()` translations, plus the exact `string.Length` member. Use LOWER/UPPER and TRIM/LTRIM/RTRIM in
both modes; character length uses MySQL CHAR_LENGTH and Oracle LENGTH. The pinned official per-function pages
and EF10 translator-extension contracts are audited in `artifacts/local-development/query-functions-design.md`.

These operations use database casing, SQL blank-space trimming and database-character counts. They do not
promise CLR CurrentCulture/invariant casing, the CLR Unicode-whitespace trim set or UTF-16 code-unit length.
Supplementary characters, NLS/collation, padding and LOB behavior require the postponed live acceptance phase.
Oracle empty results collapse to NULL, including an all-space trim of a non-null string. Use conservative
nullable metadata without argument-null propagation, so EF cannot incorrectly replace a function result null
test with its input's null test. Preserve native empty/null handling around concat, CASE and COALESCE.

Match exact MethodInfo/MemberInfo, retain EF plugins and model DbFunction precedence through the standard
method/member translator providers, and keep mode/cache isolation. Invariant/culture overloads, trim-char/array,
search and substring are outside this slice and need negative predicate tests. Top-level client projection
rules remain unchanged. Custom value converters do not establish arbitrary CLR method equivalence; preserve
ordinary mapping/parameter behavior and test connected converter cases rather than stripping conversions.

## String search contract

The QRY-06 search slice covers exact instance Contains(string), StartsWith(string) and EndsWith(string).
Search uses database comparison rules and treats `%`, `_`, quotes and backslashes as ordinary characters;
use INSTR for contains/prefix and native suffix extraction/equality for ends-with, with no LIKE pattern
interpolation. MySQL suffix uses RIGHT(source, CHAR_LENGTH(needle)); Oracle uses SUBSTR(source, -LENGTH(needle)).
The pinned function documentation and padding/character limitations are in the string-function research.

An empty constant/captured needle is true for a non-null receiver. Every search returns a non-null Boolean:
a NULL receiver or provider needle returns false, and negating that search returns true, under both null-semantics
settings. This query policy does not promise CLR null-argument exceptions. MySQL tests CHAR_LENGTH(needle)=0 in SQL.
For Oracle, inspect a constant/captured needle's provider representation before generic empty-string
normalization: a known empty needle becomes receiver IS NOT NULL, while a known null needle produces false.
Reuse the existing conservative parameter-cache disabling, preserve raw parameter values and distinguish these
cases on each execution. A narrowly scoped internal search marker expanded before EF nullability processing
is acceptable if required; never change the semantics of user-mapped native INSTR/SUBSTR functions.

Oracle column/computed needles follow native storage semantics: an empty result already collapsed to NULL
cannot be distinguished from SQL NULL and produces false. Do not guess original CLR emptiness. Native function
nullability must remain conservative, including suffix extraction that may return NULL for non-null inputs.
Guard receiver/needle and bound the final result with CASE WHEN guarded-match THEN true ELSE false, or an
equivalent total expression. Ordinary nullable suffix equality alone is unsafe: EF compensation can match
two NULL operands. A NULL suffix from a too-long Oracle needle must not become a successful suffix match.
Test positive/negated/projected conditions, nullable receivers, CASE/coalesce/concat composition, long needles,
changing empty/null/nonempty captures and both null-semantics settings. Exact char/StringComparison/culture
overloads remain outside this slice and fail as predicates; allowed client projection evaluation remains.

## Substring contract

The QRY-06 Substring slice covers exact Substring(int) and Substring(int,int), using MySQL SUBSTRING or Oracle
SUBSTR. Starts are zero-based at the CLR boundary and adjusted by one with a long-result SQL Add and long
constant, or a safely folded long constant. int.MaxValue must produce positive 2147483648; do not overflow
Int32 first or introduce an unverified MySQL CAST AS BIGINT. Captured/column arguments retain their bindings
and converters. Overflow already present inside a caller's own integer expression is outside this adjustment.

This is native database-character/range behavior for constants, captures and columns alike, including negative
starts and zero/negative/excessive lengths. It does not promise CLR range/null exceptions, UTF-16 indexing or
grapheme preservation. Oracle empty scalar results are NULL; length-zero/beyond-end and LOB locator results
remain native server acceptance cases. Do not invent a deterministic NULL CASE for undocumented outcomes.

Retain receiver result-mapping metadata: fixed/national/LOB family, declared positive Size and configured
converter. This describes transport/conversion, not verified server padding or wire type. Native slicing acts
on stored provider text followed by the configured result conversion; arbitrary converter/model-space
equivalence is not promised. Do not infer result Size from the requested length or narrow a LOB to bounded
text. NCLOB translation is an explicitly unverified extension because the pinned SUBSTR input list omits it.
String-to-binary converter predicates remain unsupported. Conservative nullable function metadata preserves
result null tests even for required sources. Existing plugin/DbFunction/mode and Oracle parameter rules remain.

## Ordinary temporal-member local contract

The original QRY-07 slice covers DateTime.Year/Month/Day/Hour/Minute/Second, DateOnly.Year/Month/Day
and TimeOnly.Hour/Minute/Second. Match exact members; use MySQL YEAR/MONTH/DAYOFMONTH/HOUR/MINUTE/SECOND
and Oracle EXTRACT(unit FROM input), with fixed allowlisted units rendered through a narrow internal marker.
Inputs remain visited expressions with their existing carriers/converters/precision; no user text becomes SQL syntax.

Oracle DATE permits only calendar extraction directly. DateTime clock parts on DATE first cast to the normal
unzoned TIMESTAMP(7) query mapping; do not clone a DATE transport into another family or cast timestamps down
to DATE. TimeOnly retains its native day-to-second interval. SECOND extraction is decimal internally and
wrapped in one-argument TRUNC before producing an int. All exposed parts use converter-free int mappings,
independent of source DateOnly/TimeOnly/custom carriers. Conservative nullable/all-false propagation keeps
result NULL tests; nullable SQL results require nullable projections.

Accept only existing supported temporal families: DateTime DATETIME or unzoned DATE/TIMESTAMP, DateOnly DATE,
TimeOnly TIME/INTERVAL. Preserve built-in carriers and connected temporal converters, rejecting unrelated
string/binary/numeric/zoned replacements. Native parts operate on stored values, not arbitrary model-space
converter equivalence. Plugin/model precedence and mode-separated services stay intact.

`DateTime` and `DateOnly` additionally target `DayOfYear` and `DayOfWeek` through EF Core 10's
`IMemberTranslator`. MySQL uses `DAYOFYEAR` and converts `DAYOFWEEK` from 1–7 to .NET's Sunday-zero
enum. Oracle uses Gregorian `TRUNC(value, 'YYYY')` for the year start and ISO `TRUNC(value, 'IW')`
for Monday, avoiding territory-dependent weekday numbering. Subtract truncated days, then add one
for ordinal days or shift and wrap the weekday with `MOD`. Preserve NULL and use the CLR result's
mapping, independent of the input converter. Test leap day, year boundaries, enum materialization,
both null semantics, and rejection of non-temporal carriers. Native computation and return types
remain live acceptance gates.

DateTimeOffset, clocks/Now/UtcNow/Today, fractional parts/Ticks, Date/TimeOfDay/DayNumber, duration
parts and Add operations need their own timezone/range/precision/calendar/wrapping contracts. This slice makes
no server extraction, DATE cast, calendar/zero-date, interval-clock or result-metadata claim without live evidence.

## Numeric Abs, Sign and Truncate local contract

The implemented QRY-07 numeric slice covers twenty-two exact unary static signatures: Math.Abs and Math.Sign for
sbyte/short/int/long/float/double/decimal, MathF.Abs/Sign(float), decimal.Abs/Sign(decimal), and
Math.Truncate(double/decimal), MathF.Truncate(float), decimal.Truncate(decimal). Both modes use ABS/SIGN;
MySQL uses TRUNCATE(input, 0), Oracle one-argument TRUNC(input). Match exact method identities and retain
conservative nullable/all-false argument propagation. No general visitor or public option is introduced.

Validate the actual provider carrier against existing primitive numeric types, then reuse the owning strict
mapping source with that carrier and the complete store declaration as a validation probe. The probe must
also retain a numeric provider carrier; implicit text conversion is insufficient. Preserve the original input
mapping/converter and query-input precision. Numeric MySQL BOOL/BOOLEAN aliases remain valid when accepted
by the existing integer mapper; actual Boolean, text/binary/temporal and null-converting carriers reject.
ABS/Truncate retain the compatible operand result mapping; SIGN and MySQL's precision-zero argument use
fresh converter-free int mappings. Native functions act on stored values followed by result conversion;
arbitrary converter algebra is not promised.

Use native domain/overflow behavior uniformly for literals, captures and columns. Preserve the full existing
finite integer/decimal/float/double ranges, including signed minima and floating values beyond CLR decimal.
Do not add an artificial int32/decimal/2^53/BIGINT input limit or convert approximate inputs through decimal.
Projected out-of-range results retain checked reading; server predicates and overflow timing need live
evidence. Existing non-finite rejection remains; signed-zero bit preservation is not promised. Truncation
removes fractions toward zero, without casting or rounding the input. Floor/Ceiling have the local contract
below; Round/Random remain separate contracts. Pinned EF v10.0.0 and OceanBase documentation revision
2a5ae3a23a51729d065cec716508eb26cb064106 are audited in the numeric-core local research artifact.

## Numeric Floor and Ceiling local contract

The local numeric slice covers eight exact identities: Math.Floor(double/decimal), MathF.Floor(float),
decimal.Floor(decimal), and their Ceiling counterparts. Reuse the existing math translator, strict numeric
carrier/declaration validation, input converters and compatible result decoders. Oracle uses FLOOR/CEIL.
The pinned MySQL pages document a BIGINT result, so use t=TRUNCATE(x,0) and a CASE selecting t-1 for x<t
(Floor), or t+1 for x>t (Ceiling), otherwise t. Put adjustment only in the taken branch; preserve the full
existing finite input domain without a BIGINT/decimal detour, clipping or a small physical write CAST.

Precision zero and provider-carrier one require fresh converter-free mappings. Keep source mappings and
stored-value semantics; do not treat a model one under an offset converter as native one. Retain NULL,
independent function null tests and native output/error behavior. Stable expressions are the tested domain;
repeated SQL occurrences do not promise CLR single evaluation for volatile DbFunction compositions.

Real EF and intercepted reader tests verify both modes, exact identities, mapping/converter ownership,
changed captures, null settings, finite extremes/subnormals and decimal boundaries. Fresh one's carrier
unwraps Nullable<T> only to construct a correctly boxed nonnull constant; original converter metadata stays
intact. Focused46 unit/12 intercepted and full Debug/Release/isolated14 Release1339 pass. The same Floor/Ceiling
package passes both patches/modes; independent source review passes with no actionable findings. Native TRUNCATE/CASE
physical result types, full-range arithmetic and actual rounding/results still require server validation.
Round remains a separate contract; the following section defines the transcendental slice.

## Transcendental numeric local contract

All twelve exact Math/MathF Sqrt, Exp, Log (one/two arguments), Log10 and Pow identities translate through
the existing math service. Both modes use SQRT/EXP/LN/POWER. MySQL Log(x,base) uses LN(x)/LN(base), retaining
valid bases in (0,1); Oracle uses LOG(base,x). Oracle Log10 uses a fresh converter-free integer ten, avoiding
accidental binary promotion of NUMBER operands. Every input retains its independently validated numeric
mapping/converter. Primary x owns the result convention, including mixed-carrier two-argument functions.

Native functions remain nullable with all-false propagation, preserving domain-result null checks under
both null semantics. No clamp, finite input cap, manufactured NaN/infinity or uniform domain-error policy
is introduced. Stored-number semantics remain h(f(g(x))) for an arbitrary converter g/inverse h; SQL
predicates do not promise the value after inverse conversion or CLR materialization rounding.

Actual Single-provider results use a private shape-checked SQL marker to cast the completed native body to
FLOAT (MySQL) or BINARY_FLOAT (Oracle), uniformly including Oracle SQRT. Normalize after the entire MySQL
logarithm quotient, preserving native inner precision. Fixed targets avoid copying physical FLOAT facets
or unsigned declarations into CAST. Model type and physical storage width do not replace provider-carrier
selection. Double-provider results retain native precision; global CAST and user functions are unchanged.

Connector0.2.0 GetDecimal cannot decode boxed native Single/Double results. Transcendental decimal-provider
results therefore use an immutable opt-in decimal mapping clone with GetValue and checked numeric
conversion, retaining converter/facets/parameter behavior through clones. Ordinary decimal properties keep
GetDecimal. Binary-to-decimal materialization follows standard CLR rounding, tiny-value-to-zero and checked
nonfinite/range failures; OceanBaseDecimal retains checked exact decimal representability. No SQL DECIMAL/
NUMBER result cast is added. External custom decimal mapping decoders outside this owning opt-in seam
explicitly reject. Integral-provider results retain checked fractional/range rejection without SQL narrowing.

Local tests cover exact server predicates, order/HAVING, null/composition, independent converters, physical
width profiles and supplied-reader conversion. Native function metadata/calculation, quotient precision,
Single casts, domain NULL/warnings/errors, overflow/underflow and subnormal behavior remain live gates.

## Scalar numeric extrema local contract

Translate 26 exact Math/MathF/decimal Min/Max identities and numeric nullable/nonnullable
EF.Functions Least/Greatest. The relational SQL visitor owns these calls and retains Math nesting,
including when EF forwards a MathF or decimal call. A singleton returns its mapped operand; an empty
or null array cannot translate. Two or more values use native LEAST/GREATEST in argument order.

Only unmapped arguments inherit the first suitable mapping. Retain each mapped argument and let the first
argument own the result decoder. Use the existing result-only decimal reader and fixed Single result cast
where applicable. The stored comparison uses each operand's converter; SQL ordering may differ from model
ordering under an arbitrary converter. Oracle documents implicit conversion to the first argument's type.
Do not flatten nested calls, reorder arguments or force a common SQL cast. Native NULL behavior is retained;
SQL tests preserve null checks without asserting Oracle's live result, mixed numeric coercion or CLR NaN,
signed-zero and floating-point parity.

EF extracts fully evaluatable inline lists and captured arrays as constants. Numeric extrema snapshot
these arrays in the ordinary compiled-query cache key so a later execution can see changed values even
when their structural hash is unchanged. Row-dependent inline lists remain expression trees. SQL translation
expands the current constant values in order. Concurrent mutation during compilation and refresh within
an existing explicit compiled delegate are outside this contract. Runtime parameter array shapes remain
unsupported; the local checks cover SQL, cache keys, binding and supplied reader values.

## Exact decimal Round local contract

`Math.Round` and `decimal.Round` each have four decimal overloads. Omitted digits mean zero; an
omitted midpoint rule means `ToEven`. Accept constant digits from 0 through 28 and all five
`MidpointRounding` values. Dynamic arguments and invalid constants fail server translation;
floating point overloads have a separate contract. EF's normal top-level client evaluation still applies.

The exact decimal SQL uses TRUNCATE in MySQL mode and TRUNC in Oracle mode. It uses an exact decimal
quantum for the requested scale and compares twice the absolute remainder to the quantum. This avoids
scaling the original operand or requiring a 29-place CLR decimal literal. At a tie, `ToEven` adjusts
only an odd retained digit; `AwayFromZero` adjusts every tie. The directed rules adjust only for the
selected sign when a remainder exists, and `ToZero` returns the truncated value. A NULL operand remains
NULL. Syntax constants use independent mappings; results retain the decimal reader and inverse converter.
Translation requires a provider decimal mapping with a decimal carrier. A decimal-to-decimal converter
must leave NULL unchanged; rounding is then applied to the stored decimal.

The local contract covers translation, binding and supplied reader values. Native decimal operator
precision, return metadata, NULL and numeric boundary results still need both modes of OceanBase.

## Binary floating Round local contract

`Math.Round` and `double.Round` accept constant digits 0 through 15; `MathF.Round` and
`float.Round` accept 0 through 6. Each has four signatures, defaults to zero digits and
`ToEven`, and accepts all five constant rules. Runtime and invalid arguments fail server
translation. SQL operates on stored binary values; the result keeps its input's reader and
inverse converter. Translation requires a valid provider binary mapping with a matching carrier
and a converter that does not convert NULL.

Use binary powers of ten to scale and unscale positive digit counts. Large inputs bypass scaling
at the .NET thresholds (10^16 for Double, 10^8 for Single); scaled values with no fractional
bits bypass integral rounding at 2^52 and 2^23 respectively. Single uses fixed binary casts to
normalize the input, intermediate operations that round to Single and the final result.
The SQL does not rely on either compatibility mode's default midpoint rule.

Actual engine precision, cast and branch evaluation, subnormals, signed zero, NULL handling and
result metadata still require real-server tests. Multiple references to the same SQL operand do
not establish single evaluation for volatile values.

## Numeric aggregates local contract

The numeric aggregate service adds exact Sum/Average selector and scalar identities for int/long/float/
double/decimal and nullable forms, plus generic Min/Max for the eleven existing primitive numeric families.
Use a derived relational aggregate provider and AddTranslators, retaining plugin priority and unrelated
inherited paths. An invalid mapping in an owned numeric method must fail explicitly; returning null would
let the inherited default translator silently take over. Count/LongCount stay with their existing owner.

Build native SUM/AVG/MIN/MAX over the original mapped selector. Native AVG avoids per-row conversion:
model int/long Average has a fresh converter-free Double result, while other result mappings retain the
source converter. Stored-number semantics are h(aggregate(g(x))) for same-model results, not arbitrary
converter cancellation or a CLR Enumerable accumulation loop. Promoted integer Average instead returns
the native stored average through the independent Double reader, without an integer inverse converter.

Keep SUM's original model type/result mapping so EF injects COALESCE with converted model zero g(0).
Wrap completed Single-provider results outside that node with fixed FLOAT/BINARY_FLOAT normalization;
the null processor must retain the converter when generating the zero. Apply final provider-carrier policy
to all four aggregates, including physically wider Single mappings and cross-carrier numeric converters.
Promoted integer Average retains its independent Double result. Broaden only numeric model/body validation
on the existing internal marker; EF's generated COALESCE can carry Int32 from its syntax zero, so that
precise mapped SUM/coalesce shape is accepted without replacing the null processor. Ordinary CAST behavior
and nonnumeric/arbitrary mismatched-body rejection remain unchanged.

Reuse the immutable opt-in decimal result reader and existing checked integral/floating readers. A server
accumulator's width does not change CLR return type or impose a property's write capacity on an aggregate.
Do not add SQL decimal/integer narrowing, input clipping or a precision cast to force reader success.

Retain filter -> CASE -> DISTINCT -> aggregate, independent nullable metadata and inherited cardinality
shapers. Empty/all-null Sum returns model zero, including nullable Sum. Nullable Average/Min/Max retain
NULL; nonnullable empty results use EF's no-elements behavior. Empty GroupBy yields no group row. Tests
with supplied rows establish SQL/parameters/shaping/reading only. Native accumulation, physical metadata,
empty/group execution, CASE/COALESCE typing, casting and rounding remain real-server gates.

## Ordinary relational shapes and integral membership local contract

The local QRY-03/04 slice characterizes inherited inner/recognized left equijoins, composite numeric keys,
correlated Any/All, ordered nullable scalar FirstOrDefault, queryable scalar Contains and grouped/ungrouped
Count/LongCount. Keep ON and WHERE predicates distinct, nullable right projections, projected/root/negated
EXISTS and Oracle table-free FROM DUAL/Boolean conversion. Count selected nullable values still counts rows;
COUNT uses fresh int/long results, never SQL Server COUNT_BIG. Empty ungrouped counts and empty GroupBy have
different result shapes. Intercepted readers verify shaping, not database evaluation.

Bare inline/captured int/int? arrays/lists use EF10's scalar IN expansion and MultipleParameters default.
Repair only a reproduced element-mapping gap, reusing EF JSON reader/writer metadata without advertising
mapped JSON collections. Preserve the owning integer mapper's converters, checked readers and query ranges.
Retain EF padding/cache processing and current capture values across empty/nonempty/all-null/bucket changes.
Empty/null collection objects follow the inherited empty relational policy; nullable element/item compensation
and UseRelationalNulls differences are explicit. Do not mutate caller collections or invent server list limits.

The complete relational compiled-query cache key must also distinguish the current context's collection
parameter translation mode. Preserve the full base key (model/query/async/null/splitting/buffering), adding
only this missing option in a scoped derived generator. A provider service graph can still be shared by
contexts with different collection modes; changing service-provider identity is not the cache repair.
Test the same expression in default/Constant/default and Constant/default/Constant order. Oracle scalar
string normalization must exclude collection envelopes, especially null collections; EF owns their expansion.

EF.Constant/context Constant use mapping-owned literals. Context Parameter mode can use an inherited Contains
fallback to scalar IN; explicit EF.Parameter(collection) remains unsupported without a primitive-collection
translator. Test both boundaries. Text lists need provider-converted Oracle empty/NULL normalization before
membership compensation; general VALUES roots need their own integer-ordinal and dialect rendering fixes.
Those and APPLY/collection projections remain separate locally feasible work. Numeric aggregate promotion/
result normalization has the contract above; nonnumeric aggregate extrema are implemented in Batch3ad, while
native aggregate execution and server ordering remain open.
The shipped EF10.0.0 package owners and source-derived gaps are audited in the local query-shapes research.

The local slice passes 30 focused unit and 40 intercepted cases. Full Debug/Release and isolated EF10.0.14
Release pass 1155 tests, including two deterministic model-eviction cases. Positive model-reference reuse
uses a test-owned retained cache: EF's shared bounded cache does not guarantee retention. A controlled
capacity experiment reproduced identity failure with equal keys/shared services; the initial natural failure
timing was not recaptured. Production caching was not changed to force retention. Independent production
review and scoped fixture re-review passed. This is local translation/binding/shaping evidence only.

## Scalar text collection membership local contract

The local QRY-03 slice covers bare inline/captured Enumerable.Contains and List.Contains over scalar text
arrays/lists in both modes, including existing provider-text converters and families. Add the existing EF
string JSON reader/writer metadata as a collection-mapping prerequisite; this does not enable JSON tables.
MySQL retains base scalar expansion. Oracle expands provider-text IN collection parameters before scalar
string normalization and base null compensation, so provider-converted empty elements become SQL NULL.

Keep raw model values and complete element mappings on each scalar expression. Empty model text converted
to nonempty stored text survives; nonempty model values converted to empty stored text become SQL NULL.
Never normalize before conversion or bind already-converted values through the converter again. Reuse the
existing scalar classification, including enum-underlying handling and null-converting converter rejection.
Classification and normal binding can each evaluate a pure converter; exactly-once invocation is not promised.

Use execution-local snapshots/name/mapping caches and fresh scalar parameter entries without mutating caller
collections or their original dictionary entries. Reuse keys include source identity, element mapping/mode
and index; one capture under different converters must retain independent bindings. Preserve MultipleParameters
padding from raw values, safe mapped Constant literals and command SQL cache disabling. EF still owns NULL
compensation, NOT/CASE/Boolean projections and relational-null differences after normalization.

Characterize empty/null/all-null/duplicate/converted-empty collections, changed captures, bucket boundaries,
name collisions, hostile literal data, fixed/national/LOB bindings and both modes/null settings. Native
collation/padding/LOB equality/charset/server limits remain live gates. General VALUES roots and explicit
EF.Parameter(collection) remain separate locally feasible work, with clear unsupported-shape tests.

The independently reviewed slice passes 25 focused unit and 20 intercepted cases. Full Debug/Release and isolated
EF10.0.14 Release pass 1200 tests. Real EF array/list queries cover both modes/null settings and changed
captures; a separate artificial SQL-tree probe verifies one snapshot enumeration, mapping identity and name
collisions. Raw padding covers boundaries through 2001 without copying EF's private filtered-index counters.
Constant literals retain raw model values and mappings. The same baseline-built text-membership package
passes both patches/modes, including Oracle all-provider-null predicates with no surviving equality/IN arm.
These are translation/binding/shaping checks; live text membership and general collection roots remain open.

## Captured integral collection roots local contract

After bare membership, the bounded collection slice covers general captured int/int? array/list roots
and nonempty inline roots, including native int and existing int-to-long converters. Filter/project/order,
composed membership, joins and Count/Any must retain duplicates, nullable elements, ordinal semantics and
current values. General roots contain exactly one row per input element; membership padding does not apply.
Converted roots require consistent EF-inferred element mapping. A root Value used against both native and
converted columns, or projected directly by a Join that also infers an int-to-long comparison mapping, can
be rejected by EF's earlier type-mapping postprocessor. Preserve that explicit conflict rather than override
inference in the runtime visitor. Native joins and consistently inferred converted membership remain in scope;
broader converted-root projection support needs a separately evidenced local design, not database access.

Expand ValuesParameter before the base nullability processor's IntTypeMapping cast, using execution-local
snapshots/mapping/name reuse and raw model values. Preserve caller collections, disable SQL caching and honor
the actual remaining Value or _ord/Value column layout after EF pruning. Captured empty/null roots carry
immutable typed NULL metadata and render a typed zero-row relation, never one fabricated NULL row.
The null rule concerns an already translated runtime parameter; ordinary LINQ null-source argument
validation can fail before translation and keeps its own explicit boundary.

The generator retains EF wrapping/aliasing but renders SELECT/UNION ALL SELECT, with quoted first-arm aliases
and Oracle FROM DUAL in every arm. Keep UNION ALL and outer EF ordering. Synthetic integer typing uses legal,
lossless targets: MySQL SIGNED for the selected signed carriers and Oracle unqualified NUMBER. Keep physical
column write precision out of query typing; retain original element mappings/converters/checked readers.
Do not broaden unrelated explicit CAST behavior or replace the provider's checked int mapping with EF's class.

Default/MultipleParameters and Constant retain their parameter/literal policies; composed Parameter roots
and explicit EF.Parameter remain unsupported without a separate translator. Originally empty inline roots
use compile-time metadata beyond this runtime slice; see the implemented
[inline-empty integral contract](#originally-empty-inline-integral-roots-local-contract).
General text/other carrier roots still need their own typing contracts and remain local follow-up work.
Native UNION coercion/results/order and server limits require later database validation.

The slice passes 40 unit and 16 intercepted cases; full Debug/Release and isolated EF10.0.14 Release pass
1256 tests. Independent source review and the subsequent nested-CAST test re-review pass. The test-only
extension ran the two amended cases in all three configurations, with no production edit or repack.
The same integral-roots package passes both patches/modes. An AsQueryable capture can inline its values;
correlated Enumerable.Where reaches the parameter root and reproduces the original IntTypeMapping cast.
Recorded SQL trees distinguish those cases. Supplied reader rows do not establish actual SQL results.

## Originally empty inline integral roots local contract

The local collection slice covers originally empty inline int/int? Count/Any/Select/join and nested
subqueries. Preserve the original element type before projection/pruning can discard Value; never infer it
from Count's int result or a first row that does not exist. Retain contextual inference and conflict rejection.
Use ordinary int mapping only without contextual mapping. Other empty families remain explicit boundaries.

Derived relational queryable visitor and postprocessor factories address the demonstrated metadata gap.
Delegate inherited translation and postprocessing; preserve the derived
visitor in subqueries using its copy constructor. Track empty-root metadata per compilation, retain it
through mapping/projection, and reattach it after base pruning before alias rewriting. Do not copy the whole
Process pipeline or add global mutable state. Reuse immutable typed NULL prototypes and legal integer
targets for genuinely zero-row bodies; normal outer DefaultIfEmpty/left-join behavior stays with EF.

The EF10.0.0 public-query reproduction retaining only an empty root's ordinal exposes an inherited pruning
defect: the body declares Value while ORDER BY still references _ord. Correct only these annotated empty
inline roots after base pruning, using original table metadata and actual surviving column references.
Restore the retained original column identity before attaching prototypes; do not replace general pruning,
rewrite nonempty row cells or guess the element type from a column name. This requires local regression
coverage in both modes, independently of later server validation.

Real EF Count-only/nested/alias/ordinal cases and intercepted sync/async readers verify the compilation
and shaping seams. Focused57 unit/eight intercepted and full Debug/Release/isolated14 Release1281 pass.
The same inline-empty package passes both patches/modes; independent source review passes with no actionable findings. A narrowly
suppressed SetTables internal-API call retains early type metadata; selected patch checks do not establish
future patch compatibility or precompiled annotation quoting. Native execution and general text/other
families remain separate gates; typed zero-row SQL and supplied readers do not prove actual server results.

## Arithmetic division and remainder local contract

The QRY-01 arithmetic slice renders integer division using MySQL `DIV` and Oracle one-argument
`TRUNC(left / right)`, preserving truncation toward zero. Oracle remainder uses `MOD(left, right)`;
MySQL retains `%`. Decimal, Single and Double division retains `/`. Integer classification follows the
translated expression's CLR numeric type, including nullable types and integral promotions, rather than
the Oracle NUMBER store name, which is also used for decimal values. Casts to fractional CLR types must
retain fractional division. Converters are not a general arithmetic transformation: characterize ordinary
value-preserving numeric conversion, without promising arbitrary scaled/text converter arithmetic.

Sources are EF Core v10.0.0 SqlExpressionFactory, SqlBinaryExpression and QuerySqlGenerator, plus OceanBase
documentation revision `2a5ae3a23a51729d065cec716508eb26cb064106`: the per-mode arithmetic-operator pages,
Oracle MOD and numeric TRUNC. Their source audit is recorded in the local query research artifact.
Oracle MOD's negative example and stated dividend-sign rule govern this contract; the page's conflicting
FLOOR formula is not copied into provider code. Server execution remains unverified.

Tests must use mapped columns, since constant-only expressions can be evaluated by EF before translation.
Cover all eight integer CLR types through their actual promoted expression types, signed negative operands,
nullable values, decimal/Single/Double controls, casts, parameters, predicates, projections and nested
division/remainder with explicit grouping. Opposite-mode contexts must retain separate dialects. Sync/async
intercepted materialization proves client generation, binding and reading only, not arithmetic results.

This slice does not promise CLR checked overflow, minimum-integer divided by -1, zero-divisor exceptions,
unsigned subtraction, floating special values or bitwise width/sign equivalence. These require their own
contract and tests; live arithmetic result/predicate tests remain open for both modes.

## Integer bitwise local contract

QRY-01 includes integer AND/OR/XOR/complement in predicates and projections. Reuse the EF10 binary/
unary nodes and inherited null processing, including lifted nullable integer operations. Classification
uses the translated numeric CLR type (or enum underlying type), never NUMBER/BIGINT names. Numeric
integer converters retain stored-space semantics, not arbitrary converter equivalence; text, floating,
decimal and null-converting carriers are rejected. Boolean AND/OR/NOT retain their existing paths.

MySQL's documented numeric bitwise result is unsigned64. Signed results need `CAST(... AS SIGNED)`;
unsigned complement below64 bits needs its CLR-width mask. Group every operand/result, preserving
nested arithmetic and predicates. Preserve small binary operand promotions before EF removes their
conversions and infers the wrong narrow result mapping. Integer-typed MySQL CAST nodes now use legal
SIGNED/UNSIGNED targets, including the widening casts required by these operators. Ordinary explicit
casts outside bitwise do not inherit its integer-carrier rejection. Collection renderer tests retain
both nested casts and all original type/mapping identities, with the corrected MySQL spelling.
Oracle AND uses BITAND, OR uses `a+b-BITAND(a,b)`, XOR uses
`a+b-2*BITAND(a,b)`, signed complement uses `-1-a`, and unsigned complement uses `maximum-a`.
NUMBER arithmetic keeps the OR/XOR intermediates exact, including signed64 endpoints. UInt64 AND
splits into high/low32-bit halves with TRUNC/MOD, reconstructing the result with4294967296. This keeps
every BITAND argument in0..4294967295 even when an operand exceeds Int64.MaxValue.

Sources: EF Core v10.0.0 RelationalSqlTranslatingExpressionVisitor, SqlExpressionFactory,
QuerySqlGenerator; OceanBase documentation revision `2a5ae3a23a51729d065cec716508eb26cb064106`
([MySQL bitwise operators](https://github.com/oceanbase/oceanbase-doc/blob/2a5ae3a23a51729d065cec716508eb26cb064106/en-US/700.reference/500.sql-reference/100.sql-syntax/200.common-tenant-of-mysql-mode/200.operator-of-mysql-mode/400.bitwise-operators-of-mysql-mode.md),
[Oracle BITAND](https://github.com/oceanbase/oceanbase-doc/blob/2a5ae3a23a51729d065cec716508eb26cb064106/en-US/700.reference/500.sql-reference/100.sql-syntax/300.common-tenant-of-oracle-mode/500.functions-of-oracle-mode/200.single-row-functions-of-oracle-mode/100.numeric-functions-of-oracle-mode/600.bitand-of-oracle-mode.md));
public server source revision `5e8a63d29dae9fde5e962b8fcc7b167d438fc343`,
`src/sql/engine/expr/ob_expr_operator.cpp` (`get_int64_from_number_type` and Oracle BITAND evaluation).
That source audit is a range precaution, not live evidence for a deployed server version or a connector bug.

Acceptance: BitwiseQueryTests pins all four operators, integer promotions/widths, enum and numeric
converters, negative and boundary operands, nested grouping, captured parameters, nullable compensation,
and Boolean controls. Intercepted readers and packaged consumers verify binding/materialization only.
Shifts remain separate local development. Oracle OR/XOR and UInt64 splitting repeat operands; volatile
single evaluation is not promised. Actual server results/coercion, physical BITAND limits and runtime
overflow remain live gates. Arbitrary converter transformations are not CLR bitwise equivalence.
Overflowing/narrowing and signedness-changing explicit casts have separate CLR wrapping/range semantics;
this slice's canonical MySQL CAST syntax does not establish those broader conversion contracts.

## Boolean XOR local contract

QRY-01 includes bool/bool? exclusive OR in both modes. Retain EF's ExclusiveOr binary node through
null processing; an early rewrite to NotEqual would compensate nullable inequality and lose lifted
NULL propagation. Independently default-map the operands and assign the native Boolean result
mapping, so a Y/N or 0/2 operand converter never becomes the result converter. The result lookup is
model-free: a global DefaultTypeMapping<bool> converter must not override the native comparison result.

At SQL generation time normalize each operand with its own mapped true/false literals:
`CASE WHEN operand = trueLiteral THEN 1 WHEN operand = falseLiteral THEN 0 ELSE NULL END`.
Compare the two normalized values with <>. This supports mixed numeric/text Boolean storage and
inverted true/false encodings. Oracle treats Boolean ExclusiveOr as a search condition; existing
three-valued CASE handling converts it to a scalar in projections, ordering and enclosing operands.
The normalization/comparison is emitted after EF null processing, so surrounding equality/inequality
still uses the configured null semantics while XOR itself propagates NULL in both settings.
An XOR IS NULL predicate may be simplified by EF to operand null tests without evaluating XOR.

Reject null-converting and nonnumeric/nontext carriers, equal/null true/false representations and
Oracle empty-string representations. Data must use the converter's valid encodings; equality of
text representations also depends on the configured native collation. The following converted
Boolean contract extends comparisons and logical operations beyond XOR. Normalization repeats
operands and Oracle scalar conversion repeats the entire predicate;
single evaluation of volatile expressions is not promised. SQL generation, intercepted parameter/
reader tests and packaged consumers are local evidence, not native result/coercion/collation proof.

## Converted Boolean comparison and logic contract

QRY-01 extends ==/!=, NOT, AND/OR (both conditional and lifted &/|), Boolean predicate/order roots,
conditional tests/Boolean arms and Boolean coalesce to independently mapped native/numeric/text
operands. Normalize noncanonical operands to native 0/1/NULL before applying the operator. A numeric
1/0 or native Boolean mapping is already canonical; keep its simple SQL. Plain converted property
projections retain their original converter. Canonical classification compares integer/decimal values
exactly, without narrowing them to Double; a true encoding of1.0000000000000000000000000001m is not1.
No global rewrite of stored properties is performed.

The relational visitor independently default-maps scalar operands and uses a strict internal
single-argument Boolean value marker only when normalization is required. The SQL generator renders
the same CASE used by XOR, with each operand's mapped true/false literals; no invented SQL function
reaches the server. The marker has nullable=true with argument null propagation. Thus EF retains
its normal compensation for ==/!= and can simplify normalized IS NULL to operand IS NULL, while
SQL three-valued AND/OR preserves lifted Boolean behavior (false AND NULL=false, true OR NULL=true).
The configured relational-null setting still governs surrounding equality, separately from logic.

New logical/comparison results use the provider-native Boolean mapping, excluding model-wide default
converters. Recognize native SQL predicate shapes (comparisons, logical operators, null tests, LIKE,
EXISTS and IN) before normalization; a model Y/N default does not make their SQL result text. Scalar
property/constant/parameter mappings remain converter-owned. Oracle predicate-to-scalar CASE constants also use the native mapping, including predicates
rebuilt by EF's parameter/null processing. Captured operands retain their independently inferred
mapping: ordinary captures bind native Boolean/Decimal, while a model default Y/N capture binds text
and is normalized before use. Ternary Boolean arms and coalesce normalize independently to the same
native result carrier. Reuse existing EF services; add no public API or dependency.

Acceptance includes BooleanQueryTests, intercepted sync/async readers with independent supplied
true/false/null results, current captured values/global defaults and the packed consumer. Rejected
encodings and valid-data/collation/coercion/volatile-evaluation boundaries are the same as XOR.
Broader global Boolean defaults on unrelated translator families and additional Boolean method
overloads remain local audit work; this bounded operator contract does not advertise all Boolean APIs.

## Boolean equality methods and nullable defaults contract

QRY-01/QRY-11 extends exact framework bool.Equals(bool/object), Nullable<bool>.Equals(object),
object.Equals(object, object) with statically Boolean operands and Nullable.Equals<bool> to the
same independently normalized equality and configured null semantics as ==. Strip only built-in
boxing conversions; Boolean-versus-null operands use typed nullable Boolean NULL. Genuinely dynamic
object comparisons and other types retain inherited EF inference. Custom identities do not gain
Boolean normalization: EF's inherited name-based Equals translator can still translate a custom
same-name method, without inspecting its body; that is not a supported arbitrary-method contract.
Other unrecognized methods reject predicates and can use EF top-level client projection.
Both Nullable<bool>.GetValueOrDefault overloads normalize receiver and
fallback independently and return native Boolean COALESCE; the no-argument default is native false.
Plain .Value projections retain storage converters; .HasValue tests nullable storage directly.
Keep its null test as binary != typed NULL until EF's converter-compensation pass completes;
the inherited unary IS NOT NULL path otherwise truth-converts its operand and can erase the filter.

Acceptance covers both modes/null settings, captured true/false/null transitions, global Boolean
defaults, intercepted sync/async native readers and the same packed provider on selected EF10 patches.
Supplied reader values do not prove server evaluation. Existing valid-encoding, coercion/collation and
repeated volatile evaluation boundaries apply. CompareTo/string/parsing/conversion APIs remain local
follow-up work; no connector defect or native database acceptance is established by these tests.

## Boolean ordering and string representation contract

QRY-01/QRY-11 adds exact bool.CompareTo(bool/object) on statically Boolean arguments. CLR Boolean
ordering is false<true regardless of converter storage order. A CASE over independently normalized
native 0/1 values returns signed Int32 -1/0/1 without MySQL unsigned subtraction. Leading null guards
preserve SQL NULL propagation from nullable.Value receivers/typed arguments before any compensated
equality branch. Object-null (including a boxed nullable field/capture) compares below any non-null Boolean; other
object types and genuinely dynamic objects are unsupported in predicates and retain client projection.
Other method identities retain inherited EF behavior, including its name-based ComparisonTranslator.

Exact bool.ToString(), bool.ToString(IFormatProvider), Nullable<bool>.ToString() and object.ToString()
on statically Boolean boxed values return native strings True/False using normalized truth. The
framework ignores IFormatProvider; the translation does too. A nullable receiver produces empty for
NULL in MySQL; Oracle's empty-string-is-NULL rule produces NULL, as elsewhere in this provider. A
nullable.Value receiver keeps SQL NULL propagation rather than emulating CLR exceptions. Results use
model-free Int32/string mappings, retaining plain property converters. EF10's inherited comparison
translator compares storage directly; it does not provide these independently normalized semantics.

Reference contracts: [Boolean.CompareTo](https://learn.microsoft.com/dotnet/api/system.boolean.compareto),
[Boolean.ToString](https://learn.microsoft.com/dotnet/api/system.boolean.tostring), and
[Nullable.ToString](https://learn.microsoft.com/dotnet/api/system.nullable-1.tostring). Tests cover both
modes/null settings, current captures, global defaults, supplied native readers and packed consumers.
They do not prove native comparisons, collation/coercion, string lengths or volatile single evaluation.
Parse/TryParse/Convert families remain separate local work. No new SQL function or dependency is added.

## Integer shift local contract

QRY-01 extends integer expressions with `<<` and `>>`. C# first promotes small integers to Int32;
Int32/UInt32 use count&31 and Int64/UInt64 use count&63, including negative/oversized counts. Preserve
the original operand conversions and map the Int32 count independently of the left expression/result.
Nullable operands/counts propagate NULL. Numeric integer converters retain stored-space semantics;
text/floating/decimal/null-converting carriers are not supported for these operations.

EF10 SqlBinaryExpression excludes shift nodes. The relational visitor constructs strict internal
two-argument built-in function markers with the left type/mapping and true/true null propagation;
the SQL generator renders them without sending a fictitious function to the server.

MySQL shifts use native unsigned64 operators after count masking. Left32-bit results are masked to
4294967295; signed32 results are rotated with XOR2147483648 then signed subtraction2147483648.
Signed64 uses CAST AS SIGNED. Signed right shift uses CASE: negative values become
`CAST(~((~value) >> maskedCount) AS SIGNED)`, nonnegative values use the native shift and signed cast.
Unsigned right shift uses the native logical shift directly. Do not use floating division for Int64.

Oracle right shift uses `FLOOR(value / POWER(2, maskedCount))`, including negative signed values.
For left shift, b=2^(width-count), u=MOD(MOD(value,b)+b,b)*2^count. Discard high bits before multiplication,
so u stays below2^width and normalization intermediates stay below2^65. Signed results use
`MOD(u+2^(width-1),2^width)-2^(width-1)`. NUMBER literal arguments keep POWER on the numeric path.
The public server source at5e8a63d29dae9fde5e962b8fcc7b167d438fc343,
`src/sql/engine/expr/ob_expr_power.cpp`, selects ObNumber.power for Oracle NUMBER input rather than
the MySQL double POW path. Its `deps/oblib/src/lib/number/ob_number_v2.cpp` integer-exponent path
uses integer power by repeated squaring, preserving these small integer powers at NUMBER precision.
This audit supports the translation design, not a deployed-server pass.
The pinned MySQL bitwise operator documentation in the preceding section defines unsigned/logical
native shifts and zero output for counts>=64; masking is therefore required for C# semantics.

Acceptance: ShiftQueryTests pins mode/result width, sign extension/truncation/count masks, promotions,
literal boundaries, nested queries, current captured values, NULL compensation and invalid storage.
Intercepted command tests cover mapped/nullable signed/unsigned reader results and count transport;
packaged consumers exercise the same translation seam. Native POWER/FLOOR/MOD precision, actual shift
results and repeated volatile operand evaluation remain live/semantic boundaries. Arbitrary converter
equivalence and overflowing explicit numeric casts remain separate contracts.
C# expression trees cannot represent `>>>`: a .NET10 compiler probe fails with CS7053 before EF is
invoked. This is a language-expression-tree boundary, not a connector/server defect; no fictitious
LINQ translation or new public function is advertised for that operator.

## Tracked writes

A modification batch contains one row operation. This permits command-level affected-row checks and keeps the
provider independent of the connector's multi-statement and multi-send options. The provider's EF10-based batch
executor controls transactions across multiple row operations.

Writes use `IRelationalCommand` and `RelationalCommandParameterObject` with `CommandSource.SaveChanges`. This
retains EF command diagnostics, interceptors, configured timeout and transaction attachment. Input parameters
carry the type mapping and current or original value selected by EF's column modification metadata.

Before executing DML, the batch validates the current value of each `IsWrite` column whose mapping implements
the internal write-validation contract. Integer precision/native-width, decimal precision/scale/unsigned,
floating width/unsigned, temporal precision/
interval leading width, binary column-length and text character-length/Unicode checks run after
value conversion and fail as `DbUpdateException` with the original validation error. This check applies to actual
writes; original key/concurrency values used only in predicates must not inherit a physical write bound.
General parameter/literal generation also serves queries, where an arithmetic operand, CASE branch or comparand
can exceed a column's capacity. Query parameter binding retains transport checks such as Oracle empty binary input.
Temporal inputs/literals use lossless transport precision and Oracle interval leading width, while output-only
parameters retain declared column metadata. Oracle DateTime DATE comparands use timestamp transport;
DateOnly retains midnight DATE semantics. See the [temporal contract](efcore-10-technical-baseline.md#temporal-mapping-contract).
Decimal input and InputOutput parameters leave precision/scale unset for connector inference; output-only decimal
parameters retain physical facets. Converted current writes reject coefficient overflow and any nonzero remainder
at the declared scale before execution. Intercepted decimal tests cover fractional and large query comparands,
CASE operands, nullable reads, converted writes and failure before command interception in both modes sync/async.
National text tests exercise native Oracle NChar/NVarchar2 input binding, larger query operands and enum-to-string/
Guid converters through real EF query/materialization/write paths. Ordinary MySQL/Oracle text retains generic
binding. Output-only declared Size remains separate from the actual input transport length; live character-set
capacity and server text/LOB behavior remain unverified.

| Mode | Generated-value path | Transaction requirement |
| --- | --- | --- |
| MySQL | Execute DML, then select generated columns by the mapped primary key on the same connection. A generated insert identity uses `LAST_INSERT_ID()`. | The batch requires a transaction whenever a follow-up read is needed. |
| Oracle | Execute DML with `RETURNING <columns> INTO :ob_out_0, ...` and typed output parameters. | Returning outputs are part of the single DML statement. |

MySQL readback requires a mapped key with at most one generated identity column; unsupported shapes fail while
building the batch, before executing DML. The currently supported generated identity types are int/long (or
converters with those provider types), without configured default values, default SQL or computed expressions.
Server-generated string keys and nonidentity numeric defaults are rejected; they cannot use LAST_INSERT_ID.
Default-only inserts use `INSERT INTO ... () VALUES ()` in MySQL and
`INSERT INTO ... (key) VALUES (DEFAULT)` in Oracle. Schema creation must establish the appropriate identity or
default mechanism; these commands do not create it.

Oracle output values are converted to the mapping's provider CLR type before applying a configured converter and
assigning through `IColumnModification.Value`, which publishes store-generated values through EF's update entry.
MySQL readback uses `IReadOnlyModificationCommand.PropagateResults`.

The local generated-readback boundary also rejects a MySQL composite key with more than one generated identity
column before DML. `LAST_INSERT_ID()` can identify only one generated identity in this provider slice; the
rejection is covered sync/async through BasicRelationalTests and does not imply a general server limitation.
A future multiple-identity strategy would require its own generation contract and tests.

GeneratedValuesTests covers composite-key inserts with exactly one identity, multi-column defaults/computed
outputs and updates. Oracle assigns synthetic values according to each RETURNING column's INTO bind name;
MySQL supplies a reader in the projected column order. Independent assertions pin the generated-column set and
input values in SQL placeholder order. On-add defaults are returned only when EF selects the store default;
explicit client values are written and retained, and ordinary updates do not refresh on-add-only defaults.

Nullable generated fields, including converted enum concurrency outputs, accept DBNull. A later update uses
the previous NULL token as IS NULL or binds its original non-NULL provider value, then refreshes the original
snapshot on success. Required NULLs and fractional integer-converted outputs fail as DbUpdateException with
the affected entity. EF discards partially published generated values on failure, retaining temporary insert
keys and pending update/original state. A zero affected-row result prevents readback/propagation; a missing
MySQL generated row raises DbUpdateConcurrencyException. These intercepted tests disable automatic
transactions and prove client state/SQL only, not execution of expressions, physical storage or atomicity.

### Offline create scripts (SCH-02 / MOD-03)

Database.GenerateCreateScript reads the EF10 design-time relational model without opening a connection.
OceanBaseSchemaSqlGenerator emits ordinary tables/scalar columns, nullability, primary/alternate keys,
nonunique/unique and descending indexes, checks, and foreign keys. Tables precede ALTER TABLE foreign keys,
including cyclic/owned/join graphs. Excluded tables and view-only mappings are not created. Referenced excluded
tables must already exist when the script is applied. Existing schemas/users must also exist; scripts do not
create an Oracle tenant, user or schema or a MySQL database.

Reuse resolved store types and provider default literals (including national text and converters), with MySQL
default expressions parenthesized. Generated int/long primary-key columns without configured default/computed
expressions become AUTO_INCREMENT (MySQL, leading primary-key column) or GENERATED BY DEFAULT AS IDENTITY
(Oracle). Each table has at most one identity; Guid keys remain client-generated. Explicit default SQL and
computed SQL are trusted model expressions and must use the target dialect/identifier case. MySQL computed
columns use the mapped type with STORED/VIRTUAL; Oracle virtual columns infer their type.

Reject unsupported shapes before returning a script: unspecified defaults/computed expressions, other inferred
generation, model sequences, HasData inserts, database collations, comments/triggers, filtered indexes,
ON DELETE SET DEFAULT, TPT/TPC inheritance, entity splitting, Oracle column collations and Oracle stored computed
columns. Table-specific identity propagation for TPT/split mappings needs a separate generation contract; ordinary
property-global ValueGenerated is insufficient. These are provider script
limitations, not blanket server limitations. Future support needs its own contract/tests. Explicit schema/table/
column/constraint/index names are escaped through the provider helper; target identifier limits, identity/default/
computed syntax versions, DDL implicit commits and type/index capacity still require live acceptance.

SchemaScriptTests covers both-mode scripts and explicit exclusions, including one-to-one, many-to-many, owned
collections/references and cyclic foreign keys at the DDL level only. It does not claim those families' complete
query/write support. The packed consumer generates a small identity/default/index script offline.
Offline script generation does not authorize deletion or establish SCH-03 completion.

### Existing-schema table creation (SCH-02 / CFG-04)

EnableOceanBaseSchemaManagement(databaseOrSchema) is an explicit per-context table-creation opt-in, preserved
through typed/untyped option clones without partitioning singleton services or model caches. Provision the MySQL
database or Oracle user/schema externally, with privileges to inspect/create the supported model objects.
The option neither grants privileges nor creates an Oracle tenant, user or database.

Before opening, require opt-in; reject current EF, enlisted or ambient transactions and
validate the full design-time DDL plan. EnsureCreated validates matching seeding delegates separately as described
below; direct CreateTables does not invoke them. Created tables and their FK principal tables must have no explicit schema
or the exact declared scope, including excluded principals. Explicit SQL default/computed/check expressions are
trusted application SQL, so the scope option is not a sandbox for arbitrary model SQL or interceptors.

Hold one EF connection lease across SELECT DATABASE() (MySQL) or SELECT SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA')
FROM DUAL (Oracle), table detection and individual DDL commands. The actual scope must equal the declaration
ordinally, including case. EnsureCreated returns false when any ordinary table exists; it does not reconcile a
partially matching schema. Explicit IRelationalDatabaseCreator.CreateTables skips the emptiness check. An empty
create plan returns false without opening for DDL; configured EnsureCreated seeding still opens a transaction and
checks the actual scope. A missing namespace fails to open rather than being created implicitly.

Async operations check pre-cancellation and pass the caller token through opening, probes and each DDL command.
The final lease close has no cancellation token. Preserve the original DDL/cancellation exception if final close
also fails, recording the latter in Exception.Data["OceanBase.SchemaCleanupError"]; cleanup after a successful
operation still reports its own failure. Physical cleanup can fail and must not be inferred from a balanced EF
lease alone. Caller/outer connection ownership follows EF's connection accounting.

DDL may implicitly commit and the plan is not atomic. No automatic retry, DROP rollback, destructive cleanup or
schema upgrade occurs; failure can leave partially created objects requiring explicit manual cleanup. Concurrent
creation is not serialized. Create/Delete/EnsureDeleted and async variants are unsupported under this
table-only option; the following section defines explicit MySQL namespace ownership. SchemaLifecycleTests
and package consumers intercept all commands: they establish
client SQL/control flow, not real DDL, physical session continuity, server permissions or cleanup.

### Explicit MySQL database ownership (SCH-01/02/03)

EnableOceanBaseDatabaseManagement(databaseName) declares ownership of an entire MySQL database,
including unrelated-to-model objects. It also enables the existing table-creation profile; switching
back to EnableOceanBaseSchemaManagement removes namespace permission. This opt-in is per-context,
cloned with relational options, omitted from diagnostic fragments and not a singleton/model-cache key.
Reject known system databases (mysql, oceanbase, sys, information_schema, performance_schema).
Oracle user/schema creation/deletion needs its own management/authentication contract and remains
unimplemented by this slice; this is not evidence of a connector or deployed-server defect.

Without an explicit management factory, before any management open, require exact ordinal agreement between the connection's configured
database and declared name, a configured connection string, a closed target native connection and no
current/enlisted/ambient transaction. Supplied/custom native connections are outside this initial
namespace profile, because their credential/provider configuration cannot be cloned through the
currently consumed public API. The explicit factory profile below supplies these independently.
Verify provider-created native connection identity and canonical configuration against EF's active
connection string (including credentials and the enforced mode), and reject native routing, credential
or TLS hooks before management opens. Runtime SetDbConnection is a supplied connection; EF's
SetConnectionString remains supported when the actual native configuration agrees. A native string
with hidden credentials cannot establish that agreement and is rejected; no private state is copied.
EnsureCreated validates the entire table plan before creating a namespace. No tenant is created.

Use an owned, independent OceanBaseRelationalConnection constructed from EF10 injected dependency
records with cloned options and Database empty. Retain mode, interceptors and runtime command timeout.
Use a bound name to query information_schema.schemata; exact matching name means present, mismatched
returned spelling or malformed values reject. NULL is not authoritative absence because catalog
visibility can depend on privileges. Create/Delete issue quoted CREATE DATABASE/DROP DATABASE;
EnsureDeleted still attempts the explicitly owned DROP after a NULL catalog result and returns false
only for MySQL error1008 with a known execution outcome. Direct Delete preserves that error. Do not
swallow authorization/network/catalog errors or unknown execution outcomes.
EnsureCreated creates missing namespace/tables, skips populated schemas and can create an empty-model
namespace. Existing-schema-only behavior remains unchanged. Clear only the target connector pool at
namespace mutation boundaries through OceanBaseConnection.ClearPool, not ClearAllPools.

Async honors cancellation before validation/opening and on all commands. Dispose only the owned
management connection; preserve an operation failure over disposal and attach a secondary error as
Data["OceanBase.DatabaseManagementCleanupError"]. Disposal failure can leave native state requiring
manual cleanup. DDL is non-atomic and is not retried: a later table failure may leave the new database or
partial tables, with no automatic DROP rollback. Concurrent creation/deletion is not serialized.
Local tests and packaged consumers prove client control flow only; native privileges, selected-server
syntax, database-name rules/case, sessions and pool invalidation still require declared live evidence.

### Explicit management connection factory (Batch7e / SCH-02 / SCH-03)

`UseOceanBaseManagementConnection(Func<OceanBaseConnection>)` is independent of namespace ownership.
It never opens or requests credentials during options configuration, model creation, ordinary queries,
create-script generation or existing-schema-only table creation. Options clones retain the delegate;
service-provider identity/debug/log fragments never include it. A second context can supply a different
factory while reusing the same provider services. Enabling table-only management revokes database
ownership without losing the factory setting.

The factory supplies a fresh, closed connection whose credentials/routing/TLS settings are authoritative
for administration. The application must select the same tenant as its owned target, with sufficient
privileges. Different user names, endpoints and callbacks do not provide a generally verifiable tenant
identity; the provider never claims to prove this equivalence. MySQL management connections must not
select a database. Supplied targets and target hooks are supported without cloning them. Exact target
database scope, closed state, selected compatibility mode, no caller/enlisted/ambient transaction and
prevalidated model still apply before requesting a management connection.

Wrap the exact returned native instance using EF's `WithConnection(connection, owned: true)` so ordinary
connection/command diagnostics and interceptors continue to work and sync/async disposal is provider-owned.
Use the current runtime command timeout and operation cancellation token. Never modify or dispose the
target when a factory mistakenly returns it. Null, opened, mode-conflicting and database-selected results
reject before any command; dispose independent invalid results and attach any disposal failure to the
original validation error. Factory exceptions retain identity. Existing command/open/disposal error and
pool invalidation policies remain unchanged. There is no automatic retry or distributed DDL transaction.

Batch7f builds Oracle CREATE/DROP USER on this independently privileged connection. See the
[Oracle lifecycle contract](efcore-10-technical-baseline.md#oracle-owned-userschema-lifecycle-batch7f)
for explicit user ownership, catalog/privilege requirements and EF10 credential-log redaction.

### EnsureCreated seeding callbacks (SCH-02 / DEF-09)

Consume EF10 CoreOptionsExtension.Seeder/AsyncSeeder as the official EF10.0.0 relational creator does:
the synchronous EnsureCreated uses Seeder, the asynchronous variant uses AsyncSeeder, and the callback
receives the same context plus the schema operationsPerformed result. Both delegates can coexist;
only the matching one runs. Only the opposite delegate configured is an InvalidOperationException
with CoreStrings.MissingSeeder; validate that mismatch before opening/DDL. Direct CreateTables,
Create/Delete and script generation do not invoke callbacks. This also applies to existing-schema
creation and explicitly owned MySQL databases or Oracle users. Namespace DDL itself remains outside
the owned seeding transaction and is not rolled back by a callback failure.

Run the callback once after successful schema processing, including an already populated schema or
empty model. Begin a fresh owned target transaction, verify the current actual scope inside that
transaction before application code, invoke the callback and commit on success. Async forwards the
caller token through begin, scope query, callback and commit, with pre-cancellation checks. DDL failure
never invokes the callback. Caller/enlisted/ambient transactions retain the existing pre-open rejection.

Dispose only the owned seed transaction; preserve a callback/probe/commit failure if disposal also
fails, recording the secondary error as Data["OceanBase.SeedingCleanupError"]. Disposal after success
reports its own failure. EF/native transaction disposal supplies rollback for uncommitted work; actual
rollback, cleanup and unknown commit outcomes remain live evidence gates. Do not retry callbacks or
undo preceding DDL. The inspected EF10 EnsureCreated contract does not acquire a migration lock;
this profile adds no cross-process serialization, so application seeding must handle concurrent calls
and be idempotent. Migrate-time locking/seeding/retry/distributed transaction support remains open.

SeedingTests uses stateful connection/command/transaction doubles for positive both-mode callback and real
SaveChanges pipeline coverage. Its owned-MySQL-namespace case retains the actual provider-created native identity
and verifies that namespace/table DDL precedes a synthetic target transaction-start failure; it does not prove
positive physical namespace seeding. A scope mismatch after successful DDL must not claim that no DDL ran.
The package-only SeedingSmoke covers matching callbacks, true/false results, transactional scope checks and
commit/disposal with native doubles; it does not contact a server. Test-only service replacements are isolated
from EF's process-wide service-provider cache.

### Bulk relational update/delete (DEF-03)

Use EF10's published relational ExecuteUpdate/ExecuteDelete translation and immediate nonquery command
pipeline. Both APIs return affected rows and bypass change tracking; zero rows are not a concurrency
exception. Multiple API calls have no implicit shared transaction. Existing named query filters and
explicit predicates apply normally; applications supply concurrency predicates themselves.

MySQL UPDATE uses its table-reference list before SET, including EF-produced joins; UPDATE/FROM is
not the MySQL contract. SET targets are qualified to avoid joined-column ambiguity. Oracle retains
ordinary single-table UPDATE with unqualified SET targets and native target aliases. Joined Oracle
UPDATE and EF fallback update joins use the correlated rewrite below.
DELETE retains EF's single-target/predicate rewrite. Source contracts are the published EF10.0.0
QuerySqlGenerator and relational method translator plus OceanBase's public MySQL parser grammar,
downloaded for inspection in artifacts/bulk-dml-ef-source/. Public grammar is not deployed-server proof.

EF setter expressions refer to the pre-update row. MySQL's assignment order must not change a later
RHS read of another assigned target column. Topologically order assignments so readers precede writers;
ignore a setter's self-reference, and use the snapshot rewrite below for cycles. Joined MySQL updates
cannot rely on assignment order, so cross-assignment dependencies and derived sources also use snapshots.
Oracle's native simultaneous assignment retains the declared order. Duplicate target
columns reject rather than depend on assignment precedence.

For direct constant/parameter setter values, reuse the target mapping's IOceanBaseWriteValidation
before opening. Runtime parameter reads disable SQL caching so current values are checked every time;
predicates keep query transport independent of column capacity. Arbitrary server-computed expressions
cannot be client-validated against write bounds. Converter, SQL, physical affected-row, collation and
transaction outcomes remain live gates. No new driver or server limitation is inferred without evidence.

Server-computed Boolean setters and Boolean column copies with different storage converters are encoded
using CASE with independent source true/false comparisons and target true/false literals; SQL NULL remains
NULL. Do not copy canonical numeric truth or a source T/F representation directly into a Y/N target.
Other server expressions whose source/target converter instances differ reject until an explicit
translation exists. Direct literal/captured values keep EF's target-mapped parameter path.

Retain root UpdateExpression/DeleteExpression tags across parameter processing: the published EF10.0.0
UpdateExpression.VisitChildren can reconstruct an expression without its Tags after a child changes.
The wrapper captures/restores those statement tags; no connector or server change is needed.

Remaining DEF-03 work includes arbitrary converter server expressions,
advanced JSON/complex/inheritance/table-split shapes and complete live acceptance; this stage does not
establish complete bulk support. Rejecting an unimplemented shape is a provider gap, not a server defect.

### Oracle correlated bulk update rewrite (DEF-03)

For an EF-translated joined/pushed-down Oracle UpdateExpression, generate a single UPDATE of the
physical target under a fresh outer alias. Each SET value is a scalar SELECT over the complete source
table/join list, with its original predicate plus equality for every target primary-key column against
the outer row. An EXISTS SELECT over the same source/predicate/key correlation limits affected targets.
This preserves filters, outer joins, nullable scalar values and nested EF paging while keeping source
values separate from the updated target alias. Multiple assignments observe the original source row.

The Oracle relational translator accepts normalized left-join sources directly. EF10's default
UPDATE/FROM shape check rejects a left join following the target and its fallback can lose anonymous
setter bindings; the provider's correlated generator does not need that restriction. Sources requiring
pushdown, including paging and filtered joined targets, still use EF's primary-key fallback.

Use only normalized outer expressions without limit/offset/distinct/group/having/projections/order;
those operators must remain inside EF's translated source subqueries. A joined keyless/view target
has no physical correlation key and rejects before opening. Delimit all metadata identifiers and choose
the outer alias outside all table/column aliases in the complete source/setter trees. No arbitrary SQL
text is interpolated except provider-owned keywords and quoted model identifiers.

References inspected: published EF10.0.0 QuerySqlGenerator and relational bulk translator;
OceanBase documentation V4.3.5 commit2a5ae3a23a51729d065cec716508eb26cb064106, Oracle UPDATE and
scalar-subquery-expression references (artifacts/bulk-oracle-source/). The documented scalar subquery
returns NULL for zero rows and errors for more than one row. EXISTS prevents unrelated target updates;
no DISTINCT/MIN/first-row selection hides duplicate source matches. Key assignments are not restricted
by a MERGE ON clause because this rewrite uses UPDATE/WHERE. Physical read consistency, cardinality
errors, source plan costs, affected rows, privileges and exact deployed-version syntax need live tests.

### MySQL original-value snapshot rewrite (DEF-03)

For cyclic single-table assignments or joined assignments reading another assigned target column,
update the physical table under a fresh alias and join a derived source by every physical primary-key
column. The derived SELECT projects original keys as __ob_key_N and each complete encoded setter as
__ob_value_N, then preserves the complete EF source/predicate and any nested paging. The outer SET
only reads projected values, so assignment order cannot expose an already overwritten target value.
Ordinary acyclic single-table updates keep topological assignment ordering; independent joins keep
native table-reference SQL. Joined sources with derived tables use snapshots conservatively because
projected aliases can hide target-column dependencies. Joined keyless targets requiring a snapshot
reject before opening. Both generators accept normalized left joins without EF's UPDATE/FROM fallback.

Use SELECT /*+ NO_MERGE */ inside the source, per OceanBase's documented prohibition of view merging.
Target the physical table rather than updating the derived table (the MySQL UPDATE reference rejects
that). The pinned public docs/grammar establish syntax and rewrite intent, not native execution:
snapshot evaluation/read consistency, optimizer handling, duplicate-match choice, affected rows and
cost still need server acceptance. Multiple matching rows keep native MySQL joined-update behavior;
no DISTINCT/MIN/arbitrary client choice is added. References are OceanBase docs V4.3.5
commit2a5ae3a23a51729d065cec716508eb26cb064106, MySQL UPDATE and NO_MERGE cost-based transformation.

### Official relational model specifications (TST-01 / MOD-01/02/03)

Use the official Microsoft.EntityFrameworkCore.Relational.Specification.Tests EF10 package and its
ModelBuilderTest generic wrappers. A separate solution project derives the complete relational
one-to-many, many-to-one, one-to-one, many-to-many, inheritance and owned model families for each
compatibility mode. RelationalTestHelpers configures real OceanBase options and provider services
through public IDbContextOptionsExtension contracts, preserving provider conventions, mapping,
validation and diagnostic definitions. Do not replace the type mapper or validator with permissive
doubles. Model construction performs no connection opening; guard that boundary in the adapter.

Retain upstream assertions and report upstream skip reasons. Any provider-specific exclusion must
identify its exact inherited method and checklist limitation. Metadata conformance does not promote
deferred query/write/schema support. Native DDL/CRUD and the comprehensive standalone live application
remain separate from this offline official-suite adapter. Resolve the minimum EF10 package and a
selected later patch in isolated trees; no new runtime/provider package dependency is introduced.

### Transactions and savepoints (TXN-01..03)

OceanBaseBatchExecutor retains EF10's automatic transaction selection and modification-command counts but owns
the sync/async loops because EF exposes no protected recovery/cleanup seam. After an async write fails, automatic
savepoint rollback and release use CancellationToken.None; the caller's cancelled token still reaches opening,
savepoint creation, writes and commit. Explicit public savepoint/transaction operations continue honoring their
own tokens. AutoSavepointsEnabled=false leaves partial caller-transaction writes for caller rollback. A closed
connection prevents savepoint commands; successful client recovery does not establish server rollback.

Track a successful connection lease separately from transaction ownership. A failed open must not close the
caller's existing transaction/outer lease. Owned transaction disposal and final close never reuse the cancelled
token. Preserve the original write/cancellation exception when cleanup or diagnostic escalation fails; secondary
exceptions are attached in Data under OceanBase.SavepointRollbackError, OceanBase.SavepointReleaseError,
OceanBase.SavepointDiagnosticsError or OceanBase.BatchCleanupError. After a successful write, savepoint-release
failures retain EF's warning policy, including configured warning escalation. No automatic retry/commit occurs.

The registered connection's transaction-start loops reject known transaction conflicts before opening, reuse
EF's starting/started callbacks and transaction factory, and capture the candidate native handle until it becomes
CurrentTransaction. A callback/factory failure disposes that handle without cancellation and restores entry
connection accounting; a secondary disposal error is attached as OceanBase.TransactionStartCleanupError.
The existing open snapshot handles physical close failures separately as OceanBase.OpenCleanupError. Interceptors
that replace a transaction retain EF's responsibility for the replaced handle. These version-local loops and the
snapshot's private-field contract must be rechecked when upgrading EF10 patches or majors.

Native disposal failure or disposal diagnostics can leave EF's transaction wrapper marked disposed while still
installed, with its lease retained. Such a context/transaction is potentially unusable and is not safe to retry;
the application must perform explicit native cleanup/discard it. Recording an exception does not prove rollback
or successful cleanup. TransactionTests uses both registered-provider interception and stateful native doubles;
the package consumer pins automatic cancellation recovery with native doubles. All physical rollback/atomicity
and network/server state remain live acceptance gates.

Reuse EF10's relational transaction implementation for isolation, ownership, commit/rollback and command
attachment. The provider owns the failure-safe start/execution loops described above and registers a
mode-specific IRelationalTransactionFactory. MySQL keeps EF's transaction implementation unchanged.
Oracle overrides ReleaseSavepoint/Async as a logical no-op: it sends no RELEASE SAVEPOINT SQL, preserves
releasing/released/error diagnostics and interceptors, and honors pre-cancelled async tokens. Oracle savepoints
remain available until replacement or transaction end; logical release is not physical server release.

Creation and rollback retain EF's quoted SAVEPOINT / ROLLBACK TO statements, including escaped identifiers and
__EFSavePoint. This path intentionally uses EF commands rather than the connector's narrower public savepoint
name policy. Connector 0.2.0 (package repository commit4734781) explicitly rejects Oracle Release and requires
names beginning with a letter. That API boundary is not reported as a connector defect; the provider must adapt
EF's automatic-savepoint contract. No client test establishes a server-wide version/capability guarantee.

EF starts a transaction for multiple row batches and for a single MySQL write requiring a follow-up generated
read. Successful owned batches commit; a failed batch/readback disposes the active owned transaction without
committing. Actual rollback then depends on native Dispose/DisposeAsync and requires live evidence. A caller
transaction remains owned by the caller: SaveChanges uses an automatic savepoint, attempts rollback on failure,
and leaves a successfully recovered transaction attached for retry. Automatic cancellation recovery is locally
covered; native rollback/dispose behavior, ambient/distributed enlistment and server state remain required live work. Callers must roll back
or dispose a transaction whose recovery failed. Do not claim unconditional atomicity from command doubles.

The authoritative EF contracts are [RelationalTransaction v10.0.0](https://github.com/dotnet/efcore/blob/v10.0.0/src/EFCore.Relational/Storage/RelationalTransaction.cs)
and [BatchExecutor v10.0.0](https://github.com/dotnet/efcore/blob/v10.0.0/src/EFCore.Relational/Update/Internal/BatchExecutor.cs).
TransactionTests combines deterministic native connection/transaction/command doubles with registered-provider
interception for transaction-start failures/ownership. It retains the provider's transaction factory, SQL, mappings
and update batches and checks client control flow, tokens, ownership, attachment, diagnostics and failure/retry
boundaries. The packed consumer checks factory/Oracle logical release plus automatic cancellation recovery with
native doubles; it never opens a network connection.
These are neither native driver execution nor real rollback/savepoint evidence.

Update and delete conditions include the original key and concurrency-token values selected by EF. A result other
than exactly one affected row raises `DbUpdateConcurrencyException` with the affected entries. Provider exceptions
are wrapped in `DbUpdateException`; cancellation remains `OperationCanceledException`. An already-cancelled async
save fails before executing the batch's command.

### Tracked graph and nullable concurrency local contract

Batch5a uses EF's existing dependency ordering and relationship fixup; no provider graph scheduler is added.
GraphUpdateTests exercises dependent-first tracking of a three-level graph, principal-first inserts,
temporary-to-generated key propagation into child foreign keys, and dependent-first cascade deletes in both
modes, sync/async. Captured DML and MySQL generated-value SELECTs must use the same native connection.
The fixture disables automatic transactions; this does not prove atomicity or server-generated values.

Composite-key updates/deletes use original concurrency values: a NULL original emits IS NULL with no
parameter, while a non-NULL original retains its predicate bind. Current NULL values in SET/INSERT still
require an input parameter carrying DBNull. The batch only omits NULL predicate parameters; AddInput itself
must not discard NULL writes or generated-read key inputs. Tests pin complete DML, values in SQL order and
absence of unused native parameters, including Oracle named binds and RETURNING output binds.

Zero-row composite update/delete results retain the conflicting entity entry and pending state. Synthetic
connector errors during DML or MySQL generated readback retain the identical InnerException and failing
entry in DbUpdateException. Cancellation raised inside those command callbacks retains the original
OperationCanceledException/token, and async commands receive the SaveChangesAsync token. Supplied affected
rows, output parameters and readers establish provider behavior only; they do not establish server error
classification, actual network cancellation, matched-row behavior, rollback or transaction attachment.
Wider generation/readback shapes, unsupported graphs, transaction and diagnostic/logging gates remain open.

### Command execution boundary local contract

Batch5b's CommandExecutionBoundaryTests uses the real EF query pipeline with network-suppressing connection
and command interceptors. Both modes and sync/async verify that `Database.SetCommandTimeout(17)` reaches the
created `DbCommand`; an in-flight `OperationCanceledException` remains the same instance rather than becoming
a `DbUpdateException` or generic wrapper; and async command interception receives the caller's cancellation
token. A failed query is followed by a successful intercepted query in both sync and async paths, proving the
EF-visible connection state remains closed and the command pipeline can be retried between operations.

These are local EF/ADO.NET boundary checks. The connection-opening interceptor suppresses the physical open and
the command interceptors do not execute a connector wire operation, so these tests do not prove physical close/
reuse, server timeout enforcement, network cancellation, connector resource cleanup, transaction
rollback or live connection behavior. Those gates remain under CON-05/TXN and the live matrix.

### Command diagnostics and redaction local contract

Batch5c's CommandLoggingTests uses `LogTo` with the provider's normal EF relational services and runs successful
and failing queries in both modes/sync paths. Runtime-captured parameter values are redacted from default
command/error log messages and appear when `EnableSensitiveDataLogging` is explicitly enabled; command SQL and
the original synthetic failure text remain observable. Compile-time constants are deliberately excluded because
EF may render them as SQL literals before logging. This proves the EF/provider logging boundary and interceptor
delivery only. Connector log sinks, server diagnostics, structured redaction policy and live failure output
remain outside the local evidence.

Both provider-created and caller-owned connections are checked for compatibility-mode conflicts. A closed connection
without `ExpectedMode` receives the configured provider mode before opening. An already-open connection must agree
with that mode. MySQL requires `UseAffectedRows=false`: EF concurrency checks count matched rows, including a match
whose stored values do not change. A conflicting option is rejected before execution rather than silently changed.
Validation happens at construction, creation, replacement and opening boundaries; the DbConnection getter stays
safe for EF's disposal/reset paths after rejection. Locally tested ownership covers closed supplied and created
connections, including sync/async disposal; opened-connection ownership still requires live evidence.

## Verification and remaining gates

`SqlGeneratorTests` verifies both-mode quoting and paging, offset without a limit, joins, grouped aggregate SQL
and inserts without SQL Server `OUTPUT`.

`BasicRelationalTests` executes the real EF query and SaveChanges pipelines with connector commands. Test-only
interceptors suppress network I/O and provide controlled result readers, output parameters and affected-row counts.
The tests verify scalar materialization, input binding, generated-key propagation, insert/update/delete,
default-only inserts, concurrency conflicts and cancellation. They deliberately disable automatic transactions;
they provide no transaction or live server evidence.
Unsupported MySQL string/default-expression keys have sync/async regression tests asserting that rejection occurs
before any command is executed.

Still required for Task 4 and release:

- Implement database creation/existence and the documented `EnsureCreated`/`EnsureDeleted` profile.
- Verify generated keys against actual identity/default columns in both modes.
- Verify MySQL matched-row behavior on a live server; connector option conflicts are covered locally.
- Complete string, decimal, temporal, Boolean, binary and Guid mappings before advertising broad query support.
- Run real transaction commit/rollback and caller-owned transaction tests, including failure after DML/readback.
- Run the two-mode live functional matrix and record connector/server versions and transport settings.
- Extend specification tests, expand the packed consumer beyond smoke checks and validate EF10 patch compatibility.

## Package checkpoint validation

### Repeatable package content gate

Run the read-only gate after packing, before the isolated consumer checks:

```powershell
pwsh -NoProfile -File tests/OceanBase.EntityFrameworkCore.PackageConsumer/Verify-Package.ps1 `
    -PackagePath artifacts/checkpoint-floating-round-20261111/OceanBase.EntityFrameworkCore.10.0.0-checkpoint.floating-round-20261111.nupkg `
    -ExpectedVersion 10.0.0-checkpoint.floating-round-20261111
```

The PowerShell 7 script uses .NET ZIP/XML APIs and checks ID/version, the net10.0 assembly and dependency group,
exact expected dependency ranges, and a nonempty packaged README with matching metadata. It does not build,
restore, execute provider queries or establish server/runtime compatibility. The current floating Round package passes;
wrong expected version, the old README-less alias package and the exact-patch README probe fail at their intended
checks. The hosted e41981e package-content gate passed on Windows/Linux; the current package checks ran locally
on Windows. Change the path/version for each new pack.

### Current floating Round checkpoint (2026-11-11)

Debug, Release and isolated EF Core 10.0.14 Release each pass 1780 tests (1414 unit, 364 intercepted,
two project consumers). The same `10.0.0-checkpoint.floating-round-20261111` package passes both modes
on EF Core 10.0.0 and 10.0.14. Its consumer checks both Double limits, ties-to-even and the
wide Single path with neutral casts. Package contents, package-only graph, baseline assembly and
baseline asset identity pass. Server precision and runtime syntax arguments remain open.

### Historical decimal Round checkpoint (2026-11-11)

Debug, Release and isolated EF Core 10.0.14 Release each pass 1768 tests (1406 unit, 360 intercepted,
two project consumers). The same `10.0.0-checkpoint.decimal-round-20261111` package passes both modes
with EF Core 10.0.0 and 10.0.14, covering default ties-to-even, 28 digits with AwayFromZero and
ToZero predicates. The previous package fails the new translation. Content, package-only graph,
baseline assembly identity and unchanged baseline assets pass. Native decimal arithmetic and
NULL/metadata behavior remain live gates.

### Historical scalar numeric extrema checkpoint (2026-11-11)

Debug, Release and isolated EF Core 10.0.14 Release each pass 1760 tests (1400 unit, 358 intercepted,
two project consumers). The same `10.0.0-checkpoint.extrema-20261111` package passes both compatibility
modes at EF Core 10.0.0 and 10.0.14. The consumer checks native scalar extrema, fixed Single result casts,
and captured array mutation and reversion, as well as the prior aggregate and other contracts. Package content,
package-only dependency graph, baseline assembly identity and unchanged baseline assets pass. Native result
typing, NULL handling, coercion and numeric edge behavior remain live gates.

### Historical numeric aggregate checkpoint (2026-11-11)

Full Debug/Release and isolated EF10.0.14 Release each pass1711 (1357 unit, 352 intercepted pipeline, two
project consumers). Focused279 numeric aggregate unit/16 intercepted cases and prior numeric/shape regressions
pass after genuine native-AVG/wide-Single and Oracle synthetic-zero RED/GREEN evidence. The same baseline-
built `10.0.0-checkpoint.aggregates-20261111` nupkg passes both modes at EF10.0.0/10.0.14. It requires native
AVG input precision, all four aggregate names, neutral Single targets including wide-column extrema,
filtered DISTINCT/HAVING/order and prior checks. Previous transcendental package fails the new aggregate
assertion. Content/package-only graph, unchanged EF10.0.0 baseline assets and packed/baseline DLL identity
pass. Independent source review passes without findings. Native metadata/accumulator/CASE/COALESCE/cast/
rounding and server empty/group execution remain live gates. Wider local query/model/write/transaction/
schema work and current hosted/Linux execution remain open. Repeat consumer commands below with
`10.0.0-checkpoint.aggregates-20261111` and `artifacts/checkpoint-aggregates-20261111`.
Evidence: `artifacts/local-development/batch3n/`.

### Historical exact transcendental checkpoint (2026-11-11)

Full Debug/Release and isolated EF10.0.14 Release each pass1416 (1078 unit, 336 intercepted pipeline, two
project consumers). Focused65 unit/12 intercepted pass after missing-dispatch and private-marker RED/GREEN
regressions. The same baseline-built `10.0.0-checkpoint.transcendental-20261111` nupkg passes both modes at
EF10.0.0/10.0.14 for all twelve server predicates, native function names, MySQL logarithm quotient, neutral
Single result targets and all prior assertions. Previous Floor/Ceiling package rejects Math.Sqrt in WHERE.
Content/package-only graph, unchanged baseline EF10.0.0 assets and packed/baseline DLL identity pass.
Independent source review passes without findings. Native metadata/calculation, quotient/cast precision,
domain errors/NULL, overflow/underflow and subnormals remain real-server gates. Round/wider local query/
model/write/transaction/schema and current hosted/Linux execution were still open there. Repeat the consumer commands
below with `10.0.0-checkpoint.transcendental-20261111` and `artifacts/checkpoint-transcendental-20261111`.
Evidence: `artifacts/local-development/batch3m/`.

### Historical exact Floor/Ceiling checkpoint (2026-11-11)

Full Debug/Release and isolated EF10.0.14 Release each pass1339 (1013 unit, 324 intercepted pipeline, two
project consumers). Focused46 unit/12 intercepted pass after genuine missing-dispatch and nullable-syntax-
carrier RED regressions. The same baseline-built `10.0.0-checkpoint.floor-ceiling-20261111` nupkg passes
both modes at EF10.0.0/10.0.14 for all eight exact Floor/Ceiling predicates. MySQL requires guarded
TRUNCATE/CASE with the correct adjustment and rejects unguarded native FLOOR/CEIL; Oracle requires the
correct FLOOR/CEIL name. All prior assertions remain. Previous inline-empty package rejects Math.Floor.
Content/package-only graph pass; baseline assets stay10.0.0. Independent source review passes with no actionable findings.
Native numeric result families, full-range/NULL/CASE calculation and volatile single evaluation remain
unverified. Round/transcendental/wider query work and current hosted/Linux execution were still open there;
the subsequent transcendental checkpoint is recorded in the progress document.

Repeat the commands below with `10.0.0-checkpoint.floor-ceiling-20261111` and
`artifacts/checkpoint-floor-ceiling-20261111`. Evidence: `artifacts/local-development/batch3l/`.

### Historical originally empty inline integral roots checkpoint (2026-11-11)

Full Debug/Release and isolated EF10.0.14 Release each pass1281 (967 unit, 312 intercepted pipeline, two
project consumers). A later test-only newline assertion portability edit reruns the covering eight cases;
production code stays unchanged. The same baseline-built `10.0.0-checkpoint.inline-empty-20261111` nupkg
passes both modes at EF10.0.0/10.0.14 for original inline-empty Count/nullable Any/nested join, sole-ordinal
declaration/order/zero-row SQL and all previous checks. Previous integral-roots package fails the new
inline-empty Count with missing retained mapping. Content/package-only graph pass; baseline assets stay10.0.0.
Independent source review passes with no actionable findings. Native results/typing/order/limits, precompiled annotation quoting
and current hosted/Linux execution remain unverified. General text/other root families and broader converter
projections stay local follow-ups; QRY-03 is partial.

Repeat the commands below with `10.0.0-checkpoint.inline-empty-20261111` and
`artifacts/checkpoint-inline-empty-20261111`. Evidence: `artifacts/local-development/batch3k/`.

### Historical captured integral roots checkpoint (2026-11-11)

Full Debug/Release and isolated EF10.0.14 Release pass 1256 tests (950 unit, 304 intercepted pipeline,
two project consumers). Independent source/scoped test reviews pass; the two nested-CAST cases pass in
all three configurations after a test-only extension. The same baseline-built
`10.0.0-checkpoint.integral-roots-20261111` nupkg passes both modes at EF10.0.0/10.0.14, with repeated correlated
three/zero/six/zero collection roots, SELECT unions and Oracle DUAL in every arm, plus previous checks.
The prior text-membership package fails the parameter-root IntTypeMapping cast. Content and package-only
graph checks pass; baseline assets stay EF10.0.0. No production change/repack followed the test extension.

Repeat the commands below with `10.0.0-checkpoint.integral-roots-20261111` and
`artifacts/checkpoint-integral-roots-20261111`. Evidence: `artifacts/local-development/batch3j/`.
Originally empty inline/general text/other carrier roots, broader converter projection and actual server
UNION coercion/results/order/limits remain open. Hosted evidence still applies to e41981e.

### Historical scalar text membership checkpoint (2026-11-11)

Debug/Release and isolated EF10.0.14 Release pass 1200 tests (910 unit, 288 intercepted pipeline, two project
consumers). Independent spec/quality review passed. The same baseline-built
`10.0.0-checkpoint.text-membership-20261111` nupkg passes both modes at
EF10.0.0/10.0.14. Repeated text captures alternate nonempty/empty/padded/all-provider-null collections;
Oracle default null compensation produces IS NULL without a residual equality/IN arm. Unit/pipeline tests
separately preserve native UNKNOWN under UseRelationalNulls. The prior membership package fails the new
text collection mapping. Content/package-only graph checks pass; baseline source assets remain EF10.0.0.

Repeat the commands below with `10.0.0-checkpoint.text-membership-20261111` and
`artifacts/checkpoint-text-membership-20261111`. Evidence: `artifacts/local-development/batch3i/`.
General VALUES/other collection families and native collation/padding/LOB/charset/results remain open.
Hosted Windows/Linux evidence above still applies to e41981e.

### Historical integral membership and relational-shape checkpoint (2026-11-11)

Debug/Release and isolated EF10.0.14 Release pass 1155 tests (885 unit, 268 intercepted pipeline, two project
consumers). Independent spec/quality and scoped cache-fixture reviews pass. The same baseline-built
`10.0.0-checkpoint.membership-20261111` nupkg passes both modes at EF10.0.0/10.0.14. Repeated int[] captures
change between nonempty, empty and padded lengths; the same query across default/Constant contexts keeps
the selected parameter/literal policy. The preceding numeric-core package fails the new collection mapping.
Content and package-only graph checks pass; baseline source assets remain EF10.0.0.

Repeat the commands below with `10.0.0-checkpoint.membership-20261111` and
`artifacts/checkpoint-membership-20261111`. Evidence: `artifacts/local-development/batch3h/`.
QRY-03/04 remain partial: text/general collections, wider aggregates and actual server execution stay open.
Hosted Windows/Linux evidence above applies to e41981e, not this source.

### Historical numeric Abs, Sign and Truncate checkpoint (2026-11-11)

Debug/Release and isolated EF10.0.14 Release pass 1083 tests (853 unit, 228 intercepted pipeline, two project
consumers). Independent spec/quality review passed. The same baseline-built
`10.0.0-checkpoint.numeric-core-20261111` nupkg passes both modes at EF10.0.0/10.0.14, with mapped numeric
predicates plus prior checks. The preceding temporal-parts package fails the new translations. Content and
package-only graph checks pass; baseline source assets remain EF10.0.0.

Repeat the commands below with `10.0.0-checkpoint.numeric-core-20261111` and
`artifacts/checkpoint-numeric-core-20261111`. Evidence: `artifacts/local-development/batch3g/`.
Native arithmetic/domain/overflow/result families and hosted execution for this source remain open.

### Historical ordinary temporal parts checkpoint (2026-11-11)

Final Debug/Release and isolated EF10.0.14 Release pass 963 tests (745 unit, 216 intercepted pipeline, two
project consumers), including four test-only review cases added after the initial 959-pass run. Independent code review
and scoped test-fix re-review passed. Captured DateOnly/TimeOnly carrier and precision checks retain existing
transport behavior. The same baseline-built `10.0.0-checkpoint.temporal-parts-20261111` nupkg passes both modes
at EF10.0.0/10.0.14, covering all six units, DATE clock casts and prior checks. The preceding Substring package
fails the new temporal predicate. Content and package-only graph gates pass.

Repeat the commands below with `10.0.0-checkpoint.temporal-parts-20261111` and
`artifacts/checkpoint-temporal-parts-20261111`. Evidence: `artifacts/local-development/batch3f/`.
Native extraction/casts/fractional/calendar/interval results and hosted execution for this source remain open.

### Historical Substring checkpoint (2026-11-11)

Debug/Release and isolated EF10.0.14 Release pass 934 tests (724 unit, 208 intercepted pipeline, two project
consumers). Independent spec/quality review passed. The same baseline-built `10.0.0-checkpoint.substring-20261111`
package passes both modes on EF10.0.0/10.0.14, including both arities and positive 2147483648 origin adjustment.
The previous search package fails the new method translation. Package-content and package-only graph checks pass.

Repeat the commands below with `10.0.0-checkpoint.substring-20261111` and `artifacts/checkpoint-substring-20261111`.
Evidence: `artifacts/local-development/batch3e/`. This establishes no server slicing/range/padding/LOB/NCLOB
outcome or hosted execution for the current source. Native conversion/character boundaries remain as above.

### Historical string-search checkpoint (2026-11-11)

Debug/Release and isolated EF10.0.14 Release pass 914 tests (712 unit, 200 intercepted pipeline, two project
consumers). Independent spec/quality review passed. The same baseline-built `10.0.0-checkpoint.search-20261111`
package passes both modes on EF10.0.0/10.0.14, including search syntax and changing nonempty/empty/null
captures. The previous wide-decimal package fails the new method translations. Package-content and
package-only dependency checks pass.

Repeat the commands below with `10.0.0-checkpoint.search-20261111` and `artifacts/checkpoint-search-20261111`.
Evidence: `artifacts/local-development/batch3d/`. The later e41981e
[hosted run 42865290840](https://github.com/cocosip/efcore.oceanbase/actions/runs/42865290840) passed all four gates
on Windows/Linux. It does not cover later source or server searches; collation/padding/character/LOB gates remain.

### Historical wide-decimal floating checkpoint (2026-11-11)

Debug/Release and isolated EF10.0.14 Release pass 882 tests (688 unit, 192 intercepted pipeline, two project
consumers). Independent spec/quality review passed. The same baseline-built
`10.0.0-checkpoint.wide-decimal-20261111` package passes both modes on EF10.0.0/10.0.14, including direct
Single midpoint rounding (bits 0x3f800001) and a wide Double reader value. The preceding unary package fails
the new reader smoke. Package-content and package-only dependency checks pass.

Repeat the commands below with `10.0.0-checkpoint.wide-decimal-20261111` and
`artifacts/checkpoint-wide-decimal-20261111`. Evidence: `artifacts/local-development/batch2d/`.
This uses synthetic connector wrapper values and establishes no new store mapping or server/platform behavior.

### Historical unary string-function checkpoint (2026-11-11)

Debug/Release and isolated EF10.0.14 Release pass 833 tests (647 unit, 184 intercepted pipeline, two project
consumers). Independent spec/quality review passed. The same baseline-built `10.0.0-checkpoint.unary-20261111`
package passes both modes at EF10.0.0/10.0.14, including native LOWER/UPPER/TRIM/LTRIM/RTRIM and character length.
The previous arithmetic package fails the new function predicate. The package-content gate and package-only
graph checks pass. A later read-only check confirms Windows/Linux hosted CI gates passed for db676ff in
[run 42854480450](https://github.com/cocosip/efcore.oceanbase/actions/runs/42854480450); no server function was executed.

Repeat the commands below with `10.0.0-checkpoint.unary-20261111` and `artifacts/checkpoint-unary-20261111`.
Evidence: `artifacts/local-development/batch3c/`. Native casing, SQL-space trim, database-character units,
converter/LOB execution and live results retain the limits above. This historical checkpoint predates the
wide-decimal floating-reader fix recorded above.

### Historical arithmetic checkpoint (2026-11-10)

Debug/Release and isolated EF10.0.14 Release each pass 801 tests (623 unit, 176 intercepted pipeline, two project
consumers). Independent spec/quality review passed. The same baseline-built
`10.0.0-checkpoint.arithmetic-20261110` nupkg passes both modes at EF10.0.0/10.0.14, including native integer
division/remainder and preserved decimal division. The previous concat package fails the new arithmetic check.
The repeatable package-content gate and package-only graph checks pass. No server arithmetic was executed.

Repeat the commands below with `10.0.0-checkpoint.arithmetic-20261110` and `artifacts/checkpoint-arithmetic-20261110`.
Evidence: `artifacts/local-development/batch3b/`. See the arithmetic contract for overflow/zero-divisor/live limits.

### Historical string concatenation checkpoint (2026-11-10)

Debug/Release and isolated EF10.0.14 Release each pass 785 tests (611 unit, 172 intercepted pipeline, two project
consumers). Independent review passed. The same baseline-built `10.0.0-checkpoint.concat-20261110` package passes
both-mode consumers at EF10.0.0/10.0.14, including native concatenation and changing Oracle empty/null/nonempty
fallback captures. The previous numeric package fails the new concat assertion. Verify-Package.ps1 passes
identity/assets/README/ranges; the isolated consumer graph contains only packages. No server function was run.

Repeat the commands below with `10.0.0-checkpoint.concat-20261110` and `artifacts/checkpoint-concat-20261110`.
Evidence: `artifacts/local-development/batch3a/`. See the local contract above for native Oracle empty/null rules,
converter boundaries and the conservative command-SQL cache tradeoff. Wider query and live gates remain open.

### Historical exact integer normalization checkpoint (2026-11-10)

Debug/Release on EF10.0.0 and isolated Release on EF10.0.14 each pass 752 tests (590 unit, 160 intercepted
pipeline, two project consumers). The same baseline-built `10.0.0-checkpoint.numeric-20261110` nupkg passes both
modes on both selected patches, including rejection of Single/Double fractions in integer reader customization.
The preceding temporal2 package fails that smoke check. Independent review passed. Package identity, net10.0,
README, connector version, bounded EF ranges and package-only consumer dependencies were inspected.

Repeat the commands below with `10.0.0-checkpoint.numeric-20261110` and `artifacts/checkpoint-numeric-20261110`.
Logs: `artifacts/local-development/batch2c/`. Wider queries/release gates and all live acceptance remain open.

### Historical temporal facet/clone checkpoint (2026-11-10)

Debug/Release on EF10.0.0 and isolated Release on EF10.0.14 each pass 706 tests (554 unit, 150 intercepted
pipeline, two project consumers). The same baseline-built `10.0.0-checkpoint.temporal2-20261110` nupkg passes
both-mode consumers on EF10.0.0/10.0.14, including temporal length/declaration/DbType clone rejection. Earlier
alias/unfinished temporal packages fail these regressions. Independent review and one focused fix round passed.
Nuspec identity, net10.0 assembly, included README, connector version, bounded EF ranges and package-only graph
were inspected. The initial temporal probe predates review fixes and is not current evidence.

Repeat the commands below with `10.0.0-checkpoint.temporal2-20261110` and `artifacts/checkpoint-temporal2-20261110`.
Logs: `artifacts/local-development/batch2b/`. Broader release gates and live database acceptance remain open.

### Historical fixed-point alias checkpoint (2026-11-10)

After the configuration/cache coverage and Oracle bare DECIMAL/NUMERIC fix, Debug/Release on EF10.0.0 and isolated
Release on EF10.0.14 each pass 627 tests (475 unit, 150 intercepted pipeline, two project consumers).
The same baseline-built `10.0.0-checkpoint.alias-20261110` nupkg passes both-mode consumers on both selected
patches. Expanded checks verify decimal/integer/Boolean alias defaults, lossless query inputs, output facets and
bare NUMBER independence; the old text package fails the alias regression. Package ID/version, net10.0 asset,
connector 0.2.0, bounded EF ranges and the package-only consumer graph were inspected. Independent review passed.

Repeat the commands below with `10.0.0-checkpoint.alias-20261110` and `artifacts/checkpoint-alias-20261110` as
version/feed. Do not overwrite the historical text package. Logs are under `artifacts/local-development/batch2a/`.
The package README, broader release gates and all live database acceptance remain open.

### Historical text checkpoint (2026-11-10)

Development resumed directly on efcore/10.0 from 20a2f18. The text declaration/write slice passes 563 tests
(420 unit, 142 intercepted pipeline, one project consumer) in Debug/Release on EF10.0.0 and in isolated Release
on EF10.0.14. The same baseline-built `10.0.0-checkpoint.text-20261110` package passes both-mode consumers
on EF10.0.0/10.0.14, including invalid text declaration/UTF-16 rejection and lossless national query binding.
Its ZIP/nuspec contains the net10.0 assembly, connector 0.2.0 and both bounded EF ranges. The consumer assets
have only package dependencies; loaded EF assembly versions are 10.0.0.0/10.0.14.0. No live execution occurs.
The missing package README message remains release preparation work. No publish or release tag was created.

Repeat the package checks from the repository root in PowerShell:

```powershell
$textVersion = '10.0.0-checkpoint.text-20261110'
$textFeed = Join-Path (Get-Location) 'artifacts/checkpoint-text-20261110'
$textConsumer = 'tests/OceanBase.EntityFrameworkCore.PackageConsumer/OceanBase.EntityFrameworkCore.PackageConsumer.csproj'
dotnet pack src/OceanBase.EntityFrameworkCore/OceanBase.EntityFrameworkCore.csproj -c Release `
    "-p:PackageVersion=$textVersion" -o $textFeed -m:1 -nr:false
foreach ($textPatch in @('10.0.0', '10.0.14')) {
    dotnet restore $textConsumer "-p:OceanBaseProviderVersion=$textVersion" `
        "-p:OceanBaseConsumerEfCoreVersion=$textPatch" --source $textFeed `
        --source (Join-Path $env:USERPROFILE '.nuget/packages') `
        --packages (Join-Path $textFeed 'consumer-packages') --force-evaluate --disable-parallel
    dotnet run --project $textConsumer -c Release --no-restore `
        "-p:OceanBaseProviderVersion=$textVersion" "-p:OceanBaseConsumerEfCoreVersion=$textPatch" -m:1 -nr:false
}
```

The recorded default consumer omitted the explicit EF version and resolved the minimum 10.0.0; the selected
later-patch run requested 10.0.14. Do not reuse this package version for later source changes: pack a unique
checkpoint version and repeat the evidence. Character-set byte budgets, live text/LOB semantics, transactions,
specification tests and final release gates remain open.

### Historical national-text checkpoint

Implementation revision `d46ae9d` was fast-forwarded from dev into efcore/10.0 after Debug and Release each passed
510 tests (383 unit, 126 intercepted pipeline and one project-reference consumer). A local prerelease package was
built on efcore/10.0; no release tag or NuGet publication was created. Branch synchronization is recorded in the
[progress handoff](efcore-10-progress.md). The earlier `2a02bc8`/95-test, `8664692`/145-test,
`188c25d`/195-test, `fbb33ea`/257-test, `3ab2345`/317-test, `9c78329`/366-test and `c84ad9d`/432-test and `f645608`/450-test and `3365ad9`/498-test packages
remain historical evidence; use the current revision below with the expanded consumer fixture.

The package `OceanBase.EntityFrameworkCore.10.0.0-checkpoint.d46ae9d.nupkg` contains net10.0 assets and its nuspec
declares `[10.0.0,11.0.0)` for both EF Core and EF Core Relational. These were checked by opening the ZIP/nuspec
and asserting the dependency IDs, ranges and assembly path. The packaging tool reported a missing package README;
package presentation remains release preparation work.

The standalone [package consumer](../../tests/OceanBase.EntityFrameworkCore.PackageConsumer/Program.cs) references
only the packed provider by default, with no ProjectReference or direct connector/EF dependencies. Its executable asserts
registration, Guid, five-type temporal, bounded/unbounded binary and explicit integer/Boolean/Guid/native floating/decimal storage, Boolean
projection and mode-specific paged query generation in both modes. The expanded national-text consumer failed on the preceding
3365ad9 package before passing with this revision: Oracle NVARCHAR2 selects native national binding and longer
query operands widen transport Size independently of column length. Decimal parameters retain value inference
independently of physical precision/scale. Temporal comparands retain fractional precision and Oracle interval
day capacity; native floating literals and captured Guid operands retain their tested representations. It deliberately does
not connect to a database. It stays outside the normal solution because it requires an already-built local
provider package. Central versions are disabled only in this isolated fixture so repository EF references cannot
mask package dependency errors. Setting `OceanBaseConsumerEfCoreVersion` adds explicit EF Core and Relational
references to simulate a consumer requesting a later patch; it does not rebuild or reference provider source.

| Validation | EF Core / Relational | Result and evidence boundary |
| --- | --- | --- |
| Provider build and Debug/Release suites | 10.0.0 | 510 passed per configuration; unit/intercepted/project consumer only. |
| Default standalone consumer | 10.0.0 | Both modes pass; provider package built against 10.0.0. |
| Consumer explicitly upgrades EF packages | 10.0.14 | Both modes pass with the same provider nupkg/assembly; runtime reports EF 10.0.14.0. |
| Source build and full Release suite | 10.0.14 | 510 passed; isolated central-version override and artifact paths, not the baseline-compiled package. |
| Package range boundary | [10.0.0,11.0.0) | Nuspec inspected; NuGet.Versioning parser accepts 10.0.0/10.0.14 and rejects 11.0.0. No EF11 runtime test claimed. |

Both `project.assets.json` and the consumer's loaded EF assembly version were inspected. These checks establish
the selected patch pair at this source revision; they do not prove every intermediate/future patch or any live
server behavior. Re-run the matrix after provider changes before release.

To repeat the checkpoint consumer verification from the repository root in PowerShell:

```powershell
$providerVersion = '10.0.0-checkpoint.d46ae9d'
$consumerProject = 'tests/OceanBase.EntityFrameworkCore.PackageConsumer/OceanBase.EntityFrameworkCore.PackageConsumer.csproj'
$packageFeed = Join-Path (Get-Location) 'artifacts/checkpoint-d46ae9d'
$consumerPackages = Join-Path $packageFeed 'consumer-packages'
dotnet restore $consumerProject "-p:OceanBaseProviderVersion=$providerVersion" `
    --source $packageFeed --source (Join-Path $env:USERPROFILE '.nuget/packages') `
    --packages $consumerPackages --force-evaluate --disable-parallel
dotnet run --project $consumerProject -c Release --no-restore `
    "-p:OceanBaseProviderVersion=$providerVersion" -m:1 -nr:false

# Consumer patch upgrade: reuse the same provider package, with explicit EF references.
dotnet restore $consumerProject "-p:OceanBaseProviderVersion=$providerVersion" `
    -p:OceanBaseConsumerEfCoreVersion=10.0.14 --source $packageFeed `
    --source (Join-Path $env:USERPROFILE '.nuget/packages') --packages $consumerPackages `
    --force-evaluate --disable-parallel
dotnet run --project $consumerProject -c Release --no-restore `
    "-p:OceanBaseProviderVersion=$providerVersion" -p:OceanBaseConsumerEfCoreVersion=10.0.14 -m:1 -nr:false
```

The validation used the local package feed and existing cached dependency packages as feeds, with a separate
consumer package directory (the recorded run reused the existing checkpoint-8664692 cache). It proves package restore/consumption, not remote NuGet availability. For another
revision, first pack a unique local prerelease version on efcore/10.0 and update the version/feed values.

To compile/test provider source against the selected later patch without changing repository package ranges:

```powershell
$patchDirectory = Join-Path (Get-Location) 'artifacts/compatibility-10.0.14'
New-Item -ItemType Directory -Path $patchDirectory -Force | Out-Null
[xml]$central = Get-Content Directory.Packages.props -Raw
foreach ($node in $central.Project.ItemGroup.PackageVersion) {
    if ($node.Include -in @('Microsoft.EntityFrameworkCore', 'Microsoft.EntityFrameworkCore.Relational')) {
        $node.Version = '[10.0.14]'
    }
}
$patchProps = Join-Path $patchDirectory 'Directory.Packages.props'
$central.Save($patchProps)
$patchArtifacts = Join-Path $patchDirectory 'build'
dotnet test OceanBase.EntityFrameworkCore.slnx -c Release --artifacts-path $patchArtifacts `
    "-p:DirectoryPackagesPropsPath=$patchProps" "-p:RestorePackagesPath=$consumerPackages" `
    "-p:RestoreSources=$(Join-Path $env:USERPROFILE '.nuget/packages')" -m:1 -nr:false
```

The EF11 exclusion is a package-resolution boundary, not a tested EF11 adaptation. Each branch must rebuild and
review its own EF-major contract. Full Task 5 acceptance still requires specification tests, live evidence,
release-revision package gates and support records.
