<#
.SYNOPSIS
    Restores a dump created by backup-db.ps1 into the database of the docker compose service "db".

.DESCRIPTION
    REPLACES the current data. Runs in a single transaction: if anything fails, nothing changes.
    Stop the API first (Ctrl+C in dev.ps1, or: docker compose stop api) so no connection holds locks.

.PARAMETER File
    Path to the .sql dump.

.PARAMETER Force
    Do not ask for confirmation.

.EXAMPLE
    ./scripts/restore-db.ps1 ./backups/jobautopilot-20261018-210000.sql
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)] [string] $File,
    [switch] $Force
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot '_compose-env.ps1')

$path = (Resolve-Path $File).Path
Set-Location $root
Assert-DbRunning
$db = Get-DbSettings $root

if (-not $Force) {
    $answer = Read-Host "This replaces all data in database '$($db.Database)' with '$path'. Type 'yes' to continue"
    if ($answer -ne 'yes') { Write-Host 'Cancelled.'; exit 1 }
}

$tmp = '/tmp/jobautopilot-restore.sql'
try {
    Invoke-Compose @('cp', $path, "db:$tmp")
    Invoke-Compose @('exec', '-T', 'db', 'psql', '--username', $db.User, '--dbname', $db.Database,
        '--set', 'ON_ERROR_STOP=1', '--single-transaction', '--quiet', '--output', '/dev/null', '--file', $tmp)
}
finally {
    & docker compose exec -T db rm -f $tmp *> $null
}

Write-Host "Restored '$path' into database '$($db.Database)'." -ForegroundColor Green
