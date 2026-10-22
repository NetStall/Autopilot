<#
.SYNOPSIS
  Tageslauf (step 08b): import, hard filters, scoring, local KI analysis for new or changed vacancies in the Top N,
  application channel, summary. Safe to run several times a day. Nothing is ever sent.

.DESCRIPTION
  Starts the run in the API's background queue (or joins the one already running), prints each stage when it is
  done and the summary at the end: new vacancies, filtered ones by reason, newly in the Top 20, e-mail-ready in the
  Top 20, failures. Without Ollama the KI stage is skipped with a message. Ctrl+C stops waiting, not the run; to
  cancel it: Invoke-RestMethod -Method Delete http://localhost:5080/api/daily-run/current

.EXAMPLE
  ./scripts/daily-run.ps1
  ./scripts/daily-run.ps1 -Top 200
#>
param(
    [string]$Api = 'http://localhost:5080',
    [int]$Top = 100
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$null = Invoke-RestMethod -Method Post "$Api/api/daily-run?top=$Top"
Write-Host "Tageslauf gestartet (Top $Top) …"
$shown = 0
while ($true) {
    $status = Invoke-RestMethod "$Api/api/daily-run"
    foreach ($stage in @($status.stages) | Select-Object -Skip $shown) {
        $color = switch ($stage.outcome) { 'Done' { 'Green' } 'Skipped' { 'Yellow' } default { 'Red' } }
        Write-Host ('{0,-10} {1,-9} {2,6:0.0} s  {3}' -f $stage.stage, $stage.outcome, ($stage.durationMs / 1000), $stage.message) -ForegroundColor $color
        $shown++
    }
    if ($status.state -eq 'Idle' -and $shown -ge @($status.stages).Count) { break }
    Start-Sleep -Seconds 2
}

$s = $status.last
if (-not $s) { return }
Write-Host ''
Write-Host "Neue Stellen: $($s.newVacancies)"
$filtered = $s.newFiltered.PSObject.Properties | ForEach-Object { "$($_.Name) $($_.Value)" }
if ($filtered) { Write-Host "Neu gefiltert: $($filtered -join ', ')" }
Write-Host "Neu in den Top 20: $(@($s.newInTop20).Count)"
foreach ($v in $s.newInTop20) { Write-Host ("  {0,3}  {1} · {2}" -f $v.rankScore, $v.title, $v.company) }
Write-Host "Per E-Mail vorbereitbar in den Top 20: $(@($s.emailReadyInTop20).Count)"
foreach ($v in $s.emailReadyInTop20) { Write-Host "  $($v.title) · $($v.company) · $($v.email)" }
foreach ($f in $s.failures) { Write-Host "Fehler: $f" -ForegroundColor Red }
