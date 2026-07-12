<#
.SYNOPSIS
    Self-contained сборка WinAdmin для переноса на любую машину без зависимостей.

.DESCRIPTION
    1. Собирает React-фронтенд (npm run build → src/backend/WinAdmin.Api/wwwroot).
    2. Публикует backend как self-contained приложение — runtime .NET встроен в пакет.
    3. Итог: папка releases/dist/ готова к копированию. Node.js и .NET не нужны на целевой машине.

.PARAMETER OutputPath
    Куда положить готовый пакет. По умолчанию: releases/dist/ внутри проекта.

.PARAMETER Runtime
    RID платформы. По умолчанию: win-x64. Для 32-bit: win-x86.

.EXAMPLE
    .\releases\build.ps1
    .\releases\build.ps1 -OutputPath "C:\Builds\WinAdmin-1.0"
#>
[CmdletBinding()]
param(
    [string]$OutputPath = "$PSScriptRoot\dist",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$Root = Resolve-Path "$PSScriptRoot\.."

function Step($msg) { Write-Host "`n==> $msg" -ForegroundColor Cyan }
function Ok($msg)   { Write-Host "    $msg" -ForegroundColor Green }

# ── 1. Frontend ────────────────────────────────────────────────────────────────
Step "Сборка фронтенда (npm run build)"
Push-Location "$Root\src\frontend"
try {
    npm run build
    if ($LASTEXITCODE -ne 0) { throw "npm run build завершился с ошибкой." }
    Ok "Статика → src/backend/WinAdmin.Api/wwwroot"
} finally {
    Pop-Location
}

# ── 2. Backend (self-contained) ────────────────────────────────────────────────
Step "Публикация backend self-contained ($Runtime)"
dotnet publish "$Root\src\backend\WinAdmin.Api\WinAdmin.Api.csproj" `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    --output $OutputPath `
    /p:PublishSingleFile=false
if ($LASTEXITCODE -ne 0) { throw "dotnet publish завершился с ошибкой." }
Ok "Пакет → $OutputPath"

# ── 3. Документация и скрипты установки ─────────────────────────────────────────
Step "Копирование документации пакета"
$PackageDir = "$PSScriptRoot\package"
if (Test-Path $PackageDir) {
    Copy-Item "$PackageDir\*" $OutputPath -Recurse -Force
    Copy-Item "$PSScriptRoot\install-service.ps1" $OutputPath -Force
    Copy-Item "$PSScriptRoot\download-release.ps1" $OutputPath -Force
    Copy-Item "$PSScriptRoot\update-release.ps1" $OutputPath -Force
    Copy-Item "$PSScriptRoot\install-from-github.ps1" $OutputPath -Force
    Ok "README, docs, scripts -> $OutputPath"
}

# ── 4. Итог ───────────────────────────────────────────────────────────────────
$SizeMb = [math]::Round((Get-ChildItem $OutputPath -Recurse | Measure-Object Length -Sum).Sum / 1MB)
Write-Host "`nГотово. Размер пакета: ~$SizeMb МБ" -ForegroundColor Green
Write-Host @"

Перенос на целевую машину:
  1. Скопируйте папку '$OutputPath' на целевую машину
  2. Запуск без IIS (нулевые зависимости):
       .\WinAdmin.exe --urls http://localhost:8080
  3. Запуск как Windows Service:
       .\install-service.ps1 -Port 8080
  4. Под IIS: нужен ASP.NET Core Hosting Bundle (~25 МБ), затем releases\install.ps1
"@ -ForegroundColor Yellow
