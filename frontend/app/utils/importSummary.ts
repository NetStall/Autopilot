import type { ImportResult, SearchImportResult } from '../types/vacancy'

interface Counts {
  added: number
  updated: number
  unchanged: number
  excluded: number
}

function formatCounts(counts: Counts): string {
  return [
    `${counts.added} neu`,
    `${counts.updated} aktualisiert`,
    `${counts.unchanged} unverändert`,
    `${counts.excluded} ausgeschlossen`,
  ].join(' · ')
}

/** „85 neu · 12 aktualisiert · 140 unverändert · 23 ausgeschlossen" */
export function formatImportSummary(result: ImportResult): string {
  return formatCounts(result)
}

/** One line of the per-search breakdown, e.g. „remote-dotnet: 3 gefunden — 1 neu · 0 aktualisiert · …" */
export function formatSearchSummary(search: SearchImportResult): string {
  return `${search.name}: ${search.fetched} gefunden — ${formatCounts(search)}`
}
