<#
.SYNOPSIS
    Публикация и установка WinAdmin как сайта/приложения IIS.

.DESCRIPTION
    1. Публикует приложение (dotnet publish) в целевой каталог.
    2. Создаёт пул приложения, работающий под указанной сервисной учётной записью
       (нужны права администратора для управления службами и питанием).
    3. Создаёт сайт IIS на заданном порту.
    4. Готовит каталог данных для SQLite (БД и аудит).

    Требует прав администратора и установленного ASP.NET Core Hosting Bundle.

.EXAMPLE
    .\install.ps1 -SiteName "WinAdmin" -Port 8080 `
        -PhysicalPath "C:\inetpub\WinAdmin" `
        -ServiceAccount "DOMAIN\svc-WinAdmin" -ServicePassword (Read-Host -AsSecureString)
#>
[CmdletBinding()]
param(
    [string]$SiteName = "WinAdmin",
    [int]$Port = 8080,
    [string]$PhysicalPath = "C:\inetpub\WinAdmin",
    [string]$DataPath = "C:\ProgramData\WinAdmin",
    [string]$ProjectPath = "$PSScriptRoot\..\src\backend\WinAdmin.Api\WinAdmin.Api.csproj",

    [Parameter(Mandatory = $true)][string]$ServiceAccount,
    [Parameter(Mandatory = $true)][securestring]$ServicePassword
)

$ErrorActionPreference = "Stop"

Write-Host "==> Проверка прав администратора" -ForegroundColor Cyan
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Запустите скрипт от имени администратора."
}

Import-Module WebAdministration

Write-Host "==> Публикация приложения в $PhysicalPath" -ForegroundColor Cyan
dotnet publish $ProjectPath -c Release -o $PhysicalPath
if ($LASTEXITCODE -ne 0) { throw "dotnet publish завершился с ошибкой." }

Write-Host "==> Каталог данных $DataPath" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $DataPath | Out-Null
# Доступ сервисной учётки к каталогу данных
icacls $DataPath /grant "${ServiceAccount}:(OI)(CI)F" | Out-Null

$plainPwd = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
    [Runtime.InteropServices.Marshal]::SecureStringToBSTR($ServicePassword))

Write-Host "==> Пул приложения $SiteName" -ForegroundColor Cyan
if (Test-Path "IIS:\AppPools\$SiteName") { Remove-WebAppPool -Name $SiteName }
New-WebAppPool -Name $SiteName | Out-Null
Set-ItemProperty "IIS:\AppPools\$SiteName" -Name managedRuntimeVersion -Value ""   # No Managed Code
Set-ItemProperty "IIS:\AppPools\$SiteName" -Name processModel.identityType -Value SpecificUser
Set-ItemProperty "IIS:\AppPools\$SiteName" -Name processModel.userName -Value $ServiceAccount
Set-ItemProperty "IIS:\AppPools\$SiteName" -Name processModel.password -Value $plainPwd

Write-Host "==> Сайт IIS $SiteName на порту $Port" -ForegroundColor Cyan
if (Test-Path "IIS:\Sites\$SiteName") { Remove-Website -Name $SiteName }
New-Website -Name $SiteName -Port $Port -PhysicalPath $PhysicalPath -ApplicationPool $SiteName | Out-Null

Write-Host "==> Настройка пути к БД (WinAdmin__DatabasePath)" -ForegroundColor Cyan
$env:WinAdmin__DatabasePath = Join-Path $DataPath "WinAdmin.db"
# Рекомендуется прописать переменную в web.config или appsettings.Production.json (см. docs/01-deploy-iis.md)

Start-Website -Name $SiteName
Write-Host "`nГотово. WinAdmin доступен на http://localhost:$Port" -ForegroundColor Green
Write-Host "Стартовый admin-ключ будет записан в $PhysicalPath\bootstrap-key.txt при первом запросе." -ForegroundColor Yellow
Write-Host "ВАЖНО: убедитесь, что учётная запись '$ServiceAccount' состоит в группе локальных администраторов" -ForegroundColor Yellow
Write-Host "       и имеет привилегию 'Завершение работы системы' (SeShutdownPrivilege)." -ForegroundColor Yellow
