# `DateTimeOffset.Offset` Implementation Plan

**Goal:** Translate Oracle `DateTimeOffset.Offset` to the existing `TimeSpan` carrier and reject the lossy MySQL case before command execution.

**Architecture:** Extend the existing temporal member translator with one internal marker whose result uses the provider's `TimeSpan` mapping. Extend the existing SQL generator marker dispatch to render Oracle `NUMTODSINTERVAL` from signed timezone hour/minute extraction. No Connector or public API changes.

**Tech Stack:** C#, .NET 10, EF Core/Relational `[10.0.0,11.0.0)`, OceanBaseConnector 0.3.0, xUnit.

**Spec:** `docs/efcore/efcore-10-date-time-offset-offset-plan.md`

## Global Constraints

- Support MySQL and Oracle modes independently; MySQL cannot recover the original offset from UTC-normalized `DATETIME`.
- Admit only non-null-converting Oracle `TIMESTAMP WITH TIME ZONE` mappings and the existing `TimeSpan`/`INTERVAL` result mapping.
- Do not change Connector source, package dependencies, EF8/EF9 scope, or live endpoint behavior.
- Intercepted readers prove EF translation/materialization only; they do not prove native OceanBase execution.

## Review Focus

- Negative offsets: preserve the sign when combining timezone hour and minute extraction.
- NULL input: propagate NULL through the interval expression and reader.
- Wrong carriers/converters: reject before SQL generation and before connection opening.
- MySQL lossy storage: reject `Offset` instead of returning an invented zero offset.
- Result mapping: keep Oracle interval and MySQL TIME mapping validation aligned with existing `TimeSpan` markers.

### Task 1: Add RED unit and functional coverage

**Files:**
- Modify: `tests/OceanBase.EntityFrameworkCore.UnitTests/TemporalMemberQueryTests.cs`
- Modify: `tests/OceanBase.EntityFrameworkCore.FunctionalTests/TimeSpanTicksFunctionalTests.cs`

- [x] Add unit cases for Oracle translation, negative/positive offset SQL marker, NULL propagation, MySQL rejection, unrelated store type, and null-converting converter.
- [x] Run the focused unit/functional filters and verify the new Oracle cases fail because `Offset` is untranslated while existing controls pass.

### Task 2: Implement the minimal marker translation

**Files:**
- Modify: `src/OceanBase.EntityFrameworkCore/Query/OceanBaseTemporalMemberTranslator.cs`

- [x] Add the `DateTimeOffset.Offset` member identity and an internal marker returning `TimeSpan` with the existing mapping.
- [x] Require Oracle mode, `DateTimeOffset` input, non-null-converting mapping, and the existing carrier validation; return `null` for MySQL or unsupported carriers.
- [x] Rerun the focused unit filter and verify the unit cases pass (3/3).

### Task 3: Render Oracle interval SQL and run functional readers

**Files:**
- Modify: `src/OceanBase.EntityFrameworkCore/Query/OceanBaseQuerySqlGenerator.cs`
- Modify: `tests/OceanBase.EntityFrameworkCore.FunctionalTests/TimeSpanTicksFunctionalTests.cs`

- [x] Render `NUMTODSINTERVAL(EXTRACT(TIMEZONE_HOUR FROM value) * 60 + EXTRACT(TIMEZONE_MINUTE FROM value), 'MINUTE')` after validating marker/result/input mappings.
- [x] Add sync/async Oracle reader rows for positive, negative, and NULL offsets plus predicate/order-by assertions; keep a MySQL pre-open rejection control.
- [x] Run the focused functional filter and verify all cases pass (3/3).

### Task 4: Package smoke and documentation

**Files:**
- Modify: `tests/OceanBase.EntityFrameworkCore.PackageConsumer/Program.cs`
- Modify: `docs/efcore/efcore-10-feature-checklist.md`
- Modify: `docs/efcore/efcore-10-development-acceptance-plan.md`
- Modify: `docs/efcore/efcore-10-feature-checklist.md`
- Modify: `docs/efcore/efcore-10-progress.md`
- Modify: `docs/efcore/efcore-10-roadmap.md`

- [x] Add package SQL assertions for Oracle translation and MySQL rejection.
- [x] Run full Release Unit (2540), Functional (2463), Specification (1538 plus 16 existing skips), ConsumerTests (2/2), package content verification and EF10.0.0/10.0.14 both-mode package consumers.
- [x] Record exact commands/results and keep live status `Blocked-live` because the pre-auth packet closure persists.

### Task 5: Review and commit

- [x] Run `git diff --check`, inspect the connected diff and request an independent read-only review.
- [x] Fix any Critical/Important findings, rerun affected tests, and create one local conventional commit for the module.
