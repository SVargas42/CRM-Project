# Debug-only UI development composition

Status: prepared for manual code review. No Git commands, commit metadata, commits, dependency downloads or remote synchronization were performed for this task. The previously prepared Release licensing-warning changes remain in the workspace; they were not discarded or committed.

## Launching the UI

In Visual Studio, select **Debug**, set **HippoCrm.Desktop** as the startup project, and select the **HippoCRM UI development (Debug only)** launch profile. That profile supplies the explicit `--ui-development` argument. The first launch profile may also be selected automatically by `dotnet run`.

To select the mode explicitly without inheriting profile arguments:

```powershell
dotnet run --project src/HippoCrm.Desktop/HippoCrm.Desktop.csproj --configuration Debug --no-restore --no-build --no-launch-profile -- --ui-development
```

Build Debug first if the binaries are not current. The app opens account setup. Choose a temporary username/password, create the fixture administrator, and use the normal dashboard/contact/organization workflows. There are no shipped credentials. The fixture administrator receives CUI authorization through the existing audited administration service, scoped to the tracked in-memory fixture. Normal login, session expiry, permissions, lifecycle filtering, retention transitions and audit transactions continue to execute.

The title and a persistent gold banner identify **UI DEVELOPMENT — MEMORY ONLY — NO OPERATIONAL DATA**. Use fictional data. Closing the process discards the workspace and accounts; normal shutdown disposes the keeper connection and clears the original pinned audit key. Create an account again on the next process launch.

For ordinary strict startup, select **HippoCRM provisioned**, or omit the development argument with `--no-launch-profile`. Unprovisioned normal startup still fails closed in Debug. Release never contains the development composition; using its development launch profile/argument cannot enable a mock.

## Mock provisioning and isolation

Program and Avalonia startup inject `MockFipsValidator` and `MockManifestVerifier` only within `#if DEBUG && HIPPOCRM_UI_DEVELOPMENT`. The composition owns its own `StartupSecurityGate`; the production `StartupSecurity` validators are not mutated. The extra compiler symbol is defined only by the Debug desktop project.

The existing startup checks also occur at the view-model and database boundaries. The selected development composition therefore creates an isolated, named, process-local SQLite memory workspace. It accepts no database path, ignores `HIPPOCRM_DATABASE_PATH`, refuses an externally supplied workspace, never instantiates OS key stores, and uses a memory-only audit key and fixture checkpoint. SQLite temporary storage and journaling are memory-only. The bundled provider is used for these UI fixtures without claiming approved FIPS operation.

Normal `Database` constructors and `SqlCipherConnectionFactory` retain their original host/module/manifest/checkpoint gates even while the UI preview is active. A test attempts a normal database open during preview and confirms rejection before its sentinel directory is created. Production workspace access has no alternate development path.

The Core memory factory/storage/key/checkpoint helpers and desktop mock adapter classes live in dedicated `DebugDevelopment` directories. Each file has a compiler guard, and both projects remove those sources from compilation for every configuration other than Debug. Release has neither the mock types nor `Database.CreateUiDevelopmentWorkspace`; calls/injection branches disappear with conditional compilation. No environment variable or Release command-line flag can enable the absent implementation.

## Files for review

| Files | Change |
| --- | --- |
| `CRM.cs` | Opt-in Debug startup branch before normal provisioning; ordinary startup remains strict. |
| `src/HippoCrm.Desktop/App.axaml.cs` | Debug-only mock verification at both Avalonia startup boundaries and fixture cleanup on lifetime exit. |
| `src/HippoCrm.Desktop/DebugDevelopment/DebugUiDevelopment.cs` | Mock validators, explicit activation, tracked fixture composition, scoped fixture-administrator authorization and visible development marking. |
| `src/HippoCrm.Desktop/ViewModels/MainWindowViewModel.cs` | Select the fixture only in the guarded development branch; retain the normal protected initialization and services. |
| `src/HippoCrm.Desktop/MainWindow.cs` | Development title/banner across setup, shell, lock/error presentation; detach the XAML shell before wrapping it to preserve Avalonia logical parenting. |
| `src/HippoCrm.Desktop/Properties/launchSettings.json` | Separate UI-development and provisioned launch profiles. |
| `src/HippoCrm.Core/Database.cs` | Partial declaration and Debug-only routing for a factory-created memory instance; normal construction and opening keep their strict factory. |
| `src/HippoCrm.Core/DebugDevelopment/UiDevelopmentDatabase.cs` | Process-local memory workspace, schema/audit initialization, ephemeral keys/checkpoint, cleanup and Debug-only factory. |
| Core and Desktop `.csproj` files | Physical non-Debug source exclusion; desktop Debug symbol. Preserve the pending Release-warning imports. |
| `tests/HippoCrm.UiTests/UiDevelopmentChecks.cs`, `Program.cs` | Real-App headless development workflow and Release metadata/gate isolation checks. |
| `tests/HippoCrm.FoundationChecks/Program.cs` | Syntax/Hungarian scans include development code with its preprocessor branches enabled. |
| This review | Launch instructions, scope and executed evidence. |

## Executed verification

- CLI Debug solution build: passed.
- Visual Studio MSBuild full Debug solution Rebuild: passed, all seven projects.
- CLI Release solution build: passed, with the pending Release licensing-warning policy.
- Debug UI development checks: **21 passed**, covering strict default startup, exact opt-in, real Avalonia initialization, setup/login, CUI-authorized fixture administration, contact/organization saves, lock/re-authentication, production-path rejection, absence of sentinel files, key wiping and fresh-workspace reset.
- Release isolation checks: **7 passed**, including reflection proving mock/storage/factory exclusion and strict startup/Avalonia denial.
- Actual Release executable invoked with `--ui-development`: rejected through the normal startup gate, exit code **1**.
- Production startup/FIPS/integrity suite: **55 passed in Debug and 55 in Release**, without enabling preview.
- Foundation suite: **114 passed**, plus **9** password/session memory, **15** deadline and **19** checkpoint checks.
- Adjacent adapter/security/migration/account suite: **163 passed**.
- Adjacent real EF policy suite: **112 passed**.
- Adjacent default headless inactivity/input suite: **19 passed**.

Reproduce the dedicated checks with:

```powershell
dotnet tests/HippoCrm.UiTests/bin/Debug/net10.0/HippoCrm.UiTests.dll --ui-development-checks
dotnet tests/HippoCrm.UiTests/bin/Release/net10.0/HippoCrm.UiTests.dll --ui-development-checks
```

The headless checks initialize the actual Avalonia App and exercise its real view model/services; they do not run an operational workspace. Native macOS launch, physical disabled-network-adapter acceptance and client-provisioned production success remain separate acceptance work. No interactive native window was launched during this code-review phase.
