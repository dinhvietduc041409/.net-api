[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$dataDir = Join-Path $root '.postgres\data'

if (-not (Test-Path (Join-Path $dataDir 'PG_VERSION'))) {
    Write-Host 'No local PostgreSQL cluster has been initialized.'
    exit 0
}

if ($env:POSTGRES_BIN) {
    $pgCtl = Join-Path $env:POSTGRES_BIN 'pg_ctl.exe'
}
else {
    $pgCtlCommand = Get-Command pg_ctl.exe -ErrorAction SilentlyContinue

    if ($pgCtlCommand) {
        $pgCtl = $pgCtlCommand.Source
    }
    else {
        $pgCtl = Get-ChildItem 'C:\Program Files\PostgreSQL' -Directory -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName 'bin\pg_ctl.exe' } |
            Where-Object { Test-Path $_ } |
            Select-Object -First 1
    }
}

if (-not $pgCtl) {
    throw 'pg_ctl.exe was not found. Set POSTGRES_BIN or install PostgreSQL.'
}

& $pgCtl -D $dataDir stop -m fast
