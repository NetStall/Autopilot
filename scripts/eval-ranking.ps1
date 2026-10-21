<#
.SYNOPSIS
  „eval-ranking" (step 07c): precision@10, precision@20 and NDCG@20 of the ranking variants over the gold labels.

.DESCRIPTION
  Prints one table with my explicit labels only and one with the automatic labels added (applied → Passt,
  Ausblenden → Passt nicht). Variants: Nur Regeln, Kombiniert (current), Nur KI-Bewertung, Rang + w × KI.
  With -Model the labelled vacancies are first analysed by that model too (extraction + KI-Bewertung, real local
  model calls, cached — a second run is instant); its variants are added. Nothing on the vacancies changes.

.EXAMPLE
  ./scripts/eval-ranking.ps1
  ./scripts/eval-ranking.ps1 -Model qwen3.5:9b
  ./scripts/eval-ranking.ps1 -Json > backups/eval.json     # raw result (ids and scores only)
#>
param(
    [string]$Api = 'http://localhost:5080',
    [string]$Model,
    [switch]$Json
)

$ErrorActionPreference = 'Stop'
$modelQuery = if ($Model) { "&model=$([uri]::EscapeDataString($Model))" } else { '' }

if ($Model) {
    Write-Host "Analysiere die bewerteten Stellen mit $Model (lokal, kann einige Minuten dauern) …"
    $run = Invoke-RestMethod -Method Post -TimeoutSec 3600 "$Api/api/evaluation/model-run?model=$([uri]::EscapeDataString($Model))&implicit=true"
    Write-Host ("{0} Stellen: {1} extrahiert, {2} bewertet, {3:0} s" -f $run.vacancies, $run.extracted, $run.scored, ($run.durationMs / 1000))
}

$reports = foreach ($implicit in 'false', 'true') {
    Invoke-RestMethod "$Api/api/evaluation/ranking?implicit=$implicit$modelQuery"
}

if ($Json) {
    $reports | ConvertTo-Json -Depth 6
    return
}

foreach ($report in $reports) {
    $title = if ($report.withAutomatic) { 'Mit automatischen Labels' } else { 'Nur meine Labels' }
    $first = $report.results[0]
    Write-Host ''
    Write-Host ('{0}: {1} Stellen, davon {2} Passt (explizit {3}, automatisch {4}, Ziel {5})' -f $title, $first.count, $first.fits, $report.explicit, $report.automatic, $report.target)
    $report.results | Format-Table -AutoSize `
        @{ n = 'Variante'; e = { $_.variant } },
        @{ n = 'P@10'; e = { '{0:0.00}' -f $_.precisionAt10 } },
        @{ n = 'P@20'; e = { '{0:0.00}' -f $_.precisionAt20 } },
        @{ n = 'NDCG@20'; e = { '{0:0.00}' -f $_.ndcgAt20 } } | Out-Host
}

if (($reports[0].explicit) -lt $reports[0].target) {
    Write-Warning ("Erst {0} von {1} eigenen Labels: die Zahlen sind ein Trend, keine belastbare Auswertung." -f $reports[0].explicit, $reports[0].target)
}
