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
    # Publish into an empty directory so removed dependencies cannot survive an incremental publish.
    $publishId = [guid]::NewGuid().ToString('N')
    $publishStage = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".build/publish-$publishId"))
    $publishOutput = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'artifacts/MediaSorter'))
    $publishRetired = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".build/retired-$publishId"))
    dotnet publish MediaSorter/MediaSorter.csproj -c Release -p:Platform=x64 -o $publishStage
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    foreach ($required in @('MediaSorter.exe', 'MediaSorter.deps.json', 'MediaSorter.pri', 'Microsoft.ui.xaml.dll', 'coreclr.dll', 'Locales/ru.json', 'Locales/en.json')) {
        if (-not (Test-Path -LiteralPath (Join-Path $publishStage $required))) { throw "Missing publish file: $required" }
    }
    foreach ($excluded in @('onnxruntime.dll', 'DirectML.dll', 'Microsoft.Windows.Widgets.dll', 'Microsoft.Windows.AI.MachineLearning.dll', 'MediaSorter.pdb', 'Sorter.Core.pdb')) {
        if (Test-Path -LiteralPath (Join-Path $publishStage $excluded)) { throw "Unexpected unused publish file: $excluded" }
    }
    # Only these generated directories may be moved/deleted; never follow reparse points.
    foreach ($checkedPath in @($publishStage, $publishOutput, $publishRetired)) {
        $expectedParent = if ($checkedPath -eq $publishOutput) { Join-Path $PSScriptRoot 'artifacts' } else { Join-Path $PSScriptRoot '.build' }
        if ([IO.Path]::GetDirectoryName($checkedPath) -ne [IO.Path]::GetFullPath($expectedParent)) { throw 'Unsafe publish path.' }
        $ancestor = $checkedPath
        while ($ancestor) {
            if ((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Reparse point: $ancestor" }
            $ancestor = [IO.Path]::GetDirectoryName($ancestor)
        }
        if (Test-Path -LiteralPath $checkedPath) {
            if (Get-ChildItem -LiteralPath $checkedPath -Force -Recurse | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'Reparse point in publish contents.' }
        }
    }
    if (Get-Process -Name MediaSorter -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $publishOutput 'MediaSorter.exe') }) { throw 'Close MediaSorter before replacing the portable build.' }
    New-Item -ItemType Directory -Path (Split-Path $publishOutput) -Force | Out-Null
    if (Test-Path -LiteralPath $publishOutput) { Move-Item -LiteralPath $publishOutput -Destination $publishRetired }
    try { Move-Item -LiteralPath $publishStage -Destination $publishOutput }
    catch {
        if (Test-Path -LiteralPath $publishRetired) { Move-Item -LiteralPath $publishRetired -Destination $publishOutput }
        throw
    }
    if (Test-Path -LiteralPath $publishRetired) { Remove-Item -LiteralPath $publishRetired -Recurse -Force }
    $publishedFiles = @(Get-ChildItem -LiteralPath $publishOutput -File -Recurse)
    $publishedMiB = [math]::Round(($publishedFiles | Measure-Object Length -Sum).Sum / 1MB, 2)
    Write-Host "Published $($publishedFiles.Count) files, $publishedMiB MiB."
    Write-Host 'Portable app: artifacts\MediaSorter\MediaSorter.exe'
} finally { Pop-Location }
