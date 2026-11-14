#requires -version 3.0
<#
    Packages a release ZIP for Windows Audio Keys from the built product pair.

    Steps:
      1. Builds and tests everything (build.ps1 -Test).
      2. Reads the release version from bin\WindowsAudioKeys.exe (see
         src\AssemblyInfo.cs), requires it to be major.minor.patch, and requires
         FileVersion and AssemblyVersion to be that version plus ".0".
      3. Verifies the published root pair matches the bin copies byte for byte.
      4. Requires README.md, LICENSE and CHANGELOG.md at the repository root, and
         requires CHANGELOG.md to document the release version.
      5. Packs dist\WindowsAudioKeys-v<ver>.zip with the five release files at
         the archive root.
      6. Writes dist\WindowsAudioKeys-v<ver>.zip.sha256 next to the ZIP.

    Only dist\ is created or cleaned; the build already publishes the root pair.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$projectRoot = $PSScriptRoot
$productExeName = 'WindowsAudioKeys.exe'
$productConfigName = $productExeName + '.config'
$outputDir = Join-Path $projectRoot 'bin'
$distDir = Join-Path $projectRoot 'dist'
$rootProduct = Join-Path $projectRoot $productExeName
$rootProductConfig = Join-Path $projectRoot $productConfigName

# 1. 빌드와 테스트. build.ps1 은 실패 시 예외를 던진다.
& (Join-Path $projectRoot 'build.ps1') -Test
if ($LASTEXITCODE -ne 0) {
    throw "build.ps1 failed with exit code $LASTEXITCODE"
}

# 2. 릴리스 버전: 빌드된 실행 파일의 ProductVersion(= AssemblyInformationalVersion).
#    major.minor.patch 형식이 아니면 어디를 고쳐야 하는지 알려주고 멈춘다.
$binProduct = Join-Path $outputDir $productExeName
$versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($binProduct)
$version = $versionInfo.ProductVersion.Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw ("ProductVersion '{0}' is not major.minor.patch. " +
        "Fix AssemblyInformationalVersion in src\AssemblyInfo.cs and rebuild.") -f $version
}

# 2-1. 네 자리 버전(FileVersion, AssemblyVersion)은 제품 버전 뒤에 ".0" 을 붙인 값과 같아야 한다.
#      셋 중 하나라도 다르면 어느 값이 어긋났는지 알려주고 멈춘다.
$expectedAssemblyVersion = $version + '.0'
$fileVersion = ([string]$versionInfo.FileVersion).Trim()
$assemblyVersion = [System.Reflection.AssemblyName]::GetAssemblyName($binProduct).Version.ToString()
if (-not $fileVersion.Equals($expectedAssemblyVersion, [System.StringComparison]::Ordinal) -or
    -not $assemblyVersion.Equals($expectedAssemblyVersion, [System.StringComparison]::Ordinal)) {
    throw ("Version mismatch: ProductVersion '{0}', FileVersion '{1}', AssemblyVersion '{2}'. " +
        "All three must agree, with FileVersion and AssemblyVersion equal to '{0}.0'. " +
        "Align AssemblyVersion, AssemblyFileVersion and AssemblyInformationalVersion " +
        "in src\AssemblyInfo.cs and rebuild.") -f $version, $fileVersion, $assemblyVersion
}

# 3. 공개된 루트 실행 파일/설정이 bin 산출물과 같은지 해시로 확인한다.
# Get-FileHash 는 기본 제공 Utility 모듈에 있다. PSModulePath 에 다른 PowerShell 의
# 모듈 경로가 섞여 있으면 5.1 이 그 모듈을 먼저 불러와 명령을 못 찾으므로,
# 명령이 없을 때만 기본 제공 모듈을 경로로 지정해 불러온다.
function Get-Sha256([string]$Path) {
    if (-not (Get-Command Get-FileHash -ErrorAction SilentlyContinue)) {
        $inboxUtility = Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Utility\Microsoft.PowerShell.Utility.psd1'
        if (Test-Path -LiteralPath $inboxUtility) {
            Import-Module $inboxUtility -Force
        }
    }

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Assert-SameContent {
    param(
        [string]$Left,
        [string]$Right
    )

    if (-not (Test-Path -LiteralPath $Left)) {
        throw "Missing published file: $Left"
    }

    if (-not (Test-Path -LiteralPath $Right)) {
        throw "Missing build output: $Right"
    }

    $leftHash = Get-Sha256 $Left
    $rightHash = Get-Sha256 $Right
    if ($leftHash -ne $rightHash) {
        throw ("Published file does not match the build output: {0} ({1}) vs {2} ({3}). " +
            "Run build.ps1 again and commit the published pair.") -f $Left, $leftHash, $Right, $rightHash
    }
}

Assert-SameContent $rootProduct $binProduct
Assert-SameContent $rootProductConfig (Join-Path $outputDir $productConfigName)

# 4. 배포 문서(README, LICENSE, CHANGELOG)가 저장소 루트에 있어야 한다.
#    CHANGELOG 에는 이번 릴리스 버전 항목("## [<버전>]")이 있어야 한다.
foreach ($name in @('README.md', 'LICENSE', 'CHANGELOG.md')) {
    $path = Join-Path $projectRoot $name
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing release document at the repository root: $path"
    }
}

# CHANGELOG 는 UTF-8 로 읽고, 파일 어느 줄에서든 "## [<버전>]" 으로 시작하는 줄을 찾는다.
$changelogText = [System.IO.File]::ReadAllText(
    (Join-Path $projectRoot 'CHANGELOG.md'),
    [System.Text.Encoding]::UTF8)
$changelogPattern = '^## \[' + [regex]::Escape($version) + '\]'
if (-not [regex]::IsMatch(
        $changelogText,
        $changelogPattern,
        [System.Text.RegularExpressions.RegexOptions]::Multiline)) {
    throw ("CHANGELOG.md has no entry for version '{0}'. " +
        "Add a '## [{0}]' section to CHANGELOG.md before packaging.") -f $version
}

# 5. ZIP: dist 안의 새 준비 폴더에 다섯 파일만 모은 뒤 루트 기준으로 압축한다.
if (-not (Test-Path -LiteralPath $distDir)) {
    New-Item -ItemType Directory -Path $distDir -Force | Out-Null
}

$packageName = 'WindowsAudioKeys-v' + $version
$staging = Join-Path $distDir $packageName
$zip = Join-Path $distDir ($packageName + '.zip')

# 준비 폴더만 지운다. dist 밖 경로는 절대 지우지 않는다.
if (Test-Path -LiteralPath $staging) {
    $resolvedDist = (Resolve-Path -LiteralPath $distDir).Path.TrimEnd('\')
    $resolvedStaging = (Resolve-Path -LiteralPath $staging).Path.TrimEnd('\')
    if (-not $resolvedStaging.StartsWith($resolvedDist + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a folder outside dist: $resolvedStaging"
    }

    Remove-Item -LiteralPath $resolvedStaging -Recurse -Force
}

New-Item -ItemType Directory -Path $staging -Force | Out-Null

$releaseFiles = @($productExeName, $productConfigName, 'README.md', 'LICENSE', 'CHANGELOG.md')
foreach ($name in $releaseFiles) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination (Join-Path $staging $name) -Force
}

Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip -Force
Remove-Item -LiteralPath $staging -Recurse -Force

# 6. 체크섬 파일: "<소문자 해시>  <파일 이름>" 한 줄, ASCII, 끝에 줄바꿈.
$zipHash = (Get-Sha256 $zip).ToLowerInvariant()
$checksumPath = $zip + '.sha256'
$checksumText = $zipHash + '  ' + $packageName + '.zip' + "`n"
[System.IO.File]::WriteAllText($checksumPath, $checksumText, [System.Text.Encoding]::ASCII)

# 7. 결과 요약.
Write-Host ("Version: {0}" -f $version)
Write-Host ("Package: {0}" -f $zip)
Write-Host ("SHA256: {0}" -f $zipHash)
