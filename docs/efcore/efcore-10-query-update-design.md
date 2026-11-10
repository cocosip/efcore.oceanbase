# EF Core 10 Query and Update Pipeline

## Scope and evidence

This is the runtime query and tracked-update part of implementation Task 4. Database creation, live server
execution, transactions, complete scalar mappings and package validation remain required work in the
[feature checklist](efcore-10-feature-checklist.md). SQL-generation tests and intercepted commands do not prove
that a server accepts the SQL, generates identity values or commits and rolls back correctly.

## EF Core 10 contracts

The implementation was checked against the EF Core Relational 10.0.0 assembly and these upstream sources:

- [Provider implementation guidance](https://learn.microsoft.com/ef/core/providers/writing-a-provider).
- [Relational service registration](https://github.com/dotnet/efcore/blob/v10.0.0/src/EFCore.Relational/Infrastructure/EntityFrameworkRelationalServicesBuilder.cs).
- [Query SQL generator](https://github.com/dotnet/efcore/blob/v10.0.0/src/EFCore.Relational/Query/QuerySqlGenerator.cs).
- [Update SQL generator](https://github.com/dotnet/efcore/blob/v10.0.0/src/EFCore.Relational/Update/UpdateSqlGenerator.cs).
- [Generated-value propagation](https://github.com/dotnet/efcore/blob/v10.0.0/src/EFCore.Relational/Update/ColumnModification.cs).
- [Relational command execution parameters](https://github.com/dotnet/efcore/blob/v10.0.0/src/EFCore.Relational/Storage/RelationalCommandParameterObject.cs).

| Contract | EF10 lifetime | Implementation responsibility |
| --- | --- | --- |
| `IProviderConventionSetBuilder` | Scoped | Extend `RelationalConventionSetBuilder` with its two dependency objects. |
| `IModelRuntimeInitializer` | Singleton | Use `RelationalModelRuntimeInitializer` to initialize relational model metadata. |
| `IModelCustomizer` | Singleton | Use `RelationalModelCustomizer` for relational model conventions and DbSet table naming. |
| `IModelValidator` | Singleton | Use `RelationalModelValidator` for the relational model boundary. |
| `IQuerySqlGeneratorFactory` | Singleton | Create a generator using `QuerySqlGeneratorDependencies` and the configured mode. |
| `IUpdateSqlGenerator` | Singleton | Use `UpdateSqlGeneratorDependencies` and the configured mode to emit DML. |
| `IModificationCommandBatchFactory` | Scoped | Consume `ModificationCommandBatchFactoryDependencies` and context options. |
| `ModificationCommandBatch` | Per batch | Implement admission, completion, transaction requirements, sync/async execution and value propagation. |

All services are registered through `EntityFrameworkRelationalServicesBuilder.TryAdd` before
`TryAddCoreServices`. The builder maintains its own registration map; mixing direct service-collection mutations
with an already-created builder can cause core defaults to replace provider factories.

## Relational model and queries

Relational conventions and model initialization must run before translating a query. Table names on an otherwise
core-only model are insufficient: query translation requires the table mappings in the relational model.

The query generator retains EF10's relational handling of projection, predicates, joins and aggregates, and changes:

- MySQL paging to `LIMIT <count> OFFSET <offset>`. Offset without a limit uses MySQL's maximum unsigned 64-bit
  limit literal, `18446744073709551615`.
- Oracle table aliases to omit `AS`, and table-free scalar selects to include `FROM DUAL`.
- Oracle paging retains EF10's `OFFSET ... ROWS FETCH NEXT ... ROWS ONLY` syntax.

This does not claim complete function translation, Boolean translation, collection translation or compatibility of
every scalar mapping. Default Oracle strings now use NVARCHAR2(2000), with length/Unicode/fixed facets described
in the [technical baseline](efcore-10-technical-baseline.md). Larger Oracle strings still map to CLOB/NCLOB;
generating an order or group operation over LOB storage does not establish valid execution. Live mapping and query
evidence remains required before advertising support.

## String concatenation local contract

The QRY-01/QRY-06/QRY-11 concatenation slice fixes string addition and the fixed two/three/four-string Concat overloads.
EF10 StringMethodTranslator produces string SqlBinary Add; the base SQL generator emits `+`, which is not this
provider's concatenation syntax. Preserve numeric addition. MySQL emits `CONCAT(left, right)` and Oracle emits
`left || right`, with nested grouping and type mappings preserved. Keep EF's null-as-empty protection for MySQL;
rewriting before nullability processing must not accidentally discard that protection.

Sources: EF Core v10.0.0 StringMethodTranslator, QuerySqlGenerator and SqlNullabilityProcessor, and OceanBase
documentation revision `2a5ae3a23a51729d065cec716508eb26cb064106`:
[MySQL CONCAT](https://github.com/oceanbase/oceanbase-doc/blob/2a5ae3a23a51729d065cec716508eb26cb064106/en-US/700.reference/500.sql-reference/100.sql-syntax/200.common-tenant-of-mysql-mode/400.functions-of-mysql-mode/200.single-row-functions-of-mysql-mode/200.string-functions-of-mysql-mode/700.concat-of-mysql-mode.md),
[Oracle concatenation](https://github.com/oceanbase/oceanbase-doc/blob/2a5ae3a23a51729d065cec716508eb26cb064106/en-US/700.reference/500.sql-reference/100.sql-syntax/300.common-tenant-of-oracle-mode/400.operator-of-oracle-mode/400.concatenate-operators-of-oracle-mode.md).
These sources define generated SQL, not live acceptance.

Oracle treats zero-length strings as NULL; concatenating two NULL strings yields NULL, while a non-null operand
survives concatenation with NULL. Exact CLR empty-string result equivalence is not promised. Test nullable
projections and null-sensitive predicates explicitly: do not inherit a proven-wrong non-nullability assumption
from EF's generic Add processing. Oracle string Add is represented as a nullable, non-null-propagating
concatenation function before EF nullability processing, then rendered as `||`; this prevents incorrect operand-OR
null tests. Empty provider string constants/parameters become SQL NULL before that processing. Classify values
after normal value conversion, including enum/Guid/string converters, while preserving original caller values
for binding. Oracle query values using experimental null-converting string converters fail explicitly.
Any unsupported null-sensitive shape must fail clearly rather than silently folding a potentially-null database
result to a constant. Keep the Oracle Boolean condition conversion in place.

Oracle queries with provider-string SQL parameters conservatively disable command-SQL caching: EF's normal cache
key distinguishes null from non-null, while Oracle also needs to distinguish empty strings after conversion.
Model/query-compilation caching is unchanged. Repeated nonempty/empty/null executions must prove that command SQL
is regenerated correctly; no parameter dictionary or caller string is mutated. MySQL retains its existing caches.

Characterize existing string equality/inequality and Equals overloads through real EF SQL. Comparison uses
database collation semantics; StringComparison/culture overloads remain unsupported predicates. Preserve allowed
top-level client projections. Array/object/IEnumerable Concat overloads and the rest of QRY-06 remain separate
matrix work. Local tests cover constants/parameters/nullable columns, nesting, projection/predicate use,
opposite-mode contexts and sync/async intercepted commands; server collation, lengths, LOBs and result semantics
still require the postponed live phase.

## Arithmetic division and remainder local contract

The planned QRY-01 arithmetic slice will render integer division using MySQL `DIV` and Oracle one-argument
`TRUNC(left / right)`, preserving truncation toward zero. Oracle remainder uses `MOD(left, right)`;
MySQL retains `%`. Decimal, Single and Double division retains `/`. Integer classification follows the
translated expression's CLR numeric type, including nullable types and integral promotions, rather than
the Oracle NUMBER store name, which is also used for decimal values. Casts to fractional CLR types must
retain fractional division. Converters are not a general arithmetic transformation: characterize ordinary
value-preserving numeric conversion, without promising arbitrary scaled/text converter arithmetic.

Sources are EF Core v10.0.0 SqlExpressionFactory, SqlBinaryExpression and QuerySqlGenerator, plus OceanBase
documentation revision `2a5ae3a23a51729d065cec716508eb26cb064106`: the per-mode arithmetic-operator pages,
Oracle MOD and numeric TRUNC. Their source audit is recorded in the local query research artifact.
Oracle MOD's negative example and stated dividend-sign rule govern this contract; the page's conflicting
FLOOR formula is not copied into provider code. Server execution remains unverified.

Tests must use mapped columns, since constant-only expressions can be evaluated by EF before translation.
Cover all eight integer CLR types through their actual promoted expression types, signed negative operands,
nullable values, decimal/Single/Double controls, casts, parameters, predicates, projections and nested
division/remainder with explicit grouping. Opposite-mode contexts must retain separate dialects. Sync/async
intercepted materialization proves client generation, binding and reading only, not arithmetic results.

This slice does not promise CLR checked overflow, minimum-integer divided by -1, zero-divisor exceptions,
unsigned subtraction, floating special values or bitwise width/sign equivalence. These require their own
contract and tests; live arithmetic result/predicate tests remain open for both modes.

## Tracked writes

A modification batch contains one row operation. This permits command-level affected-row checks and keeps the
provider independent of the connector's multi-statement and multi-send options. EF's batch executor continues to
control transactions across multiple row operations.

Writes use `IRelationalCommand` and `RelationalCommandParameterObject` with `CommandSource.SaveChanges`. This
retains EF command diagnostics, interceptors, configured timeout and transaction attachment. Input parameters
carry the type mapping and current or original value selected by EF's column modification metadata.

Before executing DML, the batch validates the current value of each `IsWrite` column whose mapping implements
the internal write-validation contract. Integer precision/native-width, decimal precision/scale/unsigned,
floating width/unsigned, temporal precision/
interval leading width, binary column-length and text character-length/Unicode checks run after
value conversion and fail as `DbUpdateException` with the original validation error. This check applies to actual
writes; original key/concurrency values used only in predicates must not inherit a physical write bound.
General parameter/literal generation also serves queries, where an arithmetic operand, CASE branch or comparand
can exceed a column's capacity. Query parameter binding retains transport checks such as Oracle empty binary input.
Temporal inputs/literals use lossless transport precision and Oracle interval leading width, while output-only
parameters retain declared column metadata. Oracle DateTime DATE comparands use timestamp transport;
DateOnly retains midnight DATE semantics. See the [temporal contract](efcore-10-technical-baseline.md#temporal-mapping-contract).
Decimal input and InputOutput parameters leave precision/scale unset for connector inference; output-only decimal
parameters retain physical facets. Converted current writes reject coefficient overflow and any nonzero remainder
at the declared scale before execution. Intercepted decimal tests cover fractional and large query comparands,
CASE operands, nullable reads, converted writes and failure before command interception in both modes sync/async.
National text tests exercise native Oracle NChar/NVarchar2 input binding, larger query operands and enum-to-string/
Guid converters through real EF query/materialization/write paths. Ordinary MySQL/Oracle text retains generic
binding. Output-only declared Size remains separate from the actual input transport length; live character-set
capacity and server text/LOB behavior remain unverified.

| Mode | Generated-value path | Transaction requirement |
| --- | --- | --- |
| MySQL | Execute DML, then select generated columns by the mapped primary key on the same connection. A generated insert identity uses `LAST_INSERT_ID()`. | The batch requires a transaction whenever a follow-up read is needed. |
| Oracle | Execute DML with `RETURNING <columns> INTO :ob_out_0, ...` and typed output parameters. | Returning outputs are part of the single DML statement. |

MySQL readback requires a mapped key with at most one generated identity column; unsupported shapes fail while
building the batch, before executing DML. The currently supported generated identity types are int/long (or
converters with those provider types), without configured default values, default SQL or computed expressions.
Server-generated string keys and nonidentity numeric defaults are rejected; they cannot use LAST_INSERT_ID.
Default-only inserts use `INSERT INTO ... () VALUES ()` in MySQL and
`INSERT INTO ... (key) VALUES (DEFAULT)` in Oracle. Schema creation must establish the appropriate identity or
default mechanism; these commands do not create it.

Oracle output values are converted to the mapping's provider CLR type before applying a configured converter and
assigning through `IColumnModification.Value`, which publishes store-generated values through EF's update entry.
MySQL readback uses `IReadOnlyModificationCommand.PropagateResults`.

Update and delete conditions include the original key and concurrency-token values selected by EF. A result other
than exactly one affected row raises `DbUpdateConcurrencyException` with the affected entries. Provider exceptions
are wrapped in `DbUpdateException`; cancellation remains `OperationCanceledException`. An already-cancelled async
save fails before executing the batch's command.

Both provider-created and caller-owned connections are checked for compatibility-mode conflicts. A closed connection
without `ExpectedMode` receives the configured provider mode before opening. An already-open connection must agree
with that mode. MySQL requires `UseAffectedRows=false`: EF concurrency checks count matched rows, including a match
whose stored values do not change. A conflicting option is rejected before execution rather than silently changed.
Validation happens at construction, creation, replacement and opening boundaries; the DbConnection getter stays
safe for EF's disposal/reset paths after rejection. Locally tested ownership covers closed supplied and created
connections, including sync/async disposal; opened-connection ownership still requires live evidence.

## Verification and remaining gates

`SqlGeneratorTests` verifies both-mode quoting and paging, offset without a limit, joins, grouped aggregate SQL
and inserts without SQL Server `OUTPUT`.

`BasicRelationalTests` executes the real EF query and SaveChanges pipelines with connector commands. Test-only
interceptors suppress network I/O and provide controlled result readers, output parameters and affected-row counts.
The tests verify scalar materialization, input binding, generated-key propagation, insert/update/delete,
default-only inserts, concurrency conflicts and cancellation. They deliberately disable automatic transactions;
they provide no transaction or live server evidence.
Unsupported MySQL string/default-expression keys have sync/async regression tests asserting that rejection occurs
before any command is executed.

Still required for Task 4 and release:

- Implement database creation/existence and the documented `EnsureCreated`/`EnsureDeleted` profile.
- Verify generated keys against actual identity/default columns in both modes.
- Verify MySQL matched-row behavior on a live server; connector option conflicts are covered locally.
- Complete string, decimal, temporal, Boolean, binary and Guid mappings before advertising broad query support.
- Run real transaction commit/rollback and caller-owned transaction tests, including failure after DML/readback.
- Run the two-mode live functional matrix and record connector/server versions and transport settings.
- Extend specification tests, expand the packed consumer beyond smoke checks and validate EF10 patch compatibility.

## Package checkpoint validation

### Repeatable package content gate

Run the read-only gate after packing, before the isolated consumer checks:

```powershell
pwsh -NoProfile -File tests/OceanBase.EntityFrameworkCore.PackageConsumer/Verify-Package.ps1 `
    -PackagePath artifacts/checkpoint-numeric-20261110/OceanBase.EntityFrameworkCore.10.0.0-checkpoint.numeric-20261110.nupkg `
    -ExpectedVersion 10.0.0-checkpoint.numeric-20261110
```

The PowerShell 7 script uses .NET ZIP/XML APIs and checks ID/version, the net10.0 assembly and dependency group,
exact expected dependency ranges, and a nonempty packaged README with matching metadata. It does not build,
restore, execute provider queries or establish server/runtime compatibility. The current numeric package passes;
wrong expected version, the old README-less alias package and the exact-patch README probe fail at their intended
checks. These runs were on Windows; Linux execution remains unverified. Change the path/version for each new pack.

### Current string concatenation checkpoint (2026-11-10)

Debug/Release and isolated EF10.0.14 Release each pass 785 tests (611 unit, 172 intercepted pipeline, two project
consumers). Independent review passed. The same baseline-built `10.0.0-checkpoint.concat-20261110` package passes
both-mode consumers at EF10.0.0/10.0.14, including native concatenation and changing Oracle empty/null/nonempty
fallback captures. The previous numeric package fails the new concat assertion. Verify-Package.ps1 passes
identity/assets/README/ranges; the isolated consumer graph contains only packages. No server function was run.

Repeat the commands below with `10.0.0-checkpoint.concat-20261110` and `artifacts/checkpoint-concat-20261110`.
Evidence: `artifacts/local-development/batch3a/`. See the local contract above for native Oracle empty/null rules,
converter boundaries and the conservative command-SQL cache tradeoff. Wider query and live gates remain open.

### Historical exact integer normalization checkpoint (2026-11-10)

Debug/Release on EF10.0.0 and isolated Release on EF10.0.14 each pass 752 tests (590 unit, 160 intercepted
pipeline, two project consumers). The same baseline-built `10.0.0-checkpoint.numeric-20261110` nupkg passes both
modes on both selected patches, including rejection of Single/Double fractions in integer reader customization.
The preceding temporal2 package fails that smoke check. Independent review passed. Package identity, net10.0,
README, connector version, bounded EF ranges and package-only consumer dependencies were inspected.

Repeat the commands below with `10.0.0-checkpoint.numeric-20261110` and `artifacts/checkpoint-numeric-20261110`.
Logs: `artifacts/local-development/batch2c/`. Wider queries/release gates and all live acceptance remain open.

### Historical temporal facet/clone checkpoint (2026-11-10)

Debug/Release on EF10.0.0 and isolated Release on EF10.0.14 each pass 706 tests (554 unit, 150 intercepted
pipeline, two project consumers). The same baseline-built `10.0.0-checkpoint.temporal2-20261110` nupkg passes
both-mode consumers on EF10.0.0/10.0.14, including temporal length/declaration/DbType clone rejection. Earlier
alias/unfinished temporal packages fail these regressions. Independent review and one focused fix round passed.
Nuspec identity, net10.0 assembly, included README, connector version, bounded EF ranges and package-only graph
were inspected. The initial temporal probe predates review fixes and is not current evidence.

Repeat the commands below with `10.0.0-checkpoint.temporal2-20261110` and `artifacts/checkpoint-temporal2-20261110`.
Logs: `artifacts/local-development/batch2b/`. Broader release gates and live database acceptance remain open.

### Historical fixed-point alias checkpoint (2026-11-10)

After the configuration/cache coverage and Oracle bare DECIMAL/NUMERIC fix, Debug/Release on EF10.0.0 and isolated
Release on EF10.0.14 each pass 627 tests (475 unit, 150 intercepted pipeline, two project consumers).
The same baseline-built `10.0.0-checkpoint.alias-20261110` nupkg passes both-mode consumers on both selected
patches. Expanded checks verify decimal/integer/Boolean alias defaults, lossless query inputs, output facets and
bare NUMBER independence; the old text package fails the alias regression. Package ID/version, net10.0 asset,
connector 0.2.0, bounded EF ranges and the package-only consumer graph were inspected. Independent review passed.

Repeat the commands below with `10.0.0-checkpoint.alias-20261110` and `artifacts/checkpoint-alias-20261110` as
version/feed. Do not overwrite the historical text package. Logs are under `artifacts/local-development/batch2a/`.
The package README, broader release gates and all live database acceptance remain open.

### Historical text checkpoint (2026-11-10)

Development resumed directly on efcore/10.0 from 20a2f18. The text declaration/write slice passes 563 tests
(420 unit, 142 intercepted pipeline, one project consumer) in Debug/Release on EF10.0.0 and in isolated Release
on EF10.0.14. The same baseline-built `10.0.0-checkpoint.text-20261110` package passes both-mode consumers
on EF10.0.0/10.0.14, including invalid text declaration/UTF-16 rejection and lossless national query binding.
Its ZIP/nuspec contains the net10.0 assembly, connector 0.2.0 and both bounded EF ranges. The consumer assets
have only package dependencies; loaded EF assembly versions are 10.0.0.0/10.0.14.0. No live execution occurs.
The missing package README message remains release preparation work. No publish or release tag was created.

Repeat the package checks from the repository root in PowerShell:

```powershell
$textVersion = '10.0.0-checkpoint.text-20261110'
$textFeed = Join-Path (Get-Location) 'artifacts/checkpoint-text-20261110'
$textConsumer = 'tests/OceanBase.EntityFrameworkCore.PackageConsumer/OceanBase.EntityFrameworkCore.PackageConsumer.csproj'
dotnet pack src/OceanBase.EntityFrameworkCore/OceanBase.EntityFrameworkCore.csproj -c Release `
    "-p:PackageVersion=$textVersion" -o $textFeed -m:1 -nr:false
foreach ($textPatch in @('10.0.0', '10.0.14')) {
    dotnet restore $textConsumer "-p:OceanBaseProviderVersion=$textVersion" `
        "-p:OceanBaseConsumerEfCoreVersion=$textPatch" --source $textFeed `
        --source (Join-Path $env:USERPROFILE '.nuget/packages') `
        --packages (Join-Path $textFeed 'consumer-packages') --force-evaluate --disable-parallel
    dotnet run --project $textConsumer -c Release --no-restore `
        "-p:OceanBaseProviderVersion=$textVersion" "-p:OceanBaseConsumerEfCoreVersion=$textPatch" -m:1 -nr:false
}
```

The recorded default consumer omitted the explicit EF version and resolved the minimum 10.0.0; the selected
later-patch run requested 10.0.14. Do not reuse this package version for later source changes: pack a unique
checkpoint version and repeat the evidence. Character-set byte budgets, live text/LOB semantics, transactions,
specification tests and final release gates remain open.

### Historical national-text checkpoint

Implementation revision `d46ae9d` was fast-forwarded from dev into efcore/10.0 after Debug and Release each passed
510 tests (383 unit, 126 intercepted pipeline and one project-reference consumer). A local prerelease package was
built on efcore/10.0; no release tag or NuGet publication was created. Branch synchronization is recorded in the
[progress handoff](efcore-10-progress.md). The earlier `2a02bc8`/95-test, `8664692`/145-test,
`188c25d`/195-test, `fbb33ea`/257-test, `3ab2345`/317-test, `9c78329`/366-test and `c84ad9d`/432-test and `f645608`/450-test and `3365ad9`/498-test packages
remain historical evidence; use the current revision below with the expanded consumer fixture.

The package `OceanBase.EntityFrameworkCore.10.0.0-checkpoint.d46ae9d.nupkg` contains net10.0 assets and its nuspec
declares `[10.0.0,11.0.0)` for both EF Core and EF Core Relational. These were checked by opening the ZIP/nuspec
and asserting the dependency IDs, ranges and assembly path. The packaging tool reported a missing package README;
package presentation remains release preparation work.

The standalone [package consumer](../../tests/OceanBase.EntityFrameworkCore.PackageConsumer/Program.cs) references
only the packed provider by default, with no ProjectReference or direct connector/EF dependencies. Its executable asserts
registration, Guid, five-type temporal, bounded/unbounded binary and explicit integer/Boolean/Guid/native floating/decimal storage, Boolean
projection and mode-specific paged query generation in both modes. The expanded national-text consumer failed on the preceding
3365ad9 package before passing with this revision: Oracle NVARCHAR2 selects native national binding and longer
query operands widen transport Size independently of column length. Decimal parameters retain value inference
independently of physical precision/scale. Temporal comparands retain fractional precision and Oracle interval
day capacity; native floating literals and captured Guid operands retain their tested representations. It deliberately does
not connect to a database. It stays outside the normal solution because it requires an already-built local
provider package. Central versions are disabled only in this isolated fixture so repository EF references cannot
mask package dependency errors. Setting `OceanBaseConsumerEfCoreVersion` adds explicit EF Core and Relational
references to simulate a consumer requesting a later patch; it does not rebuild or reference provider source.

| Validation | EF Core / Relational | Result and evidence boundary |
| --- | --- | --- |
| Provider build and Debug/Release suites | 10.0.0 | 510 passed per configuration; unit/intercepted/project consumer only. |
| Default standalone consumer | 10.0.0 | Both modes pass; provider package built against 10.0.0. |
| Consumer explicitly upgrades EF packages | 10.0.14 | Both modes pass with the same provider nupkg/assembly; runtime reports EF 10.0.14.0. |
| Source build and full Release suite | 10.0.14 | 510 passed; isolated central-version override and artifact paths, not the baseline-compiled package. |
| Package range boundary | [10.0.0,11.0.0) | Nuspec inspected; NuGet.Versioning parser accepts 10.0.0/10.0.14 and rejects 11.0.0. No EF11 runtime test claimed. |

Both `project.assets.json` and the consumer's loaded EF assembly version were inspected. These checks establish
the selected patch pair at this source revision; they do not prove every intermediate/future patch or any live
server behavior. Re-run the matrix after provider changes before release.

To repeat the checkpoint consumer verification from the repository root in PowerShell:

```powershell
$providerVersion = '10.0.0-checkpoint.d46ae9d'
$consumerProject = 'tests/OceanBase.EntityFrameworkCore.PackageConsumer/OceanBase.EntityFrameworkCore.PackageConsumer.csproj'
$packageFeed = Join-Path (Get-Location) 'artifacts/checkpoint-d46ae9d'
$consumerPackages = Join-Path $packageFeed 'consumer-packages'
dotnet restore $consumerProject "-p:OceanBaseProviderVersion=$providerVersion" `
    --source $packageFeed --source (Join-Path $env:USERPROFILE '.nuget/packages') `
    --packages $consumerPackages --force-evaluate --disable-parallel
dotnet run --project $consumerProject -c Release --no-restore `
    "-p:OceanBaseProviderVersion=$providerVersion" -m:1 -nr:false

# Consumer patch upgrade: reuse the same provider package, with explicit EF references.
dotnet restore $consumerProject "-p:OceanBaseProviderVersion=$providerVersion" `
    -p:OceanBaseConsumerEfCoreVersion=10.0.14 --source $packageFeed `
    --source (Join-Path $env:USERPROFILE '.nuget/packages') --packages $consumerPackages `
    --force-evaluate --disable-parallel
dotnet run --project $consumerProject -c Release --no-restore `
    "-p:OceanBaseProviderVersion=$providerVersion" -p:OceanBaseConsumerEfCoreVersion=10.0.14 -m:1 -nr:false
```

The validation used the local package feed and existing cached dependency packages as feeds, with a separate
consumer package directory (the recorded run reused the existing checkpoint-8664692 cache). It proves package restore/consumption, not remote NuGet availability. For another
revision, first pack a unique local prerelease version on efcore/10.0 and update the version/feed values.

To compile/test provider source against the selected later patch without changing repository package ranges:

```powershell
$patchDirectory = Join-Path (Get-Location) 'artifacts/compatibility-10.0.14'
New-Item -ItemType Directory -Path $patchDirectory -Force | Out-Null
[xml]$central = Get-Content Directory.Packages.props -Raw
foreach ($node in $central.Project.ItemGroup.PackageVersion) {
    if ($node.Include -in @('Microsoft.EntityFrameworkCore', 'Microsoft.EntityFrameworkCore.Relational')) {
        $node.Version = '[10.0.14]'
    }
}
$patchProps = Join-Path $patchDirectory 'Directory.Packages.props'
$central.Save($patchProps)
$patchArtifacts = Join-Path $patchDirectory 'build'
dotnet test OceanBase.EntityFrameworkCore.slnx -c Release --artifacts-path $patchArtifacts `
    "-p:DirectoryPackagesPropsPath=$patchProps" "-p:RestorePackagesPath=$consumerPackages" `
    "-p:RestoreSources=$(Join-Path $env:USERPROFILE '.nuget/packages')" -m:1 -nr:false
```

The EF11 exclusion is a package-resolution boundary, not a tested EF11 adaptation. Each branch must rebuild and
review its own EF-major contract. Full Task 5 acceptance still requires specification tests, live evidence,
release-revision package gates and support records.
