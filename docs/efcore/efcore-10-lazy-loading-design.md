# EF10 Lazy Loading Design

**Date:** 2026-11-13. **IDs:** DEF-08, QRY-08, CFG-03, CON-04/05, PKG-03/04.

## Scope and approach

Close the local EF10 lazy-loading integration module for both compatibility modes, before starting another
feature. Reuse EF's ILazyLoader constructor injection and the optional official
Microsoft.EntityFrameworkCore.Proxies package. Application code selects UseLazyLoadingProxies and public,
unsealed entities with virtual navigations, or uses injected ILazyLoader on ordinary entities. A custom
provider proxy/loader would duplicate EF and is unnecessary; making Proxies a mandatory provider dependency
would charge applications that do not use it. Add it only to test and package-consumer projects.

This is the implementer's choice under the user's standing authorization to finish EF10 one module at a
time. Work inline in the existing clean efcore/10.0 checkout, based on 80ce2b1. Only a demonstrated provider
defect warrants production changes. Passing existing behavior is reported as verified integration.

## Exact contracts and references

- [Lazy loading](https://learn.microsoft.com/ef/core/querying/related-data/lazy)
- [EF8 untracked lazy loading](https://learn.microsoft.com/ef/core/what-is-new/ef-core-8.0/whatsnew#enhancements-to-lazy-loading)
- [Compiled-model restrictions](https://learn.microsoft.com/ef/core/performance/advanced-performance-topics#limitations)
- Installed EF/Proxies 10.0.0 LazyLoader and LazyLoadingInterceptor decompiled under
  artifacts/lazy-loading-contract; verify the same public workflow at EF/Proxies 10.0.14.

EF's per-entity ILazyLoader loads references/collections through NavigationEntry and the provider's ordinary
query pipeline. Getter-triggered proxy loading is synchronous even after an async root query. The loader's
LoadAsync API carries cancellation explicitly. EF retains loading state for repeated/empty access and resets
it after failure; disposal/detachment and ChangeTracker.LazyLoadingEnabled govern further loading.
Untracked queries may still lazy-load while their originating context is alive. Do not substitute tracking
or assume AsNoTracking disables lazy loading.

## Acceptance

Exercise actual EF query materialization and lazy relationship queries, using intercepted native results
only because the existing real endpoint fails before authentication. Assert projection, FK and current
filter SQL/binds independently of fixture rows, fixup/identity/tracking, no redundant loaded queries,
empty/null relations, disabled and eager-loaded paths, disposed/detached behavior, cancellation/failure
recovery and tracked writes after lazy loading. Cover both modes, sync/async root materialization and
tracked/untracked behavior. Verify constructor injection without proxy generation and actual Castle proxies
through the public API. Isolated package consumers must restore the optional Proxies package at the selected
EF patch and exercise graph loading and writes without provider project references.

Query filters must use current context values; failure must leave navigation retryable and release owned
connections/readers. Preserve caller-owned open connections. Negative proxy shapes must fail visibly.
Do not advertise proxy/compiled-model combinations that EF does not support.

## Boundaries

Do not force all queries to buffer for lazy loading: the connector supports one active native reader, so
materialize root results before reading unloaded navigations. Streaming roots with a nested getter query
are outside this profile. Change-tracking proxies, concurrent DbContext use, NativeAOT and compiled models
with lazy proxies are independent capabilities. Native SQL/filter/results/write/readback, endpoint failure
classification, timing and N+1 performance remain unverified. No new driver capability is required.
