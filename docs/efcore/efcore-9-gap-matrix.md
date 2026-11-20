# EF Core 9 Gap Matrix

Use the [feature matrix](efcore-9-feature-matrix.md) for planned and implemented surface.
Record package, platform, tooling execution and live evidence in the [release checklist](efcore-9-release-checklist.md).

## Porting gaps

The `9P` records track version-local development. Start with P0 to obtain the first Provider build
and representative tests. P1 closes the documented behavior differences. P2 defines optional tooling
scope. These records count work items. A work item can affect several feature groups.

| ID | Related scope | Priority | Owner | State | Closure condition |
| --- | --- | --- | --- | --- | --- |
| 9P-01 | CFG-04 / PKG-01 | P0 | Provider build and CI | Planned | Retarget to `net9.0`. Verify the EF package versions, Connector asset and direct dependency bounds. Align test and consumer projects, tools, package verification and branch workflow. Build against EF 9.0.0 and exercise 9.0.23 on the same packed assembly. |
| 9P-02 | CFG-03 / CFG-04 | P0 | Provider services | Planned | Match options cloning, service registration, model/runtime initialization and cache-key signatures. Verify MySQL, Oracle and design-time DI using the public configuration entry points. |
| 9P-03 | MAP-06 / SQL-02 / QRY-01..08 / QRY-10..11 | P0 | Provider query pipeline | Planned | Port parameter processing, nullability, type mappings, SQL generation, Shaper integration and raw parameters. Preserve cache safety across value-dependent rewrites. Verify reused commands, converters, Oracle Boolean/empty text and MySQL positional binding. Adapt EF10 `ParametersCacheDecorator`/`Process` and type inference to EF9 `Optimize`, captured parameters and `canCache`. |
| 9P-04 | QRY-03 / DEF-04 | P0 | Provider query and server JSON_TABLE | Planned | Verify inline and captured collections, empty and NULL roots, typed converters, collation and repeated execution. Revalidate the bounded signed-integer JSON parameter path from G-21 in both modes. Verify EF9 `TranslateParameterizedCollectionsToConstants`/`TranslateParameterizedCollectionsToParameters` and per-query `EF.Constant`/`EF.Parameter`. The EF10 scalar-list option cannot be assumed. |
| 9P-05 | QRY-09 | P1 | Provider tests and EF API | Planned | Replace named filters with one composed global filter per entity. Prove context isolation, Includes and bulk DML with the filter enabled and with `IgnoreQueryFilters()`. Keep selective disable outside the branch's public feature scope. |
| 9P-06 | DEF-03 | P1 | Provider query and updates | Planned | Use expression-tree bulk setters. Verify translated set values, cache refresh, tags and affected rows for joined and paged updates/deletes in both compatibility modes. |
| 9P-07 | SCH-02 / DEF-01 / DEF-09 | P1 | Provider schema, migrations and seeding | Planned | Check creation, operation annotations, fixture scripts, history rows, retries and seed ordering. Preserve owned-object scope and distinguish DDL failure from an unknown commit outcome. Preserve EF9 `IMigrationsDatabaseLock`, pending-model checks and sync/async seeding callbacks. Include lock release on success, failure and cancellation. |
| 9P-08 | MOD-01..02 / UPD-02 / UPD-04 / DEF-06..07 / DEF-09 | P1 | Provider update and metadata services | Planned | Adapt column modifications, generated value and routine result propagation and concurrency interceptors. Verify keys and shared-token propagation across tracked graphs, splitting, inheritance and model-managed data. |
| 9P-09 | QRY-08 / DEF-08 | P1 | Provider modeling and tooling | Planned | Regenerate and compile EF-version models and both proxy profiles. Exercise split buffering and lazy loading. Verify unchanged single-active-reader and mode-isolation boundaries. |
| 9P-10 | DEF-02 / DEF-04 | P1 | Provider design and Connector metadata | Planned | Round-trip schema and code for compatible keys, defaults, indexes, identity, sequences and scalar JSON. Use public Connector APIs and retain generated C# semantics after compiling. |
| 9P-11 | DEF-08 | P2 | EF tooling and Provider | To confirm | Confirm the EF-major code-generation surface and supported publish profiles. Record a separate capability result, plus publish/runtime evidence for any profile claimed. EF9 introduced experimental NativeAOT and query precompilation. Confirm the branch's narrower scope by compiling and executing generated query artifacts on the declared platform. |

## Inherited boundaries and carry-forward work

These 70 records keep the stable IDs from the EF10 baseline. The linked baseline owns the shared
contract and detailed closure condition. **Carry forward** means the baseline has the implementation
and the target branch must port its applicable code. **Unsupported**, **Blocked** and **Deferred** retain
their scope or dependency boundary. Resolve **To confirm** through version-specific API and code evidence.

| ID | Related features | Capability | Mode / owner | EF9 disposition | Closure and version notes |
| --- | --- | --- | --- | --- | --- |
| G-01 | QRY-07 | DateTime.AddDays(double) | Both modes / Provider | Carry forward | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-02 | QRY-07 | DateTime.AddHours(double) | Both modes / Provider | Carry forward | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-03 | QRY-07 | DateTime.AddMinutes(double) | Both modes / Provider | Carry forward | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-04 | QRY-07 | DateTime.AddSeconds(double) | Both modes / Provider | Carry forward | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-05 | QRY-07 | DateTime.AddMilliseconds(double) | Both modes / Provider | Carry forward | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-06 | QRY-07 | DateTime.AddMicroseconds(double) | Both modes / Provider | Carry forward | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-07 | QRY-07 | DateTime.AddTicks(long) | Both modes / Provider | Carry forward | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-08 | QRY-07 | DateTime.Subtract(DateTime) returning TimeSpan | Both modes / Provider | Carry forward | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-09 | QRY-07 | TimeOnly.Add(TimeSpan) | Both modes / Provider | Carry forward | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-10 | QRY-07 | TimeOnly.AddHours(double) | Both modes / Provider | Carry forward | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-11 | QRY-07 | TimeOnly.AddMinutes(double) | Both modes / Provider | Carry forward | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-12 | QRY-07 | DateTimeOffset Add/Subtract/calendar method families | Both modes / Provider | Carry forward | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-13 | QRY-07 | DateOnly.FromDateTime/ToDateTime; TimeOnly.FromDateTime/FromTimeSpan/ToTimeSpan/IsBetween | Both modes / Provider | Carry forward | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-14 | QRY-06 | Contains(char), StartsWith(char), EndsWith(char) | Both modes / Provider | Carry forward | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-15 | QRY-06 | Explicit trim char/char[] overloads | Both modes / Provider | Carry forward | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-16 | QRY-06 | Remove(int), Remove(int,int) | Both modes / Provider | Carry forward | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-17 | QRY-06 | StringComparison, culture/Invariant overloads | Both modes / Provider + Server | Unsupported | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-18 | QRY-06 | IndexOf(string/char), IndexOf(string/char,int), IndexOf(string/char,int,int) | Both modes / Provider | Carry forward | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-19 | QRY-05/03 | FirstOrDefault/SingleOrDefault/DefaultIfEmpty overloads with explicit scalar defaults | Both modes / Provider | Carry forward | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-20 | QRY-02/03 | Custom comparers for ordering, joins, membership, and set operations | Both modes / Provider + Server | Unsupported | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-21 | QRY-03/DEF-04 | Single JSON parameter translation for signed Int32/Int64 collection roots | Both modes / Provider | Carry forward | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). Validate through 9P-04. |
| G-22 | QRY-03 | Oracle contextual CHAR/NCHAR collection roots | Oracle / Provider | Unsupported | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-23 | QRY-03 | Oracle ordinary BLOB equality/membership (including mixed RAW/BLOB subqueries) | Oracle / Provider + Server | Unsupported | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-24 | QRY-03 | Oracle CLOB/NCLOB/BLOB set projections, MINUS ALL/INTERSECT ALL | Oracle / Server + Provider | Unsupported | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-25 | DEF-03/MAP-07 | Bulk server expressions with incompatible converters | Both modes / Provider | Unsupported | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-26 | UPD-02/MOD-03 | Reading back MySQL default/computed generated primary keys | MySQL / Provider + Server | Unsupported | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-27 | UPD-02/05 | MySQL readback shapes with multiple generated keys | MySQL / Provider + Server | Unsupported | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-28 | DEF-07/06 | TPC store-generated key allocation across tables | Oracle / Provider | Carry forward | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-29 | MOD-03/SCH-02/DEF-01 | Model trigger metadata adaptation | Both modes / Provider | Carry forward | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-30 | SCH-02/DEF-01 | Table/column comment DDL | Both modes / Provider | Carry forward | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-31 | SCH-02/DEF-01 | Explicit database-default collation changes | MySQL / Provider + Server | Carry forward | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-32 | SCH-02/DEF-01 | Oracle column collation DDL | Oracle / Provider + Server | Unsupported | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-33 | SCH-02/DEF-01 | Filtered index DDL | Both modes / Provider + Server | Unsupported | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-34 | DEF-01 | Automatic alteration of identity/computed columns | Both modes / Provider + Server | Blocked | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-35 | DEF-01 | Table migration across namespaces | Both modes / Provider | Unsupported | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-36 | DEF-02 | Reverse engineering of expression/prefix indexes | Both modes / Provider + Connector metadata | Carry forward | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-37 | DEF-02 | Reverse engineering of known ON UPDATE column behavior for supported mappings | MySQL / Provider | Carry forward | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-38 | DEF-02 | Reverse engineering of foreign key ON UPDATE actions | Both modes / Provider + Server | Unsupported | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-39 | QRY-07 | Math/MathF.Acosh | Both modes / Provider | Carry forward | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-40 | TXN-01/DEF-09 | Distributed transactions; bounded single-resource scopes | Both modes / Provider + Connector | Unsupported | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-41 | QRY-07 | DateTimeOffset.Now | Both modes / Provider | Unsupported | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-42 | QRY-07/MAP-05 | MySQL DateTimeOffset.DateTime/Ticks/Offset/TotalOffsetMinutes | MySQL / Provider + Server | Unsupported | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-43 | MOD-03/UPD-03/SCH-02 | Database-generated rowversion/IsRowVersion columns | Both modes / Provider + Server | Unsupported | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-44 | MOD-01/SCH-02/DEF-01 | ON DELETE SET DEFAULT foreign key DDL | Both modes / Provider + Server | Unsupported | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-45 | MOD-03/SCH-02/DEF-01 | Oracle stored computed columns | Oracle / Provider + Server | Unsupported | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-46 | QRY-04 | Oracle CLOB/NCLOB aggregate Min/Max | Oracle / Provider + Server | Unsupported | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-47 | QRY-06 | Replace(string,string), Replace(char,char) | Both modes / Provider | Carry forward | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-48 | QRY-06 | Insert(int,string) | Both modes / Provider | Carry forward | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-49 | QRY-06 | PadLeft(int/int,char), PadRight(int/int,char) | Both modes / Provider | Carry forward | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| G-50 | MAP-05/DEF-02 | MySQL TIMESTAMP mapping and scaffolding | MySQL / Provider | Carry forward | [Baseline scope and closure](efcore-10-gap-matrix.md#provider-gaps-and-current-limitations). |
| F-01 | QRY-07 | DateTime.Add(TimeSpan) | Both modes / Provider | Carry forward | [Baseline scope and closure](efcore-10-gap-matrix.md#implemented-entries). |
| F-02 | QRY-07 | DateTime.Subtract(TimeSpan) | Both modes / Provider | Carry forward | [Baseline scope and closure](efcore-10-gap-matrix.md#implemented-entries). |
| B-01 | MAP-02 | Oracle binary Double transport | Oracle / Connector public parameters + Provider mappings | Carry forward | [Baseline scope and closure](efcore-10-gap-matrix.md#connector--server-dependencies). |
| B-02 | QRY-04 | Wide MySQL SUM/AVG decimal metadata | MySQL / Connector public reader + Provider aggregate shaping | Carry forward | [Baseline scope and closure](efcore-10-gap-matrix.md#connector--server-dependencies). |
| B-03 | QRY-07 | Wide MySQL extrema decimal metadata | MySQL / Connector public reader + Provider numeric translation | Carry forward | [Baseline scope and closure](efcore-10-gap-matrix.md#connector--server-dependencies). |
| B-04 | SQL-02/QRY-10 | Repeated native Input parameter clones | MySQL / Connector public API + EF leases | Carry forward | [Baseline scope and closure](efcore-10-gap-matrix.md#connector--server-dependencies). |
| B-05 | MAP-03 | Oracle NCLOB DDL | Oracle / Server | Blocked | [Baseline scope and closure](efcore-10-gap-matrix.md#connector--server-dependencies). |
| B-06 | MAP-03/04 | MySQL LOB defaults | MySQL / Server | Blocked | [Baseline scope and closure](efcore-10-gap-matrix.md#connector--server-dependencies). |
| B-07 | SQL-01 | Oracle embedded double quote identifiers | Oracle / Server; recheck by version | Blocked | [Baseline scope and closure](efcore-10-gap-matrix.md#connector--server-dependencies). |
| B-08 | QRY-03 | MySQL collection-root collation | MySQL / Provider SQL + Server | Carry forward | [Baseline scope and closure](efcore-10-gap-matrix.md#connector--server-dependencies). Validate through 9P-04. |
| B-09 | SQL-02 | MySQL text command parameter directions and streams | MySQL / Provider + Connector | Unsupported | [Baseline scope and closure](efcore-10-gap-matrix.md#connector--server-dependencies). |
| B-10 | DEF-04 | Oracle scalar JSON binding and reads | Oracle / Connector public API + Provider mappings | Carry forward | [Baseline scope and closure](efcore-10-gap-matrix.md#connector--server-dependencies). |
| C-03 | DEF-02 | Oracle identity catalog definitions | Oracle / Connector public catalog + Provider | Carry forward | [Baseline scope and closure](efcore-10-gap-matrix.md#connector--server-dependencies). |
| C-01 | DEF-08 | Generated-query and NativeAOT profiles | Both modes / Provider and upstream | To confirm | [Baseline scope and closure](efcore-10-gap-matrix.md#capability-investigations). Apply 9P-11 for this branch. |
| C-02 | MAP-05/QRY-07 | Named-zone/DST and explicit Oracle LTZ storage | Both modes / Provider and upstream | Unsupported | [Baseline scope and closure](efcore-10-gap-matrix.md#capability-investigations). |
| C-04 | DEF-08/QRY-08 | Compiled models with both proxy options | Both modes / Provider and upstream | Carry forward | [Baseline scope and closure](efcore-10-gap-matrix.md#capability-investigations). Regenerate with EF9 tools under 9P-09. |
| D-04 | DEF-04 | Structured and partial-document JSON | Provider and dependencies | Deferred | [Baseline scope and closure](efcore-10-gap-matrix.md#four-groups-explicitly-deferred-for-the-current-release). |
| D-05 | DEF-05 | Specialized type mappings | Provider and dependencies | Deferred | [Baseline scope and closure](efcore-10-gap-matrix.md#four-groups-explicitly-deferred-for-the-current-release). |
| D-06 | DEF-06 | Additional procedure and sequence shapes | Provider and dependencies | Deferred | [Baseline scope and closure](efcore-10-gap-matrix.md#four-groups-explicitly-deferred-for-the-current-release). |
| D-10 | DEF-10 | Search and advanced batching | Provider and dependencies | Deferred | [Baseline scope and closure](efcore-10-gap-matrix.md#four-groups-explicitly-deferred-for-the-current-release). |
