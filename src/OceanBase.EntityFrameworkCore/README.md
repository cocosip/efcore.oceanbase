# Sharp.OceanBase.EntityFrameworkCore

Entity Framework Core 8 provider for OceanBase MySQL and Oracle compatibility modes, using the public
OceanBaseConnector 0.4.0 API. The package targets `net8.0` and accepts EF Core 8 patch versions in
`[8.0.0,9.0.0)`. It requires `Microsoft.Extensions.Caching.Memory` 8.0.1 or later.

See the EF Core 8 feature matrix and release checklist in the repository for implementation status,
known boundaries and tested profiles.

- [Configuration](#configuration)
- [Scalar mappings](#scalar-mappings)
- [Query translations](#query-translations)
- [Retries and transactions](#retries-and-transactions)
- [Graph loading and compiled models](#graph-loading-and-compiled-models)
- [Reverse engineering](#reverse-engineering)
- [Collection and scalar query details](#collection-and-scalar-query-details)
- [Schema creation and migrations](#schema-creation-and-migrations)
- [Inheritance and split entities](#inheritance-and-split-entities)
- [Seeding and namespace ownership](#seeding-and-namespace-ownership)
- [Bulk updates and deletes](#bulk-updates-and-deletes)

## Configuration

Select the tenant's compatibility mode explicitly when configuring a context:

```csharp
using Microsoft.EntityFrameworkCore;
using OceanBase.EntityFrameworkCore;

optionsBuilder.UseOceanBase(connectionString, OceanBaseCompatibilityMode.MySql);
// For an Oracle-mode tenant, select OceanBaseCompatibilityMode.Oracle.
```

Load the connection string from application configuration or a secret store. The selected mode must match the
tenant. Driver protocol, authentication, connection pooling and transport settings belong to OceanBaseConnector.

For a supplied `OceanBaseConnection`, the provider validates configured and detected tenant modes without
rewriting its connection string. This preserves credentials hidden after a prior open and fixed data-source
configuration. Provider-created connections receive the selected `ExpectedMode` before opening.

Oracle generated-value writes use `RETURNING INTO`. Explicitly configure the Connector connection string
with `Use Oracle Prepare Execute=true` for those writes; the Connector default is disabled. The provider
does not silently enable that protocol option. Ordinary input-only writes do not need this option.

Mapped write procedures can return generated properties or the integer affected-row count in a
single result set with at most one row. In MySQL mode, return it from the procedure. In Oracle mode,
enable `Use Oracle Prepare Execute=true` and declare a final `OUT SYS_REFCURSOR` argument in the
procedure DDL, omitting that argument from the EF mapping.

MySQL text commands adapt named inputs to positional binds. Repeated caller-owned
`OceanBaseParameter` inputs use the Connector's public `Clone()` API and retain their type and
facets. A repeated or shared stream is rejected before execution; use an independent stream and
parameter instance per position. Text-command parameters are Input-only. Stored procedures use
their separate IN/OUT/INOUT binding path.

Local and bounded OceanBase 4.4.2.2 native profiles cover queries, mappings, writes, transactions,
schema tooling and package consumption in both modes. The release target is Windows x64 with ordinary
direct connections and TLS Disabled. Consult the release checklist for revision-specific evidence and
remaining gates. Linux, TLS and HA deployments require separate acceptance.

## Scalar mappings

Numeric acceptance includes all eight integer widths, nullable Boolean logic, exact decimal
storage/facets/converters and native Single boundaries in both modes. Connector 0.4.0 enables
Oracle binary Double parameter transport. The focused 4.4.2.2 regression covers CLR extrema,
subnormals, `±1E-131`, ordinary values and converter-backed properties alongside D-suffixed
literal controls. Oracle CLR Double defaults to `BINARY_DOUBLE`; explicit
`HasColumnType("BINARY_DOUBLE")` uses the same carrier. Native binary floating types reject
decimal precision facets. Server floating-point semantics still apply to arithmetic and signed zero.

For a restricted decimal business domain, explicitly use a decimal property or a deliberate
`HasConversion<decimal>().HasPrecision(18, 6)` mapping. The latter is tested for representable
values, with excess-scale writes rejected before DML. Raw SQL into `NUMBER(18,6)` rounds
`123.456789123` to `123.456789`, as expected from the declared scale. Double-to-decimal conversion
does not preserve the full Double range/precision and can underflow or overflow. Direct CLR
Double-to-NUMBER mapping and precision facets on native Oracle binary floating types remain
unsupported by this provider.

Numeric queries have a finite-domain native profile for Abs/Sign/Floor/Ceiling/Truncate,
the implemented Math/MathF transcendental overloads, scalar extrema, Clamp/CopySign,
Round, CASE and coalesce. Oracle binary-floating Sign explicitly returns zero for zero.
On the tested server MySQL lacks SINH/COSH/TANH/ASINH/ATANH and Oracle lacks ASINH/ATANH;
the provider composes supported EXP/LN/SQRT/arithmetic with small/large-input branches.
Floating transcendental results use native precision and are approximate; this is not
a bit-for-bit CLR, signed-zero or non-finite/domain-error contract. MySQL Single results
normalize to FLOAT and then widen to DOUBLE for transport, avoiding FLOAT text formatting
that otherwise loses significant digits. The Single reader/result semantics are retained.

Round supports decimal digits 0..28, Double 0..15 and Single 0..6, all five
MidpointRounding rules, and current captured digits/rules. Invalid options reject before
execution; row-dependent options and unlisted signatures do not translate in predicates.
Nullable same-carrier converters retain their stored-space arithmetic and apply the
inverse once to results; they need not be equivalent to arithmetic in model space.
Connector 0.4.0 supports the wide MySQL result metadata produced by scalar LEAST/GREATEST
over default `DECIMAL(65,28)` operands. EF reads representable results through its existing
CLR decimal mapping. Values outside CLR decimal's exact range raise a conversion error.
Additional temporal signatures, wider converter/declaration combinations and other deployments remain
outside the tested profiles.

Guid defaults use canonical 36-character text in MySQL and RAW(16) in Oracle. Binary Guid mappings
use EF's `GuidToBytesConverter` / .NET `Guid.ToByteArray()` mixed-endian layout: the UUID
`00112233-4455-6677-8899-aabbccddeeff` stores `33221100554477668899AABBCCDDEEFF`, rather than
RFC/network byte order. Select compatible text or 16-byte binary declarations when sharing data.
Oracle BLOB byte arrays preserve NULL and non-null empty values separately; empty BLOB literals
use `EMPTY_BLOB()`. Oracle RAW rejects empty arrays because it cannot preserve that distinction.
The tested MySQL 4.4.2.2 target rejects BLOB/LONGBLOB defaults with 1067; bounded VARBINARY defaults
and BLOB/LONGBLOB seed/parameter writes have separate acceptance. Other versions and LOB APIs
remain outside this storage profile.

Temporal storage has narrow live coverage for all five CLR types, NULLs, facets,
converters, defaults/HasData/generated readback and tracked updates on OceanBase 4.4.2.2.
Unzoned DateTime requires `DateTimeKind.Unspecified`. Oracle DateTimeOffset retains numeric
offsets; MySQL stores UTC in DATETIME and reads a zero-offset value. Fractional write values
must fit the declared precision exactly; MySQL supports microseconds, Oracle CLR values
retain 100ns ticks. Query comparands and original concurrency values keep transport precision
independently of column write facets. Oracle exact native results with subtick fractions,
out-of-range durations or invalid clock/date-only values reject rather than silently truncate.

Explicit Oracle `TIMESTAMP[(p)] WITH LOCAL TIME ZONE` columns map to `DateTimeKind.Unspecified`
in the current session time zone. Writes interpret unzoned values in that session; reading from a
session with a different offset changes the wall clock. The mapping preserves precision and the
store declaration in migrations, generated models and reverse engineering; native fractions finer
than 100ns reject instead of truncating. Set the session time zone consistently when wall-clock
identity matters. This storage profile supports equality/ordering and excludes temporal member
and arithmetic translations. It does not preserve a named zone, its DST rules or the original offset.
`DateTimeOffset` continues to use `TIMESTAMP WITH TIME ZONE` and numeric offsets.

Explicit MySQL `TIMESTAMP` columns map to `DateTimeKind.Unspecified` with fractional precision 0–6.
OceanBase applies the session time zone on writes and reads and enforces its native range. Set the same
session time zone for applications that need identical wall-clock values across connections. The default
DateTime type remains `DATETIME(6)`. The DateTimeOffset mapping uses UTC DATETIME. Temporal LINQ member
and method coverage on TIMESTAMP is limited to the signatures supported by the query translators.

MySQL TIME accepts signed durations through exactly `838:59:59`, with fractional seconds
below that endpoint. The provider rejects larger magnitudes for parameters, literals and
writes: on the tested server, direct out-of-range TIME parameters such as `838:59:59.000001`
store `838:59:59.000000`, while equivalent typed literals return 1525. The provider previously
allowed those extra fractions; the narrower validation prevents that loss. Raw SQL using
caller-owned parameters bypasses mapping validation. Numeric-offset session tests do not
establish named timezone/DST or other deployments.

## Query translations

### Temporal methods

The temporal query profile additionally covers implemented calendar/fractional members,
Date/TimeOfDay/Ticks/DayNumber, signed TimeSpan components and Double totals, DateOnly
AddDays/AddMonths/AddYears and DateTime AddMonths/AddYears. DateTime.Add(TimeSpan) and
Subtract(TimeSpan) translate for unzoned timestamps, duration columns, constants and
parameters, preserving NULLs. Results promote to DATETIME(6)/TIMESTAMP(7), including
Oracle DATE inputs. MySQL duration inputs retain the existing TIME range and microsecond
validation; Oracle retains 100ns values. Out-of-range results follow server error/NULL
semantics rather than reproducing CLR exceptions per row. DateTime.AddDays/AddHours/AddMinutes/
AddSeconds/AddMilliseconds/AddMicroseconds(double) translate constants, refreshed parameters and columns in both modes without
the MySQL TIME duration range. Whole/fractional units truncate separately to CLR ticks;
integer ticks are combined before timestamp arithmetic. Offsets outside the CLR unit range,
or offsets containing sub-microsecond ticks in MySQL, return SQL NULL instead of rounding.
Non-finite parameters/literals are rejected by the numeric mapping. Results retain the same
DATETIME(6)/TIMESTAMP(7) promotion and converter ownership as duration arithmetic.
DateTime.AddTicks(long) uses exact signed integer offsets without conversion to Double, including
values above 2^53. It supports constants, refreshed parameters, columns and NULLs with the same
result promotion and MySQL precision policy. Offsets outside +/-DateTime.MaxValue.Ticks return
SQL NULL, including Int64.MinValue/MaxValue; calendar result overflow follows server error/NULL behavior.
DateTime.Subtract(DateTime) returns a TimeSpan carried as exact signed integer ticks in queries,
so results can span the full supported DateTime range without using MySQL TIME storage. NULLs,
duration components/totals, scalar comparisons, Compare/CompareTo, CASE/COALESCE with ordinary
duration columns and composition into DateTime.Add/Subtract(TimeSpan) are supported. Scalar date
operands adopt the other operand's converter; two date expressions must have matching converters,
and their difference operates in stored calendar space. Custom converted native durations cannot
be combined with tick-carried results because their inverse is not generally expressible in SQL.
Binary TimeSpan Add/Subtract on date differences, mixed native/tick duration set projections and
mixed-carrier membership are explicitly rejected. Stored TIME/INTERVAL mappings are unchanged.
TimeOnly.Add(TimeSpan), AddHours(double) and AddMinutes(double) wrap at 24 hours for signed
offsets, constants, refreshed parameters, native columns and NULLs. Scalar TimeSpan offsets use
exact ticks across the full TimeSpan range; native duration columns retain their storage limits,
and custom converted native duration columns are rejected. Date-difference tick results can be
used as offsets. Numeric additions truncate the entire binary product to Int64 with saturation
before wrapping. DateTime truncates the whole and fractional components separately.
Clock results promote to TIME(6)/INTERVAL DAY TO SECOND(7), preserve contextual clock converters
and apply the inverse converter once. MySQL returns NULL for results finer than a microsecond;
Oracle retains 100ns ticks. Non-finite numeric inputs remain rejected by the numeric mapping.
DateOnly.FromDateTime and TimeOnly.FromDateTime extract the stored date/clock; ToTimeSpan returns
the clock as a native nonnegative duration. DateOnly.ToDateTime(TimeOnly) combines date and clock
with promoted timestamp precision and Kind.Unspecified; the explicit constant Unspecified overload
is also supported, while UTC/Local/dynamic Kind reject. TimeOnly.FromTimeSpan accepts only [0,24h),
returning NULL for invalid SQL values or MySQL sub-microsecond ticks, without wrapping. Keep one argument dependent on the row when a call must execute in SQL. These type-changing methods use stored calendar space and
the result's own built-in mapping, without applying an inverse converter for the input type.
TimeOnly.IsBetween uses inclusive start/exclusive end, supports midnight wrapping, and treats equal
endpoints as empty. NULL clocks/bounds return NULL; scalar bounds adopt the receiver's clock converter
with promoted precision, while differently converted column bounds reject. See the feature inventory
for the exact G-13 signatures; the methods do not imply broader parsing/formatting support.
Oracle calendar arithmetic
requires a Gregorian session; month ends clamp without losing timestamp fractions.
Oracle DateTimeOffset components use the stored offset's local wall clock; UTC members
use UTC extraction. MySQL components use the normalized UTC storage, and offset-local
Offset/TotalOffsetMinutes/Ticks/DateTime members reject in predicates. SQL DateTime
results, including UTC-valued projections and clocks, have Kind.Unspecified; callers
must assign a Kind explicitly when constructing zoned application values. Same-carrier
converter expressions operate in stored space; temporal-valued mapped results apply the
inverse converter once. Other temporal methods and wider coercion/calendar combinations
are not implied by this profile. DateTimeOffset.Subtract(DateTimeOffset) returns the signed
difference between stored UTC instants as a tick-backed TimeSpan, with SQL NULL propagation
and the precision of the stored operands. Scalar operands use the other operand's converter
at full temporal precision. Operands with mismatched converters are rejected.
DateTimeOffset.AddTicks(long), Add(TimeSpan) and Subtract(TimeSpan) preserve the stored numeric
offset on Oracle and return UTC on MySQL. Out-of-range results and MySQL adjustments finer than
one microsecond return SQL NULL. Duration operands use exact scalar ticks, ordinary native
TIME or INTERVAL columns, and tick-backed query results. Custom native duration converters reject.
DateTimeOffset AddDays/AddHours/AddMinutes/AddSeconds/AddMilliseconds/AddMicroseconds use the
same guarded whole- and fractional-tick truncation as DateTime. AddMonths and AddYears clamp
the day and preserve fractional seconds on the stored calendar. That calendar is the numeric
offset's wall clock in Oracle mode and UTC in MySQL mode.

### Numeric functions and ordering

`Math.Acosh(double)` and `MathF.Acosh(float)` translate in both modes. They calculate in Double
precision before applying the existing result mapping, including Single normalization. Finite input
at or above 1 returns a numeric result. SQL NULL and finite input below 1 return SQL NULL. Use a
nullable projection when a row can contain an out-of-domain value. Numeric mapping rejects nonfinite
parameters and literals.

Ordinary ordered queries have native coverage for OrderBy/ThenBy in both directions,
Distinct, nonnegative Skip/Take, offset-only and empty pages. Use a final unique key
when paging duplicate sort values. Native NULL ordering follows the dialect: the
tested MySQL mode sorts NULL first ascending and Oracle sorts NULL last ascending;
descending reverses those positions. Use explicit null-rank/coalesce expressions
when portable placement is required; this does not configure string collation.

### Strings

String translations support ordinary equality, two/three/four-string concat,
single-string and char Contains/StartsWith/EndsWith/IndexOf, Length, both integer Substring/Remove overloads,
ordinary string and char Replace, Insert(int,string), PadLeft/PadRight with optional char padding,
parameterless ToLower/ToUpper and Trim/TrimStart/TrimEnd with no argument, char or char[]. Length and
substring bounds count native characters rather than CLR UTF-16 units. Substring
uses native negative/out-of-range/zero-length rules; parameterless trim removes ordinary spaces,
and case conversion follows the database. StringComparison and culture/Invariant overloads are not translated in
predicates; EF may evaluate permitted final client projections.

Contains/StartsWith/EndsWith treat percent, underscore, quotes and backslashes as text. NULL source or
needle is false; a known empty needle matches a non-NULL source. Oracle computed
empty scalar text becomes NULL. Suffixes require enough source characters and
use native equality; Oracle LOB suffixes use a final-position search because
ordinary LOB equality is unavailable. Comparison behavior can differ by function:
the tested MySQL utf8mb4_unicode_ci profile equates NBSP/em-space with ordinary
space in equality, while INSTR distinguishes them. Fixed padding follows storage
semantics. Oracle scalar all-space trim becomes NULL, but ordinary CLOB trim and
empty substrings can return non-NULL empty LOBs. NCLOB remains unaccepted on the
tested 4.4.2.2 target (CREATE returns 902). Value converters operate on stored text
and apply their inverse once to string results; arbitrary converter equivalence
and string-to-binary function translation are not supported.

Char search scalars convert to text with the receiver's mapping and converter;
text-backed char columns retain their own mappings, including nullable column
needles. Numeric/binary-backed chars are not translated. Text validation rejects
lone UTF-16 surrogates rather than searching half of a supplementary character.
Each char overload follows the native collation and NULL policy of its string counterpart.

IndexOf supports string and char needles, optionally with an int start or with
int start and count. It searches a window of native characters in stored text and
returns a zero-based position or -1. Start must be in [0,length] and count in
[0,length-start]. NULL inputs and invalid ranges return SQL NULL, which can be
projected as int?. A known empty scalar needle returns the valid start, including
Oracle literals and captured parameters. SQL column and computed needles follow
native storage semantics. Matching uses the database collation.

Explicit trim repeatedly removes any character in the supplied set at the requested
ends, using stored text and case-sensitive character matching. Null/empty char[]
arguments select CLR whitespace, including tabs, newlines and Unicode spaces;
parameterless trim keeps its ordinary-space contract. Captured arrays refresh at
each execution, including in-place mutations; constant and inline char arrays and
ordinary text-backed char columns are supported, and inline arguments retain literal translation.
A NULL char column propagates
NULL, including within an inline array. Surrogate code units and custom/multichar
or non-text char converters reject. Trim characters have plain text mappings,
independent of the receiver's converter; string results use variable text mappings
with the receiver's inverse converter once. Oracle scalar empty results are NULL;
native CLOB trimming can retain an empty LOB. MySQL uses strictly anchored,
case-sensitive escaped regex, so punctuation is literal and final newlines do not
weaken the end anchor. This does not enable mapped char[] collection properties.

Remove uses native character counts in stored text. Start must be in [0,length]
and count in [0,length-start]; invalid or NULL SQL bounds/source return NULL.
Results use variable text mappings, preserving national/LOB families and applying
the receiver's inverse converter once. Oracle guarded empty results are NULL,
including LOBs; bare Substring has its own native empty-value contract.

Replace searches for literal tokens in stored text with native matching, which is
case-sensitive in the tested profiles. A NULL source or NULL/empty old token
returns SQL NULL. In Replace(string,string), NULL or empty replacement text deletes
matches. In Replace(char,char), either NULL SQL operand returns NULL. Scalar
tokens use plain text mappings independent of the receiver converter. Text-backed
char columns use the ordinary char-to-string converter. Results can grow beyond
the source value and use variable national/LOB mappings with the receiver's
inverse converter applied once. Oracle scalar empty results become NULL.

Insert splits stored text at a native character boundary in [0,length] and
inserts the supplied text there. NULL inputs and invalid indices return SQL NULL.
Known empty scalar values leave the source intact, including Oracle literals
and captured parameters. SQL columns and computed operands follow native storage
semantics. Inserted scalars use plain contextual mappings. The result preserves
variable national or LOB text and applies the receiver inverse once.

PadLeft and PadRight count native characters and leave source text intact when
the requested width is no larger than its length. Negative widths and NULL
inputs return SQL NULL. The one-argument overloads pad with ordinary space.
Explicit characters use plain contextual text mappings and support ordinary
text-backed char columns. Oracle builds the exact number of padding characters
independently of display width. Results use variable national or LOB mappings
and apply the receiver inverse once. Large SQL results follow the server's
text capacity for the source family.

### Cardinality, joins and defaults

First/FirstOrDefault/Single/SingleOrDefault and ordinary predicate overloads have
sync/async native coverage for empty, one and multiple rows with entity, scalar,
nullable, converter and reference projections. Comparer overloads are excluded.
Ordinary duplicate-entity queries separately verify tracked,
untracked and identity-resolution behavior. The ordinary graph profile below has separate
native evidence; broader official runtime families retain their own acceptance requirements.

Ordinary inner/left/cross joins, correlated scalar/collection subqueries and per-parent
paging, Any/All, nullable Contains and Union/Concat/Intersect/Except have sync/async
native coverage in both modes, including empty inputs, current captures and string
converters. Scalar-key joins use SQL equality; default anonymous composite keys retain
EF's nullable equality compensation. With UseRelationalNulls, membership can be UNKNOWN:
project it as bool? when needed; a required bool result can throw on NULL. Collection
set operations preserve NULL equality and Concat duplicates. Comparer overloads are
excluded. This bounded profile does not establish arbitrary projections, collation,
full LOB comparison or other server versions.

Parameterless `DefaultIfEmpty` has local coverage for ordinary scalar/converter defaults, anonymous
objects, DTOs, constructed mapped CLR types and boxed scalar projections, including per-parent paging.
A missing source returns null for reference projections; a present row with NULL members remains an
object. Required reads of nullable scalar expressions still throw for present NULL, while missing rows
receive their CLR default. EF retains entity tracking.

`Queryable.DefaultIfEmpty`, `FirstOrDefault`, and `SingleOrDefault` support explicit defaults for
mapped scalar projections in both modes. A present row whose value is NULL retains that NULL.
Captured defaults in composed queries are evaluated for the current execution. An ordered
`FirstOrDefault` applies ordering before taking the first row. Explicit `SingleOrDefault` defaults
are supported only at the query root, where EF checks for multiple rows. Explicit defaults for
entities and structured projections are unsupported.

## Retries and transactions

Enable EF operation retries explicitly for replayable queries and writes:

```csharp
optionsBuilder.UseOceanBase(connectionString, OceanBaseCompatibilityMode.MySql,
    options => options.EnableRetryOnFailure());
```

Defaults are six retries with a maximum 30-second delay for known transient server errors: MySQL 1205/1213,
Oracle 54/60/8177, or SQLSTATE 40001. The optional count/delay/additional server codes apply per context.
Unknown execution outcomes, cancellation, XA, commit-stage errors and failed resource cleanup never replay.
Owned retry transactions explicitly confirm rollback before disposal; uncertain outcomes require application
reconciliation. Put user transactions inside `Database.CreateExecutionStrategy().Execute`/`ExecuteAsync`;
transactions already active before execution reject. Retrying queries buffer results, increasing memory use.
Delegates, callbacks and raw SQL must be replayable; keep automatic SaveChanges transactions enabled.
DDL lifecycle operations and Migrate reject configured or active strategies because DDL commits implicitly;
use a separate context without retries for these operations. Offline script generation remains available.
This local retry policy does not establish native failover or rollback acceptance.

Single-resource `TransactionScope` and `Database.EnlistTransaction` are supported through Connector 0.4.0
in both modes. Enable `TransactionScopeAsyncFlowOption.Enabled` when crossing awaits. EF retains its
connection through transaction completion, preserves caller-open ownership and defers owned native disposal
when a context is disposed or returned to its pool first. A context with an owned connection then acquires a separate connection.
A reset or pooled context using a borrowed connection rejects reuse until the original transaction completes;
it never takes ownership of that connection. Replacing an actively enlisted connection is rejected.
Complete or dispose the transaction to release that native resource. Conflicting `RequiresNew` scopes on
the same connection reject before enlistment. `EnlistTransaction(null)` follows the Connector
rejection contract and preserves the existing enlistment. Distributed/multiple-resource participation is unsupported.
An active ambient or explicitly enlisted transaction prevents partial operation retries. Directly closing
or disposing the native connection still follows the Connector's transaction ownership contract.

## Graph loading and compiled models

Split queries support ordinary collection Includes, sibling/nested collections, filtering and paging:

```csharp
optionsBuilder.UseOceanBase(connectionString, OceanBaseCompatibilityMode.MySql,
    options => options.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
// Use AsSplitQuery() or AsSingleQuery() on an individual LINQ query to override the context default.
```

Both modes buffer split result sets before opening the next native reader. Single-query remains the default;
retry buffering is independent of splitting. Splitting adds memory use and database round trips. Use a fully
unique order with paging; separate commands can observe concurrent changes unless the application selects
an appropriate transaction/isolation level. Cancellation or unknown outcomes are not automatically replayed;
after failed graph materialization, discard or clear partially tracked entities before reusing a context.
On OceanBase 4.4.2.2 ordinary direct connections, both modes have native sync/async
coverage for single/split Include/ThenInclude, composite and alternate keys, optional
references, owned references/collections and shadow owner keys, filtered Include and
root paging, many-to-many shared identities, and all three tracking modes. Tracked
filtered Includes retain pretracked entities through EF fixup; no-tracking queries
return fresh filtered graphs. On EF Core 8, combine tenant and active predicates in one
`HasQueryFilter` expression per entity and use `IgnoreQueryFilters()` to disable all filters.
Captured query values have native row checks and observed parameter binding. These checks
do not prove identical SQL text or binding in every later split command; optional-reference
tenant-component sensitivity with the Tenant filter disabled remains untested.
Concurrent-change isolation, arbitrary model/converter combinations and graph failure recovery remain
outside that graph profile. The following sections describe lazy-loading and compiled-model profiles.

Lazy loading can use EF's injected `ILazyLoader`, or the optional official
`Microsoft.EntityFrameworkCore.Proxies` package at the application's EF Core 8 patch version:

```csharp
optionsBuilder.UseOceanBase(connectionString, OceanBaseCompatibilityMode.MySql)
    .UseLazyLoadingProxies();
```

Proxy entities must be public, unsealed and have virtual navigations. The provider does not require the
Proxies package unless the application chooses it. Both modes have local query/materialization, filter,
relationship fixup and tracked-write coverage. Getter-triggered loads are synchronous even after an async
root query; use explicit navigation `LoadAsync` or `ILazyLoader.LoadAsync` when asynchronous loading is
needed. Untracked entities can still lazy-load while their originating context is alive. Disable it with
`ChangeTracker.LazyLoadingEnabled = false` when needed; disposed or explicitly detached entities cannot
start new loads. Each unloaded navigation can issue another query (N+1).

Materialize the root query before accessing unloaded navigations. OceanBase supports one active native
reader per connection. Avoid concurrent use of a DbContext. The generated-model smoke test enables both
`UseLazyLoadingProxies()` and `UseChangeTrackingProxies()` before `dbcontext optimize` and again at runtime
with `UseModel`. Change-tracking proxy entities also require virtual mapped properties and collection
navigations that notify on changes, such as `ObservableCollection<T>`. The scoped generated-model and both-proxy combination has native graph/loading, fixup, notification and persisted-update coverage in both modes.

Ordinary EF compiled models support both compatibility modes, including provider scalar mappings and
converters with or without mapping hints. Generate a model using the matching mode's design-time context:

```shell
dotnet ef dbcontext optimize --context AppDbContext --output-dir CompiledModels --namespace App.CompiledModels
```

Select the generated model with `UseModel(App.CompiledModels.AppDbContextModel.Instance)` alongside
`UseOceanBase`. Generate and select separate models for MySQL and Oracle; an incompatible mode fails during
model initialization. Regenerate after model/provider changes. EF's compiled-model restrictions still apply,
including unsupported query filters. The public compiled-model mapping helpers and interval JSON reader are
infrastructure used by generated code. Structured JSON property mappings, query precompilation
and NativeAOT profiles are outside the EF8 Provider scope. See C-01 in the EF Core 8 gap matrix.

## Reverse engineering

Database reverse engineering uses the driver's public `GetSchema` collections, including its typed column
declarations and tri-state generation/default/update metadata. Scaffolding connections must specify
`ExpectedMode=MySql` or `ExpectedMode=Oracle`; generated `UseOceanBase` code selects that mode explicitly.
Supported scalar columns, keys, ordinary indexes, relationships and keyless views use EF's model/C# generation.
Unavailable behavior metadata produces design-time warnings requiring configuration review before writes or
schema recreation. Known defaults with unavailable SQL use EF's `HasDefaultValue()` marker; unknown identity
is not guessed from a key or sequence-looking default. This is a usable model scaffold, not a lossless DDL
backup. Unsupported types and index kinds reject explicitly. Oracle identity definitions use
Connector 0.4.0's public `GetOracleIdentityColumns` API. Supported Int32/Int64 columns preserve
`ALWAYS`, `BY DEFAULT` or `BY DEFAULT ON NULL` and the server's sequence option SQL through
the `OceanBase:IdentityGeneration` and `OceanBase:IdentityOptions` property annotations.
This includes identity columns outside the primary key. Changes to these definitions require an
explicit data-preserving migration. Known MySQL BTREE prefix
indexes keep their member lengths, order and uniqueness through scaffolded C# and DDL. The
`OceanBase:IndexPrefixLengths` annotation stores the lengths in
index column order. A zero entry denotes a full column. Sort direction follows the catalog. OceanBase
4.4.2.2 reports `ASC` for the tested prefix columns declared as `DESC`. A generated migration batch
rejects creating a unique prefix index after dropping an index on the same table. Create the
replacement first with a distinct name or use reviewed SQL that preserves uniqueness on failure.

Oracle expression indexes preserve mixed ordinary/expression members, order and uniqueness in
the table annotation `OceanBase:ExpressionIndex:<index-name>`. Its `string[]` value begins with
`"UNIQUE"` or `"NONUNIQUE"`, followed by the native SQL for each member. Generated C#, relational
models, create scripts and migrations preserve this annotation. Catalog expression recovery may
require permission to read index DDL. The tested Oracle 4.4.2.2 server rejects descending index DDL.
Replacing a unique expression index under the same name requires an explicit migration. A generated
replacement with a distinct name is created before the old unique index is dropped.
Keep that replacement separate from table/column changes so a failed replacement retains the
original uniqueness constraint. Ordinary expression index creation follows added columns, and
index removal precedes dropped columns.

On MySQL, scaffolding preserves known `ON UPDATE CURRENT_TIMESTAMP` clauses on non-key `DATETIME` and `TIMESTAMP`
columns with matching fractional precision. EF reads these values back after updates and ignores
explicit assignments on update. A known insert default enables insert readback and also makes EF ignore
explicit insert assignments. With no known default, the generated model writes the supplied insert value.
Create scripts and migrations preserve the clause through the `OceanBase:OnUpdateSql` annotation.
Schema recreation requires the SQL or value of any known default. Adding or making an update-only column
required in an existing table requires an explicit backfill migration. Oracle's catalog does not expose this behavior.

## Collection and scalar query details

General query collection roots support all eight CLR integer types, decimal, Single, Double, nullable
forms and integer-backed enums through scalar parameters or constants and SELECT/UNION ALL. Empty
roots retain their original element type and produce zero-row SQL. Numeric-to-numeric converters keep
their parameter/read hooks. Typing casts follow the provider carrier independently of column facets:
MySQL SIGNED/UNSIGNED, DECIMAL(65,30), FLOAT or DOUBLE; Oracle NUMBER, BINARY_FLOAT or BINARY_DOUBLE.
Non-finite floating inputs reject before execution. Boolean roots use the provider Boolean carrier
in MySQL and the decimal Boolean carrier in Oracle.

A captured signed Int32/Int64 collection with a standard mapping uses one JSON-text parameter and
`JSON_TABLE`. Other supported carriers use scalar bindings. Inline arrays can express constant
SQL on the EF Core 8.0.0 floor. See the EF Core 8 gap matrix for contextual converter boundaries.

Both modes support an explicit `string` property configured with `.HasColumnType("json")` for schema
creation, projection, reverse engineering and whole-document writes through `SaveChanges`.
Parameters use the Connector's native `OceanBaseDbType.Json` declaration. OceanBase validates the JSON text.
Use CLR `null` for SQL NULL and the string `"null"` for a JSON null document.
Query a document with `EF.Functions.OceanBaseJsonValue(row.Document, "$.name")`. The path must be a
nonempty constant or an evaluatable captured value for each query; dynamic column paths reject.
The function runs on the server, including constant documents, and returns text. Native JSON_VALUE
semantics apply: MySQL defaults to 512 characters and Oracle to 4000 bytes; overflow, missing paths,
JSON null, SQL NULL and malformed documents return SQL NULL. Invalid paths raise a native error.
MySQL returns object/array values as serialized JSON, while Oracle returns SQL NULL for nonscalars.
Partial-document updates, mapped collections and JSON-owned types remain outside this profile.

Oracle collection roots normalize every parameter/constant UNION ALL branch to the
common carrier; MySQL casts the first non-text branch. Text roots preserve native typed operands so
column comparison rules are retained without a narrowing CAST or conflicting CAST collation. Oracle variable non-LOB text uses
TO_CHAR/TO_NCHAR to preserve query values beyond a contextual column's length
instead of narrowing them through CAST. Contextual CHAR/NCHAR collection roots reject
before execution because a variable carrier changes fixed-width padding comparisons.
Ordinary scalar fixed-text comparisons retain their existing behavior. The ASCII profile includes
2001-character values. This does not establish general collation or LOB capacity.
Default Oracle byte-array roots use TO_BLOB, including typed empty roots. BLOB enumeration
and IS NULL/IS NOT NULL are supported; ordinary BLOB equality/inequality and membership
reject before execution. Use explicitly bounded RAW for comparable binary values;
RAW collection membership has native coverage. Oracle Queryable LOB set-operation
exclusions still apply. This does not add a general LOB comparison API.

Direct contextually converted element projections support SelectMany, FirstOrDefault and Join,
including nullable/empty roots and Distinct/Take composition. The owning numeric mapping controls
bindings, projected readers and the CLR default fallback. Incompatible requirements for one root or
its scalar-result consumers reject, including different converters with the same store type. Arbitrary
converter/operator composition remains unsupported. Native ordinary UNION typing/results
are covered by the bounded composition profile; wider converter combinations and costs are unverified.

Text collection roots support captured arrays/lists and empty inline arrays when the provider-owned mapping
uses a string carrier and does not convert nulls. Model-to-string converters retain parameter and reader
hooks; MySQL empty roots project an untyped SQL NULL and Oracle non-LOB text uses
`TO_CHAR(NULL)`/`TO_NCHAR(NULL)` with the owning charset. Oracle empty-string normalization occurs after
raw collection expansion. Bounded ASCII root UNION/membership results have native baseline coverage.
Wider text facets, collation and LOB behavior remain unverified.

Text and temporal `Min/Max` support scalar/selector and nullable forms using native aggregates and
the original mappings/readers. Supported temporal families are DateTime, DateTimeOffset, DateOnly,
TimeOnly and TimeSpan. Filters, grouping and empty/null shaping use EF's relational pipeline.
Null-converting converters, custom/cross-family carriers and Oracle CLOB/NCLOB extrema reject.
Converters use stored-value ordering. Native bounded lowercase ASCII text and the five temporal
families now have nullable/required extrema and empty/all-null result coverage on OceanBase 4.4.2.2;
wider collation, named-zone and converter combinations remain unverified.

Aggregate acceptance includes native numeric extrema, integral AVG promotion, checked SUM results,
nullable/composite/converted group keys, filtered/distinct aggregates, HAVING/paging and ordered
group collections/first entities in both modes. Converter aggregates operate on stored values and
apply the result converter once; arbitrary converters need not preserve arithmetic or ordering.
With Connector 0.4.0, MySQL `SUM` and `AVG` over the default `DECIMAL(65,28)` mapping accept
wide result metadata, including compensated empty/all-null SUM results. The mapping retains its
declared precision. EF decimal materialization requires an exactly representable CLR decimal value.
Oracle default NUMBER aggregates pass the tested result domain.

Integer `&`, `|`, `^`, `~`, `<<` and `>>` have per-mode SQL translations, including nullable values and
C# small-integer promotions. Bitwise enum storage must be numeric. Shift counts use C# width masking,
and signed right shifts preserve sign extension. Text/floating/decimal converter carriers are rejected
for these operations. Integer numeric converters operate in stored value space; arbitrary converter
equivalence, overflowing/narrowing casts and single evaluation of volatile operands need separate
contracts. C# expression trees reject `>>>` (CS7053). Native results remain unverified on OceanBase.

Boolean `^`, `==`, `!=`, `!`, AND/OR, Boolean conditions, conditional arms and coalesce normalize
each operand's numeric/text true/false encoding. Nullable logic and configured equality null semantics
remain distinct. Results use the native Boolean mapping even when operands have different converters.
Null-converting, ambiguous and Oracle empty-string encodings are rejected. Valid stored encodings,
native collation/coercion and repeated volatile operand evaluation remain acceptance boundaries.

## Schema creation and migrations

`Database.GenerateCreateScript()` generates supported relational DDL and `HasData` INSERTs offline. To create tables in an already
provisioned MySQL database or Oracle schema, explicitly name the target:

```csharp
optionsBuilder.UseOceanBase(connectionString, OceanBaseCompatibilityMode.MySql)
    .EnableOceanBaseSchemaManagement("application_database");
// await context.Database.EnsureCreatedAsync();
```

`HasData` uses EF's model-managed data conversions, owned/shared-row mapping and dependency order.
Create scripts append it after DDL. `EnsureCreated` and explicit `CreateTables` apply it only when they
create the schema, skipping excluded tables; an existing populated schema is not updated or repaired.
After DDL, initial data runs in one owned transaction with a fresh target-scope check. Failure/cancellation
rolls back that data transaction; DDL has already committed and requires inspection before recovery.
Use explicit stable seed keys. Explicit identity values do not reserve a generated-key range or promise
to advance Oracle's identity generator. Use an application-owned initialization step for data
outside the model after schema creation.

The current database/schema must match the declared name exactly, including case. `EnsureCreated` returns false
if any ordinary table exists; it does not reconcile or upgrade existing schemas. DDL is not atomic and a failure
can leave partially created objects requiring manual cleanup. Caller transactions are rejected. This option
enables supported table creation and migrations in that existing namespace.

MySQL virtual computed columns support nullability and comment alterations when their expression, type,
storage kind and other facets stay unchanged. Existing NULL computed values prevent a NOT NULL migration;
no computed value is backfilled. Expression/storage/identity-definition transitions still require an explicit
data-preserving migration supported by the target server. See G-34 in the gap matrix.

Table and column comments configured with HasComment are included in create
scripts and migrations. Migration diffs can add, replace or clear comments.
Oracle emits separate COMMENT ON statements. MySQL uses COMMENT clauses and
repeats the column definition when changing a column comment. Comment literals
support quotes, Unicode and line breaks. NUL is rejected in both modes. MySQL
rejects backslashes in comment literals because their interpretation depends on
NO_BACKSLASH_ESCAPES and the target rejects hexadecimal comment literals.
When running Oracle create scripts in SQL*Plus or a compatible client, use
`SET DEFINE OFF` to preserve text containing ampersands. Oracle migration scripts
already include this command.

`HasTrigger` describes a table trigger in the EF model. Create scripts and migrations accept the
metadata without generating trigger bodies, and metadata-only changes emit no SQL. Provision triggers
externally or author their creation and removal with `migrationBuilder.Sql`. The tested Oracle direct migration
profile wraps row-trigger DDL in `BEGIN EXECUTE IMMEDIATE '…'; END;`; use this form and
escape any single quotes within the DDL. Quoting the statement keeps `:NEW` and `:OLD` out of Connector
bind-parameter discovery.

To read back trigger-modified values during `SaveChanges`, configure the affected properties for store
generation. For a non-key value generated on insert and update, use a supported database default with
`ValueGeneratedOnAddOrUpdate`. MySQL reads by key on the same connection and Oracle uses `RETURNING INTO`
with the existing Oracle write profile. `HasTrigger` alone leaves property generation and refresh behavior
unchanged.

`Database.Migrate()` / `MigrateAsync()` now provide a locally tested migrations core for both modes under
the same schema opt-in. It supports table/column/index/constraint changes, seed data, identity/default/computed
metadata, history, upgrade/downgrade and idempotent scripts. Provision the namespace first; do not mix
EnsureCreated-managed tables with migrations. Caller/ambient/enlisted transactions and automatic retry
strategies reject before migration work because DDL commits implicitly. Inspect partial schema changes before
manual recovery. Configure the matching sync/async seeding callback; the opposite-only callback rejects early.

With `EnableOceanBaseDatabaseManagement`, MySQL model-level `UseCollation` sets the owned database's
default before schema creation or during a migration. The database must grant permission to change its
default. `EnsureCreated` skips the change when tables already exist, and `CreateTables` requires an empty
database when a model default is configured. The DDL leaves existing column definitions and data intact.
An evolved migration needs both the prior and target collation. When the old model omitted it, explicitly
supply the known previous collation in the migration's `Up` and `Down` operations. A generated downgrade
of the initial migration drops its tables and leaves the new default in place. Oracle and table-only
ownership reject this database-level setting. SQL generation and schema orchestration have local coverage. Native catalog
behavior and script execution await acceptance on the target server.

MySQL migration model diffs can toggle `AUTO_INCREMENT` on an unchanged, non-null signed `INT` or
`BIGINT` sole primary key. The target model must match the column definition. Table renames and
primary-key replacements need separate migrations. Other identity transitions and changes to computed
expressions or storage require an explicit data-preserving migration. The bounded identity-toggle native
profile is recorded in the release checklist; other transitions require separate contracts.
MySQL also supports nullability/comment changes to an unchanged virtual computed column. EF synthetic
backfill defaults do not cause computed-value updates; resolve existing NULLs before requiring the column.

Serialize runtime and scripted migrations externally and stop on the first SQL error. Oracle
scripts target SQL*Plus-style clients, disable variable substitution, exit on SQL errors and commit the final
history changes; rollback cannot undo DDL. MySQL scripts require autocommit. Raw migration SQL is trusted,
single-statement server input; client directives and multi-statement raw batches are outside this contract.
Native DDL beyond the documented checks, script-client execution and trigger reverse engineering
remain outside the verified scope.

`HasSequence<long>` declares a standalone model sequence. Both compatibility modes support create-script
generation, schema creation and migrations for creation, alteration, restart and drop. Oracle supports
renaming an unqualified sequence in the current schema. MySQL sequence rename and qualified Oracle
sequence rename are rejected. Sequence DDL uses Int64 bounds and defaults to `NOCACHE`. Oracle
sequence annotations `OceanBase:SequenceCache` (Int64, zero for NOCACHE) and
`OceanBase:SequenceOrdered` (Boolean) preserve explicit cache/order settings. Keep an alteration and its
restart in a consecutive group of sequence alterations and restarts so the provider can change the bounds
and restart value in one statement. Sequence DDL commits implicitly.

When the model declares a sequence, `EnsureCreated` treats an existing sequence in the selected namespace
as existing schema. MySQL requires read access to `oceanbase.DBA_SEQUENCES` for this check.
`CreateTables` creates the declared objects in the selected namespace. Oracle reverse engineering
uses the public Connector sequence-start API and preserves the start after values have been consumed,
increment, bounds, cycle, cache and ordering. Sequences outside the supported Int64 bounds or EF
increment range are omitted with a warning. MySQL sequence reverse engineering remains deferred pending
reliable schema-qualified metadata integration in EF. The existing Connector can query the server's
declared start through ordinary commands; its `GetSchema("Sequences")` result omits that field.
Oracle sequence discovery covers each selected namespace, including when tables are explicitly selected.
Scaffolding preserves allocation annotations through provider design-time services. The
private EF.Design build reference is excluded from the runtime package dependencies.

Configure `UseOceanBaseHiLo` on an Int32 or Int64 primary key to allocate permanent keys from a shared
model sequence in either compatibility mode. For TPC, configure the key on the hierarchy root.

```csharp
modelBuilder.HasSequence<long>("OrderNumbers").StartsAt(1).IncrementsBy(32);
modelBuilder.Entity<Order>().Property(order => order.Id).UseOceanBaseHiLo("OrderNumbers");
```

EF obtains the first key of each block during tracking. Use `AddAsync` for asynchronous sequence access.
The sequence must produce positive values, use a positive increment and have cycling disabled. The
increment is also the block size. Each block allocation checks the catalog increment and cycle flag, so
the account needs read access to the sequence metadata. Rollbacks and disposed contexts can leave gaps.
Drain active contexts before restarting or changing a sequence used by HiLo. Reserve separate ranges
for explicitly assigned or seeded keys. Keep the selected database or schema fixed for the lifetime of
the context, including when using Oracle session commands to switch schemas.

## Inheritance and split entities

TPT (`UseTptMappingStrategy`, with a table for each mapped type) has a locally tested schema/query/write
workflow in both modes. The root table generates an int/long identity once; intermediate and leaf tables store
that same key as an ordinary primary/foreign key. Assigned/composite keys and client Guid keys retain their
existing generation rules. Initial/evolved migrations and snapshots, hierarchy/OfType queries, derived Includes
and tracked multi-table inserts/updates/deletes have local coverage. Keep automatic transactions enabled for
multi-table saves. As with EF's relational executor, SaveChanges counts physical modification commands in this
profile; one leaf spanning three tables can return 3. Existing generated-key/default limitations still apply,
including MySQL's rejection of server-default key readback. The bounded native profile covers three-level
Int32 identity insert/reload, root/leaf updates, multi-table rollback and reverse-dependency deletion in both
modes with sync/async execution. Wider key types, hierarchy queries and schema evolution need separate acceptance.

TPC (`UseTpcMappingStrategy`) has a locally tested schema/migrations/query/CRUD profile in both modes.
Concrete tables repeat inherited columns; abstract types have no table. Use application-assigned keys
(`ValueGeneratedNever`, including composite keys) unique across the whole hierarchy, client-generated
Guid keys without an integer store conversion, or a shared HiLo sequence on the hierarchy root.
Oracle also supports shared sequence defaults for ordinary tracked inserts. Configure the hierarchy root
with `HasDefaultValueSql` and `ValueGeneratedOnAdd` and declare the sequence in the model.

```csharp
modelBuilder.HasSequence<long>("OrderNumbers").StartsAt(1);
modelBuilder.Entity<Order>().UseTpcMappingStrategy().Property(order => order.Id)
    .HasDefaultValueSql("\"OrderNumbers\".NEXTVAL").ValueGeneratedOnAdd();
```

Match the quoted sequence name in the default exactly. Include the quoted schema when declaring the
sequence in an explicit schema or mapping any concrete table to one. The sequence must be ascending,
positive and noncyclic. Use `NUMBER(19)` for Int64 or `NUMBER(10)` for Int32, with an explicit maximum
of `int.MaxValue` or lower for Int32. Oracle returns the generated key for relationship fixup and dependent
inserts. Reserve disjoint ranges across all concrete tables for explicit and seeded keys. Keep the physical
defaults and sequence aligned with the model. Use an explicit data-preserving migration for changes to the
shared sequence or its existing column defaults. Quiesce writers and preserve the allocation range across
all concrete tables during that transition.

MySQL supports assigned keys, client Guid and HiLo. Schema generation, migrations and the first write
batch reject independent table identities, computed keys and generated defaults outside the Oracle profile.
Base queries use EF's UNION ALL expansion; OfType, inherited filters, concrete collection Includes,
nullable concurrency and automatic multi-entity transactions have local coverage. HiLo allocates disjoint
keys in the tested profile. Cross-table uniqueness constraints, polymorphic foreign-key enforcement and
broader native inheritance coverage remain outside this profile.

Entity splitting (`SplitToTable`) and table splitting (related entities using the same `ToTable`) have a
locally tested schema/migrations/query/CRUD profile in both modes. Only an entity's primary table generates
its identity; required fragment tables receive the same key. EF requires every entity-split fragment row to
exist. A shared row produces one physical insert/update/delete, and an optional dependent whose non-shared
nullable values are all NULL materializes as absent. Map shared concurrency tokens consistently on both
entities; application-managed changes must set both mapped values, including when removing an optional
dependent. Otherwise EF can propagate NULL from the deleted dependent into an unchanged non-nullable
principal token during command preparation. Database-generated tokens are read once and propagated to both.
Removing an optional shared dependent clears its columns without deleting the
principal row. Keep automatic transactions enabled for multi-table saves. Both modes have native sync/async
coverage for two-table entity CRUD, optional shared-row absence/presence/removal and application-managed
shared-token conflicts. Wider rollback, schema-evolution and generated-token combinations require separate
acceptance; this profile does not add split queries.

The provider exposes EF design services for migrations authoring. Reference `Microsoft.EntityFrameworkCore.Design`
as a private development dependency in the startup project, and configure the same explicit mode in its host or
`IDesignTimeDbContextFactory`. The existing three-argument `UseOceanBase` overloads remain available; a callback
can select a separate migrations assembly and history table:

```csharp
optionsBuilder.UseOceanBase(connectionString, OceanBaseCompatibilityMode.MySql, options => options
    .MigrationsAssembly("Application.Migrations")
    .MigrationsHistoryTable("__ApplicationHistory"))
    .EnableOceanBaseSchemaManagement("application_database");
```

Omit `MigrationsAssembly` to keep migrations in the context assembly. A history schema, when supplied, must
match the opted-in namespace. Initial/evolved migrations, model snapshots, pending-model checks, forward/reverse/
idempotent scripts, migration listing/removal and framework-dependent Windows bundles have a package-only
verification fixture under `tests/OceanBase.EntityFrameworkCore.ToolingConsumer`.
`dotnet ef migrations remove --force` may inspect or change a live database; the fixture blocks all
connection opens and tests local file removal only. Offline bundle verification builds the executable and runs
`--help`. The EF Core 8 release checklist tracks the verified tooling and live migration profiles.

## Seeding and namespace ownership

Use `HasData` for data managed by the model and run other application seeding explicitly
after successful schema creation or migration. The application owns transaction handling and
coordination for that initialization.

For an application-owned MySQL database, `EnableOceanBaseDatabaseManagement("application_database")`
also enables namespace creation and `EnsureDeleted`. It declares ownership of the **entire database**,
including objects outside the model; deletion drops that database. System database names are rejected.
The target must be closed and select exactly the declared database name. An explicit administration
connection supports supplied targets and native credential, routing and TLS hooks:

```csharp
optionsBuilder.UseOceanBase(connectionString, OceanBaseCompatibilityMode.MySql)
    .EnableOceanBaseDatabaseManagement("application_database")
    .UseOceanBaseManagementConnection(() => new OceanBaseConnector.OceanBaseConnection(administrationConnectionString));
```

Load both strings from configuration/secrets. The factory must return a fresh, closed, independent
connection with no selected MySQL database. The provider disposes it after each namespace operation;
ordinary queries and existing-schema table creation never invoke the factory. Its configuration is
authoritative for administration: the application must select the same tenant as the target and provide
the required privileges, routing and TLS policy. The provider does not infer tenant equivalence from
different user names or callbacks. Returning the target rejects without disposing it. A factory alone
does not grant namespace ownership. Without a factory, the original provider-created string-only path
still rejects native-only configuration changes, hooks and unverifiable hidden credentials.
Oracle can explicitly own an entire user/schema with an independent administrator connection:

```csharp
optionsBuilder.UseOceanBase(connectionString, OceanBaseCompatibilityMode.Oracle)
    .EnableOceanBaseUserManagement("application_user", () => passwordFromSecretStore)
    .UseOceanBaseManagementConnection(() => new OceanBaseConnector.OceanBaseConnection(administrationConnectionString));
```

The closed target must configure that exact user login, optionally with its `@tenant` routing suffix,
and no different initial schema. Pass the user name without `@`/tenant routing to the ownership option.
The administrator must use a different login in the same tenant, with
access to `SYS.DBA_USERS` and the privileges needed to create/drop users and grant CREATE SESSION and
CREATE TABLE. Built-in system users cannot be owned. A successful catalog query establishes whether the
exact user exists; permission/authentication errors and concurrent DDL failures propagate. Existing users
keep their passwords/privileges and follow the existing-schema table-creation profile. `EnsureDeleted`
drops the entire declared user with CASCADE, including objects outside the EF model.

The password callback runs only for user creation. Empty values, double quotes and control characters
are rejected before DDL. EF Core 8 command logs and execution interceptors receive redacted `CREATE USER`
text even with sensitive logging enabled. The provider supplies the password to the underlying command
for execution. Do not log callback/server exception messages containing credentials.
User creation grants only CREATE SESSION and CREATE TABLE. A later grant/table failure can leave a user
requiring manual repair; no automatic deletion, password reset or privilege reconciliation occurs. The
provider clears the target pool before namespace DDL and again after success; other processes/sessions require
application coordination. Oracle tenants themselves are never created or deleted.
DDL is non-atomic: failed table creation can leave a new database or partial objects; no automatic
drop rollback or retry occurs. Actual server syntax, privileges and pool effects need live validation.

Automatic savepoint recovery after cancellation uses an uncancelled cleanup token and preserves the original
save error. Recovery or native disposal can still fail; discard an unusable context/transaction and explicitly
resolve native state. Local command doubles do not establish physical rollback or transaction atomicity.

Healthy native query readers consume remaining rows/results during close/disposal so that an explicitly
opened connection retains its session. Early enumeration disposal may therefore wait for the remaining
response, within the original command timeout. Failed readers and broken connections are not drained.
An explicitly infinite command timeout also leaves this cleanup without a timeout bound.
In-flight native cancellation/timeout can retain an `OceanBaseException` with an unknown execution outcome
and its cancellation/timeout cause; this state is never converted into a safe-to-retry result. Close/reopen
the connection before further work and resolve any uncertain transaction outcome separately.

## Bulk updates and deletes

`ExecuteUpdate` and `ExecuteDelete` have local coverage and a bounded native sync/async profile in both modes:
filtered updates/deletes, affected rows, joined-source updates and cyclic two-column assignments are checked
against freshly read persisted values.
They execute immediately, return affected rows and bypass the change tracker; applications supply concurrency
predicates and a transaction when several calls must share one. MySQL joined updates use native table-reference
syntax. Single-table acyclic assignments preserve old values through ordering. Cyclic assignments, dependent
joined assignments and derived joined sources use projected original values under SELECT /*+ NO_MERGE */,
joined to the physical target by every primary-key column. This requires a keyed target; broader optimizer and
read-consistency combinations require separate acceptance. Oracle joined/paged updates use correlated
scalar source queries and EXISTS over every physical primary-key column. Filters, nested paging and left joins
are preserved; joined keyless targets reject. Multiple source matches retain the scalar-subquery cardinality
error rather than selecting an arbitrary value. Each setter repeats the source query, which can be expensive.
Direct literal/captured setters use column write bounds; broader server-computed expression bounds require
separate acceptance. These are coverage boundaries, not demonstrated server defects.

Review the feature inventory, gap inventory and release checklist under `docs/efcore` for implemented
scope, deferred integrations and revision-specific acceptance.
