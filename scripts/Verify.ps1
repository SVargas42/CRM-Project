$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    $dotnet = if (Get-Command dotnet -ErrorAction SilentlyContinue) { 'dotnet' } else { Join-Path $PWD '.tools/dotnet/dotnet.exe' }
    & $dotnet build HippoCrm.slnx --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    & $dotnet run --project tests/HippoCrm.Tests --configuration Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Integration checks failed.' }
    & $dotnet run --project tests/HippoCrm.UiTests --configuration Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'UI checks failed.' }
} finally { Pop-Location }
