# EF10 Lazy Loading Implementation Plan

**Goal:** Finish the both-mode local lazy-loading integration module with reproducible public workflows.
**Architecture:** Reuse EF's loader/proxy services and provider queries/writes; fix only reproduced defects.
**Spec:** [design](efcore-10-lazy-loading-design.md). **Base:** 80ce2b1.
**Execution:** Inline in the current authorized checkout. One module, one final reviewed local commit.
**Tech stack:** .NET/EF10, optional EF Proxies 10.x, Connector 0.3.0, xUnit and existing native doubles.

## Steps

1. [x] Add the optional official Proxies reference to FunctionalTests and PackageConsumer, version-aligned
   with selected EF. Add LazyLoadingTests.cs using actual query materialization and independently checked
   lazy SQL/binds. Cover proxy and injected-loader collections/references, tracking/fixup, repeated/empty/
   null/disabled/eager paths and current filters. Run focused tests; classify existing behavior honestly.
2. [x] Add lifecycle and write cases: disposal, detach, caller-open ownership, failed/cancelled lazy load
   followed by retry and tracked updates with nullable concurrency. Add public package LazyLoadingSmoke.cs
   and Program dispatch. If a provider defect is exposed, record RED and fix its shared owning code.
3. [x] Review the whole module independently; fix important findings with focused verification. Run full
   baseline Debug/Release and isolated EF10.0.14 Release, package content and both-mode/patch consumer gates.
   Reuse the unchanged final compiled-model nupkg if production/provider README is not changed; otherwise
   pack one baseline-built artifact and verify consumers/hashes. Record exact dependency graphs.
4. [x] Update provider usage notes, progress/checklist/roadmap and this execution record, inspect the final
   diff, commit this module locally and verify a clean worktree. Do not imply native acceptance or release.

## Review focus

- Canned rows must not hide missing/wrong FK predicates, current filter parameters or projection order.
- Getter-triggered lazy queries remain sync after async roots; test that boundary explicitly.
- Untracked entities, detach/dispose and inverse fixup must not trigger recursive hidden queries.
- Failed/cancelled loads must remain retryable, with readers/connections released and caller leases kept.
- Proxies must remain optional and version-compatible; public consumers must use the packed binary.

## Evidence and rulings

- Clean base 80ce2b1 checked. No existing lazy-loading tests or provider-specific proxy implementation.
- Installed official Proxies 10.0.0/10.0.14 and Castle.Core 5.2.1 are available; use the cached packages.
- No unchanged native probe rerun: its recorded pre-auth failure cannot prove lazy SQL behavior.
- Initial focused tests pass against existing provider code; this is integration verification, not a new
  loader or translator. No production RED was reproduced, so do not invent a provider fix.
- Initial restore mistakenly omitted the supplied Connector feed; artifacts/lazy-loading-nuget.config
  restores cached EF/Proxies and supplied Connector 0.3.0 without any driver source reference.
- Expanded write fixtures initially observed only reader execution; the provider correctly writes through
  ExecuteNonQuery. Capture that boundary and match Oracle named parameters and MySQL positional bindings
  to actual predicates/assignments. No provider change is required.
- First full suites exposed ManyServiceProvidersCreatedWarning due to new test-option variants sharing
  EF's process-wide service cache. Match existing runtime fixtures by disabling service-provider caching
  only in these test contexts; do not suppress the warning or alter production caching. Final suites rerun.
- Independent review found two Important verification gaps: injected reference loading and a non-null FK
  with zero target rows. Add actual injected references and both proxy/injected empty-target cases, with
  loaded-state/no-repeat/query-bind checks. Also strengthen the packed smoke's FK/filter/write assertions
  to associate each placeholder with its actual bound value. Focused review fix passes 64 cases, zero skips.
- Separate capabilities set aside by review: native consistency/performance, streaming nested loads,
  change-tracking proxies, proxy/compiled-model combinations, NativeAOT and concurrent contexts. These are
  explicitly outside this module; no existing broader acceptance requirement is marked complete.

## Final verification (2026-11-13)

- Baseline EF10.0.0 Debug/Release and isolated EF10.0.14 Release each pass **5007 tests**: 2314 unit,
  1823 functional, 868 metadata specification and two consumers; zero failures/skips. All three processes
  exit 0. Logs: artifacts/lazy-loading-final-debug.log, lazy-loading-final-release.log and
  lazy-loading-final-patch.log. Focused 64-case log: lazy-loading-review-green.log.
- Source test assets resolve matching EF/Proxies 10.0.0 and 10.0.14, respectively. The isolated patch uses
  artifacts/lazy-loading-ef10.0.14/Directory.Packages.props and its build directory; baseline assets remain
  EF10.0.0. Restore uses artifacts/lazy-loading-nuget.config and cached packages/supplied Connector feed.
- Provider runtime source is unchanged. The updated package README documents both optional lazy-loading
  workflows, synchronous getter loading, untracked/lifetime behavior, N+1 and the single-reader boundary.
- Baseline-built package: artifacts/lazy-loading-package/
  OceanBase.EntityFrameworkCore.10.0.0-lazy-loading-20261113.nupkg. Verify-Package.ps1 passes net10.0,
  identity/README, EF [10.0.0,11.0.0) and exactly three runtime dependencies. Neither Proxies nor Castle
  is a provider dependency.
- artifacts/verify-lazy-loading-consumers.ps1 passes existing and new public smokes in both modes at both
  patches; lazy-loading-consumers.log. New proxy smokes cover sync/async roots, tracked/untracked graphs,
  filter/FK bindings, inverse fixup, no repeat queries and nullable-concurrency writes.
  Runtime roots: artifacts/lazy-loading-runtime-10.0.0 and lazy-loading-runtime-10.0.14. Their package-only
  graphs resolve matching EF/Relational/Proxies, Castle.Core 5.2.1 and Connector 0.3.0, with no project or
  Design dependency. Source Release, archive and both consumers share Provider SHA256
  C2C7B480B886B8C894DC12EC2E3B7A9EB31AAB33DF4735C9131A53DB78F9D783.
- The binary differs from the preceding module's artifact because AssemblyInformationalVersion now
  stamps base commit 80ce2b1. An initial consumer verifier retained the old expected hash; correct the
  verifier to the freshly inspected source/archive hash and rerun successfully. This is not a code defect.
- No production/tooling implementation changed, so the preceding compiled-model module's full optimize/
  migration CLI/bundle results remain historical evidence; do not claim those tools reran on this nupkg.
  Native, streaming nested-reader, change-tracking proxy, compiled-proxy/AOT and performance gates stay open.
- Independent review's two Important gaps and one Minor packed-binding gap are addressed. No production
  defect, driver change, push, tag or publication. This local module closes; the full EF10 goal stays active.
