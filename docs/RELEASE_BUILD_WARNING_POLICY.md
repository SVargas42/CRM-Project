# Release ImageSharp warning policy review

Status: changes prepared for manual review. No Git commands, commit metadata, commits, license downloads or remote synchronization were performed for this task.

## Behavior

The user requested that missing ImageSharp licensing no longer block Release compilation. The vendor package has no Release warning-mode property: its original target ties that behavior to a configuration name beginning with `Debug`. The new consumer-owned policy keeps actual application configuration at **Release**, invokes the existing vendor validator with `IsDebugBuild=false`, and uses MSBuild `ContinueOnError="WarnAndContinue"` for that licensing task. Missing/invalid license reports are visible as warnings, not successful license verification.

`bImageSharpLicenseWarningsOnly` defaults to `true` for Release and `false` otherwise. The new target is imported only for Release when this property is true. Debug retains the unchanged package target's built-in warning behavior. Explicit strict mode leaves the original vendor target in place:

```powershell
dotnet build HippoCrm.slnx --configuration Release --no-restore -p:bImageSharpLicenseWarningsOnly=false
```

Without a valid provisioned license, that strict invocation still fails. With the default Release warning policy, the full solution compiles and produces `HippoCrm.Core.dll`. This default also applies to Release builds invoked by the offline CI entry point; a builder requiring strict validation must set the property to false.

Each warning-mode invocation emits `HCRM0001` to identify the selected policy, followed by the validator's actual licensing messages. No blank license, mock key, altered package archive, changed vendor assembly or fabricated validation result is supplied. Existing explicit license files/keys and workspace license discovery remain supported.

## Implementation and review files

| File | Change |
| --- | --- |
| `Directory.Build.props` | Add the configuration-scoped warning policy property, preserving an explicit value. |
| `src/HippoCrm.Core/HippoCrm.Core.csproj` | Generate the local ImageSharp package-path property and import the warning target after SDK/package targets only for the selected Release policy with images enabled. |
| `tests/HippoCrm.EfPolicyChecks/HippoCrm.EfPolicyChecks.csproj` | Apply the same selected policy to the second direct ImageSharp consumer so the complete solution builds. |
| `build/ImageSharpLicenseWarnings.targets` | Invoke the original validator with warning-only failure severity; read its verification key from the unchanged package target and retain license key/file inputs. Missing package targets or validator metadata remain fatal. |
| This review | Record the requested policy change, strict opt-out and executed validation. |

The two consuming projects use explicit `Sdk.props`/`Sdk.targets` imports so their conditional consumer target is evaluated after the original package target. Strict mode does not import the override at all. The package path comes from `GeneratePathProperty`, avoiding a machine-specific path or duplicate hardcoded vendor verification key. A normal **locked local restore** generates that property; no package versions or dependency graph changes are requested.

Focused behavior change:

```diff
  Configuration = Release
  vendorValidator.IsDebugBuild = false
- license task failure stops compilation
+ when bImageSharpLicenseWarningsOnly=true:
+   emit HCRM0001
+   run vendor license validator with ContinueOnError=WarnAndContinue
+   allow compilation after its licensing warnings
+ when bImageSharpLicenseWarningsOnly=false:
+   keep the original vendor target and failure behavior
```

Image support remains compiled in. Application FIPS, native-module verification, authenticated installation manifests, trusted audit checkpoints, session expiry, CUI/RBAC and transactional retention code were not modified. NuGet signature validation, trusted signers and local-only locked dependency restore remain enforced. This supersedes the previous review's default Release-license-blocking policy only; it does not change runtime security provisioning requirements.

## Executed validation

- Locked restore of the seven-project solution through local `NuGet.Config`: passed.
- CLI full solution Release build: passed, **zero errors**, visible licensing/policy warnings.
- Visual Studio Community MSBuild full solution Release **Rebuild**: passed, all seven projects, including Core and Desktop.
- CLI full solution Debug build: passed, zero errors, original vendor licensing warnings; no Release override applied.
- Explicit strict Release licensing target: missing license rejected as expected.
- Warning-mode target with missing package metadata: rejected as a fatal error, not downgraded.
- Release foundation suite: **108** checks; password/session memory **9**; monotonic deadline **15**; trusted checkpoint protocol **19**: passed.
- Release startup/FIPS/integrity denial checks: **55 passed**.
- Release real EF/in-memory policy checks: **112 passed**.
- Release headless session/input/command checks: **19 passed**.

No valid license was provisioned or claimed. No production workspace was opened by these checks, no deployment was published, and no native macOS or physical disabled-adapter acceptance run was performed. Application startup continues to fail closed until its separate security provisioning is satisfied.
