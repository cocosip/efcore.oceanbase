# EF Core 9 Release Checklist

EF Core 9 acceptance is pending. Record implementation in the [feature matrix](efcore-9-feature-matrix.md)
and development work in the [gap matrix](efcore-9-gap-matrix.md). The EF10 release checklist owns
the common acceptance scenarios and historical evidence. Run branch-specific checks before claiming
EF9 acceptance. [Baseline acceptance scenarios](efcore-10-release-checklist.md#acceptance-requirements-by-feature)

## Maintenance 9.0.1 checkpoint

Prepared 2026-11-23 against published OceanBaseConnector 0.5.0. The package keeps the EF 9
dependency range and net9.0 asset. Runtime and design-time configuration use CompatibilityMode;
the package guide owns migration instructions. No Connector source or CI workflow changes are needed.

Patch verification is pending. Logs, TRX and packages belong under `artifacts/connector-050/`.
Record the Release solution, package archive and isolated floor/serviced-patch consumer results here.
The commands are `dotnet test OceanBase.EntityFrameworkCore.slnx -c Release --no-restore -m:1 -nr:false`,
`dotnet pack src/OceanBase.EntityFrameworkCore -c Release --no-restore -o artifacts/connector-050/ef9-packages`,
and `tests/OceanBase.EntityFrameworkCore.PackageConsumer/Verify-Package.ps1` with the candidate
package path and expected version 9.0.1; restore/run the PackageConsumer using that local feed.

The user requires fresh MySQL and Oracle database validation before release decisions. Tests may
create unique test-owned EFT_ objects only inside the pre-provisioned database/schema selected by
the existing User Secrets configuration, and must verify cleanup of those objects. Namespace
creation/deletion and prefix-based cleanup are outside this scope.

All live, tooling and environment evidence below belongs to the initial release and Connector 0.4.0.
It is historical evidence for this patch. Fresh both-mode live/tooling acceptance and hosted CI remain
pending; Windows offline tests do not establish Linux, TLS, proxy or HA acceptance. No patch release
tag, push or publication is authorized by preparing the source and local packages.

## Target and branch

- Record the exact fork revision of `efcore/10.0`, candidate revision, SDK, runtime, operating system and
  dependency resolution. The branch uses C# 13 and the .NET 9 runtime. CI uses the .NET 10 SDK to
  read the solution and build the `net9.0` projects.
- Target `Sharp.OceanBase.EntityFrameworkCore` 9.x with a `net9.0` asset and EF runtime dependencies in
  `[9.0.0,10.0.0)`. Build against the floor. Use EF 9.0.0 and 9.0.23 to exercise the
  dependency range. Align test, Design, Proxies, specification and `dotnet-ef` versions with each run.
- Verify the selected `OceanBaseConnector` asset and transitive dependencies. Connector 0.4.0 has a
  `net9.0` asset and is the initial compatibility target. Record the exact published dependency
  used in the candidate. Consult the repository's [branch and publication policy](version-support-matrix.md#branches-cicd-and-publishing).

## Release gates

| Gate | State | Required branch evidence |
| --- | --- | --- |
| R-01 Dependency and source identity | Pending patch verification | Restore against the EF9 floor with the matching Design package. Check SDK/TFM, test package versions, resolved Connector assets, dependency audit, and candidate source identity. |
| R-02 Local and relational specifications | Pending patch verification | Release unit, functional, consumer configuration and EF9 relational specification tests against the floor and 9.0.23. Record inherited classes, intentional skips and exact test scope. Include model, cache, mapping, query, dispatch and generated-value regressions in both modes. |
| R-03 Live database | Historical pass; patch rerun pending | Replay applicable query/collection, CRUD and bulk DML, generated-value and graph cases against OceanBase MySQL and Oracle. Verify transactions/savepoints, cancellation/recovery, seeding and metadata. Record versions, mode and scenario results. |
| R-04 Package compatibility | Pending patch verification | Pack one 9.x candidate from the floor. Verify nuspec, `net9.0` asset, bounds, dependency selection and revision. In isolated package-only consumers run against 9.0.0 and 9.0.23. Verify strict restore rejects adjacent EF majors and no EF10 asset or project reference leaks into the package. |
| R-05 Migrations, scaffolding and code generation | Historical pass; patch tooling rerun pending | Use EF9 tools for generated C# compilation, migration add/apply/rollback/idempotent scripts, history and bundle checks. Exercise migration lock acquisition/release and pending-model checks as well as synchronous/asynchronous seeding callbacks. Regenerate compiled models with the claimed proxy options. Record distinct results for optional query precompilation and NativeAOT profiles if claimed. |
| R-06 Platform and CI | Patch CI pending | Run branch CI tests and package validation on the intended Windows and Linux runners. Record separately any platform exclusions or manual acceptance. |
| R-07 Deployment and compatibility modes | Historical direct profile only | Record server version, tenant mode, permissions, authentication, transport and Connector options for both direct profiles. Require independent evidence for any proxy, TLS or HA profile claimed. |
| R-08 Review | Patch review pending | Review EF9 API adaptations, SQL and parameter behavior, data and key propagation, migration/retry semantics and mapping boundaries. Reconcile supported package documentation with the feature and gap matrices. |
| R-09 Sensitive data and database cleanup | Patch scope review pending | Use User Secrets for credentials. Record authorized ownership and cleanup boundaries. Retain safe diagnostics and verify cleanup of test-owned objects. |
| R-10 Publication | Patch publication pending authorization | Confirm package availability on the intended feed, verify candidate provenance and get release authorization. Publish only for the matching release tag under the repository policy. |

## Recording results

Record exact restore, test, package and tooling commands and their candidate identity at the affected
gate. Keep detailed logs and TRX in ignored `artifacts/`. Use EF9-generated artifacts for tooling
checks and reuse the EF10 scenario list only to select cases. The package-content script and isolated
consumers must verify the candidate independently of project references.

## Historical initial stable release preparation

The Windows package checkpoint `3c10a30` passed the Release solution, package archive and
source revision checks, and isolated consumers on EF 9.0.0 and 9.0.23. The package-only
live probes passed MySQL and Oracle direct connections and four MySQL computed-column migration
cases with TLS disabled. NuGet.org had no package under the selected ID at the time of the check,
and the resolved package and consumer dependencies had no matches in the live NuGet advisory feed.
Evidence is under `artifacts/release-prep/ef9/` and `artifacts/release-prep/audit.log`.
Hosted verification of `ci-cd.yml` and activation of the release repository's NuGet Trusted
Publishing policy remain pending. See the [release policy](version-support-matrix.md#branches-cicd-and-publishing).
The EF9 specification project also passed after updating its private MSBuild dependency to
17.8.43. The repeated advisory check found no matches in that test graph.

## Historical initial local acceptance evidence

Evidence is from the local `efcore/9.0` port based on `24f168a`, with Windows, SDK
10.0.403 and .NET runtime 9.0.22. Logs and TRX files are under `artifacts/efcore9-port/`.
The source revision embedded in local packages identifies the fork used at pack time.

- **R-01** — The floor restore selected EF Core 9.0.0 and the Connector 0.4.0 `net9.0` asset.
  Checking each 42-package Provider dependency graph against the NuGet advisory data found no
  matches. The direct NuGet CLI audit could not reach the feed because of a Schannel error.
- **R-02** — EF Core 9.0.0 and 9.0.23 each passed 7,026 Windows Release cases, including 2,904 unit,
  2,582 intercepted functional, 2 consumer configuration and 1,538 relational specification cases.
  The 16 intentional specification skips cover inherited model-building requirements outside the
  Provider's stated scope. See the [specification suite source](../../tests/OceanBase.EntityFrameworkCore.SpecificationTests/).
- **R-03 and R-05** — The full Windows Release live suite passed 939 cases against OceanBase
  4.4.2.2 with EF Core 9.0.0, including both MySQL and Oracle, with zero failures or skips.
  See `test-results/ef9-resumed-live.trx` under the evidence directory. The final run disabled
  xUnit collection parallelism after network interruptions in the earlier parallel run and
  corrected the cross-tenant graph expectation for EF9's all-filter disable API.
  The final feature sample passed 24 asynchronous and 24 synchronous scenarios across MySQL and
  Oracle. The migration sample generated and compiled
  migrations with `dotnet-ef` 9.0.0, then applied, upgraded, rolled back and verified cleanup in both
  modes. See `net9-final-sample-*.log` under the evidence directory.
- **R-04 and R-05** — Package `9.0.0-net9-finalcheck` passed archive, TFM and dependency range
  checks. Strict restores rejected EF Core 8.0.0 and 10.0.0. Package-only consumers and tooling
  passed with EF Core 9.0.0 and 9.0.23. In both modes the tooling generated and compiled
  reverse-engineered and compiled models, including proxies, checked pending model changes,
  generated migration scripts, and built and launched the `win-x64` bundle with `--help`.
  Migration lock failure and cancellation paths passed with intercepted commands. After updating the
  embedded README, the package was repacked and verified to retain identical Provider binaries,
  dependencies and assets. The repacked SHA-256 is
  `9fddcdf88edd4686848953c6c752acd68a15f688d549a271f452e2d1398ad7fa`.
- **R-06** — Local Windows verification is available. The branch workflow defines Windows and
  Linux validation. Hosted runner results remain pending.
- **R-07** — The direct MySQL and Oracle profiles use the configured test accounts and namespaces
  on OceanBase 4.4.2.2 with TLS disabled. Oracle generated-value scenarios opt in to
  `UseOraclePrepareExecute`. Proxy, TLS verification and HA require their own evidence.
- **R-08** — The port review identified and corrected failed ambient enlistment cleanup, Oracle
  credential-command interception and the inherited selective-filter assertion. Focused tests
  cover both sync and async paths. The compiled-model documentation follows the EF9 tooling
  results. Query precompilation and NativeAOT remain unverified under 9P-11.
- **R-09** — The first synchronous feature-sample attempt lost its connection during cleanup.
  Both sample-owned MySQL tables were individually dropped and verified absent. The synchronous
  retry passed. The successful final live run passed its per-fixture cleanup checks. An earlier
  interrupted run has no complete cleanup record. Historical objects of uncertain ownership are
  outside the cleanup scope.
