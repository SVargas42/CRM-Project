# Analytics v2 Step 1 plan

1. Extend only the bounded snapshot projections inside its existing single ReadOperation. Add RecentContacts (100, most recent note first, contact ID tie-break) and ContactSearch (500, last name then first name COLLATE NOCASE, then ID); count each with an identical scoped SQL predicate. Keep SkillRoster and all existing scope/lifecycle rules unchanged. Project every added text field with SQL substr(...,1,512); phone/email are the existing normalized newline-delimited column text.
2. Extend existing row records with optional trailing fields so current UI/test constructors still compile without UI edits. Reuse the contact projection for LastContacted, RecentContacts and ContactSearch. Extend roster/member projections, release both new lists/totals through the existing shared snapshot dispatcher, and preserve all partial/final failed-read release paths.
3. Extend shared service fixtures for field correctness, restricted Viewer redaction, sort/caps/true totals, tags-only/skills-only selection, truncation and one AccessVerified. A test-project-only MSBuild source copy will insert an observation call before Movement, after both new lists and extended members are populated. Capture that reference and force actual late projection, audit append and commit/checkpoint failures, proving it was populated, cleared and never returned. No hook or conditional hook is added to production source or assemblies.
4. Build once, then run only --phase2-security, the EF policy runner and --token-selection; allow at most two fix cycles on failures. Document results, query count, changed files and remaining limits. No commits, remotes, schema/package changes or Analytics table runner.

Starting state: analytics-test, clean working tree. User clarification: ContactSearch orders by case-insensitive last name, then first name, then contact ID; direct observation of partial cleanup is required for projection and audit/commit failure paths.

## Implementation and verification

Implemented on analytics-test. No UI source change was needed: existing record constructor arguments remain valid because added fields are optional trailing parameters. SkillRoster type, predicate, projection, cap and ID ordering are unchanged. Existing scope predicates, contact/organization lifecycle meanings, movement threshold, note timestamp rules and lazy-load/cleanup paths are unchanged.

### Projections and scope

| List | Projection changes / selection | Scope and total | Cap / ordering |
|---|---|---|---|
| LastContacted | Adds phone, email, skills, tags; each SQL-truncated to 512 | Existing c scope in WHERE; last note correlated to authorized c.id; organization-name scope remains in membership LEFT JOIN ON; existing independent total | 500; never-contacted first, then oldest note, ID |
| RecentContacts (new) | Same complete contact row; requires EXISTS note for c.id | Same c scope and organization-name LEFT JOIN scope; separate count using identical EXISTS predicate | 100; last note descending, contact ID ascending |
| ContactSearch (new) | Same complete contact row; trimmed/coalesced skills OR tags nonempty, so tags-only and skills-only are included | Same c scope and organization-name LEFT JOIN scope; separate count using identical nonempty predicate | 500; c.last_name COLLATE NOCASE, c.first_name COLLATE NOCASE, c.id ascending |
| OrganizationRoster | Adds phone, email, services, tags; each SQL-truncated to 512 | Existing organization root JOIN ON scope; member c scope stays in LEFT JOIN ON with Active/unarchived predicate; true total/member count unchanged | 500; existing organization name/ID ordering |
| OrganizationMembers | Adds contact phone, email, skills, tags (each 512) and nullable last-note timestamp | Existing c WHERE scope and o JOIN ON scope; notes correlated to c.id; Active membership predicate and separate true total unchanged | 1000; organization ID/contact ID ordering |

All returned names, joined organization names and other free-text projections remain bounded to 512 in SQL. Status/archived/date/ID fields retain their typed meaning. Only the authorized organization's own fields appear in its roster; contact-side organization names still come from authorized unarchived organizations through Active memberships. Hidden organization names do not remove a visible contact. Inputs and limits are not changed: phone/email remain the saved normalized newline-delimited text, and truncation is a read projection, never a save/edit representation.

### Query and audit count

**67 application SQL queries per successful snapshot**, up from 63: the two new lists each add one independent count and one capped detail query. Preflight (1), lifecycle totals (2), period queries (48), existing detail/count pairs (12), and new detail/count pairs (4) all execute inside the same ReadOperation through ScopedCommand. No window functions or additional protected reads were introduced.

Both shared runners verified that a direct public Analytics() call adds **exactly one AccessVerified**, including the capped fixture with 506 matching recent/search contacts. The unchanged real UI path in --token-selection observed **5 events for first load and 5 for Refresh**, retaining its surrounding epoch/access checks.

### Direct failed-read cleanup proof

AnalyticsSnapshot.Release clears all 11 lists and all 8 long totals. CuiMemoryPolicy.Release already dispatches to that method; its dispatch is preserved and documented to include the new lists. The function-level finally handles projection failures, and existing ReadOperation handles audit/commit failures after the function has returned its staged snapshot.

`tests/Shared/AnalyticsSnapshotProbe.targets` is imported only by the two isolated policy-runner projects. An SDK Roslyn task copies the production Analytics source into each test project's obj directory and inserts one observation call immediately before Movement, after RecentContacts, ContactSearch and extended OrganizationMembers have populated. The build rejects a missing/ambiguous insertion boundary. Test compile items replace the original linked file with that copy. The callback implementation exists only in tests/Shared/AnalyticsSnapshotProbe.cs; no hook, DEBUG hook or callback exists in production source.

Verified both generated test copies are exactly the original source after removing the single injected call. Source/project searches and built Core/Desktop binary metadata-string searches found no production probe reference. Generated copies stay under obj and are not source deliverables. This instrumentation observes the same release/audit implementation rather than substituting it.

Each forced failure first observes RecentContacts.Count=100/total=506, ContactSearch.Count=500/total=506, and a nonempty members list, then proves every list and long total in that same captured snapshot is empty/zero, the caller's result remains null, and the exception is neutral with no inner exception:

- Late projection: a malformed employment date reaches Movement only after the observation point; the service finally releases the partial snapshot.
- Audit append: a fixture trigger rejects the final AccessVerified insert; ReadOperation releases the completed snapshot.
- Commit prepare: the existing memory checkpoint rejects preparation; the completed snapshot is released and not returned.
- Commit confirmation: the existing checkpoint rejects confirmation after database commit; the snapshot is still released and not returned. A subsequent successful read verifies that trusted-checkpoint recovery remains intact.

The standalone shared Release assertion is retained alongside these four directly observed failure assertions. The probe and captured references are reset/released in finally blocks. Record contents are not printed in exceptions, logs or test messages. As with existing immutable records, clearing lists drops owned references; this is not a claim of whole-process immutable-string erasure.

### Verification

Commands used Debug, --no-restore; runners used --no-build. No restore/feed/remote access occurred.

| Check | Result |
|---|---|
| Initial `dotnet build HippoCrm.slnx --configuration Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false -v:quiet` | PASS: 0 errors, 2 existing ImageSharp license warnings; 28.76 seconds |
| Initial --phase2-security and EF policy runs | Both stopped at the new ContactSearch capped-order assertion, AnalyticsChecks.cs:238 at that time. The test incorrectly reused the restricted Viewer's first rows for an Administrator; an additional authorized fixture had an empty last name and correctly sorted first. |
| Fix cycle 1, same build command | Corrected only the Administrator expected ID ordering and added an explanatory comment. No service/production change. PASS: 0 errors, same 2 warnings; 10.02 seconds |
| --phase2-security rerun | PASS: **361** Phase 2 and adjacent adapter checks |
| EF policy runner rerun | PASS: **339** real EF/in-memory policy checks, plus **20** organization lifecycle checks |
| --token-selection, once | PASS: **33** focused checks plus existing workspace and Analytics checks; first load/Refresh audit counts 5/5. Ran before the test-only sort-expectation correction; production/UI code was unchanged afterward. |
| Test-copy equivalence / production probe absence | PASS, as described above |
| Read-only git diff --check | PASS |

The 18 new shared v2 assertions passed on both Windows SQLite adapter and real EF/SQLite memory fixtures. They cover saved fields, name/note ordering, tags-only/skills-only/neither selection, hidden parent/related record redaction across the complete snapshot, active authorized member counts, SQL truncation of long normalized multi-value details and other text, RecentContacts cap 100/true total, ContactSearch cap 500/true total, one audit event, standalone release and all four direct failure proofs. One of the two permitted fix cycles was used; no second fix cycle was needed. No source changes followed the final successful policy reruns; only this review was completed.

### Files changed

| File | Change |
|---|---|
| src/HippoCrm.Core/CrmService.Analytics.cs | Extended row records/projections, two scoped lists/counts, parser reuse, extended Release |
| src/HippoCrm.Core/CuiOwnedData.cs | Comment clarifying that shared snapshot release covers v2 lists; dispatcher behavior unchanged |
| tests/HippoCrm.SecurityChecks/AnalyticsChecks.cs | 18 shared v2 service/security/cleanup assertions and fictional fixtures |
| tests/Shared/AnalyticsSnapshotProbe.cs (new) | Test-only reference capture callback |
| tests/Shared/AnalyticsSnapshotProbe.targets (new) | Test-only build copy/instrumentation, no production import |
| tests/HippoCrm.SecurityChecks/HippoCrm.SecurityChecks.csproj | Link probe and import test-only instrumentation |
| tests/HippoCrm.EfPolicyChecks/HippoCrm.EfPolicyChecks.csproj | Same instrumentation for the real-provider shared tests |
| docs/ANALYTICS_V2_REVIEW.md (new) | Plan, implementation, proof and verification |

### Limits / deferred verification

- The old Part 2 UI and its known retained-cell cleanup failure were neither changed nor claimed fixed. --analytics-tables was explicitly not run. A future UI replacement still needs its own complete presentation cleanup verification.
- Snapshot projections and output caps bound managed results, not all SQL work or intermediates. Correlated max-note/name lookups and exact totals can remain expensive at scale. Existing audit-chain costs and the existing Overview-cache gap remain unchanged.
- No macOS execution, physical adapter-disabled check, Release build, production FIPS/SQLCipher provisioning or user-workspace acceptance was performed. The token runner's existing isolated fictional persistent workspace check ran as part of that runner; new service fixtures were memory-only.
- No schema, package, authorization, scope-string, lazy-load or UI source changes, no other runners, no contacts-routing, no commits, stashes or remote commands. A read-only exploratory rg command using a Windows wildcard path reported an invalid-path error; existing files were inspected without changing scope or retrying the wildcard command.

## Step 2: one scrolling Analytics page

Completed on analytics-test, preserving the uncommitted Step 1 data-layer work. The existing snapshot/hydration, scope, authorization, schema and packages were not changed for Step 2. AnalyticsViewModel's constructor now accepts a profile-opening delegate; its load, final verification, cancellation, epoch, reset and snapshot release code are unchanged.

### Discovery and reused navigation

Before implementation, inspected MainWindowViewModel's ContactsPageViewModel.OpenProfileAsync/ShowProfile and OrganizationsPageViewModel.OpenProfileAsync/ShowProfile, NavigationHydration.NavigateRecordAsync/NavigateAsync, DashboardViewModel, AnalyticsTables.cs, AnalyticsTableView.cs and the existing UI checks. Master Search's profile buttons remain disabled placeholders; no working Master Search cross-page command was found.

Analytics reuses **MainWindowViewModel.NavigateRecordAsync(RecordKind, long, Action<CuiProtectedRecord>)**, which routes to the empty Contacts/Organizations shell and performs the existing protected hydration and final authorization checks. ShellViewModel.OpenAnalyticsProfileAsync is a thin adapter to that method and the existing ShowProfile callbacks; those callbacks are now internal. Existing lookup-specific OpenProfileAsync guards were not loosened. AnalyticsTableViewModel rejects noncurrent/released rows and requires the current snapshot visibility gate before dispatch. The adapter requires Dashboard and CanReadCuiCache. No new routing system or direct detail SQL was added. Real mouse-click checks passed for A, B, C (organization) and D, with the expected record kind/ID arriving at the existing profile route.

### Page behavior

- One vertical ScrollViewer renders A through F in order, with a shared title/UTC loaded time/Refresh/include header, localized loading/error/Reload, thin section dividers, minimal spacing, 15–16 px body/input text and 13–14 px headers/cells. Every table uses the one shared ListBox/Grid template, trimmed cells, accessible row summaries and filtered/loaded/true count text. The shell's CUI banners remain unchanged. An empty named Grid, grdAnalyticsGraphs, is reserved for the next step; no graphs/package were added.
- The header is the lifecycle master switch: off means Active/unarchived contacts and unarchived organizations in all A–F. A/B status filters can only narrow that eligibility. Their non-Active checkboxes are disabled while off and display the exact enabling hint. A/B keep independent search and status state; only A has the validated 1–3650 days threshold, default 30. Searches match fragments or SoundexHelper.GetSoundexCode per word across name/organization/skills/tags. No whole-field Soundex or looser fallback is used.
- C searches contacts' skills/tags and organizations' services/tags. Choices are Contacts (initial), Organizations and Both. Empty search shows all eligible loaded results; a nonempty unmatched search shows the explicit empty message. Matching comma-delimited phrases retain their full multi-word text and kind. Only the displayed matched cell is shortened to 80 characters; the full matched phrases remain in the accessible summary. Organization rows use a dash and n/a in the organization/last-contact cells. Both lists retain their snapshot order, contacts followed by organizations; counts combine the participating loaded lists and true totals.
- D filters the loaded organization choices alphabetically in memory, respecting the master switch. Down opens the list and focuses its first realized row; native arrows move, Enter selects, Escape closes. Selection shows the organization's name/details and only its eligible loaded members. It never fetches more memberships. E/F retain their existing dollar/current-contract and authorized-multiple-placement rules, with the lifecycle master applied to displayed rows. All table totals remain snapshot-wide; filtered counts describe what is currently displayed, not an uncapped database search.

### Removed / replaced

Deleted the three Analytics subtab controls and their selected-index state, obsolete placeholder style, period/detail/comparison UI and formatting, old aggregate cards, shared old contact-status/skill/organization-lifecycle controls and dead roster/skill table owners. Snapshot period data and the Core SkillRoster remain intact for the other/future designs. Replaced the old failing Analytics table runner with single-page checks. Retargeted the still-relevant lazy/cache/refresh/cancel/epoch/lock assertions from subtab switching to in-memory filtering. Removed txtAnalyticsSkill from the text-input register and registered the A/B/C/D inputs.

### Presentation cleanup

The prior retained-cell issue was caused by relying on queued array-index binding updates as containers detached/recycled. AnalyticsCell now directly observes its display row's release notification, clears text synchronously, clears/unsubscribes on detachment and holds only a weak reference to the row. Reattachment reads the current row state. Accessible summaries and cell arrays are explicitly released by their display owners. This is a presentation change; snapshot lifecycle/release logic was not changed.

The replacement checks retain old cell/input/row/native-list references and also walk the live window tree after permission-epoch invalidation and actual session lock. They verify all table models, cells, summaries, counts, inputs, selection and dropdown state are cleared. The live-tree scan covers fixture names, phone/email, skills/tags/services, roles and locations in every section. Fresh epoch load and lock/sign-in show no resurrected search text. Failed hydration leaves no displayed fixture text and only a neutral error. All these assertions pass; they supersede the old UI's unresolved retained-cell result for the replaced page. Immutable/native whole-process erasure is still not claimed.

### Verification

All builds used the same Debug/no-restore solution command documented in Step 1; runners used Debug/no-build/no-restore. Three build cycles total, within the authorized bound:

| Cycle | Result |
|---|---|
| 1 | Build PASS: 0 errors, 2 existing ImageSharp warnings; 20.16 seconds. Five runners passed. Analytics stopped at its arrows/Enter dropdown assertion, AnalyticsTableChecks.cs:114 at that time. |
| 2 | Fixed the dropdown focus target: defer until visible/layout-ready and focus the first ListBoxItem, rather than ListBox itself. Build PASS: 0 errors, same warnings; 19.30 seconds. Analytics PASS: 69 checks, including mouse routing and all cleanup checks. Counts-only diagnostic confirmed selection/member count and focus return to CrmTextBox. |
| 3, final | Expanded the live-tree field coverage and exact count-label assertions on every notification. Build PASS: 0 errors, same warnings; 12.92 seconds. All six runners below passed on this final code. |

| Final runner | Result |
|---|---|
| --analytics-tables (replacement) | **70 PASS**, plus over 100 exact count-label notification assertions |
| --token-selection | **33 PASS** focused checks, plus existing fictional workspace and Analytics hydration/epoch/lock assertions |
| --master-search | **22 PASS**, plus 8 retargeted Analytics presentation assertions |
| --organization-lifecycle | **13 PASS** |
| --phase2-security (SecurityChecks) | **361 PASS** Phase 2 and adjacent checks, including all Step 1 v2 field/scope/failure proofs |
| EF policy runner | **339 PASS**, plus 20 organization lifecycle checks |

Observed audited reads: **5 per Analytics UI load**, token fixture first load/Refresh **5/5**. The full in-memory filter/dropdown/keyboard sequence adds **zero AccessVerified** events. Step 1 remains 67 SQL queries inside one protected snapshot read and one direct AccessVerified. Clicking a profile intentionally invokes the existing separately audited detail-navigation path.

The final Analytics fixture uses normal service calls to create fictional contacts, notes, contracts, placements, lifecycle changes and grants in the existing isolated Debug memory workspace. It covers each section, all lifecycle choices, independent filters, invalid days, fragments/per-word phonetics, tags-only/skills-only contacts, organization services/tags, shortened matched text/full accessible summary, alphabetical/filtered keyboard dropdown, all six tables' Tab/arrow behavior, real mouse profile routing, Viewer redaction, exact count updates, zero read amplification, retained/live-tree cleanup, re-sign-in and failure presentation. No assertion was removed to conceal the prior cleanup failure.

### Step 2 files

| File | Change |
|---|---|
| src/HippoCrm.Desktop/Views/AnalyticsPageView.cs (new) | Compact single scrolling page, inputs, accessible sections, keyboard dropdown and graph reservation |
| src/HippoCrm.Desktop/Views/AnalyticsTableView.cs | Shared tight row template, immediate cell clearing, row activation and keyboard accessibility |
| src/HippoCrm.Desktop/Views/DashboardView.axaml | Replace Analytics subtabs with AnalyticsPageView; remove obsolete placeholder style |
| src/HippoCrm.Desktop/ViewModels/AnalyticsTables.cs | Six table owners, independent A/B filters, shared lifecycle master, matched phrases, organization selection and display cleanup |
| src/HippoCrm.Desktop/ViewModels/AnalyticsViewModel.cs | Optional navigation delegate only; lifecycle body unchanged |
| src/HippoCrm.Desktop/ViewModels/DashboardViewModel.cs | Delegate wiring and obsolete subtab-state removal |
| src/HippoCrm.Desktop/ViewModels/MainWindowViewModel.cs | Thin Shell adapter to existing route; internal visibility for existing ShowProfile callbacks |
| tests/HippoCrm.UiTests/AnalyticsTableChecks.cs | Replacement 70-check runner with retained/live-tree cleanup assertions |
| tests/HippoCrm.UiTests/AnalyticsViewChecks.cs | Retarget subtab assertions to in-memory filters |
| docs/TEXT_INPUT_REGISTER.md | Replace obsolete entry and register the five current inputs |
| docs/ANALYTICS_V2_REVIEW.md | This Step 2 report |

Remaining limits: no macOS execution, physical adapter-disabled operation, production FIPS/Release provisioning, screen-reader acceptance or final human visual review was performed. Tests use Avalonia headless rendering; plain styling is ready for teammate review, not a formal WCAG certification. Snapshot caps still limit loaded results; filters cannot find records beyond those caps. Existing audit cost/Overview-cache limitations remain. Core Step 1 files are still uncommitted as authorized, but were not edited for Step 2. No source changes followed the final successful build/runners. No contacts-routing, commits, stashes or remote commands occurred.
