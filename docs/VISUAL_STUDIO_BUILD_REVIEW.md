# Visual Studio build remediation review

Status: source changes prepared for manual review. No Git commands, commit metadata, commits, package downloads or remote synchronization were performed for this task.

## Reproduced failure and fix

Using the installed Visual Studio Community MSBuild executable against `HippoCrm.slnx` in Debug reproduced three `CS0234` errors in `tests/HippoCrm.FoundationChecks/Program.cs`: the `Microsoft.CodeAnalysis` namespaces were unresolved. Core, Desktop and the other test projects compiled successfully in that same run. The reported missing application API errors were not reproduced in this checkout.

`HippoCrm.FoundationChecks.csproj` referenced compiler assemblies through `$(MSBuildSDKsPath)/../Roslyn/bincore`. The actual property values differ between hosts:

| Property | dotnet MSBuild | Visual Studio MSBuild |
| --- | --- | --- |
| `MSBuildSDKsPath` | Selected .NET SDK's `Sdks` directory | Visual Studio's `MSBuild/Sdks` directory |
| `NetCoreRoot` | Installed .NET root | Installed .NET root |
| `NETCoreSdkVersion` | `10.0.403` on this host | `10.0.403` on this host |

Both compiler references now resolve through `$(NetCoreRoot)sdk/$(NETCoreSdkVersion)/Roslyn/bincore`. This uses the selected installed SDK rather than a hardcoded machine path or an online package. The complete seven-project Debug rebuild now succeeds with Visual Studio MSBuild, and the dotnet CLI Debug build also succeeds.

## Security API integration

The following implementations and imports already exist and compile in this checkout; they were retained rather than duplicated or replaced:

- Public `StartupSecurity` and `AuditRecoveryRequiredException`, in `HippoCrm.Core`; the application and workspace view model import that namespace.
- `Session.Expired`, `Invalidate`, `IsExpired` and `TryRecordActivity`, using the monotonic deadline and expiry checks.
- `CrmService.CanCreateRecords`, enforcing administrator role plus explicit CUI authorization through the service boundary.
- Five-argument `Contacts` and three-argument `Organizations`, including parameterized nullable `enStatus` filtering.
- `CrmService.SetArchived`, which calls `RetentionStatements.SetBusinessRecordStatus` through the authorized transaction, writes its audit and uses the trusted checkpoint coordinator. Moving retention SQL into a view model would discard that boundary; existing callers remain on the secured service API.

As explicitly requested, `SqlCipherConnectionFactory` and `VerifyApprovedRuntime()` are now public. This exposes the existing fail-closed verification operation. Connection opening, provider resolution, native keying and other implementation methods remain internal/private. No FIPS, module, manifest, checkpoint, CUI, session or retention check was bypassed.

## Offline ImageSharp license handling

A blank license was tested against the installed vendor validation target in Release. It fails with **“Six Labors license is empty.”** A placeholder cannot provide a successful licensed Release build. The test used an isolated ignored artifact; no package, validator or signature was modified, and no network retrieval occurred.

`Directory.Build.props` now automatically uses a workspace-root `sixlabors.lic` when it exists and neither `SixLaborsLicenseFile` nor `SixLaborsLicenseKey` was already supplied. An explicit provisioned path/key retains precedence. `.gitignore` excludes `sixlabors.lic` at all depths so a real license is not accidentally committed. No license content or fake key was added to source.

The user confirmed that no valid license is currently provisioned and authorized local Debug development while retaining the Release gate. The installed ImageSharp target already implements this supported distinction: Debug sets `IsDebugBuild=true` and `ContinueOnError=true`; Release does neither. The passing full Debug builds exercise that behavior, so no fake license, vendor-target override or disabled image pipeline is needed.

Provide a valid authorized license through offline provisioning, either at the workspace root or with the existing `SixLaborsLicenseFile` property. The vendor validation target remains active. Debug retains image functionality and produces the existing licensing warnings; Release remains blocked until a valid license is supplied. A direct Release-target check confirmed **“No Six Labors license found”** and a nonzero exit code without any license override. No successful Release build is claimed.

## Files changed

| File | Change |
| --- | --- |
| `tests/HippoCrm.FoundationChecks/HippoCrm.FoundationChecks.csproj` | Resolve local Roslyn references using the selected SDK root/version in both build hosts. |
| `src/HippoCrm.Core/SqlCipherConnectionFactory.cs` | Make the runtime verification class/method public while retaining all checks and restricted connection implementation. |
| `Directory.Build.props` | Discover a genuinely provisioned workspace-level license without overriding an explicit path or key. |
| `.gitignore` | Exclude private license files. |
| This review | Record reproduced failure, existing API alignment, verification and remaining licensing requirement. |

## Executed checks

- Visual Studio MSBuild: full solution Debug **Rebuild passed**, all seven projects.
- dotnet CLI: solution Debug build **passed**, zero errors; two existing project licensing warnings.
- Foundation: **108 passed**, plus **9** password/session memory, **15** monotonic deadline and **19** checkpoint protocol checks.
- Existing startup/FIPS/integrity gate checks: **55 passed**, including denial before workspace/key access.
- Real EF/in-memory policy suite: **112 passed**, including lifecycle/CUI/retention and session expiry.
- Headless session/input/command suite: **19 passed**.
- Blank-license Release validation: rejected as expected; **not** a successful Release build.
- Unmodified missing-license Release gate: rejected as expected with a nonzero exit code; Debug-only development support does not waive Release validation.

No licensed Release build, native macOS run, physical disabled-adapter test or successful provisioned application launch was performed. Application startup still requires the previously approved security provisioning. If Visual Studio continues showing missing members after the rebuilt Core references are loaded, the first Build Output error, selected configuration and exact solution path are needed to identify a remaining checkout/configuration or design-time discrepancy; those errors are not present in these reproduced builds.
