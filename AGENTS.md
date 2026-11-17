# Repository collaboration rules

Follow the [repository guidelines](docs/repository-guidelines.md). The Provider consumes only public
Connector APIs. Explain proposed driver changes to protocol, authentication, TLS, pooling or wire codecs
and obtain user authorization before changing the driver.

## Documentation language and size

- These rules apply throughout this repository to EF Core 8, 9, 10, 11 and all future versions. They do
  not reset for a new version, branch or development batch.
- Write repository documentation, including AGENTS.md, in English. Keep existing documents in English
  when updating them; do not introduce parallel Chinese copies or mix languages within a document.
- New documents are allowed for distinct topics with a lasting purpose. **Do not create a document for
  each small feature, test run or commit.**
- Inspect existing topics and entry points before writing. Update the owning document first; do not
  maintain multiple inventories of the same state.
- Put plans, design decisions, test conclusions and review findings in the existing topic or inventory.
  Do not automatically generate `*-plan.md`, module reports, dated logs, session summaries or
  `docs/superpowers/` process documents. Create a standalone process document only when requested.
- Give a new document a clear purpose distinct from existing documents and link it from the index.
  When replacing content, merge or delete superseded documents and repair references in the same change.
  Do not preserve every duplicate by moving it into a new archive directory.
- Keep current contracts, status, unresolved issues, necessary evidence links and reproduction commands.
  Put build logs, TRX files, SQL dumps, temporary probes and packages in ignored `artifacts/`; use Git
  for change history.
- Update existing checkpoints instead of appending long accounts of each session or repeating full
  test totals across documents.
- Use repository-relative paths. Never include machine-specific absolute paths, credentials,
  connection strings or User Secrets values.

## Feature, gap and acceptance inventories for every version

Keep three distinct responsibilities for every EF Core version, reusing that version's existing topics.
Do not automatically generate a set of plans, designs, progress reports and acceptance reports when
starting a version or a small feature. Maintain shared contracts once; record version differences in
the relevant existing topic. EF Core 10 currently uses these three primary inventories:

| Document | Sole responsibility |
| --- | --- |
| [Feature inventory](docs/efcore/efcore-10-feature-matrix.md) | All feature groups, implementation status per mode, scope, source entry points and gap IDs |
| [Gap inventory](docs/efcore/efcore-10-gap-matrix.md) | Individual gaps, development status, scope/priority, owning layer and closure conditions; update the same entry after implementation |
| [Release checklist](docs/efcore/efcore-10-release-checklist.md) | Test, live database, package, platform, review and release gates, with evidence per mode and pending acceptance |

- Update the relevant inventories for small features in every version. Do not add feature plans,
  duplicate gap lists, progress copies or acceptance reports. A distinct lasting design topic may have
  its own document; reference it from the inventories instead of copying its contents.
- Preserve stable feature IDs. A feature group may have multiple detailed gaps. State the counting
  unit: do not mix feature groups, APIs, gap records and acceptance rows, or derive feature completion
  from the number of passing tests.
- **Separate feature implementation from release acceptance.** Record missing live, specification or
  platform evidence in the release checklist. Do not label implemented code as undeveloped because
  evidence is missing, or claim acceptance without that evidence. A fix is not a new feature.
- Every gap needs an explicit state and closure condition. Mark uncertain capabilities as `To confirm`;
  missing tests do not prove missing code. Identify unsupported, deferred and Connector/server-dependent
  capabilities without automatically expanding the current development scope.
- For feature changes, update the affected feature and gap entries. For acceptance runs, update only
  the relevant gates and evidence; do not create another document.

## Verification and commits

Preserve uncommitted changes from the user and other tasks. For documentation-only consolidation,
check scope coverage, state consistency, links, sensitive information, formatting and diffs. Do not
rerun databases or the entire product test suite merely because documentation was renamed.
Do not push or publish without explicit authorization.
