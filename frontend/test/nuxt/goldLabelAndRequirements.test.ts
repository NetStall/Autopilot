import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mockNuxtImport, mountSuspended } from '@nuxt/test-utils/runtime'
import { flushPromises } from '@vue/test-utils'
import GoldLabelButtons from '~/components/GoldLabelButtons.vue'
import VacancyRequirements from '~/components/VacancyRequirements.vue'
import type { ExtractionStatus, VacancyExtraction } from '~/types/extraction'

const api = vi.hoisted(() => ({
  setLabel: vi.fn(),
  labelSummary: vi.fn(),
  extractionStatus: vi.fn(),
  extractVacancy: vi.fn(),
}))
mockNuxtImport('useVacanciesApi', () => () => api)

const idle: ExtractionStatus = {
  state: 'Idle', available: true, model: 'qwen3:8b', total: 0, done: 0, extracted: 0, cached: 0, failed: 0,
  currentVacancyId: null, queued: [], secondsPerVacancy: null, tokensPerSecond: null, lastSecondsPerVacancy: null, startedAt: null, finishedAt: null, lastError: null,
}

const extraction: VacancyExtraction = {
  model: 'qwen3:8b',
  promptVersion: 'extraction.v1',
  extractedAt: '2026-10-21T08:00:00Z',
  result: {
    mustHave: ['C#', 'DevExpress'], niceToHave: ['Docker'], germanLevel: 'B2', germanNotRequired: false, englishLevel: null,
    minYears: 3, seniority: 'Mid', remoteLevel: 'Hybrid', homeOfficePercent: 40, employmentType: 'FullTime',
    domainRequirements: ['Bauwesen'], summary: 'Full-Stack mit Blazor. Gefragt sind C# und DevExpress.',
  },
  chips: {
    mustHave: [{ name: 'C#', sourceText: 'C#' }, { name: 'DevExpress', sourceText: 'DevExpress (Pflicht)' }],
    niceToHave: [{ name: 'Docker', sourceText: 'Docker' }],
    domains: [{ name: 'Bauwesen', sourceText: 'Bauwesen' }],
    softSkills: ['Teamgeist'],
  },
}

beforeEach(() => {
  vi.clearAllMocks()
  api.setLabel.mockResolvedValue(undefined)
  api.labelSummary.mockResolvedValue({ labeled: 1, fits: 1, notFits: 0, target: 30 })
  api.extractionStatus.mockResolvedValue(idle)
  api.extractVacancy.mockResolvedValue({ ...idle, state: 'Queued', queued: ['v1'] })
})

describe('GoldLabelButtons', () => {
  it('stores Passt / Passt nicht by click and by p / n, and removes it on the second press', async () => {
    const wrapper = await mountSuspended(GoldLabelButtons, { props: { vacancyId: 'v1', modelValue: null }, attachTo: document.body })

    await wrapper.findAll('button')[0]!.trigger('click')
    await flushPromises()
    expect(api.setLabel).toHaveBeenLastCalledWith('v1', true)
    expect(wrapper.emitted('update:modelValue')!.at(-1)).toEqual([true])
    expect(api.labelSummary).toHaveBeenCalled()

    await wrapper.setProps({ modelValue: true })
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'n' }))
    await flushPromises()
    expect(api.setLabel).toHaveBeenLastCalledWith('v1', false)

    await wrapper.setProps({ modelValue: false })
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'n' }))
    await flushPromises()
    expect(api.setLabel).toHaveBeenLastCalledWith('v1', null)
    expect(wrapper.findAll('button')[1]!.attributes('aria-pressed')).toBeDefined()
    wrapper.unmount()
  })

  it('ignores the keys while typing and rolls back when saving fails', async () => {
    const wrapper = await mountSuspended(GoldLabelButtons, { props: { vacancyId: 'v1', modelValue: null }, attachTo: document.body })
    const input = document.createElement('input')
    document.body.appendChild(input)

    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'p', bubbles: true }))
    await flushPromises()
    expect(api.setLabel).not.toHaveBeenCalled()

    api.setLabel.mockRejectedValueOnce(new Error('offline'))
    await wrapper.findAll('button')[0]!.trigger('click')
    await flushPromises()
    expect(wrapper.emitted('update:modelValue')!.at(-1)).toEqual([null])
    expect(wrapper.find('[role="alert"]').text()).toContain('nicht gespeichert')
    input.remove()
    wrapper.unmount()
  })
})

describe('VacancyRequirements', () => {
  it('shows the source and the extracted lists', async () => {
    const wrapper = await mountSuspended(VacancyRequirements, {
      props: { vacancyId: 'v1', extraction, source: 'Ai', hasDescription: true },
    })
    await flushPromises()

    expect(wrapper.find('.req__badge').text()).toBe('KI')
    expect(wrapper.text()).toContain('qwen3:8b · extraction.v1')
    expect(wrapper.findAll('.req__chip').map(c => c.text())).toEqual(['C#', 'DevExpress', 'Docker', 'Bauwesen', 'Teamgeist'])
    expect(wrapper.findAll('.req__chip')[1]!.attributes('title')).toBe('DevExpress (Pflicht)')
    expect(wrapper.find('.req__chip--soft').text()).toBe('Teamgeist')
    expect(wrapper.text()).toContain('Bauwesen')
    expect(wrapper.text()).toContain('mind. 3 Jahre · Mid-Level')
    expect(wrapper.text()).toContain('Erneut mit KI analysieren')
  })

  it('says Regeln without an extraction and queues „Mit KI analysieren"', async () => {
    const wrapper = await mountSuspended(VacancyRequirements, {
      props: { vacancyId: 'v1', extraction: null, source: 'Rules', hasDescription: true },
    })
    await flushPromises()

    expect(wrapper.find('.req__badge').text()).toBe('Regeln')
    await wrapper.find('button').trigger('click')
    await flushPromises()
    expect(api.extractVacancy).toHaveBeenCalledWith('v1')
    expect(wrapper.find('button').text()).toBe('KI analysiert …')
  })

  it('hides the button when no model is set up', async () => {
    api.extractionStatus.mockResolvedValue({ ...idle, available: false })
    const wrapper = await mountSuspended(VacancyRequirements, {
      props: { vacancyId: 'v2', extraction: null, source: 'Rules', hasDescription: true },
    })
    await flushPromises()

    expect(wrapper.find('button').exists()).toBe(false)
  })
})
