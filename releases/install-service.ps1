<#
.SYNOPSIS
    Install WinAdmin as a Windows service (quick server setup).

.DESCRIPTION
    - Registers the WinAdmin service (LocalSystem, auto start) without --urls:
      address and port come from network.json in the data folder.
    - Creates network.json (mode "local" = 127.0.0.1 only) if it does not exist.
    - Restricts folder permissions: install folder writable by Administrators/SYSTEM only,
      data folder (database, network.json) accessible by Administrators/SYSTEM only.
    - Removes the legacy "WinAdmin HTTP <port>" firewall rule (open to any address).
      Network access is enabled later with: WinAdmin.exe network set --mode network --allow <subnets>

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

$exe = Join-Path $InstallPath "WinAdmin.exe"
if (-not (Test-Path $exe)) { throw "Not found: $exe" }

New-Item -ItemType Directory -Force -Path $DataPath | Out-Null

# Refuse drive roots and shared system folders: icacls /reset /T there would break the machine.
function Test-SafeAclTarget([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    if ($full.Length -le 2) { return $false }
    $shared = @($env:ProgramData, $env:ProgramFiles, ${env:ProgramFiles(x86)}, $env:windir,
                (Join-Path $env:SystemDrive 'Users')) | Where-Object { $_ }
    foreach ($s in $shared) {
        if ($full -ieq $s.TrimEnd('\', '/')) { return $false }
    }
    return $true
}
foreach ($p in @($InstallPath, $DataPath)) {
    if (-not (Test-SafeAclTarget $p)) { throw "Refusing to use shared or root folder: $p" }
}

# ── Network settings (address/port) ──────────────────────────────
$networkFile = Join-Path $DataPath "network.json"
if (-not (Test-Path $networkFile)) {
    # UTF-8 without BOM (Windows PowerShell 5.1 Set-Content -Encoding UTF8 would add one).
    $json = @{ mode = "local"; port = $Port; allow = @() } | ConvertTo-Json
    [IO.File]::WriteAllText($networkFile, $json, (New-Object System.Text.UTF8Encoding $false))
    Write-Host "Created $networkFile (local, port $Port)" -ForegroundColor Green
} else {
    Write-Host "Keeping existing $networkFile" -ForegroundColor Yellow
}

# ── Folder permissions (by SID: Administrators, SYSTEM, Users) ───
function Set-StrictAcl([string]$Path, [string[]]$Grants) {
    icacls $Path /reset /T /C /Q | Out-Null
    icacls $Path /inheritance:r /grant:r @Grants /C /Q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "icacls failed for $Path" }
}
Set-StrictAcl $InstallPath @('*S-1-5-32-544:(OI)(CI)F', '*S-1-5-18:(OI)(CI)F', '*S-1-5-32-545:(OI)(CI)RX')
Set-StrictAcl $DataPath    @('*S-1-5-32-544:(OI)(CI)F', '*S-1-5-18:(OI)(CI)F')
Write-Host "Folder permissions restricted: $InstallPath, $DataPath" -ForegroundColor Green

# ── Service ──────────────────────────────────────────────────────
$binPath = "`"$exe`""
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

# ── Legacy firewall rule (was open to any address) ───────────────
$legacyRule = "WinAdmin HTTP $Port"
if (Get-NetFirewallRule -DisplayName $legacyRule -ErrorAction SilentlyContinue) {
    Remove-NetFirewallRule -DisplayName $legacyRule
    Write-Host "Removed legacy firewall rule: $legacyRule" -ForegroundColor Green
}

Start-Service $ServiceName
$settings = Get-Content $networkFile -Raw | ConvertFrom-Json
Write-Host ""
Write-Host "Service '$ServiceName' started." -ForegroundColor Green
Write-Host "Open: http://127.0.0.1:$($settings.port)" -ForegroundColor Green
Write-Host ""
Write-Host "Network access (optional, admin console):" -ForegroundColor Yellow
Write-Host "  .\WinAdmin.exe network set --mode network --allow 10.0.0.0/24" -ForegroundColor Yellow
Write-Host ""
Write-Host "Create the first user (if none yet):" -ForegroundColor Yellow
Write-Host "  cd `"$InstallPath`"" -ForegroundColor Yellow
Write-Host '  .\WinAdmin.exe user add --login admin --password "YourPassword" --role Администратор' -ForegroundColor Yellow
