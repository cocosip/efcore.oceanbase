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

## Reproduction

Run only the non-destructive probe after loading the two User Secret values:

```powershell
$env:OCEANBASE_RUN_LIVE_TESTS = '1'
dotnet test tests/OceanBase.EntityFrameworkCore.LiveTests/OceanBase.EntityFrameworkCore.LiveTests.csproj `
  -c Release --filter FullyQualifiedName~LiveConnectionTests
```

Do not add DDL or write cases until the endpoint owner classifies direct versus proxy routing, server
version, authentication path, and TLS/transport settings. Record those facts here without credentials,
then rerun the same probes before expanding the live matrix.

## REL-01 exit criteria

`REL-01` can move to `Done` only when this record contains the classified endpoint type, server version,
mode-specific tenant identity, Connector/EF/runtime versions, and the effective TLS/transport options,
with a successful non-destructive probe recorded separately for each required mode. The current record is
`In progress` because the endpoint classification and pre-authentication result are unresolved.
