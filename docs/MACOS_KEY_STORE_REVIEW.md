# Debug macOS key-store review

## Starting state and pre-edit trace

Branch test-main; initial git status clean. User explicitly approves the Debug/macOS-only file-key exception and overrides the Git workflow. No remote operation, stash or branch change is authorized.

| Path / method | Key access before this change | Injectable interface or direct OS API? |
|---|---|---|
| CRM.cs Program.Main; App.VerifyStartup | Exact --ui-development Debug opt-in selects the separate Debug gate | No credential API |
| Desktop/DebugDevelopment/DebugUiDevelopment.cs: fnCreateWorkspace, Workspace | Default calls Database.OpenUiDevelopmentWorkspace; tests can inject a workspace factory | Factory, no direct credential API |
| Core/DebugDevelopment/UiDevelopmentPersistentWorkspace.cs: Database.OpenUiDevelopmentWorkspace | Optional IDatabaseKeyStore argument; null defaults to WindowsCredentialKeyStore or MacKeychainKeyStore by host | Interface selected here; only Debug Mac default will change |
| Same: UiDevelopmentPersistent constructor | Retains injected objStore, passes it to UiDevelopmentFileCheckpoint | Interface |
| Core/DebugDevelopment/UiDevelopmentFileCheckpoint.cs: constructor | EnsureKey for ui-development.db and its .audit-master identity; existing DB/checkpoint must match | Interface |
| Same: Authenticate | ReadKey for audit-master identity, wipes owned buffers | Interface |
| UiDevelopmentPersistent.ApplyKey / Open | Reads workspace key through objKeys; pinned buffer passed to e_sqlcipher/sqlite3_key and wiped; registers objKeys and audit identity | Interface; Debug has its own key application |
| Core/SecurityAudit.cs: RegisterConnection, UseKey | Reads audit master through registered objStore.ReadKey; owned buffers cleared | Interface; no direct Keychain access |
| Core/SqlCipherConnectionFactory.cs: ApplyKey / Open | Production path, also interface-based; Debug Database.OpenConnection delegates to objUiDevelopment.Open instead | Not reached by this Debug workspace; unchanged |
| Core/MacKeychainKeyStore.cs | Security.framework/CoreFoundation credential APIs live in this adapter | Direct OS calls only inside adapter; it will not be constructed by the Mac Debug default |

No interface-bypassing Keychain access was found on the persistent Debug path. Change the single Debug selection point; do not modify SQLCipher, audit/checkpoint algorithms, native production adapters, startup gates, schema or authorization.

## Implementation

Files changed:
- `src/HippoCrm.Core/DebugDevelopment/UiDevelopmentFileKeyStore.cs` (new): IDatabaseKeyStore implementation for the approved fictional-data exception.
- `src/HippoCrm.Core/DebugDevelopment/UiDevelopmentPersistentWorkspace.cs`: default selection delegates to CreateUiDevelopmentKeyStore using the actual host flags. macOS selects file keys; Windows selects the existing WindowsCredentialKeyStore. Explicit injected stores remain supported.
- `tests/HippoCrm.UiTests/UiFileKeyStoreChecks.cs` (new): platform-selection, file/recovery/confinement and encrypted reopen checks.
- `tests/HippoCrm.UiTests/UiDevelopmentChecks.cs`: invokes Debug checks and adds explicit Release absence/selection assertions.
- `AGENTS.md`: only the requested approved-exception section was added; existing rules and Git workflow text remain unchanged.
- `docs/MACOS_DEBUG_SETUP.md` (new): teammate setup/reset instructions.
- `docs/MACOS_KEY_STORE_REVIEW.md` (new): this trace and review.

No production startup, schema, authorization, SQLCipher, audit-chain/checkpoint algorithm, credential adapter, package or Release behavior changed. The Desktop default factory remains DebugUiDevelopment.fnCreateWorkspace -> Database.OpenUiDevelopmentWorkspace; changing that method's Debug default selection covers all interface-based consumers without additional hooks. MacKeychainKeyStore is not referenced or constructed by that default path after this change. A caller explicitly supplying a store still controls its own injected dependency, as before.

The file store permits exactly two identities under the dedicated UiDevelopment directory: ui-development.db maps to workspace.key, and its .audit-master identity maps to audit-master.key. Foreign identities and aliases are rejected. Each is 32 independently generated RandomNumberGenerator bytes. A temporary file in the same directory is created with FileMode.CreateNew and UnixCreateMode UserRead | UserWrite on Unix, written/flushed, then published with a non-overwriting File.Move. It is never created broadly and chmodded later. Existing Unix files must already have exactly the owner-read/write mode; wrong mode/length is refused. Windows test instances intentionally omit unsupported UnixCreateMode and explicitly skip Unix permission assertions; default Windows operation never selects this adapter.

Mutable generated/read buffers use SensitiveMemory allocation/clearing; returned ReadKey buffers transfer to the existing consumer, which already wipes them. Failed-publication temporary key files are overwritten with the cleared buffer, flushed and removed. Views, exceptions and test output never print key bytes. Only disposable fictional artifacts were used for checks.

EnsureKey refuses creation when either ui-development.db or checkpoint.json already exists, even if bAllowCreate is incorrectly true. Missing keys and malformed keys use AuditRecoveryRequiredException with the existing message: `Workspace recovery is required. Contact your local administrator.` No key import from the previous Keychain-encrypted folder is attempted. Keep the old folder intact under a renamed location and start a fresh UiDevelopment folder, as documented. This exception is not a deployable replacement for production OS-protected keys or rollback-resistant checkpoints.

## Compilation guards and verification

Core source has `#if DEBUG` and lies under DebugDevelopment; HippoCrm.Core.csproj removes DebugDevelopment/**/*.cs when Configuration != Debug. Desktop opt-in remains `#if DEBUG && HIPPOCRM_UI_DEVELOPMENT`, with HIPPOCRM_UI_DEVELOPMENT defined only by the Debug condition in HippoCrm.Desktop.csproj; that project also excludes DebugDevelopment source outside Debug. No non-Debug selection path was introduced. The actual Release assembly isolation checks verify both the new class and selection method are absent.

| Authorized cycle | Build | Outcome |
|---|---|---|
| 1 | Debug solution | Failed CA1416 at UiFileKeyStoreChecks.cs:32: analyzer did not carry the Windows exclusion into the LINQ permission callback. No runner executed against stale binaries. |
| 2 | Debug solution | PASS, 0 errors, 2 existing ImageSharp warning groups. Changed only the test to check Unix modes in a directly guarded foreach and count checks dynamically. All five requested Debug runners passed once. |
| 3 | Release solution | PASS, 0 errors, 4 existing ImageSharp/license-policy warning groups. Actual Release --ui-development-checks passed once. Source unchanged from the successful Debug verification. |

| Final check on Windows | Result |
|---|---|
| Debug --ui-development-checks | 12 new file-key checks PASS, plus 21 existing UI development isolation checks PASS |
| Debug --ui-persistence-checks | 31 persistent Debug workspace checks PASS |
| Debug --token-selection | 33 focused checks PASS |
| Debug --phase2-security | 326 Phase 2 and adjacent adapter checks PASS |
| Debug EF policy runner | 304 policy checks PASS (also runs organization lifecycle checks) |
| Release --ui-development-checks | 10 isolation checks PASS, including new class/method absence and unchanged fail-closed production startup |
| Unix owner-only mode on Windows | Explicitly SKIPPED; platform does not support that assertion |

The new tests simulate macOS through the internal Debug selection flags, assert the result is the file-store type (not a MacKeychainKeyStore), and exercise independent/reused keys, no temporary leftovers after atomic publication, malformed length rejection, both database-present and checkpoint-present missing-key recovery/no key generation, foreign identity denial and file confinement. An encrypted Debug database plus signed checkpoint is created and reopened with the selected file store to cover real key application and audit reads. Windows selection is checked by type without opening OS credentials. Release has no factory to inject either flag into.

Build/test output is under ignored artifacts/macos-keystore-*.log. No contacts-routing, remote operation, commit, stash operation, branch switch or user-workspace initialization was performed. git diff --check passed. Changes are left uncommitted for manual review.

## Mac confirmation still required

This environment is Windows. Actual macOS 0600 creation/mode checks, native rename/filesystem crash behavior, absence of Keychain prompts during a real demo, actual LocalApplicationData mapping and Mac UI behavior **cannot verify without a Mac**. Code uses creation-time Unix permissions and no Mac Keychain constructor on the selected path; Windows simulation is not proof of native enforcement. Run the documented Debug setup on the Mac after preserving/renaming the old folder. Normal/Release macOS startup remains intentionally blocked by the unchanged production host-policy gate. Physical network-adapter-disabled operation was not performed here; no network behavior was added.
