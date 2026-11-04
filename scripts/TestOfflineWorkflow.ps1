param([Parameter(Mandatory)][string]$strYamlAssemblyPath)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
# Use a provisioned parser assembly; never install a validator during the test.
Add-Type -Path $strYamlAssemblyPath
$strRepository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$strWorkflowPath = Join-Path $strRepository '.github/workflows/macos-app.yml'
$strBuildScript = Join-Path $PSScriptRoot 'InvokeOfflineBuild.ps1'
$objYamlParser = [YamlDotNet.Serialization.DeserializerBuilder]::new().Build()
$script:iPassed = 0
function Assert-Check([bool]$bCondition, [string]$strMessage) {
    if (-not $bCondition) { throw ('FAIL ' + $strMessage) }
    $script:iPassed++; Write-Output ('PASS ' + $strMessage)
}
function Assert-Rejected([scriptblock]$fnCheck, [string]$strMessage) {
    try { & $fnCheck | Out-Null } catch { Assert-Check $true $strMessage; return }
    throw ('FAIL ' + $strMessage)
}
function Assert-WorkflowPolicy([string]$strText) {
    $objWorkflow = $objYamlParser.Deserialize([IO.StringReader]::new($strText))
    if ($objWorkflow['on'].Count -ne 1 -or -not $objWorkflow['on'].ContainsKey('workflow_dispatch') -or
        $objWorkflow['permissions'].Count -ne 0 -or $objWorkflow['jobs'].Count -ne 1) { throw 'Unexpected trigger, permissions or job.' }
    $objJob = $objWorkflow['jobs']['validate-offline']
    if ($objJob['runs-on'] -cne '${{ matrix.arrRunnerLabels }}' -or $objJob['defaults']['run']['shell'] -cne 'pwsh') { throw 'Offline runner/script policy changed.' }
    $arrMatrix = @($objJob['strategy']['matrix']['include'])
    if ($arrMatrix.Count -ne 2) { throw 'Both platform validations are required.' }
    foreach ($objPlatform in $arrMatrix) {
        if ('self-hosted' -notin $objPlatform['arrRunnerLabels'] -or 'hippocrm-offline-build' -notin $objPlatform['arrRunnerLabels']) { throw 'Hosted/undesignated runner rejected.' }
    }
    if (@($objJob['steps']).Count -ne 1) { throw 'Unexpected transfer or setup step.' }
    $objStep = $objJob['steps'][0]
    if ($objStep.ContainsKey('uses') -or $objStep['run'].Trim() -cne '& ./scripts/InvokeOfflineBuild.ps1 -strConfiguration Release') { throw 'Only the approved local build entry point is permitted.' }
    if ($objJob['env']['NUGET_CERT_REVOCATION_MODE'] -cne 'offline' -or $objJob['env']['DOTNET_CLI_TELEMETRY_OPTOUT'] -cne '1') { throw 'Offline environment is required.' }
}

$strWorkflow = [IO.File]::ReadAllText($strWorkflowPath)
Assert-WorkflowPolicy $strWorkflow
Assert-Check $true 'YAML parses and only manual, least-privilege self-hosted local builds are permitted'
Assert-Rejected { Assert-WorkflowPolicy ($strWorkflow.Replace('self-hosted', 'macos-latest')) } 'Hosted runner regression is rejected'
Assert-Rejected { Assert-WorkflowPolicy ($strWorkflow.Replace('& ./scripts/InvokeOfflineBuild.ps1 -strConfiguration Release', 'dotnet restore --source https://example.invalid/index.json')) } 'Network restore step regression is rejected'
Assert-Rejected { Assert-WorkflowPolicy ($strWorkflow.Replace('-strConfiguration Release', '-strConfiguration Release -bVerifyOnly')) } 'A validation-only CI bypass is rejected'
Assert-Rejected { Assert-WorkflowPolicy ($strWorkflow.Replace('      - name: Validate and build', "      - uses: actions/setup-dotnet@v6`n      - name: Validate and build")) } 'Downloaded SDK action regression is rejected'

$arrTokens = $null; $arrErrors = $null
$objScriptAst = [Management.Automation.Language.Parser]::ParseFile($strBuildScript, [ref]$arrTokens, [ref]$arrErrors)
Assert-Check (@($arrErrors).Count -eq 0) 'Local build entry point has valid PowerShell syntax'
$arrCommands = @($objScriptAst.FindAll({ param($objNode) $objNode -is [Management.Automation.Language.CommandAst] }, $true))
$arrRestores = @($arrCommands | Where-Object { $_.Extent.Text -match '^& \$strDotnet restore\b' })
$arrBuilds = @($arrCommands | Where-Object { $_.Extent.Text -match '^& \$strDotnet build\b' })
Assert-Check ($arrRestores.Count -eq 1 -and $arrRestores[0].Extent.Text.Contains('--locked-mode') -and
    $arrRestores[0].Extent.Text.Contains('--configfile $strConfig') -and $arrRestores[0].Extent.Text.Contains('-p:RestoreLockedMode=true') -and
    $arrRestores[0].Extent.Text.Contains('--packages $strCache') -and -not $arrRestores[0].Extent.Text.Contains('--source')) 'Restore uses verified configuration, locks and an isolated cache without source overrides'
Assert-Check ($arrBuilds.Count -eq 1 -and $arrBuilds[0].Extent.Text.Contains('--no-restore')) 'Build cannot trigger an implicit dependency restore'
Assert-Check (@($arrCommands | Where-Object { $_.GetCommandName() -match '^(git|gh|curl|wget|Invoke-WebRequest|Invoke-RestMethod)$' }).Count -eq 0) 'Build entry point has no source synchronization or transfer command'
Assert-Check (-not [IO.File]::ReadAllText($strBuildScript).Contains('RestoreLockedMode=false')) 'No unlocked restore override exists in the build entry point'

[xml]$objNuGetConfig = Get-Content -LiteralPath (Join-Path $strRepository 'NuGet.Config') -Raw
$arrCertificates = @($objNuGetConfig.configuration.trustedSigners.repository.certificate)
Assert-Check (@($objNuGetConfig.configuration.config.add | Where-Object { $_.key -eq 'signatureValidationMode' -and $_.value -eq 'require' }).Count -eq 1 -and
    @($arrCertificates | Where-Object { $_.allowUntrustedRoot -ne 'false' -or $_.hashAlgorithm -ne 'SHA256' }).Count -eq 0) 'Mandatory signature validation and trusted-root requirements remain enforced'
Assert-Check (@($arrCertificates | Where-Object { $_.fingerprint -ceq '5A2901D6ADA3D18260B9C6DFE2133C95D74B9EEF6AE0E5DC334C8454D1477DF4' }).Count -eq 1 -and
    @($arrCertificates | Where-Object { $_.fingerprint -ceq '1F4B311D9ACC115C8DC8018B5A49E00FCE6DA8E2855F9F014CA6F34570BC482D' }).Count -eq 1) 'Approved MicroCom repository signer is pinned alongside the existing signer'

$strSdk = (& dotnet --version | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { throw 'A local SDK is required for policy tests.' }
& $strBuildScript -strApprovedSdkVersion $strSdk -bVerifyOnly
Assert-Check $true 'Actual local SDK, all package archive hashes and all solution locks pass preflight'

# Tiny fictional feed exercises preflight failures without copying user packages or invoking restore.
$strFixture = Join-Path $strRepository ('artifacts/phase4-policy-tests/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $strFixture 'LocalPackages'), (Join-Path $strFixture 'src/Fixture') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $strRepository 'NuGet.Config') -Destination (Join-Path $strFixture 'NuGet.Config')
[IO.File]::WriteAllText((Join-Path $strFixture 'HippoCrm.slnx'), '<Solution><Project Path="src/Fixture/Fixture.csproj" /></Solution>')
[IO.File]::WriteAllText((Join-Path $strFixture 'src/Fixture/Fixture.csproj'), '<Project Sdk="Microsoft.NET.Sdk" />')
$strLock = Join-Path $strFixture 'src/Fixture/packages.lock.json'
[IO.File]::WriteAllText($strLock, '{"version":1,"dependencies":{}}')
$strArchive = Join-Path $strFixture 'LocalPackages/fictional.1.0.0.nupkg'
[IO.File]::WriteAllText($strArchive, 'FICTIONAL preflight-only archive; never sent to NuGet.')
$objInventory = @{ formatVersion = 1; packages = @(@{ file = 'fictional.1.0.0.nupkg'; bytes = (Get-Item -LiteralPath $strArchive).Length; sha256 = (Get-FileHash -LiteralPath $strArchive -Algorithm SHA256).Hash }) }
$strInventory = Join-Path $strFixture 'LocalPackages/package-inventory.json'
[IO.File]::WriteAllText($strInventory, ($objInventory | ConvertTo-Json -Depth 4))
& $strBuildScript -strRepositoryPath $strFixture -strApprovedSdkVersion $strSdk -bVerifyOnly
Assert-Check $true 'Fictional preflight fixture is valid before negative mutations'
[IO.File]::AppendAllText($strArchive, 'tampered')
Assert-Rejected { & $strBuildScript -strRepositoryPath $strFixture -strApprovedSdkVersion $strSdk -bVerifyOnly } 'Tampered local package is rejected before restore'
[IO.File]::WriteAllText($strArchive, 'FICTIONAL preflight-only archive; never sent to NuGet.')
Rename-Item -LiteralPath $strLock -NewName 'packages.lock.saved'
Assert-Rejected { & $strBuildScript -strRepositoryPath $strFixture -strApprovedSdkVersion $strSdk -bVerifyOnly } 'Missing project lock is rejected before restore'
Rename-Item -LiteralPath (Join-Path $strFixture 'src/Fixture/packages.lock.saved') -NewName 'packages.lock.json'
Assert-Rejected { & $strBuildScript -strRepositoryPath $strFixture -strApprovedSdkVersion '10.0.999' -bVerifyOnly } 'An unapproved selected SDK is rejected without downloading another SDK'
$strFixtureConfig = Join-Path $strFixture 'NuGet.Config'
$strOriginalConfig = [IO.File]::ReadAllText($strFixtureConfig)
[IO.File]::WriteAllText($strFixtureConfig, $strOriginalConfig.Replace('value="LocalPackages"', 'value="https://example.invalid/index.json"'))
Assert-Rejected { & $strBuildScript -strRepositoryPath $strFixture -strApprovedSdkVersion $strSdk -bVerifyOnly } 'Online feed configuration is rejected before restore'
[IO.File]::WriteAllText($strFixtureConfig, $strOriginalConfig)
$strPriorOverride = $env:RestoreSources
try {
    $env:RestoreSources = 'https://example.invalid/index.json'
    Assert-Rejected { & $strBuildScript -strRepositoryPath $strFixture -strApprovedSdkVersion $strSdk -bVerifyOnly } 'Inherited source override is rejected before restore'
}
finally { $env:RestoreSources = $strPriorOverride }
$strPriorActionRevision = $env:GITHUB_SHA
$strPriorLocalRevision = $env:HIPPOCRM_LOCAL_SOURCE_REVISION
try {
    $env:GITHUB_SHA = 'requested-fixture-revision'
    $env:HIPPOCRM_LOCAL_SOURCE_REVISION = 'different-fixture-revision'
    Assert-Rejected { & $strBuildScript -strRepositoryPath $strFixture -strApprovedSdkVersion $strSdk -bVerifyOnly } 'Stale provisioned source revision is rejected without fetching another revision'
}
finally { $env:GITHUB_SHA = $strPriorActionRevision; $env:HIPPOCRM_LOCAL_SOURCE_REVISION = $strPriorLocalRevision }

# Test NuGet's real locked-mode behavior on an isolated copy, without changing reviewed locks.
$strRestoreFixture = Join-Path $strFixture 'restore-lock-drift'
New-Item -ItemType Directory -Path $strRestoreFixture | Out-Null
$strRestoreProject = Join-Path $strRestoreFixture 'Core.csproj'
$strRestoreLock = Join-Path $strRestoreFixture 'packages.lock.json'
Copy-Item -LiteralPath (Join-Path $strRepository 'src/HippoCrm.Core/HippoCrm.Core.csproj') -Destination $strRestoreProject
$strOriginalLock = [IO.File]::ReadAllText((Join-Path $strRepository 'src/HippoCrm.Core/packages.lock.json'))
$strDriftedLock = $strOriginalLock.Replace('"requested": "[10.0.14, )"', '"requested": "[10.0.13, )"')
if ($strDriftedLock -ceq $strOriginalLock) { throw 'The lock-drift fixture did not change the expected constraint.' }
[IO.File]::WriteAllText($strRestoreLock, $strDriftedLock)
$strLockHash = (Get-FileHash -LiteralPath $strRestoreLock -Algorithm SHA256).Hash
$strRestoreLog = Join-Path $strRestoreFixture 'locked-restore.log'
& dotnet restore $strRestoreProject --configfile (Join-Path $strRepository 'NuGet.Config') --locked-mode --no-http-cache --disable-parallel --packages (Join-Path $strRestoreFixture 'packages') -p:RestoreLockedMode=true -p:NuGetAudit=false *> $strRestoreLog
Assert-Check ($LASTEXITCODE -ne 0 -and (Select-String -LiteralPath $strRestoreLog -Pattern 'NU1004' -Quiet)) 'Real NuGet rejects dependency/lock drift in locked mode'
Assert-Check ((Get-FileHash -LiteralPath $strRestoreLock -Algorithm SHA256).Hash -ceq $strLockHash) 'Rejected locked restore does not regenerate or rewrite its lock file'
Write-Output ($script:iPassed.ToString() + ' offline workflow policy/preflight checks passed.')
