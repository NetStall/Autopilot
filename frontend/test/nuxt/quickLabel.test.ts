import { describe, expect, it, vi } from 'vitest'
import { mockNuxtImport, mountSuspended } from '@nuxt/test-utils/runtime'
import { flushPromises } from '@vue/test-utils'
import IndexPage from '~/pages/index.vue'
import type { Vacancy } from '~/types/vacancy'

const api = vi.hoisted(() => ({
  list: vi.fn(),
  searches: vi.fn().mockResolvedValue([]),
  uiSettings: vi.fn().mockResolvedValue({ highlightKeywords: [], scoreHigh: 70, scoreMedium: 45 }),
  searchProfile: vi.fn().mockResolvedValue({ version: 1, updatedAt: null, searches: [], profile: { minScore: 0, topN: 20, germanFilter: [] } }),
  recentImports: vi.fn().mockResolvedValue([]),
  setLabel: vi.fn().mockResolvedValue(undefined),
  labelSummary: vi.fn().mockResolvedValue({ labeled: 1, fits: 1, notFits: 0, target: 30, automatic: 1, automaticFits: 0, automaticNotFits: 1 }),
  setInterest: vi.fn(),
  setSeen: vi.fn(),
  markSeen: vi.fn(),
}))
mockNuxtImport('useVacanciesApi', () => () => api)

function vacancy(id: string, overrides: Partial<Vacancy> = {}): Vacancy {
  return {
    id, source: 'Arbeitsagentur', externalId: id, title: `Entwickler ${id}`, company: 'Beispiel GmbH', location: 'Regensburg',
    url: null, description: null, employerType: 'Direct', publishedAt: null, fetchedAt: '2026-10-21T08:00:00Z', isSeen: false,
    remoteLevel: 'Hybrid', matchedSearches: [], interest: 'None', relevanceScore: 80, rankScore: 75, relevanceLevel: 'Relevant',
    relevanceReasons: [], detailsFetchedAt: null, alsoFoundAt: [], applied: null, requiredGerman: 'Unknown', unreachable: false,
    homeOfficePercent: null, descriptionIsManual: false, goldLabel: null, ...overrides,
  }
}

describe('quick label mode on the list (07c)', () => {
  it('labels the focused card with p / n and shows automatic labels as such', async () => {
    api.list.mockResolvedValue({
      items: [vacancy('a'), vacancy('b', { goldLabel: false, goldLabelAutomatic: true })],
      page: 1, pageSize: 20, totalCount: 2, totalMatching: 2, top: null,
      facets: { source: {}, remoteLevel: {}, employerType: {}, relevanceLevel: {}, requiredGerman: {} },
    })
    const wrapper = await mountSuspended(IndexPage, { route: '/?minScore=0' })
    await flushPromises()
    expect(wrapper.find('#card-b').text()).toContain('Passt nicht · automatisch')

    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'j' }))
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'p' }))
    await flushPromises()

    expect(api.setLabel).toHaveBeenLastCalledWith('a', true)
    expect(wrapper.find('#card-a').text()).toContain('Passt')
    expect(api.labelSummary).toHaveBeenCalled()

    // n on the automatic one makes it mine (not removed).
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'j' }))
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'n' }))
    await flushPromises()
    expect(api.setLabel).toHaveBeenLastCalledWith('b', false)
    expect(wrapper.find('#card-b').text()).not.toContain('automatisch')
    wrapper.unmount()
  })
})
