# `DateTimeOffset.Ticks` Implementation Plan

**Status:** Local Provider and package gates complete; native/live acceptance remains blocked by the known Connector handshake failure.

**Goal:** Translate Oracle `DateTimeOffset.Ticks` by composing a local `TIMESTAMP` conversion with the existing Gregorian `DateTime.Ticks` translator, while rejecting lossy MySQL storage.

**Architecture:** Add one member identity branch that validates the existing Oracle `DateTimeOffset` carrier, converts it to the existing `DateTime` query mapping, and calls the existing `DateTimeTicksMarker`. No new SQL arithmetic or Connector API is introduced.

**Tech Stack:** C#, .NET 10, EF Core/Relational `[10.0.0,11.0.0)`, OceanBaseConnector 0.3.0, xUnit.

**Spec:** `docs/efcore/efcore-10-date-time-offset-ticks-plan.md`

## Global Constraints

- Oracle supports local wall-clock ticks from `TIMESTAMP WITH TIME ZONE`; MySQL rejects the lossy UTC-normalized carrier.
- Keep existing `DateTimeTicksMarker` arithmetic and mapping validation; do not duplicate epoch formulas.
- Intercepted readers prove Provider behavior only; native Oracle timezone/session semantics remain open.

## Review Focus

- Positive and negative offsets must use local wall-clock ticks, not UTC ticks.
- NULL input must stay NULL through the converted marker.
- MySQL must reject before opening a connection rather than return UTC ticks.
- Wrong store carriers and null-converting converters must not enter the marker.
- SQL must not contain `SYS_EXTRACT_UTC` for this member.

### Task 1: Add RED tests

**Files:**
- Modify: `tests/OceanBase.EntityFrameworkCore.UnitTests/TemporalMemberQueryTests.cs`
- Modify: `tests/OceanBase.EntityFrameworkCore.FunctionalTests/TimeSpanTicksFunctionalTests.cs`

- [x] Add Oracle SQL-shape and MySQL rejection cases, plus unsupported/null-converting carrier cases.
- [x] Add Oracle sync/async reader cases with positive and negative local offsets and NULL.
- [x] Run focused filters and verify the post-implementation Unit 3/3 and Functional 2/2 filters pass.

### Task 2: Compose the existing DateTime tick translator

**Files:**
- Modify: `src/OceanBase.EntityFrameworkCore/Query/OceanBaseTemporalMemberTranslator.cs`

- [x] Admit only Oracle `DateTimeOffset.Ticks` with the existing non-null-converting date carrier.
- [x] Convert the input to the existing `DateTime` mapping and return `DateTimeTicksMarker` with the Int64 mapping.
- [x] Keep MySQL and unsupported carriers untranslated; rerun focused Unit tests.

### Task 3: Package and documentation gates

**Files:**
- Modify: `tests/OceanBase.EntityFrameworkCore.PackageConsumer/Program.cs`
- Modify: `docs/efcore/efcore-10-feature-checklist.md`
- Modify: `docs/efcore/efcore-10-development-acceptance-plan.md`
- Modify: `docs/efcore/efcore-10-progress.md`
- Modify: `docs/efcore/efcore-10-roadmap.md`

- [x] Add both-mode package SQL assertions, including Oracle no-UTC extraction and MySQL rejection.
- [x] Run full Release Unit 2543/Functional 2466/Specification 1538 (16 existing skips), ConsumerTests 2/2,
  package content verification and both EF10 patch consumers.
- [x] Record the unchanged live Connector status without repeating the known probe.

### Task 4: Review and commit

- [x] Run `git diff --check`, complete a code walk, fix any Critical/Important findings, and commit locally.
