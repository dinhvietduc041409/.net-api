[CmdletBinding()]
param(
    [int]$Port = 5434,
    [string]$Database = 'dfw_api'
)

$ErrorActionPreference = 'Stop'

& (Join-Path $PSScriptRoot 'db-init.ps1') -Port $Port -Database $Database
