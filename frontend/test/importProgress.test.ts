import { describe, expect, it } from 'vitest'
import type { ImportRun } from '../app/types/vacancy'
import { importProgressView, isActiveImport, lastSuccessfulImport } from '../app/utils/importProgress'

const base: ImportRun = {
  id: 'run-1',
  trigger: 'Manual',
  status: 'Queued',
  requestedAt: '2026-10-19T05:00:00Z',
  startedAt: null,
  finishedAt: null,
  durationMs: null,
  searchesTotal: 15,
  searchesDone: 0,
  currentSearch: null,
  fetched: 0,
  added: 0,
  updated: 0,
  unchanged: 0,
  excluded: 0,
  error: null,
  searches: [],
}

describe('importProgressView', () => {
  it('shows a waiting state while queued', () => {
    expect(importProgressView(base)).toMatchObject({ active: true, percent: 0, label: 'Import wartet …' })
  })

  it('shows the current search and the percentage while running', () => {
    const view = importProgressView({ ...base, status: 'Running', searchesDone: 3, currentSearch: 'region-csharp' })

    expect(view).toEqual({ active: true, percent: 20, label: 'Suche 4 von 15: region-csharp', summary: null, error: null })
  })

  it('shows the summary when done', () => {
    const view = importProgressView({
      ...base, status: 'Succeeded', searchesDone: 15, added: 85, updated: 12, unchanged: 140, excluded: 23,
    })

    expect(view).toMatchObject({ active: false, percent: 100 })
    expect(view.summary).toBe('85 neu · 12 aktualisiert · 140 unverändert · 23 ausgeschlossen')
  })

  it.each([
    ['Failed', 'The Arbeitsagentur Jobsuche API is not reachable.'],
    ['Cancelled', 'Import was cancelled.'],
  ] as const)('shows the error of a %s run', (status, error) => {
    const view = importProgressView({ ...base, status, searchesDone: 5, error })

    expect(view).toMatchObject({ active: false, summary: null, error, percent: 33 })
  })

  it('never divides by zero or exceeds 100 %', () => {
    expect(importProgressView({ ...base, status: 'Running', searchesTotal: 0 }).percent).toBe(0)
    expect(importProgressView({ ...base, status: 'Running', searchesDone: 99 }).percent).toBe(100)
  })
})

describe('isActiveImport / lastSuccessfulImport', () => {
  it('treats queued and running as active', () => {
    const statuses: ImportRun['status'][] = ['Queued', 'Running', 'Succeeded', 'Failed', 'Cancelled']

    expect(statuses.map(isActiveImport)).toEqual([true, true, false, false, false])
  })

  it('picks the newest successful run', () => {
    const runs: ImportRun[] = [
      { ...base, id: 'failed-newest', status: 'Failed', finishedAt: '2026-10-19T08:00:00Z' },
      { ...base, id: 'ok-old', status: 'Succeeded', finishedAt: '2026-10-18T05:00:00Z' },
      { ...base, id: 'ok-new', status: 'Succeeded', finishedAt: '2026-10-19T05:01:00Z' },
      { ...base, id: 'running', status: 'Running' },
    ]

    expect(lastSuccessfulImport(runs)?.id).toBe('ok-new')
    expect(lastSuccessfulImport([])).toBeNull()
  })
})
