import { describe, expect, it } from 'vitest'
import { emptyStateFor } from '../app/utils/emptyState'
import { DEFAULT_FILTERS, type VacancyFilters } from '../app/utils/vacancyFilters'

const defaults = (): VacancyFilters => ({ ...DEFAULT_FILTERS, employerTypes: [], searches: [] })

describe('emptyStateFor', () => {
  it('suggests an import for an empty database', () => {
    const state = emptyStateFor(defaults())

    expect(state.canReset).toBe(false)
    expect(state.hint).toContain('Import')
  })

  it('explains the Gemerkt filter', () => {
    expect(emptyStateFor({ ...defaults(), starredOnly: true })).toMatchObject({ title: 'Noch nichts gemerkt.', canReset: true })
  })

  it('explains the since-last-import filter', () => {
    expect(emptyStateFor({ ...defaults(), since: '2026-10-19T05:00:00Z' }).title)
      .toBe('Seit dem letzten Import keine neuen Stellen.')
  })

  it('offers a reset for other filters', () => {
    expect(emptyStateFor({ ...defaults(), q: 'cobol' })).toMatchObject({ title: 'Keine Stellen gefunden.', canReset: true })
  })
})
