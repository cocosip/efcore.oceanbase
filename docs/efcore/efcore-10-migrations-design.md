# EF Core 10 Migrations Core

## Scope and ownership

This module advances DEF-01 as a complete relational migration pipeline, rather than another query overload.
It supplies model differences, operation SQL, history, execution and upgrade/downgrade scripts in both modes.
Reverse engineering, CLI scaffolding/bundles and provider sequence/HiLo strategies remain separate work.
Local evidence does not establish native OceanBase support.

Reuse EF10's model differ, migrations assembly, migrator and command executor, and the provider's existing
type mappings, quoting and generated-key rules. Add provider services only at dialect/ownership boundaries.
Do not add dependencies. Continue on efcore/10.0 with net10.0, EF [10.0.0,11.0.0) and connector 0.2.0.

## SQL contract

Support create/drop/rename tables; add/drop/rename/alter columns; primary, unique, check and foreign-key
constraints; create/drop/rename indexes; literal seed insert/update/delete; and caller-authored SQL.
Preserve nullability, converted defaults, bounded scalar types, supported identity and computed columns.
MySQL uses MODIFY COLUMN and table-qualified index operations; Oracle uses MODIFY and DROP CONSTRAINT.
Reject unsupported annotations, sequence/schema creation/deletion, table transfers, unsupported computed/
identity alterations and unsupported collation/comment/filter shapes rather than emitting another dialect.
EnsureSchema for the explicitly selected, existing namespace is a no-op; it never creates a tenant or user.
DDL commits implicitly. Migration commands and generated scripts must not promise transactional DDL.

## Runtime and history

Database.Migrate/MigrateAsync require EnableOceanBaseSchemaManagement with an already provisioned target.
Reject caller/ambient/enlisted transactions, retrying execution strategies, a different current namespace, and
cross-scope operations/history (including a seed insert's effective model default schema). Automatic replay of
partially committed DDL is unsupported; inspect the schema and history before retrying manually.
Keep the target connection lease through validation and migration; preserve caller-owned connections.
Raw SQL/default/computed expressions are trusted migration-author input, not sandboxed SQL.

Use EF's default history table, honoring relational history options when supplied through EF infrastructure;
a public provider configuration facade and CLI tooling remain separate work. Use quoted identifiers, exact
namespace checks and MySQL's lower_case_table_names rules for catalog table names. Track successful
migrations only after their operations. Use a history-specific lock table with a single committed owner row:
only its owner can remove the row. The claim survives DDL commits, requires no advisory-lock package and
fails clearly if another migrator owns it. The provider's outer execution lease owns cleanup, including history
bootstrap failure and an uncertain claim result, and preserves primary errors if cleanup fails. EF10 disposes
its lock synchronously even in MigrateAsync; the provider defers that disposal to its outer async cleanup.
On process crash or failed cleanup, an administrator must verify that no migrator
is running before removing the stale row. Never expire or steal a live lock automatically. Script execution
requires external serialization; generated scripts do not acquire the runtime lock.

Idempotent scripts guard every command using history, including the history insert/delete itself. MySQL uses
conditional prepared statements without temporary procedures; Oracle uses guarded anonymous blocks and dynamic
DDL. Normal runtime commands must not contain client delimiter directives. Oracle SQL*Plus-style scripts use
SET DEFINE OFF, WHENEVER SQLERROR EXIT SQL.SQLCODE ROLLBACK, slash block separators and a final COMMIT. Rollback
cannot undo preceding DDL. Dynamic CR/LF is encoded with CHR so EF script indentation cannot change string data;
raw PL/SQL terminators are preserved. Unsupported dynamic commands above 32767 UTF-8 bytes reject explicitly.
MySQL raw script delimiters start on a fresh line, including after line comments. Execute MySQL scripts with
autocommit enabled and a client configured to stop on error. Raw commands must be single server statements;
client directives and multi-statement batches are outside this profile.

TPT now uses the shared physical-table key-generation contract in [efcore-10-tpt-design.md](efcore-10-tpt-design.md):
only the declaring root table generates an inherited key, while derived tables receive it. Entity splitting
uses the same physical key rule for required fragment tables; shared rows emit one physical identity and
support EF10 conditional column propagation. See [the splitting contract](efcore-10-splitting-design.md).
TPC, model triggers and unbacked generated scalar columns reject explicitly. Authoring
trusted trigger SQL is supported by the raw-command seam, but is not a model trigger scaffolding feature.
Unknown migration annotations reject rather than silently importing another provider's schema behavior.

## Acceptance

- Both-mode operation tests assert dialect, escaping, defaults, identity/computed metadata and rejection paths.
- Real model differences produce an initial migration and a schema evolution without hand-built SQL.
- Two real EF migrations generate forward, reverse and idempotent scripts; sync/async runtime doubles verify
  history progression, locking, repeat no-op, failure, cancellation and target ownership before DDL.
- Debug/Release and isolated EF10.0.14 suites, same-package consumers, content verification and independent
  source review pass before the module commit.
- Live probes use external User Secrets only. Native DDL and locking remain open until an authorized isolated
  object/cleanup scope and working transport are available.

Sources: EF Core v10.0.0 MigrationsSqlGenerator, HistoryRepository, Migrator and IMigrationsDatabaseLock;
https://learn.microsoft.com/ef/core/managing-schemas/migrations/ and its applying-migrations guidance.
