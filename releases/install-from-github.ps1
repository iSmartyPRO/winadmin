<#
.SYNOPSIS
    Download WinAdmin from GitHub and register as Windows Service.

.EXAMPLE
    .\install-from-github.ps1
    .\install-from-github.ps1 -Port 9090
    .\install-from-github.ps1 -Version 1.0.0 -InstallPath D:\WinAdmin -Port 8080
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
if (-not (Test-Path $downloadScript)) { throw "Not found: $downloadScript (run from releases/ folder or package root)" }

& $downloadScript -Repo $Repo -Version $Version -InstallPath $InstallPath -Token $Token

$installScript = Join-Path $InstallPath 'install-service.ps1'
if (-not (Test-Path $installScript)) { throw "Not found: $installScript" }

& $installScript -InstallPath $InstallPath -Port $Port -ServiceName $ServiceName

Write-Host ""
Write-Host 'Create the first user (if none yet):' -ForegroundColor Yellow
Write-Host "  cd `"$InstallPath`"" -ForegroundColor Yellow
Write-Host '  .\WinAdmin.exe user add --login admin --password "YourPassword" --scopes admin' -ForegroundColor Yellow
Write-Host ""
Write-Host "Open: http://localhost:$Port" -ForegroundColor Green
