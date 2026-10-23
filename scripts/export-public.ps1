<#
.SYNOPSIS
  Exports the tracked working tree as a fresh public repository with one commit (no history, nothing pushed).

.DESCRIPTION
  Copies every file git tracks (working-tree content; no .git, nothing git-ignored such as data/, .env or
  appsettings.Local.json) to the target folder, runs git init there and makes one commit „Initial public release".
  The private repository and its history stay untouched. The README badge is pointed at the public repository.
  Nothing is pushed and no GitHub repository is created — the script only prints the next commands.

.EXAMPLE
  ./scripts/export-public.ps1
  ./scripts/export-public.ps1 -Target ..\job-autopilot-public -PublicRepo NetStall/job-autopilot -Force
#>
param(
    [string]$Target = (Join-Path $PSScriptRoot '..\..\job-autopilot-public'),
    [string]$PublicRepo = 'NetStall/job-autopilot',
    [string]$AuthorName = 'Vitalii Strashuk',
    [string]$AuthorEmail,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

Push-Location $root
try {
    if (-not $AuthorEmail) { $AuthorEmail = git config user.email }
    if (-not $AuthorEmail) { throw 'Keine E-Mail für den Commit: -AuthorEmail angeben oder git config user.email setzen.' }

    $dirty = git status --porcelain --untracked-files=no
    if ($dirty) { Write-Warning 'Uncommitted changes – the export takes the working tree as it is, not the last commit.' }

    # -z: paths with umlauts stay unquoted.
    $files = (git -c core.quotepath=off ls-files -z) -split "`0" | Where-Object { $_ }
}
finally {
    Pop-Location
}

if (Test-Path $Target) {
    if (-not $Force) { throw "$Target exists. Remove it or run again with -Force (the folder is replaced)." }
    Remove-Item -Recurse -Force $Target
}
New-Item -ItemType Directory -Path $Target | Out-Null
$Target = (Resolve-Path $Target).Path

foreach ($file in $files) {
    $source = Join-Path $root $file
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { continue }   # deleted in the working tree
    $destination = Join-Path $Target $file
    New-Item -ItemType Directory -Force -Path (Split-Path $destination) | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination
}

$readme = Join-Path $Target 'README.md'
$text = [IO.File]::ReadAllText($readme)
[IO.File]::WriteAllText($readme, $text.Replace('NetStall/Autopilot', $PublicRepo), (New-Object Text.UTF8Encoding $false))

Push-Location $Target
try {
    git init -q -b main
    git -c core.safecrlf=false add -A
    git -c user.name="$AuthorName" -c user.email="$AuthorEmail" commit -q -m 'Initial public release'
    $count = (git ls-files | Measure-Object).Count
    Write-Host "Exported $count files to $Target (one commit, nothing pushed)."
}
finally {
    Pop-Location
}

Write-Host ''
Write-Host 'Next steps (check the copy first: build, tests, a last look):'
Write-Host "  cd `"$Target`""
Write-Host '  dotnet test backend/JobAutopilot.sln'
Write-Host '  cd frontend; npm ci; npm test; npm run build; cd ..'
Write-Host "  # create an empty public repository $PublicRepo on GitHub (no README/licence), then:"
Write-Host "  git remote add origin https://github.com/$PublicRepo.git"
Write-Host '  git push -u origin main'
