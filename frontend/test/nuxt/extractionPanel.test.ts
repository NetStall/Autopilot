import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mockNuxtImport, mountSuspended } from '@nuxt/test-utils/runtime'
import { flushPromises } from '@vue/test-utils'
import ExtractionPanel from '~/components/ExtractionPanel.vue'
import type { ExtractionStatus } from '~/types/extraction'

const api = vi.hoisted(() => ({
  extractionStatus: vi.fn(),
  pendingExtractions: vi.fn(),
  startExtraction: vi.fn(),
  cancelExtraction: vi.fn(),
  failedExtractions: vi.fn(),
  extractVacancy: vi.fn(),
}))
mockNuxtImport('useVacanciesApi', () => () => api)

const idle: ExtractionStatus = {
  state: 'Idle', available: true, model: 'qwen3:8b', total: 171, done: 171, extracted: 164, cached: 1, failed: 6,
  currentVacancyId: null, queued: [], secondsPerVacancy: 3.6, tokensPerSecond: 104, lastSecondsPerVacancy: 3.6,
  startedAt: null, finishedAt: null, lastError: null,
}

beforeEach(() => {
  vi.clearAllMocks()
  api.extractionStatus.mockResolvedValue(idle)
  api.pendingExtractions.mockResolvedValue({ top: 100, pending: 100, secondsPerVacancy: 3.6, estimatedSeconds: 360 })
  api.startExtraction.mockResolvedValue({ ...idle, state: 'Queued' })
  api.failedExtractions.mockResolvedValue([
    { vacancyId: 'v1', title: 'Backend Dev', company: 'Firma', rankScore: 70, error: 'Zeitüberschreitung: …', failedAt: '2026-10-21T10:00:00Z' },
  ])
  api.extractVacancy.mockResolvedValue({ ...idle, state: 'Queued' })
})

describe('ExtractionPanel', () => {
  it('estimates the run for Top 100 and starts it with that N', async () => {
    const wrapper = await mountSuspended(ExtractionPanel)
    await flushPromises()

    expect(api.pendingExtractions).toHaveBeenCalledWith(100)
    expect(wrapper.find('[data-test="estimate"]').text()).toBe('100 offen · ≈ 6 min')
    await wrapper.find('#extraction-top').setValue('50')
    await flushPromises()
    expect(api.pendingExtractions).toHaveBeenLastCalledWith(50)

    await wrapper.findAll('button').find(b => b.text() === 'Alle analysieren')!.trigger('click')
    await flushPromises()
    expect(api.startExtraction).toHaveBeenCalledWith(50)
  })

  it('lists the invalid ones with the reason and retries one', async () => {
    const wrapper = await mountSuspended(ExtractionPanel)
    await flushPromises()

    await wrapper.find('.extraction__link').trigger('click')
    await flushPromises()
    const list = wrapper.find('ul[aria-label="Ungültige Analysen"]')
    expect(list.text()).toContain('Backend Dev')
    expect(list.text()).toContain('Zeitüberschreitung')

    await list.find('button').trigger('click')
    await flushPromises()
    expect(api.extractVacancy).toHaveBeenCalledWith('v1', true)
    expect(wrapper.find('ul[aria-label="Ungültige Analysen"]').text()).toContain('Keine')
  })
})
