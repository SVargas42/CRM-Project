$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:NUGET_CERT_REVOCATION_MODE = 'offline'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    $strDotnet = if (Get-Command dotnet -ErrorAction SilentlyContinue) { 'dotnet' } else { Join-Path $env:ProgramFiles 'dotnet/dotnet.exe' }
    # The caller first provisions/restores the approved signed offline feed.
    & $strDotnet build HippoCrm.slnx --configuration Debug --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Feature build failed.' }
    foreach ($arrCheck in @(
        @('tests/HippoCrm.Tests', '--photo-duplicates'),
        @('tests/HippoCrm.UiTests', '--photo-duplicates-ui'),
        @('tests/HippoCrm.UiTests', '--ui-development-checks')
    )) {
        & $strDotnet run --project $arrCheck[0] --configuration Debug --no-build --no-restore --no-launch-profile -- $arrCheck[1]
        if ($LASTEXITCODE -ne 0) { throw ('Feature checks failed: ' + $arrCheck[1]) }
    }
} finally { Pop-Location }
