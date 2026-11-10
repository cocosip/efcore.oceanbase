# EF Core 10 Development Progress

## Paused checkpoint: 2026-11-10

Development is paused at the user's request. Do not resume implementation until the user requests it.
The provider is not release-ready. The [feature checklist](efcore-10-feature-checklist.md) remains the
acceptance authority; this document records the handoff rather than defining a different completion target.

The implementation checkpoint is `d46ae9d`; its package evidence was committed as `062bb84`. Both local
`dev` and `efcore/10.0` contain that checkpoint. This documentation update is saved to both branches as well.
`dev` is the integration branch and `efcore/10.0` is the independent packaging branch. `master` retains the
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

Physical write validation applies to converted current DML values, independently of query operands and original
values used only in predicates. These implementations are partial acceptance evidence for their checklist rows;
they do not establish complete scalar, query or server support.

## Verification recorded before the pause

| Check | Result | Evidence boundary |
| --- | --- | --- |
| EF Core 10.0.0 Debug suite | 510 passed: 383 unit, 126 command pipeline, one project consumer | Pipeline commands are intercepted; automatic transactions are disabled. |
| EF Core 10.0.0 Release suite | Same 510 passed, zero failed/skipped | Local source build and tests. |
| EF Core 10.0.14 isolated Release suite | Same 510 passed, zero failed/skipped | Separate package-version override and build artifacts. |
| Standalone package consumer on EF Core 10.0.0 | MySQL and Oracle passed | Uses the packed provider, without source/project references or network execution. |
| Same package on EF Core 10.0.14 | MySQL and Oracle passed | Selected patch-pair compatibility, not every current/future patch. |
| Package contents and dependency graph | Correct ID/version, net10.0 assembly, connector 0.2.0 and both EF ranges | Nuspec/ZIP and package-only consumer assets inspected. |
| Independent reviews | Decimal direct bare-UNSIGNED clone issue fixed with failing-then-passing regressions; no remaining confirmed finding in either recent slice | Live server behavior remains unverified. |

The latest local package is
`artifacts/checkpoint-d46ae9d/OceanBase.EntityFrameworkCore.10.0.0-checkpoint.d46ae9d.nupkg`.
It is an ignored local build artifact, not a published release. Package creation still reports a missing package
README. The [query/update design](efcore-10-query-update-design.md#package-checkpoint-validation) records
repeatable consumer commands. This handoff changes documentation only; it does not claim a new test run.

## Work remaining

Tasks 3, 4 and 5 remain incomplete. Required open work includes:

- String declaration parsing, explicit length/facet/key/index rules, converted write length checks, valid/invalid
  Unicode, empty-string/NULL behavior, character-set byte limits and ordinary string/LOB round trips.
- Unqualified Oracle decimal alias defaults, remaining numeric/temporal/binary edge cases and live scalar evidence.
- Composite/relationship/owned/shadow model coverage, generated/default/computed/schema validation and the wider
  query/update acceptance inventory.
- Opened connection ownership and lifecycle, in-flight cancellation, diagnostics/redaction and connector failure paths.
- Database creator and supported EnsureCreated/EnsureDeleted schema behavior; live key generation and transaction
  commit/rollback/caller-owned transaction behavior.
- Relational specification fixtures, a live acceptance fixture, supported-environment records and final release gates.

Migrations, reverse engineering and the other explicitly deferred checklist items retain their existing scope.
Do not turn required gaps into deferred features merely to obtain a passing release checklist.

## Exact resume point

The last continuation only inspected string mapping code and pinned V4.3.5 documentation. No string declaration
parser, text write validator or new string-length tests were implemented after `062bb84`. Research downloads
remain in ignored inspection scratch; the owning source and tracked contracts must be checked again on resume.

After the user resumes development:

1. Recheck branch/worktree state and read the checklist plus this handoff.
2. Define the MAP-03/MAP-06/MAP-07 string declaration/write contract in the technical baseline before code changes.
   Separate declared character limits, logical property lengths and environment-dependent byte capacity. Preserve
   lossless query operand binding and the existing native national parameter hooks.
3. Add failing declaration/property/clone/converter/query/write tests, implement the connected paths and review
   the result. Record unsupported and missing-environment cases explicitly.
4. Run the baseline and selected-patch suites; synchronize the tested checkpoint from dev to efcore/10.0 and pack
   there. Validate the same package with both selected EF patches before updating package acceptance evidence.

The user will provide live database access later. Save local connections with .NET User Secrets under
`ConnectionStrings:OceanBaseMySql` and `ConnectionStrings:OceanBaseOracle`; do not put values in source, shell
history, documentation, logs or test artifacts. Add a stable public UserSecretsId and explicit secret loading to
the future live fixture. Obtain the authorized test-owned database/schema and cleanup scope before live DDL.
This work has not configured a live acceptance fixture or recorded a live provider database test.
