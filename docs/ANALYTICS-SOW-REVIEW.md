# Keyboard submission and Analytics SOW review

Reviewed November 6, 2026 on analytics-test in the Visual Studio Community checkout.

## Changes

- Single-line email, phone and address Add fields in contact and organization creation/editors now invoke their existing command on Enter. Skill, tag and service picker Add inputs share the behavior. Ordinary profile fields do not save an entire form on Enter; multiline notes keep normal newline behavior.
- The attached behavior consumes Enter before a default button can submit, suppresses held-key repeats, respects command validation and read-only/disabled state, and detaches correctly when its command is removed.
- Analytics now shows seven UTC calendar dates of note activity, up to eight loaded organizations ranked by active contact counts, and the four retained contact lifecycle totals. Numeric labels accompany bars; colors and bar height use existing local theme resources.
- Current-window cards retain simple contact frequency totals and distinct contacts/organizations reached. Previous-window percentage deltas and prior-year comparisons were removed to conform to the SOW. Rolling one-year and year-to-date totals are counts, not comparative statistics.
- SQL aggregation remains within the existing authorized, audited snapshot transaction. Daily activity uses one bounded aggregation with explicit active/unarchived and all-authorized retained variants. Missing days are zero-filled. The snapshot performs 44 data queries rather than the previous 67; existing access verification events remain bounded.
- Archives and Inactive/Retired/Deceased records remain in storage. Inclusion is explicit, and authorization predicates apply before aggregation. Existing list caps, UTC half-open time boundaries, neutral failures, audit rollback behavior and lock/permission cleanup remain in place. New chart rows are cleared on release and hide getters when access is unavailable.
- No schema migration, new dependency, network call or business-record deletion was introduced. The existing layout, fonts and theme resources are retained.

## Automated verification

- Full Debug and Release solution builds passed (existing ImageSharp licensing warnings remain).
- 340 EF-backed policy checks passed, including analytics time boundaries, permission scopes, archive/lifecycle variants, capped projections, tamper-evident audit failures and partial-snapshot cleanup.
- 109 Analytics dashboard checks passed, including light/dark theme switching, narrow layout, keyboard table traversal, filters, lock and permission-epoch clearing.
- 17 focused Enter submission/validation and chart privacy checks passed.
- 33 focused tag/skill checks passed, plus their workspace/Analytics integration checks.
- git diff --check passed. Only explicitly reviewed source, test and documentation paths are staged; local diagnostic output, fixture images, caches, databases and credentials are excluded from this change.

The standalone SecurityChecks and FoundationChecks DLLs could not execute because Windows Application Control returned 0x800711C7. They are not reported as passing. The EF-backed runner independently executed the shared analytics security checks. No Windows policy was weakened or bypassed.

## Remaining acceptance

- Manually verify the native desktop UI on Windows and later macOS, including Enter/Return in the real editor tabs and image-independent profile workflows.
- Run acceptance on a machine with network adapters disabled. Offline restore/build and a source review are not substitutes for that test. This review did not disable adapters.
- Resolve the existing ImageSharp license warning before distribution according to the team's applicable license terms.
- The separate photo/duplicate work on test1-branch has not been merged with analytics-test. Combining their database histories requires an explicit compatibility review and tests; this change does not merge main or promise that every feature branch is integrated.

See GIT-SYNCHRONIZATION.md for the verified branch findings, safe pull/merge sequence, tracking checks and the difference between commit ancestry and a squash merge.
