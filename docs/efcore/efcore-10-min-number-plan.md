# EF Core 10 `MinNumber`/`MaxNumber` Plan

**Status:** Local implementation complete; native OceanBase and release gates remain open.
**Date:** 2026-11-16
**Feature IDs:** `QRY-07`, `QRY-11`, `MAP-02`, `MAP-07`

## Contract

Translate the exact `float.MinNumber/MaxNumber` and `double.MinNumber/MaxNumber` overloads through
the provider's existing scalar extrema translator.

- MySQL and Oracle emit `LEAST`/`GREATEST` while preserving the first operand's mapping.
- Only finite numeric carriers admitted by the existing floating-point mapping are covered; non-finite
  parameter values remain rejected before command execution.
- Other numeric types, custom methods and unsupported mappings continue to fail translation.
- No Connector API or dependency changed.

## Implementation and tests

The RED unit queries failed in all four overloads because EF reported translation failure for the
`float`/`double` methods. The provider now registers the four exact numeric-type methods beside the
existing `Math.Min`/`Math.Max` identities.

Focused evidence:

- `ScalarExtremaQueryTests`: 47 passed, 0 failed.
- `ScalarExtremaNumberFunctionalTests`: 4 passed, 0 failed, covering MySQL/Oracle and sync/async
  intercepted materialization.
- Full Release: Unit 2,399, Functional 1,935 and Specification 1,452 passed; 16 specification cases
  remain explicitly mapped skips. Debug Unit/Functional also pass 2,399/1,935.
- Package `10.0.0-min-number-20261116` passed `Verify-Package.ps1` and the packed PackageConsumer on
  EF10.0.0/10.0.14 in both modes using the isolated Connector 0.3.0 feed.

These tests establish local translation, mapping, parameter and reader behavior only. They do not prove
OceanBase NaN handling, signed-zero behavior or native `LEAST`/`GREATEST` results.

## Acceptance checklist

- [x] Exact float/double overloads translate in both modes.
- [x] Sync and async intercepted materialization passes in both modes.
- [x] Existing unsupported method and carrier boundaries remain rejected.
- [x] Package consumer SQL assertions are updated.
- [x] Run the full Debug/Release and selected EF10 patch/package gates.
- [ ] Run non-destructive native floating-point queries in both declared OceanBase tenants.

## Remaining risk

The supplied live endpoint still closes during pre-authentication, so native NaN, signed-zero,
coercion and result semantics remain unverified. The feature remains `In progress` in the Required
matrix until the native and release gates close.
