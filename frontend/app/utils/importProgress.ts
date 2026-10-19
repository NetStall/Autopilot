import type { ImportRun, ImportRunStatus } from '../types/vacancy'
import { formatImportSummary } from './importSummary'

export function isActiveImport(status: ImportRunStatus): boolean {
  return status === 'Queued' || status === 'Running'
}

export interface ImportProgressView {
  active: boolean
  /** 0–100, for <progress>. */
  percent: number
  /** e.g. „Suche 3 von 15: region-csharp" */
  label: string
  /** Counts when succeeded, otherwise null. */
  summary: string | null
  /** User-facing reason when failed or cancelled, otherwise null. */
  error: string | null
}

/** What the import button shows for a run (polled from GET /api/imports/{id}). */
export function importProgressView(run: ImportRun): ImportProgressView {
  const total = Math.max(run.searchesTotal, 0)
  const done = Math.min(Math.max(run.searchesDone, 0), total)
  const percent = total === 0 ? 0 : Math.round((done / total) * 100)

  switch (run.status) {
    case 'Queued':
      return { active: true, percent: 0, label: 'Import wartet …', summary: null, error: null }
    case 'Running':
      return {
        active: true,
        percent,
        label: run.currentSearch
          ? `Suche ${Math.min(done + 1, total)} von ${total}: ${run.currentSearch}`
          : `Import läuft … (${done} von ${total})`,
        summary: null,
        error: null,
      }
    case 'Succeeded':
      return { active: false, percent: 100, label: 'Import abgeschlossen', summary: formatImportSummary(run), error: null }
    case 'Cancelled':
      return { active: false, percent, label: 'Import abgebrochen', summary: null, error: run.error ?? 'Der Import wurde abgebrochen.' }
    default:
      return { active: false, percent, label: 'Import fehlgeschlagen', summary: null, error: run.error ?? 'Unbekannter Fehler.' }
  }
}

/** The newest finished, successful run — for „Letzter Import" and „Neu seit letztem Import". */
export function lastSuccessfulImport(runs: readonly ImportRun[]): ImportRun | null {
  return runs
    .filter(r => r.status === 'Succeeded' && r.finishedAt)
    .sort((a, b) => Date.parse(b.finishedAt!) - Date.parse(a.finishedAt!))[0] ?? null
}
