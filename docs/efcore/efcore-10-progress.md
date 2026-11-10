# EF Core 10 Development Progress

## Active local development: 2026-11-10

Development resumed at the user's request on efcore/10.0, now the active EF10 development and packaging branch.
The provider is not release-ready. The [feature checklist](efcore-10-feature-checklist.md) remains the
acceptance authority; this document records the handoff rather than defining a different completion target.

The current user goal is to finish all locally developable EF10 functionality, with tested/reviewed commits after
each small group. Real database integration/tests and EF8/EF9 adaptation are explicitly postponed. The
[local development plan](efcore-10-local-development-plan.md) tracks the full remaining inventory, including
an audit of locally feasible deferred families. Finishing one batch does not finish the overall goal.

### Hosted Windows/Linux CI evidence (2026-11-11)

Read-only GitHub inspection confirms [run 42865290840](https://github.com/cocosip/efcore.oceanbase/actions/runs/42865290840)
for e41981e succeeded on ubuntu-latest and windows-latest: restore, Release tests, pack and package-content
verification each succeeded. This covers the search checkpoint, including preceding wide-decimal changes,
not later local commits or database integration. Earlier db676ff also passed in
[run 42854480450](https://github.com/cocosip/efcore.oceanbase/actions/runs/42854480450).
Run/job/step metadata is saved under artifacts/local-development/. The earlier log archive endpoint returned
HTTP403 (repository admin rights); no new per-test count or resolved EF patch is inferred from job metadata.

### Exact transcendental checkpoint (2026-11-11)

All twelve Math/MathF Sqrt, Exp, Log (one/two arguments), Log10 and Pow identities now translate. MySQL
Log(x,base) uses LN(x)/LN(base), including bases in (0,1); Oracle uses LOG(base,x), with a fresh integer ten
for Log10. Each operand retains its own mapping/converter; primary x owns the result convention. Actual
Single-provider results normalize the completed operation through neutral FLOAT/BINARY_FLOAT SQL casts.
Decimal-provider function results use a result-only GetValue reader clone, preserving converters/facets and
ordinary GetDecimal behavior elsewhere. Binary-to-decimal CLR conversion can round at materialization;
it does not change SQL arithmetic/predicate precision. No finite input cap or SQL decimal narrowing is added.

Focused65 unit/12 intercepted tests pass following54 missing-dispatch RED failures and a focused private-
marker validation RED/GREEN correction. Final Debug/Release and isolated EF10.0.14 Release each pass
**1416 tests** (1078 unit, 336 intercepted pipeline, two project consumers), zero failures/skips. The same
baseline-built `10.0.0-checkpoint.transcendental-20261111` package passes both modes/patches with all twelve
server predicates, dialect/Single normalization and prior checks. The preceding Floor/Ceiling package
rejects Math.Sqrt in a predicate. Content/package-only graph and packed/baseline DLL identity pass;
baseline assets stay EF10.0.0. Independent source review passes with no actionable findings.

Latest package: `artifacts/checkpoint-transcendental-20261111/OceanBase.EntityFrameworkCore.10.0.0-checkpoint.transcendental-20261111.nupkg`.
Evidence: `artifacts/local-development/batch3m/`. Supplied readers prove parameter/materialization paths,
not function calculation. Native return metadata, domain NULL/warnings/errors, quotient precision, casts,
overflow/underflow/subnormals and exact rounding remain live gates. External custom decimal mapping decoders
outside the owning result-reader seam reject explicitly. Round, wider temporal/math/operators/aggregates/
collections, model/write/transaction/schema work remain local gaps. The overall EF10 goal stays active;
real DB and EF8/9 stay postponed.

### Historical exact Floor/Ceiling checkpoint (2026-11-11)

All eight Math/MathF/decimal Floor/Ceiling identities now translate. Oracle uses FLOOR/CEIL; MySQL uses
TRUNCATE plus a guarded CASE, avoiding the documented BIGINT return path of native FLOOR/CEIL. Original
input/result mappings and converters remain owners. Precision zero and provider-carrier one have fresh
converter-free mappings; nullable carrier declarations unwrap only for the nonnull syntax constant.
The translator adds no numeric input bound, clamp or precision cast. Native calculation remains unverified.

Focused coverage passes 46 unit and 12 intercepted cases. Full Debug/Release and isolated EF10.0.14
Release each pass **1339 tests** (1013 unit, 324 intercepted pipeline, two project consumers). The same
baseline-built `10.0.0-checkpoint.floor-ceiling-20261111` package passes both modes/patches for all eight
server predicates and their dialect shapes plus prior checks. Previous inline-empty package rejects the
new Floor predicate. Content/package-only graph pass; baseline assets stay EF10.0.0. Independent source review passes with no actionable findings.

Latest package: `artifacts/checkpoint-floor-ceiling-20261111/OceanBase.EntityFrameworkCore.10.0.0-checkpoint.floor-ceiling-20261111.nupkg`.
Evidence: `artifacts/local-development/batch3l/`. Supplied readers do not evaluate numeric SQL. Native
TRUNCATE/comparison/arithmetic/CASE result typing, finite extremes/subnormals, decimal exactness, guarded-arm
evaluation and actual Oracle results remain live gates. Stable expressions are the tested domain; arbitrary
volatile DbFunctions have no CLR single-evaluation promise. Round/transcendental and wider query/model/write/
transaction/schema work were still local gaps there; the transcendental slice is now recorded above.
The overall EF10 goal stays active; real DB and EF8/9 stay postponed.

### Originally empty inline integral roots checkpoint (2026-11-11)

Originally empty inline int/int? Count/Any/Select/join and nested roots retain their original element type
through delegated query translation, inferred mappings and pruning. Immutable typed NULL prototypes render
genuinely zero-row bodies. A demonstrated EF pruning defect renames a sole retained ordinal to Value while
ORDER BY still references _ord; a bounded correction restores that name only on these annotated empty roots.
Captured/nonempty roots and general pruning remain inherited. Conflicting inferred mappings still reject.

Focused coverage passes 57 unit and eight intercepted cases. Full Debug/Release and isolated EF10.0.14
Release each pass **1281 tests** (967 unit, 312 intercepted pipeline, two project consumers). After full
suites, a test-only newline assertion portability edit passes the focused eight cases; production is unchanged.
The same baseline-built `10.0.0-checkpoint.inline-empty-20261111` package passes both modes/patches for
original inline-empty Count/nullable Any/nested join and the sole-ordinal regression, plus preceding checks.
Content/package-only graph pass; baseline assets stay EF10.0.0. Independent source review passes with no actionable findings.

Historical package: `artifacts/checkpoint-inline-empty-20261111/OceanBase.EntityFrameworkCore.10.0.0-checkpoint.inline-empty-20261111.nupkg`.
Evidence: `artifacts/local-development/batch3k/`. The narrow SetTables internal-API dependency is covered by
the selected patch checks; precompiled annotation quoting and current hosted/Linux execution remain unverified.
General text/other root carriers, broader converted projections and wider query/model/write/schema work
remain local gaps. Native execution/results/typing/order/limits still need both real modes. Its next planned
numeric slice was Floor/Ceiling, now recorded above. The overall EF10 goal stays active.

### Captured integral roots and nonempty inline roots checkpoint (2026-11-11)

General captured int/int? arrays/lists now expand before EF's concrete IntTypeMapping cast in both modes,
preserving raw values, mappings/converters, exact row counts/duplicates and ordinal positions. Empty/null
translated parameters retain typed zero-row metadata. The generator emits SELECT/UNION ALL SELECT, with
quoted aliases and Oracle DUAL in every arm. Narrow synthetic typing uses SIGNED/unqualified NUMBER;
explicit nested and Type-changing casts stay on their original path. Bare membership behavior is retained.

Focused coverage passes 40 unit and 16 intercepted cases. Full Debug/Release and isolated EF10.0.14 Release
pass **1256 tests** (950 unit, 304 intercepted pipeline, two project consumers). A subsequent test-only nested-
CAST extension passes its two existing cases in all three configurations; production/package stay unchanged.
Independent source and scoped test re-review pass. The same baseline-built
`10.0.0-checkpoint.integral-roots-20261111` nupkg passes both modes/patches for changed correlated collection
roots, empty relations and Oracle DUAL per arm. Previous text-membership package fails the parameter-root cast.
Content/package-only graph pass; baseline assets remain EF10.0.0.

Historical package: `artifacts/checkpoint-integral-roots-20261111/OceanBase.EntityFrameworkCore.10.0.0-checkpoint.integral-roots-20261111.nupkg`.
Evidence: `artifacts/local-development/batch3j/`. Native UNION typing/results/order/limits remain unverified.
Earlier conflicting inferred mapping still excludes some converted Join Value projections; native joins and
consistently inferred converted membership/property reads pass locally. Next is originally empty inline
integral roots. General text/other carriers and broader converter projections remain local work; goal active.

### Scalar text collection membership checkpoint (2026-11-11)

Bare inline/captured text array/list Contains now has EF's element JSON metadata. Oracle expands provider-text
IN parameters before the existing scalar normalizer and EF null processing, retaining raw model values,
converter/mapping identity, safe names, per-execution snapshots and raw-value padding. MySQL keeps inherited
expansion. Provider-empty and model-empty-after-suffix remain distinct; default compensation and native
relational UNKNOWN are separate policies. No JSON SQL transport or general VALUES support is implied.

Focused tests pass 25 unit and 20 intercepted cases. Debug/Release and isolated EF10.0.14 Release each pass
**1200 tests** (910 unit, 288 intercepted pipeline, two project consumers). The same baseline-built
`10.0.0-checkpoint.text-membership-20261111` nupkg passes both modes at EF10.0.0/10.0.14; the preceding package
fails the new string collection mapping. Content/package-only graph gates pass; baseline assets stay EF10.0.0.
Independent source and docs/consumer reviews passed; the consumer assertion follow-up passed scoped re-review.

Historical package: `artifacts/checkpoint-text-membership-20261111/OceanBase.EntityFrameworkCore.10.0.0-checkpoint.text-membership-20261111.nupkg`.
Evidence: `artifacts/local-development/batch3i/`. Native collation/padding/LOB equality/charset/results and
current hosted execution remain unverified. Next is captured integral general collection roots/nonempty inline
roots. Originally empty inline roots and general text/other carriers remain local work; the EF10 goal stays active.

### Integral membership and ordinary relational shapes checkpoint (2026-11-11)

Bare int/int? array/list Contains now uses EF10 scalar expansion with exact element metadata. Oracle scalar
string normalization excludes collection envelopes. The scoped compiled-query key retains the full relational
base key and adds current collection mode, fixing reproduced cross-context parameter/literal cache reuse.
Inherited ordinary/composite/.NET10 left joins, scalar subqueries, Any/All and Count/LongCount are characterized
without replacing their already-correct services. General VALUES/text collections and wider aggregates remain.

Focused tests pass 30 unit and 40 intercepted cases. Final Debug/Release and isolated EF10.0.14 Release each
pass **1155 tests** (885 unit, 268 intercepted pipeline, two project consumers). Independent code and scoped
cache-fixture reviews pass. The same baseline-built `10.0.0-checkpoint.membership-20261111` nupkg passes both
modes at EF10.0.0/10.0.14; the prior numeric package fails the new captured collection query. Content and
package-only graph checks pass; baseline source stays EF10.0.0.

An initial isolated-patch model-reference assertion exposed a retention assumption in the test fixture.
Controlled cache pressure reproduces identity failure despite equal keys/shared services. Positive retention
now uses a test-owned cache and two explicit-eviction cases verify equivalent rebuilding; every original
identity/key/connection assertion remains. The original natural failure timing is not proven; no production
cache change was made to force retention.

Historical package: `artifacts/checkpoint-membership-20261111/OceanBase.EntityFrameworkCore.10.0.0-checkpoint.membership-20261111.nupkg`.
Evidence: `artifacts/local-development/batch3h/`. Server query results and hosted execution for this historical
source remain unverified. Its next planned slice was scalar text collection membership, recorded above.

### Numeric Abs, Sign and Truncate checkpoint (2026-11-11)

Twenty-two exact Math/MathF/decimal methods now translate to native ABS/SIGN and MySQL TRUNCATE(x,0)
or Oracle unary TRUNC. Strict numeric mapping validation preserves operand converters and full existing
finite ranges; SIGN and precision-zero use independent converter-free int mappings. NULL tests remain.

Genuine RED covered 88 exact-signature/mode/null combinations. Focused GREEN passes 108 unit and 12
intercepted cases. Debug/Release and isolated EF10.0.14 Release pass **1083 tests** (853 unit, 228 intercepted,
two project consumers). Independent spec/quality review passed. The same baseline-built
`10.0.0-checkpoint.numeric-core-20261111` nupkg passes both modes at EF10.0.0/10.0.14; the preceding temporal
package fails the new numeric predicate. Package content and package-only graph checks pass.

Historical package: `artifacts/checkpoint-numeric-core-20261111/OceanBase.EntityFrameworkCore.10.0.0-checkpoint.numeric-core-20261111.nupkg`.
Evidence: `artifacts/local-development/batch3g/`. Native numeric results/domain/overflow/floating families
and hosted execution for this source remain open. Next is integral membership and ordinary relational shapes;
broader queries, model/writes/transactions/schema and the overall local EF10 goal remain active.

### Ordinary temporal parts checkpoint (2026-11-11)

Twelve exact members now translate: DateTime.Year/Month/Day/Hour/Minute/Second, DateOnly.Year/Month/Day
and TimeOnly.Hour/Minute/Second. MySQL uses native part functions; Oracle uses allowlisted EXTRACT syntax.
Oracle DATE clock extraction first casts to TIMESTAMP(7); SECOND extracts decimal then applies TRUNC.
Final results use converter-free int mappings, preserving source carriers/converters and independent NULL tests.

After the independent review's test-only follow-up, focused checks pass 21 unit and eight intercepted cases.
Full Debug/Release and isolated EF10.0.14 Release pass **963 tests** (745 unit, 216 intercepted pipeline, two consumers). Captured DateOnly
and TimeOnly values/DbType/precision, numeric/binary carrier rejection and representable endpoints are covered.
The same baseline-built `10.0.0-checkpoint.temporal-parts-20261111` package passes both modes on EF10.0.0/10.0.14;
the preceding Substring package fails the new temporal predicate. Content and package-only graph gates pass.

Historical package: `artifacts/checkpoint-temporal-parts-20261111/OceanBase.EntityFrameworkCore.10.0.0-checkpoint.temporal-parts-20261111.nupkg`.
Evidence: `artifacts/local-development/batch3f/`. Native extraction/casts/calendar/fractional/interval behavior
and hosted execution for this source remain open. Clocks/offset/fractional/Add/duration and broader queries
remain separate work. Next is numeric Abs/Sign/Truncate; the overall EF10 goal remains active.

### Substring checkpoint (2026-11-11)

Exact Substring(int) and Substring(int,int) now use native SUBSTRING/SUBSTR with long-result origin
adjustment. int.MaxValue safely becomes positive 2147483648. Dynamic bindings and receiver result mapping,
including fixed/national/LOB/converter metadata, are preserved. Function NULL tests remain observable;
the carried Oracle computed-needle NULL search regression is also covered.

Focused checks pass 12 unit and eight intercepted cases. Debug/Release and isolated EF10.0.14 Release each
pass **934 tests** (724 unit, 208 intercepted pipeline, two consumers). Independent spec/quality review passed.
The same baseline-built `10.0.0-checkpoint.substring-20261111` package passes both modes on EF10.0.0/10.0.14,
including both arities and the maximum-start boundary; the previous search package fails the new translation.
Package-content and package-only graph checks pass.

Historical package: `artifacts/checkpoint-substring-20261111/OceanBase.EntityFrameworkCore.10.0.0-checkpoint.substring-20261111.nupkg`.
Evidence: `artifacts/local-development/batch3e/`. Native range/character/padding/LOB results, including the
unverified NCLOB extension, remain server gates. Hosted CI above applies through e41981e. Next is ordinary
temporal members and wider numeric/query work; the overall EF10 goal remains active.

### String search checkpoint (2026-11-11)

Exact Contains(string), StartsWith(string) and EndsWith(string) now translate with INSTR and native suffix
extraction. Guards and CASE produce a total Boolean: NULL input is false and NOT is true in both null
settings. Known empty needles match a non-null receiver; Oracle inspects converted constants/captures before
empty-to-NULL normalization. Raw bindings and native DbFunctions are preserved; command-SQL caching stays
conservative. Converted-empty literals and unrelated unmapped methods have regression coverage.

Focused tests pass 24 query cases and eight intercepted cases. Debug/Release and isolated EF10.0.14 Release
each pass **914 tests** (712 unit, 200 intercepted pipeline, two consumers). Independent spec/quality review passed.
The same baseline-built `10.0.0-checkpoint.search-20261111` package passes both modes on EF10.0.0/10.0.14,
including search syntax and repeated captured nonempty/empty/null values. The preceding wide-decimal package
fails the new method translations. Package-content and package-only graph checks pass.

Historical package: `artifacts/checkpoint-search-20261111/OceanBase.EntityFrameworkCore.10.0.0-checkpoint.search-20261111.nupkg`.
Evidence: `artifacts/local-development/batch3d/`. Native collation/padding/character/LOB results remain live
gates; exact CLR ordinal/culture/null-exception behavior is not promised. The later e41981e hosted run is
recorded above. The Substring follow-up follows this checkpoint; wider query work and the goal remain active.

### Wide exact decimal to floating checkpoint (2026-11-11)

Batch 2d fixes the confirmed shared normalization overflow: OceanBaseDecimal values parse directly as Single
or Double using the public invariant connector representation. Direct Single rounding avoids a Double
intermediate; finite-to-infinity conversion rejects. Exact integer/decimal and native non-finite paths remain.

RED reproduced 29 unit and eight intercepted failures. Final Debug/Release and isolated EF10.0.14 Release
each pass **882 tests** (688 unit, 192 intercepted pipeline, two consumers). Independent spec/quality review
passed. The same baseline-built `10.0.0-checkpoint.wide-decimal-20261111` package passes both modes on
EF10.0.0/10.0.14, including exact Single midpoint bits 0x3f800001 and a wide Double read. The previous unary
package fails the new reader smoke. Package content and package-only graph checks pass.

Historical package: `artifacts/checkpoint-wide-decimal-20261111/OceanBase.EntityFrameworkCore.10.0.0-checkpoint.wide-decimal-20261111.nupkg`.
Evidence: `artifacts/local-development/batch2d/`. This proves normalization of synthetic wrapper values;
no new NUMBER-backed storage, live database, Linux or hosted CI acceptance is claimed for this checkpoint.
The later search slice is recorded above; the full EF10 goal remains active.

### Unary string functions/character-length checkpoint (2026-11-11)

Exact parameterless ToLower/ToUpper/Trim/TrimStart/TrimEnd and string.Length now translate through derived EF
method/member providers. SQL uses native casing/space trimming and character counts; Oracle empty/null behavior
retains conservative function nullability. Scoped services preserve plugins/DbFunctions/mode isolation. Trim
returns variable text while preserving Unicode/converters and Oracle LOB families, including lowercase spellings.

Focused tests pass 24 unit and eight intercepted cases after missing-translation and four LOB regression failures.
Debug/Release and isolated EF10.0.14 Release each pass **833 tests** (647 unit, 184 intercepted pipeline, two
consumers). Independent review passed. The same baseline-built `10.0.0-checkpoint.unary-20261111` package passes
both modes at EF10.0.0/10.0.14; the preceding arithmetic package fails the unary predicate. Package-content and
package-only graph checks pass. At this local checkpoint, no live database, Linux or hosted CI run was
recorded; the subsequent db676ff hosted evidence is recorded above.

Historical package: `artifacts/checkpoint-unary-20261111/OceanBase.EntityFrameworkCore.10.0.0-checkpoint.unary-20261111.nupkg`.
Evidence: `artifacts/local-development/batch3c/`. Native casing/space/character semantics do not promise exact CLR
culture/Unicode-whitespace/UTF-16 behavior. Wide-decimal, search and Substring follow-ups are recorded above;
wider query work remains open. The goal remains active.

### Integer division/remainder checkpoint

Integer CLR division now renders MySQL DIV or Oracle one-argument TRUNC; Oracle remainder uses MOD and MySQL
retains `%`. Classification uses the translated CLR type, including promotions and nullable values, rather than
the NUMBER declaration. Decimal/Single/Double and fractional casts retain `/`; grouping and existing mappings
are unchanged. Value-preserving numeric conversion and negative/nullable/captured operands are covered.

Focused tests pass 12 unit and four intercepted cases after RED. Debug/Release and isolated EF10.0.14 Release
each pass **801 tests** (623 unit, 176 intercepted pipeline, two consumers). Independent review passed. The same
baseline-built `10.0.0-checkpoint.arithmetic-20261110` package passes both modes on EF10.0.0/10.0.14; the previous
concat package fails the new arithmetic smoke. Package identity/assets/README/ranges and package-only graph pass.

Historical arithmetic package: `artifacts/checkpoint-arithmetic-20261110/OceanBase.EntityFrameworkCore.10.0.0-checkpoint.arithmetic-20261110.nupkg`.
Evidence: `artifacts/local-development/batch3b/`. Synthetic reads prove client generation/binding/reading, not
server arithmetic/predicate membership, zero-divisor or overflow behavior. Next local work is unary string
functions/character length and wider query support. The overall goal remains active; no live test/publication.

### String concatenation and Oracle query-null checkpoint

String addition and fixed Concat overloads now use MySQL CONCAT and Oracle grouped `||`; numeric addition is
unchanged. Oracle processing precedes EF nullability optimization, preserving potentially-null concat results
and native empty-string behavior in comparisons/coalesce. Empty/null classification uses provider-converted
values without changing caller parameters; normal string/enum/Guid converters are covered. Experimental
null-converting string query converters reject explicitly. Oracle provider-string parameters conservatively
disable command-SQL caching; model and query-compilation caching remains available.

Focused tests pass 33. Debug/Release and isolated EF10.0.14 Release each pass **785 tests** (611 unit,
172 intercepted pipeline, two project consumers). Independent spec/quality review passed. The same baseline-built
`10.0.0-checkpoint.concat-20261110` package passes both modes on EF10.0.0/10.0.14, including native concat and
changing Oracle fallback parameters. The previous numeric package fails the new syntax check. The repeatable
package-content gate and package-only dependency graph pass. These checks do not execute OceanBase functions.

Historical concat package: `artifacts/checkpoint-concat-20261110/OceanBase.EntityFrameworkCore.10.0.0-checkpoint.concat-20261110.nupkg`.
Evidence: `artifacts/local-development/batch3a/`. Next local query work includes integer division/remainder,
string search/member functions and the wider checklist. The provider remains incomplete.

Windows/Linux CI adds restore, Release tests, pack and package-content checks (`5feef06`); YAML and independent
review passed. The later db676ff hosted run is recorded above. No push, publication or database execution
was performed as part of this inspection.

### Configuration/cache/SQL boundary checkpoint

Both generic/untyped configuration overloads now have both-mode positive/negative coverage. Tests verify actual
EF model/singleton reuse for compatible options, mode separation, design-time/runtime identities, safe option
debug information, identifier/schema escaping and StringBuilder parity. Consumer provider detection covers both
modes. No production fix was needed in this batch; the existing contracts passed the added characterization tests.

Full Debug and Release each pass **606 tests** (462 unit, 142 intercepted pipeline, two consumers), up from 563.
Focused options/helper coverage passes 66 tests. The standard DatabaseFacade.GetDbConnection/SetConnectionString
paths exposed the still-missing IRelationalDatabaseCreator service; these are explicitly open CFG-04/SCH-01 work.
Underlying IRelationalConnection delayed string assignment is covered; this does not claim those facade APIs work.
No live connection was made. The previous EF10.0.14/package results below remain scoped to the text checkpoint.

### Oracle fixed-point alias checkpoint

Bare Oracle DECIMAL/NUMERIC now expose precision 38 and scale zero, retaining their trimmed declarations through
converter-only clones. Configured facets and changed-precision clones produce explicit declarations with the
same alias. Integer/Boolean/enum mappings share these defaults. Converted current decimal writes reject
fractional loss before execution; query/InputOutput inputs remain value-inferred, and bare NUMBER is unchanged.

Debug/Release on EF10.0.0 and isolated Release on EF10.0.14 each pass **627 tests** (475 unit, 150 intercepted
pipeline, two project consumers). Focused tests progressed from 10/97 unit and 6/8 pipeline failures to all passing.
Independent review passed. The same baseline-built `10.0.0-checkpoint.alias-20261110` local package passes
both-mode consumers on EF10.0.0 and EF10.0.14, including alias metadata/input/output and NUMBER independence.
The new consumer fails against the preceding text package, confirming the regression check. Nuspec identity,
net10.0 assembly, dependency ranges and package-only consumer graph were inspected. The package still lacks a
README; no publication or live database execution occurred.

Historical alias package: `artifacts/checkpoint-alias-20261110/OceanBase.EntityFrameworkCore.10.0.0-checkpoint.alias-20261110.nupkg`.
The historical text package below does not include this change. At this checkpoint, remaining scalar work included temporal
facet/clone consistency and exact integer normalization for unexpected floating reader values; see the local plan.

### Temporal facet/clone checkpoint

Both-mode temporal resolution now rejects actual size/fixed-length facets while preserving EF's parsed temporal
precision. Model properties recheck configured length and precision. Declaration clones stay within their current
transport family and retain DbType, fractional/interval-leading precision and complete SQL. Unsafe postfix/DbType
overrides reject; legitimate unchanged/converter/precision clones retain their reader and wide query-input hooks.

Final Debug/Release and isolated EF10.0.14 Release each pass **706 tests** (554 unit, 150 intercepted pipeline,
two project consumers). Focused tests pass 131 after RED and an independent-review fix round. Review confirmed
both transport and postfix findings addressed; an unnecessary public Clone override was avoided after checking
the exact EF API. Expanded consumers fail against the earlier alias/unfinished temporal probes and pass with the
same new `10.0.0-checkpoint.temporal2-20261110` package on EF10.0.0/10.0.14, both modes. Package-only dependencies,
nuspec identity/ranges, net10.0 assembly and included README were inspected. No live database claim or publication.

Historical temporal package: `artifacts/checkpoint-temporal2-20261110/OceanBase.EntityFrameworkCore.10.0.0-checkpoint.temporal2-20261110.nupkg`.
Evidence: `artifacts/local-development/batch2b/`. The first temporal probe predates review fixes and is not current
evidence. Next local scalar work is exact integer normalization and the remaining characterization cells.

### Exact integer normalization checkpoint

Finite Single/Double fractions now reject before conversion to any of the eight integer CLR types. Exact integral
values, checked ranges and non-finite failures retain existing behavior; floating CLR targets are unchanged.
Reader and generated key/enum propagation share this normalization. Property precision/scale validation now also
covers integer/Boolean mappings; four Oracle property conflicts reproduced before the fix. Nullable endpoints,
Boolean false/null and Guid.Empty use the existing scalar/EF pipeline helpers.

Focused unit tests pass 116 and generated-output tests pass ten after RED. Debug/Release and isolated EF10.0.14
Release each pass **752 tests** (590 unit, 160 intercepted pipeline, two project consumers). Independent review
passed. The same baseline-built `10.0.0-checkpoint.numeric-20261110` package passes both modes at EF10.0.0/10.0.14;
the preceding temporal2 package fails the new floating-fraction reader check. Identity/net10.0/README/ranges and
the package-only graph were inspected. No live database execution or publication.

Historical numeric package: `artifacts/checkpoint-numeric-20261110/OceanBase.EntityFrameworkCore.10.0.0-checkpoint.numeric-20261110.nupkg`.
Evidence: `artifacts/local-development/batch2c/`. Before string work, the published connector source was checked
at its nuspec revision: GetDecimal explicitly converts OceanBaseDecimal through ToDecimal, so no duplicate
provider decimal-reader transport is justified. Live decimal/wire behavior remains open. Next local work is
string concatenation/nullability and then the wider query translation inventory; scalar release gates remain open.

### Package README preparation

The provider project now owns a package README describing configuration and development/evidence limits.
PackageReadmeFile metadata and packed README content were verified using no-build packaging of the already
verified isolated alias assembly; its SHA-256 matches the package assembly. The missing-README pack message is
resolved for future packs. This metadata/documentation check does not replace source or consumer verification
after later provider changes. The preceding alias/text packages remain immutable historical artifacts.
The isolated README probe uses exact EF10.0.14 override dependencies and is only a packaging check; bounded
dependency-range and consumer compatibility evidence comes from the full-source checkpoints above. Independent review passed.

The package consumer now owns `Verify-Package.ps1`, a read-only PowerShell 7 content gate reusable in local/CI
commands. At its introduction, numeric package identity/net10.0/README/ranges passed; wrong-version, missing-README and exact-patch
probes reject as intended. This verifies the artifact structure, separately from compilation, consumers or live
server tests. Those initial checks ran on Windows; subsequent hosted evidence is recorded above.

Git line endings are now governed by `.gitattributes` (a2bbe5f): automatic text uses LF, batch scripts CRLF.
Attribute checks and normalization under core.autocrlf=true/false/input passed; existing index blobs were already LF.

The preceding implementation checkpoint was `d46ae9d`, with package evidence in `062bb84` and a paused handoff
in `20a2f18`. This text slice continues from `20a2f18` on efcore/10.0; dev retains that preceding checkpoint.
No branch synchronization, push, release tag or package publication is part of this change. `master` retains the
initial repository/version design. `efcore/8.0` and `efcore/9.0` have not been created; they are planned to fork
from an EF Core 10 release baseline and then adapt independently.

## Implemented and locally verified

Both MySQL and Oracle modes have provider registration, explicit mode options/model-cache separation,
relational connection and SQL helpers, initial query SQL and tracked insert/update/delete command paths.
The provider references published OceanBaseConnector 0.2.0 through public APIs. EF Core and Relational
dependencies remain bounded to `[10.0.0,11.0.0)`.

Recent mapping checkpoints include:

| Area | Current implementation and evidence |
| --- | --- |
| Integer and Boolean | Native MySQL widths/unsigned declarations, Oracle numeric declarations, exact reader conversion and Oracle Boolean SQL handling. |
| Binary and Guid | Explicit text/binary representations, Guid byte order, length/facet conflicts, converter composition and intercepted query/write/generated-output paths. |
| Floating point | Native binary widths, finite input checks, explicit precision/unsigned declarations, checked narrowing and mode-specific literals. |
| Temporal | Five CLR temporal types, mode-specific transports and exact conversions; query precision is independent of physical column precision. |
| Decimal (`3365ad9`) | Full supported declarations, direct/property facet conflicts, exact coefficient/scale write checks and converter-preserving clones. Query inputs retain value-inferred precision/scale; output-only parameters retain column facets. |
| National text (`d46ae9d`) | Oracle NCHAR/NVARCHAR2 select native connector parameter types. Input Size widens for longer query operands; output/null metadata retains declared Size. Enum-to-string and national Guid conversions have intercepted query/read/write coverage. |
| Text declarations and writes (`57fd44a`) | Complete mode-specific declaration parsing, length/fixed/Unicode/numeric facet conflicts, property and clone validation, LOB key/index rejection, converted DML character bounds and malformed UTF-16 rejection. Queries/original predicates remain independent of column length. |

Physical write validation applies to converted current DML values, independently of query operands and original
values used only in predicates. These implementations are partial acceptance evidence for their checklist rows;
they do not establish complete scalar, query or server support.

## Historical verification for the text slice (57fd44a)

| Check | Result | Evidence boundary |
| --- | --- | --- |
| EF Core 10.0.0 Debug suite | 563 passed: 420 unit, 142 command pipeline, one project consumer | Pipeline commands are intercepted; automatic transactions are disabled. |
| EF Core 10.0.0 Release suite | Same 563 passed, zero failed/skipped | Local source build and tests. |
| EF Core 10.0.14 isolated Release suite | Same 563 passed, zero failed/skipped | Separate package-version override and build artifacts. |
| Standalone package consumer on EF Core 10.0.0 | MySQL and Oracle passed | Uses the packed provider, without source/project references or network execution. |
| Same package on EF Core 10.0.14 | MySQL and Oracle passed | Selected patch-pair compatibility, not every current/future patch. |
| Package contents and dependency graph | Correct ID/version, net10.0 assembly, connector 0.2.0 and both EF ranges | Nuspec/ZIP and package-only consumer assets inspected. |
| Independent review | No blocking finding in declaration/facet/clone/converter/Unicode/national-binding and DML-versus-predicate paths | Read-only review; live server behavior remains unverified. |

The historical text local package is
`artifacts/checkpoint-text-20261110/OceanBase.EntityFrameworkCore.10.0.0-checkpoint.text-20261110.nupkg`.
It is an ignored local build artifact, not a published release. Package creation still reports a missing package
README. The [query/update design](efcore-10-query-update-design.md#package-checkpoint-validation) records
repeatable consumer commands. This checkpoint adds 37 unit and 16 intercepted pipeline cases. The focused
unit run initially failed 29/37 cases before implementation. Removing text write validation reproduced eight
pipeline length failures; restoring it passes the complete suite. Initial NuGet restore failed under the sandbox's
TLS credentials; restore and verification succeeded in the normal user environment without dependency edits.

## Work remaining

Tasks 3, 4 and 5 remain incomplete. Required open work includes:

- Remaining text declarations/profile extensions, server empty-string/NULL and padding behavior, character-set
  byte limits (including BYTE declarations and TEXT tiers), index byte capacity and ordinary string/LOB round trips.
- Remaining numeric/temporal/binary edge cases and live scalar evidence; Oracle decimal alias defaults now have local coverage.
- Composite/relationship/owned/shadow model coverage, generated/default/computed/schema validation and the wider
  query/update acceptance inventory.
- Opened connection ownership and lifecycle, in-flight cancellation, diagnostics/redaction and connector failure paths.
- Database creator and supported EnsureCreated/EnsureDeleted schema behavior; live key generation and transaction
  commit/rollback/caller-owned transaction behavior.
- Relational specification fixtures, a live acceptance fixture, supported-environment records and final release gates.

Migrations, reverse engineering and the other explicitly deferred checklist items retain their existing scope.
Do not turn required gaps into deferred features merely to obtain a passing release checklist.

## Next development point

The local string declaration/write slice is implemented; do not repeat it. The character-count check is deliberately
independent of unknown connection/column encodings and cannot prove BYTE/LOB capacity. Existing conservative
mapping limits are a profile, not a comprehensive OceanBase type-support claim.

Continue in this order:

1. Recheck efcore/10.0/worktree state and read the checklist plus this handoff.
2. Continue the documented local query slices after the scalar checkpoint; audit newly exposed scalar gaps as needed.
3. Define per-mode text/byte/session acceptance fixtures when live access is supplied. Cover empty/NULL,
   supplementary characters, trailing spaces, byte capacity and ordinary LOB values; preserve query transport.
4. Finish opened-connection ownership, diagnostics and cancellation, followed by the wider query/update/transaction
   and schema inventory. Repeat suites/package validation after provider changes.

The user will provide live database access later. Save local connections with .NET User Secrets under
`ConnectionStrings:OceanBaseMySql` and `ConnectionStrings:OceanBaseOracle`; do not put values in source, shell
history, documentation, logs or test artifacts. Add a stable public UserSecretsId and explicit secret loading to
the future live fixture. Obtain the authorized test-owned database/schema and cleanup scope before live DDL.
This work has not configured a live acceptance fixture or recorded a live provider database test.
