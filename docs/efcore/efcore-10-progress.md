# EF Core 10 Development Progress

## Resumed text checkpoint: 2026-11-10

Development resumed at the user's request on efcore/10.0, now the active EF10 development and packaging branch.
The provider is not release-ready. The [feature checklist](efcore-10-feature-checklist.md) remains the
acceptance authority; this document records the handoff rather than defining a different completion target.

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
| Text declarations and writes (current slice) | Complete mode-specific declaration parsing, length/fixed/Unicode/numeric facet conflicts, property and clone validation, LOB key/index rejection, converted DML character bounds and malformed UTF-16 rejection. Queries/original predicates remain independent of column length. |

Physical write validation applies to converted current DML values, independently of query operands and original
values used only in predicates. These implementations are partial acceptance evidence for their checklist rows;
they do not establish complete scalar, query or server support.

## Verification recorded for the resumed slice

| Check | Result | Evidence boundary |
| --- | --- | --- |
| EF Core 10.0.0 Debug suite | 563 passed: 420 unit, 142 command pipeline, one project consumer | Pipeline commands are intercepted; automatic transactions are disabled. |
| EF Core 10.0.0 Release suite | Same 563 passed, zero failed/skipped | Local source build and tests. |
| EF Core 10.0.14 isolated Release suite | Same 563 passed, zero failed/skipped | Separate package-version override and build artifacts. |
| Standalone package consumer on EF Core 10.0.0 | MySQL and Oracle passed | Uses the packed provider, without source/project references or network execution. |
| Same package on EF Core 10.0.14 | MySQL and Oracle passed | Selected patch-pair compatibility, not every current/future patch. |
| Package contents and dependency graph | Correct ID/version, net10.0 assembly, connector 0.2.0 and both EF ranges | Nuspec/ZIP and package-only consumer assets inspected. |
| Independent review | No blocking finding in declaration/facet/clone/converter/Unicode/national-binding and DML-versus-predicate paths | Read-only review; live server behavior remains unverified. |

The latest local package is
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
- Unqualified Oracle decimal alias defaults, remaining numeric/temporal/binary edge cases and live scalar evidence.
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
2. Finish remaining local scalar gaps, including unqualified Oracle decimal aliases, before broadening query claims.
3. Define per-mode text/byte/session acceptance fixtures when live access is supplied. Cover empty/NULL,
   supplementary characters, trailing spaces, byte capacity and ordinary LOB values; preserve query transport.
4. Finish opened-connection ownership, diagnostics and cancellation, followed by the wider query/update/transaction
   and schema inventory. Repeat suites/package validation after provider changes.

The user will provide live database access later. Save local connections with .NET User Secrets under
`ConnectionStrings:OceanBaseMySql` and `ConnectionStrings:OceanBaseOracle`; do not put values in source, shell
history, documentation, logs or test artifacts. Add a stable public UserSecretsId and explicit secret loading to
the future live fixture. Obtain the authorized test-owned database/schema and cleanup scope before live DDL.
This work has not configured a live acceptance fixture or recorded a live provider database test.
