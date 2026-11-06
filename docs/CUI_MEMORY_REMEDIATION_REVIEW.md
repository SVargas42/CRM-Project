# B02–B05 remediation — code review pending

**Superseded policy:** the absolute one-minute lock/discard behavior described below was replaced by the [CUI freshness usability correction](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/docs/CUI_FRESHNESS_POLICY_REVIEW.md). The current application retains active drafts and uses the 15-minute inactivity deadline for normal timeout locking. This document records the preceding implementation and its verification.

Implemented the approved CUI lifetime/cache/cleanup and Windows credential-transfer work on `test-main`. No Git commands, commits, remote operations, package downloads, or user-workspace resets were performed. Existing FIPS, manifest, SQLCipher and protected-checkpoint production gates remain mandatory; this work does not provision or weaken them.

## Observable policy

| Control | Behavior |
| --- | --- |
| Maximum CUI presentation lifetime | 60 seconds, measured with the session's monotonic continuous clock. Input and cache hits do not renew it. Equality expires. |
| Expiry response | Lock, discard unsaved presentation state, cancel workers and require authentication again. This also applies during active use; it is independent of the existing 15-minute idle timeout. |
| Lookup pages | Up to 128 rows per record type. Primary contact/organization lookups and Master Search have previous/next controls and disclose that text filtering searches the current page. |
| Projection text | At most 512 characters per lookup field. Family/social profile fields are omitted. Selecting a record reloads the authorized full record before editing; shortened lookup text must never overwrite stored data. |
| Authorization scope | Each lease belongs to the exact issued Session object, a local permission epoch and a signed database permission-event sequence. Same identity in a different session cannot reuse it. |
| Permission changes | After a committed local administration change, invalidate live local leases and cancel their tokens. Workspace-wide database epoch detects other-process changes before asynchronous cache returns/publication and during periodic probes. |
| Probe supervision | Probe every second on the presentation context; a probe that cannot complete within five seconds initiates locking. The budget includes waiting behind legitimate SQLCipher key derivation/durable writes. This is supervised polling, not instantaneous cross-process notification or a hard real-time guarantee. |
| Cleanup supervision | Clear presentation state synchronously before awaiting pending work. Give sign-out/cleanup a two-second supervision deadline. Timeout or failure leaves a closed error screen requiring restart; do not claim that sign-out/audit committed successfully. |

The one-minute policy deliberately discards unsaved drafts on expiry. These conservative constants are explicit code policy in `CuiMemoryPolicy`; there is no environment or command-line setting that silently extends them. Review their operational suitability before deployment.

## Core changes

- [CuiMemoryLifetime.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/CuiMemoryLifetime.cs): policy constants, nonrenewable session-bound lease, cancellation timer, monotonic boundary/regression checks, local permission epoch and observer isolation.
- [Session.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/Session.cs): creates CUI leases using the same testable continuous clock as inactivity enforcement.
- [CuiLookup.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/CuiLookup.cs): authenticated permission-epoch reads; SQL-bounded minimal projections; parameterized page offsets and related-organization predicates; authorized single-record contact/organization detail reads. Partial or canceled page construction releases owned data.
- [CrmService.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/CrmService.cs): optional parameterized limits/offsets with stable ID tie-breakers, minimal columns for bounded lookups, name-only organization choices, and release of materialized read results if expiry/audit/checkpoint failure prevents ownership transfer. Existing unrestricted service overload behavior is preserved for non-UI callers; the UI supplies the bounded parameters.
- [CuiOwnedData.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/CuiOwnedData.cs): clears owned mutable model fields and collection references, including late results and failed authentication result tuples. Clearing these references is not immutable-string erasure.
- [UserAdministrationService.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/UserAdministrationService.cs): advances local invalidation only after an audited permission/account/password change commits. Failed transactions do not invalidate a successfully authorized view as if a change committed. Invalidation is conservatively process-wide, including other open workspaces.
- [AuthService.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/AuthService.cs): the self-service password-change path likewise invalidates CUI leases after its audit and session revocation commit.
- [WindowsCredentialKeyStore.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/WindowsCredentialKeyStore.cs): outer nullable managed owner and transfer flag; validation/copy/native release occur before transfer; an outer finally wipes any failed managed transfer. Testable transfer boundary permits failure injection without touching real credentials. Native zero-before-free remains intact.

The database epoch uses the verified audit history's latest role/grant/CUI/account/password security event. Read-audit events do not advance it. No schema migration, dependency change, new cryptographic implementation, or flat-file production key storage was introduced.

## Presentation changes

- [CuiPresentation.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Desktop/ViewModels/CuiPresentation.cs) and [MainWindowViewModel.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Desktop/ViewModels/MainWindowViewModel.cs): arm the lease after authentication, verify session/epoch at asynchronous cache boundaries, guard cached getters, supervise periodic authorization checks, link database work to the CUI lifetime, reject and release late results, and separate immediate clearing from sign-out. A late cleanup task is observed but cannot restore UI state or report false success. Closing also supervises pending theme work.
- [MasterSearchViewModel.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Desktop/ViewModels/MasterSearchViewModel.cs): owns fresh disposable page snapshots; resets cancel pending search without waiting for the provider; late snapshots are disposed; guarded cache getters; asynchronous authorization recheck followed by a second cancellation/revision check before publishing. No full-database contact/organization snapshot is loaded for Master Search.
- [DashboardViewModel.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Desktop/ViewModels/DashboardViewModel.cs): passes the workspace scope, validator, cancellation token and page offset to Master Search. Unconfigured standalone search validators default to denial.
- Contact/organization filter workers, related-contact filtering and organization autocomplete use cancellation and reject stale scope/revision results. Related records are filtered by organization in SQL before the page limit.
- Multi-value editors no longer serialize a second combined plaintext baseline string. They compare existing entry values, and disposal releases baseline arrays, drafts and entry fields. Route changes dispose edit state. Old middle-name field values are cleared when replaced.
- [CrmTextBox.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Desktop/CrmTextBox.cs) disables CUI editor undo history. [MainWindow.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Desktop/MainWindow.cs) clears retained shell text editors when locking. Existing modal token-selection cleanup and CUI markings remain active.
- Paging controls and scope labels are in [DashboardView.axaml](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Desktop/Views/DashboardView.axaml), [ContactsRootView.axaml](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Desktop/Views/ContactsRootView.axaml), and [OrganizationsRootView.axaml](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Desktop/Views/OrganizationsRootView.axaml).

## Tests and integration

- [CuiLifetimeChecks.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/tests/HippoCrm.FoundationChecks/CuiLifetimeChecks.cs): exact TTL boundary without timer delivery, wall-clock changes, regression, activity nonrenewal, owner/session mismatch, timer expiry, epoch cancellation, session invalidation, partial copy failure, native cleanup failure, successful transfer and validation failure cleanup.
- [CuiCacheSecurityChecks.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/tests/HippoCrm.SecurityChecks/CuiCacheSecurityChecks.cs): SQL page bounds/continuation, shortened/omitted fields, four statuses, cancellation, CUI plus record/organization grants, stable read epochs, committed revocation, full detail reload and related-record filtering before paging. Shared with the actual EF/in-memory policy suite.
- [CuiPresentationChecks.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/tests/HippoCrm.UiTests/CuiPresentationChecks.cs): reset while provider or revalidation is pending; rejected publication; expired getters before timer dispatch; probe watchdog; blocked worker cleanup; late-result disposal; undo and editor disposal checks. Uses isolated fictional headless fixtures while asserting that normal production startup still denies access.
- Foundation and SecurityChecks project files link the shared policy sources; EF checks link the shared service tests. Foundation source validation covers new files. UI entry point adds `--cui-security`. Existing search and inactivity fixtures now explicitly supply valid test scopes; pure search tests use a bare headless Application rather than bypassing the production App gate.

Local build/test evidence is recorded under ignored `artifacts/cui-*` logs. Builds use existing restored packages with `--no-restore`; no network source was enabled. Final verification results are summarized in the accompanying review response. Existing ImageSharp license warnings remain under the previously approved policy.

Verified locally:

| Check | Result |
| --- | --- |
| Full Debug solution build | Passed, zero errors; existing license warnings |
| Full Release solution build | Passed, zero errors; existing license warnings |
| Visual Studio Community MSBuild, Debug | Passed |
| Foundation source/convention checks | 140 passed |
| Password/session memory; idle deadline; trusted checkpoint protocol | 9, 15 and 19 passed respectively |
| New CUI lifetime / Windows failed-transfer checks | 13 passed |
| Adapter service/security checks | 205 passed |
| Actual EF / in-memory policy checks | 154 passed |
| Headless CUI presentation / cancellation checks | 16 passed |
| Focused Master Search checks | 13 passed |
| Headless inactivity/input checks | 19 passed |
| Debug demo startup/save/lock checks | 21 passed |
| Persistent encrypted Debug restart/lease checks | 31 passed |
| Production FIPS/manifest/native-buffer gate checks | 55 passed in each of Debug and Release |
| Release mock exclusion checks | 8 passed |

The persistent integration run exposed an overly short initial one-second probe budget while legitimate disk work held the queue. It passed with the final five-second probe budget, including explicit authentication/draft assertions. The two-second sign-out supervision and nonrenewable one-minute CUI deadline were not extended. These counts include shared cases across separate harnesses; they are not a count of unique certified controls.

## Limits that remain explicit

1. This bounds permitted presentation/cache use and removes owned references. It cannot certify physical erasure of immutable strings, completed task/runtime copies, Avalonia/native rendering memory, pagefiles, hibernation or dumps. Host memory/dump/storage protections and target-platform acceptance are still necessary.
2. Cross-process revocation is detected at authenticated database boundaries and supervised probes. It is not instantaneous invalidation of pixels or strings already disclosed to an authorized process. Dispatcher starvation is not a hard real-time environment; every guarded return still checks the monotonic lease and local epoch.
3. Cancellation is cooperative. No thread abort, unsafe connection disposal or forced native-memory release was added. An uninterruptible native call may retain its own private buffers until it returns; its results cannot repopulate an expired presentation. Timeout requires restart rather than allowing new work to race the unresolved operation.
4. Full detail for one explicitly selected record and its authorized history is still loaded when needed. Complete organization name choices remain available for relationship preservation, rather than silently dropping assigned IDs. These are session-owned and expire with the same lease; not every legacy service API has been converted to paging. Primary browse/search uses bounded projections. Lookup pages are live views, not a transaction snapshot spanning navigation; concurrent record changes can move rows between pages.
5. Native macOS behavior, disabled-network-adapter tests and provisioned FIPS/hardware-checkpoint acceptance were not executed on this Windows machine. The work is not a compliance certification and does not remediate B01, B06 or B07.

No commit messages or commit batch have been generated. Application code awaits manual review and approval.
