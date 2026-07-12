<#
.SYNOPSIS
    Build WinAdmin.Ctl as a single tiny native executable (Rust).

.DESCRIPTION
    Builds src/ctl/winadmin-ctl (Rust + native-windows-gui) and copies the
    result to releases/ctl/WinAdmin.Ctl.exe (~2 MB, no runtime dependency).
    Requires the Rust toolchain (rustup, stable-x86_64-pc-windows-msvc) and
    the MSVC linker (Visual Studio Build Tools + Windows SDK).

.EXAMPLE
    .\releases\ctl\build-ctl.ps1
#>
[CmdletBinding()]
param()

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root      = (Resolve-Path (Join-Path $ScriptDir '..\..')).Path
$Project   = Join-Path $Root 'src\ctl\winadmin-ctl'
$Built     = Join-Path $Project 'target\release\winadmin-ctl.exe'
$Exe       = Join-Path $ScriptDir 'WinAdmin.Ctl.exe'
$Log       = Join-Path $Project 'target\build.log'

# Make sure cargo is reachable even in a fresh shell.
$env:Path = "$env:USERPROFILE\.cargo\bin;$env:Path"

if (-not (Get-Command cargo -ErrorAction SilentlyContinue)) {
    Write-Error 'cargo not found. Install Rust: winget install Rustlang.Rustup'
    exit 1
}

# A previous instance may hold a lock on the output exe.
Get-Process WinAdmin.Ctl -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

Write-Host 'Building WinAdmin.Ctl (Rust, release)...' -ForegroundColor Cyan

# Run cargo via cmd.exe so PowerShell never treats cargo's stderr progress
# as a terminating error; all output is captured to a log file.
Push-Location $Project
cmd /c "cargo build --release > `"$Log`" 2>&1"
$code = $LASTEXITCODE
Pop-Location

if (Test-Path $Log) {
    Get-Content $Log | Write-Host
}
if ($code -ne 0) {
    Write-Error 'cargo build failed.'
    exit 1
}
if (-not (Test-Path $Built)) {
    Write-Error "Expected output not found: $Built"
    exit 1
}

Copy-Item -Path $Built -Destination $Exe -Force

$sizeMb = [math]::Round((Get-Item -Path $Exe).Length / 1MB, 2)

Write-Host ''
Write-Host "Done: $Exe ($sizeMb MB)" -ForegroundColor Green
Write-Host 'Run as Administrator (UAC prompt on start).' -ForegroundColor Yellow
