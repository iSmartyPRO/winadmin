<#
.SYNOPSIS
    Download WinAdmin release from GitHub and extract to target folder.

.DESCRIPTION
    Fetches WinAdmin-*-win-x64.zip from GitHub Releases (latest or specific version).
    No git or .NET required on the target machine.

.PARAMETER Repo
    GitHub repository in owner/name form.

.PARAMETER Version
    Release version without 'v' prefix (e.g. 1.0.0). Empty = latest release.

.PARAMETER InstallPath
    Folder to extract the package into.

.PARAMETER Token
    GitHub PAT for private repositories (optional).

.EXAMPLE
    .\download-release.ps1
    .\download-release.ps1 -Version 1.0.0 -InstallPath D:\WinAdmin
#>
[CmdletBinding()]
param(
    [string]$Repo = 'iSmartyPRO/winadmin',
    [string]$Version = '',
    [string]$InstallPath = 'C:\WinAdmin',
    [string]$Token = ''
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Get-GitHubHeaders {
    $h = @{ 'User-Agent' = 'WinAdmin-Installer' }
    if ($Token) { $h['Authorization'] = "Bearer $Token" }
    return $h
}

if ($Version) {
    $apiUrl = "https://api.github.com/repos/$Repo/releases/tags/v$Version"
    Write-Host "Fetching release v$Version ..."
} else {
    $apiUrl = "https://api.github.com/repos/$Repo/releases/latest"
    Write-Host 'Fetching latest release ...'
}

$release = Invoke-RestMethod -Uri $apiUrl -Headers (Get-GitHubHeaders) -UseBasicParsing
$asset = $release.assets | Where-Object { $_.name -like 'WinAdmin-*-win-x64.zip' } | Select-Object -First 1
if (-not $asset) { throw "No WinAdmin-*-win-x64.zip asset found in release $($release.tag_name)" }

$zipPath = Join-Path $env:TEMP $asset.name
Write-Host "Downloading $($asset.name) ($([math]::Round($asset.size / 1MB, 1)) MB) ..."

$dlHeaders = Get-GitHubHeaders
$dlHeaders['Accept'] = 'application/octet-stream'
Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zipPath -Headers $dlHeaders -UseBasicParsing

if (Test-Path $InstallPath) {
    Write-Host "Removing existing $InstallPath ..."
    Remove-Item $InstallPath -Recurse -Force
}
New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
Expand-Archive -Path $zipPath -DestinationPath $InstallPath -Force
Remove-Item $zipPath -Force -ErrorAction SilentlyContinue

$ver = $release.tag_name -replace '^v', ''
Write-Host ""
Write-Host "Done. WinAdmin $ver installed to $InstallPath" -ForegroundColor Green
Write-Host "Next: cd $InstallPath; .\install-service.ps1 -Port 8080" -ForegroundColor Yellow
