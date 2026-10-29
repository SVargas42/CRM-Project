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

The production key store implemented here is Windows-only, scoped to the current OS user and canonical database path. It does not request administrator rights. Moving the file or changing OS accounts requires an approved key transfer/recovery procedure. macOS startup fails closed until an approved Keychain-backed IDatabaseKeyStore is supplied; no cross-platform plaintext fallback is provided.

## Validation

Completed locally:

- 28 foundation checks passed: C# syntax, Hungarian declarations in new/replaced C# sources, basic separation contracts, property notification/command behavior, and Windows credential ABI/target identity checks.
- WindowsCredentialKeyStore and ViewModelBase compiled against installed .NET reference assemblies with warnings treated as errors.
- No real credential entries were created by tests; integration/UI fixtures now use explicitly injected, disposable in-memory test keys.

Run the foundation checks without Avalonia/SQLCipher packages:

```powershell
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
dotnet restore tests/HippoCrm.FoundationChecks --configfile NuGet.Config
dotnet run --project tests/HippoCrm.FoundationChecks --configuration Release --no-restore
```

The full solution build with `--no-restore` stopped at NETSDK1004 because project.assets.json is unavailable. This was not a successful application compilation. SQLCipher encryption tests were added for unkeyed/wrong-key access, buffer clearing, plaintext rejection/preservation, missing keys, locked files and keyed reopening, but could not execute without the approved native package. Full Avalonia binding/workflow tests, real Credential Manager round trips and tests with network adapters disabled remain required before deployment. The static foundation checks do not certify those behaviors.

Stop here for approval before Step 2.
