# String-query native acceptance

Baseline: `d5e8d25`; requirement QRY-06. Continue the agreed inline workflow:
test, fix measured Provider defects, one independent review, then local commit.
No push or publication. Reuse `LiveStorageTable`, User Secrets and Connector
0.3.0 public ADO.NET APIs. Only CREATE-confirmed unique tables are owned.

## Supported overloads and semantics

| API | Existing translation / acceptance domain |
| --- | --- |
| `==`, `!=`, instance/static string Equals without comparison argument | EF NULL compensation and native text comparison/collation; Oracle empty strings become NULL. |
| `+`, string Concat with two/three/four string arguments | Native concat with existing EF NULL handling; Oracle both-empty result is NULL. |
| Contains/StartsWith/EndsWith with one string argument | Literal text search, not LIKE patterns; NULL source/needle is false. Known empty needle matches non-NULL source; computed Oracle NULL needles are false. A suffix must fit in the source; ordinary suffix equality and INSTR can have different native comparison behavior. Oracle LOB suffixes use last-position INSTR. |
| Length | Native character length, including the store's fixed-padding behavior; not promised to count UTF-16 units. |
| Substring(int), Substring(int,int) | Zero-based input adjusted by one with long arithmetic; native substring range/negative/zero-length semantics, not CLR argument exceptions. |
| ToLower(), ToUpper() | Native case conversion and NULL results; no CLR-culture promise. |
| Trim(), TrimStart(), TrimEnd() | Native ordinary-space trim; other whitespace is preserved. Oracle all-space scalar results are NULL; CLOB results can be non-NULL empty LOBs. |
| StringComparison/culture/Invariant/char search/explicit trim chars/Remove | Not part of the translated profile; predicates must reject, permitted final client projections remain EF-owned. |

Conversions operate in stored-value space, with one inverse result conversion.
Do not claim arbitrary converters preserve text operations. Do not infer NCLOB
support from CLOB; the target's prior NCLOB DDL returns 902.

## Implementation and verification

1. Add serial native cases covering both modes, sync/async and default/relational
   NULL semantics. Use independent expected strings/characters, including NULL,
   empty, spaces/control/Unicode/supplementary characters, quote/backslash/%/_,
   current captured values, column/computed operands, both substring arities,
   constant/captured/column bounds and explicit rejection/recovery.
2. Cover ordinary/national variable/fixed text and ordinary LOB functions beyond
   4000 characters. Record actual collation/charset-dependent observations. Use
   same-SQL public Connector controls before assigning a server/reader failure.
3. A production fix requires a local failing test and smallest shared correction;
   rerun its callers and both-patch full/package gates. Test/documentation-only
   evidence may retain explicitly historical unchanged-production checks.
4. One fresh independent review and one executor fix pass for material findings;
   ledger minors and declined scopes. Final credential/LF/whitespace audit,
   source snapshot and dependency checks, exact-file local commit.
5. Update feature/gap/progress records with mode-specific facts; close QRY-06
   only when its supported profile is established. Keep unrelated mapping,
   full charset/collation/version and driver requirements in their owning rows.

## Review focus

- Captured empty and computed NULL search needles must remain distinct in Oracle.
- Source/needle NULL guards and negation must hold in predicates and projections.
- Supplementary text and negative/zero/large substring bounds need actual results.
- Fixed/national/LOB result mappings must neither truncate nor change charset.
- Unsupported overload tests must force translation rather than accidentally pass
  through a final client projection or an empty source.

## Results

The 64 native cases cover both modes, sync/async and default/relational NULL
semantics across eight families. The seed-fixed baseline passed 52/64: four
MySQL longer-suffix false positives, four Oracle CLOB suffix errors (932), and
four grouping expectation errors under the actual MySQL collation. The latter
were test corrections, not Provider defects. An earlier seed used empty RAW
through a binary converter and was corrected before Oracle query execution.

Direct public Connector controls reproduce PAD SPACE's longer-suffix false
positive and Oracle's invalid CLOB equality. The shared suffix builder now
requires source length >= needle length. Oracle CLOB/NCLOB mapping families
use INSTR(source, needle, -1) and the terminal position instead of LOB equality;
the length guard also prevents an absent longer needle from matching zero=zero.
Three local regressions were observed failing before this production change;
all 29 string-search unit cases subsequently passed.

Native expectations retain measured server semantics. The tested MySQL ordinary
column uses utf8mb4_unicode_ci: equality equates NBSP/em-space and ordinary space,
but INSTR does not; grouping merges ASCII case. The fixture accepts that profile
or a binary collation, not arbitrary uncharacterized collations. Oracle ordinary
comparison is case-sensitive. CLOB TRIM(all spaces) and beyond-end SUBSTR produce
non-NULL empty LOBs, while zero-length SUBSTR produces NULL; direct controls and
native result/null/length tests establish those distinctions. NCLOB remains
unaccepted on this target; no CLOB fallback was introduced.

The final targeted root native run passes 64/64. Both EF10.0.0 and isolated EF10.0.14 full Release suites pass6655 with16 existing
specification skips each. The root full LiveTests run is330/331: an existing
Oracle correlated-query case encounters a remote socket reset(code0). Its full
family and the final strengthened LOB family pass16/16 on targeted recovery;
the original failure remains recorded. The compatibility full run passes331/331
(321 live,10 offline profiles), including the final >4000-character Lower/Trim/
Substring assertions. No production change was made for the transient reset.

Package10.0.0-string-query-r1-20261117 passes archive/dependency checks, both-mode
consumers on both patches, and source/archive/consumer DLL plus README identity.
One independent read-only review reports0 Critical/0 Important/1 Minor: qualify
Oracle all-space Trim NULL behavior as scalar, because CLOB returns an empty
LOB. The plan/README now distinguish it. The reviewer declines broader charset/
collation/version/full-capacity/performance, arbitrary converter equivalence and
NCLOB deployment support; the executor retains those in owning rows. No second
review was requested. Final read-only catalog checks find zero tagged diagnostic
tables in both modes. Credential/LF/whitespace and snapshot audits pass.

QRY-06 is Done in both modes for this documented profile. Wider MAP/environment
requirements and the global EF10 objective remain incomplete. Commit locally;
no push/publication.
