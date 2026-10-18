# Shared helpers for the scripts in this folder (dot-sourced). Windows PowerShell 5.1 and PowerShell 7.

function Read-DotEnv([string] $Path) {
    $values = @{}
    if (Test-Path $Path) {
        foreach ($line in Get-Content -Path $Path -Encoding UTF8) {
            if ($line -match '^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*?)\s*$') {
                $values[$Matches[1]] = $Matches[2].Trim('"')
            }
        }
    }
    return $values
}

function Get-DbSettings([string] $RepositoryRoot) {
    $dotEnv = Read-DotEnv (Join-Path $RepositoryRoot '.env')
    $user = 'jobautopilot'
    $database = 'jobautopilot'
    if ($dotEnv.ContainsKey('POSTGRES_USER') -and $dotEnv['POSTGRES_USER']) { $user = $dotEnv['POSTGRES_USER'] }
    if ($dotEnv.ContainsKey('POSTGRES_DB') -and $dotEnv['POSTGRES_DB']) { $database = $dotEnv['POSTGRES_DB'] }
    return @{ User = $user; Database = $database }
}

function Assert-DbRunning {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw 'Docker is not installed.' }
    $id = & docker compose ps --status running --quiet db
    if ($LASTEXITCODE -ne 0 -or -not $id) {
        throw 'The database container is not running. Start it with: docker compose up -d db'
    }
}

function Invoke-Compose([string[]] $Arguments) {
    & docker compose @Arguments
    if ($LASTEXITCODE -ne 0) { throw "docker compose $($Arguments -join ' ') failed (exit code $LASTEXITCODE)." }
}
