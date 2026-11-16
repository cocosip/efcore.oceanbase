# `DateTimeOffset.DateTime` Implementation Plan

> **For agentic workers:** Use this plan task-by-task. Keep the RED/GREEN, review, package, documentation and local commit gates explicit.

**Status:** Local Provider and package gates complete; native/live acceptance remains blocked by the known Connector handshake failure.

**Goal:** Translate Oracle `DateTimeOffset.DateTime` to the existing local `DateTime` carrier and reject lossy MySQL storage.

**Architecture:** Add one member identity branch to `OceanBaseTemporalMemberTranslator` that validates the existing Oracle
`DateTimeOffset` carrier and converts it to the existing `DateTime` mapping. Reuse the provider's existing SQL conversion
and validation paths; add no Connector API or new dependency.

**Tech Stack:** C#, .NET 10, EF Core/Relational `[10.0.0,11.0.0)`, OceanBaseConnector 0.3.0, xUnit.

**Spec:** `docs/efcore/efcore-10-date-time-offset-date-time-plan.md`

## Global Constraints

- Oracle keeps local wall-clock values in `TIMESTAMP WITH TIME ZONE`; MySQL stores UTC-normalized `DATETIME`.
- MySQL and unsupported mappings must fail before SQL execution, with standard EF translation errors.
- Reuse existing `DateTime` mappings and SQL generator validation; do not change Connector code.
- Intercepted readers prove Provider behavior only; native timezone/session semantics remain open.

## Review Focus

- Positive and negative offsets must produce the same local clock value when their wall-clock fields match.
- The result must be a `DateTime` with no accidental UTC conversion; NULL must remain NULL.
- MySQL and unrelated/null-converting carriers must reject before a command opens.
- The generated Oracle SQL must use the existing timestamp cast and never `SYS_EXTRACT_UTC`.
- `LocalDateTime` must remain outside this module because it depends on the execution environment timezone.

### Task 1: Add RED tests

**Files:**
- Modify: `tests/OceanBase.EntityFrameworkCore.UnitTests/TemporalMemberQueryTests.cs`
- Modify: `tests/OceanBase.EntityFrameworkCore.FunctionalTests/TimeSpanTicksFunctionalTests.cs`

- [x] Add Oracle SQL-shape, MySQL rejection, unsupported-carrier and null-converting-carrier cases.
- [x] Add Oracle sync/async reader rows for positive/negative offsets and NULL, asserting `Kind.Unspecified`.
- [x] Run focused filters and confirm the Oracle positive path was untranslatable before implementation.

### Task 2: Add the minimal translator branch

**Files:**
- Modify: `src/OceanBase.EntityFrameworkCore/Query/OceanBaseTemporalMemberTranslator.cs`

- [x] Admit only Oracle `DateTimeOffset.DateTime` with the existing non-null-converting date carrier.
- [x] Convert the input to the existing `DateTime` mapping and preserve nullable propagation.
- [x] Keep MySQL and unsupported carriers untranslated; rerun focused Unit 3/3 and Functional 3/3 tests.

### Task 3: Package and documentation gates

**Files:**
- Modify: `tests/OceanBase.EntityFrameworkCore.PackageConsumer/Program.cs`
- Modify: `docs/efcore/efcore-10-feature-checklist.md`
- Modify: `docs/efcore/efcore-10-development-acceptance-plan.md`
- Modify: `docs/efcore/efcore-10-progress.md`
- Modify: `docs/efcore/efcore-10-release-workboard.md`
- Modify: `docs/efcore/efcore-10-roadmap.md`

- [x] Add both-mode package SQL assertions, including Oracle cast/no-UTC extraction and MySQL rejection.
- [x] Run full Release Unit 2546/Functional 2469/Specification 1538 (16 existing skips), ConsumerTests 2/2,
  package verification and both EF10 patch consumers.
- [x] Record unchanged live Connector status without repeating the known probe.

### Task 4: Review and commit

- [x] Run `git diff --check`, complete a code walk, fix Critical/Important findings, and commit locally.
