import type { ImportResult, SearchImportResult } from '../types/vacancy'

interface Counts {
  added: number
  updated: number
  unchanged: number
  excluded: number
}

/** Missing counts (e.g. from an older backend) are shown as 0 instead of „undefined". */
function formatCounts(counts: Partial<Counts>): string {
  return [
    `${counts.added ?? 0} neu`,
    `${counts.updated ?? 0} aktualisiert`,
    `${counts.unchanged ?? 0} unverändert`,
    `${counts.excluded ?? 0} ausgeschlossen`,
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
