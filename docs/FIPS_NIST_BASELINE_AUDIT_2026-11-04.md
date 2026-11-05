# FIPS/NIST security baseline audit

Date: 2026-11-04. Checkout: `C:/Users/ellis/Documents/ds3870testGit/CRM-Project`. Branch recorded in `.git/HEAD`: `test-main`.

## Assessment

**Not ready for operational military deployment or a claim of FIPS 140-3/NIST SP 800-171 compliance.** Production defaults correctly deny access without provisioning. The inspected Release build excludes the existing Debug mock implementations, but CUI cache lifetimes and memory-remanence guarantees remain incomplete. No successful production startup bypass or plaintext key logging was identified in the inspected paths.

This is a source and local-build assessment, not a certification, formal verification of the complete executable, or penetration test. Findings distinguish observed code behavior from deployment assumptions and untested failure cases. No application source, project policy, dependency, security gate, or user workspace was modified. This report and ignored build/evaluation logs are the audit outputs. No Git or network commands were run. External standards and validation registries were not queried because of the no-egress constraint; an authorized assessor must map evidence against the client's selected SP 800-171 revision and authoritative offline requirements.

## Findings

| ID | Severity / disposition | Finding |
| --- | --- | --- |
| B01 | High — deployment blocker, expected provisioning gap | Approved module, platform validation, release trust and rollback-resistant checkpoint are not provisioned. |
| B02 | High — confirmed memory-lifecycle gap | CUI strings and copied presentation values cannot be deterministically erased by the current reference-clearing logic. |
| B03 | High — confirmed cache authorization gap | Cached CUI catalogs/results have no independent expiry or authorization-version check. |
| B04 | Medium — confirmed cleanup scheduling gap | Lock obscures data immediately, but cleanup waits without a timeout for pending operations. |
| B05 | Medium — exceptional-path concern | Windows credential managed-buffer transfer lacks failure zeroization. |
| B06 | Medium — build-environment assurance gap | Local NuGet defaults do not independently enforce zero network attempts for arbitrary IDE/build invocations. |
| B07 | High if installation is writable by an adversary — deployment concern | Manifest verification is separate from subsequent native loading and does not itself establish an immutable installation or validated module identity. |

Severity describes the consequence if operational data is introduced into a provisioned deployment. B01 currently blocks normal production startup and limits exposure; the explicitly selected Debug demo remains unsuitable for operational data.

### B01 — Required production provisioning is absent

Evidence:

- [StartupSecurity.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/StartupSecurity.cs:40): host-policy verification rejects non-Windows platforms. Windows uses `BCryptGetFipsAlgorithmMode`; native SQLCipher mode must return SQLite INTEGER 1.
- [ReleaseManifestVerifier.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/ReleaseManifestVerifier.cs:24): absent embedded release public key fails closed.
- [TrustedAuditCheckpoint.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/TrustedAuditCheckpoint.cs:32): the default protected checkpoint store throws until a client partial implementation is supplied.
- [Core project](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/HippoCrm.Core.csproj:19): the bundled SQLCipher package is not, by its presence or version alone, evidence of a validated FIPS 140-3 module.

Using AES-256, PBKDF2-SHA256, HMAC-SHA256 or an enabled Windows FIPS policy does not establish module validation. No approved module certificate/security policy, matching operational-environment evidence, or production hardware-checkpoint acceptance evidence was supplied. The .NET cryptographic providers used for password derivation, random generation, signatures and audit HMAC also need validation-boundary review on each target platform.

Proposed action, subject to approval: provision the actual approved native distribution, authenticated manifest/public key, platform host-policy adapters and rollback-resistant checkpoint implementation; document certificate/version/platform bindings and run acceptance tests. Preserve all default denials. This is an outstanding deployment dependency, not evidence that the fail-closed implementation should be weakened.

### B02 — Reference removal is not CUI zeroization

Evidence:

- [Models.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/Models.cs:6), lines 6–75: contact and organization attributes, notes, employment and contracts use ordinary strings, records and collections. Display names and token splitting create additional strings.
- [MainWindowViewModel.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Desktop/ViewModels/MainWindowViewModel.cs:992), lines 992–1118: contact lookup keeps searchable string snapshots and captures them in a background worker.
- [MainWindowViewModel.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Desktop/ViewModels/MainWindowViewModel.cs:1194), lines 1194–1222: `strOriginalValues` retains a serialized plaintext baseline for multi-value editing.
- [MasterSearchViewModel.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Desktop/ViewModels/MasterSearchViewModel.cs:75), lines 75–123: full authorized contact/organization snapshots are processed into further strings and presentation results.
- [LockCoreAsync](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Desktop/ViewModels/MainWindowViewModel.cs:713), lines 713–739: references are reset; original string storage is not overwritten.

The lock implementation does remove top-level references. Contact page changes invalidate worker revisions and clear row/profile references; dashboard login resets master-search results. These are useful disclosure controls. They do not erase immutable strings, native UI copies, task captures, managed provider copies, pagefile/hibernation data or crash dumps. Garbage collection, assigning `""`, disposing a context, or forcing a collection cannot certify erasure. Plaintext in memory during authorized display is expected; the gap is the lack of a bounded and verifiable lifetime for its copies.

Proposed changes, subject to approval: define an explicit CUI memory-lifetime policy; minimize full-record snapshots and duplicate baselines; use bounded/paged projections; introduce disposable owners for sensitive byte/character buffers where practical; cancel and release workers at security transitions; clear editable presentation state and discard UI undo/history buffers. Do not replace all display strings with `SecureString` and claim that this erases Avalonia/runtime copies. Pair application changes with approved disk encryption, pagefile/hibernation, dump and process-debugging policies. Add tests for post-lock object retention, worker disposal and access revocation; retain the documented residual limitation for immutable/UI memory.

### B03 — Cached data does not revalidate authorization or expire independently

Evidence:

- [LoadTokenChoicesAsync](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Desktop/ViewModels/MainWindowViewModel.cs:216), lines 216–225: when `objTokenChoiceCatalog` is already populated, it is returned without a fresh `CheckSession` or service call. Invalidation occurs on local mutations or lock, rather than a monotonic cache TTL or authorization epoch.
- [MasterSearchViewModel.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Desktop/ViewModels/MasterSearchViewModel.cs:38), lines 38–49 and 111–149: cached choices/results persist after load until explicit search/reset, with no per-cache timestamp or grant-version stamp.
- [LoadMasterSearchAsync](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Desktop/ViewModels/MainWindowViewModel.cs:533): the initial snapshot correctly passes through services, but that does not authorize indefinite reuse of the returned plaintext.

The monotonic 15-minute inactivity limit is a session idle timeout, not a maximum age for a particular CUI cache. Continued activity can retain a cache indefinitely. A different administrator's grant/CUI revocation is enforced on subsequent service requests but does not automatically remove already displayed or cached values. The catalog fast path also has no independent expiry check while lock cleanup is queued. This is a freshness/revocation concern; no cross-user extraction exploit was executed.

Proposed changes, subject to approval: scope each cache to the issuing session and an authorization epoch; use a client-approved monotonic TTL; verify the current session before every cache return and result publication; invalidate on lock, expiry, permission changes, workspace switch and disposal. Requery bounded data when current authorization cannot be established. Test exact TTL boundaries, cached access immediately after session expiry, external grant revocation and worker completion after reset. Do not invent an arbitrary operational TTL without policy approval.

### B04 — Pending operations can postpone cleanup indefinitely

Evidence: [LockAsync / FinishLockAsync](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Desktop/ViewModels/MainWindowViewModel.cs:686), lines 686–710, and [WorkAsync](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Desktop/ViewModels/MainWindowViewModel.cs:506), lines 506–511.

`LockAsync` immediately invalidates the session and sets the UI locking state, which is correct. `FinishLockAsync` then awaits initialization and `tskPendingOperation` before clearing CUI in `LockCoreAsync`. `WorkAsync` has no cancellation parameter or bounded semaphore wait. A blocked native/storage operation can therefore retain CUI and worker closures beyond the inactivity deadline. Search revision checks prevent stale display publication but do not cancel computation or wipe its captured snapshot.

Proposed changes, subject to approval: separate immediate presentation invalidation from database sign-out completion; add cooperative cancellation and bounded cleanup supervision; reject publication using a session generation; define an administrator-visible recovery path for uninterruptible storage work. Never release live native resources unsafely or report an interrupted audit transaction as committed. Test blocked storage, long-running search, expiry during initialization and audit recovery failures.

### B05 — Windows managed credential result is not wiped if transfer fails

Evidence: [WindowsCredentialKeyStore.ReadKey](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/WindowsCredentialKeyStore.cs:45), lines 45–59, and [ClearAndFree](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/WindowsCredentialKeyStore.cs:71), lines 71–82.

Normal callers wipe successfully returned managed keys. Native Credential Manager output is cleared before `CredFree`. However, the managed `arrKey` is declared inside the `try`; there is no outer result owner/transfer flag to clear it if `Marshal.Copy` or native cleanup prevents the return. The macOS adapter and `NativeSecretBuffer.CopyToManaged` already implement the stronger failure-transfer pattern. This is source-confirmed missing cleanup on an exceptional path, not a demonstrated normal-path key leak; no invalid native pointer was injected into a live credential process.

Proposed change, subject to approval: hold the result in an outer nullable variable; complete native cleanup before marking ownership transferred; clear the managed result in an outer `finally` unless transfer succeeded. Add an injectable native boundary for tests of copy/cleanup failures without touching real credentials.

### B06 — Offline defaults need enforced host/build boundaries

Positive evidence:

- [NuGet.Config](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/NuGet.Config:4) clears package sources, fallback folders and audit sources, selects only relative `LocalPackages`, requires signature validation and pins repository signer certificates. The repository's `serviceIndex` URL is a signer identity, not an enabled package feed.
- [Directory.Build.props](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/Directory.Build.props:11) enables lock files/locked restore, disables NuGet advisory auditing and uses a repository-local extracted package directory.
- [InvokeOfflineBuild.ps1](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/scripts/InvokeOfflineBuild.ps1:34), lines 34–97, validates an exact SDK, rejects inherited source/fallback overrides, sets SDK telemetry/workload and certificate revocation offline settings, verifies archive hashes, uses a new cache for signed locked restore, and builds with `--no-restore`.
- [Workflow](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/.github/workflows/macos-app.yml:1) uses provisioned self-hosted builders and the offline wrapper; it does not check out source, download SDKs or transfer releases/artifacts in job steps.
- Evaluated Desktop Release imports contain no `AvaloniaStatsTask` or `Avalonia.BuildServices.targets`; the direct BuildServices package reference excludes all assets.

Remaining gap: ordinary Visual Studio or `dotnet restore/build` does not automatically execute that wrapper. Environment/CLI/MSBuild overrides can replace normal source and lock settings. Repository configuration does not establish machine-wide telemetry, certificate-revocation, workload advertising or extension behavior. [global.json](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/global.json:2) permits `latestFeature` SDK roll-forward rather than pinning the exact SDK outside the wrapper. These are assurance/reproducibility gaps, not proof that a network request actually occurred.

Proposed changes, subject to approval: make the guarded wrapper the approved build entry point; add build-time fail-closed validation of evaluated restore properties, approved SDK and prohibited imports; provision IDE/SDK offline settings; maintain authenticated offline signer/revocation and package-vulnerability inventories. Use host egress denial and test on a disconnected host with attempted-network tracing. GitHub Actions itself has a network control plane; truly air-gapped validation must invoke the local wrapper outside Actions. Merely removing online package feeds cannot prove zero network attempts by every installed tool.

### B07 — Verified-file loading depends on installation immutability

Evidence: [ReleaseManifestVerifier.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/ReleaseManifestVerifier.cs:41), lines 41–84, and [SqlCipherConnectionFactory.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/SqlCipherConnectionFactory.cs:12), lines 12–25.

The verifier authenticates an RSA-PSS/SHA256 inventory and hashes files, then closes its file streams and returns a native path. The connection factory subsequently loads that path. Against an actor able to modify the installation between verification and use, those are separate operations. Already executing managed verifier/entry code and the host/runtime also need an external trust anchor. The signed manifest requires nonempty module identifier and validation-certificate strings, but does not independently check them against a built-in approved module/certificate policy. Authenticity of the organizational signer does not by itself establish FIPS validation.

This concern is conditional on the deployment threat model and writable installation state; no file-swap exploit was attempted. Existing signature, inventory, path-alias rejection and approved-mode checks are valuable and must remain.

Proposed action, subject to approval: define and enforce installation ACLs and platform code-integrity/signing controls that prevent replacement during the process lifetime; authenticate the bootstrap/runtime and native dependencies; bind the provisioned manifest policy to approved module/certificate/platform identities. Where supported, hold verified files or use platform mechanisms that ensure verified content is the content loaded. Test a hostile concurrent replacement fixture on each supported platform. An adjacent hash or a second check alone is not a complete solution against a privileged writer.

## Debug isolation: bounded proof and executable evidence

Relevant boundaries:

- [Desktop project](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Desktop/HippoCrm.Desktop.csproj:6): only Debug adds `HIPPOCRM_UI_DEVELOPMENT`; non-Debug removes `DebugDevelopment/**/*.cs`.
- [Core project](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/HippoCrm.Core.csproj:4): non-Debug removes the Core development directory.
- [DebugUiDevelopment.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Desktop/DebugDevelopment/DebugUiDevelopment.cs:1): all mock composition/types are inside `#if DEBUG && HIPPOCRM_UI_DEVELOPMENT`.
- All three Core development source files are wholly inside `#if DEBUG`. The partial [Database.OpenConnection](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/Database.cs:77) dispatch is also inside `#if DEBUG`.
- [CRM.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/CRM.cs:11), [App.axaml.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Desktop/App.axaml.cs:30), and [MainWindowViewModel.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Desktop/ViewModels/MainWindowViewModel.cs:469) guard the development startup/workspace routes with both symbols.

For the inspected unmodified build graph, let `C_R` be the evaluated Release compilation input set, `D` the development implementation files, and `S_R` the defined Release symbols. Evaluation found `C_R ∩ D = ∅` in Core and Desktop, and `S_R = {TRACE, RELEASE}` before framework-provided symbols. Therefore `DEBUG ∉ S_R` and every existing guarded development call site is preprocessed out. No implementation from `D` can enter these assemblies through the inspected compile items. Without those types or call sites there is no managed control-flow path to these particular mock implementations in the resulting assemblies.

This is a conditional exclusion argument, not an unconditional mathematical proof about all possible artifacts named Release. It assumes trusted compiler, SDK, packages, imports/generators and build inputs. A hostile build system can rewrite source, re-add files or inject new code; a substituted binary can be mislabeled. The audited normal build and its artifacts must be authenticated at deployment. An unexpected build that defines DEBUG may fail compilation because the helpers were removed; do not interpret that as authority to re-add them.

Fresh executable checks: all 8 Release development-isolation checks passed, including absence of mock types, persistent/in-memory factories and checkpoint helper types, plus production/App startup denial. All 55 Phase 1 Release security checks passed. There is no runtime flag that can restore a preprocessed-out method; the inspected development command-line dispatch is absent from Release. Reflection tests cover the named types and entry behavior, not every instruction of every dependency.

Debug remains an intentional bypass when explicitly launched with the standalone `--ui-development` flag. Its file checkpoint authenticates data and uses a process lease, but is not rollback-resistant hardware: replaying a matching old database and checkpoint together is outside its claimed protection. Never deploy Debug or introduce operational CUI into this demo. No Release fallback to that checkpoint was found.

## Cryptographic memory and logging results

- [PasswordBuffer.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/PasswordBuffer.cs:9): PBKDF2-HMAC-SHA256 uses 600,000 iterations, 32-byte new salts and 32-byte output; accepts no salt shorter than 16 bytes. Temporary UTF-8/character arrays are pinned and wiped in `finally`; the unmanaged SecureString copy uses `ZeroFreeGlobalAllocUnicode`.
- [AuthService.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/AuthService.cs:102): expected/actual hashes and salts are read into owned buffers and cleared; password updates/inserts clear buffers after parameterized commands. Hashes and salts intentionally persist as authentication verifiers inside the encrypted database; they are not plaintext passwords or exported keys. Provider/native copies are not independently proven erased by these tests.
- [SqlCipherConnectionFactory.ApplyKey](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/SqlCipherConnectionFactory.cs:126): 32-byte key passed directly to native `sqlite3_key`, with managed buffer clear/unpin in `finally`; no key-bearing connection string or `PRAGMA key='...'` string is constructed. Connections use private cache and disabled pooling. Memory-security and memory-temp-store settings are requested.
- [SecurityAudit.WithKey](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/SecurityAudit.cs:59): distinct protected audit master key and domain-separated HMAC key are cleared after use. Serialized audit signatures/checkpoint digests are integrity metadata, not key material. New event payloads contain defined events/IDs/timestamps rather than passwords or full contact values.
- [CrmDbContext.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/CrmDbContext.cs:43): sensitive parameter logging and detailed errors are disabled; authorized reads use `AsNoTracking`, and successful writes clear the tracker. Authorization/lifecycle/CUI checks are applied at the gateway. Failed writes may leave tracked caller entities until context disposal; successful tracker clearing does not wipe caller-owned entity strings.
- [SecurePasswordInput.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Desktop/SecurePasswordInput.cs:8): password control stores bullets, disables undo/clipboard, and moves input into `SecureString`. Avalonia text input still arrives as a framework-owned transient string; the application cannot erase that string. `SecureString` is not evidence that every platform encrypts or never copies its contents.
- [SensitiveMemory.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/SensitiveMemory.cs:7) uses pinned arrays and `CryptographicOperations.ZeroMemory` plus array clearing. [MacKeychainKeyStore.cs](C:/Users/ellis/Documents/ds3870testGit/CRM-Project/src/HippoCrm.Core/MacKeychainKeyStore.cs:89) protects failed managed ownership transfer; adapter-owned native storage is zeroed before release. Apple-owned immutable framework buffers are released rather than overwritten through read-only pointers; their internal erasure remains an OS/provider acceptance obligation.

No explicit key/password/hash logging or plaintext key-file serialization was found in these production paths. Debug initialization logs exception messages; they are excluded from normal Release and should still be reviewed for environmental details. This negative source-search result is not a memory dump, provider implementation audit or guarantee against malicious diagnostics/extensions. Salt/hash storage in encrypted SQLCipher pages is expected; transient plaintext password/key/hash buffers are necessary for cryptographic computation and must have bounded ownership.

## Local verification and reproducibility

SDK observed: 10.0.403. Fresh solution Release `dotnet build --no-restore` passed: zero errors, four existing ImageSharp warning groups. The approved warning-only licensing policy was not changed; it is not a FIPS control or proof of a valid vendor license.

Executed checks:

1. Core/Desktop evaluated Release compile items: zero development files; neither development symbol defined.
2. Release UI development isolation: 8 passed.
3. Release Phase 1 gates/native-buffer/manifest negative checks: 55 passed.
4. Release actual EF/in-memory security policy: 134 passed, including CUI, role/grant, expiry and rollback negatives. These tests intentionally use isolated fixtures, not provisioned production workspaces.
5. Offline build preflight with exact approved SDK argument: passed package inventory/configuration/lock checks. A new restore was not executed for this audit; cached assemblies are not evidence of a fresh signer validation.
6. Preprocessed Desktop Release build graph inspected for excluded Avalonia build-statistics imports.

Audit logs and evaluated build graphs are under the ignored `artifacts/baseline-audit-*` paths. Release assembly SHA256 values at audit time:

```text
HippoCrm.Core.dll  59D6B07DBD0A351CC88178F8820521FE982DB8455AC11F64284DCAE597A7DCDC
HippoCrm.dll       C3979FD6578760A150F10B4C36FE3F023172F2D53CAAE7DCCEEAEB6B43A44930
```

These are evidence identifiers, not an authenticated release manifest or a new trusted signing root.

Not performed: adapter disablement, packet/connection-attempt capture, native macOS execution, provisioned FIPS positive tests, operational workspace inspection, pagefile/dump inspection, hostile native pointer injection, host ACL/WDAC assessment, certificate revocation freshness assessment, or full dependency binary analysis. No claim of certified module use, complete memory erasure or zero network attempts is supported by the available evidence.

## Proposed remediation sequence — approval required

1. Approve the CUI TTL/authorization invalidation and memory-lifetime policy; address B02–B04 with focused negative tests and honest managed/UI-memory limitations.
2. Apply the Windows failed-transfer ownership fix in B05 and test fault paths.
3. Harden/evaluate the approved build entry points and disconnected-host testing for B06.
4. Complete client provisioning and installation/code-integrity acceptance for B01/B07 before operational deployment.

No remediation has been implemented by this audit. No commit metadata was generated and no commit/push was attempted.
