import { describe, expect, it } from 'vitest'
import { emptyStateFor } from '../app/utils/emptyState'
import { defaultFilters as defaults } from '../app/utils/vacancyFilters'

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

  it('suggests „Vielleicht" when only relevant vacancies are shown', () => {
    expect(emptyStateFor({ ...defaults(), relevance: ['Relevant'] }).hint).toContain('Vielleicht')
  })

  it('offers a reset for other filters', () => {
    expect(emptyStateFor({ ...defaults(), q: 'cobol' })).toMatchObject({ title: 'Keine Stellen gefunden.', canReset: true })
  })
})
