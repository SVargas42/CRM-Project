# Step 1: MVVM and encrypted SQLite foundation

This document records Step 1. Access control, authentication auditing and password handling are subsequently implemented in [Step 2](STEP2.md); the SQLCipher/provider acceptance limitations below remain outstanding.

Only Step 1 is included. Authentication/RBAC/audit changes and background execution are not included.

## Code

- `CRM.cs`: application entry point only.
- `src/HippoCrm.Desktop/MainWindow.cs`: programmatic Avalonia view, bindings, theme and input forwarding. All workflows use view-model commands. Existing contact, organization, interaction, employment, contract, archive and dashboard workflows remain represented.
- `src/HippoCrm.Desktop/ViewModels/MainWindowViewModel.cs`: observable workspace state and commands; no Avalonia types. Execution remains synchronous pending Step 3.
- `src/HippoCrm.Desktop/ViewModels/ViewModelBase.cs`: local INotifyPropertyChanged/ICommand implementation; no MVVM package required.
- `src/HippoCrm.Core/Database.cs`: SQLCipher connection initialization, keyed schema validation, connection cleanup and transactional schema initialization.
- `src/HippoCrm.Core/WindowsCredentialKeyStore.cs`: 256-bit random secret protected by the current user's Windows Credential Manager. No plaintext key file. An injected key store supports isolated tests.
- `src/HippoCrm.Core/MacKeychainKeyStore.cs`: 256-bit random secret protected by the current user's macOS Keychain through Apple's SecItem API. No plaintext key file or fallback.
- `src/HippoCrm.Core/Schema.sql`: existing tables/triggers retained; explains the requirement to key the connection BEFORE executing the schema. SQL DDL itself cannot enable encryption.

## Offline packages

The project now references `Microsoft.Data.Sqlite.Core` 10.0.14 and proposes `SQLitePCLRaw.bundle_e_sqlcipher` 2.1.13, replacing `Microsoft.Data.Sqlite` and its ordinary SQLite bundle. The SQLCipher bundle supplies the provider/native engine. The version pin and native asset coverage must be checked against the team's approved package inventory; no SQLCipher package was available here to verify or install. No package bytes were downloaded.

Use only a complete, approved local feed, including all transitive dependencies and target runtime assets. Its independently approved manifest is a JSON object mapping each exact `.nupkg` filename to its SHA-256 checksum. A manifest generated from an untrusted package folder is not independent source verification. Example shape (replace the placeholder with the trusted checksum):

```json
{
  "SQLitePCLRaw.bundle_e_sqlcipher.2.1.13.nupkg": "<approved SHA-256 checksum>"
}
```

`NuGet.Config` clears inherited sources and fallback folders. The restore script validates every package in the local feed and uses a fresh local package cache. It rejects UNC/mapped-network paths and checksum mismatches before invoking NuGet. SDK telemetry and workload update notification are disabled for that invocation; NuGet vulnerability queries are disabled. This does not certify the behavior of third-party MSBuild tasks: inspect the approved Avalonia.BuildServices package before permitting a full build in the isolated environment.

From the repository root in PowerShell:

```powershell
# Use independently approved, locally attached media/folders; these paths are examples.
./scripts/RestoreOffline.ps1 -strFeedPath 'D:\ApprovedNuGet' -strManifestPath 'D:\ApprovedManifests\packages.sha256.json'
```

The package references have already been edited, so `dotnet add package` is unnecessary. The equivalent package-add commands, if applying the change to another checkout, are:

```powershell
# First remove the Microsoft.Data.Sqlite PackageReference from that checkout's project file.
# These commands edit references only; they do not restore/download packages.
dotnet add src/HippoCrm.Core package Microsoft.Data.Sqlite.Core --version 10.0.14 --no-restore
dotnet add src/HippoCrm.Core package SQLitePCLRaw.bundle_e_sqlcipher --version 2.1.13 --no-restore
```

The current four application/integration/UI lock files still describe the previous SQLite provider. They have intentionally NOT been hand-edited or assigned fabricated hashes. The restore script explicitly regenerates them once using the verified feed. Review and commit those changes. Subsequent restores default to locked mode through Directory.Build.props. The new dependency-free foundation test project has its own valid empty dependency lock.

After verified restore and third-party build-task review, build/run with no implicit restore:

```powershell
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
dotnet build HippoCrm.slnx --configuration Release --no-restore
dotnet run --project tests/HippoCrm.Tests --configuration Release --no-build --no-restore
dotnet run --project tests/HippoCrm.UiTests --configuration Release --no-build --no-restore
```

## Key and data lifecycle

Each database connection retrieves a short-lived 32-byte secret from the OS store, pins it while calling `sqlite3_key`, clears the managed buffer immediately afterward, verifies `cipher_version` is SQLCipher 4, and performs a schema read to reject incorrect keys. Connection pooling is disabled. The native `e_sqlcipher` import must match the provider's bundled library. Never substitute an ordinary SQLite provider or another independently loaded SQLite handle. Missing/wrong keys or unavailable native encryption fail closed; there is no plaintext fallback.

SQLCipher 4 provides the encrypted database file format; existing DDL is applied only after key validation. Temporary tables remain in memory and cipher memory protection is requested. No secret is added to SQL, configuration, connection strings, or logs. This is not a claim of FIPS validation or defense-specific certification for the proposed native package.

Existing plaintext databases are rejected without conversion, replacement, or deletion. Encryption migration must be a separately reviewed operation preserving the original and accounting for plaintext backups and sidecars. Do not delete or replace an existing database to get past this check. This Step 1 is suitable for new encrypted workspaces once validated; it does not retroactively encrypt existing data.

The Windows key store is scoped to the current OS user and canonical database path. The macOS key store uses the current user's Keychain and a SHA-256 digest of the canonical path as its account. Moving the file or changing OS accounts requires an approved key transfer/recovery procedure. Neither platform has a plaintext fallback.

## Validation

Completed locally:

- 65 foundation checks passed on the development Mac: C# syntax, Hungarian declarations, source separation, password/session behavior, property notification/command behavior, Windows credential ABI/target identity checks, macOS mutex compatibility, and the native macOS Keychain round trip. The check requires local macOS Keychain access and removes its uniquely named temporary item.
- The Release solution built with 0 warnings and 0 errors using .NET 10 on Apple Silicon.
- SQLCipher/application workflow checks passed (48), including encryption, wrong-key rejection, plaintext preservation, and record authorization.
- Headless Avalonia workflow checks passed (35); screenshots were written to `artifacts/screenshots`.
- A self-contained `osx-arm64` publish completed at `artifacts/osx-arm64`; `scripts/PackageMacApp.py` assembled `artifacts/HippoCrm.app`.
- The native app launched to its first-run setup screen using `HIPPOCRM_DATABASE_PATH` pointed at a disposable database. The temporary database and its Keychain key were removed afterward.
- WindowsCredentialKeyStore and ViewModelBase compiled against installed .NET reference assemblies with warnings treated as errors.

Run the foundation checks without Avalonia/SQLCipher packages:

```powershell
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
dotnet restore tests/HippoCrm.FoundationChecks --configfile NuGet.Config
dotnet run --project tests/HippoCrm.FoundationChecks --configuration Release --no-restore
```

The complete local package feed and checksum manifest are under ignored `artifacts/approved-nuget-feed`; the package cache is under ignored `artifacts/verified-packages`. RID-specific locked restores use ignored lock files under `artifacts/locks/osx-arm64`; committed project lock files remain unchanged, preserving the Windows locked restore. The generated app bundle is unsigned and not notarized. Creating the first workspace still requires the user to choose and enter credentials on the setup screen; the smoke test verified the native launch screen without entering credentials. Windows Credential Manager round trips remain Windows-only acceptance work.
