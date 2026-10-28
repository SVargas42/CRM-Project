param([ValidateSet('win-x64','osx-x64','osx-arm64')][string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    $dotnet = if (Get-Command dotnet -ErrorAction SilentlyContinue) { 'dotnet' } else { Join-Path $PWD '.tools/dotnet/dotnet.exe' }
    & $dotnet publish src/HippoCrm.Desktop --configuration Release --runtime $Runtime --self-contained true -p:PublishSingleFile=false --output "artifacts/$Runtime"
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
} finally { Pop-Location }
