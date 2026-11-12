# EF Core 10 Development Progress

## Active checkpoint: 2026-11-12

Development resumed on 2026-11-11 on efcore/10.0 at the user's request, following the paused Batch7e checkpoint.
The latest committed checkpoint is Batch3ag (`fecaa95`) on 2026-11-12.
Test each module and review completed major modules before committing. EF8/EF9 remain postponed.
The provider is not release-ready. The [feature checklist](efcore-10-feature-checklist.md) remains the
acceptance authority; this document records the handoff rather than defining a different completion target.

The current user goal is to finish all locally developable EF10 functionality, with tested/reviewed commits after
each small group. Live database validation is now being wired for the supplied tenants; EF8/EF9 adaptation remains postponed. The
[local development plan](efcore-10-local-development-plan.md) tracks the full remaining inventory, including
an audit of locally feasible deferred families. Finishing one batch does not finish the overall goal.

### Boolean collection-root checkpoint (2026-11-12)

Batch3af follows `776a9a6`. Captured `bool`/`bool?` arrays and lists, including originally empty inline roots,
now use the general VALUES expansion in both compatibility modes. Provider-owned Boolean mappings and
non-null-converting Boolean-to-integer/decimal converters retain current values, duplicates, order, NULL cells,
typed zero-row metadata, parameters and readers. MySQL collection typing uses SIGNED or the carrier's unsigned/
decimal target; Oracle uses NUMBER and emits FROM DUAL for each SELECT arm. Null-converting converters,
non-Boolean/unsupported carriers, unknown layouts and explicit `EF.Parameter(collection)` remain explicit
boundaries before command execution.

Focused Boolean coverage passes **26** unit and **12** intercepted functional cases. The affected collection
focus passes **206** unit and **100** intercepted functional cases. Baseline EF10.0.0 Debug/Release and an
isolated EF10.0.14 Release each pass **3960** source tests: 1888 unit + 1202 intercepted functional + 868
specification + 2 consumer tests, zero failures/skips. The packed consumer passes both modes on EF10.0.0 and
EF10.0.14. Package `10.0.0-boolean-roots-20261112` passes Verify-Package.ps1; provider DLL SHA-256 is
`4A3357600C4672FB86AA577A5C6F1D8200C959C0A7C6A494E2150DDAF2F5B57`, and nupkg SHA-256 is
`D46E134D96F35722D075776F918F80792D7BE3300FE0C19FAEDA04CDAE4F64EC`.

These are local SQL-shape, binding, reader and package-consumer results; native Boolean UNION coercion,
Boolean result semantics, live OceanBase execution, EF8/EF9 adaptation and release acceptance remain open.

### Temporal collection-root checkpoint (2026-11-12)

Batch3ah follows Batch3ag (`5c42125`). Captured `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly` and
`TimeSpan` arrays, nullable cells and empty roots use the shared VALUES expansion in both compatibility modes
where the existing mapping exposes EF10 collection metadata. MySQL emits `DATETIME`/`TIME`/`DATE` casts and
Oracle emits `TIMESTAMP`/`DATE` casts; Oracle interval transport remains an explicit boundary. The shared
temporal reader now accepts the connector and CLR carrier shapes produced by collection projections while
preserving DateOnly midnight and DateTimeOffset UTC semantics. Six focused SQL-shape unit cases and sixteen
intercepted sync/async functional cases assert parameters, decoded values, NULL rows and zero-row SQL.

The source Debug/Release gates each pass **3984** tests: 1896 unit, 1218 intercepted functional, 868
specification and 2 consumer tests, with zero failures or skips. Release packaging and `Verify-Package.ps1`
pass for `10.0.0`; provider DLL SHA-256 is `FF9C42ED909E19959249925BBEEC4AF69D5740EC769E6C9216905F25AC24356D`
and the nupkg SHA-256 is `E67F9B23902290C5C9A2739C56A83432AC45BDF1B184406A239215056B6656DC`. These are local
SQL-shape, binding, reader and package results; native temporal coercion, precision/timezone semantics and
live execution remain open.

### Live connectivity fixture checkpoint (2026-11-12)

The opt-in project `tests/OceanBase.EntityFrameworkCore.LiveTests` now loads
`ConnectionStrings:OceanBaseMySql` and `ConnectionStrings:OceanBaseOracle` from User Secrets ID
`OceanBase.EntityFrameworkCore.LiveTests`. It is intentionally outside the default solution run and requires
`OCEANBASE_RUN_LIVE_TESTS=1`. The project builds in Release with zero warnings/errors.

The first non-destructive probes used the supplied host `172.16.2.45`, SQL port `2881`, supplied credentials,
and TLS explicitly disabled because Connector 0.2.0 defaults to VerifyFull. The TCP port was reachable, but
both MySQL and Oracle handshakes ended with `Connection closed while reading the packet`, before authentication
or SQL. No database/schema mutation occurred; credentials remain in the external User Secrets file and are not
recorded here. This is an environment/transport blocker to classify, not evidence of an EF provider defect.
TST-02 remains In progress until the endpoint/transport issue is classified and both tenants complete the core
live matrix.

### General text collection-root checkpoint (2026-11-11)

Batch3ae follows b611f0b. Captured `string`/`string?` arrays and lists, including originally empty inline
roots, now use the existing general VALUES expansion in both compatibility modes. Provider-owned text mappings
and non-null-converting model-to-string converters retain their parameters, readers, current values, duplicates,
NULL cells and zero-row metadata. MySQL uses `CAST(... AS CHAR)` and Oracle uses the declared text store type;
Oracle empty-string normalization happens after expansion. Numeric roots retain their previous path. Null-converting
converters, binary/temporal/other carriers, unsupported layouts and explicit `EF.Parameter(collection)` remain
explicit boundaries before command execution.

Focused text unit coverage passes **27** cases; the general collection-root unit focus passes **151**, integer
roots **38**, numeric roots **56**, and intercepted text functional coverage **8**. Final EF10.0.0 Debug/Release
and a fresh isolated EF10.0.14 Release each pass **3922** tests: 1862 unit + 1190 intercepted functional +
868 model specification + two project-consumer cases, zero failures/skips. Evidence: `artifacts/text-roots-debug.log`,
`artifacts/text-roots-release.log` and the isolated snapshot under `artifacts/text-roots-ef10.0.14/`.

The baseline-built package `10.0.0-text-roots-20261111` passes Verify-Package.ps1, both-mode consumers on
EF10.0.0 and EF10.0.14, and package-only identity/dependency checks. Provider DLL SHA-256:
`EE745999FD26FD6E5B4AEB7803E23C1803A6246874AB8181C2ADA55ACC1BB967`. Nupkg SHA-256:
`18506B19C4A289E17816EB09B09582E20A217AB88C800A83B4D142DE6BE26B63`. Independent source review found no
actionable issue. These are local SQL-shape, binding, reader and package-consumer results; native text collation,
LOB behavior, UNION coercion, live OceanBase execution, EF8/EF9 adaptation and release acceptance remain open.

### Text and temporal aggregate extrema checkpoint (2026-11-11)

Batch3ad follows cd8d256. Queryable `Min`/`Max` now accepts the provider-owned `string`, `DateTime`,
`DateTimeOffset`, `DateOnly`, `TimeOnly` and `TimeSpan` model families, including nullable forms and
non-null-converting converters. The provider validates the OceanBase mapping, converter and reader carrier,
rejects cross-family/unowned mappings and null-converting converters, and rejects Oracle `CLOB`/`NCLOB`
extrema. Native EF aggregate construction remains the single SQL construction path; no new service, cast,
public API or dependency was added.

New unit and intercepted functional coverage exercises scalar/selector identities, current filters, all-null
and empty shapes, grouping/HAVING/order, native temporal readers, converter-owned values and sync/async
execution in both compatibility modes. The new nonnumeric aggregate unit suite passes **94** cases; the
combined nonnumeric/numeric aggregate focus passes **373** cases. The new functional suite passes **52** cases.
Final EF10.0.0 Debug/Release and isolated EF10.0.14 Release each pass **3910** tests: 1858 unit + 1182
intercepted functional + 868 model specification + two project-consumer cases, zero failures/skips.
Evidence: `artifacts/nonnumeric-extrema-{unit,functional,debug,release,patch,debug-focused,patch-focused}.log`.

The baseline-built `10.0.0-nonnumeric-extrema-20261111` package passes both-mode EF10.0.0/10.0.14
consumer smoke, package contents/dependency/graph checks and provider/archive/consumer identity checks.
Provider DLL SHA-256: `7C7B8C2C901CC9E69652B1968CA6E9D3B7FE351B7BFBB880B6FCA64B18D42957`.
Nupkg SHA-256: `FB23BF972F868C844487B0E0537ECECBB80E9531CCD4C8BA9C46DC258DAA91CC`.
The preceding converted-roots package fails the new null-converting-mapping smoke as expected; evidence:
`artifacts/nonnumeric-extrema-package.log`, `artifacts/verify-nonnumeric-extrema-package.ps1` and
`artifacts/nonnumeric-extrema-previous-package.log`. Independent source review found no actionable finding.
These are local SQL-shape, binding, reader and package-consumer results; native ordering, collation, temporal
semantics, live OceanBase execution, EF8/EF9 adaptation and release acceptance remain open.

### Contextual numeric element-projection checkpoint (2026-11-11)

Batch3ac follows f93bc48. Direct numeric collection elements now retain contextual mappings in
SelectMany, FirstOrDefault and Join, including captured/inline/empty/nullable roots, forwarding through
subqueries and Distinct/Take. A focused normalizer runs before EF's authoritative inference scanner;
only premature default projections and carrier-neutral NULL tests adopt the owning mapping. Early
Distinct supplies its KeyComparer. Nonnullable FirstOrDefault's COALESCE fallback and result mapping
retain offset converters, including the encoded CLR default. No connector or mapping-reader changes.

Two independent review findings were reproduced and fixed. Original operator/predicate requirements
are collected before rewriting, so sharing a column object with a projection cannot erase a genuine
conflict. Scalar-result consumers also retain their requirements: incompatible property, converter or
literal comparisons reject instead of mixing encoded values with native values. Same-store different
converters reject; separate roots over one capture retain independent conversions. NULL comparisons
on forwarded columns and nullable scalar results remain supported. Arbitrary converter/operator
equivalence remains separate work; explicit casts and genuinely typed inline values are preserved.
Final source re-review reran the original reproductions and the null/operator boundaries with no
remaining actionable finding.

Final EF10.0.0 Debug/Release and a fresh isolated EF10.0.14 Release each pass **3764** tests:
1764 unit +1130 intercepted functional +868 model specification +two project-consumer cases,
zero failures/skips. This adds16 unit and20 functional cases; focused collection suites pass151/80.
Coverage includes unsigned integer offsets, nullable and cross-numeric readers, same-store converters,
narrow physical facets, exact rebinding, duplicate/NULL/empty sequences and conflicting result consumers.
Supplied rows establish reader shaping and command bindings, not native query results. Evidence:
`artifacts/converted-roots-{red,scalar-null-red,unit,functional,debug,release,patch,patch-restore}.log`.
The fresh patch snapshot contains all final source/tests; baseline assets still resolve EF10.0.0.

The same baseline-built `10.0.0-converted-roots-20261111` nupkg passes both modes on EF10.0.0/10.0.14,
contextual projection/Distinct/empty/rebinding smoke, archive contents/dependency ranges, package-only
graphs and baseline/archive/consumer DLL identity. Provider DLL SHA-256:
`486DB7613C480B05B346E6A5AFE398E9E7290060F894D52B948D5E25D44B1B66`.
Nupkg SHA-256: `D3FB04729DFCA81880690BE94140DBFF157F221EB500A2F3F59727DD54FEBC24`.
The preceding numeric-roots-r1 package fails the new smoke with a caught conflicting-mapping error/exit1.
Evidence: `artifacts/converted-roots-package.log`, `artifacts/verify-converted-roots-package.ps1` and
`artifacts/converted-roots-previous-package{,-restore}.log`. No dependency changes; the existing manual
consumer audit policy is unchanged. No live database, EF8/EF9, push, publication or branch synchronization.
General text/other roots, wider result compositions and the remaining EF10 inventory stay open.

### Decimal and floating collection-root checkpoint (2026-11-11)

Batch3ab follows 677b7ef. General VALUES roots now accept decimal, Single and Double, nullable forms
and non-null-converting numeric-to-numeric converters in addition to the integer carriers. The same
expansion, EF type-mapping postprocessor and UNION renderer retain mapping/converter ownership,
capture snapshots, exact duplicates/order/NULL cells, current values and originally empty-inline types.
MySQL query casts use DECIMAL(65,30), FLOAT or DOUBLE; Oracle uses NUMBER, BINARY_FLOAT or BINARY_DOUBLE.
Query transport is independent of narrower physical facets. Non-finite floating inputs and values
converted to infinity reject before command execution. No mapping-reader or connector changes needed.

The MySQL CAST reference and public v4.3.5_CE_BP5 parser confirm the numeric cast targets; Oracle CAST's
conversion table confirms NUMBER/binary floating targets. Cached parser evidence lives under
`artifacts/numeric-roots-source/`. Source contracts and generated SQL do not establish deployed syntax,
native UNION coercion, result accuracy or cost on the supplied databases. No live execution occurred.

Forty new unit and36 intercepted functional cases cover both modes/null settings/collection policies,
arrays/lists, originally empty/nonempty inline roots, ordinal pruning, extrema, scale-28 decimals,
floating subnormals, rebinding, native read hooks and nullable cross-carrier converters. Narrow decimal
columns retain wide query inputs; double-to-Single converters retain Single casts even for wide physical
storage. Initial38 unit cases failed on the missing carrier boundary/EF UnreachableException before
implementation. The functional fixture retains Decimal GetDecimal and exercises floating GetValue,
OceanBaseDecimal normalization and overflow separately. Supplied rows establish shaping only.

Independent source review found no production defect and reproduced a broader EF mapping-inference
boundary: directly projecting contextually converted collection elements can fail in SelectMany,
FirstOrDefault and Join. Two characterization cases cover those shapes in both modes; support notes
now state the broader exclusion. This family remains local implementation work; rejecting it does not
complete converter projection support. Final documentation/evidence re-review confirms the test counts,
package identity and corrected exclusions; its stale package-row evidence finding is also corrected.
No unresolved actionable findings remain within this module's documented scope.

Final EF10.0.0 Debug/Release and isolated EF10.0.14 Release each pass **3728** tests:
1748 unit +1110 intercepted functional +868 model specification +two project-consumer cases,
zero failures/skips. Evidence: `artifacts/numeric-roots-{red,red-detail,focused,functional,
projection-boundary,debug,release,patch,patch-restore}.log`. The final source snapshot adds the two
projection-boundary cases before rebuilding all three configurations. Provider assets remain EF10.0.0.

The same baseline-built `10.0.0-numeric-roots-r1-20261111` nupkg passes both-mode manual consumers on
EF10.0.0/10.0.14, numeric captured/empty-inline smoke, contents/dependency ranges, package-only graphs
and Release/archive/consumer DLL identity. SHA-256:
`504CC9F0DEB65AFE1FDC056B37B5C2E49570D3861F350104AF1F11071451F003`.
The preceding integer-roots-r1 package fails the new smoke with a caught UnreachableException/exit1.
Evidence: `artifacts/numeric-roots-package.log`, `artifacts/verify-numeric-roots-package.ps1` and
`artifacts/numeric-roots-previous-package.log`. The negative consumer's local-feed-only restore could
not resolve baseline EF packages; the ordinary sources/normal-user network resolved it without
warning overrides. No new dependencies, EF8/EF9, push, publication or branch synchronization.
Text/other carriers, converted element projections and the remaining EF10 inventory stay open.

### Wider integer collection-root checkpoint (2026-11-11)

Batch3aa follows Oracle lifecycle commit `a891959`. General collection roots now support all eight
signed/unsigned CLR integers, nullable forms and integer-backed enums. MySQL typing casts use the
actual provider carrier's SIGNED/UNSIGNED category; Oracle uses NUMBER. Column write precision does
not narrow query inputs. Captured arrays/lists retain exact row counts, duplicates, NULL cells,
current values and mapping-owned bindings/readers; originally empty inline roots retain their type
through pruning, including the ordinal-only case. Noninteger/null-converting carriers remain separate.

A derived EF type-mapping postprocessor handles standalone parameter roots without a comparison's
inferred mapping, a path that otherwise reaches EF10's UnreachableException. Contextual inference
remains authoritative, and existing converted-Join conflicts still reject. The source contracts are
the published EF10.0.0 classes cached for Batch3j/3k. No early default mapping or driver changes added.

Independent review reproduced an explicit ValueConverter<int?,ulong?> rejection in both captured
and originally empty roots. Separate RED stages establish the expansion and inline-type defects;
validation now unwraps nullable types while retaining the original mapping/converter. Read-only
re-review independently reran the reproduction and reports no unresolved actionable findings.

Final EF10.0.0 Debug/Release and isolated EF10.0.14 Release each pass **3652** tests:
1708 unit + 1074 intercepted functional + 868 model specification + two project-consumer cases,
zero failures/skips. This adds 38 unit and 24 functional cases; focused collection runs pass95/24.
Evidence: `artifacts/integer-roots-{red,nullable-red,nullable-inline-red,focused,debug,release,patch}.log`.
The new fixtures disable global provider caching. Earlier fixture corrections retained closure-backed
captures (direct Join arguments retain their original sequence) and translated collection selectors.

The same baseline-built `10.0.0-integer-roots-r1-20261111` package passes both-mode manual consumers
at EF10.0.0/10.0.14, all-eight/nullable/empty-root smoke, nullable converters, contents, package-only
graphs and Release/archive/consumer DLL identity. SHA-256:
`7BB309A8A0F8F253D65F3C839BBF74156112162CCFDC8109194E0A482E2454D4`.
Evidence: `artifacts/integer-roots-package.log` and `artifacts/verify-integer-roots-package.ps1`.
The preceding Oracle package rejects the new root smoke with caught exit1; the initial integer-root
package predates the nullable fix and is superseded by r1. Isolated restore required the normal user
network after sandbox TLS/SSPI failure; NuGet audit and warnings-as-errors remain enabled.

Native UNION coercion/results/cost, other collection carriers, broader converter projections and
the rest of the EF10 inventory remain open. No live database, EF8/EF9, push or publication occurred.

### Oracle owned user/schema lifecycle checkpoint (2026-11-11)

Batch7f is committed as `a891959` after `698e30c`. Typed/untyped `EnableOceanBaseUserManagement(userName, Func<string>)`
explicitly owns an Oracle user and all its schema objects. It requires a closed matching target login,
a different independent administration login and the explicit management factory. Owned names exclude
tenant-routing `@` and protected system users. Successful exact bound SYS.DBA_USERS probes establish
absence; Oracle errors never use MySQL's unknown-database classification. CREATE USER is followed by
only CREATE SESSION/CREATE TABLE grants; DROP USER targets exactly the quoted user with CASCADE.
Existing users retain passwords/privileges and use existing-schema table creation. Namespace DDL remains
non-atomic, so later failures require explicit repair instead of automatic destructive rollback.

The password callback runs only for creation; unsupported quote/control/empty encodings reject before DDL.
EF10 RelationalCommand.LogCommandText protects built-in command logs, including sensitive/error logging;
trusted interceptors can still inspect executable SQL. Published Connector0.2.0 diagnostics and pinned
V4.3.5 CREATE/DROP/GRANT/DBA_USERS docs were inspected. No native interoperability claim follows.

Fourteen new options and 56 intercepted cases cover mode/scope/ownership, catalog values, SQL/binds,
quoted names, callback timing, minimum grants, existing empty/populated users, target preservation,
timeouts, cancellation, caller transactions, error identity, cleanup and log redaction. Missing-API,
cross-mode error classification and routed-name regressions were observed failing before their fixes.
Independent review identified the routed-name ambiguity; its regression/fix is verified and no actionable
findings remain. A full-suite fixture cache threshold was resolved by isolating this test fixture's services.

Final default EF10.0.0 Debug/Release and isolated EF10.0.14 Release each pass **3590** tests:
1670 unit + 1050 intercepted functional + 868 model specification + two project-consumer cases,
zero failures/skips, with warnings-as-errors enabled. The isolated snapshot was explicitly rebuilt
to avoid retaining an older test assembly. Evidence: `artifacts/oracle-user-{red,error-code-red,routing-red,
focused,options,debug,release,patch,patch-rebuild}.log`.

The baseline-built `10.0.0-oracle-user-20261111` nupkg passes contents and both-mode manual consumers
at EF10.0.0/10.0.14, including sync/async Oracle user lifecycle/redacted logs, package-only graphs and
baseline Release DLL identity: SHA-256 `54C9C8EDEF396036DF1FC9B95096D7CE9304C4B875A8CA4E40A1258AD897A0EB`.
Evidence: `artifacts/oracle-user-package.log` and `artifacts/verify-oracle-user-package.ps1`.
The user-supplied live connections were saved outside the repository under User Secrets ID
`OceanBase.EntityFrameworkCore.LiveTests`; no values were committed or printed. No live execution,
EF8/EF9 adaptation, push or publication occurred. The overall EF10 goal remains incomplete.

### Default dependency audit gate restored (2026-11-11)

Batch8b follows `9fa3fd0`. Default restore reproduced NU1902/NU1903 failures from the official
EF10.0.0 specification package's Cryptography.Xml/Pkcs 9.0.0 dependency chain. SpecificationTests
now directly references centrally managed Cryptography.Xml 9.0.23, which resolves Pkcs 9.0.23 as
well. Neither library appears in the provider dependency graph. Audit and warnings-as-errors stay enabled.

Default restore and full Debug/Release tests pass without warning overrides: **3520** tests each,
1656 unit + 994 intercepted functional + 868 model specification + two project-consumer cases,
zero failures/skips. The `10.0.0-audit-20261111` package passes Verify-Package.ps1, retaining exactly
the original three runtime dependencies. Evidence: `artifacts/dependency-audit-{red,restore,debug,release}.log`
and `artifacts/dependency-audit-package/`. No new runtime functionality or live acceptance is claimed;
the previous selected-patch and manual packed-consumer results remain historical and were not rerun here.
Independent read-only review found no actionable issues in the diff, assets, logs or package boundary.
At this checkpoint Oracle namespace lifecycle was the next functional module, completed locally by Batch7f above.

### Explicit management connection checkpoint (2026-11-11)

Batch7e follows `82dd469` and adds typed/untyped
`UseOceanBaseManagementConnection(Func<OceanBaseConnection>)`. This is real namespace-management
functionality: MySQL operations can use independent administration credentials, routing and TLS hooks,
including when the application supplies its own target connection. The exact factory instance is wrapped
with EF `WithConnection(connection, owned: true)` and disposed by the provider. The factory must return
a fresh closed independent native connection with no selected MySQL database. Returning the target
rejects without disposing it; invalid independent candidates are disposed. The application's factory
authoritatively chooses the administration tenant; the application must select the same tenant as the
owned target. Callback/account names do not establish tenant equivalence.

Explicit database ownership, exact target scope, transaction rejection and prevalidated table plans
remain mandatory. A factory alone grants no ownership and is not invoked for ordinary queries,
existing-schema-only table creation, invalid ownership/models or pre-cancelled operations. The original
string-only fallback keeps its native identity/configuration/hook guards. Options clone the delegate
without putting it in service-provider identity, debug information or option logs. Normal EF diagnostics,
interceptors, runtime timeout, cancellation, error identity and owned disposal remain in use.

Four initial sync/async supplied/provider-created cases fail on the missing public API before implementation.
Final full EF10.0.0 Debug and Release and isolated EF10.0.14 Release each pass **3520** tests:
1656 unit + 994 intercepted functional + 868 specification + two project-consumer cases, zero failures/skips.
This adds three unit and 37 functional cases. Focused management tests pass96 and options tests pass15.
Intermediate runs caught two fixture assumptions (suppressed opens can repeat, and typed options require
the matching context); these were corrected before all full runs. Read-only independent review reports
no actionable findings. Two coverage limits remain explicit: already-open factory candidates and disposal
failure while rejecting an invalid independent candidate have no dedicated regression case yet.

The same baseline-built `10.0.0-management-profile-20261111` package passes manual consumers in both
modes at EF10.0.0 and 10.0.14, package contents, package-only graphs and Release/package/consumer identity:
SHA-256 `0440D389798E84374260CF8A030F2A20975CBC2C4B34D6E6E0BA9C63018A9E4A`.
The new factory smoke verifies supplied-target preservation and management disposal in MySQL; Oracle
still verifies its existing schema profile and unsupported namespace path. PackageConsumer remains outside
CI, catches errors and exits normally. No intentional crash or old-package negative execution in this slice.
Evidence: `artifacts/management-profile-{red,debug,release,patch,package}.log`, focused/options/functional
logs and `artifacts/verify-management-profile-package.ps1`. Baseline assets remain EF10.0.0.

### Resume here: remaining EF10 local inventory

1. Recheck branch/worktree and saved commit before editing. Batch3af Boolean collection roots,
   Batch3ae text collection roots, Batch3ad text/temporal extrema, Batch3ac contextual numeric element
   projections, Batch3ab decimal/floating collection carriers, Batch3aa integer collection carriers,
   Batch7f Oracle user lifecycle, Batch7e management factories and bulk-update rewrites are implemented;
   do not repeat them. Current evidence is above;
   older sections below are historical checkpoints, not a new implementation backlog.
2. Continue QRY-03/04/06/07/11 and MAP-06/07: remaining binary/interval/other collection carriers,
   broader converter/operator compositions, function/aggregate overloads and unsupported boundaries. Reuse
   exact EF10 contracts; negative tests must not be mistaken for implementation of a feature family.
3. Widen model/filter/materialization/graph-write contracts and official query/write/runtime specification
   fixtures, then audit the full deferred inventory for locally feasible work. The pinned Oracle docs
   and public assembly inspection remain under `artifacts/oracle-lifecycle-source/`; use public driver APIs.
4. Continue the full checklist: remaining mapping/converter/collection/function combinations, wider
   model/filter/materialization contracts, advanced bulk variants, required runtime/specification coverage,
   then the documented deferred-family feasibility audit. Deferred labels do not complete the local goal.
   Migrations/tooling, JSON/complex types, advanced inheritance/splitting, split queries and retries/
   ambient/distributed transactions remain unimplemented or incomplete as listed in the checklist.
5. Real database addresses were supplied again and stored in User Secrets. Missing live fixture integration
   is implementation work, not missing user access. Keep the comprehensive standalone live-test program, live execution and
   EF8/EF9 postponed until the user resumes those phases. Do not infer driver/server support or defects
   from intercepted tests. No push, branch synchronization, tags or NuGet publication occurred.

### MySQL original-value snapshot checkpoint (2026-11-11)

Batch9c aftere1ef05e replaces cyclic/dependent-join assignment rejection with a keyed derived-source
rewrite. SELECT /*+ NO_MERGE */ projects every original physical key and each complete target-encoded
setter expression. The outer UPDATE targets the physical table, joins all original keys and assigns
only projected values. Ordinary acyclic single-table assignments keep their ordered native path.
Derived joined sources use the rewrite conservatively: EF paging projections hide physical target
reads behind new aliases, so inspecting only outer column names would miss dependencies. Both mode
generators now accept normalized left joins directly, avoiding EF's anonymous-setter fallback failure.

Four initial MySQL failures (two Oracle controls passed) establish the old rejection; two later
left-join cases expose the inherited fallback and two paging cases expose hidden dependencies.
Fourteen new BulkDmlTests cases cover exact complete SQL, current positional binds, write bounds,
tags/timeouts, nullable Boolean swaps with/without left joins, key writes/paging, keyless rejection,
failure/cancellation identity, zero-row retry and unchanged tracked state. Two caller-transaction
cases and one SQL alias test cover no added savepoints/ownership changes and both alias namespaces.
Existing cycle/dependent-join rejection tests now assert successful mode-specific SQL.

Final Debug/Release atEF10.0.0 and isolated EF10.0.14 Release each pass3480=1653 unit+957 functional+
868 specification+two consumer tests, zero failures/skips. Independent review has no actionable
findings; the concurrent README update resolves its stale unsupported claim. Evidence:
artifacts/bulk-snapshot-{red,debug,release,patch}.log. The intermediate focused log includes a test
fixture mode mismatch, fixed by configuring the alias test with real MySQL services; final full runs
include that regression and all90 focused functional/nine SQL cases.

The same baseline-built 10.0.0-bulk-snapshot-20261111 nupkg passes both-mode consumers at both patches,
contents, package-only graphs and Release/package/consumer SHA-256 identity:
40933973781B0B3B89AB8814B2C8879170EEF33DF817C40D4EDC84DA715371AA.
The prior Oracle-bulk package rejects the new cyclic MySQL smoke with caught NotSupportedException/
exit1. Evidence artifacts/bulk-snapshot-{package,prior-package}.log and
artifacts/verify-bulk-snapshot-package.ps1. Baseline source assets remainEF10.0.0; manual consumer
execution stays outside CI.

Pinned public OceanBase UPDATE/NO_MERGE docs and parser grammar establish the intended SQL contract,
not actual optimizer/materialization/read consistency, duplicate-match choice, affected rows or costs.
Those remain live gates. Arbitrary incompatible converter expressions, advanced model/JSON/complex
bulk variants and the wider EF10 inventory remain work. No live tests, EF8/9, push or publication;
no driver/server defect demonstrated. The overall goal remains active.

### Historical Oracle joined/paged bulk update checkpoint (2026-11-11)

Batch9b aftereb0c837 implements Oracle joined and pushed-down ExecuteUpdate using a scalar source
SELECT per setter and an EXISTS target filter. Each source retains the complete translated tables,
joins and predicate; every physical primary-key column correlates to a fresh outer target alias.
Nested EF paging remains inside its source subquery. Keys, schema/table/column names and aliases are
quoted; alias selection scans nested source and setter scopes. Joined keyless targets and unnormalized
outer operators reject before opening. The ordinary single-table path remains native.

Oracle's translator now accepts normalized left joins directly rather than using EF10's default
UPDATE/FROM fallback, which lost anonymous setter bindings in the new regression. Tests cover inner,
left and self joins; target on either side, including the nullable side; composite/converted/renamed
keys and key assignments; nullable Y/N-to-T/F setters; selective named filters; current captures;
skip/take including zero; tags/timeouts; exception/cancellation identity and later zero-row execution;
caller-transaction attachment without savepoints/commit/rollback. SQL keeps all duplicate source
matches so the native scalar-subquery cardinality error is not silently replaced with an arbitrary row.

OracleBulkDmlTests adds26 intercepted cases, OracleBulkDmlQueryTests adds eight SQL cases and
TransactionTests adds two cases. The initial16-case RED run failed all16 on the previous unsupported
path. Final focused76 functional cases and eight SQL cases pass. Final full Debug/Release at
EF10.0.0 and isolated EF10.0.14 Release each pass3463=1652 unit+941 functional+868 specification+
two consumer tests, zero failures/skips. Independent review found no implementation defect; its three
important coverage suggestions are now closed by four extra sync/async cases and read-only follow-up.
Evidence: artifacts/bulk-oracle-{red,focused,debug,release,patch}.log.

The same baseline-built 10.0.0-bulk-oracle-20261111 nupkg passes both-mode consumers on both patches,
package contents, package-only dependency graphs and Release/package/consumer SHA-256 identity:
5D07CCF626A37B187CBDE4DF3D08E26F4D4153FC27CA275AB8B0324509669BBA.
BulkDmlSmoke adds the correlated Oracle update. The previous bulk-dml package rejects this new smoke
with a caught NotSupportedException and exit1, without an unhandled process crash. Evidence:
artifacts/bulk-oracle-{package,prior-package}.log and artifacts/verify-bulk-oracle-package.ps1.
Baseline source assets remainEF10.0.0. PackageConsumer is manual and is not executed by CI.

Native syntax, original-row read consistency, duplicate cardinality, affected rows, permissions and
repeated-source query costs still need live acceptance. MySQL snapshot assignments, incompatible
converter expressions and wider model/JSON/complex bulk shapes remain under DEF-03. The overall
EF10 goal remains active; no live execution, EF8/9, push or publication. No driver/server defect was
demonstrated, and previously supplied database access is not an implementation blocker.

### Historical bulk relational update/delete checkpoint (2026-11-11)

Batch9a after5f56125 promotes ordinary ExecuteUpdate/ExecuteDelete into active development, reusing
EF10's relational setter translation and immediate nonquery pipeline. MySQL UPDATE now renders
native table references/joins before SET with qualified targets, replacing the base UPDATE/FROM
syntax. Single-table dependent assignments are topologically ordered to read original values before
another setter overwrites them. Cyclic assignments and dependent joined assignments require a
snapshot rewrite and reject before opening. Oracle ordinary single-table UPDATE/DELETE retain native
aliases; joined/fallback UPDATE shapes reject until a native rewrite is implemented. These rejections
are implementation gaps, not demonstrated server restrictions.

Direct constant/current-parameter setters use target mapping write validation before opening, while
predicate inputs keep independent query bounds. Parameter reads disable SQL caching to recheck each
captured value. Bulk operations bypass tracking, do not automatically enforce concurrency or create a
shared transaction, and return affected rows including zero. Caller transactions are attached without
savepoints/commit/rollback. Statement tags survive EF10 UpdateExpression.VisitChildren reconstruction.

Independent review reproduced wrong storage for computed/cross-encoded Boolean setters: canonical
0/1 or source T/F could be written directly into Y/N. Eight RED cases now pass after target-aware
CASE re-encoding; four nullable cases retain SQL NULL. Arbitrary incompatible non-Boolean converter
server expressions reject before opening pending explicit translation. No new connector dependency.

BulkDmlTests adds48 cases; TransactionTests adds four caller-transaction cases. Final full EF10.0.0
Debug/Release and isolated EF10.0.14 Release each pass3427=1644 unit+913 functional+868 specification+
two consumer tests, zero failures/skips. Focused52 pass; original30-case suite exposed20 failures,
followed by joined dependency and converter regressions. Source/test/fix review has no outstanding
findings. Evidence artifacts/bulk-dml-{red,boundary-red,converter-red,focused,debug,release,patch}.log.

The same baseline-built 10.0.0-bulk-dml-20261111 package passes both-mode consumers at both patches,
content, package-only dependency graphs and Release/package/consumer SHA-256 identity:
4A386A69D2B92C2448405A419AF861B58309577D5B1107BF0BBB2F84D1D0F3F5.
BulkDmlSmoke adds assignment order/tags/binding/write bounds/delete/computed-Boolean checks. The prior
seeding package fails the new smoke with handled exit1; no crash dialog is requested. PackageConsumer
is still manual, outside CI execution. Evidence artifacts/bulk-dml-{prior-package,package}.log and
artifacts/verify-bulk-dml-package.ps1. Baseline assets remain EF10.0.0.

DEF-03 stays In progress: Oracle joined/paged update rewrites, MySQL cyclic/dependent-joined snapshots,
arbitrary converter expression translation, wider model/JSON/complex shapes and native bulk execution
remain work. Public OceanBase parser grammar inspection does not prove deployed-server acceptance.
No live database, EF8/9, push or publication occurred; no driver/server defect was demonstrated.

### EnsureCreated seeding callbacks checkpoint (2026-11-11)

Batch7d afterc1dc62d implements matching UseSeeding/UseAsyncSeeding callbacks after successful
EnsureCreated schema processing. Both delegates can coexist; only the matching one runs, while
opposite-only configuration rejects before opening or DDL. The callback receives the context and
operationsPerformed flag even when false for populated schemas or empty models. A fresh owned target
transaction verifies the actual scope before application code, commits after callback success and
disposes on all paths. Async forwards cancellation to begin/probe/callback/commit. Primary failures
retain secondary disposal errors in Data["OceanBase.SeedingCleanupError"]. No automatic retry or DDL
rollback is attempted; callbacks must be idempotent and concurrency-aware.

SeedingTests adds46 cases across both modes with real SaveChanges pipeline work under stateful
connection/command/transaction doubles. It covers delegate selection, flags/order/identity, empty
models, DDL/probe/callback/commit/cleanup failures and cancellation after callback return. Four
additional RED-to-GREEN cases remove the misleading "No DDL was executed" message when a scope
change is detected after successful DDL. The owned MySQL namespace case retains native identity and
proves DDL precedes a synthetic target transaction-start failure; positive physical namespace seeding
is not established. Existing invalid-plan coverage now uses unsupported HasData model data rather
than rejecting valid callback configuration. New test-only replacements avoid EF's process-wide
provider cache: the initial full run exceeded its twenty-provider warning threshold, fixed by fixture
isolation without changing production options or suppressing warnings globally.

Final full Debug/Release at EF10.0.0 and isolated EF10.0.14 Release each pass3375=1644 unit+861
functional+868 specification+two consumer tests, zero failures/skips. Focused46 pass. Evidence:
artifacts/seeding-{focused,debug,release,patch,scope-red,prior-package,package}.log. Independent
source/test/final-documentation review has no outstanding findings.

The baseline-built 10.0.0-seeding-20261111 nupkg passes content, both-mode consumers on both patches,
package-only dependency graphs and Release/package/consumer SHA-256 identity:
8C211BACF7A83652DE439BADFA55F3042F592CC86647BA3D9B4BB6DF87095B34.
SeedingSmoke covers callbacks/true-false flags/transactional scope/commit/disposal with native doubles.
The preceding database-management-r2 package rejects the new callback smoke with a handled
NotSupportedException and exit1, without Windows crash reporting. PackageConsumer remains manual and
outside CI execution. Baseline assets remain EF10.0.0; no dependency range changes.

SCH-02/DEF-09 remain incomplete: real DDL/transactions/rollback, Oracle namespace management,
custom management profiles, Migrate-time seeding/locking, retries and ambient/distributed transactions
remain work. Previously supplied database access is not the reason local features remain incomplete.
No live database, EF8/9, push or publication occurred; no connector/server defect is demonstrated.

### Official relational model specifications checkpoint (2026-11-11)

Batch8a afterd264667 adds the separate solution SpecificationTests project consuming the official
Microsoft.EntityFrameworkCore.Relational.Specification.Tests package. The adapter derives complete
inherited generic model families and uses real OceanBase services, conventions, validation, type
mapping and diagnostic definitions in each mode. No provider assertion overrides, exclusions or skips
were added. An EF connection-opening interceptor rejects sync/async access before network execution;
two adapter tests verify the real service assemblies, enforced mode and rejection in both modes.

| Official model family | MySQL passing | Oracle passing |
| --- | --- | --- |
| One-to-many | 90 | 90 |
| Many-to-one | 59 | 59 |
| One-to-one | 142 | 142 |
| Many-to-many | 36 | 36 |
| Inheritance | 31 | 31 |
| Owned types | 75 | 75 |

The inherited total is866, plus two adapter guards:868 specification tests, zero failures/skips.
Full solution Debug/Release and isolated EF10.0.14 Release each pass3329=1644 unit+815 functional+
868 specification+2 consumer tests, zero failures/skips. Both selected dependency graphs resolve the
matching official specification, relational and core packages; baseline source assets remain10.0.0.
Evidence: artifacts/specification-{restore,first,debug,release,patch}.log and
artifacts/specification-results/specification-first.trx, with specification-compat/ isolated assets.

No production behavior/package dependency changed, so this test/solution integration does not require a
new runtime pack or repeated same-package consumer probes. Existing database-management-r2 package
evidence remains scoped to its tested implementation. CI now discovers this project through the same
solution command; current hosted Windows/Linux execution remains unverified. The local adapter was
reviewed independently before commit.

These results establish the selected metadata/model-building contracts, including inherited metadata
paths for deferred families; they do not establish their runtime query/write/DDL support. Other model
families and official query/update/transaction/runtime specification fixtures remain local work.
TST-01 and the overall EF10 goal stay open. The user confirms database addresses were already supplied;
the missing live fixture/configuration integration is our implementation gap, not missing user access.
No live DB/application, EF8/EF9, push or publication occurred.

### Explicitly owned MySQL database namespace checkpoint (2026-11-11)

Batch7c after89dd02e adds typed/untyped EnableOceanBaseDatabaseManagement(databaseName), separate
from the existing-schema table-only permission. It declares ownership of the entire named MySQL
database, including objects outside the EF model. System database names reject; switching back to
EnableOceanBaseSchemaManagement disables namespace management. Oracle provisioning/deletion and
supplied/custom management connections remain locally developable follow-ups, not connector defects.

Create/Delete and EnsureCreated/EnsureDeleted use an owned independent provider connection with no
selected database, cloned injected EF dependencies/options, retained interceptors/runtime timeout,
bound information_schema probes and quoted CREATE/DROP DATABASE. Only the target connector pool is
cleared around mutations. A NULL catalog row cannot prove absence because visibility depends on
privileges: EnsureDeleted still attempts DROP, returning false only for known-outcome error1008.
Direct Delete retains that error; permission/network/unknown outcomes propagate. EnsureCreated
validates the full table plan before namespace creation, handles empty models and skips populated
schemas. Caller transactions reject. Cancellation and primary-versus-cleanup failures remain explicit.

Independent review identified stale EF connection strings and runtime native replacement/hooks as a
wrong-server management risk. Fourteen RED cases establish it; provider-created identity, shared
configuration validation, canonical EF/native agreement and routing/credential/TLS hook guards fix it
before management opens. EF SetConnectionString remains supported; native-only mutation, runtime
SetDbConnection and hidden-secret ambiguity reject. No private connector state is copied.

SchemaManagementOptionsTests adds six cases (12 total); DatabaseManagementTests adds59 intercepted
sync/async cases covering namespace lifecycle, names/quoting, invisible catalogs/error classification,
connection identity, configuration mutation, transaction/plan/seeding guards, cancellation and cleanup.
Full Debug/Release and isolated EF10.0.14 Release each pass2461=1644 unit+815 functional+2 consumers,
zero failures/skips. Independent source/fix review approves. The same baseline-built
`10.0.0-database-management-r2-20261111` package passes both modes on EF10.0.0/10.0.14, package content,
package-only dependency graphs and provider DLL identity:
`7335872C39CA355B535C04B3AFFF3FCB1AD0DC5E491C21E4FF83C9AB3E7F91F0`.
The preceding package rejects the new API with a handled MissingMethodException, exit1 and no unhandled
process exception. Baseline assets remain EF10.0.0. Evidence: artifacts/database-management-{options,
profile-red,focused,debug,release-final,patch,package-final,previous-package}.log, the corresponding
package/compat directories and artifacts/verify-database-management-package.ps1.

Native permissions, server syntax/name/case rules, concurrent DDL, physical pool/session effects and
actual rollback/cleanup remain unverified live gates. DDL is non-atomic and not retried; later failure
may leave a namespace or partial objects, with no automatic DROP rollback. SCH-01/02/03 remain open.
Overall EF10 work continues; no live DB/application, EF8/EF9, push or publication occurred.

### Boolean ordering and string representation checkpoint (2026-11-11)

Batch3z after7235f38 adds exact bool.CompareTo(bool/object) and Boolean/nullable/boxed ToString
identities, including the ignored IFormatProvider overload. Independent operand normalization makes
ordering and True/False strings independent of native/numeric/text/inverted storage encodings. Signed
Int32 CASE results avoid MySQL unsigned arithmetic. Leading NULL guards preserve nullable.Value SQL
NULL propagation; object-overload NULL, including boxed nullable fields/captures, compares below a
non-null Boolean and returns1. Nullable ToString NULL becomes empty in MySQL and NULL in Oracle.

Initial10 RED cases establish missing normalization/string translation. Independent review identifies
unsigned subtraction and boxed nullable-null behavior; six additional RED cases pin both. CASE/null
guards resolve them. EF may retain equality compensation after the guards; tests assert guard order.
BooleanQueryTests adds16 cases; BasicRelationalTests adds16 sync/async cases with both null settings,
global defaults, current captures and supplied native int/string/NULL reads. The synthetic fixture
supplies Text/BoxedText's shared SQL column once, respecting EF projection deduplication.

Full Debug/Release and isolated EF10.0.14 Release each pass2396=1638 unit+756 functional+2 consumers,
zero failures/skips. The same baseline-built `10.0.0-boolean-order-text-20261111` package passes both
modes on EF10.0.0/10.0.14, package-content checks, package-only graphs and DLL identity:
`5ABF877AF83A4FD8B4F28AF467009F7B76D8DC08E26CAEFF6692C3646B8D5CFE`.
Previous Boolean-method package rejects the new ToString predicate; baseline assets remain10.0.0.
Independent fix review approves. Evidence: artifacts/boolean-order-text-{red,review-red,focused,
functional,debug-final,release-final,patch-final,package-final,old-package-red}.log and corresponding
boolean-order-text-package/ and boolean-order-text-compat/ directories.

Following the user's crash-report observation, PackageConsumer now catches managed verification
failures at its executable entry point, prints stderr diagnostics and returns1 rather than raising an
unhandled process exception. The previous-package rejection passes an explicit exit1/no-unhandled
assertion; the unchanged new package passes both modes/patches with exit0 after this consumer-only
change (artifacts/consumer-handled-{failure,success}.log). CI does not execute PackageConsumer;
it only reads package content through Verify-Package.ps1. The solution's two ConsumerTests configuration
tests remain ordinary CI tests. This is separate from the postponed live test application.

Native comparisons/coercion/collation, string representation semantics and volatile single evaluation
remain unverified. Parsing/Convert APIs and the wider local Required/deferred inventory remain open;
no connector defect or deployed-server limitation was established. Overall goal stays active; no live
DB/application, EF8/EF9, push or publication work occurred.

### Boolean equality methods and nullable defaults checkpoint (2026-11-11)

Batch3y after6aa90d9 adds exact framework Boolean typed/boxed/nullable Equals and both
Nullable<bool>.GetValueOrDefault overloads. Equality reuses independently normalized == and its
configured null semantics; defaults use native Boolean COALESCE. Nullable instance Equals arrives
through System.Object's instance identity, handled only with statically Boolean operands. Dynamic
objects/other method identities keep inherited EF behavior. A custom same-name Equals control pins
inherited name-based translation without new normalization; arbitrary method bodies are not supported.

BooleanQueryTests adds18 cases; BasicRelationalTests adds16 sync/async intercepted cases with both
null settings, global Y/N defaults, current true/false/null captures and supplied native reader values.
Initial14 RED includes12 missing-method cases, two boundary tests subsequently distinguish valid
MySQL WHERE0 spelling from a real HasValue defect. Inherited EF converter compensation descends into
unary IS NOT NULL, truth-converts its nullable storage operand and erases the filter. A binary != typed
NULL survives that pass and becomes the correct direct null test during ordinary EF null processing.
Both modes cover positive/negated native/numeric/text HasValue; raw Value retains storage conversion.

Full Debug/Release and isolated EF10.0.14 Release each pass2364=1622 unit+740 functional+2 consumers,
zero failures/skips. The same baseline-built `10.0.0-boolean-method-20261111` package passes both modes
on EF10.0.0/10.0.14, package-content checks, package-only graphs and Release/package/consumer DLL identity:
`51F43B1866E922E783DB1BEE4C4631E4B5782D47CC0191B9C5CB8BB731F25B6B`.
The previous Boolean package fails the new method-normalization consumer assertion; baseline assets
remain10.0.0. Independent source review approves and confirms preservation of inherited custom-method
behavior. Evidence: artifacts/boolean-method-{red,boundary-red,focused,functional,debug-final,
release-final,patch-final,package-final,old-package-red}.log, artifacts/boolean-member-red.log,
boolean-method-package/ and boolean-method-compat/.

Native execution, coercion/collation and repeated volatile evaluation remain unverified. Wider Boolean
APIs and other Required/deferred local work remain open. No connector defect or deployed-server limit
was established; the HasValue fault belongs to EF/provider query integration. Overall goal stays active;
no live test application/database, EF8/EF9, push or publication work occurred.

### Converted Boolean comparison and logic checkpoint (2026-11-11)

Batch3x after4ce40a8 extends independently converted Boolean ==/!=, NOT, AND/OR and lifted &/|,
predicate/order roots, ternary tests/Boolean arms and Boolean coalesce in both modes. Noncanonical
numeric/text encodings use the existing NULL-preserving CASE through a strict internal scalar marker;
native Boolean/numeric1/0 operands keep simple SQL. EF still owns nullable equality compensation.
Computed results use native Boolean mapping, while plain converted properties keep their converter.

BooleanQueryTests adds24 cases; BasicRelationalTests adds18 command/reader/SQL-shape cases, including
native true/false/null supplied results, current captures under both null settings and global Y/N
defaults. Existing XOR/integer paths remain covered. Initial RED18/20 establishes missing behavior.
Independent review finds two issues: Double narrowing misclassifies an exact decimal near1, and
global Y/N defaults misclassify native SQL predicates as text storage. Exact per-carrier checks and
native predicate result mapping resolve both. Near-one mutation RED2 and global predicate RED1
(MySQL, with Oracle positive control) pin those failures. Expanded numeric comparison/LIKE/EXISTS
reader checks pass with global defaults. Scoped re-review reports no additional actionable findings.

Final Debug/Release and isolated EF10.0.14 Release each pass2330=1604 unit+724 functional+2 consumers,
zero failures/skips. The same baseline-built `10.0.0-boolean-20261111` package passes both modes on
EF10.0.0/10.0.14, content, package-only graphs and Release/package/consumer DLL SHA-256 equality:
`C7BA526827BD9DD80AEC9EE8F373ABF44C3B0960C775CD5041B1EA8176DFC6CB`.
The prior Boolean-XOR package fails the new logical-normalization assertion as intended; source
baseline assets remain EF10.0.0. Evidence: artifacts/boolean-{focused,functional,debug-final,
release-final,patch-final,package-final}.log, boolean-{red,decimal-canonical-red,global-predicate-red,
global-predicate-green,old-package-red}.log, boolean-package/ and boolean-compat/.

Native results/coercion/collation and repeated volatile evaluation remain acceptance boundaries;
wider Boolean method/translator families and the remaining Required/deferred local inventory are
still open. No connector defect or deployed-server limitation was established. Overall goal active;
no live database/test application, EF8/EF9, push or publication work occurred.

### Boolean XOR checkpoint (2026-11-11)

Batch3w after e47892c implements bool/bool? XOR in both modes, retaining the ExclusiveOr node
through EF null processing and emitting NULL-preserving normalization/comparison only at SQL
generation. Each operand uses its own numeric/text true/false encodings; mixed/inverted converters
do not become the result converter. Oracle handles XOR as a search condition and preserves UNKNOWN
when converting to a scalar. Reject null-converting/ambiguous/unsupported and Oracle empty encodings.

Initial16 query regressions fail against the previous source. A second2-case RED catches converter
output NULL being accidentally replaced with a fallback Boolean. Independent review then identifies
the model-default mapping route: four sync/async RED tests reproduce Decimal-to-String reader errors
with a global Y/N converter. The result mapping lookup now excludes the model; all16 unit and12
intercepted cases pass, including native true/false/null reads and current Boolean capture transitions.
The XOR IS NULL test follows EF's valid operand-null simplification rather than demanding XOR SQL.
Scoped independent re-review approves with no remaining important findings.

Full baseline Debug/Release and isolated EF10.0.14 Release each pass2288=1580 unit+706 functional+2
consumers, zero failures/skips. The same baseline-built `10.0.0-boolean-xor-20261111-r1` nupkg passes
content and both-mode consumers on EF10.0.0/10.0.14, package-only graphs and Release/package/consumer
DLL SHA-256 equality: `70CBA19F37C22D734B75FF46E75630470F09674643F834F566D2C784E1C508E1`.
The preceding shift package fails the new Boolean normalization assertion as intended. Baseline
source assets remain EF10.0.0. Evidence: artifacts/boolean-xor-{focused,functional,debug-final,
release-final,patch-final,package-final}.log, boolean-xor-{red,null-carrier-red,global-converter-red,
old-package-red}.log, boolean-xor-package/ and boolean-xor-compat/.

Valid stored encodings, native collation/coercion/results and repeated volatile evaluation remain
acceptance boundaries. The later Batch3x above covers converted comparison/logical operators;
wider Boolean APIs remain local follow-up work. No connector defect or deployed-server limitation was established.
The overall goal remains active; no live tests, EF8/EF9 adaptation, push or publication occurred.

### Integer shift checkpoint (2026-11-11)

Batch3v after e52a4d3 implements LINQ integer left/right shifts in both modes. It preserves C# small
integer promotions, masks negative/oversized counts by31/63, handles signed32 wrap and signed right
extension, and maps each count as Int32 independently of the left storage/result. EF10 SQL binary
nodes reject shifts, so the relational visitor uses strict internal function markers with inherited
NULL propagation; a focused generator partial emits native MySQL shifts or Oracle NUMBER identities.
Oracle discards high bits before multiplication, keeping intermediates below2^65. Integer-exponent
ObNumber POWER source uses repeated squaring; deployed native precision/results remain unverified.

Initial RED32 cases confirm missing translation. A later exact SQL-prefix RED1 catches and fixes the
MySQL signed32 CAST grouping; fragments alone would not prove that syntax. Final35 unit/8 intercepted
cases pass, including malformed markers, count boundaries and Int32 binding, full-width synthetic
readers, current parameters and NULL/non-NULL cache transitions. EF retains NULL within the shift
formula instead of replacing the entire projection; assertions pin NULL masks and removed binds.
Independent source/test/consumer review approves. Full Debug/Release and isolated EF10.0.14 Release
each pass2260 (1564 unit,694 functional,two consumers), zero failures/skips. Evidence:
artifacts/shift-{focused,debug,release,patch}-final.log, shift-red.log, shift-cast-red.log and shift-research/.

The same baseline-built `10.0.0-shift-20261111` nupkg passes content checks and both-mode consumers
at EF10.0.0/10.0.14, including changed captured counts and independent Int32 parameter metadata.
Package-only dependency graphs and Release/package/consumer DLL hashes match:
`A647275298ED6C2DA79B6AD43BF8F7F7CD41602902A4F0EFB903866450F5CF0D`.
The preceding bitwise package fails the new shift predicate translation as intended. Source baseline
assets remain EF10.0.0. Evidence: artifacts/shift-package-final.log, shift-old-package-red.log and shift-package/.

A .NET10 compiler probe rejects expression-tree `>>>` with CS7053 before EF executes. This is a
language boundary, not an ADO.NET defect or unsupported OceanBase server feature. No new public
unsigned-right-shift function is introduced. Boolean XOR is covered by the later Batch3w above; broader numeric casts/overflow and wider
query/model/write/schema/specification/deferred inventory still need local development. No live DB,
standalone testing program, EF8/EF9, push or publication; the complete EF10 goal remains active.

### Integer bitwise checkpoint (2026-11-11)

Batch3u after42e45a8 implements integer AND/OR/XOR/complement for both modes with lifted nullable
behavior, numeric enum storage and C# small-integer promotions. MySQL signed results are normalized
from native unsigned64 results, unsigned complement retains its CLR width, and integer CAST targets
use legal SIGNED/UNSIGNED syntax. Oracle uses BITAND plus exact NUMBER identities; UInt64 AND splits
into two32-bit halves to avoid depending on an undocumented128-bit BITAND range. The audited public
server source uses signed64 conversion; no live server limitation or connector defect is asserted.

Initial focused RED14/18 exposes missing rendering/carrier rejection. Follow-up RED2 catches small
binary promotion loss; RED2 protects ordinary converted-property casts from overly broad rejection.
Independent review confirms both findings resolved. Focused22 unit and8 intercepted command cases pass,
including synthetic511 for byte OR256, enum254, signed/null/full64 results and current parameter values.
The first full run exposes old collection tests pinning illegal MySQL BIGINT/INT CAST targets; corrected
expectations retain both nested casts and all original node/type/mapping identities. Re-review approves.
Final Debug/Release and isolated EF10.0.14 Release each pass2217 (1529 unit,686 functional,two consumers),
zero failures/skips. Evidence: artifacts/bitwise-{debug,release,patch}-final.log and focused RED/GREEN logs.

The same baseline-built `10.0.0-bitwise-20261111` nupkg passes package content and both modes on
EF10.0.0/10.0.14, with package-only dependency graphs and identical Release/package/consumer DLL SHA256
`95F45678F1CC432C94AF209F467C78433B8865D98FEF44720F24A39B758BA2C2`.
The preceding transaction-recovery package fails the new signed-result bitwise consumer assertion.
Source baseline assets remain EF10.0.0. Evidence: artifacts/bitwise-package-final.log,
bitwise-old-package-red.log and bitwise-package/.

Shifts, overflow/narrowing/signedness-changing cast equivalence, arbitrary converter equivalence and
volatile single evaluation remain separate local/semantic work; native result/coercion checks remain
live gates. Standalone live testing, EF8/EF9, push and publication remain postponed. The complete local
EF10 goal is still active.

### Multi-column generated readback checkpoint (2026-11-11)

Batch5e after9d1f4d2 adds26 intercepted GeneratedValuesTests cases in both modes/sync/async. They characterize
already-working composite-key single-identity inserts and multi-output defaults/computed values, explicit default
overrides, nullable enum concurrency output/snapshot refresh, required NULL errors, failed later conversion,
zero affected rows and missing MySQL generated rows. No provider/connector defect was reproduced; no production
behavior changed. Automatic transactions are disabled in this fixture; physical expression evaluation and
atomicity remain live gates.

Independent review found the Oracle fixture ignored INTO bind order. Temporary reversal of output binds passed
all26 before the fixture fix and fails8 afterward; restored production source passes26. The fixture now resolves
named INTO binds and validates uniqueness/complete parameter sets. Focused re-review approves. Full final
Debug/Release and isolated EF10.0.14 Release each pass2187 (1507 unit,678 functional,two consumers), zero
failures/skips. Evidence: artifacts/generated-values-{focused,debug,release,patch}-final.log and mutation logs.

No package rebuild is required for this test/docs-only slice. The previous transaction-recovery package evidence
remains its historical checkpoint. A fresh provider build has updated commit informational-version metadata
(9d1f4d2 instead of16b1d92), so its SHA is not asserted identical to that preceding package. Source baseline
assets remain EF10.0.0. No live DB, standalone program, EF8/EF9, push or publication; full EF10 goal active.

### Save cancellation and cleanup checkpoint (2026-11-11)

Batch6c after16b1d92 specializes EF10's batch execution loops: automatic async rollback/release use an uncancelled
token, failed opens do not consume caller leases, and cleanup/diagnostic failures retain the original save error.
Public explicit transaction methods keep their cancellation contracts. Registered transaction-start loops dispose
untracked native handles after callback/factory failure and restore entry accounting. Native disposal failure can
leave an unusable context/transaction; neither retry safety in that branch nor physical rollback is established.

Initial cancelled-write RED2, broader RED8, sync RED6 and transaction-start RED4 confirm the failures. Independent
review found a later Started-callback allocation leak; its RED4/GREEN correction captures/disposes the untracked
handle. Final source review approves. TransactionTests now passes70, including positive registered transaction
ownership/nested rejection and default/escalated cleanup diagnostics. Debug/Release and isolated EF10.0.14 Release
each pass2161 (1507 unit,652 functional,two consumers), zero failures/skips.

The same baseline-built transaction-recovery-20261111 package passes content and both-mode/patch consumers,
including native-double automatic cancellation recovery; package-only graphs and Release/package/consumer DLL
hashes match. The preceding schema-lifecycle package fails the new automatic-recovery smoke. Baseline source
assets remain EF10.0.0. Evidence: artifacts/transaction-recovery-*.log, -compat/, -package/.
No real database execution, standalone live test program, EF8/EF9, push or publication. The full EF10 goal remains
active; namespace lifecycle, wider query/model/write/specification and deferred inventory still need development.

### Existing-schema creation checkpoint (2026-11-11)

Batch7b after ed11c8b implements EnsureCreated/CreateTables, sync/async, behind the explicit per-context
EnableOceanBaseSchemaManagement target. The database/user/schema must already exist. Validate all model DDL,
transactions/callbacks and table/FK scopes before opening; verify the exact current scope before commands.
EnsureCreated returns false when any ordinary table exists; CreateTables skips that check. No retry, DROP
rollback or reconciliation occurs. Partial DDL can remain after failure. Namespace creation/deletion remain
guarded; the option enables table creation only.

Initial missing-API/behavior RED evidence is followed by eight reproduced boundary failures: excluded cross-scope
FK principals and final cleanup masking the DDL error. Both are fixed; cleanup errors are attached to the original
exception. Focused46 lifecycle and six option cases pass. Full Debug/Release and isolated EF10.0.14 Release pass
2129 (1507 unit,620 functional,two consumers), zero failures/skips. The later async-seeder coverage addition
receives focused checks. Independent source review approves with no blocking findings. Explicit-enlistment and
stateful physical-session/caller-opened lifecycle fixtures remain nonblocking coverage follow-ups; current
intercepted fixtures prove EF leases/control flow only.

The same baseline-built schema-lifecycle-20261111 nupkg passes content, both-mode sync/async schema consumers on
EF10.0.0/10.0.14, package-only graphs and identical DLL hashes. Source baseline assets remain10.0.0.
Evidence: artifacts/schema-lifecycle-*.log, schema-lifecycle-compat/, schema-lifecycle-package/.
No live DDL, standalone live test program, EF8/EF9, push or publication. The broader EF10 goal remains active.

### Offline create scripts checkpoint (2026-11-11)

Batch7a after8a4ad6a implements GenerateCreateScript without opening a connection. It emits ordinary scalar
tables, nullability, keys/indexes/checks, foreign keys after all tables, supported identities/defaults and computed
columns. Mapped/excluded/view/owned/join/one-to-one/cyclic shapes have DDL tests; these are not full runtime
feature claims. Explicit unsupported shapes fail instead of silently losing their metadata. Mutating lifecycle
methods remain guarded until the ownership profile is implemented.

Initial RED8 confirms the missing script API. SchemaScriptTests now passes27 focused cases; Debug/Release and
isolated EF10.0.14 Release each pass2077 (1501 unit,574 functional,two consumers), zero failures/skips.
Independent review found missing MySQL computed-column collation and invalid TPT/entity-split identity inference.
RED/GREEN regressions cover both: preserve computed collation; explicitly reject TPT/TPC and entity splitting
until their table-specific generation contract is implemented. Final independent review approves with no open
findings. Same baseline-built schema-script-20261111 nupkg passes content and both-mode/patch consumers,
package-only graphs and identical assembly hashes. The preceding transactions package fails the new script
smoke at the expected NotSupportedException. Baseline source assets stay10.0.0.

Evidence: artifacts/schema-script-*.log (final suites use -final2), schema-script-compat/ and
schema-script-package/. No live DDL, server/version compatibility or schema deletion is claimed. Continue with
explicit ownership and existing-schema EnsureCreated/CreateTables; standalone live test program stays postponed.

### Transactions and savepoints checkpoint (2026-11-11)

Batch6b after456902f registers an EF10 transaction factory. Oracle release is a logical no-op with EF
diagnostics/interceptors and async cancellation, avoiding unsupported RELEASE SAVEPOINT SQL; the savepoint
survives until replacement or transaction end. Creation/rollback retain EF's quoted commands, including its
internal __EFSavePoint name. MySQL and all begin/commit/rollback/ownership behavior stay inherited.

TransactionTests adds38 cases using deterministic native doubles: ownership/disposal, isolation, async tokens,
different-connection rejection, quoted savepoints, multi-batch commit/failure, caller transaction recovery/retry,
and MySQL generated-readback attachment/failure. Four Oracle RED regressions fail before implementation;
focused38 and full Debug/Release/isolated10.0.14 each pass2050 (1474 unit,574 functional,two consumers), zero
failures/skips. Same baseline-built transactions-20261111 nupkg passes content, both-mode/patch consumers,
package-only assets and assembly identity. Baseline assets remain10.0.0. Independent review approves with no
actionable findings. Evidence: artifacts/transactions-*.log, transactions-compat/, transactions-package/.

This proves client transaction control flow only. Active native disposal must actually roll back on the server;
native cleanup failure, cancellation recovery and live state remain open. Oracle release is a known mode
difference, not a reproduced connector defect. No database execution or standalone live test program added.

### Generated readback shape checkpoint (2026-11-11)

Batch5d adds two MySQL sync/async cases for a composite key with two generated identity columns. Both fail
with the documented provider limitation before command interception; this is not a server or connector
limitation claim. Focused Release passes2/2; full Debug/Release and isolated EF10.0.14 Release each pass2012
(1474 unit,536 functional,two consumers), zero failures/skips. Independent read-only review found no code/test
defects and one stale execution-ledger status, now corrected. No production or package change. Evidence:
artifacts/generated-readback-{debug,release,patch}-final.log. Oracle multi-output, defaults/computed generation,
transactions, schema and wider local inventory remain open.

The standalone live-database test program requested on 2026-11-11 has been explicitly postponed by the user
until provider functionality is implemented. It has not been added; continue the local implementation plan.

### Tracked graph writes and nullable concurrency checkpoint (2026-11-11)

Batch5a after6714626 adds24 GraphUpdateTests cases for both modes sync/async: three-level dependency
ordering/generated-key propagation/reverse deletes, composite original-key/token predicates, NULL current
writes, zero-row conflict entries, connector-error identity and in-flight cancellation/token identity.
MySQL DML/readback shares the native connection; the fixture disables transactions and proves no atomicity.
NULL originals previously emitted IS NULL but retained an unused native bind, breaking MySQL positional
binding. The common batch parameter producer now omits those predicate binds while retaining NULL SET binds.

Initial16-case run reproduced four MySQL failures; final focused24 pass. Debug/Release and isolated
EF10.0.14 Release each pass1986 (1474 unit,510 intercepted,two project consumers), no failures/skips.
The same baseline-built10.0.0-graph-update-20261111 nupkg passes the content gate and both-mode consumers
on10.0.0/10.0.14, including composite NULL concurrency and NULL writes. The preceding lifecycle package
fails the new smoke at its unused-bind regression. Package/Release/consumer DLL identities and package-only
graphs pass; baseline assets stay10.0.0. Independent review found one P2 test-coverage gap in async
token-count assertions; the intentionally failing assertion reproduced it and the corrected assertion
passes24 focused cases plus fresh full suites. No production findings remain.

Evidence: artifacts/graph-update-*.log (including graph-update-token-red/green); isolated artifacts/graph-update-compat/;
package/consumers under
artifacts/graph-update-package/. Wider generation, graph shapes, diagnostic logging, transactions, schema
and specification inventory remain open. No real database, EF8/EF9, hosted CI, push or publication.

### Failed-open lifecycle checkpoint (2026-11-11)

Batch6a fixes shared provider Open/OpenAsync rollback after opening callbacks and ambient warnings fail.
It restores entry EF accounting, closes only the captured connection when newly opened, preserves caller
and outer ownership, and retains opening cancellation if native cleanup also fails. Two private EF10
accounting fields are isolated through UnsafeAccessor; patch/major upgrades must recheck that contract.
Cleanup failure is recorded in Exception.Data["OceanBase.OpenCleanupError"] and may require direct cleanup.

Eight registered-provider count regressions failed before implementation. ConnectionLifecycleTests now
passes36 focused cases, including physical-state doubles, cancellation, retry and replacement-connection
cleanup. Reverting the captured-close target reproduced both replacement regressions. Final Debug,
Release and isolated EF10.0.14 Release each pass1962 (1474 unit,486 intercepted,two project consumers),
zero failures/skips. Independent review identified cancellation masking and replacement targeting; both
were corrected and regression-tested. Final scoped source/test/docs/evidence review approves, zero findings.

The same baseline-built10.0.0-connection-lifecycle-final-20261111 nupkg passes package content checks and
both-mode consumers on EF10.0.0/10.0.14, including failed-open accounting and retry. Evidence:
artifacts/connection-lifecycle-*-final.log; isolated artifacts/connection-lifecycle-compat/; package under
artifacts/connection-lifecycle-package/. No database connection, hosted CI, publication or EF8/EF9 work.
The full local EF10 inventory, transactions and live lifecycle gates remain open.

### Command timeout and cancellation checkpoint (2026-11-11)

Batch5b adds eight CommandExecutionBoundaryTests cases across MySQL/Oracle and sync/async. The real EF query
pipeline reaches intercepted DbCommands with the configured timeout, preserves an in-flight cancellation
exception/token, forwards the async caller token and permits a failed query to be retried with the EF-visible
connection state closed between operations. Network opening is suppressed and rows are synthetic; no physical
connector close/reuse, wire or server claim.

Focused Release passes8/8 with no production change. Final Debug/Release/isolated14 each pass1994
(1474 unit, 518 functional, two consumers), zero failures/skips. No package rebuild was needed because
production binaries are unchanged. Independent review found two P2 evidence/coverage issues: sync retry
coverage was added, and claims were narrowed from physical lifecycle to EF-visible state/command replay.
CON-05 remains partial for live timeout/cancellation and resource behavior; DIA logging and transactions remain
separate. EF8/EF9 and real database work stay postponed.

### Command diagnostics and redaction checkpoint (2026-11-11)

Batch5c adds 16 CommandLoggingTests cases across MySQL/Oracle, sync/async, successful/failing queries and
the sensitive-data logging switch. The real EF logging pipeline reports SQL and synthetic failures; runtime
captured parameter values are hidden by default and visible only when `EnableSensitiveDataLogging` is enabled.
The initial RED was caused by a compile-time constant rendered as a SQL literal; captured variables provide the
correct parameter boundary. Focused Release passes16/16; final Debug/Release/isolated14 each pass2010
(1474 unit, 534 functional, two consumers), zero failures/skips. Independent review found and closed the
async-interceptor/event-log assertion gap; tests now require the expected sync/async callback and EF command
event text. No production code changed and no package rebuild is needed. Connector/server logging sinks,
structured redaction policy and live diagnostics remain unverified.

### Model, graph materialization and filters checkpoint (2026-11-11)

Batch4a/4b/4c adds 58 inherited-provider characterization cases in ModelMaterializationTests.
Both modes cover composite/alternate keys, required/optional relationships, owned/shadow metadata,
Include/ThenInclude and owned graph reads, three tracking modes, First/Single/default cardinality,
and EF10 named-filter composition/disable with current context and user-predicate parameters.
No provider production change was necessary; schema/generation validation, graph writes, unnamed
filters, wider graph shapes and actual server results remain open.

Full EF10.0.0 Debug/Release and isolated EF10.0.14 Release each pass 1926 tests (1438 unit,
486 intercepted, two project consumers), zero failures/skips. Independent review found two
test-assertion gaps: complete joined projections and filter logic/binding order. Both were fixed;
the final covering 58 cases pass in all three configurations and scoped re-review approves with
no remaining findings. Evidence: artifacts/model-materialization-*.log and the isolated
artifacts/model-materialization-compat/ build. Baseline assets remain EF10.0.0.
No new nupkg or hosted-runner evidence is claimed for this test/docs-only checkpoint.
The full EF10 local goal remains active; EF8/EF9 and live database tests remain postponed.

### Hosted Windows/Linux CI evidence (2026-11-11)

Read-only GitHub inspection confirms [run 42865290840](https://github.com/cocosip/efcore.oceanbase/actions/runs/42865290840)
for e41981e succeeded on ubuntu-latest and windows-latest: restore, Release tests, pack and package-content
verification each succeeded. This covers the search checkpoint, including preceding wide-decimal changes,
not later local commits or database integration. Earlier db676ff also passed in
[run 42854480450](https://github.com/cocosip/efcore.oceanbase/actions/runs/42854480450).
Run/job/step metadata is saved under artifacts/local-development/. The earlier log archive endpoint returned
HTTP403 (repository admin rights); no new per-test count or resolved EF patch is inferred from job metadata.

### Raw SQL and database facade checkpoint (2026-11-11)

Batch3s wires the scoped database creator required by EF's facade, adds read-only connection and
table probes, and preserves a single prefix for explicitly named raw SQL parameters. Local tests
cover all four raw SQL APIs, both modes, sync/async, values with SQL metacharacters, NULL, provider
parameter metadata, cancellation and error classification. Schema DDL is still unimplemented.
Debug, Release and isolated EF10.0.14 Release each pass 1840 tests (1418 unit, 420 intercepted,
two project consumers), zero failures/skips. The same `raw-facade-20261111` package passes both modes
on 10.0.0 and 10.0.14 and passes package-content verification. Independent source review found and
corrected Oracle session-schema scope; failure after physical open still needs CON-03 coverage.

Batch3t supplies a MySQL command builder that rewrites EF named templates to positional `?` binds,
orders repeated values by SQL occurrence, rejects ambiguous quote/comment/mixed/missing bindings and
keeps Oracle named commands unchanged. Query, raw SQL, tracked write and generated-key paths have
offline EF/Connector boundary coverage, including the connector's client-prepared literal fallback.
Native MySQL/Oracle execution, caller-owned repeated streams, server metadata and post-open connection
cleanup remain live or follow-up gates.
Debug, Release and isolated EF10.0.14 Release now pass 1868 tests (1438 unit, 428 intercepted,
two project consumers), zero failures/skips. The `positional-final-20261111` package passes content checks
and both-mode EF10.0.0/10.0.14 package consumers.
Independent review found and regression-tested reader-rejection connection balancing and the final
initialization interceptor's view of all positional parameters. Caller-owned native duplicates and
SQL_MODE-dependent backslash literals still reject explicitly; use distinct native parameters and
bound string values. Full QRY-10 acceptance remains open.

### Gregorian calendar checkpoint (2026-11-11)

Batch3r adds DateTime/DateOnly DayOfYear and DayOfWeek. MySQL uses native calendar functions;
Oracle uses Gregorian and ISO-week TRUNC anchors without territory-sensitive weekday formatting.
Unit tests pin full arithmetic and nullable SQL; intercepted commands check integer/enum/nullable
materialization in both sync/async paths. Synthetic leap/year/Sunday outputs do not prove engine calculation.
Twelve new cases failed translation before implementation. Independent review identified and resolved a
stale unsupported assertion and strengthened formula/null coverage; native calculation/metadata stay open.
Debug, Release and isolated EF10.0.14 Release each pass 1792 tests (1418 unit, 372 intercepted,
two project consumers), zero failures/skips. The same `calendar-final-20261111` package passes
both modes on EF10.0.0 and 10.0.14 plus the package-content gate. EF8/EF9/live tests stay postponed.

### Floating Round checkpoint (2026-11-11)

The 16 binary Round signatures on Math, MathF, Double and Single translate with all five constant
rounding rules and constant digits 0..15 for Double or 0..6 for Single. SQL preserves the original
result mapping, guards scaling and integral rounding, and normalizes Single at its precision boundaries.
The eight decimal Round signatures retain their existing behavior.

Debug, Release and isolated EF Core 10.0.14 Release each passed **1780 tests** (1414 unit, 364
intercepted, two project consumers), with zero failures or skips. The same `floating-round-20261111`
package passed both modes on EF Core 10.0.0 and 10.0.14, including package content and identity checks.

Latest package: `artifacts/checkpoint-floating-round-20261111/OceanBase.EntityFrameworkCore.10.0.0-checkpoint.floating-round-20261111.nupkg`.
Evidence: `artifacts/local-development/batch3q/`. Runtime digits and rules remain local work. Actual
arithmetic, cast precision, NULL and result metadata still need server tests. Live database testing and
EF Core 8/9 remain postponed.

### Historical decimal Round checkpoint (2026-11-11)

Eight exact `Math.Round` and `decimal.Round` overloads now translate with constant digits from 0
through 28 and all five midpoint rules. The provider emits explicit decimal operations for both modes,
preserves NULL, and shapes results through the checked decimal reader with the original converter.
Runtime digits/rules and floating point overloads remain open.

Debug, Release and isolated EF Core 10.0.14 Release each passed **1768 tests** (1406 unit, 360 intercepted,
two project consumers), with zero failures or skips. The same `decimal-round-20261111` package passes
both modes on EF Core 10.0.0 and 10.0.14. The previous package fails the new Round predicate. Package
contents, dependency graph, assembly identity and unchanged baseline assets were checked. Independent
source review passed.

Package: `artifacts/checkpoint-decimal-round-20261111/OceanBase.EntityFrameworkCore.10.0.0-checkpoint.decimal-round-20261111.nupkg`.
Evidence: `artifacts/local-development/batch3p/`. The local checks establish SQL translation, binding
and shaping; native decimal precision, result metadata and computation remain live gates.
EF Core 10 local work remains active. Live database testing and EF Core 8/9 remain postponed.

### Historical scalar numeric extrema checkpoint (2026-11-11)

The provider translates 26 `Math`/`MathF`/`decimal` Min/Max overloads and numeric
`EF.Functions.Least`/`Greatest`. It preserves operand order, nesting and the first operand's result
mapping. A singleton returns its mapped operand. Ordinary queries use a snapshot in the cache key,
so captured array changes produce current SQL even when the array's structural hash stays the same.

Missing translation and stale SQL were reproduced locally; after the fixes, 43 unit and six intercepted
cases passed. Debug, Release and isolated EF Core 10.0.14 Release each passed **1760 tests** (1400 unit,
358 intercepted, two project consumers), with zero failures or skips. The same `extrema-20261111` package
passed both compatibility modes with EF Core 10.0.0 and 10.0.14. Package contents, package-only dependency
graph, baseline DLL identity and unchanged baseline assets were verified. Independent source review passed.

Package: `artifacts/checkpoint-extrema-20261111/OceanBase.EntityFrameworkCore.10.0.0-checkpoint.extrema-20261111.nupkg`.
Evidence: `artifacts/local-development/batch3o/`. Local tests verify SQL, cache, binding and supplied reader
values. Native NULL behavior, mixed numeric coercion, result metadata and numeric edge cases still require
OceanBase execution. `Round`, wider query coverage, and model, write, transaction and schema work remain
in the local plan. Real database integration and EF Core 8/9 remain postponed.

### Historical numeric aggregate checkpoint (2026-11-11)

Exact numeric Sum/Average and generic numeric Min/Max now use native aggregate translation through the
provider aggregate service, retaining plugin priority and filtered CASE/DISTINCT ordering. AVG keeps its
original mapped input; model int/long Average has a fresh converter-free Double result. Same-type results
retain the source converter, result-only decimal readers and checked numeric decoding. Final Single casts
use neutral FLOAT/BINARY_FLOAT targets, including physically wider declarations and mixed numeric carriers.
SUM retains its mapped native node so EF's COALESCE uses converted model zero; its generated Int32 body
metadata has a narrow marker allowance. Oracle Single literal generation now accepts that synthetic zero
through validated numeric normalization and rejects narrowing overflow. Invalid owned mappings fail before
inherited fallback, while unrelated methods and Count/LongCount keep their existing owners.

Focused279 numeric aggregate unit cases and16 intercepted cases pass, along with prior numeric/shape
regressions. Initial8 native-AVG/wide-Single RED failures and an Oracle zero-literal RED become GREEN.
Final Debug/Release and isolated EF10.0.14 Release each pass **1711 tests** (1357 unit, 352 intercepted
pipeline, two project consumers), zero failures/skips. The same baseline-built
`10.0.0-checkpoint.aggregates-20261111` package passes both modes/patches for native AVG, all four aggregate
names, neutral Single results, filtered DISTINCT/HAVING/order and prior checks. The previous transcendental
package fails the new aggregate assertion. Content/package-only graph, packed/baseline DLL identity and
unchanged baseline EF10.0.0 assets pass. Independent source review passes with no actionable findings.

Package: `artifacts/checkpoint-aggregates-20261111/OceanBase.EntityFrameworkCore.10.0.0-checkpoint.aggregates-20261111.nupkg`.
Evidence: `artifacts/local-development/batch3n/`. Supplied results establish command/shaper/reader behavior,
not native aggregate calculation or server grouping/empty results. Actual accumulator precision, return
metadata, CASE/COALESCE types, casts/rounding/overflow remain live gates. Wider temporal/math/collection
queries, nonnumeric extrema, model/materialization/write/transaction/schema and specification/deferred
audits remain local work. The complete EF10 goal remains active; real DB and EF8/9 remain postponed.

### Historical exact transcendental checkpoint (2026-11-11)

All twelve Math/MathF Sqrt, Exp, Log (one/two arguments), Log10 and Pow identities now translate. MySQL
Log(x,base) uses LN(x)/LN(base), including bases in (0,1); Oracle uses LOG(base,x), with a fresh integer ten
for Log10. Each operand retains its own mapping/converter; primary x owns the result convention. Actual
Single-provider results normalize the completed operation through neutral FLOAT/BINARY_FLOAT SQL casts.
Decimal-provider function results use a result-only GetValue reader clone, preserving converters/facets and
ordinary GetDecimal behavior elsewhere. Binary-to-decimal CLR conversion can round at materialization;
it does not change SQL arithmetic/predicate precision. No finite input cap or SQL decimal narrowing is added.

Focused65 unit/12 intercepted tests pass following54 missing-dispatch RED failures and a focused private-
marker validation RED/GREEN correction. Final Debug/Release and isolated EF10.0.14 Release each pass
**1416 tests** (1078 unit, 336 intercepted pipeline, two project consumers), zero failures/skips. The same
baseline-built `10.0.0-checkpoint.transcendental-20261111` package passes both modes/patches with all twelve
server predicates, dialect/Single normalization and prior checks. The preceding Floor/Ceiling package
rejects Math.Sqrt in a predicate. Content/package-only graph and packed/baseline DLL identity pass;
baseline assets stay EF10.0.0. Independent source review passes with no actionable findings.

Latest package: `artifacts/checkpoint-transcendental-20261111/OceanBase.EntityFrameworkCore.10.0.0-checkpoint.transcendental-20261111.nupkg`.
Evidence: `artifacts/local-development/batch3m/`. Supplied readers prove parameter/materialization paths,
not function calculation. Native return metadata, domain NULL/warnings/errors, quotient precision, casts,
overflow/underflow/subnormals and exact rounding remain live gates. External custom decimal mapping decoders
outside the owning result-reader seam reject explicitly. Round, wider temporal/math/operators/aggregates/
collections, model/write/transaction/schema work remain local gaps. The overall EF10 goal stays active;
real DB and EF8/9 stay postponed.

### Historical exact Floor/Ceiling checkpoint (2026-11-11)

All eight Math/MathF/decimal Floor/Ceiling identities now translate. Oracle uses FLOOR/CEIL; MySQL uses
TRUNCATE plus a guarded CASE, avoiding the documented BIGINT return path of native FLOOR/CEIL. Original
input/result mappings and converters remain owners. Precision zero and provider-carrier one have fresh
converter-free mappings; nullable carrier declarations unwrap only for the nonnull syntax constant.
The translator adds no numeric input bound, clamp or precision cast. Native calculation remains unverified.

Focused coverage passes 46 unit and 12 intercepted cases. Full Debug/Release and isolated EF10.0.14
Release each pass **1339 tests** (1013 unit, 324 intercepted pipeline, two project consumers). The same
baseline-built `10.0.0-checkpoint.floor-ceiling-20261111` package passes both modes/patches for all eight
server predicates and their dialect shapes plus prior checks. Previous inline-empty package rejects the
new Floor predicate. Content/package-only graph pass; baseline assets stay EF10.0.0. Independent source review passes with no actionable findings.

Latest package: `artifacts/checkpoint-floor-ceiling-20261111/OceanBase.EntityFrameworkCore.10.0.0-checkpoint.floor-ceiling-20261111.nupkg`.
Evidence: `artifacts/local-development/batch3l/`. Supplied readers do not evaluate numeric SQL. Native
TRUNCATE/comparison/arithmetic/CASE result typing, finite extremes/subnormals, decimal exactness, guarded-arm
evaluation and actual Oracle results remain live gates. Stable expressions are the tested domain; arbitrary
volatile DbFunctions have no CLR single-evaluation promise. Round/transcendental and wider query/model/write/
transaction/schema work were still local gaps there; the transcendental slice is now recorded above.
The overall EF10 goal stays active; real DB and EF8/9 stay postponed.

### Originally empty inline integral roots checkpoint (2026-11-11)

Originally empty inline int/int? Count/Any/Select/join and nested roots retain their original element type
through delegated query translation, inferred mappings and pruning. Immutable typed NULL prototypes render
genuinely zero-row bodies. A demonstrated EF pruning defect renames a sole retained ordinal to Value while
ORDER BY still references _ord; a bounded correction restores that name only on these annotated empty roots.
Captured/nonempty roots and general pruning remain inherited. Conflicting inferred mappings still reject.

Focused coverage passes 57 unit and eight intercepted cases. Full Debug/Release and isolated EF10.0.14
Release each pass **1281 tests** (967 unit, 312 intercepted pipeline, two project consumers). After full
suites, a test-only newline assertion portability edit passes the focused eight cases; production is unchanged.
The same baseline-built `10.0.0-checkpoint.inline-empty-20261111` package passes both modes/patches for
original inline-empty Count/nullable Any/nested join and the sole-ordinal regression, plus preceding checks.
Content/package-only graph pass; baseline assets stay EF10.0.0. Independent source review passes with no actionable findings.

Historical package: `artifacts/checkpoint-inline-empty-20261111/OceanBase.EntityFrameworkCore.10.0.0-checkpoint.inline-empty-20261111.nupkg`.
Evidence: `artifacts/local-development/batch3k/`. The narrow SetTables internal-API dependency is covered by
the selected patch checks; precompiled annotation quoting and current hosted/Linux execution remain unverified.
General text/other root carriers, broader converted projections and wider query/model/write/schema work
remain local gaps. Native execution/results/typing/order/limits still need both real modes. Its next planned
numeric slice was Floor/Ceiling, now recorded above. The overall EF10 goal stays active.

### Captured integral roots and nonempty inline roots checkpoint (2026-11-11)

General captured int/int? arrays/lists now expand before EF's concrete IntTypeMapping cast in both modes,
preserving raw values, mappings/converters, exact row counts/duplicates and ordinal positions. Empty/null
translated parameters retain typed zero-row metadata. The generator emits SELECT/UNION ALL SELECT, with
quoted aliases and Oracle DUAL in every arm. Narrow synthetic typing uses SIGNED/unqualified NUMBER;
explicit nested and Type-changing casts stay on their original path. Bare membership behavior is retained.

Focused coverage passes 40 unit and 16 intercepted cases. Full Debug/Release and isolated EF10.0.14 Release
pass **1256 tests** (950 unit, 304 intercepted pipeline, two project consumers). A subsequent test-only nested-
CAST extension passes its two existing cases in all three configurations; production/package stay unchanged.
Independent source and scoped test re-review pass. The same baseline-built
`10.0.0-checkpoint.integral-roots-20261111` nupkg passes both modes/patches for changed correlated collection
roots, empty relations and Oracle DUAL per arm. Previous text-membership package fails the parameter-root cast.
Content/package-only graph pass; baseline assets remain EF10.0.0.

Historical package: `artifacts/checkpoint-integral-roots-20261111/OceanBase.EntityFrameworkCore.10.0.0-checkpoint.integral-roots-20261111.nupkg`.
Evidence: `artifacts/local-development/batch3j/`. Native UNION typing/results/order/limits remain unverified.
Earlier conflicting inferred mapping still excludes some converted Join Value projections; native joins and
consistently inferred converted membership/property reads pass locally. Next is originally empty inline
integral roots. General text/other carriers and broader converter projections remain local work; goal active.

### Scalar text collection membership checkpoint (2026-11-11)

Bare inline/captured text array/list Contains now has EF's element JSON metadata. Oracle expands provider-text
IN parameters before the existing scalar normalizer and EF null processing, retaining raw model values,
converter/mapping identity, safe names, per-execution snapshots and raw-value padding. MySQL keeps inherited
expansion. Provider-empty and model-empty-after-suffix remain distinct; default compensation and native
relational UNKNOWN are separate policies. No JSON SQL transport or general VALUES support is implied.

Focused tests pass 25 unit and 20 intercepted cases. Debug/Release and isolated EF10.0.14 Release each pass
**1200 tests** (910 unit, 288 intercepted pipeline, two project consumers). The same baseline-built
`10.0.0-checkpoint.text-membership-20261111` nupkg passes both modes at EF10.0.0/10.0.14; the preceding package
fails the new string collection mapping. Content/package-only graph gates pass; baseline assets stay EF10.0.0.
Independent source and docs/consumer reviews passed; the consumer assertion follow-up passed scoped re-review.

Historical package: `artifacts/checkpoint-text-membership-20261111/OceanBase.EntityFrameworkCore.10.0.0-checkpoint.text-membership-20261111.nupkg`.
Evidence: `artifacts/local-development/batch3i/`. Native collation/padding/LOB equality/charset/results and
current hosted execution remain unverified. Next is captured integral general collection roots/nonempty inline
roots. Originally empty inline roots and general text/other carriers remain local work; the EF10 goal stays active.

### Integral membership and ordinary relational shapes checkpoint (2026-11-11)

Bare int/int? array/list Contains now uses EF10 scalar expansion with exact element metadata. Oracle scalar
string normalization excludes collection envelopes. The scoped compiled-query key retains the full relational
base key and adds current collection mode, fixing reproduced cross-context parameter/literal cache reuse.
Inherited ordinary/composite/.NET10 left joins, scalar subqueries, Any/All and Count/LongCount are characterized
without replacing their already-correct services. General VALUES/text collections and wider aggregates remain.

Focused tests pass 30 unit and 40 intercepted cases. Final Debug/Release and isolated EF10.0.14 Release each
pass **1155 tests** (885 unit, 268 intercepted pipeline, two project consumers). Independent code and scoped
cache-fixture reviews pass. The same baseline-built `10.0.0-checkpoint.membership-20261111` nupkg passes both
modes at EF10.0.0/10.0.14; the prior numeric package fails the new captured collection query. Content and
package-only graph checks pass; baseline source stays EF10.0.0.

An initial isolated-patch model-reference assertion exposed a retention assumption in the test fixture.
Controlled cache pressure reproduces identity failure despite equal keys/shared services. Positive retention
now uses a test-owned cache and two explicit-eviction cases verify equivalent rebuilding; every original
identity/key/connection assertion remains. The original natural failure timing is not proven; no production
cache change was made to force retention.

Historical package: `artifacts/checkpoint-membership-20261111/OceanBase.EntityFrameworkCore.10.0.0-checkpoint.membership-20261111.nupkg`.
Evidence: `artifacts/local-development/batch3h/`. Server query results and hosted execution for this historical
source remain unverified. Its next planned slice was scalar text collection membership, recorded above.

### Numeric Abs, Sign and Truncate checkpoint (2026-11-11)

Twenty-two exact Math/MathF/decimal methods now translate to native ABS/SIGN and MySQL TRUNCATE(x,0)
or Oracle unary TRUNC. Strict numeric mapping validation preserves operand converters and full existing
finite ranges; SIGN and precision-zero use independent converter-free int mappings. NULL tests remain.

Genuine RED covered 88 exact-signature/mode/null combinations. Focused GREEN passes 108 unit and 12
intercepted cases. Debug/Release and isolated EF10.0.14 Release pass **1083 tests** (853 unit, 228 intercepted,
two project consumers). Independent spec/quality review passed. The same baseline-built
`10.0.0-checkpoint.numeric-core-20261111` nupkg passes both modes at EF10.0.0/10.0.14; the preceding temporal
package fails the new numeric predicate. Package content and package-only graph checks pass.

Historical package: `artifacts/checkpoint-numeric-core-20261111/OceanBase.EntityFrameworkCore.10.0.0-checkpoint.numeric-core-20261111.nupkg`.
Evidence: `artifacts/local-development/batch3g/`. Native numeric results/domain/overflow/floating families
and hosted execution for this source remain open. Next is integral membership and ordinary relational shapes;
broader queries, model/writes/transactions/schema and the overall local EF10 goal remain active.

### Ordinary temporal parts checkpoint (2026-11-11)

Twelve exact members now translate: DateTime.Year/Month/Day/Hour/Minute/Second, DateOnly.Year/Month/Day
and TimeOnly.Hour/Minute/Second. MySQL uses native part functions; Oracle uses allowlisted EXTRACT syntax.
Oracle DATE clock extraction first casts to TIMESTAMP(7); SECOND extracts decimal then applies TRUNC.
Final results use converter-free int mappings, preserving source carriers/converters and independent NULL tests.

After the independent review's test-only follow-up, focused checks pass 21 unit and eight intercepted cases.
Full Debug/Release and isolated EF10.0.14 Release pass **963 tests** (745 unit, 216 intercepted pipeline, two consumers). Captured DateOnly
and TimeOnly values/DbType/precision, numeric/binary carrier rejection and representable endpoints are covered.
The same baseline-built `10.0.0-checkpoint.temporal-parts-20261111` package passes both modes on EF10.0.0/10.0.14;
the preceding Substring package fails the new temporal predicate. Content and package-only graph gates pass.

Historical package: `artifacts/checkpoint-temporal-parts-20261111/OceanBase.EntityFrameworkCore.10.0.0-checkpoint.temporal-parts-20261111.nupkg`.
Evidence: `artifacts/local-development/batch3f/`. Native extraction/casts/calendar/fractional/interval behavior
and hosted execution for this source remain open. Clocks/offset/fractional/Add/duration and broader queries
remain separate work. Next is numeric Abs/Sign/Truncate; the overall EF10 goal remains active.

### Substring checkpoint (2026-11-11)

Exact Substring(int) and Substring(int,int) now use native SUBSTRING/SUBSTR with long-result origin
adjustment. int.MaxValue safely becomes positive 2147483648. Dynamic bindings and receiver result mapping,
including fixed/national/LOB/converter metadata, are preserved. Function NULL tests remain observable;
the carried Oracle computed-needle NULL search regression is also covered.

Focused checks pass 12 unit and eight intercepted cases. Debug/Release and isolated EF10.0.14 Release each
pass **934 tests** (724 unit, 208 intercepted pipeline, two consumers). Independent spec/quality review passed.
The same baseline-built `10.0.0-checkpoint.substring-20261111` package passes both modes on EF10.0.0/10.0.14,
including both arities and the maximum-start boundary; the previous search package fails the new translation.
Package-content and package-only graph checks pass.

Historical package: `artifacts/checkpoint-substring-20261111/OceanBase.EntityFrameworkCore.10.0.0-checkpoint.substring-20261111.nupkg`.
Evidence: `artifacts/local-development/batch3e/`. Native range/character/padding/LOB results, including the
unverified NCLOB extension, remain server gates. Hosted CI above applies through e41981e. Next is ordinary
temporal members and wider numeric/query work; the overall EF10 goal remains active.

### String search checkpoint (2026-11-11)

Exact Contains(string), StartsWith(string) and EndsWith(string) now translate with INSTR and native suffix
extraction. Guards and CASE produce a total Boolean: NULL input is false and NOT is true in both null
settings. Known empty needles match a non-null receiver; Oracle inspects converted constants/captures before
empty-to-NULL normalization. Raw bindings and native DbFunctions are preserved; command-SQL caching stays
conservative. Converted-empty literals and unrelated unmapped methods have regression coverage.

Focused tests pass 24 query cases and eight intercepted cases. Debug/Release and isolated EF10.0.14 Release
each pass **914 tests** (712 unit, 200 intercepted pipeline, two consumers). Independent spec/quality review passed.
The same baseline-built `10.0.0-checkpoint.search-20261111` package passes both modes on EF10.0.0/10.0.14,
including search syntax and repeated captured nonempty/empty/null values. The preceding wide-decimal package
fails the new method translations. Package-content and package-only graph checks pass.

Historical package: `artifacts/checkpoint-search-20261111/OceanBase.EntityFrameworkCore.10.0.0-checkpoint.search-20261111.nupkg`.
Evidence: `artifacts/local-development/batch3d/`. Native collation/padding/character/LOB results remain live
gates; exact CLR ordinal/culture/null-exception behavior is not promised. The later e41981e hosted run is
recorded above. The Substring follow-up follows this checkpoint; wider query work and the goal remain active.

### Wide exact decimal to floating checkpoint (2026-11-11)

Batch 2d fixes the confirmed shared normalization overflow: OceanBaseDecimal values parse directly as Single
or Double using the public invariant connector representation. Direct Single rounding avoids a Double
intermediate; finite-to-infinity conversion rejects. Exact integer/decimal and native non-finite paths remain.

RED reproduced 29 unit and eight intercepted failures. Final Debug/Release and isolated EF10.0.14 Release
each pass **882 tests** (688 unit, 192 intercepted pipeline, two consumers). Independent spec/quality review
passed. The same baseline-built `10.0.0-checkpoint.wide-decimal-20261111` package passes both modes on
EF10.0.0/10.0.14, including exact Single midpoint bits 0x3f800001 and a wide Double read. The previous unary
package fails the new reader smoke. Package content and package-only graph checks pass.

Historical package: `artifacts/checkpoint-wide-decimal-20261111/OceanBase.EntityFrameworkCore.10.0.0-checkpoint.wide-decimal-20261111.nupkg`.
Evidence: `artifacts/local-development/batch2d/`. This proves normalization of synthetic wrapper values;
no new NUMBER-backed storage, live database, Linux or hosted CI acceptance is claimed for this checkpoint.
The later search slice is recorded above; the full EF10 goal remains active.

### Unary string functions/character-length checkpoint (2026-11-11)

Exact parameterless ToLower/ToUpper/Trim/TrimStart/TrimEnd and string.Length now translate through derived EF
method/member providers. SQL uses native casing/space trimming and character counts; Oracle empty/null behavior
retains conservative function nullability. Scoped services preserve plugins/DbFunctions/mode isolation. Trim
returns variable text while preserving Unicode/converters and Oracle LOB families, including lowercase spellings.

Focused tests pass 24 unit and eight intercepted cases after missing-translation and four LOB regression failures.
Debug/Release and isolated EF10.0.14 Release each pass **833 tests** (647 unit, 184 intercepted pipeline, two
consumers). Independent review passed. The same baseline-built `10.0.0-checkpoint.unary-20261111` package passes
both modes at EF10.0.0/10.0.14; the preceding arithmetic package fails the unary predicate. Package-content and
package-only graph checks pass. At this local checkpoint, no live database, Linux or hosted CI run was
recorded; the subsequent db676ff hosted evidence is recorded above.

Historical package: `artifacts/checkpoint-unary-20261111/OceanBase.EntityFrameworkCore.10.0.0-checkpoint.unary-20261111.nupkg`.
Evidence: `artifacts/local-development/batch3c/`. Native casing/space/character semantics do not promise exact CLR
culture/Unicode-whitespace/UTF-16 behavior. Wide-decimal, search and Substring follow-ups are recorded above;
wider query work remains open. The goal remains active.

### Integer division/remainder checkpoint

Integer CLR division now renders MySQL DIV or Oracle one-argument TRUNC; Oracle remainder uses MOD and MySQL
retains `%`. Classification uses the translated CLR type, including promotions and nullable values, rather than
the NUMBER declaration. Decimal/Single/Double and fractional casts retain `/`; grouping and existing mappings
are unchanged. Value-preserving numeric conversion and negative/nullable/captured operands are covered.

Focused tests pass 12 unit and four intercepted cases after RED. Debug/Release and isolated EF10.0.14 Release
each pass **801 tests** (623 unit, 176 intercepted pipeline, two consumers). Independent review passed. The same
baseline-built `10.0.0-checkpoint.arithmetic-20261110` package passes both modes on EF10.0.0/10.0.14; the previous
concat package fails the new arithmetic smoke. Package identity/assets/README/ranges and package-only graph pass.

Historical arithmetic package: `artifacts/checkpoint-arithmetic-20261110/OceanBase.EntityFrameworkCore.10.0.0-checkpoint.arithmetic-20261110.nupkg`.
Evidence: `artifacts/local-development/batch3b/`. Synthetic reads prove client generation/binding/reading, not
server arithmetic/predicate membership, zero-divisor or overflow behavior. Next local work is unary string
functions/character length and wider query support. The overall goal remains active; no live test/publication.

### String concatenation and Oracle query-null checkpoint

String addition and fixed Concat overloads now use MySQL CONCAT and Oracle grouped `||`; numeric addition is
unchanged. Oracle processing precedes EF nullability optimization, preserving potentially-null concat results
and native empty-string behavior in comparisons/coalesce. Empty/null classification uses provider-converted
values without changing caller parameters; normal string/enum/Guid converters are covered. Experimental
null-converting string query converters reject explicitly. Oracle provider-string parameters conservatively
disable command-SQL caching; model and query-compilation caching remains available.

Focused tests pass 33. Debug/Release and isolated EF10.0.14 Release each pass **785 tests** (611 unit,
172 intercepted pipeline, two project consumers). Independent spec/quality review passed. The same baseline-built
`10.0.0-checkpoint.concat-20261110` package passes both modes on EF10.0.0/10.0.14, including native concat and
changing Oracle fallback parameters. The previous numeric package fails the new syntax check. The repeatable
package-content gate and package-only dependency graph pass. These checks do not execute OceanBase functions.

Historical concat package: `artifacts/checkpoint-concat-20261110/OceanBase.EntityFrameworkCore.10.0.0-checkpoint.concat-20261110.nupkg`.
Evidence: `artifacts/local-development/batch3a/`. Next local query work includes integer division/remainder,
string search/member functions and the wider checklist. The provider remains incomplete.

Windows/Linux CI adds restore, Release tests, pack and package-content checks (`5feef06`); YAML and independent
review passed. The later db676ff hosted run is recorded above. No push, publication or database execution
was performed as part of this inspection.

### Configuration/cache/SQL boundary checkpoint

Both generic/untyped configuration overloads now have both-mode positive/negative coverage. Tests verify actual
EF model/singleton reuse for compatible options, mode separation, design-time/runtime identities, safe option
debug information, identifier/schema escaping and StringBuilder parity. Consumer provider detection covers both
modes. No production fix was needed in this batch; the existing contracts passed the added characterization tests.

Full Debug and Release each pass **606 tests** (462 unit, 142 intercepted pipeline, two consumers), up from 563.
Focused options/helper coverage passes 66 tests. The standard DatabaseFacade.GetDbConnection/SetConnectionString
paths exposed the still-missing IRelationalDatabaseCreator service; these are explicitly open CFG-04/SCH-01 work.
Underlying IRelationalConnection delayed string assignment is covered; this does not claim those facade APIs work.
No live connection was made. The previous EF10.0.14/package results below remain scoped to the text checkpoint.

### Oracle fixed-point alias checkpoint

Bare Oracle DECIMAL/NUMERIC now expose precision 38 and scale zero, retaining their trimmed declarations through
converter-only clones. Configured facets and changed-precision clones produce explicit declarations with the
same alias. Integer/Boolean/enum mappings share these defaults. Converted current decimal writes reject
fractional loss before execution; query/InputOutput inputs remain value-inferred, and bare NUMBER is unchanged.

Debug/Release on EF10.0.0 and isolated Release on EF10.0.14 each pass **627 tests** (475 unit, 150 intercepted
pipeline, two project consumers). Focused tests progressed from 10/97 unit and 6/8 pipeline failures to all passing.
Independent review passed. The same baseline-built `10.0.0-checkpoint.alias-20261110` local package passes
both-mode consumers on EF10.0.0 and EF10.0.14, including alias metadata/input/output and NUMBER independence.
The new consumer fails against the preceding text package, confirming the regression check. Nuspec identity,
net10.0 assembly, dependency ranges and package-only consumer graph were inspected. The package still lacks a
README; no publication or live database execution occurred.

Historical alias package: `artifacts/checkpoint-alias-20261110/OceanBase.EntityFrameworkCore.10.0.0-checkpoint.alias-20261110.nupkg`.
The historical text package below does not include this change. At this checkpoint, remaining scalar work included temporal
facet/clone consistency and exact integer normalization for unexpected floating reader values; see the local plan.

### Temporal facet/clone checkpoint

Both-mode temporal resolution now rejects actual size/fixed-length facets while preserving EF's parsed temporal
precision. Model properties recheck configured length and precision. Declaration clones stay within their current
transport family and retain DbType, fractional/interval-leading precision and complete SQL. Unsafe postfix/DbType
overrides reject; legitimate unchanged/converter/precision clones retain their reader and wide query-input hooks.

Final Debug/Release and isolated EF10.0.14 Release each pass **706 tests** (554 unit, 150 intercepted pipeline,
two project consumers). Focused tests pass 131 after RED and an independent-review fix round. Review confirmed
both transport and postfix findings addressed; an unnecessary public Clone override was avoided after checking
the exact EF API. Expanded consumers fail against the earlier alias/unfinished temporal probes and pass with the
same new `10.0.0-checkpoint.temporal2-20261110` package on EF10.0.0/10.0.14, both modes. Package-only dependencies,
nuspec identity/ranges, net10.0 assembly and included README were inspected. No live database claim or publication.

Historical temporal package: `artifacts/checkpoint-temporal2-20261110/OceanBase.EntityFrameworkCore.10.0.0-checkpoint.temporal2-20261110.nupkg`.
Evidence: `artifacts/local-development/batch2b/`. The first temporal probe predates review fixes and is not current
evidence. Next local scalar work is exact integer normalization and the remaining characterization cells.

### Exact integer normalization checkpoint

Finite Single/Double fractions now reject before conversion to any of the eight integer CLR types. Exact integral
values, checked ranges and non-finite failures retain existing behavior; floating CLR targets are unchanged.
Reader and generated key/enum propagation share this normalization. Property precision/scale validation now also
covers integer/Boolean mappings; four Oracle property conflicts reproduced before the fix. Nullable endpoints,
Boolean false/null and Guid.Empty use the existing scalar/EF pipeline helpers.

Focused unit tests pass 116 and generated-output tests pass ten after RED. Debug/Release and isolated EF10.0.14
Release each pass **752 tests** (590 unit, 160 intercepted pipeline, two project consumers). Independent review
passed. The same baseline-built `10.0.0-checkpoint.numeric-20261110` package passes both modes at EF10.0.0/10.0.14;
the preceding temporal2 package fails the new floating-fraction reader check. Identity/net10.0/README/ranges and
the package-only graph were inspected. No live database execution or publication.

Historical numeric package: `artifacts/checkpoint-numeric-20261110/OceanBase.EntityFrameworkCore.10.0.0-checkpoint.numeric-20261110.nupkg`.
Evidence: `artifacts/local-development/batch2c/`. Before string work, the published connector source was checked
at its nuspec revision: GetDecimal explicitly converts OceanBaseDecimal through ToDecimal, so no duplicate
provider decimal-reader transport is justified. Live decimal/wire behavior remains open. Next local work is
string concatenation/nullability and then the wider query translation inventory; scalar release gates remain open.

### Package README preparation

The provider project now owns a package README describing configuration and development/evidence limits.
PackageReadmeFile metadata and packed README content were verified using no-build packaging of the already
verified isolated alias assembly; its SHA-256 matches the package assembly. The missing-README pack message is
resolved for future packs. This metadata/documentation check does not replace source or consumer verification
after later provider changes. The preceding alias/text packages remain immutable historical artifacts.
The isolated README probe uses exact EF10.0.14 override dependencies and is only a packaging check; bounded
dependency-range and consumer compatibility evidence comes from the full-source checkpoints above. Independent review passed.

The package consumer now owns `Verify-Package.ps1`, a read-only PowerShell 7 content gate reusable in local/CI
commands. At its introduction, numeric package identity/net10.0/README/ranges passed; wrong-version, missing-README and exact-patch
probes reject as intended. This verifies the artifact structure, separately from compilation, consumers or live
server tests. Those initial checks ran on Windows; subsequent hosted evidence is recorded above.

Git line endings are now governed by `.gitattributes` (a2bbe5f): automatic text uses LF, batch scripts CRLF.
Attribute checks and normalization under core.autocrlf=true/false/input passed; existing index blobs were already LF.

The preceding implementation checkpoint was `d46ae9d`, with package evidence in `062bb84` and a paused handoff
in `20a2f18`. This text slice continues from `20a2f18` on efcore/10.0; dev retains that preceding checkpoint.
No branch synchronization, push, release tag or package publication is part of this change. `master` retains the
initial repository/version design. `efcore/8.0` and `efcore/9.0` have not been created; they are planned to fork
from an EF Core 10 release baseline and then adapt independently.

## Implemented and locally verified

Both MySQL and Oracle modes have provider registration, explicit mode options/model-cache separation,
relational connection and SQL helpers, initial query SQL and tracked insert/update/delete command paths.
The provider references published OceanBaseConnector 0.2.0 through public APIs. EF Core and Relational
dependencies remain bounded to `[10.0.0,11.0.0)`.

Recent mapping checkpoints include:

| Area | Current implementation and evidence |
| --- | --- |
| Integer and Boolean | Native MySQL widths/unsigned declarations, Oracle numeric declarations, exact reader conversion and Oracle Boolean SQL handling. |
| Binary and Guid | Explicit text/binary representations, Guid byte order, length/facet conflicts, converter composition and intercepted query/write/generated-output paths. |
| Floating point | Native binary widths, finite input checks, explicit precision/unsigned declarations, checked narrowing and mode-specific literals. |
| Temporal | Five CLR temporal types, mode-specific transports and exact conversions; query precision is independent of physical column precision. |
| Decimal (`3365ad9`) | Full supported declarations, direct/property facet conflicts, exact coefficient/scale write checks and converter-preserving clones. Query inputs retain value-inferred precision/scale; output-only parameters retain column facets. |
| National text (`d46ae9d`) | Oracle NCHAR/NVARCHAR2 select native connector parameter types. Input Size widens for longer query operands; output/null metadata retains declared Size. Enum-to-string and national Guid conversions have intercepted query/read/write coverage. |
| Text declarations and writes (`57fd44a`) | Complete mode-specific declaration parsing, length/fixed/Unicode/numeric facet conflicts, property and clone validation, LOB key/index rejection, converted DML character bounds and malformed UTF-16 rejection. Queries/original predicates remain independent of column length. |

Physical write validation applies to converted current DML values, independently of query operands and original
values used only in predicates. These implementations are partial acceptance evidence for their checklist rows;
they do not establish complete scalar, query or server support.

## Historical verification for the text slice (57fd44a)

| Check | Result | Evidence boundary |
| --- | --- | --- |
| EF Core 10.0.0 Debug suite | 563 passed: 420 unit, 142 command pipeline, one project consumer | Pipeline commands are intercepted; automatic transactions are disabled. |
| EF Core 10.0.0 Release suite | Same 563 passed, zero failed/skipped | Local source build and tests. |
| EF Core 10.0.14 isolated Release suite | Same 563 passed, zero failed/skipped | Separate package-version override and build artifacts. |
| Standalone package consumer on EF Core 10.0.0 | MySQL and Oracle passed | Uses the packed provider, without source/project references or network execution. |
| Same package on EF Core 10.0.14 | MySQL and Oracle passed | Selected patch-pair compatibility, not every current/future patch. |
| Package contents and dependency graph | Correct ID/version, net10.0 assembly, connector 0.2.0 and both EF ranges | Nuspec/ZIP and package-only consumer assets inspected. |
| Independent review | No blocking finding in declaration/facet/clone/converter/Unicode/national-binding and DML-versus-predicate paths | Read-only review; live server behavior remains unverified. |

The historical text local package is
`artifacts/checkpoint-text-20261110/OceanBase.EntityFrameworkCore.10.0.0-checkpoint.text-20261110.nupkg`.
It is an ignored local build artifact, not a published release. Package creation still reports a missing package
README. The [query/update design](efcore-10-query-update-design.md#package-checkpoint-validation) records
repeatable consumer commands. This checkpoint adds 37 unit and 16 intercepted pipeline cases. The focused
unit run initially failed 29/37 cases before implementation. Removing text write validation reproduced eight
pipeline length failures; restoring it passes the complete suite. Initial NuGet restore failed under the sandbox's
TLS credentials; restore and verification succeeded in the normal user environment without dependency edits.

## Historical scalar-checkpoint work remaining

This older list predates the later implementations above. Use the paused checkpoint and current feature
checklist to decide what still needs code; do not repeat completed work based on this historical list.

Tasks 3, 4 and 5 remain incomplete. Required open work includes:

- Remaining text declarations/profile extensions, server empty-string/NULL and padding behavior, character-set
  byte limits (including BYTE declarations and TEXT tiers), index byte capacity and ordinary string/LOB round trips.
- Remaining numeric/temporal/binary edge cases and live scalar evidence; Oracle decimal alias defaults now have local coverage.
- Composite/relationship/owned/shadow model coverage, generated/default/computed/schema validation and the wider
  query/update acceptance inventory.
- Opened connection ownership and lifecycle, in-flight cancellation, diagnostics/redaction and connector failure paths.
- Database creator and supported EnsureCreated/EnsureDeleted schema behavior; live key generation and transaction
  commit/rollback/caller-owned transaction behavior.
- Relational specification fixtures, a live acceptance fixture, supported-environment records and final release gates.

Migrations, reverse engineering and the other explicitly deferred checklist items retain their existing scope.
Do not turn required gaps into deferred features merely to obtain a passing release checklist.

## Historical scalar-checkpoint next development point

Superseded by "Resume here" at the beginning of this document.

The local string declaration/write slice is implemented; do not repeat it. The character-count check is deliberately
independent of unknown connection/column encodings and cannot prove BYTE/LOB capacity. Existing conservative
mapping limits are a profile, not a comprehensive OceanBase type-support claim.

Continue in this order:

1. Recheck efcore/10.0/worktree state and read the checklist plus this handoff.
2. Continue the documented local query slices after the scalar checkpoint; audit newly exposed scalar gaps as needed.
3. Define per-mode text/byte/session acceptance fixtures using the previously supplied access when the live phase resumes. Cover empty/NULL,
   supplementary characters, trailing spaces, byte capacity and ordinary LOB values; preserve query transport.
4. Finish opened-connection ownership, diagnostics and cancellation, followed by the wider query/update/transaction
   and schema inventory. Repeat suites/package validation after provider changes.

The user has already supplied database addresses. Save local connections with .NET User Secrets under
`ConnectionStrings:OceanBaseMySql` and `ConnectionStrings:OceanBaseOracle`; do not put values in source, shell
history, documentation, logs or test artifacts. Add a stable public UserSecretsId and explicit secret loading to
the future live fixture. Obtain the authorized test-owned database/schema and cleanup scope before live DDL.
This work has not configured a live acceptance fixture or recorded a live provider database test.
