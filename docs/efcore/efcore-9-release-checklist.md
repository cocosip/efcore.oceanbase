# EF Core 9 Release Checklist

EF Core 9 acceptance is pending. Record implementation in the [feature matrix](efcore-9-feature-matrix.md)
and development work in the [gap matrix](efcore-9-gap-matrix.md). The EF10 release checklist owns
the common acceptance scenarios and historical evidence. Run branch-specific checks before claiming
EF9 acceptance. [Baseline acceptance scenarios](efcore-10-release-checklist.md#acceptance-requirements-by-feature)

## Target and branch

- Record the exact fork revision of `efcore/10.0`, candidate revision, SDK, runtime, operating system and
  dependency resolution. Use an SDK that reads the existing solution and install the .NET 9 runtime
  for `net9.0` tests. Treat later runtime targets as separate acceptance profiles.
- Target `Sharp.OceanBase.EntityFrameworkCore` 9.x with a `net9.0` asset and EF runtime dependencies in
  `[9.0.0,10.0.0)`. Build against the floor. Use EF 9.0.0 and 9.0.23 to exercise the
  dependency range. Align test, Design, Proxies, specification and `dotnet-ef` versions with each run.
- Verify the selected `OceanBaseConnector` asset and transitive dependencies. Connector 0.4.0 has a
  `net9.0` asset and is the initial compatibility target. Record the exact published dependency
  used in the candidate. Consult the repository's [branch and publication policy](version-support-matrix.md#branches-cicd-and-publishing).

## Release gates

| Gate | State | Required branch evidence |
| --- | --- | --- |
| R-01 Dependency and source identity | Pending | Restore against the EF9 floor with the matching Design package. Check SDK/TFM, test package versions, resolved Connector assets, dependency audit, and candidate source identity. |
| R-02 Local and relational specifications | Pending | Release unit, functional, consumer configuration and EF9 relational specification tests against the floor and 9.0.23. Record inherited classes, intentional skips and exact test scope. Include model, cache, mapping, query, dispatch and generated-value regressions in both modes. |
| R-03 Live database | Pending | Replay applicable query/collection, CRUD and bulk DML, generated-value and graph cases against OceanBase MySQL and Oracle. Verify transactions/savepoints, cancellation/recovery, seeding and metadata. Record versions, mode and scenario results. |
| R-04 Package compatibility | Pending | Pack one 9.x candidate from the floor. Verify nuspec, `net9.0` asset, bounds, dependency selection and revision. In isolated package-only consumers run against 9.0.0 and 9.0.23. Verify strict restore rejects adjacent EF majors and no EF10 asset or project reference leaks into the package. |
| R-05 Migrations, scaffolding and code generation | Pending | Use EF9 tools for generated C# compilation, migration add/apply/rollback/idempotent scripts, history and bundle checks. Exercise migration lock acquisition/release and pending-model checks as well as synchronous/asynchronous seeding callbacks. Regenerate compiled models with the claimed proxy options. Record distinct results for optional query precompilation and NativeAOT profiles if claimed. |
| R-06 Platform and CI | Pending | Run branch CI tests and package validation on the intended Windows and Linux runners. Record separately any platform exclusions or manual acceptance. |
| R-07 Deployment and compatibility modes | Pending | Record server version, tenant mode, permissions, authentication, transport and Connector options for both direct profiles. Require independent evidence for any proxy, TLS or HA profile claimed. |
| R-08 Review | Pending | Review EF9 API adaptations, SQL and parameter behavior, data and key propagation, migration/retry semantics and mapping boundaries. Reconcile supported package documentation with the feature and gap matrices. |
| R-09 Sensitive data and database cleanup | Pending | Use User Secrets for credentials. Record authorized ownership and cleanup boundaries. Retain safe diagnostics and verify cleanup of test-owned objects. |
| R-10 Publication | Pending | Confirm package availability on the intended feed, verify candidate provenance and get release authorization. Publish only for the matching release tag under the repository policy. |

## Recording results

Record exact restore, test, package and tooling commands and their candidate identity at the affected
gate. Keep detailed logs and TRX in ignored `artifacts/`. Use EF9-generated artifacts for tooling
checks and reuse the EF10 scenario list only to select cases. The package-content script and isolated
consumers must verify the candidate independently of project references.

An offline diagnostic build of EF10 revision `1dc4cd3` restored after substituting EF 9.0.22 on `net10.0` and failed at EF10 query and type-mapping signatures. It establishes a porting gap in 9P-03. R-01 and R-02 remain pending for the `net9.0` Provider.
