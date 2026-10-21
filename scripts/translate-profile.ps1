<#
.SYNOPSIS
  Fehlende deutsche Texte übersetzen (step 06c): drafts the missing German CV fields (*De, summary "<focus>.de")
  with the letter model, shows each one for review and writes data/profile.json only after approval.

.DESCRIPTION
  One Claude call (Llm:Tasks:Letter) for all missing texts. For every suggestion: j = take it, n = skip, e = type my
  own text, a = take this and all following, q = stop without saving. Before writing, the old file is copied to
  ./backups (git-ignored). The German text is inserted right after its English source, so comments and formatting
  of the file stay. The API reloads the profile by itself (within a few seconds).

.EXAMPLE
  ./scripts/translate-profile.ps1
  ./scripts/translate-profile.ps1 -DryRun     # show the drafts, change nothing
#>
param(
    [string]$Api = 'http://localhost:5080',
    [string]$ProfilePath = (Join-Path $PSScriptRoot '..\data\profile.json'),
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$missing = @(Invoke-RestMethod "$Api/api/profile/translations")
if ($missing.Count -eq 0) {
    Write-Host 'Alle Texte haben eine deutsche Fassung.' -ForegroundColor Green
    return
}

Write-Host "$($missing.Count) Texte ohne deutsche Fassung – Entwurf mit dem Modell für Anschreiben …"
$draft = Invoke-RestMethod -Method Post -TimeoutSec 300 "$Api/api/profile/translations"
Write-Host ('{0} Vorschläge · {1} Input- / {2} Output-Tokens' -f $draft.suggestions.Count, $draft.inputTokens, $draft.outputTokens)

$approved = New-Object System.Collections.Generic.List[object]
$takeAll = $false
foreach ($s in $draft.suggestions) {
    Write-Host ''
    Write-Host $s.path -ForegroundColor Cyan
    Write-Host "  EN: $($s.source)"
    Write-Host "  DE: $($s.german)" -ForegroundColor Yellow
    if ($DryRun) { continue }

    $answer = if ($takeAll) { 'j' } else { (Read-Host '  Übernehmen? [j]a / [n]ein / [e]igener Text / [a]lle / [q] abbrechen').Trim().ToLowerInvariant() }
    switch ($answer) {
        'j' { $approved.Add(@{ path = $s.path; german = $s.german }) }
        'a' { $takeAll = $true; $approved.Add(@{ path = $s.path; german = $s.german }) }
        'e' {
            $own = Read-Host '  Deutscher Text'
            if ($own.Trim()) { $approved.Add(@{ path = $s.path; german = $own.Trim() }) }
        }
        'q' { Write-Host 'Abgebrochen – nichts geändert.'; return }
        default { }
    }
}

if ($DryRun -or $approved.Count -eq 0) {
    Write-Host ''
    Write-Host 'Nichts geändert.'
    return
}

$path = (Resolve-Path $ProfilePath).Path
$bytes = [IO.File]::ReadAllBytes($path)
$hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
$json = [Text.Encoding]::UTF8.GetString($bytes).TrimStart([char]0xFEFF)

$body = @{ json = $json; translations = $approved } | ConvertTo-Json -Depth 5
$result = Invoke-RestMethod -Method Post "$Api/api/profile/translations/apply" -ContentType 'application/json; charset=utf-8' `
    -Body ([Text.Encoding]::UTF8.GetBytes($body))

$backups = Join-Path $PSScriptRoot '..\backups'
New-Item -ItemType Directory -Force $backups | Out-Null
$backup = Join-Path $backups ('profile-{0:yyyyMMdd-HHmmss}.json' -f (Get-Date))
Copy-Item $path $backup
[IO.File]::WriteAllText($path, $result.json, (New-Object Text.UTF8Encoding $hasBom))
Write-Host ''
Write-Host "$($result.inserted) Texte in $path eingetragen (Sicherung: $backup)." -ForegroundColor Green
