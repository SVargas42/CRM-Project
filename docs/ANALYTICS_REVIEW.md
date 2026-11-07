# Analytics Part 1 plan

1. Add one scoped, transactional Analytics snapshot read with fixed projections, SQL caps and independent SQL totals. Capture UTC once and compute half-open current/previous windows in C#. Database.Now and AddInteraction write UTC DateTimeOffset "O" text: yyyy-MM-ddTHH:mm:ss.fffffff+00:00. Compare canonical UTC text to identically formatted parameters to preserve full tick precision; reject noncanonical authorized note timestamps neutrally rather than miscompare mixed offsets. YTD uses prior January 1 to the same moment one year earlier.
2. Scope every contact parent and every organization. Organization predicates on contact-side LEFT JOINs belong in ON. Membership names are sorted, distinct and shortened to 512 characters. Movement excludes hidden organizations completely, including from its authorized-placement threshold, per the clarified rule.
3. Stage analytics privately in background WorkWithCancellationAsync; complete existing final live authorization/epoch checks before publishing. Add a dedicated lazy, cancelable presentation owner, local loader/error/Reload, shared loaded-at/Refresh header and cleanup on lock, epoch/stale-lease invalidation and replacement. Overview hydration/cache behavior remains unchanged.
4. Add shared service checks plus focused UI lifecycle checks through the existing authorized runners. Direct Analytics adds exactly one AccessVerified; existing UI authorization probes keep their separate entries. Measure the bounded per-load UI count. Build once, run only the requested four runners, with at most two fix cycles if needed.

## Existing Dashboard cache gap

MainWindowViewModel.objDashboard is a retained cache guarded by CanReadCuiCache. CuiPresentation.InvalidateReadCaches clears token/lookup caches but does not release or drop objDashboard on a permission-epoch change. This task does not change that existing behavior. Analytics uses its own owner and explicit invalidation instead. Sources: MainWindowViewModel.objDashboard/ClearCuiPresentation; CuiPresentation.InvalidateReadCaches/NotifyReadCacheFreshness; DashboardViewModel.Refresh.

## Clarifications applied

- Movement contains only authorized placements. Hidden placements produce no row, name, ID, count or threshold contribution. One visible plus one hidden placement does not qualify a contact for Movement. Visible contacts remain in other contact lists with organization details redacted.
- Exactly one AccessVerified applies to a direct CrmService.Analytics call. The UI's existing WorkWithCancellationAsync and ValidateCuiScopeAsync authorization checks retain their separate auditing; report the observed UI load count and verify it is bounded independently of snapshot query count.

## SECURITY ATTESTATION

All entries below execute inside `CrmService.AnalyticsAt`'s single `ReadOperation`. `Query` and `Count` both use `ScopedCommand`; that helper binds identity parameters but does not supply predicates. Existing CUI/category/distribution/recipient/record scopes are interpolated unchanged. No EF reads, window functions, tags or services are used. There are **63 application query executions per successful snapshot**, followed by the existing read-audit/commit work.

| # / query | Tables read | Contact scope | Organization scope | SQL result cap | True total / aggregation |
|---|---|---|---|---|---|
| 1. Timestamp-format preflight | interactions, contacts | Yes: c in JOIN ON | N/A | 1 scalar | Count authorized notes outside expected width/UTC shape |
| 2. Contact lifecycle totals | contacts | Yes: c in WHERE | N/A | 8 groups | count(*) per status/archive group |
| 3. Organization lifecycle totals | organizations | N/A | Yes: o in root JOIN ON | 2 groups | count(*) per archived flag |
| 4. 24 hours; Active only; Current: notes/contacts | interactions, contacts | Yes: c in JOIN ON | N/A | 1 aggregate row | count(*), count(DISTINCT c.id); parameterized half-open window |
| 5. 24 hours; Active only; Current: organizations | interactions, contacts, contact_organizations, organizations | Yes: c in JOIN ON | Yes: o in LEFT JOIN ON; Active variant adds archived=0 there | 1 scalar | count(DISTINCT o.id), Active memberships; same window/contact variant |
| 6. 24 hours; Active only; Previous: notes/contacts | interactions, contacts | Yes: c in JOIN ON | N/A | 1 aggregate row | count(*), count(DISTINCT c.id); parameterized half-open window |
| 7. 24 hours; Active only; Previous: organizations | interactions, contacts, contact_organizations, organizations | Yes: c in JOIN ON | Yes: o in LEFT JOIN ON; Active variant adds archived=0 there | 1 scalar | count(DISTINCT o.id), Active memberships; same window/contact variant |
| 8. 24 hours; All authorized; Current: notes/contacts | interactions, contacts | Yes: c in JOIN ON | N/A | 1 aggregate row | count(*), count(DISTINCT c.id); parameterized half-open window |
| 9. 24 hours; All authorized; Current: organizations | interactions, contacts, contact_organizations, organizations | Yes: c in JOIN ON | Yes: o in LEFT JOIN ON; Active variant adds archived=0 there | 1 scalar | count(DISTINCT o.id), Active memberships; same window/contact variant |
| 10. 24 hours; All authorized; Previous: notes/contacts | interactions, contacts | Yes: c in JOIN ON | N/A | 1 aggregate row | count(*), count(DISTINCT c.id); parameterized half-open window |
| 11. 24 hours; All authorized; Previous: organizations | interactions, contacts, contact_organizations, organizations | Yes: c in JOIN ON | Yes: o in LEFT JOIN ON; Active variant adds archived=0 there | 1 scalar | count(DISTINCT o.id), Active memberships; same window/contact variant |
| 12. 7 days; Active only; Current: notes/contacts | interactions, contacts | Yes: c in JOIN ON | N/A | 1 aggregate row | count(*), count(DISTINCT c.id); parameterized half-open window |
| 13. 7 days; Active only; Current: organizations | interactions, contacts, contact_organizations, organizations | Yes: c in JOIN ON | Yes: o in LEFT JOIN ON; Active variant adds archived=0 there | 1 scalar | count(DISTINCT o.id), Active memberships; same window/contact variant |
| 14. 7 days; Active only; Previous: notes/contacts | interactions, contacts | Yes: c in JOIN ON | N/A | 1 aggregate row | count(*), count(DISTINCT c.id); parameterized half-open window |
| 15. 7 days; Active only; Previous: organizations | interactions, contacts, contact_organizations, organizations | Yes: c in JOIN ON | Yes: o in LEFT JOIN ON; Active variant adds archived=0 there | 1 scalar | count(DISTINCT o.id), Active memberships; same window/contact variant |
| 16. 7 days; All authorized; Current: notes/contacts | interactions, contacts | Yes: c in JOIN ON | N/A | 1 aggregate row | count(*), count(DISTINCT c.id); parameterized half-open window |
| 17. 7 days; All authorized; Current: organizations | interactions, contacts, contact_organizations, organizations | Yes: c in JOIN ON | Yes: o in LEFT JOIN ON; Active variant adds archived=0 there | 1 scalar | count(DISTINCT o.id), Active memberships; same window/contact variant |
| 18. 7 days; All authorized; Previous: notes/contacts | interactions, contacts | Yes: c in JOIN ON | N/A | 1 aggregate row | count(*), count(DISTINCT c.id); parameterized half-open window |
| 19. 7 days; All authorized; Previous: organizations | interactions, contacts, contact_organizations, organizations | Yes: c in JOIN ON | Yes: o in LEFT JOIN ON; Active variant adds archived=0 there | 1 scalar | count(DISTINCT o.id), Active memberships; same window/contact variant |
| 20. 30 days; Active only; Current: notes/contacts | interactions, contacts | Yes: c in JOIN ON | N/A | 1 aggregate row | count(*), count(DISTINCT c.id); parameterized half-open window |
| 21. 30 days; Active only; Current: organizations | interactions, contacts, contact_organizations, organizations | Yes: c in JOIN ON | Yes: o in LEFT JOIN ON; Active variant adds archived=0 there | 1 scalar | count(DISTINCT o.id), Active memberships; same window/contact variant |
| 22. 30 days; Active only; Previous: notes/contacts | interactions, contacts | Yes: c in JOIN ON | N/A | 1 aggregate row | count(*), count(DISTINCT c.id); parameterized half-open window |
| 23. 30 days; Active only; Previous: organizations | interactions, contacts, contact_organizations, organizations | Yes: c in JOIN ON | Yes: o in LEFT JOIN ON; Active variant adds archived=0 there | 1 scalar | count(DISTINCT o.id), Active memberships; same window/contact variant |
| 24. 30 days; All authorized; Current: notes/contacts | interactions, contacts | Yes: c in JOIN ON | N/A | 1 aggregate row | count(*), count(DISTINCT c.id); parameterized half-open window |
| 25. 30 days; All authorized; Current: organizations | interactions, contacts, contact_organizations, organizations | Yes: c in JOIN ON | Yes: o in LEFT JOIN ON; Active variant adds archived=0 there | 1 scalar | count(DISTINCT o.id), Active memberships; same window/contact variant |
| 26. 30 days; All authorized; Previous: notes/contacts | interactions, contacts | Yes: c in JOIN ON | N/A | 1 aggregate row | count(*), count(DISTINCT c.id); parameterized half-open window |
| 27. 30 days; All authorized; Previous: organizations | interactions, contacts, contact_organizations, organizations | Yes: c in JOIN ON | Yes: o in LEFT JOIN ON; Active variant adds archived=0 there | 1 scalar | count(DISTINCT o.id), Active memberships; same window/contact variant |
| 28. 6 months; Active only; Current: notes/contacts | interactions, contacts | Yes: c in JOIN ON | N/A | 1 aggregate row | count(*), count(DISTINCT c.id); parameterized half-open window |
| 29. 6 months; Active only; Current: organizations | interactions, contacts, contact_organizations, organizations | Yes: c in JOIN ON | Yes: o in LEFT JOIN ON; Active variant adds archived=0 there | 1 scalar | count(DISTINCT o.id), Active memberships; same window/contact variant |
| 30. 6 months; Active only; Previous: notes/contacts | interactions, contacts | Yes: c in JOIN ON | N/A | 1 aggregate row | count(*), count(DISTINCT c.id); parameterized half-open window |
| 31. 6 months; Active only; Previous: organizations | interactions, contacts, contact_organizations, organizations | Yes: c in JOIN ON | Yes: o in LEFT JOIN ON; Active variant adds archived=0 there | 1 scalar | count(DISTINCT o.id), Active memberships; same window/contact variant |
| 32. 6 months; All authorized; Current: notes/contacts | interactions, contacts | Yes: c in JOIN ON | N/A | 1 aggregate row | count(*), count(DISTINCT c.id); parameterized half-open window |
| 33. 6 months; All authorized; Current: organizations | interactions, contacts, contact_organizations, organizations | Yes: c in JOIN ON | Yes: o in LEFT JOIN ON; Active variant adds archived=0 there | 1 scalar | count(DISTINCT o.id), Active memberships; same window/contact variant |
| 34. 6 months; All authorized; Previous: notes/contacts | interactions, contacts | Yes: c in JOIN ON | N/A | 1 aggregate row | count(*), count(DISTINCT c.id); parameterized half-open window |
| 35. 6 months; All authorized; Previous: organizations | interactions, contacts, contact_organizations, organizations | Yes: c in JOIN ON | Yes: o in LEFT JOIN ON; Active variant adds archived=0 there | 1 scalar | count(DISTINCT o.id), Active memberships; same window/contact variant |
| 36. 1 year; Active only; Current: notes/contacts | interactions, contacts | Yes: c in JOIN ON | N/A | 1 aggregate row | count(*), count(DISTINCT c.id); parameterized half-open window |
| 37. 1 year; Active only; Current: organizations | interactions, contacts, contact_organizations, organizations | Yes: c in JOIN ON | Yes: o in LEFT JOIN ON; Active variant adds archived=0 there | 1 scalar | count(DISTINCT o.id), Active memberships; same window/contact variant |
| 38. 1 year; Active only; Previous: notes/contacts | interactions, contacts | Yes: c in JOIN ON | N/A | 1 aggregate row | count(*), count(DISTINCT c.id); parameterized half-open window |
| 39. 1 year; Active only; Previous: organizations | interactions, contacts, contact_organizations, organizations | Yes: c in JOIN ON | Yes: o in LEFT JOIN ON; Active variant adds archived=0 there | 1 scalar | count(DISTINCT o.id), Active memberships; same window/contact variant |
| 40. 1 year; All authorized; Current: notes/contacts | interactions, contacts | Yes: c in JOIN ON | N/A | 1 aggregate row | count(*), count(DISTINCT c.id); parameterized half-open window |
| 41. 1 year; All authorized; Current: organizations | interactions, contacts, contact_organizations, organizations | Yes: c in JOIN ON | Yes: o in LEFT JOIN ON; Active variant adds archived=0 there | 1 scalar | count(DISTINCT o.id), Active memberships; same window/contact variant |
| 42. 1 year; All authorized; Previous: notes/contacts | interactions, contacts | Yes: c in JOIN ON | N/A | 1 aggregate row | count(*), count(DISTINCT c.id); parameterized half-open window |
| 43. 1 year; All authorized; Previous: organizations | interactions, contacts, contact_organizations, organizations | Yes: c in JOIN ON | Yes: o in LEFT JOIN ON; Active variant adds archived=0 there | 1 scalar | count(DISTINCT o.id), Active memberships; same window/contact variant |
| 44. Year to date; Active only; Current: notes/contacts | interactions, contacts | Yes: c in JOIN ON | N/A | 1 aggregate row | count(*), count(DISTINCT c.id); parameterized half-open window |
| 45. Year to date; Active only; Current: organizations | interactions, contacts, contact_organizations, organizations | Yes: c in JOIN ON | Yes: o in LEFT JOIN ON; Active variant adds archived=0 there | 1 scalar | count(DISTINCT o.id), Active memberships; same window/contact variant |
| 46. Year to date; Active only; Previous: notes/contacts | interactions, contacts | Yes: c in JOIN ON | N/A | 1 aggregate row | count(*), count(DISTINCT c.id); parameterized half-open window |
| 47. Year to date; Active only; Previous: organizations | interactions, contacts, contact_organizations, organizations | Yes: c in JOIN ON | Yes: o in LEFT JOIN ON; Active variant adds archived=0 there | 1 scalar | count(DISTINCT o.id), Active memberships; same window/contact variant |
| 48. Year to date; All authorized; Current: notes/contacts | interactions, contacts | Yes: c in JOIN ON | N/A | 1 aggregate row | count(*), count(DISTINCT c.id); parameterized half-open window |
| 49. Year to date; All authorized; Current: organizations | interactions, contacts, contact_organizations, organizations | Yes: c in JOIN ON | Yes: o in LEFT JOIN ON; Active variant adds archived=0 there | 1 scalar | count(DISTINCT o.id), Active memberships; same window/contact variant |
| 50. Year to date; All authorized; Previous: notes/contacts | interactions, contacts | Yes: c in JOIN ON | N/A | 1 aggregate row | count(*), count(DISTINCT c.id); parameterized half-open window |
| 51. Year to date; All authorized; Previous: organizations | interactions, contacts, contact_organizations, organizations | Yes: c in JOIN ON | Yes: o in LEFT JOIN ON; Active variant adds archived=0 there | 1 scalar | count(DISTINCT o.id), Active memberships; same window/contact variant |
| 52. LastContacted total | contacts | Yes: c in WHERE | N/A | 1 scalar | count(*) with identical parent scope |
| 53. LastContacted rows | contacts, interactions, contact_organizations, organizations | Yes: c in WHERE; last-note subquery tied to c.id | Yes: o in membership-name LEFT JOIN ON | 500 | Independent preceding total; max timestamp correlated to authorized c |
| 54. SkillRoster total | contacts | Yes: c in WHERE | N/A | 1 scalar | count(*) with same nonempty-skills predicate |
| 55. SkillRoster rows | contacts, contact_organizations, organizations | Yes: c in WHERE | Yes: o in membership-name LEFT JOIN ON | 500 | Independent preceding total; skills/name projections <=512 |
| 56. LatestContracts total | contacts, contracts | Yes: c in WHERE; k/k2 correlated to c.id | N/A | 1 scalar | count(*) using same greatest(date,id) selection |
| 57. LatestContracts rows | contacts, contracts, contact_organizations, organizations | Yes: c in WHERE; k/k2 correlated to c.id | Yes: o in membership-name LEFT JOIN ON | 500 | Independent preceding total; correlated contract selector LIMIT 1; location <=512 |
| 58. OrganizationRoster total | organizations | N/A | Yes: o in root JOIN ON | 1 scalar | count(*) with same organization scope |
| 59. OrganizationRoster rows | organizations, contact_organizations, contacts | Yes: c in LEFT JOIN ON, also Active/unarchived | Yes: o in root JOIN ON | 500 | Independent preceding total; count(DISTINCT c.id) per organization |
| 60. OrganizationMembers total | contacts, contact_organizations, organizations | Yes: c in WHERE | Yes: o in JOIN ON | 1 scalar | count(*) with identical Active-membership joins |
| 61. OrganizationMembers rows | contacts, contact_organizations, organizations | Yes: c in WHERE | Yes: o in JOIN ON | 1000 | Independent preceding total; both parent scopes |
| 62. Movement total | employment e/e2, contacts, organizations o/o2 | Yes: c in JOIN ON; e and e2 tied to c.id | Yes: o and alias-adjusted o2 in their JOIN ON | 1 scalar | count(*) over same authorized-placement threshold >1 |
| 63. Movement rows | employment e/e2, contacts, organizations o/o2 | Yes: c in JOIN ON; e and e2 tied to c.id | Yes: o and alias-adjusted o2 in their JOIN ON | 500 | Independent preceding total; hidden placements excluded from rows and threshold |

The repeated membership-name subquery on three contact projections selects distinct, sorted authorized unarchived organization names from Active memberships and truncates the joined result to 512 characters. The organization scope is in `ON`; `o.id IS NOT NULL` inside that subquery removes null name rows, never the outer contact. Movement does not project an organization ID, and its threshold counts only visible placements (including authorized archived organizations).

Scalar counts and aggregate groups are SQL-bounded by their shape; each of the six capped detail lists has a separate SQL true total with the same predicates. True totals cover all authorized lifecycle states. Contact Active means status Active and not archived; organization Active means not archived regardless of stored status.

## Verification and changed files

| Verification (one pass; no fixes or reruns) | Result |
|---|---|
| `dotnet build HippoCrm.slnx --configuration Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false -v:quiet` | PASS: 0 errors, 2 existing ImageSharp license warnings; 24.01 seconds |
| Security runner, `--phase2-security`, Debug/no-build/no-restore | PASS: 343 Phase 2 and adjacent adapter checks, including 17 new shared Analytics assertions |
| EF policy runner, Debug/no-build/no-restore | PASS: 321 policy checks plus 20 organization lifecycle checks; includes the same 17 Analytics service assertions |
| UI runner, `--token-selection`, Debug/no-build/no-restore | PASS: 33 existing selection checks, 2 existing workspace checks, 7 new Analytics workspace assertions |
| UI runner, `--master-search`, Debug/no-build/no-restore | PASS: 22 existing search checks plus 8 new Analytics presentation assertions |
| Read-only `git diff --check` | PASS (existing line-ending conversion notices only) |

Observed audit cost: direct `CrmService.Analytics()` adds **1 AccessVerified**; real first UI load adds **5**, and Refresh adds **5**. Analytics executes the same 63 application queries in each case without per-query audit entries. UI cost is Analytics (1), WorkWithCancellationAsync's post-work epoch read (1), and final ValidateCuiScopeAsync's epoch/access/epoch reads (3). The UI fixture asserts a bounded 5–12 events to allow surrounding freshness verification; subtab switches add zero. These assertions preserve every existing probe. Audit payloads are existing defined event metadata, never snapshot contents.

Shared fixtures verify never-contacted ordering; multiple memberships without duplicate contacts; Active/all-authorized windows; full tick boundary precision; previous YTD and calendar month start; greatest contract date/ID; Inactive contact exclusion from active roster counts; archived organization flags; Viewer record scopes; hidden organization redaction in all contact lists; one visible plus one hidden placement excluded from Movement; every list cap with true totals; neutral missing-CUI denial and late projection failure; Release; and independent CTI policy denial despite record grants. UI checks cover lazy loading, subtab cache reuse, Refresh/replacement, private staging, disabled Refresh during loading, cancellation/late-result release, local neutral errors, denied presentation, actual epoch invalidation and actual session-lock release.

## Ownership and publication

- `AnalyticsSnapshot.Release` clears all nine lists and zeroes six true totals. `CuiMemoryPolicy.Release` dispatches to it. AnalyticsAt's finally releases partial results if a query/projection fails. Existing ReadOperation releases the completed snapshot if audit/commit fails. This removes owned references; immutable strings and framework/native copies cannot be claimed to have been overwritten.
- `AnalyticsHydration.LoadAnalyticsAsync` captures session/lease/service identity, uses existing WorkWithCancellationAsync (whose SQL work runs in Task.Run), performs final ValidateCuiScopeAsync, then checks cancellation, identities, local/database epochs, live lease and cache authorization before transferring ownership. Its finally releases every untransferred snapshot. No global busy workflow is used.
- `AnalyticsViewModel` holds no published snapshot while loading. Epoch-gated getters hide stale results immediately; permission notifications cancel and clear on the presentation context. A revision plus linked cancellation prevents a superseded result from publishing and releases late results. Refresh clears before reloading; errors never preserve an old snapshot.
- `ClearCuiPresentation` explicitly resets Analytics on lock/sign-out. Read-cache invalidation and stale-lease notifications/probes reset Analytics. Navigation cleanup resets it with other page presentation owners. Closing unsubscribes its epoch notification. Overview hydration/cache logic is unchanged.
- Dashboard registration appends the Analytics loader delegate to DashboardPageViewModel; selecting Analytics (index 1) calls EnsureLoaded. The three existing subtabs share one loaded-at UTC/Refresh header, localized progress and neutral error/Reload action. No new input or package/schema changes were made. Existing CUI banners remain in the shell.

## Changed files

| File | Change |
|---|---|
| `src/HippoCrm.Core/CrmService.Analytics.cs` (new) | Snapshot types, scoped counts/projections, windows, release-on-failure |
| `src/HippoCrm.Core/CuiOwnedData.cs` | Shared snapshot release dispatch |
| `src/HippoCrm.Desktop/ViewModels/AnalyticsHydration.cs` (new) | Background staging and final live checks |
| `src/HippoCrm.Desktop/ViewModels/AnalyticsViewModel.cs` (new) | Lazy owner, local loading/error, Refresh, cancellation/epoch cleanup |
| `src/HippoCrm.Desktop/ViewModels/DashboardViewModel.cs` | Lazy Analytics selection and owner reset |
| `src/HippoCrm.Desktop/ViewModels/MainWindowViewModel.cs` | Loader registration, lock/cache/close hooks |
| `src/HippoCrm.Desktop/ViewModels/CuiPresentation.cs` | Analytics stale/freshness cleanup hooks |
| `src/HippoCrm.Desktop/Views/DashboardView.axaml` | Shared header, Refresh, progress, Reload; existing placeholders retained |
| `tests/HippoCrm.SecurityChecks/AnalyticsChecks.cs` (new) | Shared memory service/security/cap checks |
| `tests/HippoCrm.SecurityChecks/PhaseTwoSecurityChecks.cs` | Register shared checks |
| `tests/HippoCrm.SecurityChecks/HippoCrm.SecurityChecks.csproj` | Link new Core/Desktop sources into adapter runner |
| `tests/HippoCrm.EfPolicyChecks/HippoCrm.EfPolicyChecks.csproj` | Link shared Analytics checks |
| `tests/HippoCrm.UiTests/AnalyticsViewChecks.cs` (new) | Presentation and real memory workspace lifecycle checks |
| `tests/HippoCrm.UiTests/TokenSelectionChecks.cs` | Invoke workspace load/audit/epoch/lock checks |
| `tests/HippoCrm.UiTests/MasterSearchChecks.cs` | Invoke isolated presentation checks |
| `docs/ANALYTICS_REVIEW.md` (new) | Plan, attestation, verification and limits |

## Limits and remaining risks

Part 1 verification and limits below describe the first implementation. Part 2 results are appended separately.

- D08 audit verification is unchanged. One protected snapshot read reduces repeated audit work, but each of the five UI protected operations still uses the existing chain/checkpoint path. Counts are independent of the number of analytics queries; runtime still depends on data and audit size.
- Fixed output caps bound managed snapshot size, not database work. Sixty-three queries, correlated last-contract/name/placement subqueries, sorted name aggregation and exact counts can be expensive on large databases. SQL name aggregation can have larger intermediate state before its 512-character output projection. No new indexes or schema changes were authorized.
- Notes from normal writers use canonical UTC O text. Preflight rejects unexpected width/UTC shape; last-note materialization also parses the exact format. It is not an exhaustive calendar-validity check of every historical timestamp. Noncanonical/imported history may make Analytics unavailable and needs separate data review; no data repair occurs here.
- Stale or permission invalidation discards an in-flight load. If freshness revalidation invalidates that load, use Refresh after verification; automatic retry is not implemented. Analytics is intentionally cleared when navigating away through existing page cleanup.
- Late projection failure is tested for neutral failure/no transferred snapshot; partial-list clearing follows the service finally and shared release code. Whole-process/native buffer erasure is not asserted.
- macOS execution, physical network-adapter-disabled operation, production FIPS provisioning, Release integrity manifests and production/user-workspace acceptance were not exercised. The requested existing token runner also exercises its pre-existing isolated fictional persistent Debug workspace check; new Analytics workspace tests use its memory fixture only. No application was launched against a user workspace.
- No contacts-routing, additional runners, commits, stashes or remote commands were executed. No source changes followed the successful build/runners; only this report was completed. Two exploratory read commands used unsupported Windows rg globs/missing optional paths and returned errors; inspection continued at the existing paths without changing files.

## Analytics Part 2: plain tables and in-memory filters

Starting state: test-main with the expected uncommitted Part 1 files; both required Part 1 files present. Avalonia.Controls.DataGrid is NOT FOUND in project/package lock inventory; no dependency is added. Use one reusable built-in ListBox control and one shared Grid row template, with row summaries, trimmed cells, column headers and native keyboard navigation.

Part 2 adds view-model projections for the selected/comparison periods, contracts, LastContacted, skills, organization roster, selected members and Movement. All predicates, display formatting and selection operate on the existing snapshot. Every table reports filtered, loaded and true totals. Days default to 30; invalid ASCII-number/range input produces a plain error and zero LastContacted rows. Skill search is case-insensitive fragment OR GetSoundexCode on each comma/space-delimited word; a missing match remains empty. Contact status choices apply to all contact-derived lists (including contracts, members and Movement), while the Combined inclusion checkbox independently controls aggregate summaries/cards. Organization lifecycle uses its archived flag alone. Data order is retained from the already sorted snapshot; interactive column sorting is not added.

Additional row projections have explicit release paths: reset clears cell arrays, all table lists, selection and skill-search text. Table getters retain the snapshot's authorization/epoch visibility gate. Existing snapshot loading, authorization checks, cancellation and cleanup stay in place; no Core service/schema changes are made for Part 2. No production security hook is bypassed. New text inputs use CrmTextBox with its disabled undo history and are registered.

### Part 2 verification (bounded loop stopped)

| Step | Result |
|---|---|
| Initial Debug solution build, same offline/no-restore command as Part 1 | FAILED: CS0117, AnalyticsTableView.cs:58, ListBox has no HorizontalContentAlignment property |
| Fix cycle 1 | Removed the unsupported ListBox property; stretch remains on ListBoxItem. Test count access uses Items.Count. Rebuild FAILED in AnalyticsTableChecks.cs:189/194/200 (Key helper shadowed Key enum) and :202 (obsolete Headless KeyPress/KeyRelease overloads treated as errors). |
| Fix cycle 2 | Renamed the helper to Press and used the locally documented KeyPressQwerty/KeyReleaseQwerty physical-key API. Debug solution build PASSED: 0 errors, 2 existing ImageSharp license warnings; 5.24 seconds. |
| `--analytics-tables`, Debug/no-build/no-restore, once | FAILED: `FAIL Tab enters Organization members`, AnalyticsTableChecks.cs:190 (assertion helper :209), called from keyboard sequence :155. Exit 1. See diagnosis below. |
| `--token-selection`, Debug/no-build/no-restore, once | PASSED: 33 selection checks plus existing workspace and Part 1 Analytics assertions. Actual session lock and permission-epoch snapshot cleanup passed. |
| `--master-search`, Debug/no-build/no-restore, once | PASSED: 22 search checks plus 8 Part 1 Analytics presentation assertions. |
| `--organization-lifecycle`, Debug/no-build/no-restore, once | PASSED: 13 checks. |
| Read-only `git diff --check` | PASSED, with existing line-ending conversion notices. |

The new runner observed **5 AccessVerified events per Analytics load**. The token runner again observed first load **5**, Refresh **5**. Direct snapshot behavior is unchanged from Part 1 (one protected read). Every period/include/status/days/skill/organization filter exercised before keyboard checks produced **zero additional AccessVerified events**, as asserted against the real memory workspace audit log. Part 2 changes no service read or scope predicate.

All new non-keyboard checks before the failure passed: rendered row counts across three tabs; inclusion cards and variants; selected period; 24-hour/seven-day averages; zero-previous n/a; dollars/current-contract labels; malformed and boundary days inputs; fragment/per-word phonetic matching; explicit empty results; hidden-contact exclusion; all four status filters/count labels; Active/Archived roster and selected members; authorized/archived multi-placement Movement and its empty message; accessible CrmTextBox inputs and disabled undo buffers. Tab entry/exit passed for selected-period, period comparison, contracts, LastContacted, skills and organization roster. Arrow focus and row summaries passed for those with multiple rows (selected-period has one row).

**Remaining failure diagnosis:** AnalyticsTableChecks.Keyboard selects row 0, then ArrowDown selects row 1, on the actual organization roster (:193–194). This transfers selection into the real member filter. The SQL snapshot sorts roster names; the fixture's second Active organization has no membership rows (all fixture contacts are attached to the first or the hidden organization). The subsequent member-table keyboard check therefore reaches an empty ListBox. This sequence is the likely explanation for Tab skipping it; the failing run did not capture the actual member count/focus target, so an independent empty-table keyboard issue is not ruled out. No authorization assertion failed. No security scope, production validator or snapshot lifecycle was changed to address the failure.

Both permitted fix cycles were used for the compilation failures. No third fix, rebuild or runner retry was attempted. The member-table Tab/arrow completion, Movement keyboard checks, post-keyboard no-read assertion, and the new exhaustive displayed-cell lock assertion were **not reached**. Existing token-runner actual lock/snapshot-release assertions did pass, but do not substitute for that new exhaustive row-projection assertion. Implementation is presented for review with this verification gap, not claimed fully verified.

### Part 2 changed files (in addition to uncommitted Part 1)

| File | Part 2 change |
|---|---|
| `src/HippoCrm.Desktop/ViewModels/AnalyticsTables.cs` (new) | Filtered lists, count labels, selection, cards, safe formatting, per-word search, explicit display-row release |
| `src/HippoCrm.Desktop/ViewModels/AnalyticsViewModel.cs` | Partial declaration; initialize, populate and clear display projections at existing lifecycle boundaries |
| `src/HippoCrm.Desktop/Views/AnalyticsTableView.cs` (new) | Reusable built-in ListBox, unified Grid row template/headers, trimming, row summaries, native keyboard behavior |
| `src/HippoCrm.Desktop/Views/DashboardView.axaml` | Three plain table subtabs, inputs, status/include/period/archive controls, UTC statement, empty messages |
| `docs/TEXT_INPUT_REGISTER.md` | Register days threshold and skill search |
| `tests/HippoCrm.SecurityChecks/HippoCrm.SecurityChecks.csproj` | Link the new partial view-model file so the existing adapter project still compiles |
| `tests/HippoCrm.UiTests/AnalyticsTableChecks.cs` (new) | Normal-service fictional memory fixture, restricted Viewer, display/filter/read-count/keyboard/cleanup checks |
| `tests/HippoCrm.UiTests/Program.cs` | Register `--analytics-tables` |
| `docs/ANALYTICS_REVIEW.md` | This Part 2 record |

Simplifications: equal-width plain columns with text trimming; full row text remains in the item container's accessible summary. No DataGrid, charting, interactive column sorting or new package. Contact list status choices are shared across all contact-derived lists; aggregate inclusion is separate and explicitly labeled. Days default to 30. Existing refresh/reset also clears the days/search presentation inputs to avoid retaining user-entered CUI. A single selected-period row reuses the same table component. Positive/nonzero-previous percentage formatting and exhaustive zero-duration fixtures were not separately exercised by this new UI runner; formatting includes explicit divisor guards. Screen-reader acceptance, macOS, physical adapter-disabled operation, production provisioning and final visual acceptance remain unverified. Existing Part 1 stale-load and Dashboard-cache limits remain as documented.

No Core service, schema, package, authorization scope or production validation changes were made for Part 2. No contacts-routing, commits, stashes or remote commands. Source was unchanged after the passing final build and these runner executions; only this report was completed.

## Part 2 verification

This follow-up used all three newly authorized build-and-run cycles and stopped at the remaining failure. The preceding Part 2 verification record is historical; this section describes the current source and remaining blocker.

| Cycle | Build | `--analytics-tables` result |
|---|---|---|
| 1: diagnosis only | PASS: 0 errors, 2 existing ImageSharp warnings; 11.93 seconds | Reproduced `FAIL Tab enters Organization members`. Counts-only diagnostic: rosterRows=2, selectedMemberRows=0, focusedType=Button, focusedName=unnamed. This confirmed that preceding roster keyboard navigation selected an organization without members. |
| 2: fixture repair and additional checks | PASS: 0 errors, same 2 warnings; 16.63 seconds | Members and Movement Tab/arrow checks, empty-members navigation, all original data checks, percentage checks, status summaries, exact count-label checks and full-keyboard zero-audit assertion passed. Model/input lock assertions passed. Exhaustive retained-control lock assertion failed. |
| 3: binding fallbacks and retained-control diagnostics | PASS: 0 errors, same 2 warnings; 11.29 seconds | Same checks passed. Final failure: `FAIL Lock clears realized and retained table controls, row summaries, cells, count labels, inputs and status summaries`, `tests/HippoCrm.UiTests/AnalyticsTableChecks.cs:208` (assertion helper :315). Exit 1. Diagnostic: retainedTablesWithRows=0, retainedCounts=0, retainedCells=36, retainedSummaries=0, retainedInputs=0, retainedLabels=0. |

The confirmed keyboard fixture issue is repaired by explicitly selecting an organization with multiple visible members before the nonempty members check. No assertion was deleted or weakened. The populated check now reports selectedMemberRows=2 and focusedType=ListBoxItem, and passes entry, arrow focus, row summary and exit. A separate zero-member selection shows “No members match the selected contact statuses.” and passes forward/backward Tab without trapping focus. Movement entry, arrow focus and exit also pass.

The four status checkboxes remain on the Contact subtab. The existing shared status predicate continues to filter contracts, LastContacted, skills, members and Movement. New visible lines above the Organization subtab's members and Movement tables describe the selected statuses using the same checkbox state; Active and Active/Inactive transitions passed. No new query, input or filter predicate was introduced.

Additional checks exercise positive and negative nonzero-previous percentage formatting through numeric-only presentation fixtures; verify exact “Showing {filtered} of {loaded} loaded ({total} total)” text on every table count notification after filter changes (over 100 assertions); retain controls from every subtab; and inspect all eight table models, row arrays, native selections, dynamic card/count/status labels, nondefault days input and skill search after actual session lock. Model rows/cells/selections, dynamic labels and input properties clear. Presentation getters now return blank days/card values when the snapshot is unavailable, using the existing visibility gate. Existing reset/release paths and snapshot lifecycle were not changed.

The broader retained-control check exposes a **remaining security verification blocker**: 36 previously rendered cell TextBlocks still have nonempty Text after lock. Explicit empty binding fallbacks/null defaults did not resolve those retained cells. Counts alone were printed; no cell contents or record text were emitted. No table ItemsSource rows, selection, count labels, row accessibility summaries, days/search inputs or status/card labels remained in the inspected controls. This is a retained-view-text problem beyond the cleared snapshot/models; it must be resolved and reverified before claiming exhaustive UI cleanup. No fourth fix/build/run was attempted.

Observed AccessVerified count was **5 per real Analytics load** in every cycle. All exercised filters and the complete keyboard/selection sequence added **zero** AccessVerified events. Core Analytics and its surrounding protected-read behavior are unchanged. The final `--token-selection`, `--master-search`, `--organization-lifecycle`, `--phase2-security` and EF policy runs were **not run in this follow-up**, because the prerequisite Analytics pass was never achieved. Their earlier passing results do not verify these latest changes. No contacts-routing run, commit, remote command or stash operation occurred.

Files changed in this follow-up: `tests/HippoCrm.UiTests/AnalyticsTableChecks.cs` (diagnostics, fixture repair and additional assertions), `src/HippoCrm.Desktop/ViewModels/AnalyticsTables.cs` (empty-members/status presentation and blank unloaded display values), `src/HippoCrm.Desktop/Views/DashboardView.axaml` (messages, status summaries, named value labels and empty binding fallbacks), `src/HippoCrm.Desktop/Views/AnalyticsTableView.cs` (named count label and empty binding fallbacks), and this report. No Core service, snapshot lifecycle/release code, scope, authorization, schema or package change. Remaining verification: retained-cell cleanup, a complete Analytics pass and then the requested final regression group; macOS/physical air-gap/production acceptance remain unverified as before.
