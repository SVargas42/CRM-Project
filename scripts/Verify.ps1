$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    $strDotnet = if (Get-Command dotnet -ErrorAction SilentlyContinue) { 'dotnet' } else { Join-Path $env:ProgramFiles 'dotnet/dotnet.exe' }
    & $strDotnet build HippoCrm.slnx --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    & $strDotnet run --project tests/HippoCrm.FoundationChecks --configuration Release --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Foundation checks failed.' }
    if ($env:OS -eq 'Windows_NT') {
        & $strDotnet run --project tests/HippoCrm.SecurityChecks --configuration Release --no-build --no-restore
        if ($LASTEXITCODE -ne 0) { throw 'Security policy checks failed.' }
    }
    & $strDotnet run --project tests/HippoCrm.Tests --configuration Release --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Integration checks failed.' }
    & $strDotnet run --project tests/HippoCrm.UiTests --configuration Release --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'UI checks failed.' }
} finally { Pop-Location }
