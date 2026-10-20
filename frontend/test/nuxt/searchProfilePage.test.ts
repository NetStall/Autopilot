import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mockNuxtImport, mountSuspended } from '@nuxt/test-utils/runtime'
import { flushPromises } from '@vue/test-utils'
import type { SearchProfile } from '~/types/searchProfile'
import SearchProfilePage from '~/pages/suchprofil.vue'

const api = vi.hoisted(() => ({
  searchProfile: vi.fn(),
  searchProfileHistory: vi.fn(),
  previewSearchProfile: vi.fn(),
  saveSearchProfile: vi.fn(),
  rescoreStatus: vi.fn(),
  startImport: vi.fn(),
  searchProfileExportUrl: vi.fn(() => 'http://api.test/api/search-profile/export'),
}))
mockNuxtImport('useVacanciesApi', () => () => api)

function profile(): SearchProfile {
  return {
    searchTerms: [{ text: 'Softwareentwickler', scope: 'Both' }],
    places: [{ city: 'Deggendorf', radiusKm: 70 }],
    additionalPlaces: ['Straubing'],
    remoteGermany: true,
    remotePreference: 'AtLeastHybrid',
    mustHave: [],
    niceToHave: [],
    exclude: [],
    titleExclusions: ['Praktikum'],
    agency: 'Allow',
    temporary: 'RankLower',
    publishedWithinDays: 30,
    sources: { arbeitsagentur: true, himalayas: true },
    minScore: 40,
    topN: 20,
    germanFilter: [],
    hideUnreachable: true,
    penalties: { germanPerLevel: 8, experience: 8, experienceStrong: 10, mustHaveGap: 15, requiredGap: 6, likelyRequiredGap: 10, niceToHaveGap: 4, mustHaveGapMax: 30, requiredGapMax: 18, outsideRegion: 15, unreachable: 35, employerType: 10, remotePreference: 10 },
    salaryMin: null,
    salaryMax: null,
    earliestStart: null,
    maxCommuteMinutes: null,
  }
}

const preview = { errors: [], matching: 142, fullRemote: 31, currentMatching: 120, scored: 300, searches: [] }

const mounted: { unmount: () => void }[] = []

async function mountPage() {
  const wrapper = await mountSuspended(SearchProfilePage, { route: '/suchprofil' })
  mounted.push(wrapper)
  await flushPromises()
  return wrapper
}

async function waitForPreview() {
  await vi.advanceTimersByTimeAsync(700)
  await flushPromises()
}

describe('Suchprofil page', () => {
  // Each page registers a beforeunload listener; an old dirty page must not answer for the next test.
  afterEach(() => {
    mounted.splice(0).forEach(w => w.unmount())
    vi.useRealTimers()
  })

  beforeEach(() => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    vi.clearAllMocks()
    api.searchProfile.mockResolvedValue({ version: 3, updatedAt: '2026-10-20T07:00:00Z', profile: profile(), searches: [] })
    api.searchProfileHistory.mockResolvedValue([])
    api.previewSearchProfile.mockResolvedValue(preview)
  })

  it('adds and removes tags in a tag input and shows the live preview', async () => {
    const wrapper = await mountPage()
    expect(wrapper.text()).toContain('Keine ungespeicherten Änderungen.')

    const input = wrapper.find('#additional-places')
    await input.setValue('Plattling, Passau')
    await input.trigger('keydown', { key: 'Enter' })
    expect(wrapper.find('ul[aria-label="Weitere Orte in meiner Region"]').text()).toContain('Plattling')
    expect(wrapper.find('ul[aria-label="Weitere Orte in meiner Region"]').text()).toContain('Passau')

    await wrapper.find('button[aria-label="Straubing entfernen"]').trigger('click')
    expect(wrapper.find('button[aria-label="Straubing entfernen"]').exists()).toBe(false)

    await waitForPreview()
    expect(api.previewSearchProfile).toHaveBeenCalledWith(expect.objectContaining({ additionalPlaces: ['Plattling', 'Passau'] }))
    expect(wrapper.text()).toContain('Mit diesen Einstellungen: 142 passende Stellen (davon 31 voll remote) – 22 mehr als bisher')
  })

  it('sends „Nicht erreichbare Stellen ausblenden" with the preview', async () => {
    const wrapper = await mountPage()
    const checkbox = wrapper.find('input[type="checkbox"][data-test="hide-unreachable"]')
    expect((checkbox.element as HTMLInputElement).checked).toBe(true)

    await checkbox.setValue(false)
    await waitForPreview()
    expect(api.previewSearchProfile).toHaveBeenCalledWith(expect.objectContaining({ hideUnreachable: false }))
  })

  it('shows validation messages from the preview and blocks saving', async () => {
    api.previewSearchProfile.mockResolvedValue({ ...preview, errors: ['Suchbegriffe: mindestens ein Suchbegriff ist nötig.'] })
    const wrapper = await mountPage()

    await wrapper.find('button[aria-label="Suchbegriff Softwareentwickler entfernen"]').trigger('click')
    await waitForPreview()

    expect(wrapper.find('ul[aria-label="Fehler im Suchprofil"]').text()).toContain('mindestens ein Suchbegriff')
    expect(wrapper.findAll('button').find(b => b.text() === 'Speichern')!.attributes('disabled')).toBeDefined()
  })

  it('warns before leaving with unsaved changes', async () => {
    const wrapper = await mountPage()
    const clean = new Event('beforeunload', { cancelable: true })
    window.dispatchEvent(clean)
    expect(clean.defaultPrevented).toBe(false)

    await wrapper.find('#min-score').setValue('60')
    const dirty = new Event('beforeunload', { cancelable: true })
    window.dispatchEvent(dirty)

    expect(dirty.defaultPrevented).toBe(true)
    expect(wrapper.text()).not.toContain('Keine ungespeicherten Änderungen.')
  })

  it('offers the export as a download link', async () => {
    const wrapper = await mountPage()

    const link = wrapper.findAll('a').find(a => a.text() === 'Als JSON exportieren')!
    expect(link.attributes('href')).toBe('http://api.test/api/search-profile/export')
    expect(link.attributes('download')).toBeDefined()
  })

  it('imports a file only after the server accepted it, as unsaved changes', async () => {
    const wrapper = await mountPage()
    const imported = { ...profile(), minScore: 55, titleExclusions: ['Werkstudent'] }

    const input = wrapper.find('#import-file')
    Object.defineProperty(input.element, 'files', { value: [new File([JSON.stringify(imported)], 'suchprofil-v7.json', { type: 'application/json' })] })
    await input.trigger('change')
    await flushPromises()

    expect(api.previewSearchProfile).toHaveBeenCalledWith(expect.objectContaining({ minScore: 55 }))
    expect(wrapper.text()).toContain('„suchprofil-v7.json" geladen – noch nicht gespeichert.')
    expect((wrapper.find('#min-score').element as HTMLInputElement).value).toBe('55')
    expect(api.saveSearchProfile).not.toHaveBeenCalled()
  })

  it('rejects a file that is no Suchprofil', async () => {
    const wrapper = await mountPage()

    const input = wrapper.find('#import-file')
    Object.defineProperty(input.element, 'files', { value: [new File(['{"name":"x"}'], 'other.json')] })
    await input.trigger('change')
    await flushPromises()

    expect(wrapper.text()).toContain('Die Datei enthält kein Suchprofil')
    expect((wrapper.find('#min-score').element as HTMLInputElement).value).toBe('40')
  })
})
