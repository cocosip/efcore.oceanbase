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

## Execution record

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
  Current numeric package passes; wrong-version, old missing-README and exact-patch probes fail at their intended
  checks on Windows PowerShell 7. Independent spec/quality review passed; Linux execution unverified.
- Cross-platform CI follow-up: add a Windows/Linux GitHub Actions matrix for restore, Release tests, pack and
  the existing package-content gate, using PowerShell 7 on both hosts. It performs no database access or
  publication. YAML parsing/structure and independent spec/quality review pass; committed as 5feef06.
  Hosted execution and Linux runtime remain unverified until an actual workflow run succeeds.
