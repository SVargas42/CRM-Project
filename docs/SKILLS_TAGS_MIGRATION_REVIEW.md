# Phase 2a plan

1. Add identical additive definitions to Schema.sql and embedded Migration010.sql: skills and tags word tables, contact_skills, contact_tags and organization_tags links, constrained names/statuses, foreign keys and word indexes. No services table or classification columns.
2. Update SchemaManager.Initialize to accept version 10, backfill only on first upgrade/fresh initialization, and append SchemaUpgraded(10) inside the existing transaction. Read at most 500 source rows per batch, close readers before inserts, preserve source text and first case-insensitive spelling. Return only skipped table/record ID/column metadata. RetentionSchema adds no-delete protection, tolerating older fixture schemas where new tables do not yet exist.
3. Extend shared security migration checks and EF runner coverage, register the SQL resource in explicit test projects, and update old assertions of the latest schema version. No application reads, writes, EF mappings, views or packages change. Keep implementation in SchemaManager.cs so foundation explicit source lists need no new production source entry.
4. Build once; run security --phase2-security, EF policy and UI --contacts-routing once each. Record results, changed files, skipped-value metadata and verification limitations below.

## Starting state

test-main; no tracked changes; 8 ahead / 0 behind origin/test-main as of last fetch. No remote commands or stash access.

## Skipped-value report

Production workspace NOT RUN: no production database was opened or migrated. Production skipped counts and locations cannot be determined in this code-only task.

Each successful backfill edge-case fixture skipped **2 values**, with these locations only:

| Table | Record ID | Column |
|---|---:|---|
| contacts | 2 | skills |
| contacts | 2 | skills |

The same fixture was exercised once by each of the two migration runners: 4 rejection occurrences across the two independent test databases, representing the same two deliberately invalid fixture inputs. Fresh empty initialization has **0 skipped values**. The injected-failure fixture rolls back and returns no committed report. A second Initialize returns an empty report and does not repeat the backfill.

`SchemaManager.Initialize` now returns an internal read-only list of `SkippedToken` locations. Existing production callers ignore its return value; this phase adds no runtime reporting UI, log, or text export. Consequently this document is a fixture report, not a production-data inventory. Preserve/review the returned metadata during any separately authorized real-data migration before a later phase uses the relational data. Original invalid text remains untouched.

## Discovery command errors

Four reads targeted nonexistent test filenames (CuiCategoryMigrationChecks.cs, EfPolicyChecks/TestDatabase.cs, EfPolicyChecks/EfTestDatabase.cs, and SecurityChecks/SqliteAdapter.cs). One search targeted a nonexistent CuiMemoryPolicy.cs; the existing policy is partial across other files. Actual fixture implementations are SecurityChecks/TestDatabase.cs, EfPolicyChecks/InMemoryDatabase.cs, and SecurityChecks/WindowsSqliteAdapter.cs. These failed reads were not repeated. The Python WindowsApps alias could not start; that command performed no edits. File edits were made with PowerShell and apply_patch instead.

## Integrated implementation

- Both embedded SQL definitions install skills(id, name, status), tags(id, name, status), contact_skills(contact_id, skill_id, status), contact_tags(contact_id, tag_id, status), and organization_tags(organization_id, tag_id, status). Link keys are composite; foreign keys have no cascading deletes; each link has an index on its word ID.
- Word names are NOT NULL, COLLATE NOCASE and UNIQUE. CHECK constraints require trimmed length 1–64 and exclude commas and all C0/C1 control characters. All five tables use the retained four-status CHECK and default Active. No classification columns are added.
- RetentionSchema creates no-delete triggers for all five tables. It tolerates their absence when the pre-upgrade version-six test adapter initializes retention; the existing retained tables retain their previous required behavior. Normal SchemaManager initialization installs all five before retention.
- Schema.sql stamps fresh databases version 10. Existing versions up to 10 are accepted; older D06 migrations still complete and retain their version-9 audit event. Migration010 is applied idempotently, backfill runs, version 10 is set, and SchemaUpgraded with schema entity ID 10 is appended within the caller's existing single transaction. Version-10 reopening does not backfill or append another upgrade event. A failure rolls back the schema, partial inserts, source state and audit changes together.
- Backfill visits contacts.skills, contacts.tags, then organizations.tags, each ordered by ID in batches of at most 500 rows. Every source batch reader closes before writes. Archived and non-Active parents are included. Empty/NULL sources and empty split values produce no words. The parser splits comma/CR/LF, trims, rejects remaining control characters and overlength values, preserves first spelling under OrdinalIgnoreCase, and creates Active links. It never writes the authoritative source columns.
- Word identity uses bounded scans so Unicode case handling matches the picker rather than relying on SQLite's ASCII-only NOCASE. Managed source lists are cleared in finally blocks. Immutable strings/provider copies are not claimed to be cryptographically zeroized; the migration adds no long-lived CUI cache.
- All insertion-failure messages are replaced with table/record ID/column metadata, without a provider inner exception. Returned skipped entries contain no value text. Neither audit events nor test output contain source values.
- Word tables have comments explicitly prohibiting direct picker/report reads; future visibility must join through authorized parent records. No new application read/write path, EF mapping, picker, master search, view, package, or organization-services table was added.

## Verification — one build and one run per runner

| Command | Result |
|---|---|
| `dotnet build HippoCrm.slnx --configuration Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false -v:quiet` | PASS: 0 errors, 2 existing ImageSharp license warnings; 16.07 seconds |
| `dotnet run --project tests/HippoCrm.SecurityChecks --configuration Debug --no-build --no-restore -- --phase2-security` | PASS: 293 Phase 2 and adjacent adapter checks |
| `dotnet run --project tests/HippoCrm.EfPolicyChecks --configuration Debug --no-build --no-restore` | PASS: 267 policy checks plus 20 organization lifecycle checks |
| `dotnet run --project tests/HippoCrm.UiTests --configuration Debug --no-build --no-restore -- --contacts-routing` | BLOCKED/exit 1: host FIPS startup rejection before routing assertions |

Both migration runners passed the same 37 added checks. They cover fresh/upgraded definition equality, separators/whitespace, case-only and Unicode identity, spelling variants, empty/NULL sources, overlength/control-character skips, archived parents, the second source batch, unchanged source bytes, Active links/counts, status and foreign-key constraints, word-column indexes, no-delete protection, idempotence, upgrade auditing, and redacted rollback after a partial backfill. The version-nine boundary fixture is reconstructed in memory by removing only the five additions and resetting its version; its preexisting signed history is retained. A nullable historical source column is simulated in that fixture only. Production text definitions remain unchanged.

The UI runner failed with this exact message:

```text
System.Security.Cryptography.CryptographicException: The required host security policy is not enabled or could not be verified.
```

Failure origin: [StartupSecurity.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/StartupSecurity.cs:58), `HostFipsValidator.VerifyWindowsPolicy`; called by `VerifyHostPolicy` at line 49. Runner entry: [ContactRoutingChecks.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/tests/HippoCrm.UiTests/ContactRoutingChecks.cs:21), `Run`; dispatch: [UI Program.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/tests/HippoCrm.UiTests/Program.cs:88). This is an environmental prerequisite failure, not a failed schema/routing assertion. No fix attempt or rerun was made; no startup protection was changed.

## Files changed

| File | Change |
|---|---|
| [Schema.sql](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/Schema.sql) | Fresh version 10 and five additive definitions |
| [Migration010.sql](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/Migration010.sql) (new) | Matching additive migration definitions |
| [SchemaManager.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/SchemaManager.cs) | Version 10, transactional backfill and metadata-only skipped report |
| [RetentionSchema.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/RetentionSchema.cs) | Five additional no-delete triggers |
| [Core project](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/HippoCrm.Core.csproj) | Embed Migration010 |
| [SkillsTagsMigrationChecks.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/tests/HippoCrm.SecurityChecks/SkillsTagsMigrationChecks.cs) (new) | Shared migration fixture/assertions |
| [Security project](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/tests/HippoCrm.SecurityChecks/HippoCrm.SecurityChecks.csproj) | Explicit migration resource |
| [EF policy project](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/tests/HippoCrm.EfPolicyChecks/HippoCrm.EfPolicyChecks.csproj) | Link shared migration checks; existing SQL wildcard includes Migration010 |
| [PhaseTwoSecurityChecks.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/tests/HippoCrm.SecurityChecks/PhaseTwoSecurityChecks.cs) | Invoke shared checks; latest-version expectations |
| [MacosMergeChecks.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/tests/HippoCrm.SecurityChecks/MacosMergeChecks.cs) | Latest-version expectations; retain existing D06 event assertions |
| [Security Program.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/tests/HippoCrm.SecurityChecks/Program.cs) | Latest-version expectations |
| [This review](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/docs/SKILLS_TAGS_MIGRATION_REVIEW.md) (new) | Plan, findings, skipped metadata and results |

## Limitations and review notes

- Contact routing UI acceptance remains unverified because host FIPS validation rejected startup. macOS execution and physical network-adapter-disabled acceptance were not performed on this Windows host. No security gate was bypassed.
- No production database was inspected or migrated; production skipped totals remain unknown. The security adapter uses Windows in-memory SQLite; the EF runner uses the bundled native provider in memory. These results do not establish SQLCipher file encryption or FIPS certification.
- Bounded word scans avoid an unbounded in-memory dictionary but can require O(source tokens × distinct words) comparisons. Large-catalog migration duration has not been benchmarked. Source batches are bounded by row count, not total bytes in unusually large legacy fields.
- SQLite NOCASE uniqueness alone does not enforce Unicode OrdinalIgnoreCase identity for arbitrary future direct inserts. This phase's migration performs that comparison itself. A later write phase must preserve it and must build lists through parent authorization.
- The text columns remain authoritative. Subsequent edits intentionally do not synchronize the relational tables in this phase; no consumer may assume they remain current after migration.
- No new production C# file was added. The foundation checks' explicit source list was inspected and requires no addition; the existing SchemaManager/RetentionSchema source registrations remain sufficient. No package or release-manifest workflow was changed.
- Only read-only git status/diff commands were used. No stash access, commit, branch switch, remote command or push occurred. Source changes were complete before the sole build; only this review document was updated afterward.
