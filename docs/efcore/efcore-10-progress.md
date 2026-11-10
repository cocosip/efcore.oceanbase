# EF Core 10 Development Progress

## Active local development: 2026-11-10

Development resumed at the user's request on efcore/10.0, now the active EF10 development and packaging branch.
The provider is not release-ready. The [feature checklist](efcore-10-feature-checklist.md) remains the
acceptance authority; this document records the handoff rather than defining a different completion target.

The current user goal is to finish all locally developable EF10 functionality, with tested/reviewed commits after
each small group. Real database integration/tests and EF8/EF9 adaptation are explicitly postponed. The
[local development plan](efcore-10-local-development-plan.md) tracks the full remaining inventory, including
an audit of locally feasible deferred families. Finishing one batch does not finish the overall goal.

### Unary string functions/character-length checkpoint (2026-11-11)

Exact parameterless ToLower/ToUpper/Trim/TrimStart/TrimEnd and string.Length now translate through derived EF
method/member providers. SQL uses native casing/space trimming and character counts; Oracle empty/null behavior
retains conservative function nullability. Scoped services preserve plugins/DbFunctions/mode isolation. Trim
returns variable text while preserving Unicode/converters and Oracle LOB families, including lowercase spellings.

Focused tests pass 24 unit and eight intercepted cases after missing-translation and four LOB regression failures.
Debug/Release and isolated EF10.0.14 Release each pass **833 tests** (647 unit, 184 intercepted pipeline, two
consumers). Independent review passed. The same baseline-built `10.0.0-checkpoint.unary-20261111` package passes
both modes at EF10.0.0/10.0.14; the preceding arithmetic package fails the unary predicate. Package-content and
package-only graph checks pass. No live database, Linux or hosted CI run occurred.

Latest package: `artifacts/checkpoint-unary-20261111/OceanBase.EntityFrameworkCore.10.0.0-checkpoint.unary-20261111.nupkg`.
Evidence: `artifacts/local-development/batch3c/`. Native casing/space/character semantics do not promise exact CLR
culture/Unicode-whitespace/UTF-16 behavior. Next is the confirmed Batch 2d wide-decimal floating-reader fix,
followed by Batch 3d search and wider query work. The goal remains active.

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

Windows/Linux CI now has restore, Release tests, pack and package-content checks (`5feef06`); YAML and independent
review pass. Hosted runs/Linux execution remain unverified. No push, publication or database execution occurred.

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
commands. Current numeric package identity/net10.0/README/ranges pass; wrong-version, missing-README and exact-patch
probes reject as intended. This verifies the artifact structure, separately from compilation, consumers or live
server tests. Windows execution is verified; no Linux/CI execution is claimed.

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
