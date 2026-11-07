# Swollen Hippo CRM

An offline desktop CRM for Swollen Hippo Contractors, developed by Big Eagle Group. C# / .NET 10, Avalonia, and SQLite. Supported release targets are Windows and macOS.

## Run

Install the approved .NET 10 SDK. Follow [Step 1 setup and validation](docs/STEP1.md) to verify the local package feed and regenerate the provider lock files before building. From this directory:

```powershell
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
./scripts/RestoreOffline.ps1 -strFeedPath 'D:\ApprovedNuGet' -strManifestPath 'D:\ApprovedManifests\packages.sha256.json'
dotnet build HippoCrm.slnx --no-restore
dotnet run --project src/HippoCrm.Desktop --no-build --no-restore
```

Use an approved local SDK and package inventory. NuGet sources are cleared in this repository; do not add an internet feed. No API keys, server, browser, hosted database, or cloud account are required. Third-party build-task behavior must be reviewed before a full build; see the Step 1 validation limits.

## School demonstration (fictional data only)

Use the current team checkout and Debug configuration. Confirm the agreed branch/revision before building; newer work may still be on `macos`. In Visual Studio, select **HippoCRM UI development (Debug only)**. After the verified local restore and Debug build, the equivalent command is:

```powershell
dotnet run --project src/HippoCrm.Desktop --configuration Debug --no-build --no-restore -- --ui-development
```

This existing development composition uses a separate persistent workspace and is excluded from Release builds. It does not provision production FIPS, installation-integrity or rollback-resistant audit protection. Use fictional data only; do not remove production startup checks to launch a demonstration. Create a password on first use; the single account does not require a username in the desktop setup/sign-in flow. Do not distribute workspace databases, keys, checkpoint files or credentials.

## First launch

Create an administrator password on the first screen. Passwords must contain 12–256 characters. There is no shared or preconfigured password. Subsequent launches require sign-in. Five incorrect passwords lock that account for 15 minutes, including across application restarts. The workspace locks after 15 minutes without keyboard or pointer input.

SQLite data is stored under `Environment.SpecialFolder.LocalApplicationData/BigEagleGroup/HippoCrm/crm.db` for the current OS user. On Windows this is normally `%LOCALAPPDATA%\BigEagleGroup\HippoCrm\crm.db`. On macOS resolve `LocalApplicationData` for the running user's environment; the application uses .NET's platform mapping. Do not put the live database in a network share or synchronized folder. Exit the application before copying the data directory for a local backup. Keep any SQLite sidecar files with the database if present.

## Workflows

- **Organizations:** create and edit contact details, locations, services, tags, and lifecycle status. Archive and restore records through the editor and archive filter.
- **Contacts:** create and edit phone numbers, emails, addresses, skills, availability, personal relationships, social profiles, and lifecycle status. Multiple phone numbers, emails, and addresses can be entered on separate lines. Apply name/email/phone, skill, and availability filters together.
- **Interactions:** save timestamped notes with the signed-in author's name. Existing notes remain immutable; corrections are additional notes.
- **Employment:** record an organization, role, and start/end dates. End a current placement before adding another, preserving movement history. Dates use `YYYY-MM-DD`; placement intervals are inclusive and cannot overlap.
- **Contracts:** append dated values, locations, and descriptions. Values use exact integer cents in storage; agree on one currency for this deployment. Most recent contracts appear first.
- **Overview:** view record counts, active and available contacts, contacts needing follow-up, and four weeks of interaction activity. Follow-up means an active contact with no note in 30 days. Counts update when reopening Overview.
- **Appearance:** switch between light and dark modes using the sidebar.

Save profile changes before selecting another record, applying filters, navigating, locking, or exiting. New contacts must be saved before adding related history. Archived records remain editable and readable, but must be restored before adding related history.

## Project structure

| Project | Responsibility |
| --- | --- |
| `src/HippoCrm.Core` | Models, authentication, SQLite schema, transactional services, audit trail |
| `src/HippoCrm.Desktop` | Avalonia application, navigation, editors, dashboard |
| `CRM.cs` | Desktop entry point; linked into the desktop project |
| `src/HippoCrm.Desktop/MainWindow.cs` | Avalonia view and bindings |
| `src/HippoCrm.Desktop/ViewModels` | Observable workspace state and commands |
| `tests/HippoCrm.Tests` | Executable integration checks using temporary SQLite databases |
| `tests/HippoCrm.UiTests` | Headless desktop workflow checks and rendered screenshots |
| `tests/HippoCrm.SecurityChecks` | Production security/view-model logic exercised through a Windows in-memory SQLite test adapter |
| `tests/HippoCrm.FoundationChecks` | Dependency-free source, memory, MVVM and OS-key-store contract checks |
| `docs/TEAM.md` | Independent teammate assignments and acceptance criteria |
| `scripts` | Repeatable build verification and publish commands |

Run the checks:

```powershell
dotnet run --project tests/HippoCrm.Tests --no-build --no-restore
dotnet run --project tests/HippoCrm.UiTests --no-build --no-restore
```

The UI checks exercise actual Avalonia controls against an isolated temporary database and write screenshots to `artifacts/screenshots`. They do not create accounts or example records in the application's normal data directory. `scripts/Verify.ps1` builds and runs both check suites. Native Windows/macOS installation and OS integration still require target-system verification.

## Security and operational boundaries

Passwords use PBKDF2-SHA256 with individual random 32-byte salts, 600,000 iterations, and fixed-time comparison. SQL values use parameters. Changes and audit entries commit in one transaction. Foreign keys and database triggers enforce retention during normal application/database use. A person with direct write access to the database file can bypass application controls or alter schema; OS file permissions remain essential.

Step 1 introduces SQLCipher connections and Windows Credential Manager key storage for new workspaces. Existing plaintext databases are rejected without alteration; they require a separately approved migration. [Step 2](docs/STEP2.md) adds administrator/editor/viewer roles, individual record grants, transactional authentication/permission/mutation auditing, issued session credentials and SecureString password handling. Administrators create records and manage grants; editors modify assigned writable records; viewers read assigned records. User-management backend APIs exist; an administration screen is not included. Schema version 2 preserves the single legacy bootstrap administrator and requires explicit mapping for multiple legacy users.

Windows uses Windows Credential Manager. The currently merged macOS implementation uses `MacFileKeyStore`: random key files restricted to the current OS user. These files are not protected by the CRM password or macOS Keychain. This does **not** meet the repository's production requirement for OS-protected key storage. The former Keychain adapter is not active in this revision; resolving that discrepancy remains a production blocker.

## Releases

The current `.github/workflows/macos-app.yml` is manually dispatched and requires provisioned, self-hosted offline Windows x64 and macOS ARM64 runners. It validates/builds the supplied revision; it does not automatically publish GitHub releases or attach installers. A push to `main` or `macos` alone does not create an updated downloadable application.

Production packaging still requires the approved security module, authenticated release inventory/signing inputs, protected audit checkpoint provider, platform trust/signing, licensing and target-machine acceptance. The packaging script alone does not provide these. The macOS bundle still needs a packaged Finder/Dock icon in addition to the in-window logo.

## macOS development

The shared integration baseline is `main`. Check for unmerged commits on `macos` before packaging; do not assume a branch name or an older executable identifies the latest integrated build. Windows continues to use Windows Credential Manager; see the current macOS key-storage limitation above. RID-specific restore lock files are kept under ignored `artifacts/locks/osx-arm64`, leaving committed Windows lock files unchanged. Once the approved local package feed is available, build the release solution, publish, and assemble the app bundle:

```sh
dotnet build HippoCrm.slnx --configuration Release --no-restore -p:UseSharedCompilation=false -m:1
dotnet publish src/HippoCrm.Desktop/HippoCrm.Desktop.csproj --configuration Release --runtime osx-arm64 --self-contained true --no-restore -p:UseSharedCompilation=false -p:PublishSingleFile=false --output artifacts/osx-arm64
python3 scripts/PackageMacApp.py
open artifacts/HippoCrm.app
```

Project milestones: MVP review October 31, 2026; target completion November 7, 2026. Media attachments, organization logos, release packaging, and remaining release hardening are assigned in the team plan.
