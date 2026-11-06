[CmdletBinding()]
param(
    [int]$Port = 5434,
    [string]$Database = 'dfw_api'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$pgRoot = Join-Path $root '.postgres'
$dataDir = Join-Path $pgRoot 'data'
$logFile = Join-Path $pgRoot 'postgres.log'

function Resolve-PostgresBin {
    if ($env:POSTGRES_BIN) {
        return $env:POSTGRES_BIN
    }

    $initdbCommand = Get-Command initdb.exe -ErrorAction SilentlyContinue

    if ($initdbCommand) {
        return Split-Path -Parent $initdbCommand.Source
    }

    $candidate = Get-ChildItem 'C:\Program Files\PostgreSQL' -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending |
        ForEach-Object { Join-Path $_.FullName 'bin' } |
        Where-Object { Test-Path (Join-Path $_ 'initdb.exe') } |
        Select-Object -First 1

    if (-not $candidate) {
        throw 'PostgreSQL binaries were not found. Install PostgreSQL or set POSTGRES_BIN to its bin folder.'
    }

    return $candidate
}

$pgBin = Resolve-PostgresBin
$initdb = Join-Path $pgBin 'initdb.exe'
$pgCtl = Join-Path $pgBin 'pg_ctl.exe'
$pgIsReady = Join-Path $pgBin 'pg_isready.exe'
$psql = Join-Path $pgBin 'psql.exe'
$createdb = Join-Path $pgBin 'createdb.exe'

New-Item -ItemType Directory -Force -Path $pgRoot | Out-Null

if (-not (Test-Path (Join-Path $dataDir 'PG_VERSION'))) {
    Write-Host "Initializing a local PostgreSQL cluster in $dataDir ..."

    & $initdb -D $dataDir -U postgres -A trust -E UTF8 --locale=C

    if ($LASTEXITCODE -ne 0) {
        throw 'initdb failed.'
    }

    Add-Content -Path (Join-Path $dataDir 'postgresql.conf') -Value @"

# DFW API local development settings
port = $Port
listen_addresses = 'localhost'
"@
}

& $pgCtl -D $dataDir status *> $null

if ($LASTEXITCODE -ne 0) {
    Write-Host "Starting PostgreSQL on port $Port ..."

    & $pgCtl -D $dataDir -l $logFile -o "-p $Port" start

    if ($LASTEXITCODE -ne 0) {
        throw "PostgreSQL failed to start. See $logFile."
    }
}

$ready = $false

for ($attempt = 0; $attempt -lt 30; $attempt++) {
    & $pgIsReady -h localhost -p $Port -U postgres *> $null

    if ($LASTEXITCODE -eq 0) {
        $ready = $true
        break
    }

    Start-Sleep -Milliseconds 500
}

if (-not $ready) {
    throw "PostgreSQL did not become ready. See $logFile."
}

$databaseExists = & $psql -h localhost -p $Port -U postgres -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname = '$Database'"

if ($LASTEXITCODE -ne 0) {
    throw 'Could not query the PostgreSQL server.'
}

if (($databaseExists | Out-String).Trim() -ne '1') {
    Write-Host "Creating database $Database ..."

    & $createdb -h localhost -p $Port -U postgres $Database

    if ($LASTEXITCODE -ne 0) {
        throw "Could not create database $Database."
    }
}

Write-Host ''
Write-Host "PostgreSQL is ready on localhost:$Port (database: $Database, user: postgres)."
Write-Host "Connection string: Host=localhost;Port=$Port;Database=$Database;Username=postgres;Password=postgres"
