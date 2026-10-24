#requires -version 3.0
<#
    Builds the complete Windows Audio Keys suite with the in-box .NET Framework
    compiler. No SDK, NuGet or downloads.

    Outputs:
      bin\WindowsAudioKeys.exe       (WinExe app, build copy)
      bin\AudioProbe.exe             (diagnostic probe)
      bin\GameAudioKeys.Tests.exe    (noninteractive logic tests)
      bin\AudioFixture.exe           (silent audio fixture for end-to-end tests)

    After a successful build (and successful tests with -Test) the ready-to-run
    product pair is published at the repository root:
      WindowsAudioKeys.exe
      WindowsAudioKeys.exe.config

    The build never stops running instances: if WindowsAudioKeys.exe is running,
    the build stops with an instruction to close it from the tray menu first.

    Pass -Test to run the probe self-test and the console tests before publishing.
#>
[CmdletBinding()]
param(
    [switch]$Test
)

$ErrorActionPreference = 'Stop'

$projectRoot = $PSScriptRoot
$frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $frameworkDir 'csc.exe'
$webExtensions = Join-Path $frameworkDir 'System.Web.Extensions.dll'
$winForms = Join-Path $frameworkDir 'System.Windows.Forms.dll'
$drawing = Join-Path $frameworkDir 'System.Drawing.dll'
$outputDir = Join-Path $projectRoot 'bin'

$core = Join-Path $projectRoot 'src\CoreAudio.cs'
$settings = Join-Path $projectRoot 'src\Settings.cs'
$appLogic = Join-Path $projectRoot 'src\AppLogic.cs'
$hotkeys = Join-Path $projectRoot 'src\HotkeyManager.cs'
$startup = Join-Path $projectRoot 'src\Startup.cs'
$startupService = Join-Path $projectRoot 'src\StartupTaskService.cs'
$mainForm = Join-Path $projectRoot 'src\MainForm.cs'
$trayContext = Join-Path $projectRoot 'src\TrayAppContext.cs'
$program = Join-Path $projectRoot 'src\Program.cs'
$appConfig = Join-Path $projectRoot 'src\app.config'
$appManifest = Join-Path $projectRoot 'src\app.manifest'
$tests = Join-Path $projectRoot 'tests\Tests.cs'
$fixture = Join-Path $projectRoot 'fixture\AudioFixture.cs'
$probeBuild = Join-Path $projectRoot 'probe\build.ps1'

$productExeName = 'WindowsAudioKeys.exe'
$rootProduct = Join-Path $projectRoot $productExeName
$rootProductConfig = $rootProduct + '.config'

$required = @(
    $compiler
    $webExtensions
    $winForms
    $drawing
    $core
    $settings
    $appLogic
    $hotkeys
    $startup
    $startupService
    $mainForm
    $trayContext
    $program
    $appConfig
    $appManifest
    $tests
    $fixture
    $probeBuild
)

foreach ($path in $required) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Required file not found: $path"
    }
}

function Assert-ProductNotRunning {
    # Never terminate a user process. Only report it and stop the build.
    $running = @(Get-Process -Name 'WindowsAudioKeys' -ErrorAction SilentlyContinue)
    if ($running.Count -gt 0) {
        $ids = @($running | ForEach-Object { $_.Id }) -join ', '
        $message = "Windows Audio Keys is running ($productExeName, PID: $ids). " +
            "Close it from the tray menu (Exit) or the settings window, then build again. " +
            "The build does not stop running instances."
        throw $message
    }
}

Assert-ProductNotRunning

if (-not (Test-Path -LiteralPath $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

function Invoke-Compiler {
    param(
        [string]$Description,
        [string[]]$Arguments
    )

    & $compiler $Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with csc.exe exit code $LASTEXITCODE"
    }
}

$common = @(
    '/nologo'
    '/platform:anycpu'
    '/optimize+'
    '/warn:4'
    '/codepage:65001'
)

$appOutput = Join-Path $outputDir $productExeName
Invoke-Compiler -Description 'Application build' -Arguments ($common + @(
    '/target:winexe'
    '/win32manifest:' + $appManifest
    '/r:' + $winForms
    '/r:' + $drawing
    '/r:' + $webExtensions
    '/out:' + $appOutput
    $core
    $settings
    $appLogic
    $hotkeys
    $startup
    $mainForm
    $trayContext
    $startupService
    $program
))

Copy-Item -LiteralPath $appConfig -Destination ($appOutput + '.config') -Force

$testOutput = Join-Path $outputDir 'GameAudioKeys.Tests.exe'
Invoke-Compiler -Description 'Test build' -Arguments ($common + @(
    '/target:exe'
    '/r:' + $winForms
    '/r:' + $webExtensions
    '/out:' + $testOutput
    $core
    $settings
    $appLogic
    $startup
    $startupService
    $tests
))

$fixtureOutput = Join-Path $outputDir 'AudioFixture.exe'
Invoke-Compiler -Description 'Fixture build' -Arguments ($common + @(
    '/target:winexe'
    '/r:' + $winForms
    '/r:' + $drawing
    '/out:' + $fixtureOutput
    $fixture
))

# The probe keeps its own build script so it can still be built in isolation.
& $probeBuild
if ($LASTEXITCODE -ne 0) {
    throw "Probe build failed with exit code $LASTEXITCODE"
}

Write-Host ("Built: {0}" -f $appOutput)
Write-Host ("Built: {0}" -f $testOutput)
Write-Host ("Built: {0}" -f $fixtureOutput)
Write-Host ("Built: {0}" -f (Join-Path $outputDir 'AudioProbe.exe'))

if ($Test) {
    & (Join-Path $outputDir 'AudioProbe.exe') --self-test
    if ($LASTEXITCODE -ne 0) {
        throw "AudioProbe --self-test failed with exit code $LASTEXITCODE"
    }

    & $testOutput
    if ($LASTEXITCODE -ne 0) {
        throw "GameAudioKeys.Tests.exe failed with exit code $LASTEXITCODE"
    }
}

# Publish the exact pair last, after tests. Nothing at the repository root is
# touched while a running product could still depend on it.
Assert-ProductNotRunning
Copy-Item -LiteralPath $appOutput -Destination $rootProduct -Force
Copy-Item -LiteralPath ($appOutput + '.config') -Destination $rootProductConfig -Force

Write-Host ("Published: {0}" -f $rootProduct)
Write-Host ("Published: {0}" -f $rootProductConfig)
