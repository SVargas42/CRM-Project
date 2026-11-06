# Employment UI check — Part A (read-only)

Inspected 2026-11-05 before any Part B source edits. Workspace: C:/Users/ellis/Documents/ds3870testGit/CRM-Project. Findings are static code evidence, not a running-workspace inspection. No application, tests, build, Git commands or stash access occurred during Part A.

## 1. Starting state

Branch: **test-main**, read directly from .git/HEAD. HEAD: **c0ca3f657adf9e82a74071b511f59a0b59f66626** (subject: documentation). Tracked working-file content differences: **0**. Staged tree matched HEAD: **yes**. These checks read index entries/HEAD object and compared content hashes with normal CRLF-to-LF normalization; they did not execute Git. Untracked documents are not classified as tracked modifications. No branch switch or stash inspection was performed.

## 2. Employment entry and app navigation

**Employment can be added and ended in the contact editor.** General-purpose editing of an existing record's organization/role/start date or an already-ended interval is **NOT FOUND**.

Reach it through **Contacts -> lookup row's View Profile -> Edit Profile -> Employment tab**. EditContactView embeds ContactEditorView; the Employment tab is visible for a saved contact. A new contact must be saved before history controls become available. The read-only ContactProfileView Employment section displays history and has no add/end inputs. Sources: [ContactsRootView.axaml](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Desktop/Views/ContactsRootView.axaml) (profile/edit content hosts), [MainWindowViewModel.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Desktop/ViewModels/MainWindowViewModel.cs) (ContactsPageViewModel.OpenProfileAsync/BeginEditAsync, bCanEditContactHistory), [EditContactView.axaml](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Desktop/Views/EditContactView.axaml) (vContactEditFields), [ContactProfileView.axaml](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Desktop/Views/ContactProfileView.axaml) (secEmployment).

Controls in [ContactEditorView.axaml](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Desktop/Views/ContactEditorView.axaml), Employment TabItem:

| Control | Purpose / binding |
|---|---|
| cboEmployer | Organization choice, lstEmployers / objEmployer |
| txtEmploymentRole | CrmTextBox, strRole |
| txtEmploymentStart | CrmTextBox, strStartDate, YYYY-MM-DD |
| txtEmploymentEnd | CrmTextBox, optional strEndDate |
| btnAddPlacement | Add placement, cmdAddEmployment |
| lstContactEmployment | Existing EmploymentRowViewModels |
| txtPlacementEndDate | Per-row end date for open-ended placement, EmploymentRowViewModel.strEndDate |
| btnEndPlacement | cmdEnd for an open-ended placement |

The add panel and end controls use bCanEditContactHistory. MainWindowViewModel.cmdAddEmployment parses organization/role/start/optional end and invokes CrmService.AddEmployment, then reloads history. ApplyContactEditorData wraps rows with EndEmploymentAsync commands; EndEmploymentAsync parses the per-row date and calls CrmService.EndEmployment, then reloads. Source: [MainWindowViewModel.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Desktop/ViewModels/MainWindowViewModel.cs) (constructor cmdAddEmployment, ApplyContactEditorData, EndEmploymentAsync, EmploymentRowViewModel).

AddEmployment requires writable contact, readable Active/unarchived organization and an Active/unarchived contact, rejects overlapping inclusive dates and inserts the employment row with an atomic audit event. EndEmployment requires contact write/organization read and updates only an open-ended placement whose start is not after the supplied end. Source: [CrmService.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/CrmService.cs) (AddEmployment, EndEmployment, RequireActiveContact). Therefore the editor does more than display history; it appends and ends placements, while the profile is display-only.

## 3. Organization membership history

SaveRecord for a contact persists selected organization IDs through RetentionStatements.ReplaceContactOrganizations. That method marks the contact's previously Active contact_organizations rows Inactive, then INSERTs/upserts each selected distinct pair as Active. Rows are retained, not deleted; reselecting the same pair reactivates the same composite-key row. Sources: [CrmService.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/CrmService.cs) (Save(Contact), SaveRecord), [RetentionStatements.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/RetentionStatements.cs) (ReplaceContactOrganizations).

Membership start/end dates, per-link created_at/updated_at and a dated membership-event table: **NOT FOUND**. The join has contact_id, organization_id and a retained status added by RetentionSchema; its key is the contact/organization pair. Generic contact updates/audit events are not per-link timestamped history. Dated employment is a separate table and is not automatically created when memberships change. Sources: [Schema.sql](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/Schema.sql) (contact_organizations, employment, audit_log), [RetentionSchema.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/RetentionSchema.cs) (Initialize), [SecurityAudit.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/SecurityAudit.cs) (Encode/Append), CrmService.SaveRecord.

## 4. Debug --ui-development workspace and sample data

**Automatic business sample-data seeding: NOT FOUND.** On a new empty Debug workspace the inspected startup code creates schema/security state, not predefined CRM business records:

| Automatically seeded business data | Count in fresh-workspace initialization |
|---|---:|
| Contacts | 0 |
| Organizations | 0 |
| Interactions | 0 |
| Contracts | 0 |
| Employment | 0 |

These are counts of inserts performed by initialization code, not measured counts in an existing user's persistent file. Sources: [DebugUiDevelopment.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Desktop/DebugDevelopment/DebugUiDevelopment.cs) (TryEnable, Workspace, AuthorizeFixtureAdministrator), [UiDevelopmentPersistentWorkspace.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/DebugDevelopment/UiDevelopmentPersistentWorkspace.cs) (Database.OpenUiDevelopmentWorkspace, UiDevelopmentPersistent), [UiDevelopmentDatabase.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/DebugDevelopment/UiDevelopmentDatabase.cs) (Database private constructor, CreateUiDevelopmentWorkspace), [SchemaManager.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/SchemaManager.cs) (Initialize), [Schema.sql](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/Schema.sql).

The default opt-in uses a persistent encrypted UiDevelopment/ui-development.db beneath LocalApplicationData/BigEagleGroup/HippoCrm, so records previously entered by the user can remain. Their actual row counts: **NOT FOUND / not inspected**. The memory factory exists for isolated tests; test fixture records are not automatic application sample data. AuthorizeFixtureAdministrator grants the demo user's CUI access; it does not seed contacts/history. Sources: the Debug classes above and MainWindowViewModel.SignInAsync.

Automatically time-distributed sample interaction dates: **NOT FOUND**. CrmService.AddInteraction records Database.Now (UTC logging time); no Debug seeding loop spreading interactions across days/weeks was found. Predefined sample employment records: **NOT FOUND**. Sources: [CrmService.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/CrmService.cs) (AddInteraction/AddEmployment), [Database.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/Database.cs) (Now), Debug composition classes above.

## 5. Part A completion

This report was written before Part B source changes. Part A changed no source/database/migrations, ran no Git commands and accessed no stash. The later organization UI work is separate from these employment/membership findings.
## Follow-up stop condition: stored-status checks (after Part A)

The user's clarification requires archived-flag lifecycle filtering everywhere and instructs a stop if a security/access path gates organizations on stored Active. No further source changes were made after that clarification; the following restriction was found and left unchanged.

- **Protected EF read restriction:** [CrmDbContext.cs:70](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/CrmDbContext.cs:70), CrmDbContext.OnModelCreating, sets the organization global filter to `objRow.Status == enVisibleStatus && !objRow.Archived && objRow.strClassification == CuiPolicy.strMarking`. Both constructors default enVisibleStatus to RecordStatus.Active (lines 27/29). ReadAuthorizedOrganizations (line 196) queries dbOrganizations without bypassing that global filter, then adds CUI/category/distribution/recipient/record-grant checks and audit. Thus the default protected EF read excludes a restored unarchived/Inactive organization. Changing this would touch the EF filter, which the original task prohibited.
- **Core record authorization itself does not require stored Active:** [AccessControl.cs:49](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/AccessControl.cs:49), CanAccess, accepts stored status IN Active/Inactive/Retired/Deceased; Require delegates to it. [CrmService.cs:15](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/CrmService.cs:15), strOrganizationScope, likewise accepts those four values. Active policy/grant rows are separate security conditions and remain required.
- **Separate employment business precondition:** [CrmService.cs:393](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/CrmService.cs:393), AddEmployment, after Require checks, verifies organizations `id=$id AND archived=0 AND status='Active'`; otherwise it throws Select an active organization. This is a mutation eligibility condition, distinct from the CUI/record-grant scope. It was not loosened.
- **Restore/trigger:** CrmService.SetArchived restores by clearing archived while preserving Inactive. RetentionSchema.Initialize rejects only archived=1 AND status=Active. Therefore restored archived=0/status=Inactive is valid but fails the default EF status filter and the employment precondition above. Restore, stored statuses and triggers remain unchanged.

Pending code already made under the original Part B authorization, before the clarification: removed create/edit organization status selectors; changed organization profile/lookup display to archived-derived Active/Archived (OrganizationEditorView.axaml, EditOrganizationView.axaml, OrganizationProfileView.axaml, OrganizationsRootView.axaml, and display properties/notifications in MainWindowViewModel.cs). No lifecycle queries, access scopes, backend services, EF mappings, schema, migrations, packages or contact controls were changed. Part B is incomplete; compilation/tests have not been run. Stopped for the user's decision on the protected EF filter and the separate employment eligibility check. No Git commands were executed.