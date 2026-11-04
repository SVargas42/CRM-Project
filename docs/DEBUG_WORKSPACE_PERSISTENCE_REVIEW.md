# Persistent Debug demo workspace review

Status: source changes prepared for manual review. No Git commands, commit metadata, commits, downloads or remote synchronization were performed for this task.

## Cause and behavior

The original development composition intentionally used a randomly named in-memory database, an ephemeral audit key and a process-local checkpoint. Account setup repeated because the account and records themselves disappeared, not just because a lease stamp was missing. Persisting a checkpoint alone would leave it without matching database history or the key needed to verify that history.

The normal Debug UI-development profile now opens a stable, dedicated `UiDevelopment` directory under:

```text
Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
  / BigEagleGroup / HippoCrm / UiDevelopment
```

It contains:

| File | Purpose |
| --- | --- |
| `ui-development.db` | SQLCipher-encrypted demo accounts, records, preferences and audit history. |
| `checkpoint.json` | Versioned audit checkpoint, workspace binding and authentication MAC. No passwords, records or cryptographic keys are serialized into it. |
| `workspace.lease` | Exclusive OS file-handle lease held while this preview owns the workspace. |

Database and audit keys use the existing current-user Windows Credential Manager or macOS Keychain adapters. No plaintext key file or plaintext SQLite fallback is introduced. Closing the preview releases the lease and clears transient key-read buffers without deleting the stored keys or demo database. On restart the adapter reacquires the lease, authenticates the checkpoint, verifies the matching database audit head and loads the existing account. The login screen no longer requests setup for that account.

An exclusive handle cannot remain owned by a process after it exits. The lease file remains, but ownership is reacquired on startup; the OS releases the handle on process termination. A second preview must wait until the current owner exits. File existence alone never means a live owner or a stale lock.

The first launch after this change creates the persistent demo once. Data from an already closed memory-only preview cannot be recovered. The application continues to ignore production workspace path configuration and uses fixed demo filenames in a dedicated directory. The development title/banner now say **LOCAL DEMO**, retaining **NO OPERATIONAL DATA**. Use fictional data.

## Checkpoint and restart handling

- Checkpoint writes use a new same-directory temporary file, flush to disk and replace the checkpoint. The existing prepare/database-commit/confirm coordinator advances the persisted version; state is not deferred until application exit.
- A domain-separated HMAC derived from the protected audit master key authenticates the checkpoint body and its workspace binding. Transient key and MAC buffers are cleared after use.
- Existing database/key/checkpoint state is reused. Missing state beside an existing database, invalid JSON, authentication failure, a stale mismatching head or locked files never silently enroll a replacement workspace or regenerate keys.
- Matching prepared database history completes interrupted confirmation through the existing audit protocol. An ambiguous mismatch requires recovery; it is not treated as permission to reset the stamp.
- A temporary checkpoint file lock can be retried after it is released. Close another running preview before attempting to acquire its workspace lease. Do not delete the checkpoint to resolve a busy workspace.

This is a **Debug mock**, not a rollback-resistant production F07 provider. HMAC detects unauthenticated changes and an old checkpoint against newer database history. A privileged actor able to restore a matching old database/checkpoint pair can still roll both back. Filesystem replacement is not certified hardware monotonic storage or a claim of power-loss durability. The real production checkpoint provisioning path remains unchanged and fail-closed.

## Compiler containment and files

The persistent factory, encrypted storage adapter and checkpoint adapter are inside `#if DEBUG` files under the existing `DebugDevelopment` directory, which is also removed from non-Debug compilation. Desktop composition/injection remains under `#if DEBUG && HIPPOCRM_UI_DEVELOPMENT`. No production factory, FIPS validator, manifest validator or trusted checkpoint registration is replaced.

| Files | Changes |
| --- | --- |
| `src/HippoCrm.Core/DebugDevelopment/UiDevelopmentDatabase.cs` | Shared Debug fixture interface; retain the memory factory for isolated tests. |
| `src/HippoCrm.Core/DebugDevelopment/UiDevelopmentPersistentWorkspace.cs` | Dedicated encrypted disk fixture, OS-protected key identities, normal reopen and audit verification. |
| `src/HippoCrm.Core/DebugDevelopment/UiDevelopmentFileCheckpoint.cs` | Persistent authenticated stamp, versioned updates and exclusive lifetime lease. |
| `src/HippoCrm.Desktop/DebugDevelopment/DebugUiDevelopment.cs` | Default to the persistent fixture; allow isolated tests to inject their own factory before opening. |
| `src/HippoCrm.Desktop/MainWindow.cs` | Update the development title to reflect local persistence. |
| `.gitignore` | Exclude dedicated `UiDevelopment` data folders from source control. |
| `tests/HippoCrm.UiTests/UiPersistenceChecks.cs`, `Program.cs` | Encrypted reopen, UI restart, cross-process lease probes and negative persistence cases. |
| `tests/HippoCrm.UiTests/UiDevelopmentChecks.cs` | Keep original memory tests explicitly isolated and verify the persistent factory is absent from Release. |
| `tests/HippoCrm.FoundationChecks/Program.cs` | Extend source syntax/Hungarian checks to the new Debug code. |
| UI development review and this document | Superseding behavior, storage boundaries and validation evidence. |

## Executed validation

- Debug and Release seven-project CLI builds passed, with the existing licensing policies/warnings.
- **29 persistent Debug checks passed**, including account/contact/organization/theme preservation, version advancement after reopen, SQLCipher ciphertext checks, exclusive ownership across a second process, lease reacquisition, stale/malformed/missing/locked checkpoint rejection, wrong-key rejection, no silent enrollment and key-buffer clearing.
- The same suite exercised actual Avalonia initialization and two view-model/workspace lifecycles: account setup and contact save, close/dispose, then login and loading the saved contact without another setup.
- Original isolated memory-preview checks: **21 passed**.
- Release exclusion/startup checks: **8 passed**, including absence of both Debug workspace factories and all fixture storage/key/checkpoint types.
- Foundation source checks: **120 passed**; password/session memory **9**, inactivity **15**, checkpoint protocol **19** passed.
- Production startup/FIPS/integrity denial checks: **55 passed**.

Persistence test keys were explicitly injected, fictional in-memory keys retained by the test across database lifecycles. Tests wrote only isolated encrypted fixtures under ignored `artifacts/` directories; they did not populate OS credential stores or the user's default demo directory. Cross-process probes exercised the actual lease file. Native macOS/Keychain and provisioned user-profile restart acceptance remain separate checks. Release has no file-checkpoint adapter fallback.

Reproduce dedicated checks after building Debug/Release:

```powershell
dotnet tests/HippoCrm.UiTests/bin/Debug/net10.0/HippoCrm.UiTests.dll --ui-persistence-checks
dotnet tests/HippoCrm.UiTests/bin/Debug/net10.0/HippoCrm.UiTests.dll --ui-development-checks
dotnet tests/HippoCrm.UiTests/bin/Release/net10.0/HippoCrm.UiTests.dll --ui-development-checks
```
