# Phase 1 security gates and provisioning contract

Status: implementation for review; no deployment credentials, approved native module, signing key, or macOS host-policy adapter are supplied. This phase addresses F01, F02 and F05 only. No commits or publication are authorized until the user approves the proposed batches below.

## Current behavior

Unprovisioned builds refuse startup and workspace access in Debug and Release. There is no development flag, environment-variable bypass, alternate SQLite fallback, or mutable production validator registration. `IFipsValidator` and `IManifestVerifier` are composed by `StartupSecurityGate`; isolated tests inject probes into their own gate instances. The production composition is immutable.

`CRM.Main` checks host policy and authenticated installation integrity, then verifies SQLCipher approved mode on a keyed in-memory connection before Avalonia startup. A failure exits with code 1 and a nontechnical message. `App.Initialize`, framework completion, and `MainWindowViewModel.InitializeAsync` independently enforce host/integrity checks. `Database` construction and the connection factory require the same integrity/host checks and the in-memory module probe before directory creation, workspace connections, or workspace credential access. Each keyed workspace connection checks approved module mode again before schema access. Missing/unknown results are fatal, including an unrecognized `cipher_fips_status` PRAGMA.

The Windows host adapter calls `BCryptGetFipsAlgorithmMode`; API failure or disabled policy is rejected. The macOS host-policy adapter is deliberately unprovisioned and rejects all attempts. No invented global macOS FIPS switch or assumed approved provider is substituted. The client's deployment team must provide an approved platform policy implementation before enabling macOS access. Successful compilation is not evidence of a validated deployment.

## Offline release trust

The client supplies a public RSA SubjectPublicKeyInfo DER file to the trusted build process with `-p:ReleaseSigningPublicKeyFile=<absolute-local-path>`. Core embeds it as `HippoCrm.ReleaseSigningKey.spki`. RSA keys must be at least 3072 bits. This is public verification material, not a signing credential. No default or fixture key is embedded. An omitted resource always rejects installation verification.

The provisioning team signs the exact UTF-8 bytes of `hippocrm-release.json` using RSA-PSS with SHA-256 and places the detached raw signature bytes in `hippocrm-release.sig`. Both files reside in `AppContext.BaseDirectory`. No online certificate-chain discovery, OCSP, CRL retrieval, certificate store lookup, or adjacent replaceable verification key is used. Private signing credentials must remain in the client's approved offline signing facility; this implementation neither accepts nor writes them.

Example manifest structure (all values are illustrative, not approval evidence):

```json
{
  "iVersion": 1,
  "strPlatform": "win-x64",
  "strModuleId": "<approved module identity and version>",
  "strValidationCertificate": "<approved validation reference>",
  "strSqlCipherPath": "<relative path>/e_sqlcipher.dll",
  "lstFiles": [
    { "strPath": "HippoCrm.exe", "strSha256": "<64 hexadecimal characters>" },
    { "strPath": "HippoCrm.dll", "strSha256": "<64 hexadecimal characters>" },
    { "strPath": "HippoCrm.Core.dll", "strSha256": "<64 hexadecimal characters>" }
  ]
}
```

The real inventory must include **every** file in the immutable installation tree except the root manifest and detached signature: native SQLCipher/cryptographic dependencies, executable, managed assemblies, runtime/configuration files, fonts, logos, and other bundled assets. Embedded resources are covered by their containing executable/assembly hash. Signing the example above alone will fail. User databases, logs, mutable settings, and credentials belong outside this tree.

The platform identifier is `win-` or `osx-` plus lowercase process architecture. The native basename must match the provider ABI: `e_sqlcipher.dll` on Windows or `libe_sqlcipher.dylib` on macOS. The signed module identity/certificate fields are the provisioning authority's assertion of the specific approved distribution; merely filling them in is not a validation procedure. The deployment team must verify the module's certificate, security policy, platform and approved operating mode. SQLCipher's mode query must return SQLite INTEGER 1 after keying. The existing community bundle is not accepted merely because it is packaged or reports a 4.x version.

Unknown JSON members, wrong platforms, empty policy fields, malformed hashes, missing/unlisted files, duplicate inventory paths, absolute/traversal/alternate-stream paths, aliases/reparse points, wrong signatures, and unreadable files fail closed. Manifest input is bounded to 4 MiB and signature input to 1024 bytes. The exact authenticated native path is used for both Core and SQLitePCL provider imports rather than relying on ambient library search.

This phase supports an immutable directory deployment with an apphost executable. Framework-dependent `dotnet HippoCrm.dll` launches and extracted single-file layouts are not approved layouts: the process executable and required assemblies must be inside the signed tree, and missing assembly locations are rejected. Prior single-file preview publication commands are therefore not suitable for provisioning this phase. Do not weaken the gate to reuse those packages.

The trust anchor is embedded in the verifier's assembly. The provisioning boundary must authenticate/protect that bootstrap itself with approved OS signing, installation ACLs, and platform trust controls, and prevent modification/replacement during execution. A verifier cannot make itself unpatchable to an adversary who controls its code or OS. Hash checks do not substitute for those controls, module validation, or a rollback-resistant release policy. These remain deployment acceptance requirements, not guarantees claimed by this patch.

## macOS memory ownership

`NativeSecretBuffer` owns a writable unmanaged allocation and clears it before releasing it; SafeHandle provides idempotent/finalizer cleanup. Keychain writes use `CFDataCreateWithBytesNoCopy` with `kCFAllocatorNull`, and release the query/data before disposing their external backing owner. The original pinned managed key is cleared in a finally block.

Keychain reads keep their OS-owned immutable CFData read-only. A pinned managed result is transferred only after copying and native cleanup succeed. A failed transfer clears the result; the copy helper also clears a partially populated destination on copy failure.

Apple documents that no-copy CFData may still make internal copies. No claim is made that this code can wipe immutable OS objects, Apple framework internals, or every OS cache. It wipes adapter-controlled writable allocations; OS-owned objects are released using the supported API. Native macOS integration must validate lifecycle order and Keychain access on the provisioned target.

## Checks and acceptance

Run with `DOTNET_CLI_TELEMETRY_OPTOUT=1`, `DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true`, and `NUGET_CERT_REVOCATION_MODE=offline`:

```powershell
dotnet build HippoCrm.slnx --configuration Debug --no-restore -p:UseSharedCompilation=false -p:BuildInParallel=false -m:1
dotnet run --project tests/HippoCrm.Tests/HippoCrm.Tests.csproj --configuration Debug --no-build --no-restore -- --phase1-security
dotnet run --project tests/HippoCrm.FoundationChecks/HippoCrm.FoundationChecks.csproj --configuration Debug --no-build --no-restore
dotnet run --project tests/HippoCrm.SecurityChecks/HippoCrm.SecurityChecks.csproj --configuration Debug --no-build --no-restore -- --phase1-security
dotnet run --project tests/HippoCrm.UiTests/HippoCrm.UiTests.csproj --configuration Debug --no-build --no-restore -- --phase1-security
```

Phase 1 tests use generated, ephemeral RSA keys and fictional public files under ignored `artifacts/`. No signing private key or real database is stored. They cover host/mode status rejection, startup sequencing, signed file tampering, traversal, wrong keys/platforms, missing/extra files, locked files, buffer erasure before free, partial-copy cleanup, and denial before workspace/key-store side effects. The adapter suite's explicit Phase 1 mode runs RBAC/migration/account checks and unprovisioned ViewModel denial. It reports that the old positive provisioned ViewModel assertions were not run; they are retained for a provisioned test environment. Positive native-workspace and headless UI suites cannot sign in on this unprovisioned build and must not be given a runtime bypass to make them pass.

Required before deployment: real approved module/license and signing anchor, macOS host-policy adapter, signed deployment manifests, native macOS Keychain/module/launch checks, provisioned positive workspace/UI regressions, permission-denied tests under a restricted OS account, file-substitution/alias tests on each target, FIPS-mode API failure/self-test handling, sealed-installation tests, and machine-level execution with network adapters disabled. No network-adapter settings were changed during this implementation. The known ImageSharp Debug license warning is unrelated and is not waived.

### Local verification results (November 3, 2026)

- Full Windows Debug solution build: passed, zero errors; existing ImageSharp license warning.
- PhaseOneSecurityChecks: 55 checks passed. Signed fixture files are fictional and their generated private keys remain ephemeral in memory.
- FoundationChecks: 74 foundation checks plus 9 password/session memory checks passed, including syntax and Hungarian declarations for the new security source.
- SecurityChecks with `--phase1-security`: 93 adapter security/migration/ViewModel checks passed. This includes 64 security-policy assertions and intentionally replaces the positive provisioned ViewModel run with unprovisioned denial assertions; the counts must not be added as separate totals.
- UiTests with `--phase1-security`: direct Avalonia initialization and production ViewModel startup both refused access (2 checks passed). This is not native graphical acceptance.
- `git diff --check`: passed. No dependency versions, lock files, schemas, retention behavior, CI workflows or other audit findings were changed.

The active checkout is `test1-branch` at baseline `1349442493d1f7394292e4162c3f35818a2f8199`. The preferred Visual Studio checkout remains absent on this machine. The configured origin was fetched and inspected before editing; its main and test1-branch refs had no divergent changes relative to this checkout at that inspection. All proposed source changes remain unstaged and uncommitted.

## Proposed commits — pending user approval

Every batch stages explicit paths and receives a staged-diff review and relevant checks before any approved commit. No local artifacts, logs, databases, fixture keys, or caches belong in these commits.

1. **`fix(security): clear macOS adapter-owned native key buffers`**
   - `src/HippoCrm.Core/NativeSecretBuffer.cs`
   - `src/HippoCrm.Core/MacKeychainKeyStore.cs`
   - `tests/HippoCrm.FoundationChecks/HippoCrm.FoundationChecks.csproj`

   Body: Introduce a zero-before-free SafeHandle owner and use it for Keychain write backing storage. Transfer read results only after native cleanup succeeds and wipe partial managed copies on failure. Preserve OS ownership of immutable CFData and the existing Keychain identities.

2. **`feat(security): add provisioned host and release-manifest validators`**
   - `src/HippoCrm.Core/StartupSecurity.cs`
   - `src/HippoCrm.Core/ReleaseManifestVerifier.cs`
   - `src/HippoCrm.Core/HippoCrm.Core.csproj`
   - `tests/HippoCrm.SecurityChecks/HippoCrm.SecurityChecks.csproj`

   Body: Define IFipsValidator/IManifestVerifier, strict host/module result handling, and authenticated complete release inventories. Accept only a build-embedded client public key, bind native module identity to the signed inventory, and reject unprovisioned platforms/layouts without a bypass. Link the shared gate implementation into the isolated adapter harness.

3. **`fix(security): gate startup and workspace access on approved security state`**
   - `CRM.cs`
   - `src/HippoCrm.Core/Database.cs`
   - `src/HippoCrm.Core/SqlCipherConnectionFactory.cs`
   - `src/HippoCrm.Desktop/App.axaml.cs`
   - `src/HippoCrm.Desktop/ViewModels/MainWindowViewModel.cs`

   Body: Enforce the immutable gates at application, UI and Core entry points. Load the authenticated native module explicitly, probe approved mode on a keyed in-memory connection before workspace/key-store side effects, and recheck each workspace connection before schema access. Exit startup with a nontechnical error when validation fails.

4. **`test(security): verify Phase 1 denial and integrity constraints`**
   - `tests/HippoCrm.Tests/PhaseOneSecurityChecks.cs`
   - `tests/HippoCrm.Tests/Program.cs`
   - `tests/HippoCrm.FoundationChecks/Program.cs`
   - `tests/HippoCrm.UiTests/Program.cs`

   Body: Add offline signed-fixture and secret-lifecycle negative tests, expand Hungarian/syntax verification, and verify denial before workspace/credential creation. Exercise direct Avalonia initialization and production ViewModel denial without setting up a workspace or bypassing the gate.

5. **`test(security): retain adapter regressions under unprovisioned startup`**
   - `tests/HippoCrm.SecurityChecks/Program.cs`
   - `tests/HippoCrm.SecurityChecks/ViewModelSecurityChecks.cs`

   Body: Add an explicit adapter test mode for the deliberately unprovisioned startup contract, verify no administrator or authenticated data appears, and continue the RBAC/migration/account regressions. Retain the existing positive provisioned ViewModel tests and report clearly that they were not run in this mode.

6. **`docs(security): document Phase 1 provisioning and acceptance gates`**
   - `docs/PHASE1_SECURITY_PROVISIONING.md`
   - `MacPortInfo.txt`

   Body: Document the client-injected trust/module contract, supported package layout, macOS ownership limits, reproduction commands and outstanding platform acceptance. Record that unprovisioned builds intentionally refuse workspace access and that no compliance certification or publication is established.

## API references

- [Microsoft BCryptGetFipsAlgorithmMode](https://learn.microsoft.com/en-us/windows/win32/api/bcrypt/nf-bcrypt-bcryptgetfipsalgorithmmode)
- [Zetetic SQLCipher API: cipher_fips_status](https://www.zetetic.net/sqlcipher/sqlcipher-api/index.html)
- [Zetetic SQLCipher for Apple FIPS](https://www.zetetic.net/sqlcipher/sqlcipher-apple-fips/)
- [Apple CFDataCreateWithBytesNoCopy](https://developer.apple.com/documentation/corefoundation/cfdatacreatewithbytesnocopy(_:_:_:_:))
