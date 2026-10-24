#requires -version 3.0
<#
    Builds bin\AudioProbe.exe with the in-box .NET Framework C# compiler.
    No NuGet, no SDK, no downloads.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$probeDir = $PSScriptRoot
$projectRoot = Split-Path -Parent $probeDir
$frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $frameworkDir 'csc.exe'
$webExtensions = Join-Path $frameworkDir 'System.Web.Extensions.dll'
$source = Join-Path $probeDir 'AudioProbe.cs'
$sharedCore = Join-Path $projectRoot 'src\CoreAudio.cs'
$outputDir = Join-Path $projectRoot 'bin'
$outputExe = Join-Path $outputDir 'AudioProbe.exe'

if (-not (Test-Path -LiteralPath $compiler)) {
    throw "C# compiler not found: $compiler"
}

if (-not (Test-Path -LiteralPath $webExtensions)) {
    throw "System.Web.Extensions.dll not found: $webExtensions"
}

if (-not (Test-Path -LiteralPath $source)) {
    throw "Source file not found: $source"
}

if (-not (Test-Path -LiteralPath $sharedCore)) {
    throw "Shared core source not found: $sharedCore"
}

if (-not (Test-Path -LiteralPath $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

$compilerArgs = @(
    '/nologo'
    '/target:exe'
    '/platform:anycpu'
    '/optimize+'
    '/warn:4'
    '/codepage:65001'   # source is UTF-8 without BOM
    '/r:' + $webExtensions
    '/out:' + $outputExe
    $sharedCore
    $source
)

& $compiler $compilerArgs
if ($LASTEXITCODE -ne 0) {
    throw "csc.exe failed with exit code $LASTEXITCODE"
}

Write-Host ("Built: {0}" -f $outputExe)
