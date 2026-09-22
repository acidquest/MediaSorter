param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    if (-not $SkipTests) {
        dotnet run --project Sorter.Tests/Sorter.Tests.csproj -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Safety tests failed.' }
        dotnet run --project Sorter.MetadataTests/Sorter.MetadataTests.csproj -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Metadata tests failed.' }
    }
    dotnet publish MediaSorter/MediaSorter.csproj -c Release -p:Platform=x64 -o artifacts/MediaSorter
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Write-Host 'Portable app: artifacts\MediaSorter\MediaSorter.exe'
} finally { Pop-Location }
