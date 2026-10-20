import { describe, expect, it, vi } from 'vitest'
import { mockNuxtImport, mountSuspended } from '@nuxt/test-utils/runtime'
import { flushPromises } from '@vue/test-utils'
import IndexPage from '~/pages/index.vue'

const api = vi.hoisted(() => ({
  list: vi.fn(),
  searches: vi.fn().mockResolvedValue([]),
  uiSettings: vi.fn().mockResolvedValue({ highlightKeywords: [], scoreHigh: 70, scoreMedium: 45 }),
  searchProfile: vi.fn().mockResolvedValue({ version: 1, updatedAt: null, searches: [], profile: { minScore: 40, topN: 20, germanFilter: [] } }),
  recentImports: vi.fn().mockResolvedValue([]),
  markSeen: vi.fn(),
  setSeen: vi.fn(),
  setInterest: vi.fn(),
}))
mockNuxtImport('useVacanciesApi', () => () => api)

const emptyPage = {
  items: [],
  page: 1,
  pageSize: 20,
  totalCount: 0,
  totalMatching: 258,
  top: 20,
  facets: { source: {}, remoteLevel: {}, employerType: {}, relevanceLevel: {}, requiredGerman: {} },
}

describe('list ↔ URL', () => {
  it('reads Mindestbewertung, Top N and Deutsch from the URL, sends them and writes changes back', async () => {
    api.list.mockResolvedValue(emptyPage)
    const wrapper = await mountSuspended(IndexPage, { route: '/?minScore=60&top=20&german=B2,B1OrLower' })
    await flushPromises()

    expect(api.list).toHaveBeenLastCalledWith(expect.objectContaining({ minScore: 60, top: 20, german: ['B2', 'B1OrLower'] }))
    expect((wrapper.find('input[type="range"]').element as HTMLInputElement).value).toBe('60')
    const top = wrapper.findAll('input[type="checkbox"]').find(i => i.element.parentElement?.textContent?.includes('Nur Top 20'))!
    expect((top.element as HTMLInputElement).checked).toBe(true)
    expect(wrapper.text()).toContain('Top 20 von 258')

    const slider = wrapper.find('input[type="range"]')
    await slider.setValue('75')
    await slider.trigger('change')
    await flushPromises()

    const router = useRouter()
    await vi.waitFor(() => expect(router.currentRoute.value.query.minScore).toBe('75'))
    expect(router.currentRoute.value.query.top).toBe('20')
    expect(router.currentRoute.value.query.german).toBe('B2,B1OrLower')
    wrapper.unmount()
  })
})
