# EF Core 8 Release Checklist

EF Core 8 acceptance is pending. Record implementation in the [feature matrix](efcore-8-feature-matrix.md)
and development work in the [gap matrix](efcore-8-gap-matrix.md). The EF10 release checklist owns
the common acceptance scenarios and historical evidence. Run branch-specific checks before claiming
EF8 acceptance. [Baseline acceptance scenarios](efcore-10-release-checklist.md#acceptance-requirements-by-feature)

## Target and branch

- Record the exact fork revision of the completed `efcore/9.0` branch, candidate revision, SDK, runtime, operating system and
  dependency resolution. Use an SDK that reads the existing solution and install the .NET 8 runtime
  for `net8.0` tests. Treat later runtime targets as separate acceptance profiles.
- Target `OceanBase.EntityFrameworkCore` 8.x with a `net8.0` asset and EF runtime dependencies in
  `[8.0.0,9.0.0)`. Build against the floor. Use EF 8.0.0 and 8.0.34 to exercise the
  dependency range. Align test, Design, Proxies, specification and `dotnet-ef` versions with each run.
- Verify the selected `OceanBaseConnector` asset and transitive dependencies. Connector 0.4.0 has a
  `net8.0` asset and is the initial compatibility target. Record the exact published dependency
  used in the candidate. Consult the repository's [branch and publication policy](version-support-matrix.md#branches-cicd-and-publishing).

## Release gates

| Gate | State | Required branch evidence |
| --- | --- | --- |
| R-01 Dependency and source identity | Pending | Restore against the EF8 floor with the matching Design package. Check SDK/TFM, test package versions, resolved Connector assets, dependency audit, and candidate source identity. |
| R-02 Local and relational specifications | Pending | Release unit, functional, consumer configuration and EF8 relational specification tests against the floor and 8.0.34. Record inherited classes, intentional skips and exact test scope. Include model, cache, mapping, query, dispatch and generated-value regressions in both modes. |
| R-03 Live database | Pending | Replay applicable query/collection, CRUD and bulk DML, generated-value and graph cases against OceanBase MySQL and Oracle. Verify transactions/savepoints, cancellation/recovery, seeding and metadata. Record versions, mode and scenario results. |
| R-04 Package compatibility | Pending | Pack one 8.x candidate from the floor. Verify nuspec, `net8.0` asset, bounds, dependency selection and revision. In isolated package-only consumers run against 8.0.0 and 8.0.34. Verify strict restore rejects adjacent EF majors and no EF9 asset or project reference leaks into the package. |
| R-05 Migrations, scaffolding and code generation | Pending | Use EF8 tools for generated C# compilation, migration add/apply/rollback/idempotent scripts, history and bundle checks. Exercise externally serialized migrations and HasData without EF9 callbacks. Regenerate compiled models with the claimed proxy options. Precompiled queries and NativeAOT are outside the initial scope. |
| R-06 Platform and CI | Pending | Run branch CI tests and package validation on the intended Windows and Linux runners. Record separately any platform exclusions or manual acceptance. |
| R-07 Deployment and compatibility modes | Pending | Record server version, tenant mode, permissions, authentication, transport and Connector options for both direct profiles. Require independent evidence for any proxy, TLS or HA profile claimed. |
| R-08 Review | Pending | Review EF8 API adaptations, SQL and parameter behavior, data and key propagation, migration/retry semantics and mapping boundaries. Reconcile supported package documentation with the feature and gap matrices. |
| R-09 Sensitive data and database cleanup | Pending | Use User Secrets for credentials. Record authorized ownership and cleanup boundaries. Retain safe diagnostics and verify cleanup of test-owned objects. |
| R-10 Publication | Pending | Confirm package availability on the intended feed, verify candidate provenance and get release authorization. Publish only for the matching release tag under the repository policy. |

## Recording results

Record exact restore, test, package and tooling commands and their candidate identity at the affected
gate. Keep detailed logs and TRX in ignored `artifacts/`. Use EF8-generated artifacts for tooling
checks and reuse the EF10 scenario list only to select cases. The package-content script and isolated
consumers must verify the candidate independently of project references.

EF8 has no candidate build or live results. Its baseline is selected after the EF9 port completes.
