# Aggregate and grouped-query native acceptance

Baseline: `f8532df`; requirement: QRY-04 in the feature matrix. Continue the
existing inline implementation/test/review/local-commit workflow. No push.

## Scope and design

Reuse `LiveStorageTable`, Connector 0.3.0 public APIs, existing User Secrets and
the ordinary direct endpoints. Create only uniquely named, CREATE-confirmed
owned tables and clean them through the existing independent cleanup path.
Do not change connection policy or implement Connector internals.

Add native tests for all eleven numeric extrema carriers and the five standard
Sum/Average families, required/nullable projections, empty/all-null inputs,
Count/LongCount, filtered/distinct aggregates, nullable/composite/converted keys,
HAVING/order/paging, grouped collections/first entities, and text/five temporal
extrema. Compare actual results to independent in-memory expectations, including
both compatibility modes, sync/async and both NULL configurations for groups.
Existing local negative mapping/overload tests remain part of the evidence.

Production changes require a meaningful failing regression and direct Connector
controls where server/transport ownership is uncertain. Ordinary finite Double
samples do not close the separately recorded Oracle full-range parameter gap.

## Tasks and gates

1. Add tests; run the new native family serially and classify failures.
2. Correct test mistakes separately from actual Provider defects; add local RED
   before any production fix. Record explicit unsupported shapes without
   weakening existing assertions.
3. Run affected local tests and the native family on EF10.0.0 and an isolated
   EF10.0.14 snapshot. If production changes, run both full local suites and
   package consumers; otherwise retain the prior unchanged-production checks
   explicitly as historical evidence.
4. One independent review, one executor correction pass; record remaining minor
   findings. Audit exact changed files for credentials, LF and whitespace.
5. Update feature/gap/progress records and commit locally. Close QRY-04 only if
   its complete criterion is demonstrated; otherwise state the remaining gap.

## Review focus

- NULL keys must retain their group elements even under relational NULL mode.
- Empty/all-null Sum returns zero; nullable extrema/average return null;
  required empty extrema/average throw and permit a subsequent valid query.
- Native decimal/Single/integer promoted AVG must use the actual result reader.
- Text ordering and timezone expectations must match the declared test domain.
- Group paging/rebinding must not accidentally test only an empty result.

## Findings and results

- The first native run passed 58/64. Six MySQL failures came from default
  DECIMAL(65,28) SUM/group result metadata, not SQL translation. Direct Connector
  GetValue/GetProviderSpecificValue/GetFieldValue<OceanBaseDecimal> controls
  reproduce SUM/AVG rejection; MIN/MAX and a text diagnostic projection succeed.
  Explicit (18,4) controls pass. Keep the default unchanged and retain the
  default characterization beside separate positive explicit-facet tests.
- The classified run passed 68/68. Adding converter/checked-result cases exposed
  four failures before ADO: nullable Single converter mappings have CLR type
  float? while SQL expression types are unwrapped float. Local two-mode RED is
  0/2; normalizing only the mapping CLR type gives 6/6 with existing malformed
  marker guards. Native converter cases then pass 4/4. Other carrier/ownership,
  null-converting and malformed marker restrictions remain enforced.
- Full Release EF10.0.0 and isolated EF10.0.14 each pass 6652 tests, with 16
  existing specification skips. r1 package contents/dependency ranges, both-mode
  consumers on both patches, graphs and source/archive/consumer DLL identity
  pass; the packed README matches source. These are offline/package checks.
- EF10.0.0 and isolated EF10.0.14 full LiveTests each pass 267/267, comprising
  257 live tests and 10 offline profile tests. All 72 new cases pass in each.
  After adding SUM HAVING/aggregate order and grouped text/temporal projections,
  their affected root family passes 12/12; the EF10.0.14 full run includes these
  additions. Final read-only catalog checks report zero tagged diagnostic tables
  in both namespaces. No failed handshake needs a retry in these final runs.
- Independent review: zero Critical, zero Important, one Minor. The production
  guard and its aggregate/math/rounding/extrema callers were reviewed. Deferred
  minor: Tag currently depends on Bucket in the native fixture, so these rows
  cannot detect grouping/correlation that drops only the second key. Existing
  local SQL/supplied-reader coverage remains; native key independence is not
  established by this dataset. No second reviewer is requested.
- Review scope: wider collations/named zones/arbitrary converters/deployments
  and full-range Oracle Double remain in their owning matrix rows; this module
  does not claim them. The Connector metadata rejection's internal cause is not
  assigned from public-API observations alone. Oracle QRY-04 is Done for the
  stated ordinary direct 4.4.2.2 profile. MySQL QRY-04 remains In progress for
  default Decimal SUM/AVG results. Required totals are MySQL 11 Done/42 In
  progress and Oracle 12 Done/41 In progress; each still has 53 requirements.

Diagnostic logs/TRXs and the execution ledger are under the ignored
`artifacts/aggregate-query/` directory. Native operations are serial. No
credentials are recorded, no Connector source is changed, and no package is
published. This module does not close the global EF10 goal.
