# Asynchronous navigation hydration — manual review

Implemented Solution 1 locally. Implementation and verification initially paused for manual review without Git operations. The user subsequently authorized a local-only commit on `test-main`. No package, D06 schema, cryptographic implementation, OS provisioning or audit-chain algorithm was changed.

## Behavior

Overview, Contacts, Organizations and the Audit Logs placeholder select their empty destination before requesting database work. Contact and organization profile buttons and their Back commands use the same routing/hydration lifecycle. Unsaved-change confirmation and the existing local session/lock checks still precede a transition; navigation does not bypass a discard decision or an expired session.

Hydration uses a page-local loading/error state. The main content pane shows an opaque local loading indicator while its empty view is already selected and laid out. Sidebar navigation and Lock remain available. Data-backed page actions stay disabled until hydration succeeds. Storage or audit failures show a local error and Reload action instead of leaving a global busy state or publishing a partial result.

Mutations retain their exclusive busy guard. Navigation cannot abandon a running write. Authentication releases its busy gate once the session/security scope is established, before dashboard hydration. A command revision prevents an older asynchronous operation from clearing a newer operation's busy state.

## Security and ownership

- Each request captures its route generation, issuing Session, CUI lease, service instance and linked cancellation token. A different route, session or workspace invalidates publication.
- Leaving a route cancels its workers, invalidates search/row owners, clears bound collections and releases owned model/draft references. Search inputs and multi-value drafts are reset after discard approval. Empty profile shells contain only the requested identity; detail/history fields are not copied from an old projection into the skeleton.
- All service/database work remains inside `Task.Run` behind the existing serialized database boundary. D06 category, recipient, record-grant and role checks, SQLCipher, audit append/commit and checkpoint behavior remain in the protected Core services.
- A stale lease is revalidated before loading. A final live scope check is always required before publication. Its database/local permission epochs must match those captured before the snapshot was loaded; a newly verified epoch cannot authorize an older snapshot.
- Hydration results remain in a private disposable staging owner. They are transferred to bindings only after the security checks and route/session/generation checks pass. A canceled worker releases its late result before ownership can transfer. An uninterruptible native operation is observed rather than unsafely aborted or having its live native resources freed.
- Authorization or cryptographic-integrity denial triggers locking. Recoverable storage failure leaves an empty local error view. There is no cached-CUI fallback.
- Lock cancels navigation immediately, hides/clears presentation state and supervises every outstanding hydration alongside existing pending work. Late tasks cannot refill the next route or a signed-out workspace.

Ordinary C# strings and framework/native copies retain the documented B02 physical-erasure limitations. This change preserves the existing reference/buffer cleanup rules; it does not claim whole-process memory erasure or instantaneous cross-process permission notification. D03/D08 and OS provisioning findings are not represented as resolved by this UI change.

## Files to review

- [NavigationHydration.cs](../src/HippoCrm.Desktop/ViewModels/NavigationHydration.cs): immediate route selection, page state, request generation/cancellation, staged snapshot ownership, profile hydration, final publication checks and local retry.
- [MainWindowViewModel.cs](../src/HippoCrm.Desktop/ViewModels/MainWindowViewModel.cs): separate navigation commands, cancellation-aware worker helper, authentication handoff, supervised cleanup, profile/Back routing and forwarded shell state. The original single-argument worker API remains available to existing lifecycle checks.
- [CuiPresentation.cs](../src/HippoCrm.Desktop/ViewModels/CuiPresentation.cs): withholds cache getters during loading/error and prevents automatic cache reloads from racing an explicit hydration.
- [MainWindow.axaml](../src/HippoCrm.Desktop/MainWindow.axaml): content-pane loading/error overlays, accessible announcements and Reload action. The CUI banner and sidebar remain outside these localized overlays.
- [NavigationHydrationChecks.cs](../tests/HippoCrm.UiTests/NavigationHydrationChecks.cs): actual Avalonia headless navigation, layout, security and cancellation regressions.
- [UI test entry point](../tests/HippoCrm.UiTests/Program.cs): `--navigation-hydration` command.
- [Security test project](../tests/HippoCrm.SecurityChecks/HippoCrm.SecurityChecks.csproj): links the new partial ViewModel into its existing source-based adapter harness.
- [Foundation checks](../tests/HippoCrm.FoundationChecks/Program.cs): includes the new production source in syntax/convention checks.

## Verification

SDK 10.0.403; existing restored local packages; `--no-restore`. Debug and Release solution builds passed with zero errors. The pre-existing ImageSharp licensing warnings remain (two Debug/four Release warning groups); they were not suppressed or changed.

| Check | Result |
| --- | --- |
| New Debug headless navigation/hydration suite | 34 passed |
| Existing Debug CUI lifecycle/presentation suite | 26 passed |
| Existing Debug UI create/save/lock/reauthentication fixture | 21 passed |
| Foundation syntax/convention suite | 142 passed, plus its separately reported password/session/lease/checkpoint cases |
| Release service/security adapter suite | 256 passed |
| Release actual EF/image policy suite, including D06 | 230 passed |
| Release startup security gates | 55 passed |
| Release routed input/session checks | 19 passed |
| Release development/mock isolation | 8 passed |

Counts overlap and are not counts of independent compliance controls. Evidence is in ignored `artifacts/navigation-*` logs.

The new latency fixture holds the real database semaphore while executing a warmed Contacts navigation. Destination selection measured **28.03 ms** in the final focused run; the test requires less than 100 ms. Before releasing the database queue, it also constructs and lays out the actual Contacts view, confirms its local spinner, confirms no previous/unverified CUI is present, and services dispatcher callbacks. This verifies that route selection/layout no longer waits for database authorization/hydration. It is not a production p95/p99 or physical-display frame-rate claim.

Other new cases cover rapid A→B→C→A selection, canceled late native results and cleared owned fields, live CTI exclusion despite a record grant, both profile shells and Back navigation while the queue is blocked, local audit-failure/retry, locking while loading, and CUI revocation before a pending load can publish.

The older standalone `--contacts-routing` harness was attempted but stops at its original normal production FIPS startup requirement on this unprovisioned host, before its routing assertions execute. No production gate was weakened to run it. The new 34-case suite explicitly asserts normal provisioning denial before using the existing opt-in Debug memory fixture, and exercises the real window/XAML and protected services with fictional data.

Not executed: provisioned production FIPS/hardware-checkpoint acceptance, native macOS, disabled-network-adapter tests or a production workspace latency trace. Data hydration may still take seconds where connection/audit work is slow; this implementation makes the destination and its loading state independent of that wait. Audit scale and connection reuse remain separate architectural work.

## Review status

The user has approved staging and committing this milestone locally on `test-main`. Pushing remains explicitly prohibited.
