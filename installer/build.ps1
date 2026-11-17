# PlayTimeTracker 설치 파일 만들기
#   1) 앱을 실행 파일 하나로 게시 (.NET 런타임 포함, 따로 설치할 필요 없음) → artifacts\app
#   2) Inno Setup으로 설치 파일 생성 → artifacts\installer\PlayTimeTracker-Setup-<버전>.exe
#
# 사용: powershell -ExecutionPolicy Bypass -File installer\build.ps1 [-Version 1.0.0]
param([string]$Version = "1.0.0")
$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot -Parent
$appOut = Join-Path $root "artifacts\app"

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6이 없습니다. 설치: winget install JRSoftware.InnoSetup" }

if (Test-Path $appOut) { Remove-Item $appOut -Recurse -Force }
dotnet publish (Join-Path $root "src\PlayTimeTracker.App") -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -p:Version=$Version -o $appOut -nologo
if ($LASTEXITCODE -ne 0) { throw "게시 실패" }

& $iscc "/DAppVersion=$Version" (Join-Path $PSScriptRoot "PlayTimeTracker.iss")
if ($LASTEXITCODE -ne 0) { throw "설치 파일 만들기 실패" }

Get-Item (Join-Path $root "artifacts\installer\PlayTimeTracker-Setup-$Version.exe")
