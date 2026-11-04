# Offline package feed

This directory contains the 46 exact-version package archives required by the current solution. Keep `.nupkg` files intact; do not extract or edit them. Root `NuGet.Config` selects only this relative folder, clears inherited package/fallback/audit sources, and requires signed packages. The project keeps locked restore enabled and disables online NuGet vulnerability auditing.

`package-inventory.json` records each archive's identity, version, size, SHA-256, NuGet content hash, signature type and signer certificate SHA-256. It is a locally generated review baseline, not an independently approved checksum manifest, a current certificate revocation check, or a FIPS certification. All 46 signatures verified offline, but current revocation status could not be established from the cached revocation information. An authorized reviewer must approve provenance and licensing before release.

Verify packages and restore on an isolated Windows development machine using PowerShell:

```powershell
$env:NUGET_CERT_REVOCATION_MODE = 'offline'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
dotnet nuget verify './LocalPackages/*.nupkg' --all --configfile NuGet.Config
dotnet restore HippoCrm.slnx --configfile NuGet.Config --locked-mode --disable-parallel -p:BuildInParallel=false -m:1
```

On macOS, set the same environment variables with `export` before invoking the same `dotnet` commands. Use a locally installed .NET 10 SDK. Do not enable an online source or regenerate locks to work around a package identity/hash/signature failure. A deliberate dependency update requires a separately approved package set and review of all changed lock files.

The SQLCipher bundle is intentional. Do not replace it with `bundle_e_sqlite3` or ordinary `Microsoft.Data.Sqlite` / `Microsoft.EntityFrameworkCore.Sqlite` convenience packages. The `.Core` packages leave native provider selection to the existing SQLCipher connection factory. The bundled native SQLCipher distribution has not been established as FIPS-validated by this review.

Avalonia.BuildServices is excluded from application build assets, and Avalonia.Remote.Protocol is excluded from runtime assets. Their archives are still required for the dependency graph. URLs embedded in package metadata identify documentation/source/license/certificate information; they are not configured restore sources.

The supplied ImageSharp 3.2.0 build target reports a missing Six Labors license for Debug builds and requires a valid license for Release builds. Obtain the appropriate licensing approval and configure it without committing a private license key. No license bypass is included here.

Package archives total 196,212,879 bytes. The largest is `skiasharp.nativeassets.webassembly.2.88.9.nupkg` at 71,804,127 bytes. Review repository storage/distribution policy before committing the binary batches. Native macOS execution and a test with disabled network adapters remain separate release checks.
