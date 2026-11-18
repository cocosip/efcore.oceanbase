# EF Core 10 Feature Gap Matrix

Updated: 2026-11-18; corresponds to the [55 feature groups](efcore-10-feature-matrix.md). This matrix tracks only code capability gaps,
explicitly unsupported features, dependencies, and capabilities to confirm. Pre-release tests, acceptance coverage, and review evidence belong only in the [release checklist](efcore-10-release-checklist.md).

## Statuses and counts

- The fixed statuses are `Not started` / `In development` / `Implemented` / `Blocked` / `Deferred` / `Unsupported` / `To confirm`.
  Update the existing row during development. Once implemented, retain its ID, set it to Implemented, and record the implementation entry point/commit; do not create a separate feature document.
- `P0/P1 completion` identifies feature development to prioritize. `Extension` identifies signatures/metadata capabilities confirmed to be unavailable; it does not automatically add a commitment for the current release.
  `Unsupported` is an explicit rejection in the current contract; update the scope and approach before lifting it. `Deferred` preserves the original release decision for four groups.
- Count G entries as **gap records**: one record may cover a set of overloads. This is not an API count and must not be subtracted from the 55 feature groups.
  G-21/G-28 are related details of deferred groups, not two additional deferred feature groups. Count B/C/D separately; do not present them as Provider development backlog counts.
- The current known unavailable/rejected capabilities are based on the code and package README. Supporting every BCL method is not an implicit Provider commitment.
  Add newly discovered valid requirements to their owning row or give them a new stable ID. Rejection of invalid inputs or malformed models/catalogs is validation, not a missing feature.

Current counts: 50 G records (2 Not started, 1 In development, 24 Implemented, 1 Blocked, 20 Unsupported, 2 Deferred). Also retained are 2 Implemented F records, 9 external dependency B records, 4 To confirm C records, and 4 Deferred D groups.

## Development order

Prioritize missing Provider functionality before the pre-release acceptance campaign.

1. G-01..11 complete the selected P0/P1 temporal additions and differences. Preserve DateTime's
   separate whole/fraction tick truncation, exact Int64 duration results and TimeOnly's whole-product
   truncation/saturation, 24-hour wrapping, NULL/precision and contextual converter contracts.
2. G-13 conversions and IsBetween are implemented for the exact signatures in the feature inventory;
   preserve stored-calendar, NULL/precision, invalid-duration and DateTimeKind boundaries.
3. G-14 char search overloads are implemented using existing search SQL and contextual text mappings;
   preserve char-to-text conversion, valid Unicode, total Boolean NULL semantics and native collation.
   Repeated mapping-specific parameters keep their canonical names in both modes.
4. G-16 Remove(int)/Remove(int,int) are implemented using guarded native substring/concat, native
   character counts and variable result mappings. Preserve NULL/range/overflow and converter ownership.
5. G-15 explicit trim char/char[] signatures are implemented as repeated character-set removal.
   Preserve null/empty array whitespace, valid Unicode, refreshed arrays/inline nullable char columns,
   EF.Constant inlining, stored-text converter ownership, case sensitivity and strict regex anchors
   without NULL compensation.
6. G-18 implements the six IndexOf signatures with native character positions and guarded ranges.
   G-47 implements ordinary string and char Replace. G-48 implements guarded Insert. G-49 implements
   non-truncating character padding. G-30 implements table and column comment DDL, and G-29
   accepts model trigger metadata. G-37 preserves the MySQL DATETIME update clause in scaffolded
   models and DDL. G-36 preserves MySQL BTREE prefix indexes. Expression and other non-ordinary
   indexes require a complete public catalog contract. Continue with the remaining Provider features,
   selected by application value and Provider feasibility rather than a commitment to every BCL method.
   Investigate C items before classifying them as missing code;
   retain Unsupported/Deferred contracts and resolve B dependencies at their owning layer.

Each implementation still needs focused SQL/mapping/behavior regression tests and relevant inventory
updates. Schedule the full live/specification/platform/package/release acceptance campaign after the
selected feature work; its pending gates do not take precedence over this development queue.
Acceptance requirements remain in the [release checklist](efcore-10-release-checklist.md).

## Provider gaps and current limitations

| ID | Related feature | Missing capability | Status | Scope/priority | Mode / owner layer | Closure conditions | Owning source |
| --- | --- | --- | --- | --- | --- | --- | --- |
| G-01 | QRY-07 | DateTime.AddDays(double) | Implemented | P0 priority completion | Both modes / Provider | Translator/renderer supports predicates/composition, constants/refreshed parameters/columns/NULL and signed fractional offsets; whole/fraction ticks are combined exactly before promoted timestamp arithmetic. Invalid unit ranges and MySQL sub-microsecond offsets return NULL, without silent rounding | [Temporal methods][temporal] |
| G-02 | QRY-07 | DateTime.AddHours(double) | Implemented | P0 priority completion | Both modes / Provider | Same contracts as G-01; Oracle uses positive intervals with explicit timestamp addition/subtraction to preserve negative subsecond offsets; numeric offsets do not use the MySQL TIME carrier | [Temporal methods][temporal] |
| G-03 | QRY-07 | DateTime.AddMinutes(double) | Implemented | P1 completion | Both modes / Provider | Same exact whole/fraction tick conversion, NULL/range guards, parameters/columns, composition and promoted results as G-01; no MySQL TIME carrier | [Temporal methods][temporal] |
| G-04 | QRY-07 | DateTime.AddSeconds(double) | Implemented | P1 completion | Both modes / Provider | Same contracts as G-03; signed subsecond values retain separate binary fractional multiplication before integer tick arithmetic | [Temporal methods][temporal] |
| G-05 | QRY-07 | DateTime.AddMilliseconds(double) | Implemented | P1 completion | Both modes / Provider | Same contracts as G-03; MySQL rejects offsets with sub-microsecond ticks by returning NULL | [Temporal methods][temporal] |
| G-06 | QRY-07 | DateTime.AddMicroseconds(double) | Implemented | P1 completion | Both modes / Provider | Same contracts as G-03; Oracle retains 100ns offsets and MySQL returns NULL for unrepresentable ticks | [Temporal methods][temporal] |
| G-07 | QRY-07 | DateTime.AddTicks(long) | Implemented | P1 completion | Both modes / Provider | Exact Int64 constants/refreshed parameters/nullable columns without Double conversion; offsets outside +/-DateTime.MaxValue.Ticks return NULL, including Int64 extrema without ABS overflow. Same result promotion, MySQL precision and server result-overflow contracts as G-03 | [Temporal methods][temporal] |
| G-08 | QRY-07 | DateTime.Subtract(DateTime) returning TimeSpan | Implemented | P1 completion | Both modes / Provider | Exact signed integer ticks carry full supported DateTime differences, NULL, duration members, scalar comparisons/Compare/CompareTo, CASE/COALESCE and timestamp arithmetic composition. Stored TIME/INTERVAL mappings are unchanged. Scalar dates keep promoted precision and contextual converters; mismatched date converters, custom native duration conversions, binary duration arithmetic and mixed set/membership carriers reject explicitly | [Temporal methods][temporal] |
| G-09 | QRY-07 | TimeOnly.Add(TimeSpan) | Implemented | P1 completion | Both modes / Provider | Translator and TimeOnly renderer wrap at 24 hours with signed/NULL offsets; scalar durations use exact ticks across the full TimeSpan range and accept date-difference composition. Native duration columns retain storage limits; custom converted native durations reject. Clock results promote precision and preserve contextual converters | [Temporal methods][temporal] |
| G-10 | QRY-07 | TimeOnly.AddHours(double) | Implemented | P1 completion | Both modes / Provider | Whole binary-product truncation and Int64 saturation precede 24-hour wrapping, preserving low ticks near Int64 limits. Supports constants/refreshed parameters/nullable columns/composition and promoted clock mappings; MySQL sub-microsecond results return NULL instead of rounding, Oracle retains 100ns | [Temporal methods][temporal] |
| G-11 | QRY-07 | TimeOnly.AddMinutes(double) | Implemented | P1 completion | Both modes / Provider | Same contracts as G-10, including fractional-tick truncation, adjacent saturation boundaries and exact signed wrapping for the minute unit | [Temporal methods][temporal] |
| G-12 | QRY-07 | DateTimeOffset Add/Subtract/calendar method families | In development | Extension | Both modes / Provider | `Subtract(DateTimeOffset)` returns the signed UTC instant difference using the existing tick-backed query duration. `AddTicks(long)` preserves the Oracle numeric offset and MySQL UTC storage while guarding timestamp range and precision. The remaining duration and calendar overloads still need per-signature contracts. | [Temporal methods][temporal] |
| G-13 | QRY-07 | DateOnly.FromDateTime/ToDateTime; TimeOnly.FromDateTime/FromTimeSpan/ToTimeSpan/IsBetween | Implemented | Extension | Both modes / Provider | TranslateConversion reuses date/time/tick carriers; see the feature inventory's exact signatures. FromTimeSpan accepts only [0,24h), returning NULL for invalid SQL values or MySQL sub-microsecond results; EF.Parameter scalars use full-range ticks. ToDateTime promotes timestamp precision and returns Kind.Unspecified, rejecting UTC/Local/dynamic Kind. IsBetween has inclusive/exclusive endpoints, midnight wrapping, empty equal endpoints and explicit NULL propagation; scalar bounds adopt the clock converter and mismatched column converters reject. Type-changing conversions use stored calendar space and their own result mapping | [Temporal methods][temporal] |
| G-14 | QRY-06 | Contains(char), StartsWith(char), EndsWith(char) | Implemented | Extension | Both modes / Provider | OceanBaseStringTranslator reuses native search/collation and total Boolean NULL semantics. Scalar chars compose CharToStringConverter with the receiver mapping/converter; text-backed char columns preserve their own mappings and non-text carriers reject. Existing UTF-16 validation rejects lone surrogates. Literals, refreshed parameters, nullable columns, national/fixed/LOB text and composition are covered; the existing parameter-name repair now preserves repeated mapping-specific names in both modes | [Strings][strings] |
| G-15 | QRY-06 | Explicit trim char/char[] overloads | Implemented | Extension | Both modes / Provider | OceanBaseExplicitTrimTranslator implements all six signatures as repeated character-set removal in stored text space, independently of the receiver converter. Null/empty arrays use CLR whitespace; parameterless trim retains ordinary spaces. Constants, per-execution captured arrays/in-place mutations, compiled-query array parameters and inline nullable char columns are supported without enabling mapped collections; surrogate and custom/multichar/non-text char carriers reject. Results preserve national/LOB families, variable mapping and the receiver inverse once; NULL chars propagate and Oracle CLOB trim retains native empty LOBs. Oracle uses LTRIM/RTRIM; MySQL uses escaped, case-sensitive strictly anchored regex with native NULL-propagating concatenation. Literal/parameter receivers receive default mappings and supported array shapes retain plugin dispatch. Constant-array cache keys snapshot mutations | [Explicit trim][trim] |
| G-16 | QRY-06 | Remove(int), Remove(int,int) | Implemented | Extension | Both modes / Provider | OceanBaseStringTranslator uses native character counts and stored text, with start in [0,length] and count in [0,length-start]; invalid/NULL SQL bounds or NULL source return NULL. Guarded substring/concat uses long arithmetic, variable text/national/LOB families and the receiver's inverse converter exactly once. Oracle guarded empty results are NULL, including LOBs; bare Substring retains its separate native contract. Constants/refreshed parameters/independently nullable columns/composition, fixed/national/LOB text and extreme bounds are covered | [Strings][strings] |
| G-17 | QRY-06 | StringComparison, culture/Invariant overloads | Unsupported | Current scope limitation | Both modes / Provider + Server | Lift only with a per-signature approach that preserves comparison semantics; do not assume equivalence to DB collation | [Strings][strings] |
| G-18 | QRY-06 | IndexOf(string/char), IndexOf(string/char,int), IndexOf(string/char,int,int) | Implemented | Extension | Both modes / Provider | OceanBaseStringIndexOfTranslator uses native character counts/collation, zero-based positions or -1, guarded wide start/count arithmetic, SQL NULL results for NULL inputs or invalid ranges, and contextual needle mappings. Oracle distinguishes known empty scalar needles from NULL. Constants, refreshed parameters, nullable columns and composition are covered. See G-47 for the remaining families | [IndexOf][indexof] |
| G-19 | QRY-05/03 | FirstOrDefault/SingleOrDefault/DefaultIfEmpty overloads with explicit scalar defaults | Implemented | Extension | Both modes / Provider | Row presence distinguishes an empty source from an existing NULL result. Supports mapped scalar defaults, including captured parameters. SingleOrDefault with an explicit default is limited to the terminal query so multiple rows are detected. Explicit defaults for entities and structured projections are unsupported | [Query normalization][defaults] |
| G-20 | QRY-02/03 | Custom comparers for ordering, joins, membership, and set operations | Unsupported | Current scope limitation | Both modes / Provider + Server | Add support only for specific comparers that can be translated consistently with SQL comparison rules | [Queries][roots] |
| G-21 | QRY-03/DEF-04 | Primitive collection translation using a single JSON parameter | Deferred | Related to D-04 | Both modes / Provider | Establish a separate JSON SQL/type contract; preserve existing Constant/MultipleParameters support | [Queries][roots] |
| G-22 | QRY-03 | Oracle contextual CHAR/NCHAR collection roots | Unsupported | Current scope limitation | Oracle / Provider | Preserve fixed blank-padding comparison; do not use TO_CHAR/TO_NCHAR as a supposedly equivalent fixed-length replacement | [SQL][sqlgenerator] |
| G-23 | QRY-03 | Oracle ordinary BLOB equality/membership (including mixed RAW/BLOB subqueries) | Unsupported | Current scope limitation | Oracle / Provider + Server | Define a LOB comparison approach and NULL compensation; preserve the supported bounded RAW path | [SQL][sqlgenerator] |
| G-24 | QRY-03 | Oracle CLOB/NCLOB/BLOB set projections, MINUS ALL/INTERSECT ALL | Unsupported | Current scope limitation | Oracle / Server + Provider | Confirm native capabilities of the target OB version; do not silently convert to narrower scalars or truncate LOBs to pass | [SQL][sqlgenerator] |
| G-25 | DEF-03/MAP-07 | Bulk server expressions with incompatible converters | Unsupported | Current scope limitation | Both modes / Provider | Provide equivalent dedicated translations for specific converters/expressions; do not promise algebraic equivalence for arbitrary conversions | [Bulk][bulk] |
| G-26 | UPD-02/MOD-03 | Reading back MySQL default/computed generated primary keys | Unsupported | Current scope limitation | MySQL / Provider + Server | Safely locate the new row and preserve generated keys; do not guess results using a non-unique query | [Writes][writes] |
| G-27 | UPD-02/05 | MySQL readback shapes with multiple generated keys | Unsupported | Current scope limitation | MySQL / Provider + Server | Uniquely locate/return multiple generated values and reject unsupported shapes before DML | [Writes][writes] |
| G-28 | DEF-07/06 | TPC store-generated key allocation across tables | Deferred | Related to D-06 | Both modes / Provider | Ensure uniqueness across the entire hierarchy; independent table identities are not a substitute; assigned/client Guid support is implemented | [Inheritance generated values][annotations] |
| G-29 | MOD-03/SCH-02/DEF-01 | Model trigger metadata adaptation | Implemented | Extension | Both modes / Provider | `HasTrigger` metadata is retained and accepted by table creation and migrations. Metadata-only changes emit no operations. Author trigger SQL explicitly and configure affected properties for the existing generated-value readback. See the [package contract](../../src/OceanBase.EntityFrameworkCore/README.md) for Oracle direct migration SQL | [DDL][schema], [Migrations][migrations] |
| G-30 | SCH-02/DEF-01 | Table/column comment DDL | Implemented | Extension | Both modes / Provider | Create scripts, schema creation and migrations preserve comments, with model diffs for adding, changing, clearing and reverting them. MySQL uses native COMMENT clauses and restates the column definition when modifying it. Oracle uses separate COMMENT ON statements. NUL and mode-dependent MySQL backslashes are rejected during generation | [DDL][schema], [Migrations][migrations] |
| G-31 | SCH-02/DEF-01 | Database collation changes | Not started | Extension | Both modes / Provider + Server | Define valid DDL and its impact according to namespace ownership and the target OB version | [Migrations][migrations] |
| G-32 | SCH-02/DEF-01 | Oracle column collation DDL | Unsupported | Current scope limitation | Oracle / Provider + Server | First confirm target version/type support; do not ignore the configuration | [DDL][schema] |
| G-33 | SCH-02/DEF-01 | Filtered index DDL | Unsupported | Current scope limitation | Both modes / Provider + Server | Provide an equivalent approach supported by the target OB version; Filter is currently explicitly rejected | [DDL][schema] |
| G-34 | DEF-01 | Automatic alteration of identity/computed columns | Not started | Extension | Both modes / Provider | Provide a safe rebuild/migration approach that prevents data loss; do not silently discard changes | [Migrations][migrations] |
| G-35 | DEF-01 | Table migration across namespaces | Unsupported | Current scope limitation | Both modes / Provider | Establish explicit permission/ownership and data migration contracts; ordinary rename is implemented | [Migrations][migrations] |
| G-36 | DEF-02 | Reverse engineering of expression/prefix and other non-ordinary indexes | Blocked | Extension | Both modes / Provider + Connector metadata | MySQL BTREE prefix indexes preserve per-column lengths, order and uniqueness in C# and DDL. Connector 0.3.1 does not expose expression text through IndexColumns. Close the remaining gap after the public catalog supplies exact member expressions and the facts needed to distinguish and recreate the selected index kinds | [Reverse engineering][scaffold] |
| G-37 | DEF-02 | Reverse engineering of known ON UPDATE column behavior for supported mappings | Implemented | Extension | MySQL / Provider | Preserve catalog `ON UPDATE CURRENT_TIMESTAMP[(p)]` on non-key DATETIME columns in scaffolded C#, write readback, and DDL. Use update-only generation unless the catalog establishes an insert default. Required additions without an insert default require an explicit backfill. Oracle metadata is unknown for this behavior. See G-50 for MySQL TIMESTAMP | [Reverse engineering][scaffold], [DDL][schema] |
| G-38 | DEF-02 | Reverse engineering of foreign key ON UPDATE actions | Unsupported | Current scope limitation | Both modes / Provider + Server | Establish a contract expressible by the EF model and target OB version; currently only NO ACTION/RESTRICT are accepted | [Reverse engineering][scaffold] |
| G-39 | QRY-07 | Math/MathF.Acosh | Implemented | Extension | Both modes / Provider | Translates both signatures for finite stored inputs at or above 1 with Double intermediates and existing result normalization. SQL NULL and finite inputs below 1 produce SQL NULL | [Math methods][math] |
| G-40 | TXN-01/DEF-09 | Provider guarantees for ambient/distributed transactions | Unsupported | Current scope limitation | Both modes / Provider + Connector | Establish public driver enlist/XA capabilities together with EF orchestration; native transactions do not imply distributed support | [Transactions][transactions] |
| G-41 | QRY-07 | DateTimeOffset.Now | Unsupported | Current scope limitation | Both modes / Provider | First define the contract between CLR local offset and the database clock/session offset; existing UtcNow support does not establish local Now support | [Temporal methods][temporal] |
| G-42 | QRY-07/MAP-05 | MySQL DateTimeOffset.DateTime/Ticks/Offset/TotalOffsetMinutes | Unsupported | Current storage contract limitation | MySQL / Provider + Server | Current DATETIME storage uses UTC and cannot recover the original offset; require a separate lossless storage approach, without changing the existing UTC contract to claim support | [Temporal mapping][temporalmap] |
| G-43 | MOD-03/UPD-03/SCH-02 | Database-generated rowversion/IsRowVersion columns | Unsupported | Current scope limitation | Both modes / Provider + Server | Define an automatic version-column mechanism available on the target OB version and its readback; ordinary manual concurrency tokens are implemented | [DDL][schema] |
| G-44 | MOD-01/SCH-02/DEF-01 | ON DELETE SET DEFAULT foreign key DDL | Unsupported | Current scope limitation | Both modes / Provider + Server | Lift only when supported by the target OB version and equivalent to EF delete behavior; do not silently downgrade to RESTRICT | [DDL][schema] |
| G-45 | MOD-03/SCH-02/DEF-01 | Oracle stored computed columns | Unsupported | Current scope limitation | Oracle / Provider + Server | Distinguish supported virtual computed columns from physically stored computed columns; first confirm target OB capabilities | [Migrations][migrations] |
| G-46 | QRY-04 | Oracle CLOB/NCLOB aggregate Min/Max | Unsupported | Current scope limitation | Oracle / Provider + Server | Establish lossless LOB comparison/return-type contracts; preserve bounded text/temporal extrema | [Aggregates][aggregates] |
| G-47 | QRY-06 | Replace(string,string), Replace(char,char) | Implemented | Extension | Both modes / Provider | Native literal replacement in stored text. NULL source or invalid old token returns SQL NULL. NULL/empty new string deletes matches and NULL char operands propagate. Variable national/LOB result families retain the receiver inverse once. Expanded strings and LOBs, captured tokens, nullable columns and composition are covered | [Strings][strings] |
| G-48 | QRY-06 | Insert(int,string) | Implemented | Extension | Both modes / Provider | Insert at a native character boundary in [0,length] with wide arithmetic. NULL input or an invalid index yields SQL NULL. Known empty scalar values leave the source intact, including in Oracle. Plain contextual values and variable national/LOB results preserve stored text behavior and the receiver inverse | [Strings][strings] |
| G-49 | QRY-06 | PadLeft(int/int,char), PadRight(int/int,char) | Implemented | Extension | Both modes / Provider | Width counts native characters, with no truncation. Negative or NULL width, NULL source and NULL char operands yield SQL NULL. Oracle generates single-width spaces before substituting the requested char and concatenating with the source. Variable national/LOB mappings retain the receiver inverse | [Strings][strings] |
| G-50 | MAP-05/DEF-02 | MySQL TIMESTAMP mapping and scaffolding | Unsupported | Current scope limitation | MySQL / Provider | The MySQL DateTime mapping accepts DATETIME and rejects TIMESTAMP. Establish session time zone conversion, native range and precision, and tracked write/readback behavior before enabling the mapping and extending G-37 to TIMESTAMP | [Temporal mapping][temporalmap] |

## Implemented entries

Retain only stable gap IDs and closure results; do not append development logs.

| ID | Related feature | Capability | Status | Implementation location/commit | Owner | Closure result | Code |
| --- | --- | --- | --- | --- | --- | --- | --- |
| F-01 | QRY-07 | DateTime.Add(TimeSpan) | Implemented | `f119f51` | Both modes / Provider | Production translator/renderer supports predicates, columns/parameters/constants/NULL, and result precision promotion; record evidence only in the release checklist | [Temporal methods][temporal] |
| F-02 | QRY-07 | DateTime.Subtract(TimeSpan) | Implemented | `f119f51` | Both modes / Provider | Same as F-01; negative durations, fractional DATE results, and exactly one execution of the inverse converter | [Temporal methods][temporal] |

## Connector / server dependencies

The following historical failures have direct Connector/SQL comparisons; recheck the current pinned dependency identity against the release checklist.
The Provider must not bypass them by changing the wire protocol, narrowing mappings, silently converting to text, or swallowing errors. Explain driver changes first and wait for user confirmation.

| ID | Related feature | Mode | Status | Issue and conditions for resolution | Owner |
| --- | --- | --- | --- | --- | --- |
| B-01 | MAP-02 | Oracle | Blocked | Extreme Double parameters return 1426, and tested tiny values become zero; default/explicit BINARY_DOUBLE, Prepare, and Precision17 did not resolve this. The same D literals retain fidelity; locate/fix the issue in the owning layer, then rerun with the same parameters | Connector/Server; root cause undetermined |
| B-02 | QRY-04 | MySQL | Blocked | SUM/AVG with default DECIMAL(65,28), including zero compensation for empty sets, are rejected at public reader metadata; require correct result transport preserving the default precision | Connector/Server result boundary |
| B-03 | QRY-07 | MySQL | Blocked | Default Decimal scalar LEAST/GREATEST encounter the same metadata rejection; success with explicitly narrower precision does not close the default profile | Connector/Server result boundary |
| B-04 | SQL-02/QRY-10 | MySQL | Blocked | Repeated positions of caller-owned native parameters require a public clone/ownership contract; currently no cloning occurs and these are explicitly rejected; independent parameters work | Connector public API + EF leases |
| B-05 | MAP-03 | Oracle | Blocked | NCLOB CREATE on target 4.4.2.2 returns 902 through both EF and direct execution; require a supported version/declaration, without substituting CLOB storage to claim completion | Server |
| B-06 | MAP-03/04 | MySQL | Blocked | LOB DEFAULT returns 1067 on the target; bounded VARBINARY and other existing supported types remain supported; require target server capabilities or an explicit limitation | Server |
| B-07 | SQL-01 | Oracle | Blocked | Identifiers containing embedded double quotes return 600 for EF/direct CREATE or SELECT; backtick-only/ordinary name controls succeed; do not change correct escaping to conceal server behavior | Server; recheck by version |
| B-08 | QRY-03 | MySQL | Blocked | Collection roots associated with bounded/fixed text properties produce collation error 1267; require an equivalent collation approach, without truncating through a narrower CAST or changing comparison rules | Provider SQL + Server |
| B-09 | SQL-02/DEF-06 | MySQL | Unsupported | Current Provider command adaptation supports only Input; repeated streams also cannot be safely reused; Output/InputOutput/ReturnValue require public driver contracts and explicit Provider integration | Provider + Connector |

## Capabilities to confirm

To confirm does not mean unimplemented. After investigation, move the item to G (a code gap), the release checklist (only evidence is missing), or Implemented; do not leave an ambiguous status indefinitely instead of investigating.

| ID | Related feature | Status | Capability to confirm and next step |
| --- | --- | --- | --- |
| C-01 | DEF-08 | To confirm | NativeAOT/precompiled queries: separately check EF10 generation/runtime restrictions and Provider entry points; do not infer support from ordinary compiled model results |
| C-02 | MAP-05/QRY-07 | To confirm | Named timezones/DST/non-Gregorian calendars and TIMESTAMP WITH LOCAL TIME ZONE: first check mapping/semantic contracts; numeric offset evidence is insufficient to determine completeness |
| C-03 | DEF-02 | To confirm | Scaffold fidelity for Oracle identity modes/options: check GetSchema facts and the generated model to determine whether metadata is missing or only live database evidence is absent |
| C-04 | DEF-08/QRY-08 | To confirm | Check whether change-tracking proxies, lazy proxies with compiled models, and broader inheritance combinations have dedicated rejections or missing adaptations; nested lazy loading within ordinary streaming roots is explicitly excluded by the single active reader, so do not infer implementation gaps from missing tests |

## Four groups explicitly deferred for the current release

| ID | Related feature group | Status | Unavailable functionality | Conditions for lifting deferral |
| --- | --- | --- | --- | --- |
| D-04 | DEF-04 | Deferred | Mapping/query/update for JSON, mapped primitive collections/arrays, and complex/JSON-owned types | Separate native type/SQL contracts and implementation; captured collection IN parameters are already supported |
| D-05 | DEF-05 | Deferred | Provider integration for Spatial, XML, native enum, OBJECT, LOB locators, and REF CURSOR | Define Connector public capabilities and OceanBase type contracts; ordinary enum/CLOB/BLOB scalars are outside this group |
| D-06 | DEF-06 | Deferred | Stored procedure INSERT/UPDATE/DELETE mapping, sequence/HiLo, and TPC allocation strategies | Design parameter/return-value/key strategies; preserve current rejecting entry points |
| D-10 | DEF-10 | Deferred | Full-text/vector/search functions and advanced batching optimizations | Target server versions, separate feature implementations, and performance goals |

Do not remove data precision/NULL/ownership validation or intentional rejections while completing features. Update feature group, gap, and acceptance statuses separately.

[writes]: ../../src/OceanBase.EntityFrameworkCore/Update/OceanBaseModificationCommandBatch.cs
[schema]: ../../src/OceanBase.EntityFrameworkCore/Storage/OceanBaseSchemaSqlGenerator.cs
[sqlgenerator]: ../../src/OceanBase.EntityFrameworkCore/Query/OceanBaseQuerySqlGenerator.cs
[roots]: ../../src/OceanBase.EntityFrameworkCore/Query/OceanBaseQueryableMethodTranslatingExpressionVisitorFactory.cs
[defaults]: ../../src/OceanBase.EntityFrameworkCore/Query/OceanBaseQueryTranslationPreprocessorFactory.cs
[strings]: ../../src/OceanBase.EntityFrameworkCore/Query/OceanBaseStringTranslator.cs
[indexof]: ../../src/OceanBase.EntityFrameworkCore/Query/OceanBaseStringIndexOfTranslator.cs
[trim]: ../../src/OceanBase.EntityFrameworkCore/Query/OceanBaseExplicitTrimTranslator.cs
[temporal]: ../../src/OceanBase.EntityFrameworkCore/Query/OceanBaseTemporalMethodTranslator.cs
[temporalmap]: ../../src/OceanBase.EntityFrameworkCore/Storage/OceanBaseTemporalTypeMapping.cs
[transactions]: ../../src/OceanBase.EntityFrameworkCore/Storage/OceanBaseRelationalTransactionFactory.cs
[migrations]: ../../src/OceanBase.EntityFrameworkCore/Migrations/OceanBaseMigrationsSqlGenerator.cs
[scaffold]: ../../src/OceanBase.EntityFrameworkCore/Design/OceanBaseDatabaseModelFactory.cs
[bulk]: ../../src/OceanBase.EntityFrameworkCore/Query/OceanBaseBulkDmlVisitor.cs
[annotations]: ../../src/OceanBase.EntityFrameworkCore/Metadata/OceanBaseRelationalAnnotationProvider.cs
[math]: ../../src/OceanBase.EntityFrameworkCore/Query/OceanBaseMathTranslator.cs
[aggregates]: ../../src/OceanBase.EntityFrameworkCore/Query/OceanBaseAggregateMethodTranslator.cs
