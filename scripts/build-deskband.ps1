param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$nativeRoot = Join-Path $projectRoot 'src\InternetMeter.Deskband.Native'
$outputDirectory = Join-Path $nativeRoot "bin\$Configuration"
$outputPath = Join-Path $outputDirectory 'InternetMeter.Deskband.dll'

$compilerCandidates = @(
    'C:\Program Files\CodeBlocks\MinGW\bin\g++.exe',
    'C:\msys64\mingw64\bin\g++.exe'
)
$pathCompiler = Get-Command g++.exe -ErrorAction SilentlyContinue
if ($pathCompiler) { $compilerCandidates += $pathCompiler.Source }
$compiler = $compilerCandidates | Where-Object {
    if (!(Test-Path -LiteralPath $_)) { return $false }
    (& $_ -dumpmachine) -match 'x86_64'
} | Select-Object -First 1
if (!$compiler) { throw 'An x64 MinGW-w64 g++ compiler is required to build the native Windows 10 deskband.' }
$env:PATH = (Split-Path -Parent $compiler) + ';' + $env:PATH

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$optimization = if ($Configuration -eq 'Release') { '-O2' } else { '-O0' }
$arguments = @(
    '-std=c++17', $optimization, '-shared', '-D_WIN32_WINNT=0x0601', '-DNTDDI_VERSION=0x06010000',
    '-static', '-static-libgcc', '-static-libstdc++',
    '-o', $outputPath,
    (Join-Path $nativeRoot 'Deskband.cpp'),
    (Join-Path $nativeRoot 'Deskband.def'),
    '-lole32', '-luuid', '-lgdi32', '-luser32', '-lshell32', '-luxtheme'
)
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw "Native deskband compilation failed with exit code $LASTEXITCODE." }
Write-Host "Native deskband: $outputPath"
