# EF10 Compiled Models

**Date:** 2026-11-13. **Base:** 87aeda8. **IDs:** DEF-08, CFG-03/04, MAP-01..07, QRY-01/08,
UPD-01..05, PKG-03/04. Continue the authorized local EF10 development sequence on efcore/10.0.

## Intent and approach

Support EF's ordinary compiled-model workflow: generate C# with dbcontext optimize, compile that code,
and execute ordinary query/write pipelines using UseModel in both modes. Preserve provider mapping types,
store declarations/facets, conversions/comparers, graph metadata and native parameter validation. A model
is generated separately for each compatibility mode; using a model with the wrong mode must fail clearly.

Reuse EF10's runtime annotation/model generators and mapping cloning. Supply only the provider mapping
construction seam required by generated code; avoid making every internal mapping/constructor public or
maintaining a parallel model generator. Inspect exact EF10 CreateDefaultTypeMapping/Create contracts before
implementation. Provider-owned conversion behavior should be reconstructed by provider runtime code, not
copied as inaccessible C# expressions. No new runtime dependency or driver API is needed.

Alternatives: expose all mapping internals/default instances (unnecessary public API and mode ambiguity),
or replace model generation entirely (duplicates EF metadata/relationship services). A small generator
extension plus a public generated-code factory preserves the existing mapping implementation.

## Acceptance and boundaries

Prove unsupported generation before changing production. Then test actual EF code generation and compile
the emitted C#; verify mapping identity/facets, built-in and user converters, nullable values/comparers,
scalar parameters/reader materialization, graph loading and tracked writes. Exercise sync/async, both modes
and EF10.0.0/10.0.14 against one baseline-built nupkg. Assert generated code avoids internal provider types
and context construction; keep package-only consumers. Include rejected wrong-mode use and EF's documented
unsupported compiled-model cases (for example query filters), without silently dropping metadata.

Compiled models do not imply NativeAOT, precompiled queries, lazy-loading proxies, arbitrary JSON/complex
collections or every EF optimization. Those remain independent inventory. Runtime behavior is proved with
native doubles; physical SQL/results, performance/startup measurements and server acceptance remain open.
References: EF10 CSharpRuntimeAnnotationCodeGenerator and RelationalCSharpRuntimeAnnotationCodeGenerator,
decompiled from the exact 10.0.0 packages under artifacts/compiled-model-contract; official
[compiled models](https://learn.microsoft.com/ef/core/performance/advanced-performance-topics#compiled-models)
and [dbcontext optimize](https://learn.microsoft.com/ef/core/cli/dotnet#dotnet-ef-dbcontext-optimize).

## Rulings

Existing authorization covers implementation choices and inline execution; do not reopen approval for each
module. The previous split-query feature was committed at 87aeda8 with a clean tree and complete local
tests/package evidence. This module advances the remaining DEF-08 capability, not native/release completion.
