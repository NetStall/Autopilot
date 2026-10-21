<#
.SYNOPSIS
  Sets the gold labels („Passt" / „Passt nicht") I already gave in chat (step 07a), through the API.

.DESCRIPTION
  Each entry is found by a search term plus a part of the title; it is labelled only when exactly one vacancy
  matches, otherwise it is reported and skipped. Safe to run again: a label is simply set again. Needs the API from
  step 07a or later (http://localhost:5080 by default).

.EXAMPLE
  ./scripts/seed-gold-labels.ps1
#>
param(
    [string]$Api = 'http://localhost:5080'
)

$ErrorActionPreference = 'Stop'

$labels = @(
    @{ Query = 'ResMed';            Title = 'Senior C# / .NET Developer';            Fits = $false; Note = 'MEDIFOX Senior AWS' },
    @{ Query = 'Vidispine';         Title = 'Broadcast & Video Formats';             Fits = $false; Note = 'Vidispine Broadcast' },
    @{ Query = 'Bauwesen Schmidt';  Title = 'C#/.NET & Blazor';                      Fits = $false; Note = 'Schmidt Blazor/DevExpress' },
    @{ Query = 'ifm prover';        Title = 'Tools und Anwendungen / C#';            Fits = $false; Note = 'ifm prover' },
    @{ Query = 'TGW';               Title = 'Full Stack Developer C# / Vue.js';      Fits = $true;  Note = 'TGW' },
    @{ Query = 'Meierhofer';        Title = 'Software Engineer Fullstack (.NET';     Fits = $true;  Note = 'Meierhofer' },
    @{ Query = 'AraCom';            Title = '.NET & Angular oder Vue.js';            Fits = $true;  Note = 'AraCom .NET' },
    @{ Query = 'DPS';               Title = 'Softwareentwickler';                    Fits = $true;  Note = 'DPS' }
)

foreach ($entry in $labels) {
    $query = [uri]::EscapeDataString($entry.Query)
    $page = Invoke-RestMethod "$Api/api/vacancies?q=$query&pageSize=50&includeDismissed=true&includeUnreachable=true&minScore=0"
    $found = @($page.items | Where-Object { $_.title.Contains($entry.Title) })
    if ($found.Count -ne 1) {
        Write-Warning "$($entry.Note): $($found.Count) Treffer – übersprungen."
        continue
    }

    $body = @{ fits = $entry.Fits } | ConvertTo-Json
    Invoke-RestMethod -Method Put "$Api/api/vacancies/$($found[0].id)/label" -ContentType 'application/json' -Body $body | Out-Null
    $label = if ($entry.Fits) { 'Passt' } else { 'Passt nicht' }
    Write-Host "$($entry.Note): $label"
}

$summary = Invoke-RestMethod "$Api/api/labels/summary"
Write-Host "Bewertet: $($summary.labeled) / $($summary.target)"
