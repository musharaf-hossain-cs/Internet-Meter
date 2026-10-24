param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$appVersion = '1.0.3'
$localDotNet = Join-Path $projectRoot '.dotnet\dotnet.exe'
$dotnetCommand = if (Test-Path -LiteralPath $localDotNet) { $localDotNet } else { 'dotnet' }
$publishDirectory = Join-Path $projectRoot 'artifacts\publish'
$artifactsDirectory = Join-Path $projectRoot 'artifacts'
$artifactsPrefix = $artifactsDirectory.TrimEnd('\') + '\'
$zipPath = Join-Path $projectRoot "artifacts\InternetMeter-win-x64-$appVersion.zip"
$checksumsPath = Join-Path $projectRoot 'artifacts\SHA256SUMS.txt'

& (Join-Path $PSScriptRoot 'build-deskband.ps1') -Configuration $Configuration

& $dotnetCommand restore (Join-Path $projectRoot 'InternetMeter.sln')
& $dotnetCommand test (Join-Path $projectRoot 'InternetMeter.sln') --no-restore -c $Configuration
& $dotnetCommand publish (Join-Path $projectRoot 'src\InternetMeter\InternetMeter.csproj') `
    --no-restore -c $Configuration -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o $publishDirectory

Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $zipPath -Force

$localIscc = Join-Path $projectRoot '.tools\InnoSetup\ISCC.exe'
$isccCommand = if (Test-Path -LiteralPath $localIscc) { $localIscc } else {
    $installedIscc = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($installedIscc) { $installedIscc.Source } else { $null }
}
if ($isccCommand) {
    & $isccCommand (Join-Path $projectRoot 'installer\InternetMeter.iss')
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup compilation failed with exit code $LASTEXITCODE."
    }
} else {
    Write-Warning 'Inno Setup was not found; the portable build is ready but the installer was not compiled.'
}

$checksumTargets = @(
    (Join-Path $publishDirectory 'InternetMeter.exe'),
    (Join-Path $publishDirectory 'InternetMeter.Deskband.dll'),
    $zipPath
) + @(
    Get-ChildItem (Join-Path $projectRoot 'artifacts\installer\InternetMeter-Setup-*.exe') -File -ErrorAction SilentlyContinue |
        Sort-Object Name |
        Select-Object -ExpandProperty FullName
)
$checksumTargets = $checksumTargets | Where-Object { Test-Path -LiteralPath $_ }
$checksumLines = foreach ($target in $checksumTargets) {
    if (!$target.StartsWith($artifactsPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Checksum target is outside the artifacts directory: $target"
    }
    $relativePath = $target.Substring($artifactsPrefix.Length).Replace('\', '/')
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $target).Hash
    "$hash  $relativePath"
}
[IO.File]::WriteAllLines($checksumsPath, $checksumLines)

Write-Host "Published: $publishDirectory"
Write-Host "Archive:   $zipPath"
Write-Host "Checksums: $checksumsPath"

