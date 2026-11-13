# EF10 Compiled Model Implementation Plan

**Goal:** Generate, compile and use ordinary both-mode EF10 compiled models.
**Architecture:** Extend EF runtime annotation generation at the provider mapping seam; reuse existing
mapping implementations and EF compiled-model services. **Spec:** [design](efcore-10-compiled-model-design.md).
**Execution:** Inline in the existing authorized feature checkout. **Base:** 87aeda8.
**Tech stack:** .NET/EF10, Connector 0.3.0, xUnit, existing EF Design tooling.

## Steps

1. [x] Add UnitTests/CompiledModelTests.cs; run actual EF model code generation for both modes and record
   RED for inaccessible/incompatible provider mapping construction. Pin ordinary scalar/converter graphs.
2. [x] Implement only the owning runtime mapping factory and design annotation-generator seam, register it
   in OceanBaseDesignTimeServices. Preserve EF comparer/converter/relational generation. Make tests GREEN.
3. [x] Compile generated models and verify UseModel query/materialization/write parameters, graph metadata,
   mode isolation and unsupported-filter failures. Add package-only dbcontext optimize/compiled-use tooling.
   Fix only reproduced defects with RED/green regressions.
4. [x] Review the module independently and fix important findings; run full Debug/Release/isolated patch,
   package/runtime/generated-model/migration/tooling/bundle gates. Update progress/inventory and commit locally.

## Review focus

- A generated model must retain its mode and reject incompatible options before query/write execution.
- Mapping reconstruction must retain provider read/write guards and every store facet, including converters.
- Generated source must compile outside this repository without internal types or hidden project references.
- EF unsupported filters/customizations must fail rather than produce incomplete compiled models.
- Ordinary noncompiled models, migrations/scaffolding and both supported EF patches must retain behavior.

## Evidence and rulings

- Previous turn: progress, split-query module implemented/reviewed/committed at 87aeda8. Full goal active.
- Inline execution and feature selection follow the user's existing authorization; no new driver work or
  dependency is requested. Native server/performance acceptance stays separate from local compiled-use evidence.
- Initial RED (compiled-model-red.log): Oracle TimeOnly's provider-valued sentinel has no C# literal
  mapping. Adding an instance converter with mapping hints reproduces incompatible internal mapping/default
  construction in both modes (compiled-model-converter-red.log). Add provider factory construction via EF's
  CreateDefaultTypeMapping seam and exact interval constructor literals in runtime/context-free design sources.
- Ruling: ordinary EF10 compiled models resolve most mappings at runtime; mapping-hint converters require
  explicit mapping code even without NativeAOT. Test that path across integer/floating/decimal/text/binary/
  temporal mappings instead of claiming ordinary generation implies every mapping construction path works.
- Further RED (compiled-model-all-mappings-red.log): generated provider converter expressions reference
  internal temporal helpers/interval JSON reader, and importing Connector introduces an enum-name ambiguity.
  Public generated-code conversion forwarding and the interval reader/writer preserve existing behavior;
  fully qualified mode literals and an integer model-mode annotation remove the ambiguity. Mapping classes
  remain internal. The factory is used only by generated metadata, not a separate mapping algorithm.
- Mode RED (compiled-model-use-red.log): compiled models could be initialized under the opposite mode.
  Tag provider-created models and validate the tag before runtime initialization, including already used
  models. Generated C#/UseModel/mapping and parameter parity, sentinel, validation, wrong-mode and visible
  named/ordinary filter rejection cases pass 11 tests (compiled-model-expanded.log).
- Package-only runtime graph smoke passes both modes sync/async using actual optimized C# models:
  compiled-model-runtime-green.log. It forbids OnModelCreating, enforces non-overlapping native readers,
  checks scalar conversions/inverse fixup/tracking, converted updates/original NULL concurrency and
  pre-write length guards/connection cleanup. An initial fixture expected a raw ArgumentException from
  SaveChanges; the existing provider correctly wraps it in DbUpdateException. Correct the expectation,
  retaining inner-exception and no-command assertions; no production write change is needed.
- Prior split-query nupkg RED: compiled-model-old-package-build.log compiles the new public fixture;
  compiled-model-old-package-red.log fails dbcontext optimize specifically at its incompatible provider
  temporal mapping construction. This is a new provider capability, independent of extra test count.
- Independent review found one P1: without mapping hints, EF emits a full composed converter and then
  re-infers the physical mapping from its provider carrier. Both-mode RED in compiled-model-no-hints-red.log
  proves MySQL DATE becomes DATETIME(6) and Oracle Boolean integer mapping becomes decimal mapping. Preserve
  explicit instance-converter property mappings in Generate(IProperty,...) via the same EF Create/Clone seam.
  Hinted/unhinted actual generation, Roslyn compilation and UseModel parity now pass 13 tests in
  compiled-model-no-hints-green.log. The package graph fixture uses unhinted DateOnly/TimeOnly/TimeSpan too.
  Wrong-mode regression initializes the correct-mode model first, then verifies reuse still rejects.
  No second actionable finding was reported. No extra review loop is needed after the confirmed fix pass.

## Final verification (2026-11-13)

- EF10.0.0 Debug/Release and isolated EF10.0.14 Release each pass **4943 tests**: 2314 unit,
  1759 functional, 868 upstream metadata specification and two consumers; zero failures/skips.
  Logs: artifacts/compiled-model-final-debug.log, compiled-model-final-release.log and
  compiled-model-final-patch.log. All three processes exit 0.
- Final baseline-built package: artifacts/compiled-model-package/
  OceanBase.EntityFrameworkCore.10.0.0-compiled-model-r2-20261113.nupkg. Content validation passes.
  The earlier package without r2 predates the converter review fix and is not the final artifact.
- Package-only runtime consumers pass both modes on both EF patches; compiled-model-consumers.log.
  Actual CLI optimize, generated-code build and compiled-model use pass both modes/patches, followed by
  full migration CLI and four Windows bundle build/help checks. Both tooling processes exit 0:
  compiled-model-tooling-10.0.0.log and compiled-model-tooling-10.0.14.log.
- Tooling scratch roots: artifacts/tooling-10.0.0-9a91a2d2687b43528c21957453147d51 and
  artifacts/tooling-10.0.14-eabf1f041e3e45ceb3c6f708ab789ffd. Runtime roots:
  artifacts/compiled-model-runtime-10.0.0 and artifacts/compiled-model-runtime-10.0.14.
- Source Release, nupkg entry, both runtime and both tooling Provider DLLs have SHA256
  5E8D3EC8992AC942E857757D56957D8D9DF478D9344EFF4A67296BCE0775E29F.
  Runtime/Design resolve the selected EF patch, Connector resolves 0.3.0, and consumer graphs have no
  project dependencies. Runtime consumers have no Design dependency.
- Native execution, startup/memory/performance claims, NativeAOT and precompiled queries are unverified.
  Ordinary compiled models do not remove EF's query-filter restrictions. No new live probe, driver
  change, push, tag or publication is part of this module. The overall EF10 goal remains active.
