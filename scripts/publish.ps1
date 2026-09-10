<#
.SYNOPSIS
    Builds a self-contained release bundle for OpenKaraoke.

.DESCRIPTION
    Publishes the Avalonia desktop project for the requested runtime identifier and
    copies the documentation (README/LINUX guide/third-party notices) plus (for
    linux-x64) the desktop-entry installer into the output folder. The bundle is
    framework-independent, so the target PC does not need a .NET runtime installed.

.EXAMPLE
    pwsh scripts/publish.ps1 -Runtime win-x64

.EXAMPLE
    pwsh scripts/publish.ps1 -Runtime linux-x64
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'linux-x64')]
    [string]$Runtime = 'win-x64',

    # Optional absolute output folder. Defaults to <repo>/artifacts/<runtime>.
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'src\OpenKaraoke.Desktop\OpenKaraoke.Desktop.csproj'

if (-not (Test-Path $projectPath)) {
    throw "프로젝트 파일을 찾을 수 없습니다: $projectPath"
}

function Resolve-DotNet {
    $command = Get-Command dotnet -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $command) {
        return $command.Source
    }

    $local = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
    if (Test-Path $local) {
        return $local
    }

    throw 'dotnet SDK를 찾을 수 없습니다. https://dotnet.microsoft.com/download/dotnet/8.0 에서 .NET 8 SDK를 설치하세요.'
}

$dotnet = Resolve-DotNet

$isDefaultOutput = [string]::IsNullOrWhiteSpace($OutputDirectory)
if ($isDefaultOutput) {
    $OutputDirectory = Join-Path $repoRoot "artifacts\$Runtime"
}

# Only folders we own (artifacts/<rid>) are wiped, so a custom path is never deleted.
if ($isDefaultOutput -and (Test-Path $OutputDirectory)) {
    Remove-Item -LiteralPath $OutputDirectory -Recurse -Force
}

Write-Host "[1/2] Publishing $Runtime (self-contained)..." -ForegroundColor Cyan
& $dotnet publish $projectPath `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -o $OutputDirectory

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish 실패 (exit code $LASTEXITCODE)"
}

Write-Host '[2/2] Copying documentation and desktop files...' -ForegroundColor Cyan

Copy-Item (Join-Path $repoRoot 'THIRD-PARTY-NOTICES.md') -Destination $OutputDirectory -Force
Copy-Item (Join-Path $repoRoot 'README.md') -Destination $OutputDirectory -Force
Copy-Item (Join-Path $repoRoot 'LINUX.md') -Destination $OutputDirectory -Force

if ($Runtime -eq 'linux-x64') {
    Copy-Item (Join-Path $repoRoot 'packaging\open-karaoke.desktop.in') -Destination $OutputDirectory -Force
    Copy-Item (Join-Path $repoRoot 'packaging\install-desktop-entry.sh') -Destination $OutputDirectory -Force
}

$size = [math]::Round((Get-ChildItem $OutputDirectory -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1)

Write-Host ''
Write-Host "완료: $OutputDirectory ($size MB)" -ForegroundColor Green
if ($Runtime -eq 'win-x64') {
    Write-Host '실행: OpenKaraoke.exe'
} else {
    Write-Host '리눅스 PC로 폴더 전체를 복사한 뒤:'
    Write-Host '  chmod +x OpenKaraoke install-desktop-entry.sh'
    Write-Host '  ./OpenKaraoke                      # 실행'
    Write-Host '  ./install-desktop-entry.sh         # 앱 메뉴 등록(선택)'
    Write-Host 'ffmpeg가 설치되어 있어야 합니다: sudo apt install ffmpeg fonts-noto-cjk'
    Write-Host '자세한 설치·설정 절차: LINUX.md 참고'
}
