# Phase 4 review: offline CI policy (F08)

Status: Phase 4 code and the explicit MicroCom.Runtime signer addition are approved. Validation completed before local commit execution. Git operations began only after approval; no remote synchronization is authorized or performed.

## Changes for review

| File | Result |
| --- | --- |
| `.github/workflows/macos-app.yml` | Replaces hosted build/publish jobs with manually triggered validation on designated self-hosted macOS ARM64 and Windows X64 builders. No checkout/setup/download/upload/cache actions or release publication remain. |
| `scripts/InvokeOfflineBuild.ps1` | Shared local entry point validates provisioned SDK/source metadata, local package inventory and existing project locks, performs a fresh-cache locked restore through `NuGet.Config`, then builds with `--no-restore`. |
| `scripts/TestOfflineWorkflow.ps1` | Parses actual YAML using a locally provisioned YamlDotNet assembly, validates the workflow and PowerShell AST, and exercises negative preflight and real locked-restore cases. No validator is downloaded. |
| `NuGet.Config` | Adds the explicitly approved historical repository signer for MicroCom.Runtime while retaining the existing signer, mandatory signature verification and trusted-root checks. |
| This document | Records the review diff, provisioning contract, validation evidence and approved package trust resolution. |

The workflow's material before/after changes are:

```diff
- on: push to macos branch, plus manual dispatch
- permissions: contents: write
- runs-on: macos-14 / windows-latest / ubuntu-24.04
- uses: actions/checkout
- uses: actions/setup-dotnet
- dotnet restore --source https://api.nuget.org/v3/index.json
- runtime restore with RestoreLockedMode=false and freshly generated runtime locks
- upload/download artifact actions
- gh release create/upload
+ on: manual dispatch only
+ permissions: {}
+ runs-on: self-hosted + platform/architecture + hippocrm-offline-build labels
+ invoke the provisioned local scripts/InvokeOfflineBuild.ps1
+ dotnet restore --configfile <local NuGet.Config> --locked-mode
+                --packages <new local cache> -p:RestoreLockedMode=true
+ dotnet build --no-restore
```

All seven solution projects are restored together using their existing reviewed `packages.lock.json` files. The CI path neither generates per-RID locks nor performs runtime publication. There are no reviewed source-controlled publication locks for the old per-RID restore steps; generating new ones inside CI would defeat the requested locked policy. This workflow validates the solution and keeps its output locally. A future deployment/package process needs separately reviewed RID locks, approved runtime packages, licensing, signing and the existing security provisioning. No current release or remote artifact transfer is retained.

The root `NuGet.Config` adds only the explicitly approved signer certificate described below. Mandatory signature validation, existing signer trust, `Directory.Build.props`, LocalPackages inventory/archives and all seven project lock files remain unchanged. The NuGet signer service-index URL remains a signer identity, not a retrieval source.

## Provisioned builder contract

Both jobs require local source, PowerShell, a preinstalled .NET 10 SDK, approved package archives and complete project locks. The labels are:

- `self-hosted`, `macOS`, `ARM64`, `hippocrm-offline-build`
- `self-hosted`, `Windows`, `X64`, `hippocrm-offline-build`

The runner administrator provisions `github.workspace` before dispatch. The build never checks out, fetches or updates source. `HIPPOCRM_LOCAL_SOURCE_REVISION` must match the controller's `GITHUB_SHA`; this is a trusted provisioning handoff marker, not an independent cryptographic source attestation. The provisioner is responsible for the reviewed source snapshot and protected runner/tool installation.

Set `HIPPOCRM_APPROVED_SDK_VERSION` to the exact reviewed installed SDK version, such as the locally tested `10.0.403`. The selected SDK must match it exactly. Missing/wrong SDKs fail; no SDK installation is attempted. SDK symlinks used by macOS installers resolve to a checked local target. Build inputs reject network paths and filesystem aliases. The provisioner must enforce local storage and network isolation on the target OS; these checks do not replace an OS isolation policy.

Preflight verifies every archive's filename, size and SHA-256 against the existing package inventory, rejects unexpected/missing archives and source/fallback environment overrides, and requires each solution project's lock. Inventory consistency does not replace independent provenance approval. NuGet then enforces the existing mandatory signature/signer policy and locked content hashes from a unique empty cache. Telemetry/workload update notifications and online auditing are disabled; certificate revocation uses offline mode. Successful restore must leave all project locks unchanged. Subsequent build cannot restore implicitly.

The workflow preserves Release build configuration. The existing ImageSharp Release licensing requirement still applies; provision the authorized license locally without committing credentials or disabling the image feature to make CI pass.

GitHub Actions itself has a networked control plane and log transport. Removing job transfer steps cannot make the runner service fully air-gapped. Fully disconnected machines invoke the same entry point directly, outside Actions:

```powershell
$env:HIPPOCRM_APPROVED_SDK_VERSION = '10.0.403' # use the actually approved installed version
./scripts/InvokeOfflineBuild.ps1 -strConfiguration Release
```

`-bVerifyOnly` checks provisioned inputs without running restore/build; the workflow policy test rejects adding that switch to the CI job. The actual job always restores and builds, or fails closed.

## Validation and approved signer resolution

**22 automated workflow/preflight checks passed.** They cover YAML parsing, manual/least-privilege self-hosted execution, absence of SDK/action transfers, exact local entry-point invocation, locked restore arguments, no implicit build restore, input hash/lock validation, accepted valid fixtures, preserved mandatory signature/trusted-root requirements, explicit certificate pins, and rejection of hosted runners, online feeds, inherited source overrides, tampered packages, missing locks, stale source revision and wrong SDK. A real NuGet restore against a deliberately drifted isolated lock fails with `NU1004` and does not rewrite that lock.

The initial fresh-cache run rejected `MicroCom.Runtime 0.11.0` with `NU3034`. The supplied package and inventory identify repository signer SHA-256 `5A2901D6ADA3D18260B9C6DFE2133C95D74B9EEF6AE0E5DC334C8454D1477DF4`; the original configuration trusted only `1F4B311D9ACC115C8DC8018B5A49E00FCE6DA8E2855F9F014CA6F34570BC482D`. The user explicitly approved trusting the MicroCom signer before committing. `NuGet.Config` now pins that historical certificate under the same NuGet repository identity alongside the original certificate. Both require SHA-256 and `allowUntrustedRoot="false"`; `signatureValidationMode="require"` is preserved.

**Offline MicroCom signature verification, a fresh-cache locked restore of all seven projects and the seven-project Debug build now pass.** Validation used the exact workflow entry point with SDK `10.0.403`, a new empty package cache, `ContinuousIntegrationBuild=true` and `--no-restore` for the build. The build reports zero errors and the two existing ImageSharp Debug licensing warnings. No archive was replaced, lock regenerated, untrusted root accepted or signature check disabled. Offline verification does not establish current online revocation status. Release licensing remains a separate provisioned requirement; no successful Release build is claimed.

Reproduce the policy checks using an already installed, approved YAML parser assembly:

```powershell
./scripts/TestOfflineWorkflow.ps1 -strYamlAssemblyPath '<local path to approved YamlDotNet.dll>'
```

This host used the existing Visual Studio Publish extension's local parser assembly; no new package/tool was retrieved. Test fixtures and diagnostic logs remain under ignored `artifacts/` directories and are not source changes.

No hosted workflow was dispatched, no network adapters were changed, and native self-hosted macOS execution was not available. Disabled-adapter and macOS acceptance must use the provisioned builders. No application/runtime security gate was modified by this phase.
