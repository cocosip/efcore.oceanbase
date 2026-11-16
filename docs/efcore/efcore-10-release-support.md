# EF Core 10 Release Support Record

This record owns `REL-01`. It records the candidate support environment without storing credentials or
claiming a live server result that has not been observed.

## Provider and runtime

| Item | Value |
| --- | --- |
| Provider branch | `efcore/10.0` |
| Target framework | `net10.0` |
| EF Core range | `[10.0.0, 11.0.0)` |
| EF Core versions exercised locally | `10.0.0`, `10.0.14` |
| `dotnet-ef` used by tooling checks | `10.0.15` |
| OceanBaseConnector | `0.3.0` release package |
| Required compatibility modes | MySQL and Oracle |
| Credentials | User Secrets only; no credential values are committed |

## Candidate tenant profiles

The live test project reads these keys through `AddUserSecrets<LiveConnectionTests>`:

| Mode | Secret key | Expected database/schema | Probe SQL |
| --- | --- | --- | --- |
| MySQL | `ConnectionStrings:OceanBaseMySql` | database `risdb` | `SELECT 1`, `SELECT DATABASE()` |
| Oracle | `ConnectionStrings:OceanBaseOracle` | schema `RISDB` | `SELECT 1 FROM DUAL`, `SELECT SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA') FROM DUAL` |

The supplied connection strings identify endpoint `172.16.2.45:2881` in the current live evidence. The
repository does not establish whether that endpoint is a direct tenant endpoint or an OBProxy path. No
TLS, proxy, compression or other transport option is hard-coded by the provider; those options remain
properties of the secret-managed connection strings and must be recorded after endpoint classification.

## Current evidence

- TCP reachability to `172.16.2.45:2881` was observed.
- Both MySQL and Oracle probes close the packet during the pre-authentication handshake with
  `Connection closed while reading the packet`.
- Authentication, `SELECT 1`, schema inspection, SQL, and DDL have not completed against this endpoint.
- Therefore this record describes a candidate environment and its known limitation; it does not establish
  `TST-02`, native provider support, or a release-ready server profile.

On 2026-11-16, the new `LiveReadOnlyQueryTests` profile was explicitly attempted once per mode on
EF10.0.0/Connector 0.3.0/.NET 10. Both failed at connection opening with `OceanBaseException`,
server code `0` and **0 EF reader commands**. The sanitized errors do not expose the underlying
connector message; do not infer a more specific cause from this new attempt. Authentication, server
version and query execution were not established. No test objects, DDL or data writes were attempted.

The profile now provides real EF sync/async materialization, changing numeric/text/NULL parameters,
Boolean expressions, paging, aggregates/GroupBy/HAVING, Any/All and set-operation assertions over four
literal inline rows. It supplies no fake result reader. Four independent offline SQL/model checks pass
at EF10.0.0 and EF10.0.14; they verify fixture construction, not native results. See
[the module record](efcore-10-read-only-live-query-plan.md). Both modes remain OceanBase targets;
OceanBase V4.3.5 references define the fixture's syntax, not the unidentified deployed server version.

## Reproduction

Restore from an available package source first. Run offline fixture checks without the live opt-in:

```powershell
dotnet test tests/OceanBase.EntityFrameworkCore.LiveTests/OceanBase.EntityFrameworkCore.LiveTests.csproj `
  -c Release --no-restore --filter FullyQualifiedName~ReadOnlyQueryProfileTests
```

Run the sanitized native SELECT-only EF profile using the existing User Secret values. Restore the
previous opt-in afterwards; never print the connection strings:

```powershell
$previousOptIn = $env:OCEANBASE_RUN_LIVE_TESTS
try {
  $env:OCEANBASE_RUN_LIVE_TESTS = '1'
  dotnet test tests/OceanBase.EntityFrameworkCore.LiveTests/OceanBase.EntityFrameworkCore.LiveTests.csproj `
    -c Release --no-restore --filter FullyQualifiedName~LiveReadOnlyQueryTests
} finally {
  $env:OCEANBASE_RUN_LIVE_TESTS = $previousOptIn
}
```

Do not add DDL or write cases until the endpoint owner classifies direct versus proxy routing, server
version, authentication path, and TLS/transport settings. Record those facts here without credentials,
then rerun the same probes before expanding the live matrix.

## REL-01 exit criteria

`REL-01` can move to `Done` only when this record contains the classified endpoint type, server version,
mode-specific tenant identity, Connector/EF/runtime versions, and the effective TLS/transport options,
with a successful non-destructive probe recorded separately for each required mode. The current record is
`In progress` because the endpoint classification and pre-authentication result are unresolved.
