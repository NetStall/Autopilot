<#
.SYNOPSIS
  Creates application documents ("Unterlagen erstellen") for the N best vacancies of the list and prints, per
  vacancy, the time, tokens, estimated cost, fact-guard warnings and the letter text.

.DESCRIPTION
  One Claude call per vacancy (counts against Claude:DailyCallLimit). Vacancies that already have an application are
  skipped by the API (existing documents are returned, no new call). Output goes to this console only — nothing is
  written to files. Review, edit and approve the drafts in the UI afterwards (/unterlagen/{id}).

.EXAMPLE
  ./scripts/draft-top-vacancies.ps1 -Count 3
#>
param(
    [int]$Count = 3,
    [string]$Api = 'http://localhost:5080'
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

# ProblemDetails of a failed call as „title detail" (Windows PowerShell 5.1 does not always fill ErrorDetails).
function ProblemText($errorRecord) {
    $body = $errorRecord.ErrorDetails.Message
    if (-not $body -and $errorRecord.Exception.Response) {
        $reader = New-Object System.IO.StreamReader($errorRecord.Exception.Response.GetResponseStream())
        $body = $reader.ReadToEnd()
    }
    try {
        $problem = $body | ConvertFrom-Json
        return "$($problem.title) $($problem.detail)"
    }
    catch {
        return "$($errorRecord.Exception.Message) $body"
    }
}

$page = Invoke-RestMethod "$Api/api/vacancies?pageSize=$Count&sort=relevance"
foreach ($vacancy in $page.items) {
    Write-Host ''
    Write-Host "=== $($vacancy.title) · $($vacancy.company) (Bewertung $($vacancy.rankScore))" -ForegroundColor Cyan
    $watch = [Diagnostics.Stopwatch]::StartNew()
    try {
        $docs = Invoke-RestMethod -Method Post "$Api/api/vacancies/$($vacancy.id)/documents"
    }
    catch {
        Write-Host "Fehler: $(ProblemText $_)" -ForegroundColor Red
        continue
    }
    $watch.Stop()
    $u = $docs.usage
    Write-Host ("{0:N1} s · {1} Input- / {2} Output-Tokens · ≈ {3} $ · {4} · Seiten: Anschreiben {5}, Lebenslauf {6}" -f `
        $watch.Elapsed.TotalSeconds, $u.inputTokens, $u.outputTokens, $u.costUsd, $docs.model, $docs.letterPages, $docs.cvPages)
    foreach ($w in $docs.warnings) {
        $where = if ($null -ne $w.paragraph) { " (Absatz $($w.paragraph + 1))" } else { '' }
        Write-Host "  $($w.severity): $($w.message)$where" -ForegroundColor Yellow
    }
    Write-Host ''
    Write-Host $docs.letter.subject
    Write-Host ''
    Write-Host $docs.letter.salutation
    foreach ($paragraph in $docs.letter.paragraphs) {
        Write-Host ''
        Write-Host $paragraph
    }
    Write-Host ''
    Write-Host $docs.letter.closing
    Write-Host "→ Prüfen und freigeben: http://localhost:3000/unterlagen/$($docs.applicationId)" -ForegroundColor Green
}
