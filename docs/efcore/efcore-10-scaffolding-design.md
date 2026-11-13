# EF10 Database Reverse Engineering

**Date:** 2026-11-13. **IDs:** DEF-02, CFG-04, MAP-01..07, MOD-01..03, PKG-03/04, TST-01.

## Workflow and approach

Implement the provider's discoverable `IDatabaseModelFactory`, provider configuration code generator and
context-free design-time type mapping services. Reuse EF's relational scaffolding/model/code generation and
the published connector's public `DbConnection.GetSchema` for tables, keys, indexes and foreign keys.
Use Connector 0.3.0's public 23-field Columns collection for column declarations and tri-state generation,
default and update metadata. Do not duplicate the connector's column catalog queries, declaration reconstruction
or default-SQL inference. Unknown behavior remains unknown and produces a warning requiring configuration
review before writes or migration-based schema recreation; it does not prevent scaffolding usable columns.
Unsupported/missing type declarations and known unsupported behavior still fail explicitly.

Connections must specify `ExpectedMode=MySql` or `ExpectedMode=Oracle`; generated UseOceanBase code selects
that explicit mode. Named connection strings are resolved by EF's existing resolver. Caller-opened connections
remain open and caller-owned; factory-opened connections close on success/failure, and string-created connections
are disposed. No schema management opt-in or DDL is involved. No shared mutable last-mode state is permitted.

## Supported metadata

Read visible ordinary tables/views, ordered scalar columns, exact store declarations/nullability/default SQL,
identity/computed metadata, primary/composite/unique keys, ascending/descending ordinary indexes and ordered
foreign keys/delete actions. Preserve quoted identifiers and parameterize namespace values. Default selection
is the current database/schema; explicit schemas and qualified tables add requested namespaces, with schema
and table selections forming a union. Unqualified tables refer to the current namespace. A table selector
accepts plain, double-quoted or backtick-quoted one/two-part identifiers with escaped quote characters.

Do not silently omit unsupported selected column/index/relationship shapes or missing requested objects.
Unknown types, prefix/expression/fulltext/spatial indexes, unrepresentable update actions and inconsistent
catalog rows fail with an actionable error. Cross-selection foreign keys and CHECK constraints need explicit
diagnostics because EF's relational reverse-engineering model cannot preserve them fully. Views are keyless.
The model is a representation of the supported relational profile, not a lossless DDL backup.

CLR carriers use the existing provider scalar contracts; do not infer application Guid/enum/Boolean converters
from arbitrary binary/text/numeric columns. Oracle NUMBER(p,0) uses int for p<=9 and long for p<=18; wider, fractional and unbounded
NUMBER use decimal. This range-safe narrowing applies to ordinary columns as well as identities. Retain explicit store declarations so design-time defaults cannot alter a tenant's
type. The generated model/context must compile and resolve provider services in both modes and selected patches.

## Verification and boundaries

Use deterministic schema/command doubles through the real factory and real EF reverse engineer/code generator;
compile generated package-only contexts and exercise their models/queries/migration differences. Test filtering,
hostile/quoted names, ownership, catalog failures, unsupported shapes and service discovery without a DbContext.
The API contract is sufficient for safe consumption, but 0.3.0 does not supply complete MySQL default metadata
or Oracle identity/ordinary-generation/update metadata for all supported shapes. Missing identity modes/options
prevent claiming complete Oracle identity reconstruction; they are documented scaffold review limits,
not additional defects that Connector must fix beyond the actual server/Java capability.
Native catalog availability, privileges, result carriers and generated
context execution require live validation. Reuse User Secrets only for authorized read-only probes/catalog reads;
the current handshake failure does not excuse unfinished local code. EF8/9 and publication remain postponed.
