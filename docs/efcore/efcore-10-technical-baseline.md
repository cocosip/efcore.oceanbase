# EF Core 10 Provider Technical Baseline

## Purpose

This document is the implementation reference for the first provider vertical slice. It records the EF Core
contracts that must be checked against the exact EF Core 10 package before code is written. It is intentionally
smaller than a complete EF Core feature catalog.

## Version and package baseline

- Development branch: dev.
- Release branch: efcore/10.0.
- EF dependency range: Microsoft.EntityFrameworkCore and Microsoft.EntityFrameworkCore.Relational [10.0.0, 11.0.0).
- Initial provider asset: the runtime target required by EF Core 10.
- ADO.NET dependency: the public OceanBaseConnector package APIs.
- Public package: OceanBase.EntityFrameworkCore 10.x.

EF Core 10 requires .NET 10 to build and run. NuGet normally selects the lowest applicable version inside a bounded
range, unless a consumer dependency requires a higher version. A range permits consumer patch upgrades; it does not
automatically test those upgrades. The package-consumer gate must record the SDK, runtime, resolved EF Core patch,
provider package and connector.

## Provider entry points

The first implementation must establish these contracts:

1. A UseOceanBase options extension for a connection string and an existing DbConnection.
2. An explicit OceanBaseCompatibilityMode value for MySQL or Oracle mode.
3. An options extension implementing the EF Core database-provider contract and applying provider services.
4. A provider invariant name and IsOceanBase database-facade check.
5. A model-cache key that includes the compatibility mode.

The connection string must remain owned by OceanBaseConnector. The EF provider passes it through and does not
reimplement connection-string parsing, pooling or protocol negotiation.

## EF Core service map

The implementation is split by EF Core contract. The exact constructor signatures and service lifetimes must be
verified against the 10.x assemblies before each task:

| Area | Contract to verify | First-slice responsibility |
| --- | --- | --- |
| Options | IDbContextOptionsExtension and its extension-info object | Register the provider and validate mode/options. |
| Model cache | IModelCacheKeyFactory | Separate MySQL and Oracle models. |
| Connection | IRelationalConnection / RelationalConnection | Create and own an OceanBaseConnection through the connector. |
| SQL helpers | ISqlGenerationHelper | Quote identifiers and create parameter names per mode. |
| Type mapping | IRelationalTypeMappingSource | Map common CLR and OceanBase scalar types. |
| Query SQL | IQuerySqlGeneratorFactory and provider query generator | Emit mode-correct SELECT, predicates, joins and paging. |
| Boolean SQL | IRelationalParameterBasedSqlProcessorFactory / RelationalParameterBasedSqlProcessor | Convert Oracle conditions and numeric Boolean projections after parameter/nullability processing. |
| Updates | IUpdateSqlGenerator and modification-command batch services | Emit INSERT, UPDATE and DELETE with parameters. |
| Key retrieval | Generated-value and result-column mapping services | Read database-generated keys without assuming SQL Server OUTPUT. |
| Database | IRelationalDatabaseCreator | Implement connection/database existence and EnsureCreated boundaries. |
| Conventions | IProviderConventionSetBuilder and model validation services | Apply only conventions required by the first feature profile. |

Migrations, reverse engineering, design-time services, JSON/LOB/OBJECT/REF CURSOR mappings and provider-specific
bulk operations are separate milestones. Their service contracts must not be added as placeholders.

## Mode-specific SQL contract

The MySQL and Oracle paths must have separate tests for:

- identifier delimiters and reserved words;
- parameter prefix and binding rules;
- Boolean and temporal literals/results;
- decimal precision and scale;
- paging syntax;
- identity or sequence key generation;
- affected-row and concurrency semantics;
- transaction and savepoint behavior.

Common code is allowed only where both modes produce the same SQL and result semantics. A mode check based only
on the connection string is insufficient when the EF model cache can reuse a model.

## Connection validation and mapping checkpoint

The following contracts were checked against EF Core Relational 10.0.0 sources and the published
OceanBaseConnector 0.2.0 public API:

- [RelationalConnection lifecycle](https://github.com/dotnet/efcore/blob/v10.0.0/src/EFCore.Relational/Storage/RelationalConnection.cs).
- [DecimalTypeMapping](https://github.com/dotnet/efcore/blob/v10.0.0/src/EFCore.Relational/Storage/DecimalTypeMapping.cs)
  and [StringTypeMapping](https://github.com/dotnet/efcore/blob/v10.0.0/src/EFCore.Relational/Storage/StringTypeMapping.cs).
- [Property types, length and precision](https://learn.microsoft.com/ef/core/modeling/entity-properties).

`RelationalConnection` reads `DbConnection` during disposal/reset. The provider therefore validates caller-supplied
connections during construction, replacements before assignment, created connection strings before allocation,
and existing connections before each sync/async open. A getter does not revalidate a rejected connection during
cleanup. Failed replacement validation preserves the previous connection. EF's base implementation retains
ownership and disposal behavior. Connection interceptors must not replace connector connections with incompatible
implementations; validation before opening also covers intercepted creation.

| Mapping facet | MySQL contract | Oracle contract |
| --- | --- | --- |
| Default decimal | DECIMAL(65,30), enough integer/fractional capacity for CLR decimal values | Unqualified NUMBER, allowing connector value-based precision/scale inference |
| Configured decimal | Precision 1..65; scale 0..30 and no greater than precision | Precision 1..38; scale -84..127, including scale greater than precision |
| Precision without scale | Scale defaults to zero | Scale defaults to zero |
| Explicit decimal type | DECIMAL/NUMERIC declarations are preserved; unqualified type uses store defaults (10,0) | NUMBER/DECIMAL/NUMERIC declarations are preserved |
| Default string | LONGTEXT; bounded VARCHAR(255) for a key/index | NVARCHAR2(2000); bounded NVARCHAR2(255) for a key/index |
| Bounded variable string | VARCHAR(n) up to 16383 characters, otherwise LONGTEXT | NVARCHAR2(n) up to 2000; non-Unicode VARCHAR2(n CHAR) up to 4000, otherwise NCLOB/CLOB |
| Fixed string | CHAR(n), at most 255 | NCHAR(n), at most 1000; non-Unicode CHAR(n CHAR), at most 2000 |
| String parameter facets | DbType reflects Unicode/fixed length; Size is set explicitly | Same metadata contract; CHAR/BYTE units in explicit store declarations preserve Size |

Decimal mappings explicitly configure parameter Precision and the connector's DecimalScale, because the generic
EF mapping does not set these parameter facets. DecimalScale preserves Oracle negative scales through the public
connector API; values that require rounding remain subject to connector validation. String mappings explicitly set
parameter Size. Cloning retains these provider parameter hooks. Input null becomes DBNull; output-only parameters
retain their unset value, direction and facets until execution.

The text limits are conservative mapping choices, not a guarantee of every charset/server byte limit. MySQL
Unicode depends on the configured tenant/column character set; the Unicode flag does not configure that charset.
Oracle CHAR/BYTE declarations retain their explicit storage semantics. Large Oracle strings still use LOB storage,
which restricts ordering/grouping. Live charset, byte-limit, decimal round-trip and LOB execution tests remain open.
Scalar SQL literals, Boolean/Guid/temporal behavior, remaining explicit store types and value converters also remain
open acceptance work; this checkpoint does not close the entire MAP category.

## Numeric and Guid reading contract

OceanBaseConnector 0.2.0 identifies source revision 4734781a7028aea990af7a832ca7d251a2055556 in its nuspec.
Its typed reader methods cast primitive values rather than generally converting numeric types. Oracle NUMBER
results are decimal or OceanBaseDecimal, so an integer mapping cannot assume GetInt32/GetInt64 succeeds.
Integer mappings must read the provider value, convert with range checks, and reject fractional values rather
than silently rounding. Oracle generated output propagation uses the same normalization/conversion before
applying the property's converter. MySQL's signed/unsigned integer storage must preserve the CLR range. Boolean reading
must accept the connector's Boolean and numeric result forms, independently of the tiny-integer reader option.

Guid storage is explicit: MySQL CHAR(36) uses the standard hyphenated string representation; Oracle RAW(16)
uses the built-in EF GuidToBytesConverter, which follows Guid.ToByteArray .NET serialization order. This does
not imply compatibility with an external application's alternative UUID byte order. Parameter and result
conversion must be paired. Oracle binary literals use HEXTORAW, not the MySQL X'...' form.

String SQL literals must preserve quotes and backslashes regardless of MySQL NO_BACKSLASH_ESCAPES. A hazardous
MySQL string can be represented by UTF-8 hex conversion rather than a mode-dependent backslash escape. Oracle
national-character literals use the N prefix. Parameters remain the preferred path for runtime values.

These are implementation acceptance contracts; their live round-trip gates remain open until server tests pass.

The scalar checkpoint implements all eight integer mappings with range-checked provider-value reading, and
composes EF's built-in converters for Guid and Oracle Boolean values. Mapping clones retain dialect and parameter
hooks. Direct converter tests and intercepted EF query/write tests cover Oracle-shaped decimal values and
MySQL numeric Boolean results; this does not prove wire execution. Temporal types, complete explicit-type/facet
validation, binary boundaries and the full custom-converter matrix remain open.

## Binary floating-point storage contract

This MAP-02/MAP-06/MAP-07/UPD-02 slice covers CLR float/double and native binary floating-point columns.
Read EF v10.0.0 FloatTypeMapping/DoubleTypeMapping, mapping-info parsing and reader customization, and published
connector 0.2.0 prepared Single/Double binding and exact typed reader casts before implementation. OceanBase
documentation remains pinned to 2a5ae3a23a51729d065cec716508eb26cb064106 (V4.3.5):
[MySQL floating types](https://github.com/oceanbase/oceanbase-doc/blob/2a5ae3a23a51729d065cec716508eb26cb064106/en-US/700.reference/500.sql-reference/100.sql-syntax/200.common-tenant-of-mysql-mode/100.basic-elements-of-mysql-mode/100.data-type-of-mysql-mode/200.numeric-of-mysql-mode/400.floating-point-type-of-mysql-mode.md),
[Oracle floating types](https://github.com/oceanbase/oceanbase-doc/blob/2a5ae3a23a51729d065cec716508eb26cb064106/en-US/700.reference/500.sql-reference/100.sql-syntax/300.common-tenant-of-oracle-mode/300.basic-elements-of-oracle-mode/100.built-in-data-types-of-oracle-mode/300.numeric-data-type-of-oracle-mode/400.floating-point-number-of-oracle-mode.md),
and [Oracle numeric literals](https://github.com/oceanbase/oceanbase-doc/blob/2a5ae3a23a51729d065cec716508eb26cb064106/en-US/700.reference/500.sql-reference/100.sql-syntax/300.common-tenant-of-oracle-mode/300.basic-elements-of-oracle-mode/300.literal-of-oracle-mode/300.numeric-literal-of-oracle-mode.md).

| Contract | MySQL | Oracle |
| --- | --- | --- |
| Defaults | float -> FLOAT; double -> DOUBLE | float -> BINARY_FLOAT; double -> BINARY_DOUBLE |
| Explicit native storage for either CLR type | FLOAT, FLOAT(p), DOUBLE, DOUBLE PRECISION; optional UNSIGNED | BINARY_FLOAT, BINARY_DOUBLE |
| Precision | FLOAT(p), p=0..24 chooses 32-bit, p=25..53 chooses 64-bit; HasPrecision(p) selects FLOAT(p), including a bare explicit FLOAT declaration | Native binary precision is intrinsic; configured precision/scale reject |
| Other facets | Declared/configured precision must agree; size/fixed-length/scale reject | Size/fixed-length reject |
| Literals | Invariant round-trip double representation with exponent, including exact widening of a CLR float | Invariant round-trip representation with F (CLR float) or D (CLR double) suffix |

The provider retains Single/Double parameter binding according to the CLR provider type, independently of column
width. GetValue plus checked numeric conversion handles server float/double reader shapes, including converter
composition and generated outputs. IEEE rounding during explicit float/double conversion is expected; a finite
database value that would overflow CLR float must throw rather than materializing infinity. Existing non-finite
reader values are preserved, following the connector. Parameters and literals reject NaN/infinity: the pinned
server documentation does not support these input constants. Output-only parameters retain their direction.

Apply physical 32-bit capacity and UNSIGNED checks only to converted values actually written by SaveChanges;
finite query operands/arithmetic/CASE results may exceed a column's capacity or be negative. Never narrow query
parameters to the column type. Finite values within capacity may round when an explicitly narrower column stores
them. Clone precision changes update FLOAT(p) and capacity together. FLOAT(M,D), ZEROFILL, session-dependent REAL,
Oracle NUMBER-backed FLOAT and decimal-backed storage require separate contracts and currently reject clearly.
Custom converters can target supported decimal storage when that is intended. MySQL/Oracle live boundaries,
subnormal preservation, Oracle normalization of negative zero, server arithmetic and comparisons remain required
open acceptance cases; client parameter/reader tests do not prove those server behaviors.

## Explicit Guid storage contract

This MAP-04/MAP-06/MAP-07 slice retains the defaults above and honors an explicit Guid column declaration.
The EF v10.0.0 GuidToStringConverter, GuidToBytesConverter and mapping-source converter composition, and the
published connector's prepared string/binary binding were checked before implementation. Reuse the existing
text/binary mapping implementations and EF converters; do not introduce a second byte-order convention.

| Representation | MySQL | Oracle |
| --- | --- | --- |
| Standard hyphenated text, exactly 36 characters | CHAR(36), VARCHAR(36) | CHAR(36), VARCHAR2(36), NCHAR(36), NVARCHAR2(36); CHAR/VARCHAR2 may specify CHAR or BYTE semantics |
| .NET binary order, exactly 16 bytes | BINARY(16), VARBINARY(16) | RAW(16) |

The explicit declaration controls conversion, DbType, size and fixed/variable metadata. Surrounding whitespace
and case are accepted. Explicit Unicode/fixed/length facets must agree with the representation; numeric facets,
other lengths, LOBs and incompatible/cross-mode declarations reject clearly. Without an explicit declaration,
MySQL keeps CHAR(36) (VARCHAR(36) when fixed-length is explicitly false) and Oracle keeps variable RAW(16).
Unicode selects string binding for MySQL text; Oracle national types select Unicode, CHAR/VARCHAR2 select ANSI.
Binary mappings reject a Unicode=true facet. Callers needing another encoding must configure a value converter
and the corresponding string/byte-array column themselves. Null handling, converter composition and generated
outputs follow the existing scalar pipeline. Guid ordering, SQL UUID functions, server-generated keys and live
round trips remain separate acceptance cases; matching the byte representation does not establish their support.

## Explicit integer and Boolean storage contract

This MAP-01/MAP-06/MAP-07 slice extends the existing checked integer reader contract to physical column types.
Read EF v10.0.0 `IntTypeMapping`, `BoolTypeMapping`, `RelationalTypeMapping` and `RelationalTypeMappingSource` and
the published connector's prepared numeric parameter handling before implementation. The OceanBase documentation
baseline is the same pinned V4.3.5 revision used below: MySQL `200.numeric-of-mysql-mode/200.integer-type-of-mysql-mode.md`
and Oracle `300.numeric-data-type-of-oracle-mode/200.number-data-type-of-oracle-mode.md` in their respective type directories.

| Contract | MySQL | Oracle |
| --- | --- | --- |
| Explicit integral storage | TINYINT, SMALLINT, MEDIUMINT, INT/INTEGER, BIGINT, each optionally UNSIGNED | NUMBER, NUMBER(p), NUMBER(p,0), DECIMAL/NUMERIC equivalents; bare INT/INTEGER/SMALLINT aliases |
| Boolean storage | The same integer families, including BOOL/BOOLEAN and TINYINT(1) | The same zero-scale numeric profile, with the existing Boolean-to-decimal converter |
| Physical bounds | Native signed/unsigned width, including signed/unsigned 24-bit MEDIUMINT | Precision 1..38, zero scale; bare NUMBER does not constrain precision |
| Configured precision | Rejected: integer display width is not EF numeric precision | HasPrecision(p) generates NUMBER(p,0); conflicting explicit/configured precision rejects |
| Display width | Optional width 1..255 is preserved in SQL; it never narrows storage or becomes parameter Size | Not a display-width feature |
| Unsupported combinations | BIT, ZEROFILL, SERIAL attributes and non-integral columns reject instead of silently using the default | Nonzero scale, unsupported columns and incompatible facets reject instead of introducing rounding |

All eight CLR integer types retain their own range and DbType; a narrower explicit column additionally constrains
input values. Validate current values of written columns before executing DML, independently of permissive server
SQL modes. Apply converters before validation. EF propagates column mappings to query operands and CASE branches;
those values and arithmetic results may exceed the stored column's capacity and must remain valid queries.
Original/concurrency values used only in predicates are also query operands. Query parameters retain CLR binding
and required metadata; physical capacity checks must not run in general parameter or literal generation.
Oracle integer Precision/Scale are passed to parameter metadata; the connector retains typed numeric binding.
Nulls and output-only parameters retain existing semantics. Clones and enum converters preserve validation and
reader hooks; changing a clone's Oracle precision must update its declaration and capacity together. Boolean
input remains zero/one; existing reader semantics are unchanged. Full Boolean predicate/null behavior remains QRY-01.

Acceptance tests cover aliases, display-width independence, signed/unsigned and precision boundaries, null/output
parameters, malformed/incompatible declarations, clones/converters and real EF query/write interception. Live
round trips, unsigned arithmetic restrictions and server SQL mode/DDL remain separate open gates.

## Binary mapping contract

This MAP-04/MAP-06 acceptance slice covers `byte[]` storage selection, explicit declarations, parameter/literal
length validation, nulls and mapping clones. It does not close large/empty Oracle BLOB execution or live gates.
Read EF v10.0.0 `ByteArrayTypeMapping`, `RelationalTypeMappingInfo` and `RelationalTypeMapping` before changes.
The published connector revision identified above binds ordinary byte arrays through `DbType.Binary`; its
`OceanBaseDbType.Blob` denotes a LOB locator and must not be used for ordinary byte-array values.

The OceanBase documentation baseline is revision `2a5ae3a23a51729d065cec716508eb26cb064106` (V4.3.5):
[binary strings](https://github.com/oceanbase/oceanbase-doc/blob/2a5ae3a23a51729d065cec716508eb26cb064106/en-US/700.reference/500.sql-reference/100.sql-syntax/200.common-tenant-of-mysql-mode/100.basic-elements-of-mysql-mode/100.data-type-of-mysql-mode/400.string-of-mysql-mode/300.binary-and-varbinary-of-mysql-mode.md),
[string lengths](https://github.com/oceanbase/oceanbase-doc/blob/2a5ae3a23a51729d065cec716508eb26cb064106/en-US/700.reference/500.sql-reference/100.sql-syntax/200.common-tenant-of-mysql-mode/100.basic-elements-of-mysql-mode/100.data-type-of-mysql-mode/400.string-of-mysql-mode/200.char-and-varchar-of-mysql-mode.md),
[BLOB types](https://github.com/oceanbase/oceanbase-doc/blob/2a5ae3a23a51729d065cec716508eb26cb064106/en-US/700.reference/500.sql-reference/100.sql-syntax/200.common-tenant-of-mysql-mode/100.basic-elements-of-mysql-mode/100.data-type-of-mysql-mode/500.large-object-of-mysql-mode/200.blob-type-of-mysql-mode.md) and
[RAW](https://github.com/oceanbase/oceanbase-doc/blob/2a5ae3a23a51729d065cec716508eb26cb064106/en-US/700.reference/500.sql-reference/100.sql-syntax/300.common-tenant-of-oracle-mode/300.basic-elements-of-oracle-mode/100.built-in-data-types-of-oracle-mode/500.raw-data-type-of-oracle-mode.md).
These references guide conservative mapping choices; they do not establish a tested server version.

| Case | MySQL | Oracle |
| --- | --- | --- |
| Unbounded byte array | LONGBLOB, replacing the previous 65,535-byte BLOB default | BLOB |
| Bounded variable array | VARBINARY(n) through 262,144 bytes, otherwise LONGBLOB | RAW(n) through 2,000 bytes, otherwise BLOB |
| Key/index without a size | VARBINARY(255) | RAW(255) |
| Fixed array | BINARY(n), conservative length 1..255; server right-pads shorter values with zero bytes | Rejected: RAW is variable-length and does not implement fixed binary semantics |
| Explicit declarations | BINARY (default 1), BINARY(n), VARBINARY(n), TINYBLOB, BLOB, MEDIUMBLOB, LONGBLOB | RAW(n), BLOB |
| Null parameter/literal | DBNull / NULL | DBNull / NULL |
| Empty non-null input | Allowed without padding for variable storage | Rejected until a tested non-null empty-BLOB transport exists; RAW empty values cannot be assumed distinct from NULL |

Outer declaration whitespace is normalized consistently before storage classification. Explicit declarations
must match the mode and configured size/fixed facets; malformed declarations, absent
required lengths, non-positive lengths, unsupported precision/scale and conflicting facets fail clearly.
LOB keys/indexes are rejected because this profile does not implement prefix/function indexes. MySQL storage
capacities are 255 / 65,535 / 16,777,215 / 536,870,910 bytes respectively for the four BLOB families; packet/memory
limits remain independent. Reject oversized values actually written by SaveChanges before DML, including values
that a permissive MySQL SQL mode could truncate. Apply converters first. Query comparisons can use values longer
than the column and must not inherit this write constraint. Output-only parameters retain direction/facets without
input checks. Oracle empty-input and literal-function limits are separate transport constraints.

Oracle literal generation uses HEXTORAW for at most 2,000 bytes; reject larger literals and use parameters instead.
The BLOB parameter path, large payloads, empty-BLOB support, MySQL padding, binary-key comparison and generated
binary/Guid outputs still require additional implementation or live evidence. The focused unit suite must cover
both modes, declaration boundaries, null/empty/oversized inputs and cloned/converter-composed mappings; intercepted
sync/async EF queries and tracked writes must verify parameter and materialization paths.

## Temporal mapping contract

MAP-05/MAP-06/MAP-07 and UPD-02 cover parameter binding, SQL literals, reader conversion and generated outputs.
Read the EF10 DateTime/DateTimeOffset/TimeSpan mapping sources and
[precision guidance](https://learn.microsoft.com/ef/core/modeling/entity-properties#precision-and-scale)
before implementation. Temporal EF Precision means fractional-second digits; configure the connector's Scale,
not its numeric Precision. Scale is not an independent EF temporal facet.

| CLR type | MySQL storage and semantics | Oracle storage and semantics |
| --- | --- | --- |
| DateTime | DATETIME(6); unzoned, Kind.Unspecified | TIMESTAMP(7); unzoned, Kind.Unspecified |
| DateTimeOffset | DATETIME(6); normalize the instant to UTC, bind an unspecified DateTime and read offset zero; original offset is not retained | TIMESTAMP(7) WITH TIME ZONE; retain the numeric UTC offset |
| DateOnly | DATE; convert to/from midnight DateTime | DATE; convert to/from midnight DateTime; reject a non-midnight external value |
| TimeOnly | TIME(6); convert to/from TimeSpan in [00:00,24:00) | INTERVAL DAY(1) TO SECOND(7); zero-day, non-negative interval; convert through the connector's exact interval value |
| TimeSpan | TIME(6); signed duration bounded by 838:59:59.999999 | INTERVAL DAY(9) TO SECOND(7); signed duration; checked conversion to/from the connector's exact interval value |

MySQL fractional precision is 0..6; Oracle TIMESTAMP/INTERVAL precision is 0..9, with default 7 preserving CLR
100ns ticks. DATE has precision zero. Explicit supported declarations preserve their storage semantics and
unqualified store defaults (MySQL DATETIME/TIME: 0; Oracle TIMESTAMP/INTERVAL seconds: 6, interval days: 2).
Conflicting explicit/configured precision and unsupported temporal declarations fail clearly. Timezone-less
DateTime values with Local/UTC Kind reject consistently. Oracle nanoseconds not exactly representable in a CLR
value fail on reading.

Column precision and interval leading precision constrain values actually written by SaveChanges, after value
conversion, using the same IsWrite validation contract as numeric/binary mappings. They must not constrain query
comparands or CASE branches. General input parameters and literals preserve at least MySQL microseconds (6 digits)
or Oracle CLR ticks (7 digits); an Oracle declaration at precision 8/9 retains that higher metadata precision.
Oracle interval query transport uses leading precision 9, independently of a narrower column. Output-only
parameters retain declared column precision/leading metadata. MySQL submicrosecond values and durations beyond
the native TIME range still reject as transport limits. No implicit rounding/truncation is introduced.

For a DateTime mapped to Oracle DATE, query parameters use timestamp transport and literals use TIMESTAMP so
fractional comparands remain exact; actual DATE writes still reject fractions. DateOnly retains midnight DATE
transport/literals. An input-output parameter uses query transport because its input must remain exact. Parameter
and literal generation cannot infer that a value is an actual column write; only the update batch can.

The connector 0.2.0 published revision accepts DateTime/DateTimeOffset/TimeSpan for MySQL/Oracle timestamp/TIME
paths as applicable, but DateOnly/TimeOnly need converters. MySQL does not accept DbType.DateTimeOffset;
Oracle does not accept DbType.Time. Oracle intervals require OceanBaseIntervalDayToSecond values plus the
public IntervalDayToSecond parameter type. Provider code must not call internal codecs or change the driver.
Oracle timestamp/zoned result wrappers require exact public conversions on both query and generated-output paths.

Interval model-to-provider converters create a lossless intermediate value at the connector's maximum precision.
The mapping validates its current declared precision at the write boundary; a converter
must not capture a precision that becomes stale when the mapping is cloned. A precision clone updates its SQL
store declaration and output parameter Scale together, while preserving converters and reader hooks. Input
parameter Scale remains wide enough for query values even after a clone reduces column precision.

Acceptance cases include all five CLR types in both modes, default/explicit precision, nulls and boundary values,
UTC normalization/offset retention, negative and multi-day durations, sub-tick rejection, parameter/literal
consistency, mapping clones/converters and sync/async EF materialization/writes. Server-specific calendar ranges,
session behavior and actual wire round trips remain live gates; local parameter acceptance is not proof of server
acceptance. Zone identity/BC dates/zero dates/LTZ and native nanosecond CLR types are outside this scalar profile.

## Oracle Boolean search conditions

EF Core Relational 10.0.0 exposes the scoped `IRelationalParameterBasedSqlProcessorFactory` service. Its `Create`
method receives `RelationalParameterBasedSqlProcessorParameters`; the processor constructor receives dependencies
and those parameters. Override `Process(Expression, ParametersCacheDecorator)`, call base processing first, then
convert the Oracle expression tree. The obsolete `Optimize` API is not used.

Oracle NUMBER(1) values and SQL conditions have different contexts. Predicates/HAVING/join conditions and searched
CASE tests require conditions; projected/ordered/grouped values and comparison operands require scalar values.
Comparisons, logical operators, EXISTS, IN, LIKE and null tests are conditions. Scalar Boolean predicates compare
against 1. Projected conditions use `CASE WHEN condition THEN 1 WHEN NOT condition THEN 0 ELSE NULL END`, preserving
UNKNOWN for nullable Boolean expressions. MySQL retains base processing and numeric Boolean SQL semantics.

The nullable-negation regression uses an actual nullable Boolean expression. In contrast, casting the C# result
of `nullableInt > 0` to bool? does not request lifted-to-null comparison semantics: that CLR comparison returns
false when the operand is null. Tests must preserve that difference.

Verified references: [EF10 processor](https://github.com/dotnet/efcore/blob/v10.0.0/src/EFCore.Relational/Query/RelationalParameterBasedSqlProcessor.cs)
and [SQL Server's context-conversion example](https://github.com/dotnet/efcore/blob/v10.0.0/src/EFCore.SqlServer/Query/Internal/SearchConditionConverter.cs).
The provider has no SQL Server package dependency and does not use its bitwise optimizations. Eight local SQL
cases cover numeric predicates, comparisons/NOT/EXISTS/LIKE projections, nesting, CASE tests and nullable NOT.
These cases do not close the complete Boolean/null-semantics or live query gate.

## Test entry points

The provider follows the EF Core provider-writing guidance:

- provider unit tests for options, mode selection and SQL fragments;
- selected Microsoft.EntityFrameworkCore.Relational.Specification.Tests classes with provider fixtures;
- SQL assertions for mode-specific SQL where result-only tests would miss a dialect error;
- package-consumer tests using packed provider and connector packages;
- live MySQL and Oracle tests when the server and permissions are available.

The first vertical slice should prove model creation, a scalar query, insert with generated key, update, delete,
transaction rollback and both sync/async paths in each mode. A passing unit test without a valid relational
command or reader path is not a provider milestone.

## EF10 feature review

EF10 release notes mention complex-type improvements, LINQ and SQL translation changes, relational JSON updates,
named query filters, ExecuteUpdateAsync changes and security-related logging behavior. These remain deferred until
the first slice is stable and their OceanBase SQL semantics are designed separately.

## Implementation gate

Before changing a version-local EF service, inspect the EF Core 10 assembly/XML documentation and the corresponding
specification-test base class. Record the verified interface, constructor dependencies, service lifetime and test
class in the implementation plan. Do not guess from an EF Core 8 or EF Core 9 signature.
