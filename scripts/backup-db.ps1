<#
.SYNOPSIS
    Dumps the PostgreSQL database (docker compose service "db") to ./backups/jobautopilot-<timestamp>.sql.

.DESCRIPTION
    The dump contains DROP ... IF EXISTS statements, so restore-db.ps1 can replace the current data with it.
    The file is written inside the container and copied out, so no shell re-encoding can corrupt it.
    ./backups is git-ignored: dumps contain personal data.

.EXAMPLE
    ./scripts/backup-db.ps1
#>
[CmdletBinding()]
param(
    [string] $OutputDirectory = ''
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
. (Join-Path $PSScriptRoot '_compose-env.ps1')

if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root 'backups' }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

Assert-DbRunning
$db = Get-DbSettings $root
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$file = Join-Path $OutputDirectory "jobautopilot-$stamp.sql"
$tmp = "/tmp/jobautopilot-$stamp.sql"

try {
    Invoke-Compose @('exec', '-T', 'db', 'pg_dump', '--username', $db.User, '--dbname', $db.Database,
        '--clean', '--if-exists', '--no-owner', '--file', $tmp)
    Invoke-Compose @('cp', "db:$tmp", $file)
}
finally {
    & docker compose exec -T db rm -f $tmp *> $null
}

$size = [math]::Round((Get-Item $file).Length / 1KB, 1)
Write-Host "Backup written: $file ($size KB)" -ForegroundColor Green
