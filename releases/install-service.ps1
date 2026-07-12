<#
.SYNOPSIS
    Install WinAdmin as a Windows service (quick server setup).

.EXAMPLE
    .\install-service.ps1
    .\install-service.ps1 -InstallPath "D:\WinAdmin" -Port 8080
#>
[CmdletBinding()]
param(
    [string]$ServiceName = "WinAdmin",
    [string]$InstallPath = $PSScriptRoot,
    [int]$Port = 8080,
    [string]$DataPath = "C:\ProgramData\WinAdmin"
)

$ErrorActionPreference = "Stop"

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run PowerShell as Administrator."
}

$exe = Join-Path $InstallPath "WinAdmin.Api.exe"
if (-not (Test-Path $exe)) { throw "Not found: $exe" }

New-Item -ItemType Directory -Force -Path $DataPath | Out-Null

$binPath = "`"$exe`" --urls http://0.0.0.0:$Port"
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -eq 'Running') { Stop-Service $ServiceName -Force }
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

sc.exe create $ServiceName binPath= $binPath start= auto DisplayName= "WinAdmin" | Out-Null
sc.exe description $ServiceName "WinAdmin - monitoring and management for Windows" | Out-Null

$dbPath = Join-Path $DataPath "WinAdmin.db"
[Environment]::SetEnvironmentVariable("WinAdmin__DatabasePath", $dbPath, "Machine")
Write-Host "WinAdmin__DatabasePath = $dbPath" -ForegroundColor Yellow
Write-Host "Recommended: also set WinAdmin__Jwt__Secret (see README.md)" -ForegroundColor Yellow

$ruleName = "WinAdmin HTTP $Port"
if (-not (Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow | Out-Null
    Write-Host "Firewall rule added: $ruleName" -ForegroundColor Green
}

Start-Service $ServiceName
Write-Host ""
Write-Host "Service '$ServiceName' started." -ForegroundColor Green
Write-Host "Open: http://localhost:$Port" -ForegroundColor Green
Write-Host ""
Write-Host "Create the first user (if none yet):" -ForegroundColor Yellow
Write-Host "  cd `"$InstallPath`"" -ForegroundColor Yellow
Write-Host '  .\WinAdmin.Api.exe user add --login admin --password "YourPassword" --scopes admin' -ForegroundColor Yellow
