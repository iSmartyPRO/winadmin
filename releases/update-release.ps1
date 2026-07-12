<#
.SYNOPSIS
    Update an existing WinAdmin installation from GitHub Releases.

.DESCRIPTION
    Stops the Windows service (if running), downloads a new release, reinstalls the
    service. Database in C:\ProgramData\WinAdmin is preserved (WinAdmin__DatabasePath).

.EXAMPLE
    .\update-release.ps1
    .\update-release.ps1 -Version 1.0.1 -Port 9090
#>
[CmdletBinding()]
param(
    [string]$Repo = 'iSmartyPRO/winadmin',
    [string]$Version = '',
    [string]$InstallPath = 'C:\WinAdmin',
    [int]$Port = 8080,
    [string]$ServiceName = 'WinAdmin',
    [string]$Token = ''
)

$ErrorActionPreference = 'Stop'

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run PowerShell as Administrator.'
}

$downloadScript = Join-Path $PSScriptRoot 'download-release.ps1'
if (-not (Test-Path $downloadScript)) {
  $downloadScript = Join-Path $InstallPath 'download-release.ps1'
}
if (-not (Test-Path $downloadScript)) { throw "Not found: download-release.ps1" }

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($svc -and $svc.Status -eq 'Running') {
    Write-Host "Stopping service $ServiceName ..."
    Stop-Service $ServiceName -Force
}

& $downloadScript -Repo $Repo -Version $Version -InstallPath $InstallPath -Token $Token

$installScript = Join-Path $InstallPath 'install-service.ps1'
& $installScript -InstallPath $InstallPath -Port $Port -ServiceName $ServiceName

Write-Host ""
Write-Host "Update complete. Service $ServiceName is running on port $Port." -ForegroundColor Green
Write-Host "Database was NOT removed (see WinAdmin__DatabasePath)." -ForegroundColor Yellow
