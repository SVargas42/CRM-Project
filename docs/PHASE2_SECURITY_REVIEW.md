# Phase 2 remediation review and proposed local commits

Date: November 4, 2026 (America/Los_Angeles). Baseline: `test1-branch`, `a8e9781`. Scope: F03, F06 and F09 only. Status: implemented and tested locally, awaiting explicit approval to commit. Nothing is staged or committed. No fetch, pull, push, or remote synchronization was performed. The user's local-only Git boundary supersedes the repository's older automatic-fetch/push wording.

## Resulting behavior

### F06: explicit CUI authorization and markings

The approved policy uses a CUI-authorized user flag in addition to existing roles and record grants. It introduces no national-security clearance levels. `CuiPolicy` accepts exactly `CUI`; missing, null, differently cased, or unsupported model markings are rejected. Contacts and organizations have a persisted `classification` column with NOT NULL/CHECK constraints. Histories, employment links, contracts and image BLOBs inherit the policy of their owning contact/organization; multi-parent reads require all applicable parent permissions.

Schema version 7 is an additive migration from versions 1–6. Existing contacts/organizations are tagged CUI; **all existing users, including administrators, receive `cui_authorized=0`**. Fresh accounts also default to no CUI authorization. No existing record/grant is removed. Existing signed audit history is preserved and the migration's signed schema event is in the same transaction. If the audit append fails, column additions, status normalization and the version change roll back together. Reopening version 7 repeats no migration. Older application versions that do not support schema 7 must not be used to open an upgraded workspace; switching branches is not a rollback procedure.

`UserAdministrationService.SetCuiAuthorization` is the explicit, authenticated administrator operation for reviewing CUI access. An administrator's account-management authority does not itself confer access to business CUI. The administrator may explicitly authorize their own CUI access; the action records actor, subject and event in the signed audit transaction. Revocation inactivates the user's sessions in that same transaction. Existing account-management metadata APIs remain administrator-only so an administrator with no business-CUI authorization can perform the explicit approval. This phase exposes the audited service API; it does not add a new account-administration screen or infer authorization from a role or a UI tag.

Business reads, token catalogs, choices, aggregates, related-history operations, image ingestion and EF gateways require valid CUI authorization in addition to their existing access rules. A CUI-authorized Viewer remains unable to write even with a writable grant; an Editor still needs the applicable record grant; an Administrator without CUI authorization sees no business records and cannot create them. A record with unknown stored classification is excluded even for administrators. Live identity checks and revocation prevent a retained service/context from restoring revoked access.

Contacts no longer disclose organization IDs through legacy fields or relationship lists when the actor cannot access those organizations. Hidden relationships are preserved server-side when the actor edits other contact fields. EF handles disconnected updates by comparing lifecycle values with live storage, not trusting caller-supplied original values, and does not remove a masked organization reference as a side effect of such an edit.

The main shell, login, lock/error presentation and modal windows carry persistent accessible CUI markings. The shared profile header displays the record's marking. Banner text remains outside scrolling business content, uses local theme resources, and has a screen-reader name. No remote font, asset or network dependency is introduced.

### F03: lifecycle filtering

Contact lookup now has the four validated choices Active, Inactive, Retired and Deceased. Applying filters sends the selected enum into a parameterized service predicate; invalid programmatic enum values are rejected independently by the service. Organization lookup preserves its explicit Any-status option, but its selected status is now also passed to the service. Contact/organization lookup defaults to Active, with a separate archive-visibility filter.

Organization choices and token catalogs default to unarchived Active records. Their APIs accept explicit, validated lifecycle selections where needed. Master Search explicitly requests all four statuses and applies its selected status to the authorized snapshot. Linked-contact history explicitly requests retained statuses instead of accidentally relying on an unfiltered default.

Dashboard totals, recent interactions, weekly activity and follow-up counts use the requested, validated lifecycle as a parameter (Active by default). The archive-count metric intentionally counts authorized archived records across valid retained statuses. CUI and record grants apply to every aggregate. Adding contact history/contract/employment records requires an unarchived Active contact, and adding employment also requires an unarchived Active organization.

### F09: retained archive transitions

Archive/restore commands call `CrmService.SetArchived`, which validates live access and revision and invokes `RetentionStatements.SetBusinessRecordStatus`. The status, archive visibility, revision and signed archive/restore audit event commit together. Audit failure rolls back all of them. Generic service/EF saves cannot toggle archival visibility as a substitute for this command.

- Archiving Active changes status to Inactive.
- Archiving Inactive, Retired or Deceased preserves that status.
- Restoring visibility preserves the retained status; it does not silently reactivate a Retired/Deceased record.
- Activating an archived record is rejected until visibility is explicitly restored.
- Stale revisions are rejected; no database row is deleted.

Existing normal service edits also route their retained status/revision update through the retention helper in the save transaction. EF rejects actual lifecycle changes through generic updates and directs callers to the lifecycle service. Database triggers independently reject an archived Active row. Migration normalizes historical archived Active rows to Inactive and advances their revision to invalidate stale drafts, preserving Retired/Deceased rows.

Archival no longer implicitly saves an in-progress draft. The UI requests that unsaved edits be saved before the separate archive/restore action. Success messages explain retained history and unchanged restoration status.

## Verification

All restores used `NuGet.Config` and the local signed package feed with locked restore enforced. The new EF policy project adds no new package versions or archives: it uses the same pinned EF/SQLite Core/SQLCipher/ImageSharp versions already in LocalPackages. Its new committed lock graph was generated locally and subsequently passed a locked solution restore. Existing committed dependency graphs are unchanged.

| Check | Result |
| --- | --- |
| Locked offline solution restore | All seven projects passed |
| Full Windows Debug solution build | Passed, zero errors; existing missing ImageSharp-license warnings in Core and the isolated EF harness |
| Phase 2 plus adjacent Windows adapter suite | 163 checks passed, including legacy migrations, RBAC/account regressions, CUI combinations, explicit lifecycle filters, denial, revision checks, archive/CUI-audit rollback and row retention |
| Real EF/in-memory policy suite | 94 checks passed, including the shared Phase 2 suite plus actual EF query translation, grants/CUI enforcement, malformed marking rejection, image denial/cleanup, blocked direct/bulk commands and retained hidden relationships |
| Headless view suite | 13 checks passed for four status choices, invalid selection rejection, accessible CUI markings, error presentation and modal windows |
| Foundation conventions/memory | 92 foundation checks plus 9 password/session memory checks passed |
| Phase 1 regression suite | 55 checks passed; unprovisioned startup/workspace access still denied |
| Whitespace review | `git diff --check` passed |

Counts overlap where suites intentionally reuse policy cases; they are not a sum of distinct security assertions.

Reproduction commands (with `DOTNET_CLI_TELEMETRY_OPTOUT=1`, `DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true`, and `NUGET_CERT_REVOCATION_MODE=offline`):

```powershell
dotnet restore HippoCrm.slnx --configfile NuGet.Config --locked-mode --disable-parallel -p:BuildInParallel=false -m:1
dotnet build HippoCrm.slnx --configuration Debug --no-restore -p:UseSharedCompilation=false -p:BuildInParallel=false -m:1
dotnet run --project tests/HippoCrm.SecurityChecks/HippoCrm.SecurityChecks.csproj --configuration Debug --no-build --no-restore -- --phase2-security
dotnet run --project tests/HippoCrm.EfPolicyChecks/HippoCrm.EfPolicyChecks.csproj --configuration Debug --no-build --no-restore
dotnet run --project tests/HippoCrm.UiTests/HippoCrm.UiTests.csproj --configuration Debug --no-build --no-restore -- --phase2-security
dotnet run --project tests/HippoCrm.FoundationChecks/HippoCrm.FoundationChecks.csproj --configuration Debug --no-build --no-restore
dotnet run --project tests/HippoCrm.Tests/HippoCrm.Tests.csproj --configuration Debug --no-build --no-restore -- --phase1-security
```

The EF harness compiles shared production policy source into a separate TEST executable with a memory-only Database implementation. It cannot construct a disk workspace. It does not reference or alter the production Database/startup factory, inject a production validator, or enable a runtime development switch. Its native SQLCipher in-memory execution tests EF/SQLite behavior, not FIPS validation or encrypted database-file acceptance. The Windows adapter likewise uses fictional in-memory fixtures. Headless controls are hosted by a bare test Application; production CRM startup remains denied, and no real workspace or credentials were accessed.

Not performed: native macOS execution, provisioned positive CRM startup/full UI flows, real OS credential integration, screen-reader/keyboard/contrast acceptance on both native platforms, restricted-account permission-denial execution, or execution with physical network adapters disabled. The Phase 1 provisioning prerequisites remain in force. Legacy positive disk-workspace/UI fixtures will require approved provisioning plus explicit CUI approvals and the new retention workflow before that acceptance run; no bypass was added to make them pass. Existing licensing/FIPS/deployment gates are not waived.

## Proposed local commit batches

No commit command will run until the user explicitly approves this plan. Approval will also cover staging the listed files and inspecting their staged diffs. No remote operation will be attempted. Each batch contains no more than five related files. Integrated checks above validate the complete Phase 2 change set; the ordered batches group its policy, migration, UI and fixture dependencies for review.

### 1. `feat(security): define CUI markings and additive schema migration`

Files: `src/HippoCrm.Core/CuiPolicy.cs`, `src/HippoCrm.Core/Models.cs`, `src/HippoCrm.Core/Schema.sql`, `src/HippoCrm.Core/Migration007.sql`, `src/HippoCrm.Core/HippoCrm.Core.csproj`.

Body: Define the single approved CUI marking, persisted record classification and default-denied user authorization. Add the migration resource that preserves existing records and normalizes historical archive status without reactivating terminal lifecycle states.

### 2. `fix(security): enforce explicit CUI authorization alongside roles`

Files: `src/HippoCrm.Core/SchemaManager.cs`, `src/HippoCrm.Core/AccessControl.cs`, `src/HippoCrm.Core/UserAdministrationService.cs`, `src/HippoCrm.Core/SecurityAudit.cs`.

Body: Advance verified workspaces transactionally to schema 7, preserving signed audit history. Require live CUI authorization alongside record permissions and add an explicit administrator approval/revocation API with same-transaction audit and session revocation.

### 3. `fix(data): enforce lifecycle scopes and audited retention transitions`

Files: `src/HippoCrm.Core/CrmService.cs`, `src/HippoCrm.Core/CrmDbContext.cs`, `src/HippoCrm.Core/RetentionStatements.cs`, `src/HippoCrm.Core/RetentionSchema.cs`.

Body: Parameterize validated lifecycle scopes across lookup, choices, catalogs and aggregates while preserving CUI/grant enforcement. Route archive/status transitions through the revision-checked retention helper, prevent generic-save bypasses, preserve hidden relationships, and roll back when auditing fails.

### 4. `feat(ui): retain accessible CUI banners across main window states`

Files: `src/HippoCrm.Desktop/Views/CuiBanner.axaml`, `src/HippoCrm.Desktop/Views/CuiBanner.axaml.cs`, `src/HippoCrm.Desktop/MainWindow.axaml`, `src/HippoCrm.Desktop/MainWindow.cs`.

Body: Add a reusable local-theme CUI banner and retain it in the authenticated shell, login, lock and error states. Provide visible and screen-reader text without adding an interactive focus stop.

### 5. `feat(ui): mark modal windows and record profiles as CUI`

Files: `src/HippoCrm.Desktop/Views/ArchiveContactDialog.axaml`, `src/HippoCrm.Desktop/Views/ArchiveContactDialog.axaml.cs`, `src/HippoCrm.Desktop/Views/TagSelectionWindow.axaml`, `src/HippoCrm.Desktop/Views/SkillSelectionWindow.axaml`, `src/HippoCrm.Desktop/Views/ProfileHeader.axaml`.

Body: Keep CUI banners and titles on archive/confirmation and token-selection windows. Display the persisted record marking in profile headers and preserve existing dialog commands and local resources.

### 6. `feat(ui): apply lifecycle selection and explicit archive commands`

Files: `src/HippoCrm.Desktop/ViewModels/MainWindowViewModel.cs`, `src/HippoCrm.Desktop/Views/ContactsRootView.axaml`.

Body: Add four validated contact lifecycle choices and pass contact/organization selections into the authorized service. Use explicit retained archive/restore operations, require saved drafts before archival, preserve restoration status, and derive creation permissions from CUI authorization as well as role.

### 7. `test(security): verify CUI migration and retention rollback`

Files: `tests/HippoCrm.SecurityChecks/HippoCrm.SecurityChecks.csproj`, `tests/HippoCrm.SecurityChecks/TestDatabase.cs`, `tests/HippoCrm.SecurityChecks/PhaseTwoSecurityChecks.cs`, `tests/HippoCrm.SecurityChecks/VersionSixSchema.sql`, `tests/HippoCrm.SecurityChecks/Program.cs`.

Body: Add captured version 6 in-memory fixtures and Phase 2 negative cases for default-denied migration, malformed tags, role/grant combinations, all lifecycle states, stale revisions and same-transaction archive/CUI-audit failures. Verify historical rows and signed migration events are retained.

### 8. `test(ui): verify CUI markings and lifecycle controls`

Files: `tests/HippoCrm.SecurityChecks/SecurityPolicyChecks.cs`, `tests/HippoCrm.FoundationChecks/Program.cs`, `tests/HippoCrm.UiTests/Program.cs`, `tests/HippoCrm.UiTests/PhaseTwoViewChecks.cs`.

Body: Explicitly approve fictional CUI fixture users through the audited service so existing RBAC tests retain their purpose. Expand Hungarian/source validation and headless tests for accessible banners and the four status choices, retaining production startup denial.

### 9. `test(data): exercise real EF CUI policies in memory`

Files: `tests/HippoCrm.EfPolicyChecks/HippoCrm.EfPolicyChecks.csproj`, `tests/HippoCrm.EfPolicyChecks/InMemoryDatabase.cs`, `tests/HippoCrm.EfPolicyChecks/Program.cs`, `tests/HippoCrm.EfPolicyChecks/packages.lock.json`, `HippoCrm.slnx`.

Body: Add an isolated memory-only harness for the real EF provider and shared production policy source, using the existing signed offline dependency versions. Exercise translated filters, denied mutations, masked relationship preservation, image authorization/cleanup and revocation without changing production startup composition.

### 10. `docs(security): document Phase 2 policy and acceptance evidence`

Files: `docs/PHASE2_SECURITY_REVIEW.md`, `MacPortInfo.txt`.

Body: Record the approved CUI policy, additive migration behavior, lifecycle/archive semantics, local validation and pending native acceptance. Document the local-only commit boundary and preserve Phase 1 deployment restrictions.
