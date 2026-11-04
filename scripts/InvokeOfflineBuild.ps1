param(
    [ValidateSet('Debug', 'Release')][string]$strConfiguration = 'Release',
    [string]$strRepositoryPath = (Join-Path $PSScriptRoot '..'),
    [string]$strApprovedSdkVersion = $env:HIPPOCRM_APPROVED_SDK_VERSION,
    [switch]$bVerifyOnly
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-LocalPath([string]$strPath) {
    $strFullPath = [IO.Path]::GetFullPath($strPath)
    if ($strFullPath.StartsWith('\\') -or $strFullPath.Contains('://')) { throw 'Build inputs must use local paths.' }
    foreach ($objDrive in [IO.DriveInfo]::GetDrives()) {
        if ($objDrive.DriveType -eq [IO.DriveType]::Network -and $strFullPath.StartsWith($objDrive.Name, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Network-mounted build inputs are prohibited.'
        }
    }
    $strAncestor = $strFullPath
    while ($strAncestor) {
        if (Test-Path -LiteralPath $strAncestor) {
            if ((Get-Item -LiteralPath $strAncestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw 'Build inputs must not traverse filesystem aliases.'
            }
        }
        $strAncestor = [IO.Path]::GetDirectoryName($strAncestor)
    }
    return $strFullPath
}

$strRepository = Assert-LocalPath $strRepositoryPath
if ($env:GITHUB_SHA -and $env:HIPPOCRM_LOCAL_SOURCE_REVISION -cne $env:GITHUB_SHA) {
    throw 'Provision the requested source revision offline before starting this job.'
}
if ($strApprovedSdkVersion -notmatch '^10\.0\.\d+$') { throw 'Provision an approved exact .NET 10 SDK version before building.' }
$objDotnet = Get-Command dotnet -CommandType Application -ErrorAction Stop
# macOS SDK installers commonly expose dotnet through a symlink. Verify its real local target.
$objSdkTarget = (Get-Item -LiteralPath $objDotnet.Source).ResolveLinkTarget($true)
$strDotnet = Assert-LocalPath $(if ($null -eq $objSdkTarget) { $objDotnet.Source } else { $objSdkTarget.FullName })
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:NUGET_CERT_REVOCATION_MODE = 'offline'

Push-Location -LiteralPath $strRepository
try {
    $strActualSdk = (& $strDotnet --version | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $strActualSdk -cne $strApprovedSdkVersion) { throw 'The provisioned SDK does not match the approved version.' }
    $strConfig = Assert-LocalPath (Join-Path $strRepository 'NuGet.Config')
    [xml]$objConfig = Get-Content -LiteralPath $strConfig -Raw
    $arrSources = @($objConfig.configuration.packageSources.add)
    if ($arrSources.Count -ne 1 -or $arrSources[0].key -cne 'LocalPackages' -or $arrSources[0].value -cne 'LocalPackages' -or
        $null -eq $objConfig.configuration.packageSources.clear -or
        @($objConfig.configuration.config.add | Where-Object { $_.key -eq 'signatureValidationMode' -and $_.value -eq 'require' }).Count -ne 1) {
        throw 'NuGet.Config must require signatures and exclusively select LocalPackages.'
    }
    foreach ($strVariable in @('RestoreSources', 'RestoreAdditionalProjectSources', 'RestoreFallbackFolders', 'RestoreAdditionalProjectFallbackFolders')) {
        if ([Environment]::GetEnvironmentVariable($strVariable)) { throw 'Inherited NuGet source/fallback overrides are prohibited.' }
    }
    $strFeed = Assert-LocalPath (Join-Path $strRepository 'LocalPackages')
    $strInventory = Assert-LocalPath (Join-Path $strFeed 'package-inventory.json')
    $objInventory = Get-Content -LiteralPath $strInventory -Raw | ConvertFrom-Json
    $arrPackages = @(Get-ChildItem -LiteralPath $strFeed -Filter '*.nupkg' -File)
    if ($objInventory.formatVersion -ne 1 -or $arrPackages.Count -eq 0 -or @($objInventory.packages).Count -ne $arrPackages.Count) {
        throw 'The local package inventory is missing, empty or incomplete.'
    }
    $setNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($objEntry in $objInventory.packages) {
        if ([IO.Path]::GetFileName($objEntry.file) -cne $objEntry.file -or $objEntry.file -notmatch '^[A-Za-z0-9_.-]+\.nupkg$' -or
            -not $setNames.Add($objEntry.file) -or $objEntry.sha256 -notmatch '^[0-9a-fA-F]{64}$') { throw 'Invalid local package inventory entry.' }
        $strArchive = Assert-LocalPath (Join-Path $strFeed $objEntry.file)
        if ((Get-Item -LiteralPath $strArchive).Length -ne $objEntry.bytes -or
            (Get-FileHash -LiteralPath $strArchive -Algorithm SHA256).Hash -ine $objEntry.sha256) { throw ('Local package verification failed: ' + $objEntry.file) }
    }
    $strSolution = Assert-LocalPath (Join-Path $strRepository 'HippoCrm.slnx')
    [xml]$objSolution = Get-Content -LiteralPath $strSolution -Raw
    $arrProjects = @($objSolution.SelectNodes('//Project'))
    if ($arrProjects.Count -eq 0) { throw 'The local solution has no projects.' }
    $dicLocks = @{}
    foreach ($objProject in $arrProjects) {
        $strProject = Assert-LocalPath (Join-Path $strRepository $objProject.Path)
        if (-not $strProject.StartsWith($strRepository + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-Path -LiteralPath $strProject -PathType Leaf)) { throw 'A solution project is missing or outside the local source tree.' }
        $strLock = Assert-LocalPath (Join-Path ([IO.Path]::GetDirectoryName($strProject)) 'packages.lock.json')
        $dicLocks[$strLock] = (Get-FileHash -LiteralPath $strLock -Algorithm SHA256).Hash
    }
    if ($bVerifyOnly) { Write-Output 'Offline SDK, configuration, package inventory and project locks verified.'; return }
    # Force signature/content verification instead of reusing a previously populated cache.
    $strCache = Assert-LocalPath (Join-Path $strRepository ('artifacts/offline-ci/packages-' + [Guid]::NewGuid().ToString('N')))
    if (Test-Path -LiteralPath $strCache) { throw 'The CI package cache must be new.' }
    & $strDotnet restore $strSolution --configfile $strConfig --locked-mode --no-http-cache --disable-parallel --packages $strCache -p:RestoreLockedMode=true -p:NuGetAudit=false -p:BuildInParallel=false -m:1
    if ($LASTEXITCODE -ne 0) { throw 'Locked offline restore failed. Review local packages, trust and lock files; no fallback is permitted.' }
    foreach ($strLock in $dicLocks.Keys) {
        if ((Get-FileHash -LiteralPath $strLock -Algorithm SHA256).Hash -cne $dicLocks[$strLock]) { throw 'A dependency lock file changed during restore.' }
    }
    & $strDotnet build $strSolution --configuration $strConfiguration --no-restore -p:RestoreLockedMode=true "-p:RestorePackagesPath=$strCache" -p:ContinuousIntegrationBuild=true -p:UseSharedCompilation=false -p:BuildInParallel=false -m:1
    if ($LASTEXITCODE -ne 0) { throw 'Offline build failed. Provision approved local build inputs; do not download replacements.' }
}
finally { Pop-Location }
