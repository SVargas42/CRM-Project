# Phase 2b plan

1. Extract the existing migration tokenizer into one internal helper, preserving split/trim/validation/first-spelling and skipped-value behavior. Leave all existing Phase 2a migration checks unchanged. Register new source files in the explicit security adapter project; inspect foundation source registration.
2. Add one internal retained-link synchronizer, called after authorized parent writes in CrmService.SaveRecord and CrmDbContext.SaveAuthorizedChanges, before their existing audit/commit. Preserve EF mappings. Load each required word table once per parent save into an OrdinalIgnoreCase dictionary, clear owned collections in finally blocks, reactivate/insert needed links and inactivate only removed Active links. Preserve word rows and text; no per-word audit events.
3. Add an internal metadata-only consistency method on CrmService using one ReadOperation, exact existing record scopes and 500-row batches; no UI/public API. Compare valid source words with Active links and release temporary data on all paths.
4. Add shared save/security checks and Debug memory/persistent fixture checks, invoked only by the authorized runners. Build once; run security --phase2-security, EF policy, UI --token-selection, --master-search and --organization-lifecycle once each. Do not run contacts routing. Report failures without retry loops.

## Starting state

test-main; no tracked/untracked changes reported at start. Read-only HEAD diff confirms Migration010.sql and SchemaManager.cs match HEAD; Migration010 exists and Initialize accepts version 10. No remote command or stash access.

## Writer inventory (reported before implementation)

- [CrmService.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/CrmService.cs), Save(Contact), Save(Organization), SaveRecord: reflected parameterized parent INSERT/UPDATE. This is the Desktop save path and will own dual-write.
- [CrmDbContext.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/CrmDbContext.cs), SaveAuthorizedChanges / base.SaveChanges: a second production writer bypassing SaveRecord. It will call the same synchronizer in its existing authorized transaction. No EF mapping/query changes are needed.
- [MainWindowViewModel.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Desktop/ViewModels/MainWindowViewModel.cs), ApplyTokenValues (via ApplyTags/ApplySkills/ApplyServices) changes draft properties only; SaveContactAsync/SaveOrganizationAsync use CrmService.Save. Organization/contact editor code-behind and token dialogs delegate to those draft methods. Models expose text-backed properties; no separate SQL writer was found there. Debug workspaces execute the same CrmService/EF code rather than a separate record-save implementation.
- Direct SQL fixture writers bypass SaveRecord deliberately: [SkillsTagsMigrationChecks.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/tests/HippoCrm.SecurityChecks/SkillsTagsMigrationChecks.cs), VersionNineFixture; [CuiCategoryChecks.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/tests/HippoCrm.SecurityChecks/CuiCategoryChecks.cs), VersionEightMigrationChecks; [PhaseTwoSecurityChecks.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/tests/HippoCrm.SecurityChecks/PhaseTwoSecurityChecks.cs), MigrationChecks; [MacosMergeChecks.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/tests/HippoCrm.SecurityChecks/MacosMergeChecks.cs), HeadSevenUpgradeChecks/MacSixUpgradeChecks. These seed legacy text for migration and remain intentional exceptions, covered by backfill. New corruption/consistency fixtures will also deliberately use raw SQL and be reported as such.
- Schema.sql, LegacySchema.sql, VersionSixSchema.sql and Migration004.sql define text columns/defaults; they are not recurring runtime record writers. Existing other migrations change relationships, lifecycle and CUI policy, not skills/tag content. The raw classification/policy tampering statements in policy tests do not write the target text columns. Snapshot-only MasterSearch fixtures and object initializers are in-memory data, not database writes.
- Existing ContactRoutingChecks directly downgrades fixture schema and saves through CrmService, and directly assigns draft text. It remains outside this run as expressly instructed. No application raw SQL content writer beyond SaveRecord and authorized EF was found by the solution search, including Debug code.

## Legacy-fixture handling

- Security Program.MigrationChecks uses a captured version-1 fixture but saves CRM records only in its separately created current-schema workspace; legacy user/audit seeding is raw SQL. No dual-write bypass will be added.
- PhaseTwoSecurityChecks.MigrationChecks (version 6), MacosMergeChecks.HeadSevenUpgradeChecks (version 7), MacSixUpgradeChecks (version 6), and CuiCategoryChecks.VersionEightMigrationChecks (version 8) seed old contact/organization rows through raw SQL, upgrade before CRM read/use, and do not successfully Save a contact/organization before upgrading. Keep these migration fixtures unchanged.
- SkillsTagsMigrationChecks.VersionNineFixture constructs version 9 and uses raw SQL only until Upgrade. Keep every existing migration assertion unchanged.
- ContactRoutingChecks.Run saves in a current workspace before its synthetic version-3 downgrade, then reconstructs Database before further saves. Do not change or execute it in this phase; its independent FIPS/fixture issues are documented separately.
- RemediationChecks.CheckLegacyMigration stamps its SQL-created fixture version 5, writes audit history, then constructs Database to upgrade; no contact/organization Save occurs on that old boundary. Other captured pre-10 contact/organization save sites were NOT FOUND.
- Service saves will require the relational tables; there will be no table-existence skip or legacy fallback that weakens dual-write.

## Compatibility and required behavior

Contact Save retains exactly its pre-2b validation and normalization: total tag text cannot exceed 4,096 characters; the existing distinct/trimmed tag list cannot exceed 32 entries; each tag cannot exceed 64 characters or contain a control character. Original rejection messages and unconditional normalization are restored verbatim. Invalid skills still save unchanged in the text and are omitted from relational links. Defensive invalid-word skipping remains inside the synchronizer; it does not weaken existing parent validation. No validation was removed from Save(Organization), SaveRecord or the authorized EF write path, and no new validation was added to skills, services, organization tags or EF. Reads, catalogs, pickers and master search remain text-backed. SetRecordStatus, SetArchived/Restore and organization services remain outside link synchronization.

## Correction

- Read-only diff against HEAD found only Save(Contact) had loosened validation. Restored its 4,096-character total limit and original error: `Keep contact tags under 4,096 characters.` Restored the combined 32-tag / 64-character / control-character check and original error: `Use up to 32 tags, each with 1–64 characters and no control characters.` Restored unconditional comma-space tag normalization. After correction, the only CrmService difference from HEAD is the dual-write call in SaveRecord.
- Removed the 2b expectation that invalid contact tags save successfully. Added four rejection cases covering count, per-tag length, control characters and total length, checking exact messages and unchanged stored parent fields, text, words, links and audit. Retained the separate invalid-skill preservation/no-link case and the Viewer-hidden mismatch check for both parent scopes.
- Added explicit private-canary redaction assertions for validation/synchronization exceptions, audit fields, captured save/rejection stdout and stderr, and this review document. A hostile fixture trigger deliberately includes a private canary in its error; the synchronizer must suppress it and its inner exception. No canary values are printed or documented.
- Shared parser, dual-write, consistency checking, authorization, rollback and generic link-failure errors are unchanged. ContactRoutingChecks was neither edited nor run; its invalid-tag rejection expectation is valid again.
- Correction verification completed in one build and one run per requested runner, with no failures or fix attempts. Earlier verification below is historical; the correction results immediately below describe the current code.

| Correction verification | Result |
|---|---|
| Debug solution build, same offline/no-restore command as below | PASS: 0 errors, 2 existing ImageSharp license warnings; 13.70 seconds |
| Security `--phase2-security` | PASS: 326 checks |
| EF policy runner | PASS: 304 policy checks plus 20 organization lifecycle checks |
| UI `--token-selection` | PASS: 33 token-selection checks plus both Debug workspace checks |

Both policy runners passed all four restored validation cases, the invalid-skill preservation case, the Viewer-hidden consistency check and all four private-canary redaction checks. The shared dual-write suite now has 33 checks (25 previous checks, with its invalid-text case narrowed to skills, plus four validation and four redaction assertions). Existing Phase 2a migration checks remain unchanged and passed. Stored-parent snapshot comparisons include all current contact fields, source text, word/link state and audit signatures. No source changes followed the correction build; only this document was updated to record results. Master search, organization lifecycle and contacts routing were not separately run during the correction; the first two passed in the initial 2b pass below, while contacts routing remains expressly excluded. No file outside CrmService.cs, TokenDualWriteChecks.cs and this review was edited for the correction.

## Results and limitations

Implemented and compiled. Links for records edited between 2a and 2b stay stale until each record's next save. Loading whole word tables on each save is unbenchmarked, and save-time cost is unbenchmarked. No production workspace was opened or migrated.

## Integrated behavior

- [TokenWords.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/TokenWords.cs), TokenWords.Parse, owns the one comma/CR/LF grammar. It trims, drops empty parts, rejects overlength/control-character words, and deduplicates with OrdinalIgnoreCase retaining the first spelling. Invalid occurrences still invoke the migration's location-only callback. Both parser collections are cleared when iteration finishes or fails. The Phase 2a migration test file and its assertions were left unchanged and passed in both policy runners.
- [TokenLinks.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/TokenLinks.cs), Synchronize/SyncColumn, loads each required word table once per parent save, across all visibility and word-status states. Dictionaries use OrdinalIgnoreCase and ID order; no word identity data is returned. Missing words are inserted with their first spelling. A SQLite UNIQUE error (extended code 2067) reselects the ASCII NOCASE match or propagates failure. Needed links are inserted/reactivated; removed Active links become Inactive. Already-correct links are not rewritten. Word names and statuses are never changed. Dictionaries and desired-ID sets are cleared in finally blocks.
- CrmService.SaveRecord calls Synchronize after its existing parent/assignment write authorization and parent row/ID creation, within the same transaction and before the existing Created/Updated event. CrmDbContext.SaveAuthorizedChanges calls it after base.SaveChanges(false), using the generated ID sidecar, before its existing per-parent audit event and final commit. EF query filters, mappings and authorization are unchanged. Multi-parent EF transactions synchronize each changed parent separately.
- Unexpected synchronization errors are replaced by a generic parent table/record-ID message with no inner exception. The transaction rolls back parent text/revision, new words, partial link changes and audit together. There are no word-level audit events or diagnostic payloads.
- [TokenConsistency.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/TokenConsistency.cs), CrmService.CheckTokenConsistency, is internal. One existing ReadOperation covers contact and organization scans using their exact existing scope strings, ordered 500-row batches and closed batch readers. It checks all authorized retained lifecycle states, including archived parents; lifecycle is not a grant. CompareColumn compares shared-parser word sets against Active link names only after the parent was authorized in that transaction. Results contain only table, record ID, source column and MissingLink/ExtraActiveLink. At most one of each kind is returned per source column, not one entry per word. Temporary batches/sets and failed result collections use finally cleanup and the existing CuiMemoryPolicy.Release pattern; ReadOperation still releases results if audit/commit fails.
- No public API or UI wiring was added. Schema version remains 10. Schema.sql, migrations, packages, EF mappings, SetRecordStatus, SetArchived/Restore, services storage and existing read queries were not changed. Archive/status transitions intentionally do not synchronize links.

## Phase 1 reads and picker confirmation

[CrmService.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/CrmService.cs), TokenChoices, still selects c.tags/c.skills and o.tags/o.services under the existing scopes in one protected read. MainWindowViewModel.LoadTokenChoicesAsync and ApplyTokenValues still use those text-backed catalogs/drafts. MasterSearchViewModel and CuiLookup retain their text projections. No picker, catalog, search, view, banner, keyboard control or accessibility property was changed. New word tables are not a source for picker lists or reports; the new global word reads are private save-time identity resolution and authorized-parent consistency inspection only.

## Initial Phase 2b verification (before correction)

| Command | Result |
|---|---|
| `dotnet build HippoCrm.slnx --configuration Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false -v:quiet` | PASS: 0 errors, 2 existing ImageSharp license warnings; 16.52 seconds |
| `dotnet run --project tests/HippoCrm.SecurityChecks --configuration Debug --no-build --no-restore -- --phase2-security` | PASS: 318 Phase 2 and adjacent adapter checks |
| `dotnet run --project tests/HippoCrm.EfPolicyChecks --configuration Debug --no-build --no-restore` | PASS: 296 policy checks plus 20 organization lifecycle checks |
| `dotnet run --project tests/HippoCrm.UiTests --configuration Debug --no-build --no-restore -- --token-selection` | PASS: 33 token-selection checks plus 2 separate Debug workspace checks |
| `dotnet run --project tests/HippoCrm.UiTests --configuration Debug --no-build --no-restore -- --master-search` | PASS: 22 checks |
| `dotnet run --project tests/HippoCrm.UiTests --configuration Debug --no-build --no-restore -- --organization-lifecycle` | PASS: 13 checks |

In the initial pass, both policy runners passed the 37 existing Phase 2a migration checks unchanged and the 25 original shared dual-write checks. EF additionally passed 4 new EF-write checks. The invalid-tag preservation expectation from that initial pass has been replaced by rejection checks under the Correction section. Other coverage includes new records, source content, removal/reactivation, first spelling, Unicode identity, no redundant writes, shared tags, services exclusion, rollback after partial link changes, unauthorized-save denial, untouched lifecycle links, no-delete protection, metadata-only consistency results, both parent scopes, and a mismatch beyond the first 500-row batch. Checker auditing is asserted to add exactly one protected read event. Existing CUI/CTI, record grants, distribution/recipient and contact lifecycle checks also passed.

[TokenWorkspaceChecks.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/tests/HippoCrm.UiTests/TokenWorkspaceChecks.cs), Run/Verify, is invoked by token selection in Debug. It verifies the same Unicode/shared-tag synchronization in both Database.CreateUiDevelopmentWorkspace and Database.OpenUiDevelopmentWorkspace, with fictional data and ephemeral keys. The persistent fixture uses a unique artifacts/token-workspace-checks/.../UiDevelopment directory; its encrypted fixture files remain test artifacts, with no key export. No production startup gate was modified or bypassed for production workspaces.

All authorized runners exited 0. No fix attempt or rerun was needed. Contacts routing was not run, as instructed. Source code was complete before the sole build; only this review document was updated afterward.

## Files changed

| File | Change |
|---|---|
| [TokenWords.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/TokenWords.cs) (new) | Shared internal parser |
| [TokenLinks.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/TokenLinks.cs) (new) | Transactional retained-link synchronization |
| [TokenConsistency.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/TokenConsistency.cs) (new) | Internal scoped, metadata-only consistency checker |
| [SchemaManager.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/SchemaManager.cs) | Use shared parser without changing migration results |
| [CrmService.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/CrmService.cs) | Save-time synchronization; parent validation restored exactly to HEAD |
| [CrmDbContext.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/CrmDbContext.cs) | Cover authorized EF writer, with unchanged mappings |
| [TokenDualWriteChecks.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/tests/HippoCrm.SecurityChecks/TokenDualWriteChecks.cs) (new) | Shared service/security/consistency checks |
| [PhaseTwoSecurityChecks.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/tests/HippoCrm.SecurityChecks/PhaseTwoSecurityChecks.cs) | Invoke shared checks |
| [Security project](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/tests/HippoCrm.SecurityChecks/HippoCrm.SecurityChecks.csproj) | Explicitly compile three new Core helpers |
| [EF policy project](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/tests/HippoCrm.EfPolicyChecks/HippoCrm.EfPolicyChecks.csproj) | Link shared save checks; existing Core wildcard includes helpers |
| [TokenEfWriteChecks.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/tests/HippoCrm.EfPolicyChecks/TokenEfWriteChecks.cs) (new) | Authorized EF creation/update/rollback checks |
| [EF Program.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/tests/HippoCrm.EfPolicyChecks/Program.cs) | Invoke EF checks |
| [TokenWorkspaceChecks.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/tests/HippoCrm.UiTests/TokenWorkspaceChecks.cs) (new) | Both Debug workspace types |
| [TokenSelectionChecks.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/tests/HippoCrm.UiTests/TokenSelectionChecks.cs) | Invoke Debug workspace checks |
| [This review](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/docs/SKILLS_TAGS_DUAL_WRITE_REVIEW.md) (new) | Plan, writer inventory, fixtures, results and limits |

The foundation project's explicit compile list was inspected: it does not compile SchemaManager, CrmService or CrmDbContext and needs no new helper entry. Core and UI use default source inclusion. No new packages or resources were needed.

## Verification limits and remaining considerations

- macOS execution, physical adapter-disabled operation, release builds, production FIPS/module provisioning and deployment acceptance were not exercised by this Windows Debug pass. The persistent Debug workspace test validates its existing SQLCipher path, not a FIPS certification claim.
- No production records were inspected. There is no automatic repair pass: records edited between phases stay stale until saved again. The checker can identify authorized stale-column metadata without exposing word text; it does not repair anything.
- Whole-word-table memory/cost and total save-time cost are unbenchmarked. Dictionaries intentionally include all words under this approved save-time design and are cleared on exit. Immutable strings and provider copies cannot be claimed to be cryptographically erased; no long-lived word cache was added. Consistency scans use bounded source-row batches, but one exceptionally large field and the total mismatch metadata can still be large.
- The defensive UNIQUE-conflict reselection branch was inspected but not forced by a dedicated race fixture. Saves hold the existing immediate SQLite transaction; Unicode and ASCII word reuse paths were exercised.
- ContactRoutingChecks.Run's invalid-tag rejection assertion matches the restored pre-2b validation again. Its separately documented FIPS/migration fixture issues remain outside this correction. This runner was neither edited nor run.
- A discovery read of a nonexistent Core.Tests project path returned no content; no source from it was used. Only git status and git diff were used. No commit, stash access, branch switch, fetch, push, other remote command or network-dependent restore was performed.
