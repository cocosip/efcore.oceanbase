# Oracle Double declarations and precision regression

Follow-up to MAP-02 and the user's declaration/precision question. Compare the
existing default Double mapping and explicit BINARY_DOUBLE against a deliberately
decimal-converted NUMBER(18,6), using Connector 0.3.0 and ordinary Oracle 4.4.2.2.
Reuse LiveStorageTable ownership, independent cleanup and safe diagnostics.
No Connector changes, automatic carrier conversion or full-range support claim.

- [x] Verify model mappings and actual USER_TAB_COLUMNS, ordinary native values,
  restricted-domain decimal conversion, excess-scale rejection and server rounding.
- [x] Compare parameter and D-literal writes to the same native declarations for
  extrema, Epsilon and values around the observed NUMBER range; test whether
  parameter Precision=17 changes the observed failure/loss.
- [x] Run serial sync/async live regression on both EF patches, update the numeric
  evidence and packaged README, independent review, secret audit and local commit.

Review focus: distinguish NUMBER(p,s) intentional rounding from pre-storage native
parameter loss; never infer Oracle Database behavior from OceanBase DUMP type codes;
do not describe double-to-decimal conversion as lossless over the CLR Double range.
Characterizations explicitly record unresolved behavior rather than accepting it.

## Findings

LiveOracleDoubleDeclarationTests adds eight sync/async cases. USER_TAB_COLUMNS
confirms both default and explicit BINARY_DOUBLE have NULL precision/scale, while
the deliberately decimal-converted property is NUMBER(18,6). Ordinary native
123.456789123 round-trips unchanged; the converted property accepts 123.456789
and rejects excess scale before DML with the entity entry retained. Raw SQL
123.456789123D into NUMBER(18,6) stores 123.456789: intentional column rounding.

For both native declarations, MinValue/MaxValue parameter writes fail with 1426;
signed Epsilon and signed 1E-131 parameter writes store zero; 1E-120 is preserved.
The same native columns retain every tested value bit-for-bit when inserted with
the mapping's D literal and read back through EF after clearing tracking. Thus
correct native declaration alone does not resolve the observed parameter boundary.
Direct public DbType.Double parameters with Precision 0/17 reproduce MaxValue1426,
1E-131 zero and preserved1E-120 in the explicit native column.

The Provider explicitly rejects HasPrecision(17) on native BINARY_DOUBLE and a
direct CLR Double NUMBER(18,6) declaration without conversion; tests preserve
those existing boundaries. Decimal conversion is an opt-in restricted-domain
choice, not a full-range lossless Double workaround. No new production mapping,
automatic precision policy, literal interpolation of application inputs or
Connector implementation is introduced.

Ignored direct controls in artifacts/oracle-double-declarations/probe.log also
compare unbounded NUMBER: 1E-131 becomes zero there even for D literals. OceanBase
DUMP codes differ from Oracle Database's; the observed Double parameter numeric
carrier does not establish codec/server ownership. No Oracle Database or other
OceanBase version claim is inferred from these controls.

## Verification

Serial final LiveTests on EF10.0.0 and isolated10.0.14 each pass 93 cases
(83 live, 10 offline), zero failed/skipped, including all eight new cases.
Production source is unchanged since 3f60765; the preceding final Release suites
each passed 6645 cases with 16 existing specification skips and are not repeated
for this test/documentation-only follow-up.

Package 10.0.0-double-declarations-r1-20261117 includes the updated restriction
and restricted-domain guidance. Both-mode package consumers on both patches,
dependency graph, package/source DLL and README identity checks pass. The
compatibility test source matches and its assets resolve EF10.0.14. Read-only
catalog checks find zero tagged diagnostic tables in either namespace.

Ruling: characterize/document the measured behavior without a speculative binding
fix or artificial RED case. A wrong codec/server attribution could conceal data
loss; full-range native parameter acceptance remains open. Review and local commit
finish this bounded follow-up; no push, publication or wider mapping development.

Fresh independent review found no Critical/Important/Minor findings; no fix pass
was needed. Codec/server ownership, other versions/Oracle Database, wider numeric
domains/alternative carriers and production acceptance of known losses were
excluded from the verdict and remain open. The unchanged-production offline
suite was not rerun; its prior evidence remains explicitly labeled. Credential
audit of the exact changed/new module files and whitespace checks pass.
