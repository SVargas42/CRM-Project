# Organization archived-flag lifecycle — Part B review

Branch: test-main. Resumed from five partial UI edits; Part A in [docs/employment-ui-check.md](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/docs/employment-ui-check.md) was not redone or edited during this resumed task. Only permitted read-only git status/git diff commands were used. No stashes, branch switching, commit messages, commits or push.

## Resulting behavior

Organizations display Active when archived=false and Archived when archived=true. Stored status remains unchanged, including restored organizations retaining Inactive. Contacts retain their four stored-status choices and predicates. Organization create/edit forms have no stored-status selector; lookup and Master Search expose Active/Archived, defaulting to Active. The shared Master Search status selector now applies only to contacts.

## Stored-status lifecycle filter inventory and changes

| Location | Organization change |
|---|---|
| [src/HippoCrm.Core/CrmDbContext.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/CrmDbContext.cs) — constructors/OnModelCreating/ReadAuthorizedOrganizations | Independent bOrganizationsArchived selector drives the organization global query filter. Contact enVisibleStatus/filter unchanged; organization classification/CUI/CTI/recipient/grant predicates remain. Column/table mappings unchanged. |
| [src/HippoCrm.Core/CrmService.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/CrmService.cs) — Organizations | archived parameter is the lifecycle selector; stored-status predicate removed. Legacy enStatus argument remains validated for compatibility but does not filter organizations. |
| Same — AllOrganizations and ContactEditor | All unarchived, authorized organizations are choices, including restored Inactive rows. |
| Same — TokenChoices | Organization tags/services use archived=0 independent of contact enStatus. Contact skills/tags predicates unchanged; still one ReadOperation. |
| Same — Dashboard | Organization total uses archived=0 without contact lifecycle parameter; all contact counts/period logic unchanged. |
| Same — AddEmployment | After existing contact/organization authorization, organization eligibility checks archived=0 only. Contact Active/unarchived requirement and date/overlap rules unchanged. |
| Same — Contacts membership query | Membership rows still require link status Active; organization visibility uses archived=0 and existing organization scope. Contact lifecycle filtering unchanged. |
| [src/HippoCrm.Core/CuiLookup.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/CuiLookup.cs) — ContactRecord/LookupPage | Consistent unarchived organization memberships; separate bOrganizationsArchived controls only Master Search's organization projection. Contact archive/status paths remain independent. |
| [src/HippoCrm.Desktop/ViewModels/MainWindowViewModel.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Desktop/ViewModels/MainWindowViewModel.cs) — RefreshListsAsync/OrganizationsPageViewModel | Remove organization RecordStatus path; binary visibility selector loads the matching archived flag. Row/profile lifecycle text derives from flag. |
| Same — LoadMasterSearchAsync/ShellViewModel | Pass organization archive choice independently to existing bounded/scoped LookupPage. |
| [src/HippoCrm.Desktop/ViewModels/MasterSearchViewModel.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Desktop/ViewModels/MasterSearchViewModel.cs) | Organization choices, tags/services and results use selected archive flag; contact status matching remains the same. Organization result labels derive from flag. |
| [src/HippoCrm.Desktop/ViewModels/NavigationHydration.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Desktop/ViewModels/NavigationHydration.cs) | Remove organization status initialization/notification; navigation still resets the archive choice to Active. |
| [src/HippoCrm.Desktop/Views/OrganizationEditorView.axaml](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Desktop/Views/OrganizationEditorView.axaml), EditOrganizationView.axaml | Remove status selectors. |
| [src/HippoCrm.Desktop/Views/OrganizationProfileView.axaml](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Desktop/Views/OrganizationProfileView.axaml), OrganizationsRootView.axaml | Replace raw status/boolean display with Active/Archived lifecycle; lookup has two choices. Linked-contact status controls remain contact-specific. |
| [src/HippoCrm.Desktop/Views/DashboardView.axaml](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Desktop/Views/DashboardView.axaml) | Separate organization Active/Archived filter; label shared enum selector Contact status. |

Security conditions intentionally left as-is: AccessControl.CanAccess/Require; CrmService strContactScope/strOrganizationScope; CuiPolicy category/distribution/recipient predicates; Active grant/session/policy and membership-row states. RecordStatus, SetRecordStatus, Restore/SetArchived implementation, retention triggers, schema/migrations and packages unchanged. No new text input was added.

## Restore and authorization evidence

The EF/service checks confirmed archived=false/status=Inactive after restore; this passes existing retention triggers, appears in default service/EF Active results, employer choices, memberships, catalogs and organization counts. Archived organizations are excluded from Active and selectable under Archived. The Viewer tests show no access to ungranted organizations under either choice, including EF. AddEmployment accepts the restored organization and rejects an archived organization. Four contact status cases remain isolated in SQL and EF.

## Validation — bounded exactly as requested

- **One Debug solution build:** passed, zero errors, two existing ImageSharp license warnings, 15.65 seconds. Command: dotnet build HippoCrm.slnx --configuration Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false -v:quiet.
- **One EF policy runner:** passed. It printed 20 added organization lifecycle checks plus 230 existing/shared EF/in-memory policy checks. Command: dotnet run --project tests/HippoCrm.EfPolicyChecks --configuration Debug --no-build --no-restore.
- **One organization lifecycle UI runner:** failed at its Archived-selection assertion, after six checks passed. Command: dotnet run --project tests/HippoCrm.UiTests --configuration Debug --no-build --no-restore -- --organization-lifecycle.
- **One Master Search runner:** failed at the new Archived-choice assertion after previous checks passed. Command: dotnet run --project tests/HippoCrm.UiTests --configuration Debug --no-build --no-restore -- --master-search.

**One fix attempt applied after the UI failures:** ChangeVisibilityAsync no longer starts a redundant second filtering operation after RefreshRows already scheduled one. The redundant operation canceled the task exposed to awaiting callers. The Master Search check now clears the preceding malformed search term explicitly; Clear filters intentionally does not clear search text. These final two edits have **not been rebuilt or rerun**, because the user limited builds/runners to one invocation. Therefore final compilation and UI acceptance after the fix are **not verified**; the successful build predates those two edits. No retry loop was performed.

Ignored logs: artifacts/organization-lifecycle-build.log, organization-lifecycle-ef.log, organization-lifecycle-views.log, organization-lifecycle-search.log. Legacy ContactRoutingChecks organization assertions were updated to the binary selector but that provisioned-workspace runner was not executed. Native Windows/macOS graphical acceptance and disabled-adapter testing were not performed.

## Review status

Paused for manual review with UI revalidation outstanding. Existing Part A report remains at [docs/employment-ui-check.md](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/docs/employment-ui-check.md). No protected checks, database or stored lifecycle values were loosened or migrated.