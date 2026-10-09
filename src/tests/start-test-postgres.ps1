<#
.SYNOPSIS
    Запускает временный кластер PostgreSQL для тестов (не трогает установленную службу).
.EXAMPLE
    .\src\tests\start-test-postgres.ps1            # старт, выводит строку для WINADMIN_TEST_POSTGRES
    .\src\tests\start-test-postgres.ps1 -Stop      # остановка и удаление кластера
#>
param(
    [string]$BinDir = "C:\Program Files\PostgreSQL\18\bin",
    [string]$DataDir = (Join-Path $env:TEMP "winadmin-test-pg"),
    [int]$Port = 55432,
    [switch]$Stop
)
$ErrorActionPreference = "Stop"
$pgCtl = Join-Path $BinDir "pg_ctl.exe"
if ($Stop) {
    if (Test-Path $DataDir) { & $pgCtl -D $DataDir stop -m fast | Out-Null; Remove-Item -Recurse -Force $DataDir }
    return
}
if (-not (Test-Path $DataDir)) {
    & (Join-Path $BinDir "initdb.exe") -D $DataDir -U winadmin --auth=trust -E UTF8 | Out-Null
}
# Отдельный скрытый процесс без наследования дескрипторов: иначе вызывающая консоль
# ждёт завершения postgres (он работает, пока кластер не остановят).
$log = Join-Path $DataDir "log.txt"
if (-not (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)) {
    Start-Process -FilePath $pgCtl -WindowStyle Hidden -ArgumentList @(
        '-D', "`"$DataDir`"", '-o', "`"-p $Port -c listen_addresses=127.0.0.1`"", '-l', "`"$log`"", 'start')
    $deadline = (Get-Date).AddSeconds(30)
    while (-not (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)) {
        if ((Get-Date) -gt $deadline) { throw "PostgreSQL не запустился за 30 с, см. $log" }
        Start-Sleep -Milliseconds 300
    }
}
"Host=127.0.0.1;Port=$Port;Username=winadmin;Database=postgres"
