<#
.SYNOPSIS
    Dev mode in one window: PostgreSQL (Docker), API (dotnet watch) and frontend (nuxt dev).

.DESCRIPTION
    Checks prerequisites, starts the "db" container and waits until it is healthy, then runs the API and the
    frontend side by side with prefixed, coloured output ([api], [web]). Ctrl+C stops both process trees;
    the database container keeps running (stop it with: docker compose stop db).
    Works with Windows PowerShell 5.1 and PowerShell 7.

.PARAMETER ApiPort
    Port of the API on localhost. Default: API_PORT from .env, otherwise 5080.

.PARAMETER WebPort
    Port of the frontend on localhost. Default: WEB_PORT from .env, otherwise 3000.

.PARAMETER SmokeTestSeconds
    For automated checks: stop everything after this many seconds, using the same shutdown as Ctrl+C.

.EXAMPLE
    ./dev.ps1
.EXAMPLE
    ./dev.ps1 -ApiPort 5081 -WebPort 3001
#>
[CmdletBinding()]
param(
    [int] $ApiPort = 0,
    [int] $WebPort = 0,
    [int] $SmokeTestSeconds = 0
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Set-Location $root

function Write-Status([string] $Message, [ConsoleColor] $Color = 'Gray') {
    Write-Host "[dev] $Message" -ForegroundColor $Color
}

function Stop-WithError([string] $Message) {
    Write-Host "[dev] $Message" -ForegroundColor Red
    exit 1
}

function Get-MajorVersion([string] $Text) {
    if ($Text -match '(\d+)\.\d+') { return [int] $Matches[1] }
    return 0
}

function Read-DotEnv([string] $Path) {
    $values = @{}
    foreach ($line in Get-Content -Path $Path -Encoding UTF8) {
        if ($line -match '^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*?)\s*$') {
            $values[$Matches[1]] = $Matches[2].Trim('"')
        }
    }
    return $values
}

function Test-PortInUse([int] $Port) {
    # Something listens if we can connect - on IPv4 or IPv6 loopback (nuxt dev listens on [::1] only).
    foreach ($address in @([System.Net.IPAddress]::Loopback, [System.Net.IPAddress]::IPv6Loopback)) {
        $client = New-Object System.Net.Sockets.TcpClient($address.AddressFamily)
        try {
            if ($client.ConnectAsync($address, $Port).Wait(300) -and $client.Connected) { return $true }
        }
        catch { }
        finally { $client.Dispose() }
    }
    return $false
}

# ---------------------------------------------------------------- prerequisites
Write-Status 'Checking prerequisites ...'

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    Stop-WithError 'Docker is not installed. Install Docker Desktop: https://www.docker.com/products/docker-desktop/'
}
& docker info --format '{{.ServerVersion}}' *> $null
if ($LASTEXITCODE -ne 0) {
    Stop-WithError 'Docker is installed but not running. Start Docker Desktop and wait for "Engine running".'
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Stop-WithError '.NET SDK not found. Install the .NET 10 SDK: https://dotnet.microsoft.com/download'
}
$dotnetVersion = (& dotnet --version) | Select-Object -First 1
if ((Get-MajorVersion $dotnetVersion) -lt 10) {
    Stop-WithError ".NET SDK $dotnetVersion found, but version 10 or newer is required."
}

if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
    Stop-WithError 'Node.js not found. Install Node 24: https://nodejs.org/'
}
$nodeVersion = (& node --version) | Select-Object -First 1
if ((Get-MajorVersion $nodeVersion.TrimStart('v')) -lt 24) {
    Stop-WithError "Node $nodeVersion found, but version 24 or newer is required."
}

$envFile = Join-Path $root '.env'
if (-not (Test-Path $envFile)) {
    Copy-Item (Join-Path $root '.env.example') $envFile
    Write-Status 'Created .env from .env.example (local development credentials).' Yellow
}
$dotEnv = Read-DotEnv $envFile

function Get-Setting([string] $Name, [string] $Default) {
    if ($dotEnv.ContainsKey($Name) -and $dotEnv[$Name]) { return $dotEnv[$Name] }
    return $Default
}

if ($ApiPort -eq 0) { $ApiPort = [int] (Get-Setting 'API_PORT' '5080') }
if ($WebPort -eq 0) { $WebPort = [int] (Get-Setting 'WEB_PORT' '3000') }
$dbPort = [int] (Get-Setting 'POSTGRES_PORT' '5432')
$dbName = Get-Setting 'POSTGRES_DB' 'jobautopilot'
$dbUser = Get-Setting 'POSTGRES_USER' 'jobautopilot'
$dbPassword = Get-Setting 'POSTGRES_PASSWORD' ''
if (-not $dbPassword) { Stop-WithError 'POSTGRES_PASSWORD is empty in .env.' }

foreach ($port in @($ApiPort, $WebPort)) {
    if (Test-PortInUse $port) {
        Stop-WithError "Port $port is already in use (is the app already running, e.g. via 'docker compose up'?). Stop it or pass -ApiPort/-WebPort."
    }
}

$nodeModules = Join-Path $root 'frontend/node_modules'
$installedLock = Join-Path $nodeModules '.package-lock.json'
$lockFile = Join-Path $root 'frontend/package-lock.json'
$needsInstall = -not (Test-Path $installedLock)
if (-not $needsInstall) {
    # package-lock.json changed (e.g. after git pull) since the last install.
    $needsInstall = (Get-Item $lockFile).LastWriteTimeUtc -gt (Get-Item $installedLock).LastWriteTimeUtc
}
if ($needsInstall) {
    Write-Status 'Installing frontend dependencies (npm install) ...' Yellow
    Push-Location (Join-Path $root 'frontend')
    try {
        & npm install --no-fund --no-audit
        if ($LASTEXITCODE -ne 0) { Stop-WithError 'npm install failed.' }
    }
    finally { Pop-Location }
}

# ---------------------------------------------------------------- database
Write-Status "Starting PostgreSQL (docker compose, port $dbPort) and waiting until it is healthy ..."
& docker compose up -d --wait db
if ($LASTEXITCODE -ne 0) {
    Stop-WithError "The database container did not become healthy. Check: docker compose logs db"
}

# ---------------------------------------------------------------- processes
$ansi = [regex] '\x1B\[[0-9;?]*[ -/]*[@-~]'

function Start-DevProcess([string] $Name, [string] $FileName, [string] $Arguments, [string] $WorkingDirectory, [hashtable] $Environment) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $FileName
    $psi.Arguments = $Arguments
    $psi.WorkingDirectory = $WorkingDirectory
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.RedirectStandardInput = $true
    $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $psi.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    foreach ($key in $Environment.Keys) { $psi.EnvironmentVariables[$key] = [string] $Environment[$key] }

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $psi
    $process.EnableRaisingEvents = $true
    Register-ObjectEvent -InputObject $process -EventName OutputDataReceived -SourceIdentifier "$Name.out" | Out-Null
    Register-ObjectEvent -InputObject $process -EventName ErrorDataReceived -SourceIdentifier "$Name.err" | Out-Null
    [void] $process.Start()
    $process.BeginOutputReadLine()
    $process.BeginErrorReadLine()
    return $process
}

function Write-ProcessLine([string] $SourceIdentifier, [string] $Line) {
    if ($null -eq $Line) { return }
    $name, $stream = $SourceIdentifier.Split('.')
    $text = $ansi.Replace($Line, '')
    $prefixColor = if ($name -eq 'api') { 'Cyan' } else { 'Magenta' }
    $textColor = 'Gray'
    if ($stream -eq 'err' -or $text -match '\b(error|fail|exception)\b') { $textColor = 'Red' }
    elseif ($text -match '\bwarn') { $textColor = 'Yellow' }
    Write-Host ("[{0}] " -f $name) -ForegroundColor $prefixColor -NoNewline
    Write-Host $text -ForegroundColor $textColor
}

function Stop-ProcessTree([System.Diagnostics.Process] $Process) {
    if ($null -eq $Process) { return }
    try {
        if (-not $Process.HasExited) {
            # /T: the whole tree (dotnet watch -> dotnet -> app, cmd -> npm -> node), /F: no prompts.
            & taskkill.exe /PID $Process.Id /T /F *> $null
            [void] $Process.WaitForExit(5000)
        }
    }
    catch { }
}

$apiUrl = "http://localhost:$ApiPort"
$webUrl = "http://localhost:$WebPort"
$apiEnvironment = @{
    ASPNETCORE_ENVIRONMENT                 = 'Development'
    ASPNETCORE_URLS                        = $apiUrl
    ConnectionStrings__Default             = "Host=localhost;Port=$dbPort;Database=$dbName;Username=$dbUser;Password=$dbPassword"
    Cors__AllowedOrigins__0                = $webUrl
    DOTNET_WATCH_RESTART_ON_RUDE_EDIT      = 'true'
    DOTNET_WATCH_SUPPRESS_EMOJIS           = '1'
    DOTNET_CLI_TELEMETRY_OPTOUT            = '1'
}
$webEnvironment = @{
    NUXT_PUBLIC_API_BASE = $apiUrl
    NUXT_TELEMETRY_DISABLED = '1'
}

$processes = @()
$ctrlCWasInput = $false
$inputIsConsole = $true
try {
    $inputIsConsole = -not [Console]::IsInputRedirected
    if ($inputIsConsole) {
        # Ctrl+C becomes a key press for this script (and is not sent to the children), so we stop the
        # process trees ourselves and nothing is left behind.
        $ctrlCWasInput = [Console]::TreatControlCAsInput
        [Console]::TreatControlCAsInput = $true
    }
    try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

    Write-Status "Starting API on $apiUrl (dotnet watch) ..."
    $processes += Start-DevProcess 'api' 'dotnet' `
        "watch --non-interactive --project backend/src/JobAutopilot.Api run --no-launch-profile" `
        $root $apiEnvironment
    Write-Status "Starting frontend on $webUrl (nuxt dev) ..."
    $processes += Start-DevProcess 'web' 'cmd.exe' "/d /c npm run dev -- --port $WebPort" `
        (Join-Path $root 'frontend') $webEnvironment

    Write-Status 'Press Ctrl+C to stop API and frontend.' Green
    $started = Get-Date
    $ready = $false
    $nextHealthCheck = Get-Date

    while ($true) {
        foreach ($evt in @(Get-Event -ErrorAction SilentlyContinue)) {
            if ($evt.SourceIdentifier -match '^(api|web)\.(out|err)$') {
                Write-ProcessLine $evt.SourceIdentifier $evt.SourceEventArgs.Data
            }
            Remove-Event -EventIdentifier $evt.EventIdentifier
        }

        if ($inputIsConsole -and [Console]::KeyAvailable) {
            $key = [Console]::ReadKey($true)
            if ($key.Key -eq [ConsoleKey]::C -and ($key.Modifiers -band [ConsoleModifiers]::Control)) {
                Write-Status 'Ctrl+C - stopping ...' Yellow
                break
            }
        }

        $exited = @($processes | Where-Object { $_.HasExited })
        if ($exited.Count -gt 0) {
            Write-Status 'A process has exited - stopping the other one.' Yellow
            break
        }

        if (-not $ready -and (Get-Date) -ge $nextHealthCheck) {
            $nextHealthCheck = (Get-Date).AddSeconds(2)
            try {
                $response = Invoke-WebRequest -Uri "$apiUrl/health/ready" -UseBasicParsing -TimeoutSec 1
                if ($response.StatusCode -eq 200 -and (Test-PortInUse $WebPort)) {
                    $ready = $true
                    Write-Status "Ready: $webUrl  (API $apiUrl)" Green
                }
            }
            catch { }
        }

        if ($SmokeTestSeconds -gt 0 -and ((Get-Date) - $started).TotalSeconds -ge $SmokeTestSeconds) {
            Write-Status "Smoke test finished after $SmokeTestSeconds s (ready: $ready) - stopping ..." Yellow
            if (-not $ready) { $script:smokeFailed = $true }
            break
        }

        Start-Sleep -Milliseconds 100
    }
}
finally {
    foreach ($process in $processes) { Stop-ProcessTree $process }
    foreach ($id in @('api.out', 'api.err', 'web.out', 'web.err')) {
        Unregister-Event -SourceIdentifier $id -ErrorAction SilentlyContinue
    }
    Get-Event -ErrorAction SilentlyContinue | Remove-Event -ErrorAction SilentlyContinue
    if ($inputIsConsole) {
        try { [Console]::TreatControlCAsInput = $ctrlCWasInput } catch { }
    }
    Write-Status 'API and frontend stopped. The database keeps running (docker compose stop db).' Green
}

if ((Get-Variable -Name smokeFailed -Scope Script -ErrorAction SilentlyContinue) -and $script:smokeFailed) { exit 1 }
