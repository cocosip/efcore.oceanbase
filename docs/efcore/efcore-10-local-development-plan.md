# EF Core 10 Local Development Plan

**Goal:** Implement every locally developable EF10 feature, in reviewed/tested commits, while postponing real
database integration and EF8/EF9 adaptation as explicitly requested on 2026-11-10.

**Architecture:** Continue on efcore/10.0 from 57fd44a. Reuse the existing EF relational services, connector 0.2.0
public APIs and per-mode mappings/SQL. Separate local implementation evidence from server acceptance.

**Spec:** efcore-10-feature-checklist.md, efcore-10-technical-baseline.md, efcore-10-query-update-design.md.
**Tech Stack:** C#, .NET 10, EF Core/Relational [10.0.0,11.0.0), xUnit, OceanBaseConnector 0.2.0.

## Global constraints

- Develop and commit on efcore/10.0. Do not push, publish, tag or synchronize other branches implicitly.
- No real database connections, live fixtures or destructive database tests in this execution phase.
- EF8/EF9 work remains postponed. Local passing evidence never completes a required live gate.
- Preserve every Required feature ID. Track local coverage separately instead of weakening acceptance criteria.
- For each batch: specify behavior, add tests (RED for missing behavior), implement, run full tests, review,
  fix confirmed issues, update progress/checklist and commit. Existing correct behavior gets characterization tests.
- Use independent reviews for each batch. Workers must not spawn additional workers or commit unreviewed code.
- Keep docs portable; no credentials or machine-specific paths. No new dependency for a small helper.

## Work inventory and order

This is an execution ledger, not a replacement support matrix. A checked local task can still have open live gates.
Do not mark the goal complete while any locally developable item below remains unresolved.

| Batch | Feature IDs / owning code | Local completion evidence |
| --- | --- | --- |
| 1. Configuration and SQL boundaries | CFG-01/02/03, SQL-01/02; OptionsTests, ConsumerTests, SqlGenerationHelperTests | Both modes/overloads; null/invalid options; deferred connection configuration; real cache separation/reuse and design-time keys; identifier escaping/schema and both StringBuilder paths. |
| 2. Remaining scalar contracts | MAP-01..07; Storage mappings and focused unit/pipeline tests | Oracle decimal aliases; remaining explicit numeric forms; complete converter/null/clone/input/output boundaries and documented unsupported combinations. Environment-dependent ranges/byte capacity remain separate. |
| 3. Query translator services | QRY-01/03/04/06/07/11; Query translators and SQL generator | Supported string/date/numeric overload tables, concat/arithmetic/null semantics, negative translation tests, collection membership, correlated subqueries and aggregates. |
| 4. Model and materialization | MOD-01/02/03, QRY-02/05/08/09; conventions, validator, model/query tests | Composite/alternate keys, relationships/owned/shadow, cardinality, tracking/identity resolution, Include, filters and generation validation through real EF pipelines. |
| 5. Tracked writes and diagnostics | UPD-01..05, DIA-01/02, CON-05; Update batch, command tests | Composite/nullable concurrency, graphs, generated/default/computed output shapes, failures/entries/inner errors, cancellation/timeout and protected parameter logging. |
| 6. Transactions and lifecycle | TXN-01..03, CON-02..05; relational connection/transaction services | Local API/service wiring and deterministic command/transaction doubles for ownership, enlistment, atomic failure behavior and savepoints; server semantics explicitly pending. |
| 7. Database/schema services | SCH-01..03, CFG-04; database creator and DDL services | Define ownership/name/permission boundaries, mode-specific SQL and exception classification, existence/create/delete/schema scripts using deterministic doubles, no real DDL. |
| 8. Specification and package gates | TST-01, PKG-01..04, REL-01/02 | Selected EF relational specification fixtures locally; documented exclusions; repeatable nupkg identity/content/range gate, README, expanded package consumers and selected patch matrix. |
| 9. Deferred inventory audit | DEF-01..10 | Revisit every documented deferred family after Required local implementation stabilizes. Implement locally feasible families under explicit per-mode contracts and tests; record precise architectural/connector/server blockers for any unimplementable family. Do not treat a Deferred label alone as evidence it cannot be developed. |
| 10. Completion audit | All IDs | Compare docs/public API/source/tests/package evidence. Every locally feasible feature is implemented/reviewed/committed; only specifically evidenced database or external blockers may remain. |

## Batch 1 brief

**Files:** OptionsTests.cs; SqlGenerationHelperTests.cs; ConsumerTests/ProviderConfigurationTests.cs;
production options/cache/helper code only if a demonstrated defect needs fixing.

**Behavior:** Explicit modes are preserved for both generic/untyped overloads; invalid modes/null builders/null
connections reject. A null connection string may configure services/models and be filled later using standard
EF SetConnectionString; connecting without a string must fail clearly without network access. Different connection
strings in one mode reuse compatible EF services/models; MySQL and Oracle never share a model/service graph.
Design-time and runtime cache keys differ. Identifier escaping must double embedded per-mode delimiters, qualify
schema and preserve empty/Unicode names consistently in return-string and StringBuilder overloads. Consumers
identify the provider in both modes. Do not claim full CFG-04 until database creator services exist.

1. Read the connected production APIs and existing tests; enumerate positive/negative/cache/escaping cases.
2. Add characterization tests for existing contracts. If a bug appears, isolate it with a failing test before fixing.
3. Run the focused tests, then full Debug/Release suites. Record counts, commands and remaining gaps.
4. Independent read-only review; address confirmed issues with regression tests. Update feature evidence and commit.

## Review focus

- Mode-specific singleton factories must not capture a different context's mode through cache reuse.
- Connection strings must not leak into cache/log/debug evidence or demand network access during model building.
- EF parsed facets/converters must not bypass the provider's validation or alter query operand semantics.
- SQL null/cardinality/string/temporal semantics must not inherit SQL Server-only syntax.
- Local transaction/schema doubles prove client control flow, not connector or server execution.

## Batch 2a brief: Oracle fixed-point aliases

**Files:** Storage/OceanBaseDecimalTypeMapping.cs, Storage/OceanBaseIntegerTypeMapping.cs;
UnitTests/DecimalMappingTests.cs and ExplicitIntegerMappingTests.cs; FunctionalTests/BasicRelationalTests.cs.
The updated decimal contract in the technical baseline is authoritative.

1. RED: Oracle bare DECIMAL and NUMERIC expose precision 38, scale zero, preserve trimmed declarations,
   output metadata and composed converters/clones; reject fractional current decimal writes. Bare NUMBER
   still accepts fractional decimals and leaves precision/scale unspecified. Query/InputOutput inputs through
   fixed aliases retain value inference even when fractional. Configured precision/scale regenerate explicit SQL.
   Integer mappings using bare DECIMAL/NUMERIC expose precision 38, zero scale, while configured precision
   overrides the alias default by generating an explicit declaration. Test bool/enum converter paths as applicable.
2. Implement the smallest shared default-selection fixes; no transport fork, aliases outside existing scope,
   CLR floating-point expansion or dependency changes in this sub-batch.
3. Add intercepted EF sync/async query/write tests for decimal aliases, including converted current values,
   pre-command rounding rejection, lossless query operands and unqualified NUMBER independence.
4. Run focused RED/GREEN and full Debug/Release; independent review and commit with progress/evidence.
   Later selected-patch/package verification must run after the production change before closing package evidence.

## Batch 2b brief: temporal facet and clone validation

**Files:** Storage/OceanBaseTypeMappingSource.cs, Storage/OceanBaseTemporalTypeMapping.cs;
UnitTests/TemporalMappingTests.cs and TemporalQueryMappingTests.cs as needed. Preserve existing query/write
transport and converters; no new temporal types, server ranges or dependencies.

1. RED: both modes and all five temporal CLR types reject size and fixed-length=true facets through direct
   resolution and model properties. Clones reject incompatible/cross-mode store declarations, suffixes,
   precision conflicts and size/fixed facets rather than producing mismatched SQL/DbType/converter metadata.
   Include a converter-composed mapping and Oracle DATE/interval/zoned representations in clone boundaries.
   Distinguish EF's parsed declaration Size from actual length configuration; property GetMaxLength must reject
   even when it equals parsed precision. Limit clone declarations to the current transport family, so DATE,
   unzoned/zoned timestamps and intervals cannot retain stale DbType or converter semantics after a family change.
2. Add the smallest validation consistent with the existing temporal declaration parser. Keep legal unchanged
   clones, converter composition and precision changes working; generated declaration, Precision and output
   Scale/leading metadata must agree. Retain wide Input/InputOutput query precision after precision reduction.
3. Focused RED/GREEN, full Debug/Release and independent review. Record actual failing checks and exact counts,
   update MAP-05/06/07 evidence and commit. Live temporal/session/calendar acceptance remains open.

## Batch 2c brief: exact integer normalization and scalar gaps

**Files:** Storage/OceanBaseNumericConversion.cs; UnitTests/ScalarMappingTests.cs;
FunctionalTests/BasicRelationalTests.cs. ExplicitIntegerMappingTests.cs and OceanBaseTypeMappingSource.cs only
if a property-facet regression actually fails. Keep the existing type profile and public API.

1. RED: finite Single/Double fractions must not round into any of the eight integer CLR types, including
   converter-backed readers and generated outputs. Use the real mapping reader path and intercepted EF output
   propagation. Preserve exact integral conversions, checked range/NaN/infinity failures and floating CLR readers.
2. Fix the shared normalization boundary, retaining decimal/OceanBaseDecimal behavior and avoiding a second
   generated-output conversion path. Characterize missing nullable integer endpoints, Boolean false/null and
   Guid.Empty cells using existing helpers instead of duplicating the established scalar suite.
3. Probe explicit Oracle integer property precision conflicts through actual model/property mapping. If already
   rejected, retain only useful characterization; if not, record RED and fix at the shared validation boundary.
4. Focused RED/GREEN, full Debug/Release, independent review and scoped commit. Repeat current package/selected
   patch evidence after the scalar production changes before completing Batch 2 locally.

## Batch 3a brief: string concatenation and query boundaries

**Files:** Query/OceanBaseQuerySqlGenerator.cs; scoped query/parameter processor code only when regression
evidence requires it; UnitTests/SqlGeneratorTests.cs (or a focused StringQueryTests.cs);
FunctionalTests/BasicRelationalTests.cs. Follow the concatenation contract in the query/update design.

1. RED: C# string addition and fixed Concat(2/3/4 strings) use native syntax, numeric addition remains `+`.
   Cover nested/constant/parameter/nullable columns, both modes, predicates/projections and independent contexts.
2. Fix SQL generation while retaining MySQL null protection. Probe Oracle nullable projections and null-sensitive
   predicates against the documented native empty/NULL result rule; fix incorrect nullable metadata or reject a
   specific unsupported shape explicitly. A SQL-shape pass must not hide a known nullability optimization bug.
   RED includes concat IS NULL/IS NOT NULL, one/both nullable inputs, concat comparisons/NOT, nested Concat,
   COALESCE to a nonempty fallback and to empty, and the same query with nonempty/empty/null captured parameters,
   under both null-semantics settings. Correct this before EF's nullability optimization; changing a binary
   expression's nullable flag alone is insufficient because EF may decompose its null test as operand OR.
3. Characterize equality/inequality/string Equals, default/relational null semantics, negative StringComparison
   and custom predicates, and allowed top-level client projections. Reuse EF behavior where it is correct.
4. Add sync/async intercepted parameter/materialization evidence, full Debug/Release, independent review and
   scoped commit. QRY-06 stays partial; later search/member/substring/case/trim slices still need their own matrix.

## Batch 3b brief: numeric division and remainder

**Files:** Query/OceanBaseQuerySqlGenerator.cs; a focused UnitTests/ArithmeticQueryTests.cs;
FunctionalTests/BasicRelationalTests.cs. The arithmetic contract in the query/update design is authoritative.
Do not replace the expression factory or general query pipeline for a SQL-rendering correction.

1. RED: mapped integer division emits MySQL DIV and Oracle TRUNC(a / b), while Oracle remainder emits MOD(a,b).
   Include signed int/long, promoted small integers, uint/ulong, nullable expressions, negative operands,
   captured divisors and a value-preserving numeric converter. Decimal/Single/Double and explicit fractional
   casts retain ordinary division, even when Oracle storage is NUMBER. Preserve numeric +,-,* and unary minus.
2. Implement the smallest generator correction. Preserve grouping for a/(b+c), a/(b/c), nested remainder,
   multiplication around division and predicate comparisons. Keep inferred mappings/null propagation;
   do not use store declaration names as an integer classifier. No bitwise or Math/Random expansion here.
3. Add sync/async intercepted queries for parameter binding and nullable/non-null materialization in both modes.
   Supply synthetic results honestly: these prove no server arithmetic or predicate-membership results.
4. Focused RED/GREEN, full Debug/Release, independent review, selected-patch/package checks, documentation
   and scoped commit. QRY-01 remains partial until wider operators and live acceptance pass.

## Batch 3c brief: unary string functions and character length

**Files:** Query method/member translator providers and focused string translators; Internal/OceanBaseOptionsExtension.cs;
UnitTests/StringFunctionQueryTests.cs; FunctionalTests/BasicRelationalTests.cs. Follow the planned unary string
contract in the query/update design and pinned research in artifacts/local-development/query-functions-design.md.

1. RED: exact parameterless ToLower/ToUpper/Trim/TrimStart/TrimEnd and string.Length translate in predicates,
   projections and order/group expressions with mapped columns. Cover nullable inputs, empty/null constants
   and captured inputs, nested concat, CASE/coalesce, function IS NULL/NOT NULL and both null-semantics options.
   Oracle all-space trim can produce NULL even from a required column; preserve result null tests.
2. Register derived RelationalMethodCallTranslatorProvider/RelationalMemberTranslatorProvider with standard
   AddTranslators, preserving plugins/model DbFunctions and opposite-mode cache separation. Reuse the SQL
   expression factory/default mapping APIs; no new dependency or general query visitor replacement.
3. Use exact overload/member matching and conservative nullable metadata. Character length means database
   characters; trim means SQL blank space; casing means database rules. Reject unsupported culture/Invariant,
   trim-char/char-array and unrelated same-name members in predicates; preserve allowed client projections.
   Check bounded/fixed/national text and a connected string converter without discarding conversion metadata.
4. Add both-mode sync/async intercepted parameter/read checks with synthetic string/nullable int results.
   Focused RED/GREEN, full Debug/Release, independent review, patch/package evidence and scoped commit.
   Search/substring, exact UTF-16/Unicode-whitespace guarantees and server results remain separate work.

## Batch 2d follow-up brief: wide exact decimal to floating normalization

Run after the active Batch 3c worker has stopped. This is a confirmed remaining local scalar gap, not a
database integration task. Requirements are the planned normalization paragraph in the technical baseline.
The package-only reproduction is artifacts/local-development/floating-decimal-probe/ (six genuine failures
against the arithmetic package after correcting an initial probe-only namespace compilation ambiguity).

**Files:** Storage/OceanBaseNumericConversion.cs; UnitTests/FloatingPointMappingTests.cs;
FunctionalTests/BasicRelationalTests.cs. Do not add new store mappings, converters, dependencies or numeric types.

1. RED through actual mapping-reader/generated-output paths: OceanBaseDecimal 1E30, a 38-digit fraction and
   1E-40 target both float/double without CLR decimal overflow. Include positive/negative/zero values, normal,
   subnormal/underflow and target overflow boundaries, extreme scales and a converter-composed reader.
2. Fix the shared conversion boundary using public connector APIs/BCL. Round directly to the requested IEEE
   target; avoid a float-via-double double-rounding path. Finite conversion to infinity rejects. Preserve exact
   integer/fraction/range and decimal semantics, existing native non-finite reader behavior and narrowing checks.
   Include an exact Single midpoint regression: 1 + 2^-24 + 10^-60 must round to the next float above 1,
   whereas converting through Double first can incorrectly round to 1. Assert the expected target bits/value.
3. Add intercepted sync/async floating reads and generated outputs with synthetic wrapper values, reusing
   existing helpers. These prove shared normalization rather than new wire/server type claims.
4. Focused RED/GREEN, full Debug/Release, independent review, patch/package gates, progress and scoped commit.
   Check the actual connector 0.2.0 nuspec source revision rather than relying on connector repository HEAD.

## Batch 3d brief: string search with safe empty-needle handling

Run after Batch 3c and the confirmed Batch 2d normalization follow-up. **Files:** existing string method
translator/provider, scoped Oracle string processor/generator only if required, focused StringSearchQueryTests.cs,
and FunctionalTests/BasicRelationalTests.cs. The planned search contract in query/update design is authoritative.

1. RED exact Contains(string)/StartsWith(string)/EndsWith(string) over mapped nullable/required columns,
   constants, parameters and column needles; wildcard-like characters are ordinary data. Include nonempty,
   empty/null, long/repeated needles, negatives, bool projections and default/relational nulls.
2. Reuse existing translator services and fixed SQL function names. MySQL dynamic empty branch uses character
   length. Oracle known constant/captured empty must become source IS NOT NULL before normalization erases
   its identity; a null needle produces false. Expand any private marker before base nullability, preserving normal
   DbFunction behavior and raw caller values. Oracle column/computed empty needles already collapsed to NULL
   produce false. Guard source/needle and return a total Boolean for all three operations: NULL input is false,
   NOT is true, under both null settings. Bound nullable suffix equality with guards/CASE so EF both-NULL
   compensation cannot invent a suffix match; too-long Oracle suffix NULL also yields false.
3. Test changing one cached query between nonempty/empty/null, composition with concat/unary functions/CASE/
   COALESCE, both-mode context isolation and unsupported char/comparison overloads. No LIKE escaping helper,
   user-derived SQL fragments or general pipeline replacement. Retain conservative function nullability.
4. Add sync/async intercepted binding/materialization/parameter-change cases, focused RED/GREEN, full
   Debug/Release, independent review, patch/package gates, docs and scoped commit. Server collation/padding/
   substring results remain live gates; exact CLR exception/ordinal semantics are not promised.

## Batch 3e brief: Substring and index adjustment

**Files:** Query/OceanBaseStringTranslator.cs; UnitTests/StringSubstringQueryTests.cs;
FunctionalTests/BasicRelationalTests.cs, plus obsolete substring rejection assertions only if present.
Follow the planned Substring contract in query/update design; retain the current converter convention.

1. RED exact Substring(int) and Substring(int,int) through mapped predicates/projections in both modes/null
   settings. Cover constants/captures/columns, starts 0/-1/-2/int.MinValue/int.MaxValue, lengths -1/0/MaxValue,
   nullable results, CASE/coalesce/concat/unary/search composition and unsupported signatures/nontext mappings.
2. Use long-result origin adjustment before any constant evaluation. Assert Type/mapping and positive
   2147483648 at the maximum; preserve raw captured/column mapping. No new cast rendering or generic visitor.
3. Native range/character semantics apply uniformly. Preserve receiver result mapping/converter and positive
   upper-bound Size across fixed/national/LOB families, including lowercase declarations. NCLOB native behavior
   is an unverified extension, not established capability. Preserve independent function NULL production.
4. Add sync/async intercepted parameter/result-conversion cases in both modes. Carry the Batch 3d review minor:
   directly cover Oracle computed CASE/COALESCE needle collapsing to NULL, positive/NOT/projected searches.
5. Focused RED/GREEN, full Debug/Release, independent code/docs review, selected-patch/same-package gates
   and scoped commit. Server contents, invalid-range outcomes, characters, padding/LOB and hosted execution
   for new source remain distinct acceptance gates. No live work or EF8/9 adaptation.

## Batch 3f brief: ordinary temporal members

**Files:** Query/OceanBaseTemporalMemberTranslator.cs (new), OceanBaseMemberTranslatorProvider.cs,
OceanBaseQuerySqlGenerator.cs; UnitTests/TemporalMemberQueryTests.cs and FunctionalTests/BasicRelationalTests.cs.
The planned ordinary temporal-member contract in query/update design is authoritative.

1. RED the twelve exact DateTime/DateOnly/TimeOnly members in both modes/null settings, mapped predicates,
   projections/order/group and composition. Preserve plugin precedence/scoped services/opposite-mode isolation.
2. Use MySQL native functions and a narrow Oracle builtin marker with fixed allowlisted unit fragment and
   visited operand, rendered as EXTRACT(unit FROM input). Only Oracle DATE clock parts cast to TIMESTAMP(7).
   SECOND extracts as decimal then one-argument TRUNC; final result mapping is int without source converter.
3. Validate existing temporal carriers/families without stripping built-in/custom temporal conversions.
   Reject unrelated string/binary/numeric/zoned carriers and unsupported members. Preserve nullable function
   result tests and captured parameter precision; no SQL grammar interpolation or general visitor replacement.
4. Add both-mode sync/async intercepted Int32 comparison/nullable reader/Oracle NUMBER wrapper checks,
   with controlled temporal inputs where present. No synthetic server extraction, clock/cast or calendar claim.
5. Focused RED/GREEN, full Debug/Release, independent code/docs review, selected-patch/package gates and
   scoped commit. Clocks/offset/fractional/Add/duration members and broader numeric queries remain separate.

## Batch 3g brief: numeric Abs, Sign and Truncate

**Files:** Query/OceanBaseMathTranslator.cs (new), OceanBaseMethodCallTranslatorProvider.cs,
UnitTests/MathQueryTests.cs (new) and FunctionalTests/BasicRelationalTests.cs. Root owns docs/package consumer.
The planned numeric contract in query/update design defines the exact twenty-two methods and native limits.

1. Genuine RED for every exact signature in mapped predicates/order/projections, both modes/null settings,
   nullable composition and result NULL tests. Cover negative fractions, signed/decimal endpoints, zero and
   finite wide floating values. Top-level client evaluation and synthetic results cannot prove translation
   or server arithmetic.
2. Reuse strict numeric mapping validation as a probe, preserving operand mappings/converters and full query
   ranges. Reject nonnumeric/null-converting carriers; permit accepted numeric BOOL/BOOLEAN aliases. Pin
   SIGN's fresh int mapping and TRUNCATE's independent int zero. Preserve plugin/DbFunction/mode precedence.
3. Add intercepted sync/async captures/readers in both modes, including numeric wrappers, converters and wide
   floats. Native ABS/SIGN/TRUNCATE/TRUNC domain, overflow and result metadata remain live acceptance gates.
4. Focused GREEN, full Debug/Release, independent code/docs review, isolated later-patch and same-package
   consumer/content gates, then scoped commit. No new dependency, generator, visitor or public configuration.

## Batch 3h brief: integral membership and ordinary relational shapes

**Files:** Storage/OceanBaseIntegerTypeMapping.cs only for a reproduced metadata repair;
UnitTests/RelationalShapeQueryTests.cs (new) and FunctionalTests/BasicRelationalTests.cs.
The planned relational-shape contract in query/update design defines this bounded QRY-03/04 slice.

1. Characterize ordinary inner/left equijoins, composite numeric keys, ON/WHERE placement, correlated/root/
   projected Any/All, ordered nullable scalar FirstOrDefault and queryable scalar Contains in both modes.
2. Reproduce and repair captured int/int? array/list mapping for bare Contains, reusing EF's element JSON
   metadata and scalar parameter expansion. Preserve converters/readers/ranges and distinguish default,
   Constant, context Parameter fallback and unsupported explicit EF.Parameter(collection).
3. Cover empty/null/all-null/duplicate collections, nullable items/negation/projections, both null settings,
   repeated changed captures and padding bucket boundaries without mutating caller arrays or stale SQL.
4. Characterize Count/LongCount grouped/ungrouped/predicate/HAVING/distinct/nullable cases. Add sync/async
   intercepted current parameter/nullable-right/int/long/overflow reads, clearly labeled as supplied results.
5. Focused/full tests, independent code/docs review, selected-patch and same-package gates, then scoped commit.
   Text lists, general VALUES, APPLY, SUM/AVG/Min/Max promotion and actual server results stay separate work.

## Batch 3i brief: scalar text collection membership

**Files:** Storage/OceanBaseStringTypeMapping.cs, Query/OceanBaseCollectionParameterExpandingVisitor.cs (new),
OceanBaseParameterBasedSqlProcessorFactory.cs and existing Oracle string visitor only as needed;
UnitTests/TextCollectionQueryTests.cs (new) and FunctionalTests/BasicRelationalTests.cs.
The planned text-collection contract in query/update design is authoritative.

1. Reproduce missing string collection mapping and Oracle converted-empty expansion; reuse the built-in
   string JSON reader/writer while preserving mapping clone/converter/parameter hooks.
2. Expand only Oracle provider-text IN parameter collections before existing scalar normalization/base NULL
   compensation. Preserve raw values and per-mapping bindings, execution-local state, safe generated names,
   EF bucket policy and cache disabling. MySQL keeps inherited scalar expansion.
3. Cover inline/captured arrays/lists, NULL/empty/nonempty/duplicates and converted-empty elements, both NULL
   settings, changed captures, padding boundaries, name collisions and same capture under two converters.
4. Add sync/async binding/Boolean/nullable reader checks with supplied results; full tests, independent review,
   selected-patch/same-package gates and commit. General VALUES/explicit parameter transport and native
   collation/LOB/charset/actual membership remain separate work and acceptance gates.

## Batch3j brief: captured integral collection roots

**Files:** existing collection-parameter visitor/processor as needed, OceanBaseQuerySqlGenerator.cs,
UnitTests/CollectionRootQueryTests.cs (new) and FunctionalTests/BasicRelationalTests.cs.
The planned captured-root contract in query/update design defines this bounded follow-up.

1. Reproduce general captured int/int? root failures through real EF queries, then expand VALUES before
   the base IntTypeMapping cast. Reuse execution-local snapshots/names/mappings without membership padding.
2. Render SELECT/UNION ALL SELECT with Oracle DUAL per arm, quoted aliases, retained/pruned ordinals and
   legal lossless signed integer typing. Empty captured roots have typed zero rows, not a fake NULL row.
3. Cover arrays/lists, native int/int-to-long converters, null/empty/duplicates/all-null/current captures,
   filter/project/order/composed membership/count/any/join shapes, both modes/null settings and policy modes.
4. Add intercepted sync/async binding/checked reader cases, focused/full tests, independent review and
   selected-patch/same-package gates. Originally empty inline/general text/other carriers stay explicit
   local follow-ups; native coercion/cardinality/order/results remain live gates. Not dispatched yet.

## Batch3k brief: originally empty inline integral roots

**Files:** derived relational queryable-visitor/postprocessor factories only if the demonstrated metadata
gap requires them; minimal options registration and existing VALUES annotation/generator integration;
CollectionRootQueryTests.cs and FunctionalTests/BasicRelationalTests.cs.
The inline-empty local contract in query/update design and local metadata research define this slice.

1. Record the existing real EF empty-inline Count/Any/Select/join failures, then retain original int/int?
   type/mapping before projection/pruning. Preserve contextual inference and its conflict rejection.
2. Use narrow inherited hooks/factories, retain the derived visitor in nested subqueries, and carry immutable
   empty metadata through pruning/alias rewriting without copying Process or guessing Count's element type.
3. Reuse legal typed zero-row rendering; verify nested/alias/ordinal/converted/unsupported-family boundaries
   with real EF and sync/async supplied readers. Keep current captured/nonempty/membership regressions.
4. Focused/full tests, independent review, isolated patch/same-package gates and scoped commit. General
   text/other families and native database results remain separate. Local implementation and source review
   complete: focused57 unit/eight intercepted, Debug/Release/isolated14 Release1281 and same-package
   consumers on both patches/modes pass. Source/docs/consumer reviews pass; native and wider feature gates remain open.

## Batch3l brief: exact Floor/Ceiling translations

**Files:** existing OceanBaseMathTranslator.cs, new FloorCeilingQueryTests.cs, the two now-supported
MathQueryTests negative-list entries and intercepted BasicRelationalTests.cs cases. The numeric local contract in query/update design and numeric-next
research define the eight exact methods, full finite ranges and converter/native-result boundaries.

1. RED exact methods in real EF queries; add Oracle FLOOR/CEIL and MySQL TRUNCATE plus guarded CASE
   without BIGINT narrowing. Reuse validation; retain original mappings and fresh syntax constants.
2. Verify null settings, stable nested expressions, finite/subnormal/decimal extremes, same/cross-carrier
   converters and checked readers. Keep volatile single-evaluation and native result behavior explicit.
3. Focused/full checks, independent review, isolated patch/same-package gates and scoped local commit.
   Local implementation/source review complete: focused46 unit/12 intercepted, Debug/Release/isolated14
   Release1339 and same-package consumers pass both modes/patches. Round/transcendental functions remain separate.

## Batch3m brief: exact transcendental numeric methods

Implement all twelve exact Math/MathF Sqrt, Exp, Log (one/two arguments), Log10 and Pow identities using
the existing numeric translator. Both modes use native SQRT/EXP/LN/POWER; two-argument Log uses MySQL
LN(x)/LN(base) and Oracle LOG(base,x), and Oracle Log10 uses an independent raw provider ten. Preserve
each operand mapping/converter; primary x owns the result. Native functions retain independent nullable
metadata. No finite-domain clipping or fabricated CLR NaN/exception policy is introduced.

Normalize actual Single-provider results in SQL after the complete operation with neutral FLOAT or
BINARY_FLOAT casts; retain native double results. A result-only decimal mapping clone uses GetValue and
checked numeric conversion because Connector0.2.0 GetDecimal rejects boxed native float/double values.
The clone retains the converter/facets; ordinary decimal readers and write validation remain owners.
Binary-to-decimal conversion may round at materialization and does not establish decimal SQL arithmetic.

Cover exact dispatch, independent mixed converters, NULL semantics, cast/composition boundaries and
sync/async supplied-reader materialization. Root owns package-only consumers on both selected EF patches.
Native execution/typing/precision/domain errors remain live gates. Detailed brief/evidence:
artifacts/local-development/batch3m/. Local source/patch/package gates and independent source review pass;
broader local work remains open.

## Batch3n brief: numeric aggregates

Implement native SUM/AVG/MIN/MAX through a derived aggregate translator provider, preserving plugin
priority. Own exact Sum/Average numeric/nullable selector identities and generic numeric Min/Max across
the existing eleven primitive numeric families. Keep filtered CASE/DISTINCT ordering, inherited cardinality
shapers and current Count/LongCount behavior. Invalid owned mappings fail explicitly rather than falling
through to EF's default aggregate translator.

AVG retains the mapped input without per-row conversion. Model int/long Average uses an independent
converter-free Double result; other aggregates retain the source converter/result convention. Native SUM
keeps its mapping so EF's COALESCE uses converted model zero. Normalize final Single-provider results
outside that SUM/COALESCE with fixed FLOAT/BINARY_FLOAT targets, including physically wider declarations.
Narrowly widen the existing private marker to numeric aggregate model types; preserve nonnumeric rejection.
Reuse the result-only decimal reader without SQL decimal narrowing or physical write-bound clipping.

Baseline package SQL and consumer RED reproduce integer AVG pre-casts and wide-declaration Single result
casts. Unit/pipeline tests will cover both modes/null settings, selected numeric/nullable families, mixed
converters, empty/null shaping, filtering/distinct, current parameters and checked reads. Native execution/
metadata/accumulation/rounding remain live gates. Detailed brief/provenance/evidence:
artifacts/local-development/batch3n/. Local source/patch/package gates and independent source review pass.
The overall local EF10 goal remains active.

## Batch3o brief: scalar numeric extrema

Translate exact Math/MathF/decimal Min/Max and numeric EF.Functions Least/Greatest through the relational
SQL visitor. Preserve nested calls, existing operand mappings, operand order and the first result converter.
Use native LEAST/GREATEST for two or more arguments and identity for one. Both row-dependent inline
arguments and extracted numeric constant arrays are supported. Snapshot these arrays in the ordinary query
cache key so a caller can change a capture between executions without reusing old SQL. Do not promise
refresh within an explicit compiled delegate or concurrent mutation during query compilation.

Reuse the fixed Single result cast, result-only decimal reader and checked numeric mappings. Native
NULL behavior and Oracle first-argument coercion remain server semantics; no CLR floating-point parity
claim. Baseline package SQL probe confirms the missing translations and both EF array shapes. Brief and
evidence: artifacts/local-development/batch3o/.

## Batch3p brief: exact decimal Round

Add all eight exact decimal `Math.Round` and `decimal.Round` overloads with constant digits and
midpoint rules. Use the existing method translator provider, exact decimal truncation and remainder
tests, and a result-only decimal reader that preserves the input mapping and converter. Exercise
both modes, ties, directed modes, 0 and 28 digits, invalid/runtime arguments and command shaping.
Floating point overloads and runtime argument semantics remain separate local work.
Batch brief and local evidence: `artifacts/local-development/batch3p/`.

## Batch3q brief: binary floating Round

Add the four exact Round signatures on each of Math (Double), MathF (Single), Double and Single.
Use matching binary mappings, constant rule/digit validation, .NET scaling limits and integral guards,
with neutral Single casts at binary precision boundaries. Preserve the original result reader and
converter. See `artifacts/local-development/batch3q/` for decompiled runtime reference and local
evidence. Runtime arguments and actual engine calculation remain separate acceptance work.

## Batch3r brief: Gregorian calendar members

QRY-07/QRY-11: extend the existing temporal member translator with `DateTime` and `DateOnly`
`DayOfYear` and `DayOfWeek`. Reuse the native carrier checks and leave unrelated date/time members
outside this slice. Pin the per-mode SQL and nullable/converted behavior in `TemporalMemberQueryTests`,
exercise enum projection and filtering through `BasicRelationalTests`, then run the full suite and the
packed consumer. Review the mappings, SQL and live limitations before committing.

## Batch3s brief: raw SQL command boundaries

QRY-10/DIA-01: characterize inherited FromSqlRaw/FromSqlInterpolated and
ExecuteSqlRaw/ExecuteSqlInterpolated through the real EF command builder and connector parameters.
Add sync/async, both-mode tests to BasicRelationalTests with hostile text, NULL, explicit connector
parameters, repeat execution and composed query materialization. Fix only proven provider defects.
Raw SQL identifiers/text remain caller-owned; interpolating identifiers is not supported. Exercise
packed SQL translation, run complete suites, review, update QRY-10 evidence and commit. Native SQL
execution, server parameter metadata and database effects remain live gates.

The initial command tests expose CFG-04/SCH-01's missing database creator. Implement its read-only
connection/Exists/HasTables service using IRelationalConnection and IRawSqlCommandBuilder, with
MySQL unknown-database classification and propagated Oracle/auth/permission/network errors from Exists.
CanConnect follows EF's boolean probe contract while preserving cancellation. DDL methods explicitly
reject before opening a connection until Batch 7 supplies the schema creation/ownership contract;
SCH-02/03 are still Required and Planned. Add facade and cancellation/error tests before registration.

## Batch3t brief: MySQL positional command boundary

QRY-01/03/06/07/10, UPD-01/02/03, CFG-04: keep EF's named SQL templates, but supply a
provider relational command builder/factory that converts the final MySQL DbCommand to positional
SQL and orders bound values by marker occurrence. Oracle commands remain named. Preserve EF's
original LogCommandText, command timeout/transaction, cancellation, interceptors and reader cleanup.
Parse quotes/comments without treating marker-like text as binds; reject ambiguous SQL_MODE-dependent
backslash quoting, executable comments with binds, missing/unused/mixed binds and repeated streams
before execution. Recreate repeated mapped parameters through EF rather than copying native facets.
Connector 0.2.0 has no public parameter clone; repeated caller-owned native parameters require an
explicit supported duplication contract, not inference from Scale's ambiguous zero getter.

1. Add focused failing tests for order/repetition, quote/comment skipping and malformed/ambiguous SQL.
2. Implement parser and command factory in Storage; register the factory before core services.
3. Add real EF query/raw/write tests asserting final positional command text and ordered values;
   verify the connector 0.2.0 binding contract directly offline. Update changed actual-command assertions.
4. Run full Debug/Release, isolated EF10.0.14 Release and packed consumers; independent source review,
   update remaining boundaries, then commit. No database, EF8/EF9 or publication work.

## Execution record

- Batch3s: four raw query cases passed initially; twelve execute cases exposed missing creator DI.
  After registration, prefixed raw parameters exposed doubled SQL prefixes and Oracle scalar tests
  exposed a connector-number conversion gap. Both fixed. Final Debug/Release/isolated EF10.0.14 Release
  1840 each, no failures/skips; same-package consumers and contents pass. Independent review fixed
  Oracle metadata scope. Connector v0.2.0 review identified MySQL positional binding as the next
  local priority, spanning generated queries, raw SQL and tracked writes. DDL/live remain open.

- Batch3t: positional MySQL command factory/parser covers query, raw, tracked write and generated-key
  order/repetition, quote/comment rejection, mixed/missing binds and connector client-prepared fallback.
  Initial focused run: 11 failing/two passing cases. Final Debug/Release/isolated EF10.0.14 Release 1868 each, zero failures/skips;
  package content and EF10.0.0/10.0.14 consumers pass. Caller-owned repeated streams remain explicitly unsupported.
  Independent review fixes have RED/GREEN regressions for reader open balancing and initialization interceptors.

- Batch3r extends the existing temporal translator with four Gregorian calendar members. Twelve RED
  translation cases; final Debug/Release/isolated EF10.0.14 Release 1792 each, zero failures/skips.
  Package `calendar-final-20261111` passes both modes/patches and package-content verification.
  Independent source review findings addressed; engine arithmetic/result metadata remain live gates.

- Batch3q adds 16 floating Round signatures with constant syntax arguments, precision guards and
  Single normalization. Debug, Release and isolated EF Core 10.0.14 each pass 1780 tests. The same
  `floating-round-20261111` package passes both modes and both tested EF patches. Runtime digits and
  rules remain local work; native precision, casts and computation remain live gates.

- Batch3p translates eight exact decimal Round overloads, supports five constant rules and digits
  0..28, and keeps input mappings and checked decimal result shaping. Debug, Release and isolated
  EF Core 10.0.14 each pass 1768; the same `decimal-round-20261111` package passes both modes and
  patches, including Round predicates that the previous package rejects. Native decimal result
  behavior remains a live gate; floating point Round and runtime arguments remain local work.

- Batch3o adds scalar numeric extrema translation, preserves nested operands and result mapping, and
  snapshots extracted numeric extrema arrays for ordinary query cache keys. Focused tests pass 43 unit and
  six intercepted cases, including same-hash mutation and reversion. Debug, Release and isolated 10.0.14
  Release each pass 1760 tests (1400 unit, 358 intercepted, two project consumers); the same baseline-built
  `extrema-20261111` package passes both modes and EF patches. Content, graph, DLL and baseline asset checks
  pass. Independent source review passed. Native computation remains a live gate. See `artifacts/local-development/batch3o/`.

- Batch3n native numeric aggregates: initial8 native-AVG/wide-Single RED failures and a focused Oracle
  synthetic-zero literal RED become279 numeric unit/16 intercepted passes. New aggregate service preserves
  plugins/Count/LongCount, native AVG inputs, independent promoted Double, mapped SUM zero and result
  converters; a narrow marker allowance retains EF's Int32-typed coalesce. Oracle Single zero literals
  normalize safely without InfinityF. Final Debug/Release and isolated14 Release1711=1357+352+2 pass, no
  skips. Same baseline-built aggregates package passes both modes/patches, content/graph/DLL/baseline
  identity gates. Independent source spec/quality review passes without findings. Evidence:
  artifacts/local-development/batch3n/. Real server
  accumulation/metadata/empty/group execution remain live gates; wider local query/model/write/transaction/
  schema/specification/deferred audit remains. Overall EF10 goal active, no push or EF8/9 work.

- Batch3m local transcendental slice:54 expected missing-dispatch RED failures become65 focused unit and12
  intercepted passes. Exact twelve Math/MathF identities preserve independent operands and x-owned result
  converters; neutral Single SQL normalization follows the completed operation. Result-only decimal
  GetValue clones retain ordinary decimal readers and clone/parameter behavior. A focused marker-shape
  RED/GREEN correction precedes final full Debug/Release and isolated14 Release1416=1078+336+2, no skips.
  Same baseline-built transcendental package passes both modes/patches; content, package-only graph and
  DLL/baseline identity pass. Independent source spec/quality review passes without findings. Evidence:
  artifacts/local-development/batch3m/. Native metadata/calculation/precision/domain errors remain live
  gates. Round, wider queries/aggregates/model/write/transaction/schema work remains local; full goal active.

- Baseline: 57fd44a, clean efcore/10.0. Prior verified suite: 563 (420 unit, 142 intercepted, one consumer).
- Batch 1: local configuration/cache/helper scope complete; focused 66, full Debug/Release 606 each, independent
  review passed. No production change required. Standard DatabaseFacade.GetDbConnection/SetConnectionString
  failed because IRelationalDatabaseCreator is missing (10 initial failures); move these regressions to Batch 7
  under CFG-04/SCH-01. IRelationalConnection tests prove only the lower-level delayed-assignment contract.
- Environment follow-up: a2bbe5f adds repository-owned LF/CRLF rules; attributes/normalization and review passed.
- Batch 2a: local Oracle fixed-point alias scope complete. Unit RED 10/97 failures and pipeline RED 6/8 failures
  became 97/97 and 8/8 passes. Full Debug/Release and isolated EF10.0.14 Release each pass 627 (475+150+2).
  Independent review passed; the same alias-20261110 nupkg passes both modes on EF10.0.0/10.0.14, with package
  identity/assets/ranges and package-only graph inspected. Expanded consumer fails against the old text package.
  Evidence: artifacts/local-development/batch2a/. No server connection or release publication.
- Batch 2b local scope complete: actual facets/property metadata and complete transport-family clones validated,
  while legal precision/converter clones preserve wide query inputs. Final focused 131 and Debug/Release 706 pass.
- Batch 2b fix round 1: initial focused/full checks pass, but independent review found public Clone DbType and
  StoreTypePostfix overrides may bypass transport/declaration consistency. Add RED regressions and reject unsafe
  overrides. Both findings are addressed and independently re-reviewed. True postfix regression uses mappingInfo;
  a postfix-only argument is an EF no-op and does not justify another public override. Final isolated EF10.0.14
  Release passes 706. The same baseline-built temporal2-20261110 package passes both modes at EF10.0.0/10.0.14;
  README, identity, assets, bounded ranges and package-only consumer graph inspected. The initial temporal probe
  predates review fixes and is not current completion evidence. No publication or database connection occurred.
- Batch 2c local scope complete: finite floating fractions reject integer reads/generated keys and enums;
  four confirmed Oracle property precision conflicts now reject through the shared numeric property check.
  Focused 116 unit and ten pipeline pass; Debug/Release and isolated EF10.0.14 Release each pass 752 (590+160+2).
  Independent review passed. Same baseline-built numeric-20261110 nupkg passes both modes on both selected patches;
  previous temporal2 package fails the new reader smoke. Package identity/assets/README/ranges and package-only
  graph inspected. Evidence: artifacts/local-development/batch2c/. No live connection/publication.
- Scalar audit note: connector 0.2.0 nuspec revision 4734781a7028aea990af7a832ca7d251a2055556
  OceanBaseDataReader.GetDecimal/GetFieldValue already handles OceanBaseDecimal.ToDecimal; reuse that reader
  path rather than inventing a second transport. Byte/session/calendar/LOB/wire gates remain external evidence.
- Batch 3a local concat/null-boundary scope complete: initial SQL RED 12/14 failures and converter RED 2/2
  failures became 33 focused passes. Debug/Release and isolated EF10.0.14 Release pass 785 (611+172+2).
  Independent review passed. Same baseline-built concat-20261110 package passes both modes on EF10.0.0/10.0.14;
  previous numeric package fails the new syntax check. Package-content gate and package-only graph pass.
  Oracle provider-string parameters disable command-SQL caching for native empty/null correctness; pure
  converters preserve raw binding values and null-converting string converters reject explicitly.
  Evidence: artifacts/local-development/batch3a/. Batch 3b arithmetic and wider string/query work remain open.
- Batch 3b local arithmetic scope complete: unit RED 9/12 and intercepted RED 4/4 failures become 12+4 focused
  passes. Debug/Release and isolated EF10.0.14 Release pass 801 (623+176+2). Independent review passed.
  Same baseline-built arithmetic-20261110 package passes both modes on EF10.0.0/10.0.14; preceding concat
  package fails the arithmetic smoke. Package-content gate and package-only graph pass. Server arithmetic,
  predicate membership/zero-divisor/overflow gates remain open. Evidence: artifacts/local-development/batch3b/.
  Next local slice is Batch 3c unary string functions; search/substring/date/numeric/wider query work remains.
- Numeric research exposed a local floating-reader defect: the existing shared conversion always calls exact
  OceanBaseDecimal.ToDecimal before selecting float/double. A package-only probe against arithmetic-20261110
  reproduces six overflows for representable target values (1E30, 38-digit fraction, 1E-40). The published
  connector revision confirms invariant bounded scientific ToString is available. Queue Batch 2d after 3c;
  this historical reproduction is resolved by Batch 2d below.
- Batch 3c unary functions/Length local scope complete: focused 24 unit + eight intercepted pass after RED,
  including four lowercase/whitespace Oracle LOB return-family failures. Debug/Release and isolated EF10.0.14
  Release pass 833 (647+184+2). Independent review passed. Same baseline-built unary-20261111 package passes
  both modes on EF10.0.0/10.0.14; the previous arithmetic package fails the new unary predicate. Repeatable
  package-content gate and package-only graph pass. Evidence: artifacts/local-development/batch3c/.
  Native casing/SQL-space trim/database-character semantics and converter/live limits are documented.
  Batch 2d normalization follows below, then Batch 3d search; the full goal remains active.
- Batch 2d local scope complete: shared OceanBaseDecimal normalization parses directly at Single/Double
  precision, rejects finite-to-infinity conversion and preserves exact/native paths. RED 29 unit + eight
  intercepted failures; final Debug/Release and isolated EF10.0.14 Release pass 882 (688+192+2). Independent
  spec/quality review passed. Same baseline-built wide-decimal-20261111 package passes both modes on
  EF10.0.0/10.0.14; previous unary package fails the new Single midpoint smoke. Content gate and package-only
  graph pass. Evidence: artifacts/local-development/batch2d/. No new storage/live/platform claim.
  Next is Batch 3d search; the overall goal remains active.
- Batch 2d committed as 3bb365a after code/docs approval and one docs example correction; no push.
- Ruling: Batch 3d searches return a total Boolean, false for NULL receiver/provider needle and true when
  negated, in both null settings. Known empty is receiver IS NOT NULL; Oracle stored/computed empty has
  already become NULL. Explicit guards/CASE prevent nullable suffix equality compensation from matching
  NULLs. This follows common query predicate handling without promising CLR exceptions; changing the policy
  later would change observable query results and require regression updates.
- Batch 3d local search scope complete: exact string Contains/StartsWith/EndsWith use native functions and
  guarded total Booleans; Oracle provider-empty captures resolve before normalization. Initial translation
  RED 2/2 plus converted-empty and unrelated-method NRE regressions became 24 focused query and eight
  intercepted passes. Debug/Release and isolated EF10.0.14 Release pass 914 (712+200+2). Independent code
  review passed. Same baseline-built search-20261111 package passes both modes on EF10.0.0/10.0.14;
  previous wide-decimal package fails the new method translations. Content and package-only graph pass.
  Evidence: artifacts/local-development/batch3d/. Server search/collation/padding/character/LOB and current
  hosted execution remain unverified. Next is Batch 3e Substring; the full goal remains active.
- Batch 3d minor (deferred to 3e): add a direct Oracle computed-needle CASE/COALESCE-to-NULL regression.
  Nullable column needles and composed sources already pass; independent review approved with this
  non-blocking coverage suggestion. Carry it into the next string-composition slice and final audit.
- Batch 3d committed as e41981e after code/docs approval; current local source/package checks pass 914.
  Batch 3e starts next using the documented native substring/index/result-mapping contract. No push.
- Batch 3e local Substring scope complete: exact two arities use safe long origin adjustment and preserve
  result family/converter metadata. Genuine translation RED 8/10; final focused 12 unit (including two carried
  computed Oracle NULL needle cases) and eight intercepted pass. Debug/Release and isolated EF10.0.14 Release
  pass 934 (724+208+2). Independent spec/quality review passed. Same baseline-built substring-20261111 package
  passes both modes on EF10.0.0/10.0.14, including both arities/positive max-start; preceding search package
  fails missing Substring. Content gate and package-only graph pass. Evidence: artifacts/local-development/batch3e/.
  Batch 3d computed-needle review minor is closed. Native NCLOB/range/character/padding/LOB and current hosted
  gates remain open. Next is Batch 3f ordinary temporal parts; the complete local EF10 goal remains active.
- Batch 3e committed as 4e78da1 after code/docs approval, 934 local tests and both selected package patches.
  Batch 3f ordinary temporal members starts next; no push/publication or EF8/9 work.
- Read-only CI follow-up: run 42865290840 at e41981e has successful Windows/Linux jobs and all four
  restore/test/pack/content gates. Metadata is artifacts/local-development/ci-e41981e.json; this covers
  the search checkpoint and preceding changes, not later source. No run dispatch or push was performed.
- Batch 3f fix round 1/5: all three coverage findings addressed; captured DateOnly/TimeOnly carrier/precision,
  numeric/binary negatives and representable temporal endpoints added without production changes. Scoped
  independent re-review approved. Final Debug/Release and isolated EF10.0.14 Release pass 963 (745+216+2).
- Batch 3f local ordinary temporal parts complete: twelve exact members, Oracle DATE clock cast and decimal
  SECOND then TRUNC, converter-free int results and conservative NULL metadata. Focused 21 unit/eight
  intercepted pass. Same baseline-built temporal-parts-20261111 package passes both modes on EF10.0.0/10.0.14;
  prior Substring package fails the new member predicate. Content gate and package-only graph pass.
  Evidence: artifacts/local-development/batch3f/. Native extraction/casts/calendar/interval and current hosted
  execution remain open. Next is Batch 3g numeric Abs/Sign/Truncate; the complete local EF10 goal stays active.
- Batch 3f committed as 9f595ed after final 963-pass suites, code/fix/docs approval and package gates. No push.
  Batch 3g starts from this checkpoint using the exact twenty-two-method numeric contract.
- Batch 3g local numeric scope complete: exact 22 Abs/Sign/Truncate signatures, strict mapping validation,
  compatible source/result converters, independent SIGN/precision int and full existing finite ranges.
  Genuine RED 88 missing translations; focused GREEN 108 unit+12 intercepted. Full Debug/Release and isolated
  EF10.0.14 Release each pass 1083 (853+228+2). Independent spec/quality review approved without findings.
  Same baseline-built numeric-core-20261111 nupkg passes both modes on EF10.0.0/10.0.14; preceding temporal
  package fails the new numeric predicate. Content gate and package-only graph pass; baseline assets stay
  EF10.0.0. Evidence: artifacts/local-development/batch3g/. Native arithmetic/domain/overflow/result families
  and current hosted execution remain open. Next is Batch 3h integral membership/ordinary relational shapes;
  wider local work remains active. Development resumed after the user's pause; no push or publication.
- Batch 3g committed as a98e27b after code/docs approval and full source/patch/package gates; no push.
- Batch 3h ruling: initialize matching built-in JSON reader/writer metadata for all existing closed generic
  integer-mapper carriers (eight integers, bool and decimal), preserving clones/converters. An int-only special
  case would miss int properties converted to long; the same constructor also owns Boolean transport. This
  enables EF collection mapping metadata, not a claim of mapped JSON/one-parameter collection SQL support.
- Batch 3h additional RED: Oracle null collection normalization rewrites a parameter envelope into a scalar
  NULL; exclude ElementTypeMapping envelopes from the scalar string visitor. A separate four-case same-query
  mode-order RED confirms shipped EF's relational compiled-query key omits ParameterizedCollectionMode.
  Ruling: add the current collection mode around the full base key in a scoped derived generator, registered
  through the existing provider builder; retain service-provider hash/sharing. This fixes a demonstrated
  local cache defect without copying EF's key or isolating service graphs unnecessarily.
- Batch 3h initial Debug/Release pass 1153 and independent code review approves, but isolated EF10.0.14
  Release exposes one existing Oracle model-reference cache assertion failure (1152 other passes). Fix round1
  investigates exact service/key/cache state before any correction; no package/commit acceptance yet.
- Batch 3h fix round1/5: controlled same-capacity pressure reproduces identity failure with equal keys and
  shared services, proving unconditional model retention is an invalid fixture assumption. The exact natural
  failure timing remains unproven. Test-owned retained cache preserves every positive assertion; two explicit
  eviction cases verify equivalent rebuilding/shared keys/services. No production cache change. Scoped review
  approves (one addressed, zero open). Final Debug/Release and isolated EF10.0.14 Release pass 1155 (885+268+2).
- Batch 3h local slice complete: integral scalar IN metadata, Oracle envelope exclusion and complete-base-key
  collection-mode partitioning; inherited joins/subqueries/quantifiers/counts receive characterization only.
  Focused 30 unit/40 intercepted pass. Same membership-20261111 nupkg passes both modes on EF10.0.0/10.0.14,
  while the preceding numeric package fails the new captured collection mapping. Content/package-only graph
  pass; baseline assets remain EF10.0.0. Evidence: artifacts/local-development/batch3h/. QRY-03/04 stay partial;
  actual server results/current hosted execution remain open. Next is Batch3i scalar text membership.
- Batch3h committed as 1b14513 after production/fix/docs approval, 1155-pass source gates and both selected
  same-package consumers. Final staged check removed only trailing blank lines from the new test file;
  no behavioral change or extra suite rerun. Clean efcore/10.0 before Batch3i; no push or publication.
- Batch3i started from 1b14513 with a fresh implementer and exclusive provider-build ownership. Previous
  membership package fails the new string collection consumer before SQL generation (missing element
  mapping). Metadata-only repair then exposes Oracle converted-empty expansion, so this is local missing
  behavior rather than a live-environment blocker. Default NULL compensation and relational UNKNOWN remain
  distinct contracts. Final acceptance is pending; no current-source package or completion claimed.
- Batch3i source gates: 25 focused unit/20 intercepted pass; full Debug/Release and isolated EF10.0.14
  Release pass 1200 (910+288+2), zero failures/skips. Exact metadata repair and Oracle pre-expansion retain
  raw values/mappings/converters, per-execution state, collision-safe names and padding before normalization.
  Same baseline-built text-membership-20261111 package passes both modes on EF10.0.0/10.0.14, including changed
  captures and all-provider-null Oracle compensation. Content/package-only graph pass; baseline assets stay
  EF10.0.0. Evidence: artifacts/local-development/batch3i/. Independent source spec/quality review approves
  without findings; final docs/consumer review and commit acceptance remain pending.
- Batch3i consumer fix round1/5: docs pass, but nonempty captures need a positive comparison-arm assertion
  after empty/all-provider-null transitions. Original implementer owns the consumer-only correction and
  covering same-package checks. Production source stays approved; no repeated full suite or repack required.
- Batch3i fix round1/5 complete (one addressed, zero open): comparison-arm assertion now names SearchText,
  rejects stale false/IS NULL-only predicates and preserves MySQL empty versus Oracle NULL semantics. Same
  unchanged package passes both patches/modes; provider DLL/package hashes unchanged. Scoped independent
  spec/quality re-review approves with no new findings. Local slice ready for commit; next is planned Batch3j.
- Batch3i committed as c7f1bb0 after all code/docs/consumer reviews and covering checks. Clean efcore/10.0,
  ahead5, no push/publication. Batch3j starts next under the captured-integral-root contract; runtime-null
  zero-row handling does not replace upstream LINQ null-source argument validation.
- Batch3j ruling: preserve proven upstream conflicting-mapping rejection for a collection Value used with
  native and converted properties, or directly projected by Join while its key infers an int-to-long mapping.
  The error occurs in EF's type-mapping postprocessor before the owned runtime seams. Keep exact negative
  tests, native join positives and consistently inferred converted membership/reader positives. No new
  compile-time service in this bounded slice; broader converted-root projection stays local follow-up work.
- Batch3j initial gates pass40 unit/16 intercepted, full Debug/Release and isolated EF10.0.14 Release1256
  (950+304+2), and same integral-roots package consumers pass both modes/patches. Independent review passes
  spec but requests a nested synthetic-wrapper/explicit-CAST regression. Fix round1 is test-only, confined
  to the existing renderer cases and covering checks; production code and package stay unchanged.
- Batch3j fix round1/5 complete (one addressed, zero open): existing renderer cases now enter the outer
  identity wrapper around an explicit long-to-int CAST and retain both targets/node/mapping identities.
  Covering2/2 pass in baseline Debug/Release and isolated14 Release; production/assets/centralprops unchanged.
  Scoped independent spec/quality re-review approves. No full suite/pack/consumer repeat for this test-only fix.
- Batch3j local slice source/patch/package gates pass1256; same integral-roots-20261111 package passes both
  modes/patches and content/package-only graph, baseline assets stay10.0.0. QRY-03 stays partial. Native UNION
  coercion/cardinality/order/limits and current hosted execution remain open. Next is planned Batch3k.
- Batch3j committed as cccdd04 after source/test/docs/consumer approvals. Consumer-only fix round2 requires
  exact UNION separator counts in both modes; unchanged package reruns pass both patches, provider/package
  hashes unchanged. Clean efcore/10.0 ahead6, no push/publication. Continue originally empty inline roots.
- Batch3k RED12 confirms missing inline-empty metadata; delegated inline/subquery visitor and postprocessor
  retain original CLR type, contextual mapping/converter and immutable prototypes. A real ordinal-only empty
  ElementAt expression reproduces EF pruning's body-Value/ORDER-BY-_ord mismatch (RED2). Ruling: repair only
  our annotated zero-row roots using actual surviving references/original identity; retain general pruning.
- Batch3k focused57 unit/eight intercepted pass. Full Debug/Release and isolated14 Release each1281=967+312+2,
  zero failures/skips; one later newline assertion portability edit reruns only the covering eight cases.
  Same inline-empty-20261111 package/content/graph consumers pass both modes/patches; baseline assets stay10.0.0.
  Independent source review passes with no actionable findings. QRY-03 remains partial; SetTables internal API and precompiled
  annotation quoting/native execution have explicit evidence limits. Next planned numeric slice is Batch3l.
- Batch3k committed as9287fab after source/docs/consumer approvals and doc-only stale-status fix round1.
  All11 intended files including new factories staged/whitespace checked. Clean efcore/10.0 ahead7; no push.
- Batch3l RED44fail/two pass confirms missing exact dispatch. Extend existing translator by20 lines for eight
  Floor/Ceiling identities: Oracle FLOOR/CEIL, MySQL guarded TRUNCATE/CASE with independent raw syntax constants.
  Nullable numeric carrier self-review adds RED1fail/one pass, then unwraps only fresh one's boxed carrier;
  original mapping/converter/declared nullable ProviderClrType remain intact. No ordinary numeric input cap.
- Batch3l focused46 unit/12 intercepted, full Debug/Release and isolated14 Release1339=1013+324+2 pass with
  zero failures/skips. Same baseline-built floor-ceiling package passes both modes/patches, content and
  package-only graph; baseline assets stay10.0.0. Independent source review passes with no actionable finding.
  Native TRUNCATE/comparison/arithmetic/CASE typing, finite/subnormal/decimal calculation and volatile
  single evaluation remain explicit live/semantic limits. Wider math/temporal/aggregate/collection and later
  model/write/transaction/schema inventory remain locally developable; overall goal active.
- Line-ending recheck: git ls-files --eol reports no CRLF/mixed index text. Existing working CRLF files remain
  safe under the repository attributes; do not reset local edits or change global core.autocrlf to refresh them.
- Remaining Batch 2 scalar work and Batches 3–10 pending. The overall goal remains active.
- Batch 8 package README slice: use the provider project's README as package-owned documentation and declare
  PackageReadmeFile. Verify archive metadata/content with no-build packing of an already verified isolated
  assembly, independently of the active scalar worker; this does not complete the broader package/release gates.
  Archive metadata/exact content and SHA-256 equality with the verified assembly pass; pack no longer reports a
  missing README. Evidence: artifacts/local-development/package-readme/. Independent review passed. The isolated
  probe has exact EF10.0.14 dependencies; it does not replace bounded-range alias package/consumer evidence.
- Batch 8 package content gate: add PackageConsumer/Verify-Package.ps1 using only PowerShell/.NET ZIP/XML APIs.
  Inputs are an existing nupkg and expected version. Assert provider identity, net10.0 assembly/framework,
  README metadata/nonempty content and exactly the three expected dependency ranges. Verify a valid current
  package plus wrong-version, missing-README and exact-patch probes fail for the intended reason. This read-only
  gate neither builds nor executes a provider or changes the independent source/consumer/live gates.
  At introduction, the numeric package passed; wrong-version, old missing-README and exact-patch probes failed at their intended
  checks on Windows PowerShell 7. Independent spec/quality review passed; Linux execution unverified.
- Cross-platform CI follow-up: add a Windows/Linux GitHub Actions matrix for restore, Release tests, pack and
  the existing package-content gate, using PowerShell 7 on both hosts. It performs no database access or
  publication. YAML parsing/structure and independent spec/quality review pass; committed as 5feef06.
  Hosted execution and Linux runtime remain unverified until an actual workflow run succeeds.
- Hosted CI follow-up (read-only): run 42854480450 at db676ff has successful ubuntu-latest/windows-latest jobs
  and successful restore/test/pack/content steps. Run/job metadata saved in artifacts/local-development/ci-db676ff.json;
  log-archive download was denied HTTP403 (admin rights), so no fresh per-test count is asserted. This evidence
  applies to db676ff only. No push or workflow dispatch was performed.
