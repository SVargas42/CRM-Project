# CUI freshness policy usability correction

This supersedes the absolute one-minute lock/discard policy documented in the earlier B02–B05 review. No Git commands or commits were executed.

## Current behavior

- **Session timeout is 15 minutes of inactivity.** Accepted user input renews that monotonic inactivity deadline. Background verification does not count as input and cannot keep an idle session alive.
- **The 60-second interval is authorization-cache freshness only.** Its timer marks the lease stale; it does not cancel the session, lock the UI, dispose editors or discard working copies. A successful live check renews the existing lease without replacing the issued session.
- Background probes continue to use the protected local database. They verify the live credential, CUI authorization and current record's required read/write grants, and verify a stable permission epoch before renewal. A concurrently changed epoch requires another check.
- The timer checks freshness cheaply. It performs protected database revalidation when the 60-second interval expires or a local permission epoch changes, rather than opening the database and appending audit events every second. Other-process changes are detected by protected service calls and these bounded freshness checks, not instantaneous IPC notification.
- An unrelated permission change can renew the epoch silently. Read-only lookup/token/search caches are invalidated when their database epoch changes; selected records, baselines, note input and multi-value editor drafts remain intact. Lookup data reloads while browsing or when returning from an editor. Cache invalidation preserves pending search text; clearing a lookup selection cannot close an active editor.
- A slow probe has a five-second supervision budget. Timeout marks freshness stale and observes the outstanding operation rather than ending the session. Only one probe may remain outstanding, preventing an accumulating queue. Stale lookup/search/dashboard cache getters withhold data while active working copies remain intact. New cache publication still requires successful live authorization and a matching captured epoch. Temporary storage errors do not discard drafts.
- Confirmed revocation, expired credentials, cryptographic/audit-integrity failure, manual locking and closing retain fail-closed handling. This change removes an absolute timer as a normal lock cause; it does not allow editing/saving records after access has been revoked. Every protected service read/mutation retains its authorization and audit boundary.
- A committed write's scalar acknowledgement survives an unrelated epoch change. Save handlers retain its persisted ID/revision before refreshing caches, preventing a failed refresh from causing duplicate creation on retry. Merging acknowledged data preserves newer draft input and carries normalized saved fields forward.

## Changed code

| File | Change |
| --- | --- |
| [CuiMemoryLifetime.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/CuiMemoryLifetime.cs) | Separate session validity from stale freshness; explicit `NeedsRefresh`, `MarkStale`, `RenewVerified`; race-safe renewal/local epoch check; no terminal cancellation on cache-age expiry. |
| [CuiLookup.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/CuiLookup.cs) | Audited live validation of CUI authorization and selected-record access before renewing an epoch. |
| [CuiPresentation.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Desktop/ViewModels/CuiPresentation.cs) | Silent freshness/epoch renewal, cache-only invalidation, nonterminal probe timeout with single outstanding operation, and safe acknowledged-record merge. |
| [MainWindowViewModel.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Desktop/ViewModels/MainWindowViewModel.cs) | Preserve active editors on refresh; drop/revalidate stale projections; propagate committed identities before cache reload; refresh invalid lookup data on return from editing. |
| [MasterSearchViewModel.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Desktop/ViewModels/MasterSearchViewModel.cs) | Cache-only invalidation independent of draft/search-input reset. |

## Regression coverage

- Foundation tests simulate more than 30 minutes of accepted activity with repeated freshness expiry and renewal; the issued session remains valid. They also verify that background renewal alone cannot extend the exact 15-minute idle boundary, and retain owner mismatch, regressing-clock and Windows key-transfer failure checks.
- Headless tests drive real protected-service authorization against an isolated Debug memory fixture for 30 simulated active minutes. Record text, pending note and multi-value input survive; unrelated grant updates renew the epoch; a queued probe timeout preserves the draft; confirmed CUI revocation remains denied. Normal production provisioning denial is asserted before injecting the fixture.
- Additional tests cover pending search input, asynchronous result cancellation, successful write acknowledgement across an epoch change, and merging committed identity/normalized fields without overwriting newer user input.
- The existing security suite retains lifecycle, CUI/grant, audit rollback, session and provisioning checks. Former assertions that expected one-minute draft destruction now assert stale-cache verification without session destruction, as required by the updated policy.

The original memory limitations remain: ordinary C# strings, UI/native copies and host dump/pagefile contents cannot be proven erased by dropping references. Active working copies intentionally live until save, cancel, explicit close, inactivity lock or confirmed security denial. FIPS/manifest/checkpoint provisioning and Release mock exclusion remain unchanged. No network sources or dependencies were added.

Verification logs: ignored `artifacts/cui-freshness-*`. Builds use pre-existing local packages and `--no-restore`. Native macOS and disabled-adapter acceptance testing were not performed on this Windows host.

Verified locally: Debug and Release solution builds passed with zero errors (existing ImageSharp license warnings remain). All 206 service-security checks passed, along with 155 EF policy checks, 140 foundation checks, 18 lifetime/key-transfer checks, 26 CUI presentation checks, 19 routed-input/idle checks, 13 search checks, 21 Debug demo checks and 31 persistent Debug checks. The 55 Release startup/security gate checks and 8 Release mock-exclusion checks also passed. These harness counts include shared cases; no compliance certification is implied.
