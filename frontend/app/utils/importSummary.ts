import type { ImportResult } from '../types/vacancy'

/** „12 neu · 3 aktualisiert · 40 unverändert" */
export function formatImportSummary(result: ImportResult): string {
  return [
    `${result.added} neu`,
    `${result.updated} aktualisiert`,
    `${result.unchanged} unverändert`,
  ].join(' · ')
}
