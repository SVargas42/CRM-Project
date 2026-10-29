param(
    [Parameter(Mandatory)][string]$strFeedPath,
    [Parameter(Mandatory)][string]$strManifestPath
)
$ErrorActionPreference = 'Stop'
function Assert-LocalPath([string]$strPath) {
    $strFullPath = [IO.Path]::GetFullPath($strPath)
    if ($strFullPath.StartsWith('\\') -or ([IO.DriveInfo]::new([IO.Path]::GetPathRoot($strFullPath))).DriveType -eq 'Network') {
        throw 'Only a local folder or attached local volume is permitted.'
    }
    return $strFullPath
}
$strLocalFeed = Assert-LocalPath $strFeedPath
$strLocalManifest = Assert-LocalPath $strManifestPath
# The manifest must arrive through the team's independently trusted approval process.
$objManifest = Get-Content -LiteralPath $strLocalManifest -Raw | ConvertFrom-Json
$arrPackages = @(Get-ChildItem -LiteralPath $strLocalFeed -Filter '*.nupkg' -File)
if ($arrPackages.Count -eq 0) { throw 'The verified local feed is empty.' }
foreach ($objPackage in $arrPackages) {
    $strExpectedHash = $objManifest.PSObject.Properties[$objPackage.Name].Value
    if ($strExpectedHash -notmatch '^[0-9a-fA-F]{64}$') { throw "Missing approved SHA-256 for $($objPackage.Name)." }
    if ((Get-FileHash -LiteralPath $objPackage.FullName -Algorithm SHA256).Hash -ne $strExpectedHash) {
        throw "Package checksum mismatch: $($objPackage.Name)."
    }
}
$strRepository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$strPackageCache = Join-Path $strRepository 'artifacts/verified-packages'
# Do not reuse an unverified cache on the first restore.
if ((Test-Path -LiteralPath $strPackageCache) -and @(Get-ChildItem -LiteralPath $strPackageCache -Force).Count -ne 0) {
    throw 'Use a fresh package cache; the current offline cache must be reviewed before reuse.'
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_CLI_HOME = Join-Path $strRepository 'artifacts/dotnet-home'
$strDotnet = if (Get-Command dotnet -ErrorAction SilentlyContinue) { (Get-Command dotnet).Source } else { 'C:/Program Files/dotnet/dotnet.exe' }
# Deliberately regenerate the old plaintext-provider lock graph once. Later restores stay locked.
& $strDotnet restore (Join-Path $strRepository 'HippoCrm.slnx') --configfile (Join-Path $strRepository 'NuGet.Config') --source $strLocalFeed --packages $strPackageCache --no-http-cache --force-evaluate --disable-parallel -p:RestoreLockedMode=false -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Offline restore failed. Review the local package set; do not add an internet source.' }
Write-Output 'Offline restore completed. Review and commit the regenerated lock files before building.'
