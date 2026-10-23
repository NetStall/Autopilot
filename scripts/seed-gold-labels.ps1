<#
.SYNOPSIS
  Sets the gold labels („Passt" / „Passt nicht") I already gave in chat (step 07a), through the API.

.DESCRIPTION
  The labels are real vacancies, so they live in the git-ignored data/labels.local.json:
    [ { "query": "<Suchbegriff>", "title": "<Teil des Titels>", "fits": true, "note": "<Notiz>" }, … ]
  Each entry is found by its search term plus a part of the title; it is labelled only when exactly one vacancy
  matches, otherwise it is reported and skipped. Safe to run again: a label is simply set again. Needs the API from
  step 07a or later (http://localhost:5080 by default).

.EXAMPLE
  ./scripts/seed-gold-labels.ps1
  ./scripts/seed-gold-labels.ps1 -File data/other-labels.json
#>
param(
    [string]$Api = 'http://localhost:5080',
    [string]$File = (Join-Path $PSScriptRoot '..\data\labels.local.json')
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $File)) {
    throw "Keine Labels gefunden: $File. Lege die Datei an (Format siehe Hilfe: Get-Help ./scripts/seed-gold-labels.ps1)."
}
$labels = Get-Content $File -Raw -Encoding UTF8 | ConvertFrom-Json

foreach ($entry in $labels) {
    $query = [uri]::EscapeDataString($entry.query)
    $page = Invoke-RestMethod "$Api/api/vacancies?q=$query&pageSize=50&includeDismissed=true&includeUnreachable=true&minScore=0"
    $found = @($page.items | Where-Object { $_.title.Contains($entry.title) })
    if ($found.Count -ne 1) {
        Write-Warning "$($entry.note): $($found.Count) Treffer – übersprungen."
        continue
    }

    $body = @{ fits = [bool]$entry.fits } | ConvertTo-Json
    Invoke-RestMethod -Method Put "$Api/api/vacancies/$($found[0].id)/label" -ContentType 'application/json' -Body $body | Out-Null
    $label = if ($entry.fits) { 'Passt' } else { 'Passt nicht' }
    Write-Host "$($entry.note): $label"
}

$summary = Invoke-RestMethod "$Api/api/labels/summary"
Write-Host "Bewertet: $($summary.labeled) / $($summary.target)"
