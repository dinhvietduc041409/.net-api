[CmdletBinding()]
param(
    [int]$Port = 5434,
    [string]$Database = 'dfw_api'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$pgRoot = Join-Path $root '.postgres'

& (Join-Path $PSScriptRoot 'db-stop.ps1')

if (Test-Path $pgRoot) {
    Remove-Item -Recurse -Force $pgRoot
}

& (Join-Path $PSScriptRoot 'db-init.ps1') -Port $Port -Database $Database
