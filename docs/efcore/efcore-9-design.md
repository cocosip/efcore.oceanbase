# EF Core 9 Provider Design

## Goal

Port the completed EF Core 8 provider to EF Core 9 without changing the public package identity or the documented
MySQL/Oracle behavior unless EF Core 9 requires a deliberate compatibility change.

## Porting rules

1. Read the EF Core 9 what's-new and breaking-change documents listed in official-references.md.
2. Compile the provider against the exact EF Core 9 dependency range before changing shared code.
3. Compare every provider service registration and internal API use with the EF Core 8 branch.
4. Keep EF9-only adapters in the efcore-9 branch. Backport only changes that are EF-independent and tested on the
   EF8 branch.
5. Run the same MySQL and Oracle feature profile as EF8 before adding EF9-specific features.

## Expected implementation boundary

The port must preserve options, explicit compatibility mode, model-cache identity, common type mappings, basic
query translation, update SQL, key retrieval and transactions. Changes to query translation, JSON, primitive
collections, seeding, compiled models or other EF9 features require a separate design entry and tests; they are
not inherited automatically from the EF Core release notes.

## Package and runtime

- Branch: efcore-9.
- Package: OceanBase.EntityFrameworkCore 9.x.
- EF dependency: the 9.x range only.
- Initial asset: net8.0 if the EF9 support matrix permits it.

## Verification gates

The EF9 branch is releasable only when the EF8 behavioral suite passes against the EF9 provider, mode-specific SQL
tests pass for MySQL and Oracle, and package-consumer tests prove that the 9.x package does not resolve EF8 or EF10
provider dependencies.
