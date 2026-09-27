# 建置可發佈的單一執行檔到 dist\DeskFrames.exe
# 需求：.NET 10 SDK（執行端需 .NET 10 Desktop Runtime；加上 -SelfContained 則不需要）
param([switch]$SelfContained)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$running = Get-Process DeskFrames -ErrorAction SilentlyContinue
if ($running) { Write-Host '偵測到 DeskFrames 正在執行，請先從系統匣「結束」後再建置。' -ForegroundColor Yellow; exit 1 }

$sc = if ($SelfContained) { 'true' } else { 'false' }
# 清除 win-x64 中間檔：切換 self-contained 與否時，殘留的 ReadyToRun 檔會導致啟動失敗
Remove-Item obj\Release\net10.0-windows\win-x64, dist -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish DeskFrames.csproj -c Release -r win-x64 "-p:SelfContained=$sc" `
    -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none -o dist
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "`n完成：$(Join-Path $PSScriptRoot 'dist\DeskFrames.exe')" -ForegroundColor Green
